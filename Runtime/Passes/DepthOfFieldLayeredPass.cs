using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldPass : IDisposable
    {
        static void GatherLayer(ComputeCommandBuffer cmd, in Resources data, bool near, TextureHandle input, TextureHandle output, TextureHandle dilated)
        {
            var s = data.settings;
            var cs = data.shaders.layeredGather;
            Configure(cmd, data, cs);
            int k = near ? data.shaders.layeredGatherNearKernel : data.shaders.layeredGatherFarKernel;
            float radius = near ? s.lens.z : s.lens.w;
            float maxMip = Mathf.Min(s.downsample == 1 ? 4 : s.downsample == 2 ? 3 : 2, Mathf.FloorToInt(Mathf.Log(Mathf.Max(s.blurWidth, s.blurHeight), 2)));
            cmd.SetComputeVectorParam(cs, "_Params1", new Vector4(near ? s.counts.x : s.counts.y, radius / s.downsample, s.optics.y, radius));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(maxMip, 0, 0, 0));
            cmd.SetComputeVectorParam(cs, "_TexelSize", Size(s.blurWidth, s.blurHeight));
            Texture(cmd, cs, k, "_InputTexture", input);
            Texture(cmd, cs, k, "_OutputTexture", output);
            Texture(cmd, cs, k, "_InputCoCTexture", (near ? data.nearCoC : data.farCoC));
            if (near)
            {
                Texture(cmd, cs, k, "_InputDilatedCoCTexture", dilated);
                Texture(cmd, cs, k, "_OutputAlphaTexture", data.nearAlpha);
            }

            cmd.SetComputeBufferParam(cs, k, "_BokehKernel", (near ? data.nearSamples : data.farSamples));
            if (!data.array)
            {
                Clear(cmd, data, output);
                if (near)
                    Clear(cmd, data, data.nearAlpha, true);
                cmd.SetComputeVectorParam(cs, "_DofDispatchSize", Size(s.blurWidth, s.blurHeight));
                cmd.SetComputeBufferParam(cs, k, "_TileList", (near ? data.nearTiles : data.farTiles));
                cmd.DispatchCompute(cs, k, data.indirectDispatch, near ? 0u : 12u);
            }
            else
                Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, data.views);
        }

        static void RenderLayeredAperture(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Aperture Samples", out var data);
            data.resources = r;
            if (r.settings.lens.z > 0)
                builder.UseBuffer(r.nearSamples, AccessFlags.Write);
            if (r.settings.lens.w > 0)
                builder.UseBuffer(r.farSamples, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredAperture(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredAperture(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = data.shaders.apertureSamples;
            Configure(cmd, data, cs);
            int k = data.shaders.apertureSamplesKernel;
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.optics.x, 0, 0, 0));
            for (int layer = 0; layer < 2; ++layer)
            {
                if (layer == 0 ? !near : !far)
                    continue;
                int count = (int)(layer == 0 ? s.counts.x : s.counts.y);
                cmd.SetComputeVectorParam(cs, "_Params1", new Vector4(count, 1 - s.apertureShape.y, s.apertureShape.x, s.apertureShape.z * Mathf.Deg2Rad));
                cmd.SetComputeBufferParam(cs, k, "_BokehKernel", (layer == 0 ? data.nearSamples : data.farSamples));
                cmd.DispatchCompute(cs, k, (count * count + 63) / 64, 1, 1);
            }
        }

        static void RenderLayeredPrefilter(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Prefilter", out var data);
            data.resources = r;
            builder.UseTexture(r.input, AccessFlags.Read);
            builder.UseTexture(r.stableCoC, AccessFlags.Read);
            if (r.settings.lens.z > 0)
            {
                builder.UseTexture(r.near0, AccessFlags.Write);
                builder.UseTexture(r.nearCoC, AccessFlags.Write);
            }

            if (r.settings.lens.w > 0)
            {
                builder.UseTexture(r.far0, AccessFlags.Write);
                builder.UseTexture(r.farCoC, AccessFlags.Write);
            }

            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredPrefilter(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredPrefilter(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = data.shaders.prefilter;
            Configure(cmd, data, cs);
            int k = data.shaders.prefilterKernel;
            Texture(cmd, cs, k, "_InputTexture", data.input);
            Texture(cmd, cs, k, "_InputCoCTexture", data.stableCoC);
            cmd.SetComputeVectorParam(cs, "_CoCTargetScale", Vector4.one);
            if (near)
            {
                Texture(cmd, cs, k, "_OutputNearTexture", data.near0);
                Texture(cmd, cs, k, "_OutputNearCoCTexture", data.nearCoC);
            }

            if (far)
            {
                Texture(cmd, cs, k, "_OutputFarTexture", data.far0);
                Texture(cmd, cs, k, "_OutputFarCoCTexture", data.farCoC);
            }

            Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, data.views);
        }

        static void RenderLayeredPyramids(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Far Mips", out var data);
            data.resources = r;
            builder.UseTexture(r.mipColor0, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor1, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor2, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipColor3, AccessFlags.ReadWrite);
            builder.UseTexture(r.far0, AccessFlags.ReadWrite);
            builder.UseTexture(r.farCoC, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipScalar0, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipScalar1, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipScalar2, AccessFlags.ReadWrite);
            builder.UseTexture(r.mipScalar3, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredPyramids(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredPyramids(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            Pyramid(cmd, data, data.far0, s.blurWidth, s.blurHeight, false);
            Pyramid(cmd, data, data.farCoC, s.blurWidth, s.blurHeight, true);
        }

        static void RenderLayeredDilation(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Near Dilation", out var data);
            data.resources = r;
            builder.UseTexture(r.nearCoC, AccessFlags.Read);
            builder.UseTexture(r.dilated0, AccessFlags.ReadWrite);
            builder.UseTexture(r.dilated1, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredDilation(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredDilation(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            TextureHandle dilated = data.nearCoC;
            if (near)
            {
                var cs = data.shaders.nearDilation;
                Configure(cmd, data, cs);
                int k = data.shaders.nearDilationKernel;
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.blurWidth - 1, s.blurHeight - 1, 0, 0));
                int passes = Mathf.Max(1, Mathf.CeilToInt((s.lens.z / s.downsample + 2) / 4));
                for (int i = 0; i < passes; ++i)
                {
                    TextureHandle destination = ((i & 1) == 0 ? data.dilated0 : data.dilated1);
                    Texture(cmd, cs, k, "_InputCoCTexture", dilated);
                    Texture(cmd, cs, k, "_OutputCoCTexture", destination);
                    Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, data.views);
                    dilated = destination;
                }
            }
        }

        static void RenderLayeredTiles(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Tile Classification", out var data);
            data.resources = r;
            builder.UseBuffer(r.indirectDispatch, AccessFlags.Write);
            builder.UseBuffer(r.nearTiles, AccessFlags.Write);
            builder.UseBuffer(r.farTiles, AccessFlags.Write);
            if (r.settings.lens.z > 0)
                builder.UseTexture(DilatedTexture(r), AccessFlags.Read);
            if (r.settings.lens.w > 0)
                builder.UseTexture(r.farCoC, AccessFlags.Read);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredTiles(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredTiles(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            TextureHandle dilated = DilatedTexture(data);
            if (!data.array)
            {
                cmd.SetBufferCounterValue(data.nearTiles, 0);
                cmd.SetBufferCounterValue(data.farTiles, 0);
                var cs = data.shaders.clearIndirect;
                Configure(cmd, data, cs);
                int k = data.shaders.clearIndirectKernel;
                cmd.SetComputeBufferParam(cs, k, "_IndirectBuffer", data.indirectDispatch);
                cmd.DispatchCompute(cs, k, 1, 1, 1);
                cs = data.shaders.tileClassification;
                Configure(cmd, data, cs);
                k = data.shaders.tileClassificationKernel;
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.blurWidth - 1, s.blurHeight - 1, 0, 0));
                cmd.SetComputeBufferParam(cs, k, "_IndirectBuffer", data.indirectDispatch);
                if (near)
                {
                    Texture(cmd, cs, k, "_InputNearCoCTexture", dilated);
                    cmd.SetComputeBufferParam(cs, k, "_NearTileList", data.nearTiles);
                }

                if (far)
                {
                    Texture(cmd, cs, k, "_InputFarCoCTexture", data.farCoC);
                    cmd.SetComputeBufferParam(cs, k, "_FarTileList", data.farTiles);
                }

                Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, 1);
            }
        }

        static void RenderLayeredFarGather(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Far Gather", out var data);
            data.resources = r;
            builder.UseTexture(r.far0, AccessFlags.Read);
            builder.UseTexture(r.farCoC, AccessFlags.Read);
            builder.UseTexture(r.far1, AccessFlags.Write);
            builder.UseBuffer(r.farSamples, AccessFlags.Read);
            if (!r.array)
            {
                builder.UseBuffer(r.indirectDispatch, AccessFlags.Read);
                builder.UseBuffer(r.farTiles, AccessFlags.Read);
            }

            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredFarGather(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredFarGather(ComputeCommandBuffer cmd, in Resources data)
        {
            GatherLayer(cmd, data, false, data.far0, data.far1, default);
        }

        static void RenderLayeredPreCombine(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Precombine Far", out var data);
            data.resources = r;
            builder.UseTexture(r.near0, AccessFlags.Read);
            builder.UseTexture(r.far1, AccessFlags.Read);
            builder.UseTexture(r.farCoC, AccessFlags.Read);
            builder.UseTexture(r.near1, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredPreCombine(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredPreCombine(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = data.shaders.preCombine;
            Configure(cmd, data, cs);
            int k = data.shaders.preCombineKernel;
            Texture(cmd, cs, k, "_InputTexture", data.near0);
            Texture(cmd, cs, k, "_InputFarTexture", data.far1);
            Texture(cmd, cs, k, "_InputCoCTexture", data.farCoC);
            Texture(cmd, cs, k, "_OutputTexture", data.near1);
            Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, data.views);
        }

        static void RenderLayeredNearGather(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Near Gather", out var data);
            data.resources = r;
            builder.UseTexture(r.nearAlpha, AccessFlags.Write);
            builder.UseBuffer(r.nearSamples, AccessFlags.Read);
            builder.UseTexture(r.settings.lens.w > 0 ? r.near1 : r.near0, AccessFlags.Read);
            builder.UseTexture(NearOutputTexture(r), AccessFlags.Write);
            builder.UseTexture(r.nearCoC, AccessFlags.Read);
            builder.UseTexture(DilatedTexture(r), AccessFlags.Read);
            if (!r.array)
            {
                builder.UseBuffer(r.indirectDispatch, AccessFlags.Read);
                builder.UseBuffer(r.nearTiles, AccessFlags.Read);
            }

            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredNearGather(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredNearGather(ComputeCommandBuffer cmd, in Resources data)
        {
            TextureHandle source = data.settings.lens.w > 0 ? data.near1 : data.near0;
            GatherLayer(cmd, data, true, source, NearOutputTexture(data), DilatedTexture(data));
        }

        static void RenderLayeredCombine(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Layer Combine", out var data);
            data.resources = r;
            builder.UseTexture(r.input, AccessFlags.Read);
            builder.UseTexture(r.spatial, AccessFlags.Write);
            if (r.settings.lens.z > 0)
            {
                builder.UseTexture(NearOutputTexture(r), AccessFlags.Read);
                builder.UseTexture(r.nearAlpha, AccessFlags.Read);
            }

            if (r.settings.lens.w > 0)
            {
                builder.UseTexture(r.far1, AccessFlags.Read);
                builder.UseTexture(r.stableCoC, AccessFlags.Read);
            }

            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteLayeredCombine(context.cmd, pass.resources);
            });
        }

        static void ExecuteLayeredCombine(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = data.shaders.layeredCombine;
            Configure(cmd, data, cs);
            int k = data.shaders.layeredCombineKernel;
            Texture(cmd, cs, k, "_InputTexture", data.input);
            Texture(cmd, cs, k, "_OutputTexture", data.spatial);
            if (near)
            {
                Texture(cmd, cs, k, "_InputNearTexture", NearOutputTexture(data));
                Texture(cmd, cs, k, "_InputNearAlphaTexture", data.nearAlpha);
            }

            if (far)
            {
                Texture(cmd, cs, k, "_InputFarTexture", data.far1);
                Texture(cmd, cs, k, "_InputCoCTexture", data.stableCoC);
            }

            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }
    }
}
