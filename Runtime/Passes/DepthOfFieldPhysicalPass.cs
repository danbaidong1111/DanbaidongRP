using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldPass : IDisposable
    {
        static void BindPhysical(ComputeCommandBuffer cmd, in Resources data, ComputeShader cs, int k, TextureHandle source, TextureHandle tiles, TextureHandle output)
        {
            Texture(cmd, cs, k, "_InputTexture", source);
            Texture(cmd, cs, k, "_InputCoCTexture", data.stableCoC);
            Texture(cmd, cs, k, "_TileList", tiles);
            Texture(cmd, cs, k, "_OutputTexture", output);
            cmd.SetComputeBufferParam(cs, k, "_ApertureShapeTable", data.apertureShape);
            cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", 256);
        }

        static void RenderPhysicalMinMax(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF CoC Min Max", out var data);
            data.resources = r;
            builder.UseTexture(r.stableCoC, AccessFlags.Read);
            builder.UseTexture(r.tile0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalMinMax(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalMinMax(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = data.shaders.minMax;
            Configure(cmd, data, cs);
            int k = data.shaders.minMaxKernel;
            Texture(cmd, cs, k, "_InputTexture", data.stableCoC);
            Texture(cmd, cs, k, "_OutputTexture", data.tile0);
            cmd.SetComputeVectorParam(cs, "_OutputResolution", new Vector4(s.tileWidth, s.tileHeight, 0, 0));
            Dispatch(cmd, cs, k, s.tileWidth, s.tileHeight, data.views);
        }

        static void RenderPhysicalDilation(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Tile Dilation", out var data);
            data.resources = r;
            builder.UseTexture(r.tile0, AccessFlags.ReadWrite);
            builder.UseTexture(r.tile1, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalDilation(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalDilation(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            TextureHandle tiles = data.tile0, tileOutput = data.tile1;
            var cs = data.shaders.tileDilation;
            Configure(cmd, data, cs);
            int k = data.shaders.tileDilationKernel;
            int iterations = Mathf.Max(1, Mathf.CeilToInt(s.lens.z / 8));
            for (int i = 0; i < iterations; ++i)
            {
                Texture(cmd, cs, k, "_InputTexture", tiles);
                Texture(cmd, cs, k, "_OutputTexture", tileOutput);
                Dispatch(cmd, cs, k, s.tileWidth, s.tileHeight, data.views);
                CoreUtils.Swap(ref tiles, ref tileOutput);
            }
        }

        static void RenderPhysicalAperture(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Aperture Shape", out var data);
            data.resources = r;
            builder.UseBuffer(r.apertureShape, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalAperture(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalAperture(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = data.shaders.apertureShape;
            Configure(cmd, data, cs);
            int k = data.shaders.apertureShapeKernel;
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.apertureShape.x, 1 - s.apertureShape.y, s.apertureShape.z * Mathf.Deg2Rad, s.optics.x));
            cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", 256);
            cmd.SetComputeBufferParam(cs, k, "_ApertureShapeTable", data.apertureShape);
            cmd.DispatchCompute(cs, k, 4, 1, 1);
        }

        static void RenderPhysicalSlowGather(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Full Resolution Gather", out var data);
            data.resources = r;
            builder.UseTexture(r.input, AccessFlags.Read);
            builder.UseTexture(r.stableCoC, AccessFlags.Read);
            builder.UseTexture(r.spatial, AccessFlags.Write);
            builder.UseBuffer(r.apertureShape, AccessFlags.Read);
            builder.UseTexture(PhysicalTileTexture(r), AccessFlags.Read);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalSlowGather(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalSlowGather(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            TextureHandle tiles = PhysicalTileTexture(data);
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w);
            float rings = Mathf.Max(s.counts.x, s.counts.y);
            var cs = data.shaders.slowGather;
            Configure(cmd, data, cs);
            int k = data.shaders.slowGatherKernel;
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.adaptive.x, s.adaptive.y, s.downsample, 1f / s.downsample));
            BindPhysical(cmd, data, cs, k, data.input, tiles, data.spatial);
            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }

        static void RenderPhysicalPyramid(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Color Pyramid", out var data);
            data.resources = r;
            builder.UseTexture(r.spatial, AccessFlags.Read);
            builder.UseTexture(r.mipColor0, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor1, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor2, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor3, AccessFlags.ReadWrite);
            builder.UseTexture(r.pyramid, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalPyramid(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalPyramid(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            Copy(cmd, data, data.spatial, data.pyramid, s.width, s.height);
            Pyramid(cmd, data, data.pyramid, s.width, s.height, false);
        }

        static void RenderPhysicalGather(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Adaptive Gather", out var data);
            data.resources = r;
            builder.UseTexture(r.stableCoC, AccessFlags.Read);
            builder.UseTexture(r.low, AccessFlags.Write);
            builder.UseBuffer(r.apertureShape, AccessFlags.Read);
            builder.UseTexture(r.settings.downsample == 1 ? r.input : r.pyramid, AccessFlags.Read);
            builder.UseTexture(PhysicalTileTexture(r), AccessFlags.Read);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalGather(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalGather(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            TextureHandle tiles = PhysicalTileTexture(data);
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w), rings = Mathf.Max(s.counts.x, s.counts.y);
            TextureHandle source = (s.downsample == 1 ? data.input : data.pyramid);
            var cs = data.shaders.physicalGather;
            Configure(cmd, data, cs);
            int k = data.shaders.physicalGatherKernel;
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(1 + Mathf.Ceil(Mathf.Log(Mathf.Max(maxRadius, 1), 2)), 3, 1f / s.downsample, s.downsample));
            cmd.SetComputeVectorParam(cs, "_Params3", s.adaptive);
            BindPhysical(cmd, data, cs, k, source, tiles, data.low);
            Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, data.views);
        }

        static void RenderPhysicalCombine(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Upsample", out var data);
            data.resources = r;
            builder.UseTexture(r.low, AccessFlags.Read);
            builder.UseTexture(PhysicalTileTexture(r), AccessFlags.Read);
            builder.UseTexture(r.spatial, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePhysicalCombine(context.cmd, pass.resources);
            });
        }

        static void ExecutePhysicalCombine(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            TextureHandle tiles = PhysicalTileTexture(data);
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w), rings = Mathf.Max(s.counts.x, s.counts.y);
            var cs = data.shaders.physicalCombine;
            Configure(cmd, data, cs);
            int k = data.shaders.physicalCombineKernel;
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.adaptive.x, s.adaptive.y, s.downsample, 1f / s.downsample));
            Texture(cmd, cs, k, "_InputNearTexture", data.low);
            Texture(cmd, cs, k, "_TileList", tiles);
            Texture(cmd, cs, k, "_OutputTexture", data.spatial);
            cmd.SetComputeIntParam(cs, "_DebugTileClassification", s.debugTiles ? 1 : 0);
            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }
    }
}
