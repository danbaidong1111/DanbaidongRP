using System;
using UnityEngine.Experimental.Rendering;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldCompatibility
    {
        internal static Vector4 Size(int width, int height) => new Vector4(width, height, 1f / width, 1f / height);
        internal static void Keyword(CommandBuffer cmd, ComputeShader shader, string keyword, bool enabled)
        {
            var local = shader.keywordSpace.FindKeyword(keyword);
            if (local.isValid) cmd.SetKeyword(shader, local, enabled);
        }
        internal static void Texture(CommandBuffer cmd, ComputeShader shader, int kernel, string name, RTHandle texture, int mip = 0)
            => cmd.SetComputeTextureParam(shader, kernel, name, texture, mip);
        internal static void Dispatch(CommandBuffer cmd, ComputeShader shader, int kernel, int width, int height, int views)
        {
            cmd.SetComputeVectorParam(shader, "_DofDispatchSize", Size(width, height));
            cmd.DispatchCompute(shader, kernel, (width + 7) / 8, (height + 7) / 8, views);
        }
        internal static void Copy(CommandBuffer cmd, Work w, RTHandle source, RTHandle destination, int sourceMip = 0, int destinationMip = 0)
        {
            var cs = w.shaders.utilities;
            bool scalar = destination.rt.graphicsFormat == GraphicsFormat.R16_SFloat;
            int kernel = cs.FindKernel(scalar ? "CopyScalarMip" : "CopyColorMip");
            cmd.SetComputeIntParam(cs, "_UtilitySourceMip", sourceMip);
            Texture(cmd, cs, kernel, "_InputTexture", source);
            Texture(cmd, cs, kernel, scalar ? "_OutputScalar" : "_OutputColor", destination, destinationMip);
            Dispatch(cmd, cs, kernel, Mathf.Max(1, destination.rt.width >> destinationMip), Mathf.Max(1, destination.rt.height >> destinationMip), w.views);
        }
        internal static void Clear(CommandBuffer cmd, Work w, RTHandle target)
        {
            var cs = w.shaders.utilities;
            bool scalar = target.rt.graphicsFormat == GraphicsFormat.R16_SFloat;
            int kernel = cs.FindKernel(scalar ? "ClearScalar" : "ClearColor");
            Texture(cmd, cs, kernel, scalar ? "_OutputScalar" : "_OutputColor", target);
            Dispatch(cmd, cs, kernel, w.settings.blurWidth, w.settings.blurHeight, w.views);
        }
        internal static void Configure(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            Vector2 jitter = s.depthJitter;
            if (SystemInfo.graphicsUVStartsAtTop) jitter.y = -jitter.y;
            foreach (var shader in w.shaders.allShaders)
            {
                Keyword(cmd, shader, "STEREO_INSTANCING_ON", w.array);
                Keyword(cmd, shader, "STEREO_MULTIVIEW_ON", false);
                Keyword(cmd, shader, "ENABLE_ALPHA", true);
                Keyword(cmd, shader, "NEAR", s.lens.z > 0);
                Keyword(cmd, shader, "FAR", s.lens.w > 0);
                Keyword(cmd, shader, "FULL_RES", s.downsample == 1);
                Keyword(cmd, shader, "HIGH_QUALITY", s.highQuality && s.downsample != 1);
                Keyword(cmd, shader, "LOW_QUALITY", !s.highQuality && s.downsample != 1);
                Keyword(cmd, shader, "LOW_RESOLUTION", s.downsample != 1);
                Keyword(cmd, shader, "USE_TILES", !w.array);
                Keyword(cmd, shader, "USE_MIN_DEPTH", false);
                Keyword(cmd, shader, "FIX_NEAR_BLEND", s.limitNear && s.parameters.x > .5f);
                Keyword(cmd, shader, "ENABLE_MAX_BLENDING", s.physicalAlgorithm);
                Keyword(cmd, shader, "FORCE_POINT_SAMPLING", s.downsample != 4);
                cmd.SetComputeVectorParam(shader, "_DofScreenSize", Size(s.width, s.height));
                cmd.SetComputeVectorParam(shader, "_DofBlurSize", Size(s.blurWidth, s.blurHeight));
                cmd.SetComputeVectorParam(shader, "_DofDepthSize", w.depthSize);
                cmd.SetComputeVectorParam(shader, "_DofSourceScale", w.sourceScale);
                cmd.SetComputeVectorParam(shader, "_DofProjection", s.projection);
                cmd.SetComputeVectorParam(shader, "_DofZBufferParams", s.zBuffer);
                cmd.SetComputeVectorParam(shader, "_DofLens", s.lens);
                cmd.SetComputeVectorParam(shader, "_TaaJitterStrength", new Vector4(0, 0, -jitter.x, -jitter.y));
                cmd.SetComputeVectorParam(shader, "_TaaFrameInfo", new Vector4(0, 0, w.frame & 1023, w.temporal ? 1 : 0));
                cmd.SetComputeVectorParam(shader, "_TargetScale", new Vector4(s.downsample, 1f / s.downsample, 0, 0));
            }
        }
        internal static RTHandle RT(Work w, Target target) => w.targets[(int)target];
        internal static Target OutputTarget(Work w) => QualitySettings.activeColorSpace == ColorSpace.Gamma ? Target.Output : w.temporal ? Target.Resolved : Target.Spatial;
        internal static void EncodeOutput(CommandBuffer cmd, Work w)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma) return;
            var cs = w.shaders.utilities; int kernel = cs.FindKernel("CopyColor");
            cmd.SetComputeVectorParam(cs, "_UtilityParams", new Vector4(-1, 0, 0, 0));
            cmd.SetComputeVectorParam(cs, "_DofSourceScale", Vector4.one);
            Texture(cmd, cs, kernel, "_InputTexture", RT(w, w.temporal ? Target.Resolved : Target.Spatial));
            Texture(cmd, cs, kernel, "_OutputColor", RT(w, Target.Output));
            Dispatch(cmd, cs, kernel, w.settings.width, w.settings.height, w.views);
        }
        internal static void PrepareInput(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var utility = w.shaders.utilities;
            int kernel = utility.FindKernel("CopyColor");
            cmd.SetComputeVectorParam(utility, "_UtilityParams", new Vector4(QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0, 0, 0, 0));
            Texture(cmd, utility, kernel, "_InputTexture", w.source);
            Texture(cmd, utility, kernel, "_OutputColor", RT(w, Target.Input));
            Dispatch(cmd, utility, kernel, s.width, s.height, w.views);
            kernel = utility.FindKernel("CopyDepth");
            Texture(cmd, utility, kernel, "_CameraDepthTexture", w.depth);
            Texture(cmd, utility, kernel, "_OutputScalar", RT(w, Target.Depth));
            Dispatch(cmd, utility, kernel, s.width, s.height, w.views);
        }
        internal static void DebugCoC(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var utility = w.shaders.utilities;
            int kernel = utility.FindKernel("DebugCoC");
            cmd.SetComputeVectorParam(utility, "_UtilityParams", s.physicalAlgorithm ? new Vector4(s.lens.z, s.lens.w, 0, 0) : Vector4.one);
            Texture(cmd, utility, kernel, "_InputTexture", RT(w, Target.StableCoC));
            Texture(cmd, utility, kernel, "_OutputColor", RT(w, Target.Spatial));
            Dispatch(cmd, utility, kernel, s.width, s.height, w.views);
        }
        internal static void CopySharp(CommandBuffer cmd, Work w)
        {
            Copy(cmd, w, RT(w, Target.Input), RT(w, Target.Spatial));
            if (w.temporal) Copy(cmd, w, RT(w, Target.Input), RT(w, Target.Resolved));
        }
        internal static void Execute(CommandBuffer cmd, Work w)
        {
            Configure(cmd, w);
            PrepareInput(cmd, w);
            if (w.settings.lens.z <= 0 && w.settings.lens.w <= 0) CopySharp(cmd, w);
            else
            {
                CircleOfConfusion(cmd, w);
                ReprojectCoC(cmd, w);
                if (w.settings.debug) DebugCoC(cmd, w);
                else
                {
                    if (w.settings.physicalAlgorithm) Physical(cmd, w); else Layered(cmd, w);
                    if (w.temporal) Temporal(cmd, w);
                }
            }
            EncodeOutput(cmd, w);
        }
        internal static void CircleOfConfusion(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var cs = (s.physicalAlgorithm ? w.shaders.physicalCoC : w.shaders.layeredCoC);
            bool manual = s.parameters.x > .5f;
            int k = cs.FindKernel(s.physicalAlgorithm ? manual ? "KMainCoCManual" : "KMainCoCPhysical" : manual ? "KMainManual" : "KMainPhysical");
            Vector4 parameters = s.ranges;
            if (s.physicalAlgorithm)
            {
                parameters = manual ? new Vector4(s.ranges.z, s.ranges.y, 1f / (s.ranges.w - s.ranges.z), 1f / (s.ranges.x - s.ranges.y))
                    : new Vector4(s.lens.w, s.lens.z, 0, 0);
                cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.lens.z, s.lens.w, 0, 0));
            }
            else if (!manual) parameters = new Vector4(s.lens.x, s.optics.z, 0, 0);
            cmd.SetComputeVectorParam(cs, "_Params", parameters);
            Texture(cmd, cs, k, "_CameraDepthTexture", w.depth);
            Texture(cmd, cs, k, s.physicalAlgorithm ? "_OutputTexture" : "_OutputCoCTexture", RT(w, Target.CoC));
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
        internal static void ReprojectCoC(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var cs = w.shaders.reprojection; int k = cs.FindKernel("KMain");
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.stabilize && w.historyValid ? .86f : 0, 1, 1, 0));
            cmd.SetComputeFloatParam(cs, "_DofHistoryValid", s.stabilize && w.historyValid ? 1 : 0);
            Texture(cmd, cs, k, "_InputCoCTexture", RT(w, Target.CoC));
            Texture(cmd, cs, k, "_InputHistoryCoCTexture", w.previousCoC);
            Texture(cmd, cs, k, "_DofPreviousDepth", w.previousDepth);
            Texture(cmd, cs, k, "_CameraDepthTexture", w.depth);
            Texture(cmd, cs, k, "_CameraMotionVectorsTexture", w.motion);
            Texture(cmd, cs, k, "_OutputCoCTexture", RT(w, Target.StableCoC));
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
        internal static void Pyramid(CommandBuffer cmd, Work w, RTHandle texture, int width, int height, bool scalar)
        {
            int levels = Mathf.Min(4, Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)));
            if (width >= 16 && height >= 16 && width % 16 == 0 && height % 16 == 0 && SystemInfo.graphicsDeviceVendorID != 0x8086)
            {
                var cs = w.shaders.mip; int kernel = cs.FindKernel(scalar ? "KMainCoC" : "KMainColorAlpha");
                Texture(cmd, cs, kernel, "_InputTexture", texture);
                for (int i = 1; i <= 4; ++i) Texture(cmd, cs, kernel, "_OutputMip" + i, texture, i);
                Dispatch(cmd, cs, kernel, width / 2, height / 2, w.views);
                return;
            }
            RTHandle previous = texture;
            for (int i = 0; i < levels; ++i)
            {
                int mw = Mathf.Max(1, width >> (i + 1)), mh = Mathf.Max(1, height >> (i + 1));
                var temp = RT(w, (Target)((int)(scalar ? Target.MipScalar0 : Target.MipColor0) + i));
                var cs = (scalar ? w.shaders.utilities : w.shaders.safeMip); int k = cs.FindKernel(scalar ? "DownsampleCoC" : "KMain");
                cmd.SetComputeVectorParam(cs, "_TexelSize", Size(mw, mh));
                cmd.SetComputeVectorParam(cs, "_UtilityParams", Vector4.zero);
                Texture(cmd, cs, k, "_InputTexture", previous);
                Texture(cmd, cs, k, scalar ? "_OutputScalar" : "_OutputTexture", temp);
                Dispatch(cmd, cs, k, mw, mh, w.views);
                Copy(cmd, w, temp, texture, 0, i + 1);
                previous = temp;
            }
        }
        internal static Target DilatedTarget(Settings s) => (Mathf.Max(1, Mathf.CeilToInt((s.lens.z / s.downsample + 2) / 4)) & 1) != 0 ? Target.Dilated0 : Target.Dilated1;
        internal static Target NearOutputTarget(Settings s) => s.lens.w > 0 ? Target.Near0 : Target.Near1;
        internal static void Layered(CommandBuffer cmd, Work w)
        {
            LayeredAperture(cmd, w);
            LayeredPrefilter(cmd, w);
            LayeredPyramids(cmd, w);
            LayeredDilation(cmd, w);
            LayeredTiles(cmd, w);
            LayeredFarGather(cmd, w);
            LayeredPreCombine(cmd, w);
            LayeredNearGather(cmd, w);
            LayeredCombine(cmd, w);
        }
        internal static void LayeredAperture(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = w.shaders.apertureSamples; int k = cs.FindKernel("KParametricBlurKernel");
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.optics.x, 0, 0, 0));
            for (int layer = 0; layer < 2; ++layer)
            {
                if (layer == 0 ? !near : !far) continue;
                int count = (int)(layer == 0 ? s.counts.x : s.counts.y);
                cmd.SetComputeVectorParam(cs, "_Params1", new Vector4(count, 1 - s.apertureShape.y, s.apertureShape.x, s.apertureShape.z * Mathf.Deg2Rad));
                cmd.SetComputeBufferParam(cs, k, "_BokehKernel", w.buffers[layer]);
                cmd.DispatchCompute(cs, k, (count * count + 63) / 64, 1, 1);
            }
        }
        internal static void LayeredPrefilter(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = w.shaders.prefilter; int k = cs.FindKernel("KMain");
            Texture(cmd, cs, k, "_InputTexture", RT(w, Target.Input));
            Texture(cmd, cs, k, "_InputCoCTexture", RT(w, Target.StableCoC));
            cmd.SetComputeVectorParam(cs, "_CoCTargetScale", Vector4.one);
            if (near) { Texture(cmd, cs, k, "_OutputNearTexture", RT(w, Target.Near0)); Texture(cmd, cs, k, "_OutputNearCoCTexture", RT(w, Target.NearCoC)); }
            if (far) { Texture(cmd, cs, k, "_OutputFarTexture", RT(w, Target.Far0)); Texture(cmd, cs, k, "_OutputFarCoCTexture", RT(w, Target.FarCoC)); }
            Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, w.views);
        }
        internal static void LayeredPyramids(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            if (far) { Pyramid(cmd, w, RT(w, Target.Far0), s.blurWidth, s.blurHeight, false); Pyramid(cmd, w, RT(w, Target.FarCoC), s.blurWidth, s.blurHeight, true); }
        }
        internal static void LayeredDilation(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            RTHandle dilated = RT(w, Target.NearCoC);
            if (near)
            {
                var cs = w.shaders.nearDilation; int k = cs.FindKernel("KMain");
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.blurWidth - 1, s.blurHeight - 1, 0, 0));
                int passes = Mathf.Max(1, Mathf.CeilToInt((s.lens.z / s.downsample + 2) / 4));
                for (int i = 0; i < passes; ++i)
                {
                    RTHandle destination = RT(w, (i & 1) == 0 ? Target.Dilated0 : Target.Dilated1);
                    Texture(cmd, cs, k, "_InputCoCTexture", dilated); Texture(cmd, cs, k, "_OutputCoCTexture", destination);
                    Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, w.views); dilated = destination;
                }
            }
        }
        internal static void LayeredTiles(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            RTHandle dilated = RT(w, DilatedTarget(s));
            if (!w.array)
            {
                cmd.SetBufferCounterValue(w.buffers[3], 0); cmd.SetBufferCounterValue(w.buffers[4], 0);
                var cs = w.shaders.clearIndirect; int k = cs.FindKernel("KClear");
                cmd.SetComputeBufferParam(cs, k, "_IndirectBuffer", w.buffers[2]); cmd.DispatchCompute(cs, k, 1, 1, 1);
                cs = w.shaders.tileClassification; k = cs.FindKernel("KMain");
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.blurWidth - 1, s.blurHeight - 1, 0, 0));
                cmd.SetComputeBufferParam(cs, k, "_IndirectBuffer", w.buffers[2]);
                if (near) { Texture(cmd, cs, k, "_InputNearCoCTexture", dilated); cmd.SetComputeBufferParam(cs, k, "_NearTileList", w.buffers[3]); }
                if (far) { Texture(cmd, cs, k, "_InputFarCoCTexture", RT(w, Target.FarCoC)); cmd.SetComputeBufferParam(cs, k, "_FarTileList", w.buffers[4]); }
                Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, 1);
            }
        }
        internal static void LayeredFarGather(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            if (far) GatherLayer(cmd, w, false, RT(w, Target.Far0), RT(w, Target.Far1), RT(w, DilatedTarget(s)));
        }
        internal static void LayeredPreCombine(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            RTHandle nearInput = RT(w, Target.Near0), nearOutput = RT(w, Target.Near1);
            if (near && far)
            {
                var cs = w.shaders.preCombine; int k = cs.FindKernel("KMainPreCombineFar");
                Texture(cmd, cs, k, "_InputTexture", nearInput); Texture(cmd, cs, k, "_InputFarTexture", RT(w, Target.Far1));
                Texture(cmd, cs, k, "_InputCoCTexture", RT(w, Target.FarCoC)); Texture(cmd, cs, k, "_OutputTexture", nearOutput);
                Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, w.views);
                CoreUtils.Swap(ref nearInput, ref nearOutput);
            }
        }
        internal static void LayeredNearGather(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            if (near) GatherLayer(cmd, w, true, RT(w, far ? Target.Near1 : Target.Near0), RT(w, NearOutputTarget(s)), RT(w, DilatedTarget(s)));
        }
        internal static void LayeredCombine(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            bool near = s.lens.z > 0, far = s.lens.w > 0;
            var cs = w.shaders.layeredCombine; int k = cs.FindKernel("KMain");
            Texture(cmd, cs, k, "_InputTexture", RT(w, Target.Input)); Texture(cmd, cs, k, "_OutputTexture", RT(w, Target.Spatial));
            if (near) { Texture(cmd, cs, k, "_InputNearTexture", RT(w, NearOutputTarget(s))); Texture(cmd, cs, k, "_InputNearAlphaTexture", RT(w, Target.NearAlpha)); }
            if (far) { Texture(cmd, cs, k, "_InputFarTexture", RT(w, Target.Far1)); Texture(cmd, cs, k, "_InputCoCTexture", RT(w, Target.StableCoC)); }
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
        internal static void GatherLayer(CommandBuffer cmd, Work w, bool near, RTHandle input, RTHandle output, RTHandle dilated)
        {
            var s = w.settings; var cs = w.shaders.layeredGather; int k = cs.FindKernel(near ? "KMainNear" : "KMainFar");
            float radius = near ? s.lens.z : s.lens.w;
            float maxMip = Mathf.Min(s.downsample == 1 ? 4 : s.downsample == 2 ? 3 : 2, Mathf.FloorToInt(Mathf.Log(Mathf.Max(s.blurWidth, s.blurHeight), 2)));
            cmd.SetComputeVectorParam(cs, "_Params1", new Vector4(near ? s.counts.x : s.counts.y, radius / s.downsample, s.optics.y, radius));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(maxMip, 0, 0, 0));
            cmd.SetComputeVectorParam(cs, "_TexelSize", Size(s.blurWidth, s.blurHeight));
            Texture(cmd, cs, k, "_InputTexture", input); Texture(cmd, cs, k, "_OutputTexture", output);
            Texture(cmd, cs, k, "_InputCoCTexture", RT(w, near ? Target.NearCoC : Target.FarCoC));
            if (near) { Texture(cmd, cs, k, "_InputDilatedCoCTexture", dilated); Texture(cmd, cs, k, "_OutputAlphaTexture", RT(w, Target.NearAlpha)); }
            cmd.SetComputeBufferParam(cs, k, "_BokehKernel", w.buffers[near ? 0 : 1]);
            if (!w.array)
            {
                Clear(cmd, w, output); if (near) Clear(cmd, w, RT(w, Target.NearAlpha));
                cmd.SetComputeVectorParam(cs, "_DofDispatchSize", Size(s.blurWidth, s.blurHeight));
                cmd.SetComputeBufferParam(cs, k, "_TileList", w.buffers[near ? 3 : 4]);
                cmd.DispatchCompute(cs, k, w.buffers[2], near ? 0u : 12u);
            }
            else Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, w.views);
        }
        internal static void BindPhysical(CommandBuffer cmd, Work w, ComputeShader cs, int k, RTHandle source, RTHandle tiles, RTHandle output)
        {
            Texture(cmd, cs, k, "_InputTexture", source); Texture(cmd, cs, k, "_InputCoCTexture", RT(w, Target.StableCoC));
            Texture(cmd, cs, k, "_TileList", tiles); Texture(cmd, cs, k, "_OutputTexture", output);
            cmd.SetComputeBufferParam(cs, k, "_ApertureShapeTable", w.buffers[5]);
            cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", 256);
        }
        internal static Target PhysicalTileTarget(Settings s) => (Mathf.Max(1, Mathf.CeilToInt(s.lens.z / 8)) & 1) != 0 ? Target.Tile1 : Target.Tile0;
        internal static void Physical(CommandBuffer cmd, Work w)
        {
            PhysicalMinMax(cmd, w);
            PhysicalDilation(cmd, w);
            PhysicalAperture(cmd, w);
            PhysicalSlowGather(cmd, w);
            PhysicalPyramid(cmd, w);
            PhysicalGather(cmd, w);
            PhysicalCombine(cmd, w);
        }
        internal static void PhysicalMinMax(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var cs = w.shaders.minMax; int k = cs.FindKernel("KMainCoCMinMax");
            Texture(cmd, cs, k, "_InputTexture", RT(w, Target.StableCoC)); Texture(cmd, cs, k, "_OutputTexture", RT(w, Target.Tile0));
            cmd.SetComputeVectorParam(cs, "_OutputResolution", new Vector4(s.tileWidth, s.tileHeight, 0, 0));
            Dispatch(cmd, cs, k, s.tileWidth, s.tileHeight, w.views);
        }
        internal static void PhysicalDilation(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            RTHandle tiles = RT(w, Target.Tile0), tileOutput = RT(w, Target.Tile1);
            var cs = w.shaders.tileDilation; int k = cs.FindKernel("KMain");
            int iterations = Mathf.Max(1, Mathf.CeilToInt(s.lens.z / 8));
            for (int i = 0; i < iterations; ++i)
            {
                Texture(cmd, cs, k, "_InputTexture", tiles); Texture(cmd, cs, k, "_OutputTexture", tileOutput);
                Dispatch(cmd, cs, k, s.tileWidth, s.tileHeight, w.views); CoreUtils.Swap(ref tiles, ref tileOutput);
            }
        }
        internal static void PhysicalAperture(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            var cs = w.shaders.apertureSamples; int k = cs.FindKernel("ComputeShapeBuffer");
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.apertureShape.x, 1 - s.apertureShape.y, s.apertureShape.z * Mathf.Deg2Rad, s.optics.x));
            cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", 256); cmd.SetComputeBufferParam(cs, k, "_ApertureShapeTable", w.buffers[5]);
            cmd.DispatchCompute(cs, k, 4, 1, 1);
        }
        internal static void PhysicalSlowGather(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            RTHandle tiles = RT(w, PhysicalTileTarget(s));
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w);
            float rings = Mathf.Max(s.counts.x, s.counts.y);
            var cs = w.shaders.physicalCombine; int k = cs.FindKernel("ComputeSlowTiles");
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.adaptive.x, s.adaptive.y, s.downsample, 1f / s.downsample));
            BindPhysical(cmd, w, cs, k, RT(w, Target.Input), tiles, RT(w, Target.Spatial));
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
        internal static void PhysicalPyramid(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            RTHandle source = RT(w, Target.Input);
            if (s.downsample != 1)
            {
                Copy(cmd, w, RT(w, Target.Spatial), RT(w, Target.Pyramid));
                Pyramid(cmd, w, RT(w, Target.Pyramid), s.width, s.height, false); source = RT(w, Target.Pyramid);
            }
        }
        internal static void PhysicalGather(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            RTHandle tiles = RT(w, PhysicalTileTarget(s));
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w), rings = Mathf.Max(s.counts.x, s.counts.y);
            RTHandle source = RT(w, s.downsample == 1 ? Target.Input : Target.Pyramid);
            var cs = w.shaders.physicalGather; int k = cs.FindKernel("KMain");
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(1 + Mathf.Ceil(Mathf.Log(Mathf.Max(maxRadius, 1), 2)), 3, 1f / s.downsample, s.downsample));
            cmd.SetComputeVectorParam(cs, "_Params3", s.adaptive);
            BindPhysical(cmd, w, cs, k, source, tiles, RT(w, Target.Low));
            Dispatch(cmd, cs, k, s.blurWidth, s.blurHeight, w.views);
        }
        internal static void PhysicalCombine(CommandBuffer cmd, Work w)
        {
            var s = w.settings;
            RTHandle tiles = RT(w, PhysicalTileTarget(s));
            float maxRadius = Mathf.Max(s.lens.z, s.lens.w), rings = Mathf.Max(s.counts.x, s.counts.y);
            var cs = w.shaders.physicalCombine; int k = cs.FindKernel("UpsampleFastTiles");
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(rings, maxRadius, s.optics.x, 0));
            cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.adaptive.x, s.adaptive.y, s.downsample, 1f / s.downsample));
            Texture(cmd, cs, k, "_InputNearTexture", RT(w, Target.Low));
            Texture(cmd, cs, k, "_TileList", tiles); Texture(cmd, cs, k, "_OutputTexture", RT(w, Target.Spatial));
            cmd.SetComputeIntParam(cs, "_DebugTileClassification", s.debugTiles ? 1 : 0);
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
        internal static float CalculateTemporalContrast(float antiFlicker)
            => .7f - Mathf.Lerp(0f, .3f, Mathf.SmoothStep(.5f, 1f, antiFlicker));

        internal static void Temporal(CommandBuffer cmd, Work w)
        {
            var s = w.settings; var cs = w.shaders.temporalResolve; int k = cs.FindKernel("Resolve");
            float historyBlend = Mathf.Lerp(.4f, .95f, Mathf.InverseLerp(.6f, .95f, s.temporal.w));
            float contrast = CalculateTemporalContrast(s.temporal.x);
            cmd.SetComputeVectorParam(cs, "_DofTemporalParams", new Vector4(w.historyValid ? 1 : 0, 1 - historyBlend, w.exposure ? 1 : 0, s.alpha ? 1 : 0));
            cmd.SetComputeVectorParam(cs, "_DofTemporalTuning", new Vector4(contrast, 250 * s.temporal.y * s.temporal.y * s.temporal.y,
                s.temporal.z, Mathf.InverseLerp(.51f, 1f, s.temporal.x)));
            Texture(cmd, cs, k, "_InputTexture", RT(w, Target.Spatial)); Texture(cmd, cs, k, "_InputHistoryTexture", w.previousColor);
            Texture(cmd, cs, k, "_InputVelocityMagnitudeHistory", w.previousVelocity); Texture(cmd, cs, k, "_DofPreviousDepth", w.previousDepth);
            Texture(cmd, cs, k, "_DofResolvedDepth", RT(w, Target.Depth)); Texture(cmd, cs, k, "_CameraMotionVectorsTexture", w.motion);
            Texture(cmd, cs, k, "_OutputTexture", RT(w, Target.Resolved)); Texture(cmd, cs, k, "_OutputVelocityMagnitudeHistory", w.currentVelocity);
            // Exposure histories are scalar 2D textures even for stereo cameras.
            if (w.exposure) { Texture(cmd, cs, k, "_DofExposureCurrent", w.exposureCurrent); Texture(cmd, cs, k, "_DofExposurePrevious", w.exposurePrevious); }
            else { cmd.SetComputeTextureParam(cs, k, "_DofExposureCurrent", Texture2D.blackTexture); cmd.SetComputeTextureParam(cs, k, "_DofExposurePrevious", Texture2D.blackTexture); }
            Dispatch(cmd, cs, k, s.width, s.height, w.views);
        }
    }
}
