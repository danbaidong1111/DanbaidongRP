using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    internal sealed partial class ExposurePass : IDisposable
    {
        internal const int HistogramBins = 128;
        internal static float LensExposureScale => Mathf.Max(1e-4f, ColorUtils.lensImperfectionExposureScale);
        const int MeterSize = 1024;
        readonly ComputeShader m_Shader;
        readonly Material m_Apply;
        readonly int m_Clear, m_Histogram, m_HistogramArray, m_Average, m_AverageArray, m_ResolveHistogram, m_ResolveAverage;
        static readonly ProfilingSampler s_MeterSampler = new ProfilingSampler("Exposure Metering");
        static readonly ProfilingSampler s_ResolveSampler = new ProfilingSampler("Exposure Adaptation");
        static readonly ProfilingSampler s_ApplySampler = new ProfilingSampler("Apply Exposure");

        internal ExposurePass(ComputeShader shader, Shader applyShader)
        {
            m_Shader = shader;
            m_Apply = applyShader != null ? CoreUtils.CreateEngineMaterial(applyShader) : null;
            if (shader == null) return;
            m_Clear = shader.FindKernel("ClearHistogram");
            m_Histogram = shader.FindKernel("BuildHistogram");
            m_HistogramArray = shader.FindKernel("BuildHistogramArray");
            m_Average = shader.FindKernel("MeterAverage");
            m_AverageArray = shader.FindKernel("MeterAverageArray");
            m_ResolveHistogram = shader.FindKernel("ResolveHistogram");
            m_ResolveAverage = shader.FindKernel("ResolveAverage");
        }
        public void Dispose()
        {
            DisposeCameraStates();
            CoreUtils.Destroy(m_Apply);
        }

        internal static float FiniteClamp(float value, float fallback, float min, float max) =>
            Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, min, max);
        internal static float Calibration(Exposure settings) => settings.targetMidGrey.value == ExposureMidGrey.Grey18 ? 18f : settings.targetMidGrey.value == ExposureMidGrey.Grey14 ? 14f : 12.5f;
        internal static float EV100ToExposure(float ev) => 1f / (LensExposureScale * Mathf.Pow(2f, ev));
        internal static float CameraEV100(Camera camera) => Mathf.Log(Mathf.Max(1e-6f, camera.aperture * camera.aperture * 100f / (Mathf.Max(1e-6f, camera.shutterSpeed) * Mathf.Max(1, camera.iso))), 2f);
        internal static Vector4 GetExposureParameters(Exposure settings)
        {
            float min = FiniteClamp(settings.limitMin.value, -1f, -30f, 30f);
            float max = FiniteClamp(settings.limitMax.value, 14f, -30f, 30f);
            return new Vector4(Mathf.Min(min, max), Mathf.Max(min, max), FiniteClamp(settings.compensation.value, 0f, -30f, 30f), Calibration(settings));
        }
        internal static Vector4 GetPercentiles(Exposure settings)
        {
            float high = FiniteClamp(settings.histogramHighPercent.value, 90f, 1f, 100f) * .01f;
            float low = Mathf.Min(FiniteClamp(settings.histogramLowPercent.value, 40f, 0f, 99f) * .01f, high - .01f);
            return new Vector4(low, high, 0, 0);
        }
        internal static float ComputeTargetExposure(float logLuminance, Vector4 parameters) =>
            Mathf.Clamp(logLuminance + Mathf.Log(100f / parameters.w, 2f) - parameters.z, parameters.x, parameters.y);
        internal static float AdaptExposure(float previous, float target, float lightToDark, float darkToLight, float deltaTime)
        {
            float speed = target > previous ? darkToLight : lightToDark;
            return Mathf.Lerp(previous, target, 1f - Mathf.Pow(2f, -Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime)));
        }

        struct Settings
        {
            internal Vector4 exposure, histogram, adaptation, curve, mask, maskLimits, meterSize;
            internal int metering, views;
            internal bool array, histogramMode;
        }
        sealed class Resources
        {
            internal TextureHandle source, previous, output, average, curve, mask;
            internal BufferHandle histogram;
            internal CameraState cameraState;
            internal int frame;
        }
        sealed class ComputePassData
        {
            internal ComputeShader shader;
            internal int kernel;
            internal Settings settings;
            internal Resources resources;
        }
        sealed class ApplyPassData
        {
            internal Material material;
            internal TextureHandle source, exposure;
            internal Vector4 parameters;
        }

        Settings PrepareSettings(Exposure volume, UniversalCameraData data, TextureDesc source, bool valid, Vector2 curveRange)
        {
            bool histogram = volume.mode.value == ExposureMode.AutomaticHistogram;
            int width = histogram ? Mathf.Max(1, source.width) : MeterSize, height = histogram ? Mathf.Max(1, source.height) : MeterSize;
            Vector2 center = volume.proceduralCenter.value;
            if (volume.centerAroundExposureTarget.value && data.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional) && additional.exposureTarget != null)
            {
                Vector3 viewport = data.camera.WorldToViewportPoint(additional.exposureTarget.position);
                center += new Vector2(viewport.x, SystemInfo.graphicsUVStartsAtTop ? 1f - viewport.y : viewport.y);
            }
            center = new Vector2(Mathf.Clamp01(center.x), Mathf.Clamp01(center.y));
            Vector2 radii = volume.proceduralRadii.value;
            var p = GetExposureParameters(volume);
            var h = GetPercentiles(volume);
            h.z = volume.usesCurve ? curveRange.x : p.x;
            h.w = Mathf.Max(h.z + .001f, volume.usesCurve ? curveRange.y : p.y);
            return new Settings
            {
                exposure = p, histogram = h, meterSize = new Vector4(width, height, 1f / width, 1f / height),
                adaptation = new Vector4(FiniteClamp(volume.speedLightToDark.value, 1, 0, 1000), FiniteClamp(volume.speedDarkToLight.value, 3, 0, 1000),
                    Application.isPlaying ? FiniteClamp(Time.unscaledDeltaTime, 0, 0, 1) : 1f / 60f,
                    valid && volume.adaptationMode.value == ExposureAdaptationMode.Progressive ? 1 : 0),
                curve = new Vector4(curveRange.x, curveRange.y, volume.usesCurve ? 1 : 0, LensExposureScale),
                mask = new Vector4(center.x * width, center.y * height, Mathf.Max(.001f, radii.x) * width, Mathf.Max(.001f, radii.y) * height),
                maskLimits = new Vector4(1f / Mathf.Max(.001f, volume.proceduralSoftness.value),
                    Mathf.Pow(2f, FiniteClamp(volume.maskMinIntensity.value, -30, -30, 30)) * p.w / 100f,
                    Mathf.Pow(2f, FiniteClamp(volume.maskMaxIntensity.value, 30, -30, 30)) * p.w / 100f, 0),
                metering = (int)volume.metering.value,
                views = source.dimension == TextureDimension.Tex2DArray ? source.slices : 1,
                array = source.dimension == TextureDimension.Tex2DArray,
                histogramMode = volume.mode.value == ExposureMode.AutomaticHistogram
            };
        }

        Resources InitResources(RenderGraph graph, UniversalCameraData data, TextureHandle source, Exposure volume, out Settings settings)
        {
            var desc = graph.GetTextureDesc(source);
            var system = HistoryFrameRTSystem.GetOrCreate(data.camera, data.xr.multipassId);
            var state = GetCameraState(system);
            if (ReAllocateHistoryTextureIfNeeded(system))
                state.Invalidate();
            bool valid = state.Prepare(data, system.historyFrameCount, (int)volume.mode.value * 8 + (int)volume.metering.value);
            var range = new Vector2(-10, 20);
            var curve = volume.usesCurve ? state.GetCurve(volume, out range) : null;
            settings = PrepareSettings(volume, data, desc, valid, range);
            return new Resources
            {
                source = source, cameraState = state, frame = system.historyFrameCount,
                previous = graph.ImportTexture(system.GetPreviousFrameRT(HistoryFrameType.Exposure)), output = graph.ImportTexture(system.GetCurrentFrameRT(HistoryFrameType.Exposure)),
                curve = curve != null ? graph.ImportTexture(curve) : graph.defaultResources.blackTexture, mask = graph.ImportTexture(state.GetMask(volume.weightTextureMask.value)),
                histogram = settings.histogramMode ? graph.CreateBuffer(new BufferDesc(HistogramBins, sizeof(uint)) { name = "Exposure Histogram" }) : BufferHandle.nullHandle,
                average = !settings.histogramMode ? graph.CreateTexture(new TextureDesc(32, 32)
                { name = "Exposure Weighted Average", colorFormat = GraphicsFormat.R32G32_SFloat, enableRandomWrite = true }) : TextureHandle.nullHandle
            };
        }

        static void SetParameters(ComputePassData pass, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var shader = pass.shader;
            var s = pass.settings;
            cmd.SetComputeVectorParam(shader, "_ExposureParams", s.exposure);
            cmd.SetComputeVectorParam(shader, "_ExposureMeterSize", s.meterSize);
            float samples = Mathf.Ceil(s.meterSize.x * .5f) * Mathf.Ceil(s.meterSize.y * .5f);
            cmd.SetComputeFloatParam(shader, "_ExposureWeightScale", Mathf.Min(2048, Mathf.Floor(4294967040f / Mathf.Max(1, samples))));
            cmd.SetComputeVectorParam(shader, "_ExposurePercentiles", s.histogram);
            cmd.SetComputeVectorParam(shader, "_ExposureAdaptation", s.adaptation);
            cmd.SetComputeVectorParam(shader, "_ExposureCurveParams", s.curve);
            cmd.SetComputeVectorParam(shader, "_ExposureMask", s.mask);
            cmd.SetComputeVectorParam(shader, "_ExposureMaskLimits", s.maskLimits);
            cmd.SetComputeIntParam(shader, "_ExposureMetering", s.metering);
            cmd.SetComputeIntParam(shader, "_ExposureViews", s.views);
        }
        void RenderMetering(RenderGraph graph, Resources resources, Settings settings)
        {
            if (settings.histogramMode)
            {
                using var builder = graph.AddComputePass<ComputePassData>("Clear Exposure Histogram", out var pass);
                pass.shader = m_Shader;
                pass.kernel = m_Clear;
                pass.resources = resources;
                builder.UseBuffer(resources.histogram, AccessFlags.Write);
                builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext context) =>
                {
                    context.cmd.SetComputeBufferParam(data.shader, data.kernel, "_ExposureHistogram", data.resources.histogram);
                    context.cmd.DispatchCompute(data.shader, data.kernel, 1, 1, 1);
                });
            }
            using (var builder = graph.AddComputePass<ComputePassData>(settings.histogramMode ? "Exposure Histogram" : "Exposure Average", out var pass, s_MeterSampler))
            {
                pass.shader = m_Shader;
                pass.kernel = settings.histogramMode ? (settings.array ? m_HistogramArray : m_Histogram) : (settings.array ? m_AverageArray : m_Average);
                pass.resources = resources;
                pass.settings = settings;
                builder.UseTexture(resources.source, AccessFlags.Read);
                builder.UseTexture(resources.mask, AccessFlags.Read);
                if (settings.histogramMode) builder.UseBuffer(resources.histogram, AccessFlags.ReadWrite);
                else builder.UseTexture(resources.average, AccessFlags.Write);
                builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext context) =>
                {
                    SetParameters(data, context);
                    var r = data.resources;
                    var cmd = context.cmd;
                    RTHandle source = r.source;
                    cmd.SetComputeVectorParam(data.shader, "_ExposureSourceScale", source.useScaling ? source.rtHandleProperties.rtHandleScale : Vector4.one);
                    cmd.SetComputeTextureParam(data.shader, data.kernel, data.settings.array ? "_ExposureSourceArray" : "_ExposureSource", r.source);
                    cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposureWeightMask", r.mask);
                    if (data.settings.histogramMode) cmd.SetComputeBufferParam(data.shader, data.kernel, "_ExposureHistogram", r.histogram);
                    else cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposureAverage", r.average);
                    cmd.DispatchCompute(data.shader, data.kernel, data.settings.histogramMode ? Mathf.CeilToInt(data.settings.meterSize.x / 32) : 32,
                        data.settings.histogramMode ? Mathf.CeilToInt(data.settings.meterSize.y / 16) : 32, 1);
                });
            }
        }
        void RenderAdaptation(RenderGraph graph, Resources resources, Settings settings)
        {
            using var builder = graph.AddComputePass<ComputePassData>("Exposure Adaptation", out var pass, s_ResolveSampler);
            pass.shader = m_Shader;
            pass.kernel = settings.histogramMode ? m_ResolveHistogram : m_ResolveAverage;
            pass.resources = resources;
            pass.settings = settings;
            if (settings.histogramMode) builder.UseBuffer(resources.histogram, AccessFlags.Read);
            else builder.UseTexture(resources.average, AccessFlags.Read);
            builder.UseTexture(resources.curve, AccessFlags.Read);
            builder.UseTexture(resources.previous, AccessFlags.Read);
            builder.UseTexture(resources.output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext context) =>
            {
                SetParameters(data, context);
                var cmd = context.cmd;
                var r = data.resources;
                if (data.settings.histogramMode) cmd.SetComputeBufferParam(data.shader, data.kernel, "_ExposureHistogram", r.histogram);
                else cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposureAverage", r.average);
                cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposureCurveTexture", r.curve);
                cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposurePrevious", r.previous);
                cmd.SetComputeTextureParam(data.shader, data.kernel, "_ExposureOutput", r.output);
                cmd.DispatchCompute(data.shader, data.kernel, 1, 1, 1);
                r.cameraState.Commit(r.frame);
            });
        }
        TextureHandle RenderApply(RenderGraph graph, TextureHandle source, TextureHandle exposure, float multiplier, bool automatic)
        {
            var desc = graph.GetTextureDesc(source);
            desc.name = "Exposed Scene Color";
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.depthBufferBits = DepthBits.None;
            desc.enableRandomWrite = false;
            desc.clearBuffer = false;
            var destination = graph.CreateTexture(desc);
            using var builder = graph.AddRasterRenderPass<ApplyPassData>("Apply Exposure", out var pass, s_ApplySampler);
            pass.material = m_Apply;
            pass.source = source;
            pass.exposure = exposure;
            pass.parameters = new Vector4(automatic ? 1 : 0, multiplier, 0, 0);
            builder.UseTexture(source, AccessFlags.Read);
            builder.UseTexture(exposure, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (ApplyPassData data, RasterGraphContext context) =>
            {
                RTHandle sourceHandle = data.source;
                Vector4 scale = sourceHandle.useScaling ? sourceHandle.rtHandleProperties.rtHandleScale : Vector4.one;
                data.material.SetTexture("_ExposureTexture", data.exposure);
                data.material.SetVector("_ExposureApplyParams", data.parameters);
                Blitter.BlitTexture(context.cmd, sourceHandle, new Vector4(scale.x, scale.y, 0, 0), data.material, 0);
            });
            return destination;
        }
        internal TextureHandle Render(RenderGraph graph, ContextContainer frameData, TextureHandle source)
        {
            var data = frameData.Get<UniversalCameraData>();
            var volume = VolumeManager.instance.stack.GetComponent<Exposure>();
            if (volume == null || !volume.IsActive() || m_Apply == null)
            {
                InvalidateHistory(data);
                return source;
            }
            var exposure = graph.defaultResources.blackTexture;
            if (volume.isAutomatic)
            {
                if (m_Shader == null || !SystemInfo.supportsComputeShaders || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.LoadStore)) return source;
                var resources = InitResources(graph, data, source, volume, out var settings);
                RenderMetering(graph, resources, settings);
                RenderAdaptation(graph, resources, settings);
                exposure = resources.output;
            }
            else InvalidateHistory(data);
            float ev = (volume.mode.value == ExposureMode.UsePhysicalCamera ? CameraEV100(data.camera) : FiniteClamp(volume.fixedExposure.value, 0, -30, 30)) - FiniteClamp(volume.compensation.value, 0, -30, 30);
            return RenderApply(graph, source, exposure, EV100ToExposure(Mathf.Clamp(ev, -30, 30)), volume.isAutomatic);
        }
    }
}
