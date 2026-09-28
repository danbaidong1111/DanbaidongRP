using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed class ReSTIRGlobalIlluminationPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("Render ReSTIR GI");
        private static readonly ProfilingSampler s_RayTracingSampler = new ProfilingSampler("ReSTIR GI Initial");
        private static readonly ProfilingSampler s_ScreenSpaceSampler = new ProfilingSampler("ReSTIR GI Screen Space Initial");
        private static readonly ProfilingSampler s_TemporalSampler = new ProfilingSampler("ReSTIR GI Temporal");
        private static readonly ProfilingSampler s_SpatialSampler = new ProfilingSampler("ReSTIR GI Spatial");
        private static readonly ProfilingSampler s_ResolveSampler = new ProfilingSampler("ReSTIR GI Resolve");
        private static readonly ProfilingSampler s_CopySampler = new ProfilingSampler("ReSTIR GI History Copy");
        private static readonly ProfilingSampler s_DenoiseTemporalSampler = new ProfilingSampler("ReSTIR GI Denoise Temporal");
        private static readonly ProfilingSampler s_DenoiseSpatialSampler = new ProfilingSampler("ReSTIR GI Denoise Spatial");
        private static readonly ProfilingSampler s_UpsampleSampler = new ProfilingSampler("ReSTIR GI Depth Normal Upsample");
        private static readonly int s_ShaderVariablesGlobalIllumination = Shader.PropertyToID("ShaderVariablesScreenSpaceGlobalIllumination");
        private static readonly int[] s_ATrousStepSizes = { 1, 2 };
        private static readonly ScaleFunc s_TraceScaleFunc = CalculateTraceSize;
        private static readonly ScaleFunc s_FullResolutionScaleFunc = sourceSize => sourceSize;
        private const int k_NeighborOffsetCount = 256;

        private static Vector2Int CalculateTraceSize(Vector2Int sourceSize)
        {
            return new Vector2Int(RenderingUtils.DivRoundUp(sourceSize.x, 2), RenderingUtils.DivRoundUp(sourceSize.y, 2));
        }

        private struct GIHistoryState
        {
            internal int frameCount;
            internal int width;
            internal int height;
            internal bool halfResolution;
            internal bool rayTracing;
            internal int candidateCount;
            internal bool denoiserHistoryWritten;
        }

        private readonly ComputeShader m_ComputeShader;
        private readonly ComputeShader m_DenoiserCS;
        private readonly RayTracingShader m_RayTracingShader;
        private readonly int m_TemporalKernel;
        private readonly int m_SpatialKernel;
        private readonly int m_CopyKernel;
        private readonly int m_CopyResolveKernel;
        private readonly int m_ResolveKernel;
        private readonly int m_UpsampleKernel;
        private readonly int m_ScreenSpaceKernel;
        private readonly int m_DenoiseTemporalKernel;
        private readonly int m_DenoiseTemporalFinalKernel;
        private readonly int[] m_DenoiseSpatialKernels;
        private readonly GraphicsBuffer m_NeighborOffsetsBuffer;
        private readonly Dictionary<int, GIHistoryState> m_HistoryStates = new Dictionary<int, GIHistoryState>();

        private GlobalIllumination m_VolumeSettings;
        private bool m_UseRayTracing;

        internal ReSTIRGlobalIlluminationPass(RenderPassEvent evt, ComputeShader computeShader, ComputeShader denoiserCS, RayTracingShader rayTracingShader)
        {
            renderPassEvent = evt;
            m_ComputeShader = computeShader;
            m_DenoiserCS = denoiserCS;
            m_RayTracingShader = rayTracingShader;

            if (m_ComputeShader != null)
            {
                m_TemporalKernel = m_ComputeShader.FindKernel("ReSTIRGIResamplingTemporal");
                m_SpatialKernel = m_ComputeShader.FindKernel("ReSTIRGIResamplingSpatial");
                m_CopyKernel = m_ComputeShader.FindKernel("ReSTIRGIResamplingCopy");
                m_CopyResolveKernel = m_ComputeShader.FindKernel("ReSTIRGIResamplingCopyResolve");
                m_ResolveKernel = m_ComputeShader.FindKernel("ReSTIRGIResamplingResolve");
                m_UpsampleKernel = m_ComputeShader.FindKernel("ReSTIRGIDepthNormalUpsample");
                m_ScreenSpaceKernel = m_ComputeShader.FindKernel("ReSTIRGIInitialScreenSpace");

                m_NeighborOffsetsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, k_NeighborOffsetCount, sizeof(float) * 2)
                {
                    name = "ReSTIR GI Neighbor Offsets"
                };
                m_NeighborOffsetsBuffer.SetData(CreateNeighborOffsets());
            }

            if (m_DenoiserCS != null)
            {
                m_DenoiseTemporalKernel = m_DenoiserCS.FindKernel("ReSTIRGIDenoiseTemporal");
                m_DenoiseTemporalFinalKernel = m_DenoiserCS.FindKernel("ReSTIRGIDenoiseTemporalFinal");
                m_DenoiseSpatialKernels = new[]
                {
                    m_DenoiserCS.FindKernel("ReSTIRGIDenoiseSpatialStep1"),
                    m_DenoiserCS.FindKernel("ReSTIRGIDenoiseSpatialStep2")
                };
            }
        }

        private static Vector2[] CreateNeighborOffsets()
        {
            // Matches RTXDI FillNeighborOffsetBuffer: a plastic-constant sequence
            // rejected against a radius-0.5 disk, quantized as R8G8_SNORM.
            const float quantizationScale = 250.0f;
            const float snormScale = 1.0f / 127.0f;
            float phi2 = 1.0f / 1.3247179572447f;
            float phi2Squared = phi2 * phi2;
            float u = 0.5f;
            float v = 0.5f;
            int offsetIndex = 0;
            var offsets = new Vector2[k_NeighborOffsetCount];

            while (offsetIndex < offsets.Length)
            {
                u += phi2;
                v += phi2Squared;
                if (u >= 1.0f)
                    u -= 1.0f;
                if (v >= 1.0f)
                    v -= 1.0f;

                float centeredU = u - 0.5f;
                float centeredV = v - 0.5f;
                if (centeredU * centeredU + centeredV * centeredV > 0.25f)
                    continue;

                int quantizedU = (int)(centeredU * quantizationScale);
                int quantizedV = (int)(centeredV * quantizationScale);
                offsets[offsetIndex++] = new Vector2(quantizedU * snormScale, quantizedV * snormScale);
            }

            return offsets;
        }

        internal void Dispose()
        {
            m_NeighborOffsetsBuffer?.Release();
        }

        internal bool Setup(UniversalCameraData cameraData)
        {
            var stack = VolumeManager.instance.stack;
            m_VolumeSettings = stack.GetComponent<GlobalIllumination>();
            m_UseRayTracing = m_VolumeSettings != null && GlobalIllumination.RayTracingActive(m_VolumeSettings)
                && cameraData.supportedRayTracing && cameraData.rayTracingSystem != null
                && cameraData.rayTracingSystem.GetRayTracingState() && m_RayTracingShader != null;
            return m_ComputeShader != null
                && m_DenoiserCS != null
                && m_VolumeSettings != null
                && m_VolumeSettings.IsActive();
        }

        private static RTHandle HistoryReservoirTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem, string suffix, bool halfResolution)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(halfResolution ? s_TraceScaleFunc : s_FullResolutionScaleFunc,
                TextureXR.slices, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_ReSTIRGIReservoir{1}_{2}", viewName, suffix, frameIndex));
        }

        private static void ReAllocateReservoirHistoryTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, HistoryFrameType historyType, string suffix, bool halfResolution, bool forceReallocate, out RTHandle currentFrameRT, out RTHandle previousFrameRT)
        {
            var currentTexture = historyRTSystem.GetCurrentFrameRT(historyType);
            if (forceReallocate || currentTexture == null || !currentTexture.useScaling || currentTexture.scaleFactor != Vector2.zero)
            {
                historyRTSystem.ReleaseHistoryFrameRT(historyType);
                historyRTSystem.AllocHistoryFrameRT((int)historyType, cameraData.camera.name,
                    (graphicsFormat, viewName, frameIndex, rtHandleSystem) => HistoryReservoirTextureAllocator(graphicsFormat, viewName, frameIndex, rtHandleSystem, suffix, halfResolution),
                    GraphicsFormat.R32G32B32A32_UInt, 2);
            }

            currentFrameRT = historyRTSystem.GetCurrentFrameRT(historyType);
            previousFrameRT = historyRTSystem.GetPreviousFrameRT(historyType);
        }

        private static RTHandle HistoryDenoisedGITextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem, bool halfResolution)
        {
            frameIndex &= 1;
            return rtHandleSystem.Alloc(halfResolution ? s_TraceScaleFunc : s_FullResolutionScaleFunc,
                TextureXR.slices, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_ReSTIRGIDenoised_{1}", viewName, frameIndex));
        }

        private static void ReAllocateDenoisedGIHistoryTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, bool halfResolution, bool forceReallocate, out RTHandle currentFrameRT, out RTHandle previousFrameRT)
        {
            var historyType = HistoryFrameType.ReSTIRGIDenoised;
            var currentTexture = historyRTSystem.GetCurrentFrameRT(historyType);
            if (forceReallocate || currentTexture == null || !currentTexture.useScaling || currentTexture.scaleFactor != Vector2.zero)
            {
                historyRTSystem.ReleaseHistoryFrameRT(historyType);
                historyRTSystem.AllocHistoryFrameRT((int)historyType, cameraData.camera.name,
                    (graphicsFormat, viewName, frameIndex, rtHandleSystem) => HistoryDenoisedGITextureAllocator(graphicsFormat, viewName, frameIndex, rtHandleSystem, halfResolution),
                    GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currentFrameRT = historyRTSystem.GetCurrentFrameRT(historyType);
            previousFrameRT = historyRTSystem.GetPreviousFrameRT(historyType);
        }

        private static TextureHandle CreateReservoirResampleTexture(RenderGraph renderGraph, UniversalCameraData cameraData, int width, int height, string name)
        {
            TextureDesc desc = new TextureDesc(cameraData.cameraTargetDescriptor);
            desc.width = width;
            desc.height = height;
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            desc.enableRandomWrite = true;
            desc.filterMode = FilterMode.Point;
            desc.wrapMode = TextureWrapMode.Clamp;
            desc.colorFormat = GraphicsFormat.R32G32B32A32_UInt;
            // Every dispatched reservoir kernel writes every in-bounds pixel,
            // including background. Initializing a transient RT again is redundant.
            desc.clearBuffer = false;
            desc.clearColor = Color.clear;
            desc.name = name;
            return renderGraph.CreateTexture(desc);
        }

        private static TextureHandle CreateGlobalIlluminationTexture(RenderGraph renderGraph, UniversalCameraData cameraData, int width, int height, string name)
        {
            TextureDesc desc = new TextureDesc(cameraData.cameraTargetDescriptor);
            desc.width = width;
            desc.height = height;
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            desc.enableRandomWrite = true;
            desc.filterMode = FilterMode.Point;
            desc.wrapMode = TextureWrapMode.Clamp;
            desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            // Resolve / denoise / upsample all write background and invalid pixels.
            desc.clearBuffer = false;
            desc.clearColor = Color.clear;
            desc.name = name;
            return renderGraph.CreateTexture(desc);
        }

        private static RTHandle HistoryDenoisedGIMomentsTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem, bool halfResolution)
        {
            frameIndex &= 1;
            return rtHandleSystem.Alloc(halfResolution ? s_TraceScaleFunc : s_FullResolutionScaleFunc,
                TextureXR.slices, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_ReSTIRGIDenoisedMoments_{1}", viewName, frameIndex));
        }

        private static void ReAllocateDenoisedGIMomentsHistoryTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, bool halfResolution, bool forceReallocate, out RTHandle currentFrameRT, out RTHandle previousFrameRT)
        {
            var historyType = HistoryFrameType.ReSTIRGIMoments;
            var currentTexture = historyRTSystem.GetCurrentFrameRT(historyType);
            if (forceReallocate || currentTexture == null || !currentTexture.useScaling || currentTexture.scaleFactor != Vector2.zero)
            {
                historyRTSystem.ReleaseHistoryFrameRT(historyType);
                historyRTSystem.AllocHistoryFrameRT((int)historyType, cameraData.camera.name,
                    (graphicsFormat, viewName, frameIndex, rtHandleSystem) => HistoryDenoisedGIMomentsTextureAllocator(graphicsFormat, viewName, frameIndex, rtHandleSystem, halfResolution),
                    GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currentFrameRT = historyRTSystem.GetCurrentFrameRT(historyType);
            previousFrameRT = historyRTSystem.GetPreviousFrameRT(historyType);
        }

        private static bool IsTraceHistoryTextureValid(RTHandle texture)
        {
            return texture != null && texture.useScaling && texture.scaleFactor == Vector2.zero;
        }

        private class PassData
        {
            internal ComputeShader computeShader;
            internal RayTracingShader rayTracingShader;
            internal int temporalKernel;
            internal int spatialKernel;
            internal int copyKernel;
            internal int copyResolveKernel;
            internal int resolveKernel;
            internal int upsampleKernel;
            internal int screenSpaceKernel;
            internal ComputeShader denoiserCS;
            internal int denoiseTemporalKernel;
            internal int denoiseTemporalFinalKernel;
            internal int[] denoiseSpatialKernels;

            internal TextureHandle depthTexture;
            internal TextureHandle previousDepthTexture;
            internal TextureHandle motionVectorTexture;
            internal TextureHandle gbuffer2;
            internal TextureHandle blueNoiseRayTexture;
            internal TextureHandle depthPyramidTexture;
            internal BufferHandle depthPyramidOffsets;
            internal TextureHandle previousColorTexture;
            internal bool colorHistoryValid;
            internal bool useRayTracing;
            internal float rayLength;
            internal float clampValue;
            internal TextureHandle globalIlluminationTexture;
            internal TextureHandle traceGlobalIlluminationTexture;
            internal TextureHandle denoiseSpatialIntermediateTexture;
            internal TextureHandle currentDenoisedGIHistory;
            internal TextureHandle previousDenoisedGIHistory;
            internal TextureHandle currentDenoisedGIMomentsHistory;
            internal TextureHandle previousDenoisedGIMomentsHistory;
            internal BufferHandle ambientProbe;
            internal BufferHandle neighborOffsets;
            internal TextureHandle reflectProbe;

            internal TextureHandle currentReservoirData0;
            internal TextureHandle currentReservoirData1;
            internal TextureHandle currentReservoirData2;
            internal TextureHandle previousReservoirData0;
            internal TextureHandle previousReservoirData1;
            internal TextureHandle previousReservoirData2;
            internal TextureHandle resampleSpatialOutputReservoirData0;
            internal TextureHandle resampleSpatialOutputReservoirData1;
            internal TextureHandle resampleSpatialOutputReservoirData2;

            internal RayTracingAccelerationStructure rtas;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal ShaderVariablesScreenSpaceGlobalIllumination constantBuffer;

            internal int sourceWidth;
            internal int sourceHeight;
            internal int traceWidth;
            internal int traceHeight;
            internal int frameCount;
            internal bool halfResolution;
            internal bool enableTemporalReuse;
            internal bool enableSpatialReuse;
            internal bool enableDenoiser;
        internal bool enableDenoiseTemporal;
        internal bool enableDenoiseSpatial;
        internal float temporalDenoiseAccumulation;
        internal bool denoiserHistoryValid;
            internal Matrix4x4 clipToPrevClipMatrix;
        }

        private void InitPassData(RenderGraph renderGraph, PassData passData, ContextContainer frameData, HistoryFrameRTSystem historyRTSystem, int colorPyramidMipCount)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            int sourceWidth = cameraData.cameraTargetDescriptor.width;
            int sourceHeight = cameraData.cameraTargetDescriptor.height;
            bool halfResolution = m_VolumeSettings.restirHalfResolution.value;
            int traceWidth = halfResolution ? RenderingUtils.DivRoundUp(sourceWidth, 2) : sourceWidth;
            int traceHeight = halfResolution ? RenderingUtils.DivRoundUp(sourceHeight, 2) : sourceHeight;

            passData.computeShader = m_ComputeShader;
            passData.rayTracingShader = m_RayTracingShader;
            passData.temporalKernel = m_TemporalKernel;
            passData.spatialKernel = m_SpatialKernel;
            passData.copyKernel = m_CopyKernel;
            passData.copyResolveKernel = m_CopyResolveKernel;
            passData.resolveKernel = m_ResolveKernel;
            passData.upsampleKernel = m_UpsampleKernel;
            passData.screenSpaceKernel = m_ScreenSpaceKernel;
            passData.useRayTracing = m_UseRayTracing;
            passData.rayLength = m_VolumeSettings.rayLength;
            passData.clampValue = m_VolumeSettings.clampValue;
            passData.denoiserCS = m_DenoiserCS;
            passData.denoiseTemporalKernel = m_DenoiseTemporalKernel;
            passData.denoiseTemporalFinalKernel = m_DenoiseTemporalFinalKernel;
            passData.denoiseSpatialKernels = m_DenoiseSpatialKernels;
            passData.sourceWidth = sourceWidth;
            passData.sourceHeight = sourceHeight;
            passData.traceWidth = traceWidth;
            passData.traceHeight = traceHeight;
            passData.frameCount = historyRTSystem.historyFrameCount;
            passData.halfResolution = halfResolution;
            int cameraId = cameraData.camera.GetInstanceID();
            bool hasHistoryState = m_HistoryStates.TryGetValue(cameraId, out GIHistoryState historyState);
            bool historyResolutionChanged = !hasHistoryState || historyState.halfResolution != halfResolution;
            bool historyIsContinuous = hasHistoryState
                && historyState.frameCount + 1 == historyRTSystem.historyFrameCount
                && historyState.width == sourceWidth
                && historyState.height == sourceHeight
                && historyState.halfResolution == halfResolution
                && historyState.rayTracing == m_UseRayTracing
                && historyState.candidateCount == Mathf.Clamp(m_VolumeSettings.sampleCount.value, 1, 8);
            bool historyValid = historyIsContinuous
                && historyRTSystem.historyFrameCount > 1
                && !cameraData.resetHistory
                && IsTraceHistoryTextureValid(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ReSTIRGIReservoir0))
                && IsTraceHistoryTextureValid(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ReSTIRGIReservoir1))
                && IsTraceHistoryTextureValid(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ReSTIRGIReservoir2))
                && IsTraceHistoryTextureValid(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ReSTIRGIDenoised))
                && IsTraceHistoryTextureValid(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ReSTIRGIMoments));
            passData.enableTemporalReuse = m_VolumeSettings.restirTemporalReuse.value;
            passData.enableSpatialReuse = m_VolumeSettings.restirSpatialReuse.value;
        passData.enableDenoiseTemporal = m_VolumeSettings.temporalDenoise;
        passData.enableDenoiseSpatial = m_VolumeSettings.spatialDenoise;
        passData.temporalDenoiseAccumulation = m_VolumeSettings.temporalDenoiseAccumulation;
        passData.enableDenoiser = m_DenoiserCS != null && (passData.enableDenoiseTemporal || passData.enableDenoiseSpatial);
            passData.denoiserHistoryValid = historyValid && historyState.denoiserHistoryWritten;
            passData.depthTexture = resourceData.cameraDepthTexture;
            passData.motionVectorTexture = resourceData.motionVectorColor;
            passData.gbuffer2 = resourceData.gBuffer[2];
            passData.blueNoiseRayTexture = resourceData.blueNoiseUnitVec3Cosine;
            passData.ambientProbe = resourceData.skyAmbientProbe;
            passData.neighborOffsets = renderGraph.ImportBuffer(m_NeighborOffsetsBuffer, bufferName: "ReSTIR GI Neighbor Offsets");
            passData.reflectProbe = resourceData.skyReflectionProbe;

            var prevDepthRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.Depth);
            passData.previousDepthTexture = prevDepthRT != null
                ? renderGraph.ImportTexture(prevDepthRT)
                : resourceData.cameraDepthTexture;
            if (prevDepthRT == null)
            {
                historyValid = false;
                passData.denoiserHistoryValid = false;
            }

            if (!m_UseRayTracing)
            {
                passData.depthPyramidTexture = resourceData.cameraDepthPyramidTexture;
                passData.depthPyramidOffsets = resourceData.cameraDepthPyramidMipLevelOffsets;
                var previousColor = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain);
                passData.previousColorTexture = previousColor != null
                    ? renderGraph.ImportTexture(previousColor) : renderGraph.defaultResources.blackTexture;
                passData.colorHistoryValid = previousColor != null && prevDepthRT != null
                    && historyRTSystem.historyFrameCount > 1 && !cameraData.resetHistory
                    && historyRTSystem.rtHandleProperties.previousViewportSize == new Vector2Int(sourceWidth, sourceHeight);
                passData.constantBuffer._ColorPyramidUvScaleAndLimitPrevFrame = RenderingUtils.ComputeViewportScaleAndLimit(
                    historyRTSystem.rtHandleProperties.previousViewportSize, historyRTSystem.rtHandleProperties.previousRenderTargetSize);
                float thickness = m_VolumeSettings.depthBufferThickness.value;
                float thicknessScale = 1.0f / (1.0f + thickness);
                float n = cameraData.camera.nearClipPlane;
                float f = cameraData.camera.farClipPlane;
                passData.constantBuffer._RayMarchingThicknessScale = thicknessScale;
                passData.constantBuffer._RayMarchingThicknessBias = -n / (f - n) * thickness * thicknessScale;
                passData.constantBuffer._RayMarchingSteps = m_VolumeSettings.maxRaySteps;
                passData.constantBuffer._RayMarchingFallbackHierarchy = (int)m_VolumeSettings.rayMiss.value;
                passData.constantBuffer._SSGIDepthPyramidMaxMip = resourceData.cameraDepthPyramidInfo.mipLevelCount - 1;
                passData.constantBuffer._SSGIColorPyramidMaxMip = Mathf.Max(0, colorPyramidMipCount - 1);
            }

            ReAllocateReservoirHistoryTextureIfNeeded(historyRTSystem, cameraData, HistoryFrameType.ReSTIRGIReservoir0, "Data0", halfResolution, historyResolutionChanged, out var currentReservoir0, out var previousReservoir0);
            ReAllocateReservoirHistoryTextureIfNeeded(historyRTSystem, cameraData, HistoryFrameType.ReSTIRGIReservoir1, "Data1", halfResolution, historyResolutionChanged, out var currentReservoir1, out var previousReservoir1);
            ReAllocateReservoirHistoryTextureIfNeeded(historyRTSystem, cameraData, HistoryFrameType.ReSTIRGIReservoir2, "Data2", halfResolution, historyResolutionChanged, out var currentReservoir2, out var previousReservoir2);

            passData.currentReservoirData0 = renderGraph.ImportTexture(currentReservoir0);
            passData.currentReservoirData1 = renderGraph.ImportTexture(currentReservoir1);
            passData.currentReservoirData2 = renderGraph.ImportTexture(currentReservoir2);
            passData.previousReservoirData0 = renderGraph.ImportTexture(previousReservoir0);
            passData.previousReservoirData1 = renderGraph.ImportTexture(previousReservoir1);
            passData.previousReservoirData2 = renderGraph.ImportTexture(previousReservoir2);

            passData.resampleSpatialOutputReservoirData0 = CreateReservoirResampleTexture(renderGraph, cameraData, traceWidth, traceHeight, "_ReSTIRGIResampleSpatialOutput0");
            passData.resampleSpatialOutputReservoirData1 = CreateReservoirResampleTexture(renderGraph, cameraData, traceWidth, traceHeight, "_ReSTIRGIResampleSpatialOutput1");
            passData.resampleSpatialOutputReservoirData2 = CreateReservoirResampleTexture(renderGraph, cameraData, traceWidth, traceHeight, "_ReSTIRGIResampleSpatialOutput2");
            passData.traceGlobalIlluminationTexture = CreateGlobalIlluminationTexture(renderGraph, cameraData, traceWidth, traceHeight, "_ReSTIRGITraceTexture");
            passData.denoiseSpatialIntermediateTexture = passData.enableDenoiseSpatial
                ? CreateGlobalIlluminationTexture(renderGraph, cameraData, traceWidth, traceHeight, "_ReSTIRGIDenoiseSpatialIntermediate")
                : TextureHandle.nullHandle;

            ReAllocateDenoisedGIHistoryTextureIfNeeded(historyRTSystem, cameraData, halfResolution, historyResolutionChanged, out var currentDenoisedGIHistory, out var previousDenoisedGIHistory);
            passData.currentDenoisedGIHistory = renderGraph.ImportTexture(currentDenoisedGIHistory);
            passData.previousDenoisedGIHistory = renderGraph.ImportTexture(previousDenoisedGIHistory);

            ReAllocateDenoisedGIMomentsHistoryTextureIfNeeded(historyRTSystem, cameraData, halfResolution, historyResolutionChanged, out var currentDenoisedGIMoments, out var previousDenoisedGIMoments);
            passData.currentDenoisedGIMomentsHistory = renderGraph.ImportTexture(currentDenoisedGIMoments);
            passData.previousDenoisedGIMomentsHistory = renderGraph.ImportTexture(previousDenoisedGIMoments);

            // At full resolution publish the last producer directly, rather than
            // allocating and copying another full-screen GI texture.
            TextureHandle denoisedOutput = passData.traceGlobalIlluminationTexture;
            if (passData.enableDenoiser)
            {
                denoisedOutput = passData.currentDenoisedGIHistory;
                if (passData.enableDenoiseSpatial)
                    denoisedOutput = (s_ATrousStepSizes.Length & 1) == 0
                        ? passData.denoiseSpatialIntermediateTexture : passData.traceGlobalIlluminationTexture;
            }
            passData.globalIlluminationTexture = halfResolution
                ? CreateGlobalIlluminationTexture(renderGraph, cameraData, sourceWidth, sourceHeight, "_GlobalIlluminationTexture")
                : denoisedOutput;

            passData.clipToPrevClipMatrix = Matrix4x4.identity;
            MotionVectorsPersistentData motionData = null;
            if (cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                motionData = additionalCameraData.motionVectorsPersistentData;
            if (motionData != null)
                passData.clipToPrevClipMatrix = motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection);
            else
            {
                historyValid = false;
                passData.denoiserHistoryValid = false;
            }

            if (m_UseRayTracing)
            {
                var stack = VolumeManager.instance.stack;
                var rayTracingSettings = stack.GetComponent<RayTracingSettings>();
                passData.rtas = cameraData.rayTracingSystem.RequestAccelerationStructure();
                passData.rayTracingCB = cameraData.rayTracingSystem.GetShaderVariablesRaytracingCB(new Vector2Int(sourceWidth, sourceHeight), rayTracingSettings);
                passData.rayTracingCB._RaytracingRayMaxLength = m_VolumeSettings.rayLength;
                passData.rayTracingCB._RaytracingNumSamples = Mathf.Clamp(m_VolumeSettings.sampleCount.value, 1, 8);
                passData.rayTracingCB._RaytracingSampleIndex = 0;
                passData.rayTracingCB._RaytracingMaxRecursion = m_VolumeSettings.bounceCount.value;
                passData.rayTracingCB._RayTracingClampingFlag = 1;
                passData.rayTracingCB._RaytracingIntensityClamp = m_VolumeSettings.clampValue;
                passData.rayTracingCB._RaytracingPreExposition = 0;
                passData.rayTracingCB._RayTracingDiffuseLightingOnly = 1;
                passData.rayTracingCB._RayTracingLodBias = m_VolumeSettings.textureLodBias.value;
                passData.rayTracingCB._RayTracingAPVRayMiss = 1;
                passData.rayTracingCB._RayTracingRayMissFallbackHierarchy = (int)m_VolumeSettings.rayMiss.value;
                passData.rayTracingCB._RayTracingRayMissUseAmbientProbeAsSky = 1;
                passData.rayTracingCB._RayTracingLastBounceFallbackHierarchy = (int)m_VolumeSettings.lastBounceFallbackHierarchy.value;
                passData.rayTracingCB._RayTracingAmbientProbeDimmer = m_VolumeSettings.ambientProbeDimmer.value;
                passData.rayTracingCB._RayTracingReflectionFrameIndex = historyRTSystem.historyFrameCount;
            }

            passData.constantBuffer._SSGITraceScreenSize = new Vector4(traceWidth, traceHeight, 1.0f / traceWidth, 1.0f / traceHeight);
            passData.constantBuffer._SSGISourceSize = new Vector4(sourceWidth, sourceHeight, 1.0f / sourceWidth, 1.0f / sourceHeight);
            passData.constantBuffer._SSGIOutputSize = new Vector4(sourceWidth, sourceHeight, 1.0f / sourceWidth, 1.0f / sourceHeight);
            passData.constantBuffer._SSGIIntensity = m_VolumeSettings.restirIntensity.value;
            passData.constantBuffer._SSGIDepthTolerance = m_VolumeSettings.restirDepthThreshold.value;
            passData.constantBuffer._SSGINormalTolerance = m_VolumeSettings.restirNormalThreshold.value;
            // SDK's single-candidate stream has M=1. Our initial RIS has M=N;
            // keep the history/current ratio in frame units as N is adjusted.
            passData.constantBuffer._SSGITemporalMaxSamples = m_VolumeSettings.restirMaxHistoryLength.value
                * Mathf.Clamp(m_VolumeSettings.sampleCount.value, 1, 8);
            passData.constantBuffer._SSGIMaxSampleAge = m_VolumeSettings.restirMaxSampleAge.value;
            passData.constantBuffer._SSGISpatialRadius = halfResolution
                ? m_VolumeSettings.restirHalfSpatialRadius.value : m_VolumeSettings.restirSpatialRadius.value;
            passData.constantBuffer._SSGISpatialSampleCount = halfResolution
                ? m_VolumeSettings.restirHalfSpatialSamples.value : m_VolumeSettings.restirSpatialSamples.value;
            passData.constantBuffer._SSGICandidateCount = Mathf.Clamp(m_VolumeSettings.sampleCount.value, 1, 8);
            passData.constantBuffer._SSGIFrameIndex = historyRTSystem.historyFrameCount;
            passData.constantBuffer._SSGIEnableTemporalJacobian = m_VolumeSettings.restirTemporalJacobian.value ? 1.0f : 0.0f;
            passData.constantBuffer._SSGIEnableSpatialJacobian = m_VolumeSettings.restirSpatialJacobian.value ? 1.0f : 0.0f;
            passData.constantBuffer._SSGIHistoryValid = historyValid ? 1 : 0;

            m_HistoryStates[cameraId] = new GIHistoryState
            {
                frameCount = historyRTSystem.historyFrameCount,
                width = sourceWidth,
                height = sourceHeight,
                halfResolution = halfResolution,
                rayTracing = m_UseRayTracing,
                candidateCount = Mathf.Clamp(m_VolumeSettings.sampleCount.value, 1, 8),
                denoiserHistoryWritten = passData.enableDenoiser
            };
        }

        private static void BindRayTracingReservoirOutput(PassData data, ComputeCommandBuffer cmd)
        {
            cmd.SetRayTracingTextureParam(data.rayTracingShader, ShaderConstants._OutputReservoirData0RW, data.currentReservoirData0);
            cmd.SetRayTracingTextureParam(data.rayTracingShader, ShaderConstants._OutputReservoirData1RW, data.currentReservoirData1);
            cmd.SetRayTracingTextureParam(data.rayTracingShader, ShaderConstants._OutputReservoirData2RW, data.currentReservoirData2);
        }

        private static void BindTemporalReservoirTextures(PassData data, ComputeCommandBuffer cmd, TextureHandle output0, TextureHandle output1, TextureHandle output2)
        {
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._CurrentReservoirData0, data.currentReservoirData0);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._CurrentReservoirData1, data.currentReservoirData1);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._CurrentReservoirData2, data.currentReservoirData2);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._PreviousReservoirData0, data.previousReservoirData0);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._PreviousReservoirData1, data.previousReservoirData1);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._PreviousReservoirData2, data.previousReservoirData2);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._OutputReservoirData0RW, output0);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._OutputReservoirData1RW, output1);
            cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._OutputReservoirData2RW, output2);
        }

        private static void BindSpatialInputTextures(PassData data, ComputeCommandBuffer cmd, TextureHandle input1, TextureHandle input2)
        {
            cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._InputReservoirData1, input1);
            cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._InputReservoirData2, input2);
        }

        private static void BindSpatialOutputTextures(PassData data, ComputeCommandBuffer cmd, TextureHandle output0, TextureHandle output1, TextureHandle output2)
        {
            cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._OutputReservoirData0RW, output0);
            cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._OutputReservoirData1RW, output1);
            cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._OutputReservoirData2RW, output2);
        }

        private static void CopyReservoir(PassData data, ComputeCommandBuffer cmd,
            TextureHandle input0, TextureHandle input1, TextureHandle input2,
            TextureHandle output0, TextureHandle output1, TextureHandle output2, bool resolveGI)
        {
            int kernel = resolveGI ? data.copyResolveKernel : data.copyKernel;
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._InputReservoirData0, input0);
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._InputReservoirData1, input1);
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._InputReservoirData2, input2);
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData0RW, output0);
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData1RW, output1);
            cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData2RW, output2);
            if (resolveGI)
            {
                cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._GBuffer2, data.gbuffer2);
                cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._GlobalIlluminationTextureRW, data.traceGlobalIlluminationTexture);
            }
            cmd.DispatchCompute(data.computeShader, kernel,
                RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
        }

        private static void BindResolveTextures(PassData data, ComputeCommandBuffer cmd, TextureHandle input0, TextureHandle input1, TextureHandle input2, TextureHandle outputGI)
        {
            cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._InputReservoirData0, input0);
            cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._InputReservoirData1, input1);
            cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._InputReservoirData2, input2);
            cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._GlobalIlluminationTextureRW, outputGI);
        }

        private static void BindDenoiseTemporalTextures(PassData data, ComputeCommandBuffer cmd, int temporalKernel,
            TextureHandle inputGI, TextureHandle outputHistoryGI)
        {
            cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._InputGITexture, inputGI);
            cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._PrevDenoisedGITexture, data.previousDenoisedGIHistory);
            cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._PrevMomentsTexture, data.previousDenoisedGIMomentsHistory);
            cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._OutputGITextureRW, outputHistoryGI);
            cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._OutputMomentsTextureRW, data.currentDenoisedGIMomentsHistory);
        }

        private static void BindDenoiseSpatialTextures(PassData data, ComputeCommandBuffer cmd, int kernel, TextureHandle inputGI, TextureHandle outputGI)
        {
            cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._InputGITexture, inputGI);
            cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._InputMomentsTexture, data.currentDenoisedGIMomentsHistory);
            cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._OutputGITextureRW, outputGI);
        }

        private static void ExecutePass(PassData data, ComputeCommandBuffer cmd)
        {
            ConstantBuffer.Push(cmd, data.constantBuffer, data.computeShader, s_ShaderVariablesGlobalIllumination);
            if (data.useRayTracing)
            {
                using (new ProfilingScope(cmd, s_RayTracingSampler))
                {
                    cmd.SetRayTracingShaderPass(data.rayTracingShader, "IndirectDXR");
                    cmd.SetGlobalBuffer(ShaderConstants._AmbientProbeData, data.ambientProbe);
                    cmd.SetGlobalTexture(ShaderConstants._SkyTexture, data.reflectProbe);
                    cmd.SetRayTracingAccelerationStructure(data.rayTracingShader, "_RaytracingAccelerationStructure", data.rtas);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, ShaderConstants._CameraDepthTexture, data.depthTexture);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, ShaderConstants._GBuffer2, data.gbuffer2);
                    BindRayTracingReservoirOutput(data, cmd);
                    ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);
                    ConstantBuffer.PushGlobal(cmd, data.constantBuffer, s_ShaderVariablesGlobalIllumination);
                    BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._UnitVec3_Cosine, cmd, data.rayTracingShader, data.blueNoiseRayTexture, data.frameCount);
                    cmd.DispatchRays(data.rayTracingShader, "SingleRayGen", (uint)data.traceWidth, (uint)data.traceHeight, 1);
                }

            }
            else
            {
                using (new ProfilingScope(cmd, s_ScreenSpaceSampler))
                {
                    int kernel = data.screenSpaceKernel;
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._GBuffer2, data.gbuffer2);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._CameraDepthPyramidTexture, data.depthPyramidTexture);
                    cmd.SetComputeBufferParam(data.computeShader, kernel, ShaderConstants._DepthPyramidMipLevelOffsets, data.depthPyramidOffsets);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._ColorPyramidTexture, data.previousColorTexture);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._PrevCameraDepthTexture, data.previousDepthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._SSGISkyTexture, data.reflectProbe);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData0RW, data.currentReservoirData0);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData1RW, data.currentReservoirData1);
                    cmd.SetComputeTextureParam(data.computeShader, kernel, ShaderConstants._OutputReservoirData2RW, data.currentReservoirData2);
                    cmd.SetComputeFloatParam(data.computeShader, ShaderConstants._SSGIRayLength, data.rayLength);
                    cmd.SetComputeFloatParam(data.computeShader, ShaderConstants._SSGIClampValue, data.clampValue);
                    cmd.SetComputeIntParam(data.computeShader, ShaderConstants._SSGIColorHistoryValid, data.colorHistoryValid ? 1 : 0);
                    cmd.SetComputeMatrixParam(data.computeShader, ShaderConstants._ClipToPrevClipMatrix, data.clipToPrevClipMatrix);
                    BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._UnitVec3_Cosine, cmd, data.computeShader, kernel, data.blueNoiseRayTexture, data.frameCount);
                    cmd.DispatchCompute(data.computeShader, kernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                }
            }

            TextureHandle resampleTemporalOutput0 = data.currentReservoirData0;
            TextureHandle resampleTemporalOutput1 = data.currentReservoirData1;
            TextureHandle resampleTemporalOutput2 = data.currentReservoirData2;

            if (data.enableTemporalReuse)
            {
                using (new ProfilingScope(cmd, s_TemporalSampler))
                {
                    cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._PrevCameraDepthTexture, data.previousDepthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                    cmd.SetComputeTextureParam(data.computeShader, data.temporalKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                    cmd.SetComputeMatrixParam(data.computeShader, ShaderConstants._ClipToPrevClipMatrix, data.clipToPrevClipMatrix);
                    BindTemporalReservoirTextures(data, cmd,
                        data.resampleSpatialOutputReservoirData0,
                        data.resampleSpatialOutputReservoirData1,
                        data.resampleSpatialOutputReservoirData2);
                    cmd.DispatchCompute(data.computeShader, data.temporalKernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                }

                resampleTemporalOutput0 = data.resampleSpatialOutputReservoirData0;
                resampleTemporalOutput1 = data.resampleSpatialOutputReservoirData1;
                resampleTemporalOutput2 = data.resampleSpatialOutputReservoirData2;
            }

            if (data.enableSpatialReuse)
            {
                TextureHandle spatialInput1 = resampleTemporalOutput1;
                TextureHandle spatialInput2 = resampleTemporalOutput2;
                TextureHandle resampleSpatialOutput0 = data.enableTemporalReuse
                    ? data.currentReservoirData0 : data.resampleSpatialOutputReservoirData0;
                TextureHandle resampleSpatialOutput1 = data.enableTemporalReuse
                    ? data.currentReservoirData1 : data.resampleSpatialOutputReservoirData1;
                TextureHandle resampleSpatialOutput2 = data.enableTemporalReuse
                    ? data.currentReservoirData2 : data.resampleSpatialOutputReservoirData2;

                using (new ProfilingScope(cmd, s_SpatialSampler))
                {
                    cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                    cmd.SetComputeBufferParam(data.computeShader, data.spatialKernel, ShaderConstants._ReSTIRGINeighborOffsets, data.neighborOffsets);
                    BindSpatialInputTextures(data, cmd, spatialInput1, spatialInput2);
                    BindSpatialOutputTextures(data, cmd, resampleSpatialOutput0, resampleSpatialOutput1, resampleSpatialOutput2);
                    cmd.SetComputeTextureParam(data.computeShader, data.spatialKernel,
                        ShaderConstants._GlobalIlluminationTextureRW, data.traceGlobalIlluminationTexture);
                    cmd.DispatchCompute(data.computeShader, data.spatialKernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                }

                resampleTemporalOutput0 = resampleSpatialOutput0;
                resampleTemporalOutput1 = resampleSpatialOutput1;
                resampleTemporalOutput2 = resampleSpatialOutput2;
            }

            // The current history must contain the final reservoir, while no compute
            // kernel may bind the same texture simultaneously as SRV and UAV.
            bool finalReservoirIsCurrent = resampleTemporalOutput0.Equals(data.currentReservoirData0);
            if (!finalReservoirIsCurrent)
            {
                using (new ProfilingScope(cmd, data.enableSpatialReuse ? s_CopySampler : s_ResolveSampler))
                {
                    CopyReservoir(data, cmd,
                        resampleTemporalOutput0, resampleTemporalOutput1, resampleTemporalOutput2,
                        data.currentReservoirData0, data.currentReservoirData1, data.currentReservoirData2,
                        resolveGI: !data.enableSpatialReuse);
                }
            }

            if (!data.enableSpatialReuse && finalReservoirIsCurrent)
            {
                using (new ProfilingScope(cmd, s_ResolveSampler))
                {
                    cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                    cmd.SetComputeTextureParam(data.computeShader, data.resolveKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                    BindResolveTextures(data, cmd,
                        data.currentReservoirData0, data.currentReservoirData1, data.currentReservoirData2,
                        data.traceGlobalIlluminationTexture);
                    cmd.DispatchCompute(data.computeShader, data.resolveKernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                }
            }

            TextureHandle upsampleInput = data.traceGlobalIlluminationTexture;
            if (data.enableDenoiser)
            {
                ConstantBuffer.Push(cmd, data.constantBuffer, data.denoiserCS, s_ShaderVariablesGlobalIllumination);
                bool runTemporalPass = data.enableDenoiseTemporal || data.enableDenoiseSpatial;
                TextureHandle denoiseInput = data.traceGlobalIlluminationTexture;

                if (runTemporalPass)
                {
                    using (new ProfilingScope(cmd, s_DenoiseTemporalSampler))
                    {
                        int temporalKernel = data.denoiseTemporalKernel;
                        TextureHandle temporalOutput = data.currentDenoisedGIHistory;
                        float temporalAccumulation = data.enableDenoiseTemporal ? data.temporalDenoiseAccumulation : 0.0f;
                        cmd.SetComputeFloatParam(data.denoiserCS, ShaderConstants._GITemporalAccumulation, temporalAccumulation);
                        cmd.SetComputeIntParam(data.denoiserCS, ShaderConstants._GIDenoiseHistoryValid, data.denoiserHistoryValid ? 1 : 0);
                        cmd.SetComputeMatrixParam(data.denoiserCS, ShaderConstants._ClipToPrevClipMatrix, data.clipToPrevClipMatrix);
                        cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                        cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._PrevCameraDepthTexture, data.previousDepthTexture);
                        cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                        cmd.SetComputeTextureParam(data.denoiserCS, temporalKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                        BindDenoiseTemporalTextures(data, cmd, temporalKernel, denoiseInput, temporalOutput);
                        cmd.DispatchCompute(data.denoiserCS, temporalKernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                    }

                    denoiseInput = data.currentDenoisedGIHistory;
                }

                if (data.enableDenoiseSpatial)
                {
                    TextureHandle spatialInput = denoiseInput;
                    using (new ProfilingScope(cmd, s_DenoiseSpatialSampler))
                    {
                        // One dispatch performs one 3x3 dilated A-Trous iteration. Increasing the
                        // dilation between ping-pong iterations expands the filter footprint.
                        for (int passIndex = 0; passIndex < s_ATrousStepSizes.Length; ++passIndex)
                        {
                            int spatialKernel = data.denoiseSpatialKernels[passIndex];
                            TextureHandle spatialOutput = (passIndex & 1) == 0
                                ? data.traceGlobalIlluminationTexture
                                : data.denoiseSpatialIntermediateTexture;
                            cmd.SetComputeTextureParam(data.denoiserCS, spatialKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                            cmd.SetComputeTextureParam(data.denoiserCS, spatialKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                            BindDenoiseSpatialTextures(data, cmd, spatialKernel, spatialInput, spatialOutput);
                            cmd.DispatchCompute(data.denoiserCS, spatialKernel, RenderingUtils.DivRoundUp(data.traceWidth, 8), RenderingUtils.DivRoundUp(data.traceHeight, 8), 1);
                            spatialInput = spatialOutput;
                        }
                    }

                    denoiseInput = spatialInput;
                }

                upsampleInput = denoiseInput;
            }

            if (data.halfResolution)
            using (new ProfilingScope(cmd, s_UpsampleSampler))
            {
                cmd.SetComputeTextureParam(data.computeShader, data.upsampleKernel, ShaderConstants._CameraDepthTexture, data.depthTexture);
                cmd.SetComputeTextureParam(data.computeShader, data.upsampleKernel, ShaderConstants._GBuffer2, data.gbuffer2);
                cmd.SetComputeTextureParam(data.computeShader, data.upsampleKernel, ShaderConstants._InputGITexture, upsampleInput);
                cmd.SetComputeTextureParam(data.computeShader, data.upsampleKernel, ShaderConstants._GlobalIlluminationTextureRW, data.globalIlluminationTexture);
                cmd.DispatchCompute(data.computeShader, data.upsampleKernel,
                    RenderingUtils.DivRoundUp(data.sourceWidth, 8), RenderingUtils.DivRoundUp(data.sourceHeight, 8), 1);
            }
        }

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, int colorPyramidMipCount)
        {
            var cameraData = frameData.Get<UniversalCameraData>();

            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            if (historyRTSystem == null)
                return TextureHandle.nullHandle;

            using (var builder = renderGraph.AddComputePass("Render ReSTIR GI", out PassData passData, s_ProfilingSampler))
            {
                InitPassData(renderGraph, passData, frameData, historyRTSystem, colorPyramidMipCount);
                TextureHandle outputTexture = passData.globalIlluminationTexture;

                builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                builder.UseTexture(passData.previousDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.motionVectorTexture, AccessFlags.Read);
                builder.UseTexture(passData.gbuffer2, AccessFlags.Read);
                builder.UseTexture(passData.blueNoiseRayTexture, AccessFlags.Read);
                if (!passData.useRayTracing)
                {
                    builder.UseTexture(passData.depthPyramidTexture, AccessFlags.Read);
                    builder.UseBuffer(passData.depthPyramidOffsets, AccessFlags.Read);
                    builder.UseTexture(passData.previousColorTexture, AccessFlags.Read);
                }
                if (passData.halfResolution)
                    builder.UseTexture(passData.globalIlluminationTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.traceGlobalIlluminationTexture, AccessFlags.ReadWrite);
                if (passData.enableDenoiseSpatial)
                    builder.UseTexture(passData.denoiseSpatialIntermediateTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.currentDenoisedGIHistory, AccessFlags.ReadWrite);
                builder.UseTexture(passData.previousDenoisedGIHistory, AccessFlags.Read);
                builder.UseTexture(passData.currentDenoisedGIMomentsHistory, AccessFlags.ReadWrite);
                builder.UseTexture(passData.previousDenoisedGIMomentsHistory, AccessFlags.Read);
                builder.UseTexture(passData.currentReservoirData0, AccessFlags.ReadWrite);
                builder.UseTexture(passData.currentReservoirData1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.currentReservoirData2, AccessFlags.ReadWrite);
                builder.UseTexture(passData.previousReservoirData0, AccessFlags.Read);
                builder.UseTexture(passData.previousReservoirData1, AccessFlags.Read);
                builder.UseTexture(passData.previousReservoirData2, AccessFlags.Read);
                builder.UseTexture(passData.resampleSpatialOutputReservoirData0, AccessFlags.ReadWrite);
                builder.UseTexture(passData.resampleSpatialOutputReservoirData1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.resampleSpatialOutputReservoirData2, AccessFlags.ReadWrite);
                if (passData.useRayTracing)
                    builder.UseBuffer(passData.ambientProbe, AccessFlags.Read);
                if (passData.enableSpatialReuse)
                    builder.UseBuffer(passData.neighborOffsets, AccessFlags.Read);
                builder.UseTexture(passData.reflectProbe, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(outputTexture, ShaderConstants._GlobalIlluminationTexture);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context.cmd);
                });

                return outputTexture;
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));

            cmd.SetKeyword(ShaderGlobalKeywords.ScreenSpaceGlobalIllumination, false);
        }

        private static class ShaderConstants
        {
            public static readonly int _CameraDepthPyramidTexture = Shader.PropertyToID("_CameraDepthPyramidTexture");
            public static readonly int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
            public static readonly int _ColorPyramidTexture = Shader.PropertyToID("_ColorPyramidTexture");
            public static readonly int _SSGISkyTexture = Shader.PropertyToID("_SSGISkyTexture");
            public static readonly int _SSGIRayLength = Shader.PropertyToID("_SSGIRayLength");
            public static readonly int _SSGIClampValue = Shader.PropertyToID("_SSGIClampValue");
            public static readonly int _SSGIColorHistoryValid = Shader.PropertyToID("_SSGIColorHistoryValid");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _PrevCameraDepthTexture = Shader.PropertyToID("_PrevCameraDepthTexture");
            public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
            public static readonly int _CurrentReservoirData0 = Shader.PropertyToID("_CurrentReservoirData0");
            public static readonly int _CurrentReservoirData1 = Shader.PropertyToID("_CurrentReservoirData1");
            public static readonly int _CurrentReservoirData2 = Shader.PropertyToID("_CurrentReservoirData2");
            public static readonly int _PreviousReservoirData0 = Shader.PropertyToID("_PreviousReservoirData0");
            public static readonly int _PreviousReservoirData1 = Shader.PropertyToID("_PreviousReservoirData1");
            public static readonly int _PreviousReservoirData2 = Shader.PropertyToID("_PreviousReservoirData2");
            public static readonly int _InputReservoirData0 = Shader.PropertyToID("_InputReservoirData0");
            public static readonly int _InputReservoirData1 = Shader.PropertyToID("_InputReservoirData1");
            public static readonly int _InputReservoirData2 = Shader.PropertyToID("_InputReservoirData2");
            public static readonly int _OutputReservoirData0RW = Shader.PropertyToID("_OutputReservoirData0RW");
            public static readonly int _OutputReservoirData1RW = Shader.PropertyToID("_OutputReservoirData1RW");
            public static readonly int _OutputReservoirData2RW = Shader.PropertyToID("_OutputReservoirData2RW");
            public static readonly int _GlobalIlluminationTexture = Shader.PropertyToID("_GlobalIlluminationTexture");
            public static readonly int _GlobalIlluminationTextureRW = Shader.PropertyToID("_GlobalIlluminationTextureRW");
            public static readonly int _InputGITexture = Shader.PropertyToID("_InputGITexture");
            public static readonly int _PrevDenoisedGITexture = Shader.PropertyToID("_PrevDenoisedGITexture");
            public static readonly int _PrevMomentsTexture = Shader.PropertyToID("_PrevMomentsTexture");
            public static readonly int _InputMomentsTexture = Shader.PropertyToID("_InputMomentsTexture");
            public static readonly int _OutputGITextureRW = Shader.PropertyToID("_OutputGITextureRW");
            public static readonly int _OutputFinalGITextureRW = Shader.PropertyToID("_OutputFinalGITextureRW");
            public static readonly int _OutputMomentsTextureRW = Shader.PropertyToID("_OutputMomentsTextureRW");
            public static readonly int _GITemporalAccumulation = Shader.PropertyToID("_GITemporalAccumulation");
            public static readonly int _GIDenoiseHistoryValid = Shader.PropertyToID("_GIDenoiseHistoryValid");
            public static readonly int _ClipToPrevClipMatrix = Shader.PropertyToID("_ClipToPrevClipMatrix");
            public static readonly int _AmbientProbeData = Shader.PropertyToID("_AmbientProbeData");
            public static readonly int _ReSTIRGINeighborOffsets = Shader.PropertyToID("_ReSTIRGINeighborOffsets");
            public static readonly int _SkyTexture = Shader.PropertyToID("_SkyTexture");
        }
    }

}
