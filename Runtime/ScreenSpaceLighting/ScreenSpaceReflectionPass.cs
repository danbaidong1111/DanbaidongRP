using System;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private static string m_SSRTracingProfilerTag = "SSRTracing";
        private static string m_SSRResolveProfilerTag = "SSRResolve";
        private static string m_SSRAccumulateProfilerTag = "SSRAccumulate";
        private static ProfilingSampler m_SSRClassifyTilesProfilingSampler = new ProfilingSampler(m_SSRClassifyTilesProfilerTag);
        private static ProfilingSampler m_SSRTracingProfilingSampler = new ProfilingSampler(m_SSRTracingProfilerTag);
        private static ProfilingSampler m_SSRResolveProfilingSampler = new ProfilingSampler(m_SSRResolveProfilerTag);
        private static ProfilingSampler m_SSRAccumulateProfilingSampler = new ProfilingSampler(m_SSRAccumulateProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;
        private ScreenSpaceReflectionSettings m_CurrentSettings;
        private int m_BlueNoiseTexArrayIndex;

        private RTHandle m_SSRHitPointTexture;
        private RTHandle m_SSRRayInfoTexture;
        private RTHandle m_SSRAvgRadianceTexture;
        private RTHandle m_SSRHitDepthTexture;
        private RTHandle m_SSRLightingTexture;

        private int m_SSRClassifyTilesKernel;
        private int m_SSRTracingKernel;
        private int m_SSRResolveKernel;
        private int m_SSRAccumulateKernel;

        private ScreenSpaceReflection m_volumeSettings;
        private UniversalRenderer m_Renderer;
        private HistoryFrameRTSystem m_CurCameraHistoryRTSystem;

        private ComputeBuffer m_DispatchIndirectBuffer = null;
        private ComputeBuffer m_TileListBuffer = null;

        private ShaderVariablesScreenSpaceReflection m_CBuffer;

        private bool traceDownSample = false;

        // Constants

        // Statics
        private static Vector2 s_accumulateTextureScaleFactor = Vector2.one;
        private static readonly int _ShaderVariablesScreenSpaceReflection = Shader.PropertyToID("ShaderVariablesScreenSpaceReflection");

        internal ScreenSpaceReflectionPass(ComputeShader computeShader)
        {
            m_CurrentSettings = new ScreenSpaceReflectionSettings();
            m_Compute = computeShader;

            m_SSRClassifyTilesKernel = m_Compute.FindKernel("ScreenSpaceReflectionsClassifyTiles");
            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
            m_SSRResolveKernel = m_Compute.FindKernel("ScreenSpaceReflectionsResolve");
            m_SSRAccumulateKernel = m_Compute.FindKernel("ScreenSpaceReflectionsAccumulate");

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

            // As descripted in HDRP, but we only use DispatchIndirect.
            // DispatchIndirect: Buffer with arguments has to have three integer numbers at given argsOffset offset: number of work groups in X dimension, number of work groups in Y dimension, number of work groups in Z dimension.
            // DrawProceduralIndirect: Buffer with arguments has to have four integer numbers at given argsOffset offset: vertex count per instance, instance count, start vertex location, and start instance location
            // Use use max size of 4 unit for allocation
            m_DispatchIndirectBuffer = ShaderData.instance.GetSSRDispatchIndirectBuffer(3);
            m_TileListBuffer = ShaderData.instance.GetSSRTileListBuffer(RenderingUtils.DivRoundUp(desc.width, 8) * RenderingUtils.DivRoundUp(desc.height, 8));
            // Avoid GC or GPU readback. No need to SetData or GetData.
            //m_DispatchIndirectBuffer.SetData(new uint[3] { 0, 1, 1 });
            //m_TileListBuffer.SetData(tileListArray);


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
            
            ReAllocatedAccumulateTextureIfNeeded(m_CurCameraHistoryRTSystem, renderingData.cameraData);
            ReAllocatedNumFramesAccumTextureIfNeeded(m_CurCameraHistoryRTSystem, renderingData.cameraData);
        }

        void UpdateSSRConstantBuffer(RenderingData renderingData)
        {
            float n = renderingData.cameraData.camera.nearClipPlane;
            float f = renderingData.cameraData.camera.farClipPlane;
            float thickness = m_volumeSettings.depthBufferThickness.value;
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

            float ssrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
            float roughnessFadeStart = 1 - m_volumeSettings.smoothnessFadeStart;
            float roughnessFadeLength = ssrRoughnessFadeEnd - roughnessFadeStart;

            // ColorPyramidSize 
            Vector4 colorPyramidUvScaleAndLimitPrev = RenderingUtils.ComputeViewportScaleAndLimit(m_CurCameraHistoryRTSystem.rtHandleProperties.previousViewportSize, m_CurCameraHistoryRTSystem.rtHandleProperties.previousRenderTargetSize);

            // Constant Params
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

                m_CBuffer._SSR_MATRIX_VP = viewAndProjectionMatrix;
                m_CBuffer._SSR_MATRIX_I_VP = inverseViewProjection;

                MotionVectorsPersistentData motionData = null;
                if (renderingData.cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                    motionData = additionalCameraData.motionVectorsPersistentData;
                if (motionData != null)
                {
                    m_CBuffer._SSR_PREV_MATRIX_VP = motionData.previousViewProjectionJittered;
                    m_CBuffer._SSR_MATRIX_CLIP_TO_PREV_CLIP = motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection);
                }

                m_CBuffer._SsrTraceScreenSize = new Vector4(m_SSRHitPointTexture.rt.width, m_SSRHitPointTexture.rt.height, 1.0f / m_SSRHitPointTexture.rt.width, 1.0f / m_SSRHitPointTexture.rt.height);
                m_CBuffer._SsrThicknessScale = thicknessScale;
                m_CBuffer._SsrThicknessBias = thicknessBias;
                m_CBuffer._SsrIterLimit = m_volumeSettings.rayMaxIterations;
                m_CBuffer._SsrFrameCount = Time.frameCount;

                m_CBuffer._SsrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
                m_CBuffer._SsrRoughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
                m_CBuffer._SsrRoughnessFadeEndTimesRcpLength = ((roughnessFadeLength != 0) ? (ssrRoughnessFadeEnd * (1.0f / roughnessFadeLength)) : 1);
                m_CBuffer._SsrEdgeFadeRcpLength = Mathf.Min(1.0f / m_volumeSettings.screenFadeDistance.value, float.MaxValue);

                m_CBuffer._ColorPyramidUvScaleAndLimitPrevFrame = colorPyramidUvScaleAndLimitPrev;

                m_CBuffer._SsrDepthPyramidMaxMip = m_Renderer.depthBufferMipChainInfo.mipLevelCount - 1;
                m_CBuffer._SsrColorPyramidMaxMip = m_Renderer.colorPyramidHistoryMipCount - 1;
                m_CBuffer._SsrReflectsSky = m_volumeSettings.reflectSky.value ? 1 : 0;
                m_CBuffer._SsrAccumulationAmount = m_volumeSettings.accumulationFactor.value;


                var prevRT = m_CurCameraHistoryRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
                Vector4 historyFrameRTSize = new Vector4(prevRT.rt.width, prevRT.rt.height, prevRT.rt.texelSize.x, prevRT.rt.texelSize.y);
                m_CBuffer._HistoryFrameRTSize = historyFrameRTSize;

                m_CBuffer._SsrPBRBias = m_volumeSettings.biasFactor.value;
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
            if (m_CurCameraHistoryRTSystem == null || 
                m_CurCameraHistoryRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation) == null)
            {
                Debug.LogError("History invalid");
                return;
            }

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.SSR)))
            {
                cmd.SetRenderTarget(m_SSRLightingTexture);
                cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

                UpdateSSRConstantBuffer(renderingData);

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


                using (new ProfilingScope(cmd, m_SSRClassifyTilesProfilingSampler))
                {
                    cmd.SetComputeBufferParam(m_Compute, m_SSRClassifyTilesKernel, ShaderConstants.gDispatchIndirectBuffer, m_DispatchIndirectBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_SSRClassifyTilesKernel, ShaderConstants.gTileList, m_TileListBuffer);
                    ConstantBuffer.Push(cmd, m_CBuffer, m_Compute, _ShaderVariablesScreenSpaceReflection);
                    cmd.DispatchCompute(m_Compute, m_SSRClassifyTilesKernel, RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.height, 8), 1);
                }




                using (new ProfilingScope(cmd, m_SSRTracingProfilingSampler))
                {
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

                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, ShaderConstants._SSRHitPointTexture, m_SSRHitPointTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, ShaderConstants._SSRRayInfoTexture, m_SSRRayInfoTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, ShaderConstants._CameraMotionVectorsTexture, m_Renderer.m_MotionVectorColor);

                    cmd.SetComputeBufferParam(m_Compute, m_SSRTracingKernel, ShaderConstants._DepthPyramidMipLevelOffsets, m_Renderer.depthBufferMipChainInfo.GetOffsetBufferData(m_Renderer.depthPyramidMipLevelOffsetsBuffer));

                    cmd.SetComputeBufferParam(m_Compute, m_SSRTracingKernel, ShaderConstants.gDispatchIndirectBuffer, m_DispatchIndirectBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_SSRTracingKernel, ShaderConstants.gTileList, m_TileListBuffer);
                    ConstantBuffer.Push(cmd, m_CBuffer, m_Compute, _ShaderVariablesScreenSpaceReflection);
                    cmd.DispatchCompute(m_Compute, m_SSRTracingKernel, m_DispatchIndirectBuffer, 0);

                }

                using (new ProfilingScope(cmd, m_SSRResolveProfilingSampler))
                {
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._SSRHitPointTexture, m_SSRHitPointTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._ColorPyramidTexture, HistoryFrameRTSystem.GetOrCreate(renderingData.cameraData.camera).GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain));
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._SSRAccumTexture, SSRResolveHandle);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._CameraMotionVectorsTexture, m_Renderer.m_MotionVectorColor);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._SSRRayInfoTexture, m_SSRRayInfoTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._SSRHitDepthTexture, m_SSRHitDepthTexture);
                    cmd.SetComputeTextureParam(m_Compute, m_SSRResolveKernel, ShaderConstants._SSRAvgRadianceTexture, m_SSRAvgRadianceTexture);

                    
                    cmd.SetComputeBufferParam(m_Compute, m_SSRResolveKernel, ShaderConstants.gDispatchIndirectBuffer, m_DispatchIndirectBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_SSRResolveKernel, ShaderConstants.gTileList, m_TileListBuffer);
                    ConstantBuffer.Push(cmd, m_CBuffer, m_Compute, _ShaderVariablesScreenSpaceReflection);
                    cmd.DispatchCompute(m_Compute, m_SSRResolveKernel, m_DispatchIndirectBuffer, 0);
                }

                if (m_volumeSettings.usedAlgorithm == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
                {
                    using (new ProfilingScope(cmd, m_SSRAccumulateProfilingSampler))
                    {
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRHitPointTexture, m_SSRHitPointTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRAccumTexture, currAccumHandle);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SsrAccumPrev, prevAccumHandle);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SsrLightingTexture, m_SSRLightingTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._CameraMotionVectorsTexture, m_Renderer.m_MotionVectorColor);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRHitDepthTexture, m_SSRHitDepthTexture);
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRAvgRadianceTexture, m_SSRAvgRadianceTexture);

                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRPrevNumFramesAccumTexture, m_CurCameraHistoryRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation));
                        cmd.SetComputeTextureParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants._SSRNumFramesAccumTexture, m_CurCameraHistoryRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation));

                        ConstantBuffer.Push(cmd, m_CBuffer, m_Compute, _ShaderVariablesScreenSpaceReflection);
                        cmd.SetComputeBufferParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants.gDispatchIndirectBuffer, m_DispatchIndirectBuffer);
                        cmd.SetComputeBufferParam(m_Compute, m_SSRAccumulateKernel, ShaderConstants.gTileList, m_TileListBuffer);
                        cmd.DispatchCompute(m_Compute, m_SSRAccumulateKernel, m_DispatchIndirectBuffer, 0);
                    }


                }

                cmd.SetGlobalTexture(ShaderConstants._SsrLightingTexture, m_SSRLightingTexture);
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
            m_SSRAvgRadianceTexture?.Release();
            m_SSRHitDepthTexture?.Release();
            m_SSRLightingTexture?.Release();
        }


        static class ShaderConstants
        {
            public static readonly int gDispatchIndirectBuffer      = Shader.PropertyToID("gDispatchIndirectBuffer");
            public static readonly int gTileList                    = Shader.PropertyToID("gTileList");
            public static readonly int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
            public static readonly int _SSRHitPointTexture          = Shader.PropertyToID("_SSRHitPointTexture");
            public static readonly int _SSRRayInfoTexture           = Shader.PropertyToID("_SSRRayInfoTexture");
            public static readonly int _CameraMotionVectorsTexture  = Shader.PropertyToID("_CameraMotionVectorsTexture");
            public static readonly int _ColorPyramidTexture         = Shader.PropertyToID("_ColorPyramidTexture");
            public static readonly int _SsrAccumPrev                = Shader.PropertyToID("_SsrAccumPrev");
            public static readonly int _SSRAccumTexture             = Shader.PropertyToID("_SSRAccumTexture");
            public static readonly int _SSRHitDepthTexture          = Shader.PropertyToID("_SSRHitDepthTexture");
            public static readonly int _SSRAvgRadianceTexture       = Shader.PropertyToID("_SSRAvgRadianceTexture");
            public static readonly int _SsrLightingTexture          = Shader.PropertyToID("_SsrLightingTexture");
            public static readonly int _SSRPrevNumFramesAccumTexture = Shader.PropertyToID("_SSRPrevNumFramesAccumTexture");
            public static readonly int _SSRNumFramesAccumTexture    = Shader.PropertyToID("_SSRNumFramesAccumTexture");
        }
    }
}