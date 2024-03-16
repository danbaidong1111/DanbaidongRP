using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSRTracingProfilerTag = "SSRTracing";
        private static string m_SSRResolveProfilerTag = "SSRResolve";
        private static string m_SSRAccumulateProfilerTag = "SSRAccumulate";
        private static string m_SSRBilateralProfilerTag = "SSRBilateral";
        private static string m_SSRATrousProfilerTag = "SSRATrous";
        private static ProfilingSampler m_SSRTracingProfilingSampler = new ProfilingSampler(m_SSRTracingProfilerTag);
        private static ProfilingSampler m_SSRResolveProfilingSampler = new ProfilingSampler(m_SSRResolveProfilerTag);
        private static ProfilingSampler m_SSRAccumulateProfilingSampler = new ProfilingSampler(m_SSRAccumulateProfilerTag);
        private static ProfilingSampler m_SSRBilateralProfilingSampler = new ProfilingSampler(m_SSRBilateralProfilerTag);
        private static ProfilingSampler m_SSRATrousProfilingSampler = new ProfilingSampler(m_SSRATrousProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;
        private ScreenSpaceReflectionSettings m_CurrentSettings;
        private int m_BlueNoiseTexArrayIndex;

        private RTHandle m_SSRHitPointTexture;
        private RTHandle m_SSRRayInfoTexture;
        private RTHandle m_SSRAvgRadianceTexture;
        private RTHandle m_SSRHitDepthTexture;
        private RTHandle m_SSRResolveVarianceTexture;
        private RTHandle m_SSRLightingTexture;

        private int m_SSRTracingKernel;
        private int m_SSRResolveKernel;
        private int m_SSRAccumulateKernel;
        private int m_SSRBilateralKernel;
        private int m_SSRATrousKernel;
        private ScreenSpaceReflection m_volumeSettings;
        private UniversalRenderer m_Renderer;
        private HistoryFrameRTSystem m_CurCameraHistoryRTSystem;

        private bool traceDownSample = false;

        // Constants

        // Statics
        private static Vector2 s_accumulateTextureScaleFactor = Vector2.one;

        internal ScreenSpaceReflectionPass(ComputeShader computeShader)
        {
            m_CurrentSettings = new ScreenSpaceReflectionSettings();
            m_Compute = computeShader;

            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
            m_SSRResolveKernel = m_Compute.FindKernel("ScreenSpaceReflectionsResolve");
            m_SSRAccumulateKernel = m_Compute.FindKernel("ScreenSpaceReflectionsAccumulate");
            m_SSRBilateralKernel = m_Compute.FindKernel("ScreenSpaceReflectionsBilateral");
            m_SSRATrousKernel = m_Compute.FindKernel("ScreenSpaceReflectionsATrous");

            m_BlueNoiseTexArrayIndex = 0;
        }

        /// <summary>
        /// Setup controls per frame shouldEnqueue this pass.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal bool Setup(ScreenSpaceReflectionSettings settings, UniversalRenderer renderer)
        {
            m_CurrentSettings = settings;
            m_Renderer = renderer;

            var stack = VolumeManager.instance.stack;
            m_volumeSettings = stack.GetComponent<ScreenSpaceReflection>();

            // Let renderer know we need history color.
            ConfigureInput(ScriptableRenderPassInput.HistoryColor | ScriptableRenderPassInput.Motion);

            return m_Compute != null && m_volumeSettings != null && m_volumeSettings.IsActive();
        }

        static RTHandle HistoryAccumulateTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedAccumulateTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, CameraData cameraData)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionAccumulation, cameraData.camera.name
                                                            , HistoryAccumulateTextureAllocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

        }

        static RTHandle HistoryNumFramesAccumTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRNumFramesAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedNumFramesAccumTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, CameraData cameraData)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation, cameraData.camera.name
                                                            , HistoryNumFramesAccumTextureAllocator, GraphicsFormat.R8_UNorm, 2);
            }
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_CurCameraHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(renderingData.cameraData.camera);
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16_UNorm;
            desc.enableRandomWrite = true;

            var hitDesc = desc;
            if (traceDownSample)
            {
                hitDesc.width /= 2;
                hitDesc.height /= 2;
            }
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRHitPointTexture, hitDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSRHitPointTexture");

            var lightingDesc = desc;
            lightingDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRLightingTexture, lightingDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSRLightingTexture");

            var rayInfoDesc = desc;
            rayInfoDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRRayInfoTexture, rayInfoDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSRRayInfoTexture");

            var avgRadianceDesc = desc;
            avgRadianceDesc.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            avgRadianceDesc.width = RenderingUtils.DivRoundUp(desc.width, 8);
            avgRadianceDesc.height = RenderingUtils.DivRoundUp(desc.height, 8);
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRAvgRadianceTexture, avgRadianceDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSRAvgRadianceTexture");

            var hitDepthDesc = desc;
            hitDepthDesc.graphicsFormat = GraphicsFormat.R16_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRHitDepthTexture, hitDepthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSRHitDepthTexture");

            var resolveVarianceDesc = desc;
            resolveVarianceDesc.graphicsFormat = GraphicsFormat.R16_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRResolveVarianceTexture, resolveVarianceDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSRResolveVarianceTexture");
            
            if (m_volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
            {
                ReAllocatedAccumulateTextureIfNeeded(m_CurCameraHistoryRTSystem, renderingData.cameraData);
                ReAllocatedNumFramesAccumTextureIfNeeded(m_CurCameraHistoryRTSystem, renderingData.cameraData);
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Compute == null)
            {
                Debug.LogErrorFormat("{0}.Execute(): Missing computeShader. ScreenSpaceReflection pass will not execute.", GetType().Name);
                return;
            }

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.SSR)))
            {

                // Default Approximation we use lighingTexture as SSRResolve handle.
                var SSRResolveHandle = m_SSRLightingTexture;
                var currAccumHandle = m_CurCameraHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
                var prevAccumHandle = m_CurCameraHistoryRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

                if (m_volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.Approximation)
                {
                    cmd.EnableShaderKeyword("SSR_APPROX");
                }
                else
                {
                    // TODO: Clear accum and accum prev
                    SSRResolveHandle = currAccumHandle;

                    cmd.DisableShaderKeyword("SSR_APPROX");
                }

                using (new ProfilingScope(cmd, m_SSRTracingProfilingSampler))
                {
                    cmd.SetRenderTarget(m_SSRHitPointTexture);
                    cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 0, 0);
                    cmd.SetRenderTarget(m_SSRRayInfoTexture);
                    cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 0, 0);

                    var stencilBuffer = renderingData.cameraData.renderer.cameraDepthTargetHandle.rt.depthBuffer;
                    float n = renderingData.cameraData.camera.nearClipPlane;
                    float f = renderingData.cameraData.camera.farClipPlane;
                    float thickness = m_volumeSettings.depthBufferThickness.value;
                    float thicknessScale = 1.0f / (1.0f + thickness);
                    float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

                    // ColorPyramidSize 
                    Vector4 colorPyramidUvScaleAndLimitPrev = RenderingUtils.ComputeViewportScaleAndLimit(m_CurCameraHistoryRTSystem.rtHandleProperties.previousViewportSize, m_CurCameraHistoryRTSystem.rtHandleProperties.previousRenderTargetSize);

                    var blueNoise = BlueNoiseSystem.TryGetInstance();
                    if (blueNoise != null)
                    {
                        m_BlueNoiseTexArrayIndex = (m_BlueNoiseTexArrayIndex + 1) % BlueNoiseSystem.blueNoiseArraySize;
                        blueNoise.BindSTBNVec2Texture(cmd, m_BlueNoiseTexArrayIndex);
                    }

                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_SSRHitPointTexture", m_SSRHitPointTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_SSRRayInfoTexture", m_SSRRayInfoTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_CameraMotionVectorsTexture", m_Renderer.m_MotionVectorColor);

                    // Constant Params
                    // TODO: Should use ConstantBuffer
                    // ConstantBuffer.Push(ctx.cmd, data.cb, cs, HDShaderIDs._ShaderVariablesScreenSpaceReflection);
                    {
                        // Wtf? It seems that Unity has not setted this jittered matrix (or changed by something)?
                        // We will transfer our own Temporal AA jittered matrices and use it in SSR compute.
                        Matrix4x4 viewMatrix = renderingData.cameraData.GetViewMatrix();
                        // Jittered, non-gpu
                        //Matrix4x4 projectionMatrix = renderingData.cameraData.GetProjectionMatrix();
                        // Jittered, gpu
                        Matrix4x4 gpuProjectionMatrix = renderingData.cameraData.GetGPUProjectionMatrix(renderingData.cameraData.IsCameraProjectionMatrixFlipped());
                        Matrix4x4 viewAndProjectionMatrix = gpuProjectionMatrix * viewMatrix;
                        Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
                        Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
                        Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;

                        cmd.SetComputeMatrixParam(m_Compute, "_SSR_MATRIX_VP", viewAndProjectionMatrix);
                        cmd.SetComputeMatrixParam(m_Compute, "_SSR_MATRIX_I_VP", inverseViewProjection);

                        MotionVectorsPersistentData motionData = null;
                        if (renderingData.cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                            motionData = additionalCameraData.motionVectorsPersistentData;
                        if (motionData != null)
                        {
                            cmd.SetComputeMatrixParam(m_Compute, "_SSR_PREV_MATRIX_VP", motionData.previousViewProjectionJittered);
                            cmd.SetComputeMatrixParam(m_Compute, "_SSR_MATRIX_CLIP_TO_PREV_CLIP", motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection));
                        }

                        cmd.SetComputeVectorParam(m_Compute, "_SsrTraceScreenSize", new Vector4(m_SSRHitPointTexture.rt.width, m_SSRHitPointTexture.rt.height, 1.0f / m_SSRHitPointTexture.rt.width, 1.0f / m_SSRHitPointTexture.rt.height));
                        cmd.SetComputeIntParam(m_Compute, "_SsrStencilBit", (int)(1 << 3));
                        cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeEnd", 1 - m_volumeSettings.minSmoothness);
                        cmd.SetComputeIntParam(m_Compute, "_SsrDepthPyramidMaxMip", m_Renderer.depthBufferMipChainInfo.mipLevelCount - 1);
                        cmd.SetComputeIntParam(m_Compute, "_SsrReflectsSky", m_volumeSettings.reflectSky.value ? 1 : 0);
                        cmd.SetComputeIntParam(m_Compute, "_SsrIterLimit", m_volumeSettings.rayMaxIterations);
                        cmd.SetComputeFloatParam(m_Compute, "_SsrThicknessScale", thicknessScale);
                        cmd.SetComputeFloatParam(m_Compute, "_SsrThicknessBias", thicknessBias);
                        cmd.SetComputeFloatParam(m_Compute, "_SsrPBRBias", m_volumeSettings.biasFactor.value);
                        cmd.SetComputeVectorParam(m_Compute, "_ColorPyramidUvScaleAndLimitPrevFrame", colorPyramidUvScaleAndLimitPrev);
                        cmd.SetComputeIntParam(m_Compute, "_SsrFrameCount", Time.frameCount);
                        

                        cmd.SetComputeBufferParam(m_Compute, m_SSRTracingKernel, "_DepthPyramidMipLevelOffsets", m_Renderer.depthBufferMipChainInfo.GetOffsetBufferData(m_Renderer.depthPyramidMipLevelOffsetsBuffer));
                    }


                    cmd.DispatchCompute(m_Compute, m_SSRTracingKernel, RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.height, 8), 1);
                }

                using (new ProfilingScope(cmd, m_SSRResolveProfilingSampler))
                {
                    cmd.SetRenderTarget(SSRResolveHandle);
                    cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);
                    cmd.SetRenderTarget(m_SSRHitDepthTexture);
                    cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);
                    // cmd.SetRenderTarget(m_SSRResolveVarianceTexture);
                    // cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRHitPointTexture", m_SSRHitPointTexture);
                    cmd.SetComputeIntParam(m_Compute, "_SsrColorPyramidMaxMip", m_Renderer.colorPyramidHistoryMipCount - 1);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_ColorPyramidTexture", HistoryFrameRTSystem.GetOrCreate(renderingData.cameraData.camera).GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain));
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRAccumTexture", SSRResolveHandle);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_CameraMotionVectorsTexture", m_Renderer.m_MotionVectorColor);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRRayInfoTexture", m_SSRRayInfoTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRHitDepthTexture", m_SSRHitDepthTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRResolveVarianceTexture", m_SSRResolveVarianceTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, "_SSRAvgRadianceTexture", m_SSRAvgRadianceTexture);

                    // Constant Params
                    {
                        float ssrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
                        float roughnessFadeStart = 1 - m_volumeSettings.smoothnessFadeStart;
                        float roughnessFadeLength = ssrRoughnessFadeEnd - roughnessFadeStart;

                        cmd.SetComputeFloatParam(m_Compute, "_SsrEdgeFadeRcpLength", Mathf.Min(1.0f / m_volumeSettings.screenFadeDistance.value, float.MaxValue));
                        cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeRcpLength", (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0);
                        cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeEndTimesRcpLength", ((roughnessFadeLength != 0) ? (ssrRoughnessFadeEnd * (1.0f / roughnessFadeLength)) : 1));
                    }

                    cmd.DispatchCompute(m_Compute, m_SSRResolveKernel, RenderingUtils.DivRoundUp(SSRResolveHandle.rt.width, 8), RenderingUtils.DivRoundUp(SSRResolveHandle.rt.height, 8), 1);
                }

                if (m_volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
                {
                    // using (new ProfilingScope(cmd, m_SSRATrousProfilingSampler))
                    // {
                    //     cmd.SetComputeTextureParam(m_Compute, m_SSRATrousKernel, "_SSRAccumTexture", currAccumHandle);
                    //     cmd.SetComputeFloatParam(m_Compute, "_ATrousStepSize", Mathf.Pow(2, 0));
                    //     cmd.DispatchCompute(m_Compute, m_SSRATrousKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 32), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 32), 1);
                    //     cmd.SetComputeFloatParam(m_Compute, "_ATrousStepSize", Mathf.Pow(2, 1));
                    //     cmd.DispatchCompute(m_Compute, m_SSRATrousKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 32), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 32), 1);
                    //     cmd.SetComputeFloatParam(m_Compute, "_ATrousStepSize", Mathf.Pow(2, 2));
                    //     cmd.DispatchCompute(m_Compute, m_SSRATrousKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 32), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 32), 1);
                    //     cmd.SetComputeFloatParam(m_Compute, "_ATrousStepSize", Mathf.Pow(2, 3));
                    //     cmd.DispatchCompute(m_Compute, m_SSRATrousKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 32), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 32), 1);


                    // }

                    //using (new ProfilingScope(cmd, m_SSRBilateralProfilingSampler))
                    //{
                    //    cmd.SetComputeTextureParam(m_Compute, m_SSRBilateralKernel, "_SSRAccumTexture", currAccumHandle);
                    //    cmd.SetComputeTextureParam(m_Compute, m_SSRBilateralKernel, "_SsrLightingTexture", m_SSRLightingTexture);
                    //    cmd.SetComputeTextureParam(m_Compute, m_SSRBilateralKernel, "_SSRResolveVarianceTexture", m_SSRResolveVarianceTexture);

                    //    cmd.DispatchCompute(m_Compute, m_SSRBilateralKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 8), 1);
                    //}

                    using (new ProfilingScope(cmd, m_SSRAccumulateProfilingSampler))
                    {
                        cmd.SetRenderTarget(m_SSRLightingTexture);
                        cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRHitPointTexture", m_SSRHitPointTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRAccumTexture", currAccumHandle);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SsrAccumPrev", prevAccumHandle);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SsrLightingTexture", m_SSRLightingTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_CameraMotionVectorsTexture", m_Renderer.m_MotionVectorColor);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRHitDepthTexture", m_SSRHitDepthTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRResolveVarianceTexture", m_SSRResolveVarianceTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRAvgRadianceTexture", m_SSRAvgRadianceTexture);

                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRPrevNumFramesAccumTexture", m_CurCameraHistoryRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation));
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, "_SSRNumFramesAccumTexture", m_CurCameraHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation));


                        cmd.DispatchCompute(m_Compute, m_SSRAccumulateKernel, RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRLightingTexture.rt.height, 8), 1);
                    }


                }


                cmd.SetGlobalTexture("_SsrLightingTexture", m_SSRLightingTexture);
            }

        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            //CoreUtils.SetKeyword(cmd, ShaderKeywordStrings, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
            m_SSRHitPointTexture?.Release();
            m_SSRRayInfoTexture?.Release();
            m_SSRHitDepthTexture?.Release();
            m_SSRResolveVarianceTexture?.Release();
            m_SSRLightingTexture?.Release();
        }
    }
}