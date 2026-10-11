using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldPass : IDisposable
    {
        internal sealed class ShaderResources
        {
            internal readonly ComputeShader apertureSamples;
            internal readonly int apertureSamplesKernel;
            internal readonly ComputeShader layeredCoC;
            internal readonly int layeredCoCPhysicalKernel, layeredCoCManualKernel;
            internal readonly ComputeShader reprojection;
            internal readonly int reprojectionKernel;
            internal readonly ComputeShader prefilter;
            internal readonly int prefilterKernel;
            internal readonly ComputeShader mip;
            internal readonly int mipColorAlphaKernel, mipCoCKernel;
            internal readonly ComputeShader nearDilation;
            internal readonly int nearDilationKernel;
            internal readonly ComputeShader clearIndirect;
            internal readonly int clearIndirectKernel;
            internal readonly ComputeShader tileClassification;
            internal readonly int tileClassificationKernel;
            internal readonly ComputeShader layeredGather;
            internal readonly int layeredGatherNearKernel, layeredGatherFarKernel;
            internal readonly ComputeShader preCombine;
            internal readonly int preCombineKernel;
            internal readonly ComputeShader layeredCombine;
            internal readonly int layeredCombineKernel;
            internal readonly ComputeShader physicalCoC;
            internal readonly int physicalCoCPhysicalKernel, physicalCoCManualKernel;
            internal readonly ComputeShader minMax;
            internal readonly int minMaxKernel;
            internal readonly ComputeShader tileDilation;
            internal readonly int tileDilationKernel;
            internal readonly int apertureShapeKernel;
            internal readonly int slowGatherKernel;
            internal readonly ComputeShader physicalGather;
            internal readonly int physicalGatherKernel;
            internal readonly ComputeShader physicalCombine;
            internal readonly int physicalCombineKernel;
            internal readonly ComputeShader temporalResolve;
            internal readonly int temporalResolveKernel;
            internal readonly ComputeShader utilities;
            internal readonly int utilitiesClearColorKernel, utilitiesClearScalarKernel, utilitiesCopyColorMipKernel, utilitiesCopyScalarMipKernel, utilitiesCopyColorKernel, utilitiesCopyDepthKernel, utilitiesDebugCoCKernel, utilitiesDownsampleCoCKernel;
            internal readonly ComputeShader safeMip;
            internal readonly int safeMipKernel;
            internal readonly ComputeShader[] allShaders;
            internal readonly bool isValid;

            internal ShaderResources(PostProcessData.ShaderResources shaders)
            {
                apertureSamples = shaders.depthOfFieldKernelCS;
                layeredCoC = shaders.depthOfFieldCoCCS;
                reprojection = shaders.depthOfFieldCoCReprojectCS;
                prefilter = shaders.depthOfFieldPrefilterCS;
                mip = shaders.depthOfFieldMipCS;
                nearDilation = shaders.depthOfFieldDilateCS;
                clearIndirect = shaders.depthOfFieldClearIndirectArgsCS;
                tileClassification = shaders.depthOfFieldTileMaxCS;
                layeredGather = shaders.depthOfFieldGatherCS;
                preCombine = shaders.depthOfFieldPreCombineFarCS;
                layeredCombine = shaders.depthOfFieldCombineCS;
                safeMip = shaders.depthOfFieldMipSafeCS;
                physicalCoC = shaders.dofCircleOfConfusionCS;
                minMax = shaders.dofCoCMinMaxCS;
                tileDilation = shaders.dofMinMaxDilateCS;
                physicalGather = shaders.dofGatherCS;
                physicalCombine = shaders.dofCombineCS;
                temporalResolve = shaders.depthOfFieldTemporalCS;
                utilities = shaders.depthOfFieldUtilitiesCS;
                allShaders = new[]
                {
                    apertureSamples, layeredCoC, reprojection, prefilter,
                    mip, nearDilation, clearIndirect, tileClassification,
                    layeredGather, preCombine, layeredCombine, safeMip,
                    physicalCoC, minMax, tileDilation, physicalGather,
                    physicalCombine, temporalResolve, utilities,
                };
                isValid = Array.TrueForAll(allShaders, shader => shader != null);
                if (!isValid)
                    return;

                apertureSamplesKernel = apertureSamples.FindKernel("KParametricBlurKernel");
                layeredCoCPhysicalKernel = layeredCoC.FindKernel("KMainPhysical");
                layeredCoCManualKernel = layeredCoC.FindKernel("KMainManual");
                reprojectionKernel = reprojection.FindKernel("KMain");
                prefilterKernel = prefilter.FindKernel("KMain");
                mipColorAlphaKernel = mip.FindKernel("KMainColorAlpha");
                mipCoCKernel = mip.FindKernel("KMainCoC");
                nearDilationKernel = nearDilation.FindKernel("KMain");
                clearIndirectKernel = clearIndirect.FindKernel("KClear");
                tileClassificationKernel = tileClassification.FindKernel("KMain");
                layeredGatherNearKernel = layeredGather.FindKernel("KMainNear");
                layeredGatherFarKernel = layeredGather.FindKernel("KMainFar");
                preCombineKernel = preCombine.FindKernel("KMainPreCombineFar");
                layeredCombineKernel = layeredCombine.FindKernel("KMain");
                physicalCoCPhysicalKernel = physicalCoC.FindKernel("KMainCoCPhysical");
                physicalCoCManualKernel = physicalCoC.FindKernel("KMainCoCManual");
                minMaxKernel = minMax.FindKernel("KMainCoCMinMax");
                tileDilationKernel = tileDilation.FindKernel("KMain");
                apertureShapeKernel = apertureSamples.FindKernel("ComputeShapeBuffer");
                slowGatherKernel = physicalCombine.FindKernel("ComputeSlowTiles");
                physicalGatherKernel = physicalGather.FindKernel("KMain");
                physicalCombineKernel = physicalCombine.FindKernel("UpsampleFastTiles");
                temporalResolveKernel = temporalResolve.FindKernel("Resolve");
                utilitiesClearColorKernel = utilities.FindKernel("ClearColor");
                utilitiesClearScalarKernel = utilities.FindKernel("ClearScalar");
                utilitiesCopyColorMipKernel = utilities.FindKernel("CopyColorMip");
                utilitiesCopyScalarMipKernel = utilities.FindKernel("CopyScalarMip");
                utilitiesCopyColorKernel = utilities.FindKernel("CopyColor");
                utilitiesCopyDepthKernel = utilities.FindKernel("CopyDepth");
                utilitiesDebugCoCKernel = utilities.FindKernel("DebugCoC");
                utilitiesDownsampleCoCKernel = utilities.FindKernel("DownsampleCoC");
                safeMipKernel = safeMip.FindKernel("KMain");
            }
        }

        static readonly int[] s_OutputMipIds =
        {
            Shader.PropertyToID("_OutputMip1"),
            Shader.PropertyToID("_OutputMip2"),
            Shader.PropertyToID("_OutputMip3"),
            Shader.PropertyToID("_OutputMip4")
        };
        static Vector4 Size(int width, int height) => new Vector4(width, height, 1f / width, 1f / height);
        static void Texture(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, string name, TextureHandle texture, int mip = 0) => cmd.SetComputeTextureParam(shader, kernel, name, texture, mip);
        static void Dispatch(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, int width, int height, int views)
        {
            cmd.SetComputeVectorParam(shader, "_DofDispatchSize", Size(width, height));
            cmd.DispatchCompute(shader, kernel, (width + 7) / 8, (height + 7) / 8, views);
        }

        static void Keyword(ComputeShader shader, string name, bool enabled)
        {
            var keyword = shader.keywordSpace.FindKeyword(name);
            if (!keyword.isValid)
                return;
            if (enabled)
                shader.EnableKeyword(keyword);
            else
                shader.DisableKeyword(keyword);
        }

        static void Configure(ComputeCommandBuffer cmd, in Resources data, ComputeShader shader)
        {
            var s = data.settings;
            Vector2 jitter = s.depthJitter;
            if (SystemInfo.graphicsUVStartsAtTop)
                jitter.y = -jitter.y;
            Keyword(shader, "STEREO_INSTANCING_ON", data.array);
            Keyword(shader, "STEREO_MULTIVIEW_ON", false);
            Keyword(shader, "ENABLE_ALPHA", true);
            Keyword(shader, "NEAR", s.lens.z > 0);
            Keyword(shader, "FAR", s.lens.w > 0);
            Keyword(shader, "FULL_RES", s.downsample == 1);
            Keyword(shader, "HIGH_QUALITY", s.highQuality && s.downsample != 1);
            Keyword(shader, "LOW_QUALITY", !s.highQuality && s.downsample != 1);
            Keyword(shader, "LOW_RESOLUTION", s.downsample != 1);
            Keyword(shader, "USE_TILES", !data.array);
            Keyword(shader, "USE_MIN_DEPTH", false);
            Keyword(shader, "FIX_NEAR_BLEND", s.limitNear && s.parameters.x > .5f);
            Keyword(shader, "ENABLE_MAX_BLENDING", s.physicalAlgorithm);
            Keyword(shader, "FORCE_POINT_SAMPLING", s.downsample != 4);
            cmd.SetComputeVectorParam(shader, "_DofScreenSize", Size(s.width, s.height));
            cmd.SetComputeVectorParam(shader, "_DofBlurSize", Size(s.blurWidth, s.blurHeight));
            cmd.SetComputeVectorParam(shader, "_DofDepthSize", data.depthSize);
            cmd.SetComputeVectorParam(shader, "_DofSourceScale", data.scale.value);
            cmd.SetComputeVectorParam(shader, "_DofProjection", s.projection);
            cmd.SetComputeVectorParam(shader, "_DofZBufferParams", s.zBuffer);
            cmd.SetComputeVectorParam(shader, "_DofLens", s.lens);
            cmd.SetComputeVectorParam(shader, "_TaaJitterStrength", new Vector4(0, 0, -jitter.x, -jitter.y));
            cmd.SetComputeVectorParam(shader, "_TaaFrameInfo", new Vector4(0, 0, data.frame & 1023, data.temporal ? 1 : 0));
            cmd.SetComputeVectorParam(shader, "_TargetScale", new Vector4(s.downsample, 1f / s.downsample, 0, 0));
        }

        static void Copy(ComputeCommandBuffer cmd, in Resources data, TextureHandle source, TextureHandle destination, int width, int height, bool scalar = false, int destinationMip = 0)
        {
            var cs = data.shaders.utilities;
            Configure(cmd, data, cs);
            int kernel = scalar ? data.shaders.utilitiesCopyScalarMipKernel : data.shaders.utilitiesCopyColorMipKernel;
            cmd.SetComputeIntParam(cs, "_UtilitySourceMip", 0);
            Texture(cmd, cs, kernel, "_InputTexture", source);
            Texture(cmd, cs, kernel, scalar ? "_OutputScalar" : "_OutputColor", destination, destinationMip);
            Dispatch(cmd, cs, kernel, width, height, data.views);
        }

        static void Clear(ComputeCommandBuffer cmd, in Resources data, TextureHandle target, bool scalar = false)
        {
            var cs = data.shaders.utilities;
            Configure(cmd, data, cs);
            int kernel = scalar ? data.shaders.utilitiesClearScalarKernel : data.shaders.utilitiesClearColorKernel;
            Texture(cmd, cs, kernel, scalar ? "_OutputScalar" : "_OutputColor", target);
            Dispatch(cmd, cs, kernel, data.settings.blurWidth, data.settings.blurHeight, data.views);
        }

        static TextureHandle DilatedTexture(in Resources data) => (Mathf.Max(1, Mathf.CeilToInt((data.settings.lens.z / data.settings.downsample + 2) / 4)) & 1) != 0 ? data.dilated0 : data.dilated1;
        static TextureHandle NearOutputTexture(in Resources data) => data.settings.lens.w > 0 ? data.near0 : data.near1;
        static TextureHandle PhysicalTileTexture(in Resources data) => (Mathf.Max(1, Mathf.CeilToInt(data.settings.lens.z / 8)) & 1) != 0 ? data.tile1 : data.tile0;
        static TextureHandle MipTexture(in Resources data, bool scalar, int mip) => mip switch
        {
            0 => scalar ? data.mipScalar0 : data.mipColor0,
            1 => scalar ? data.mipScalar1 : data.mipColor1,
            2 => scalar ? data.mipScalar2 : data.mipColor2,
            _ => scalar ? data.mipScalar3 : data.mipColor3
        };
        static void Pyramid(ComputeCommandBuffer cmd, in Resources data, TextureHandle texture, int width, int height, bool scalar)
        {
            int levels = Mathf.Min(4, Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)));
            if (width >= 16 && height >= 16 && width % 16 == 0 && height % 16 == 0 && SystemInfo.graphicsDeviceVendorID != 0x8086)
            {
                var cs = data.shaders.mip;
                Configure(cmd, data, cs);
                int kernel = scalar ? data.shaders.mipCoCKernel : data.shaders.mipColorAlphaKernel;
                Texture(cmd, cs, kernel, "_InputTexture", texture);
                for (int i = 1; i <= 4; ++i)
                    cmd.SetComputeTextureParam(cs, kernel, s_OutputMipIds[i - 1], texture, i);
                Dispatch(cmd, cs, kernel, width / 2, height / 2, data.views);
                return;
            }

            TextureHandle previous = texture;
            for (int i = 0; i < levels; ++i)
            {
                int mw = Mathf.Max(1, width >> (i + 1)), mh = Mathf.Max(1, height >> (i + 1));
                var temp = MipTexture(data, scalar, i);
                var cs = (scalar ? data.shaders.utilities : data.shaders.safeMip);
                Configure(cmd, data, cs);
                int k = scalar ? data.shaders.utilitiesDownsampleCoCKernel : data.shaders.safeMipKernel;
                cmd.SetComputeVectorParam(cs, "_TexelSize", Size(mw, mh));
                cmd.SetComputeVectorParam(cs, "_UtilityParams", Vector4.zero);
                Texture(cmd, cs, k, "_InputTexture", previous);
                Texture(cmd, cs, k, scalar ? "_OutputScalar" : "_OutputTexture", temp);
                Dispatch(cmd, cs, k, mw, mh, data.views);
                Copy(cmd, data, temp, texture, mw, mh, scalar, i + 1);
                previous = temp;
            }
        }

        static void RenderPrepareInput(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Prepare Input", out var data);
            data.resources = r;
            builder.UseTexture(r.source, AccessFlags.Read);
            builder.UseTexture(r.depth, AccessFlags.Read);
            builder.UseTexture(r.input, AccessFlags.Write);
            builder.UseTexture(r.resolvedDepth, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecutePrepareInput(context.cmd, pass.resources);
            });
        }

        static void ExecutePrepareInput(ComputeCommandBuffer cmd, in Resources data)
        {
            RTHandle color = data.source, depth = data.depth;
            data.scale.value = new Vector4(color.useScaling ? color.rtHandleProperties.rtHandleScale.x : 1, color.useScaling ? color.rtHandleProperties.rtHandleScale.y : 1, depth.useScaling ? depth.rtHandleProperties.rtHandleScale.x : 1, depth.useScaling ? depth.rtHandleProperties.rtHandleScale.y : 1);
            var s = data.settings;
            var utility = data.shaders.utilities;
            Configure(cmd, data, utility);
            int kernel = data.shaders.utilitiesCopyColorKernel;
            cmd.SetComputeVectorParam(utility, "_UtilityParams", new Vector4(QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0, 0, 0, 0));
            Texture(cmd, utility, kernel, "_InputTexture", data.source);
            Texture(cmd, utility, kernel, "_OutputColor", data.input);
            Dispatch(cmd, utility, kernel, s.width, s.height, data.views);
            kernel = data.shaders.utilitiesCopyDepthKernel;
            Texture(cmd, utility, kernel, "_CameraDepthTexture", data.depth);
            Texture(cmd, utility, kernel, "_OutputScalar", data.resolvedDepth);
            Dispatch(cmd, utility, kernel, s.width, s.height, data.views);
        }

        static void RenderCircleOfConfusion(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Circle of Confusion", out var data);
            data.resources = r;
            builder.UseTexture(r.depth, AccessFlags.Read);
            builder.UseTexture(r.coc, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteCircleOfConfusion(context.cmd, pass.resources);
            });
        }

        static void ExecuteCircleOfConfusion(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = (s.physicalAlgorithm ? data.shaders.physicalCoC : data.shaders.layeredCoC);
            Configure(cmd, data, cs);
            bool manual = s.parameters.x > .5f;
            int k = s.physicalAlgorithm ? manual ? data.shaders.physicalCoCManualKernel : data.shaders.physicalCoCPhysicalKernel : manual ? data.shaders.layeredCoCManualKernel : data.shaders.layeredCoCPhysicalKernel;
            Vector4 parameters = s.ranges;
            if (s.physicalAlgorithm)
            {
                parameters = manual ? new Vector4(s.ranges.z, s.ranges.y, 1f / (s.ranges.w - s.ranges.z), 1f / (s.ranges.x - s.ranges.y)) : new Vector4(s.lens.w, s.lens.z, 0, 0);
                cmd.SetComputeVectorParam(cs, "_Params2", new Vector4(s.lens.z, s.lens.w, 0, 0));
            }
            else if (!manual)
                parameters = new Vector4(s.lens.x, s.optics.z, 0, 0);
            cmd.SetComputeVectorParam(cs, "_Params", parameters);
            Texture(cmd, cs, k, "_CameraDepthTexture", data.depth);
            Texture(cmd, cs, k, s.physicalAlgorithm ? "_OutputTexture" : "_OutputCoCTexture", data.coc);
            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }

        static void RenderReprojectCoC(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF CoC Reprojection", out var data);
            data.resources = r;
            builder.UseTexture(r.coc, AccessFlags.Read);
            builder.UseTexture(r.depth, AccessFlags.Read);
            builder.UseTexture(r.motion, AccessFlags.Read);
            builder.UseTexture(r.previousCoC, AccessFlags.Read);
            builder.UseTexture(r.previousDepth, AccessFlags.Read);
            builder.UseTexture(r.stableCoC, AccessFlags.Write);
            if (!r.temporal)
                builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteReprojectCoC(context.cmd, pass.resources);
                if (!pass.resources.temporal)
                    pass.resources.cameraState?.Commit(pass.resources.frame);
            });
        }

        static void ExecuteReprojectCoC(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = data.shaders.reprojection;
            Configure(cmd, data, cs);
            int k = data.shaders.reprojectionKernel;
            cmd.SetComputeVectorParam(cs, "_Params", new Vector4(s.stabilize && data.historyValid ? .86f : 0, 1, 1, 0));
            cmd.SetComputeFloatParam(cs, "_DofHistoryValid", s.stabilize && data.historyValid ? 1 : 0);
            Texture(cmd, cs, k, "_InputCoCTexture", data.coc);
            Texture(cmd, cs, k, "_InputHistoryCoCTexture", data.previousCoC);
            Texture(cmd, cs, k, "_DofPreviousDepth", data.previousDepth);
            Texture(cmd, cs, k, "_CameraDepthTexture", data.depth);
            Texture(cmd, cs, k, "_CameraMotionVectorsTexture", data.motion);
            Texture(cmd, cs, k, "_OutputCoCTexture", data.stableCoC);
            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }

        static void RenderDebugCoC(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF CoC Debug", out var data);
            data.resources = r;
            builder.UseTexture(r.stableCoC, AccessFlags.Read);
            builder.UseTexture(r.spatial, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteDebugCoC(context.cmd, pass.resources);
            });
        }

        static void ExecuteDebugCoC(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var utility = data.shaders.utilities;
            Configure(cmd, data, utility);
            int kernel = data.shaders.utilitiesDebugCoCKernel;
            cmd.SetComputeVectorParam(utility, "_UtilityParams", s.physicalAlgorithm ? new Vector4(s.lens.z, s.lens.w, 0, 0) : Vector4.one);
            Texture(cmd, utility, kernel, "_InputTexture", data.stableCoC);
            Texture(cmd, utility, kernel, "_OutputColor", data.spatial);
            Dispatch(cmd, utility, kernel, s.width, s.height, data.views);
        }

        static void RenderEncodeOutput(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Output Encoding", out var data);
            data.resources = r;
            builder.UseTexture(r.temporal ? r.resolved : r.spatial, AccessFlags.Read);
            builder.UseTexture(r.output, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteEncodeOutput(context.cmd, pass.resources);
            });
        }

        static void ExecuteEncodeOutput(ComputeCommandBuffer cmd, in Resources data)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
                return;
            var cs = data.shaders.utilities;
            Configure(cmd, data, cs);
            int kernel = data.shaders.utilitiesCopyColorKernel;
            cmd.SetComputeVectorParam(cs, "_UtilityParams", new Vector4(-1, 0, 0, 0));
            cmd.SetComputeVectorParam(cs, "_DofSourceScale", Vector4.one);
            Texture(cmd, cs, kernel, "_InputTexture", (data.temporal ? data.resolved : data.spatial));
            Texture(cmd, cs, kernel, "_OutputColor", data.output);
            Dispatch(cmd, cs, kernel, data.settings.width, data.settings.height, data.views);
        }

        static void RenderTemporal(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("DOF Temporal Resolve", out var data);
            data.resources = r;
            builder.UseTexture(r.spatial, AccessFlags.Read);
            builder.UseTexture(r.resolvedDepth, AccessFlags.Read);
            builder.UseTexture(r.motion, AccessFlags.Read);
            builder.UseTexture(r.previousColor, AccessFlags.Read);
            builder.UseTexture(r.previousVelocity, AccessFlags.Read);
            builder.UseTexture(r.previousDepth, AccessFlags.Read);
            builder.UseTexture(r.resolved, AccessFlags.Write);
            builder.UseTexture(r.currentVelocity, AccessFlags.Write);
            if (r.exposure)
            {
                builder.UseTexture(r.exposureCurrent, AccessFlags.Read);
                builder.UseTexture(r.exposurePrevious, AccessFlags.Read);
            }
            else
                builder.UseTexture(r.black, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) =>
            {
                ExecuteTemporal(context.cmd, pass.resources);
                pass.resources.cameraState?.Commit(pass.resources.frame);
            });
        }

        static void ExecuteTemporal(ComputeCommandBuffer cmd, in Resources data)
        {
            var s = data.settings;
            var cs = data.shaders.temporalResolve;
            Configure(cmd, data, cs);
            int k = data.shaders.temporalResolveKernel;
            float historyBlend = Mathf.Lerp(.4f, .95f, Mathf.InverseLerp(.6f, .95f, s.temporal.w));
            float contrast = CalculateTemporalContrast(s.temporal.x);
            cmd.SetComputeVectorParam(cs, "_DofTemporalParams", new Vector4(data.historyValid ? 1 : 0, 1 - historyBlend, data.exposure ? 1 : 0, s.alpha ? 1 : 0));
            cmd.SetComputeVectorParam(cs, "_DofTemporalTuning", new Vector4(contrast, 250 * s.temporal.y * s.temporal.y * s.temporal.y, s.temporal.z, Mathf.InverseLerp(.51f, 1f, s.temporal.x)));
            Texture(cmd, cs, k, "_InputTexture", data.spatial);
            Texture(cmd, cs, k, "_InputHistoryTexture", data.previousColor);
            Texture(cmd, cs, k, "_InputVelocityMagnitudeHistory", data.previousVelocity);
            Texture(cmd, cs, k, "_DofPreviousDepth", data.previousDepth);
            Texture(cmd, cs, k, "_DofResolvedDepth", data.resolvedDepth);
            Texture(cmd, cs, k, "_CameraMotionVectorsTexture", data.motion);
            Texture(cmd, cs, k, "_OutputTexture", data.resolved);
            Texture(cmd, cs, k, "_OutputVelocityMagnitudeHistory", data.currentVelocity);
            // Exposure histories are scalar 2D textures even for stereo cameras.
            if (data.exposure)
            {
                Texture(cmd, cs, k, "_DofExposureCurrent", data.exposureCurrent);
                Texture(cmd, cs, k, "_DofExposurePrevious", data.exposurePrevious);
            }
            else
            {
                cmd.SetComputeTextureParam(cs, k, "_DofExposureCurrent", data.black);
                cmd.SetComputeTextureParam(cs, k, "_DofExposurePrevious", data.black);
            }

            Dispatch(cmd, cs, k, s.width, s.height, data.views);
        }
    }
}
