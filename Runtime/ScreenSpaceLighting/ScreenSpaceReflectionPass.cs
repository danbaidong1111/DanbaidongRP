using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private static string m_SSRTracingProfilerTag = "SSRTracing";
        private static string m_RayTracingReflectionProfilerTag = "RayTracingReflection";
        private static string m_SSRResolveProfilerTag = "SSRResolve";
        private static string m_SSRAccumulateProfilerTag = "SSRAccumulate";
        private static ProfilingSampler m_SSRClassifyTilesProfilingSampler = new ProfilingSampler(m_SSRClassifyTilesProfilerTag);
        private static ProfilingSampler m_SSRTracingProfilingSampler = new ProfilingSampler(m_SSRTracingProfilerTag);
        private static ProfilingSampler m_RayTracingReflectionSampler = new ProfilingSampler(m_RayTracingReflectionProfilerTag);
        private static ProfilingSampler m_SSRResolveProfilingSampler = new ProfilingSampler(m_SSRResolveProfilerTag);
        private static ProfilingSampler m_SSRAccumulateProfilingSampler = new ProfilingSampler(m_SSRAccumulateProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;

        private int m_SSRClassifyTilesKernel;
        private int m_SSRTracingKernel;
        private int m_SSRResolveKernel;
        private int m_SSRAccumulateKernel;

        private ScreenSpaceReflection m_volumeSettings;

        // Constants

        // Statics
        private static Vector2 s_accumulateTextureScaleFactor = Vector2.one;
        private static readonly int _ShaderVariablesScreenSpaceReflection = Shader.PropertyToID("ShaderVariablesScreenSpaceReflection");

        internal ScreenSpaceReflectionPass(RenderPassEvent evt, ComputeShader computeShader)
        {
            this.renderPassEvent = evt;

            m_Compute = computeShader;

            m_SSRClassifyTilesKernel = m_Compute.FindKernel("ScreenSpaceReflectionsClassifyTiles");
            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
            m_SSRResolveKernel = m_Compute.FindKernel("ScreenSpaceReflectionsResolve");
            m_SSRAccumulateKernel = m_Compute.FindKernel("ScreenSpaceReflectionsAccumulate");
        }

        /// <summary>
        /// Setup controls per frame shouldEnqueue this pass.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal bool Setup()
        {
            var stack = VolumeManager.instance.stack;
            m_volumeSettings = stack.GetComponent<ScreenSpaceReflection>();

            return m_Compute != null && m_volumeSettings != null && m_volumeSettings.IsActive();
        }

        static RTHandle HistoryAccumulateTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedAccumulateTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionAccumulation, cameraData.camera.name
                                                            , HistoryAccumulateTextureAllocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
        }

        static RTHandle HistoryNumFramesAccumTextureAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one * s_accumulateTextureScaleFactor, TextureXR.slices, colorFormat: graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_SSRNumFramesAccumTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedNumFramesAccumTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation, cameraData.camera.name
                                                            , HistoryNumFramesAccumTextureAllocator, GraphicsFormat.R8_UNorm, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionNumFramesAccumulation);
        }

        internal class SSRPassData
        {
            internal ComputeShader cs;
            internal int classifyTilesKernel;
            internal int tracingKernel;
            internal int resolveKernel;
            internal int accumulateKernel;

            // Classify tiles
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;
            internal BufferHandle raysCoordBuffer;

            internal TextureHandle cameraDepthTexture;
            internal TextureHandle depthPyramidTexture;
            internal BufferHandle depthPyramidMipLevelOffsets;
            internal TextureHandle rayHitColorTexture;
            internal TextureHandle hitPointTexture;
            internal Vector2Int TraceTextureSize;
            internal int camHistoryFrameCount;
            internal TextureHandle blueNoiseArray;
            internal TextureHandle ssrLightingTexture;
            internal TextureHandle rayInfoTexture;

            internal TextureHandle rayDirTexture;

            internal TextureHandle motionVectorTexture;
            internal TextureHandle prevColorPyramidTexture;
            internal TextureHandle avgRadianceTexture;

            internal TextureHandle currAccumulateTexture;
            internal TextureHandle prevAccumulateTexture;
            internal TextureHandle currNumFramesAccumTexture;
            internal TextureHandle prevNumFramesAccumTexture;

            internal ShaderVariablesScreenSpaceReflection constantBuffer;

            internal ScreenSpaceReflectionAlgorithm usedAlgo;

            // RayTracing
            internal BufferHandle dispatchRayIndirectBuffer;
            internal bool requireRayTracing;
            internal RayTracingShader rtrtShader;
            internal GPULights.GPULightsOutPassData rayTracingLights;
            internal RayTracingAccelerationStructure rtas;
            // Sky Ambient & Reflect
            internal BufferHandle ambientProbe;
            internal TextureHandle reflectProbe;
            internal ShaderVariablesRaytracing rayTracingCB;
        }

        void InitResource(RenderGraph renderGraph, SSRPassData passData, UniversalResourceData resourceData, UniversalCameraData cameraData, HistoryFrameRTSystem historyRTSystem)
        {
            // Compute shaders
            passData.cs = m_Compute;
            passData.classifyTilesKernel = m_SSRClassifyTilesKernel;
            passData.tracingKernel = m_SSRTracingKernel;
            passData.resolveKernel = m_SSRResolveKernel;
            passData.accumulateKernel = m_SSRAccumulateKernel;

            // Target texture Desc
            TextureDesc texDesc = new TextureDesc(cameraData.cameraTargetDescriptor);
            texDesc.msaaSamples = MSAASamples.None;
            texDesc.depthBufferBits = DepthBits.None;
            texDesc.enableRandomWrite = true;
            texDesc.filterMode = FilterMode.Point;
            texDesc.wrapMode = TextureWrapMode.Clamp;

            // Create Buffers
            // DispatchIndirect: Buffer with arguments has to have three integer numbers at given argsOffset offset: number of work groups in X dimension, number of work groups in Y dimension, number of work groups in Z dimension.
            // RayTracingIndirect also use this buffer, 3 offset.
            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirect = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.SSRDispatchIndirectBuffer, 3, "SSRDispatIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchIndirectBuffer = renderGraph.ImportBuffer(dispatchIndirect);
            passData.tileListBuffer = renderGraph.CreateBuffer(new BufferDesc(RenderingUtils.DivRoundUp(texDesc.width, 8) * RenderingUtils.DivRoundUp(texDesc.height, 8), sizeof(uint), "SSRTileListBuffer"));
            passData.raysCoordBuffer = renderGraph.CreateBuffer(new BufferDesc(texDesc.width * texDesc.height, sizeof(uint), "RaysCoordBuffer"));


            var dispatchRayIndirect = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.RTReflectionIndirectBuffer, 3, "RTRReflectionIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchRayIndirectBuffer = renderGraph.ImportBuffer(dispatchRayIndirect);


            passData.cameraDepthTexture = resourceData.cameraDepthTexture;
            passData.depthPyramidTexture = resourceData.cameraDepthPyramidTexture;
            passData.depthPyramidMipLevelOffsets = resourceData.cameraDepthPyramidMipLevelOffsets;

            passData.camHistoryFrameCount = historyRTSystem.historyFrameCount;
            passData.blueNoiseArray = resourceData.blueNoise128RG;

            passData.TraceTextureSize = new Vector2Int(texDesc.width, texDesc.height);

            var rayHitColorDesc = texDesc;
            rayHitColorDesc.name = "_RayHitColorTexture";
            rayHitColorDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rayHitColorDesc.clearBuffer = true;
            rayHitColorDesc.clearColor = new Color(0, 0, 0, 0);
            passData.rayHitColorTexture = renderGraph.CreateTexture(rayHitColorDesc);


            var lightingDesc = texDesc;
            lightingDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            lightingDesc.filterMode = FilterMode.Bilinear;
            lightingDesc.name = "_SSRLightingTexture";
            lightingDesc.clearBuffer = true;
            lightingDesc.clearColor = new Color(0, 0, 0, 0);
            passData.ssrLightingTexture = renderGraph.CreateTexture(lightingDesc);

            var rayInfoDesc = texDesc;
            rayInfoDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rayInfoDesc.name = "_SSRRayInfoTexture";
            passData.rayInfoTexture = renderGraph.CreateTexture(rayInfoDesc);

            var dispatchRayDirDesc = texDesc;
            dispatchRayDirDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            dispatchRayDirDesc.name = "_DispatchRayDirTexture";
            passData.rayDirTexture = renderGraph.CreateTexture(dispatchRayDirDesc);

            var avgRadianceDesc = texDesc;
            avgRadianceDesc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;
            avgRadianceDesc.width = RenderingUtils.DivRoundUp(texDesc.width, 8);
            avgRadianceDesc.height = RenderingUtils.DivRoundUp(texDesc.height, 8);
            avgRadianceDesc.filterMode = FilterMode.Bilinear;
            avgRadianceDesc.name = "_SSRAvgRadianceTexture";
            passData.avgRadianceTexture = renderGraph.CreateTexture(avgRadianceDesc);

            passData.motionVectorTexture = resourceData.motionVectorColor;
            passData.prevColorPyramidTexture = renderGraph.ImportTexture(historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain));

            // Import history texture.
            RTHandle currAccumulateTexture, prevAccumulateTexture;
            RTHandle currNumFramesAccumTexture, prevNumFramesAccumTexture;
            ReAllocatedAccumulateTextureIfNeeded(historyRTSystem, cameraData, out currAccumulateTexture, out prevAccumulateTexture);
            ReAllocatedNumFramesAccumTextureIfNeeded(historyRTSystem, cameraData, out currNumFramesAccumTexture, out prevNumFramesAccumTexture);

            passData.currAccumulateTexture = renderGraph.ImportTexture(currAccumulateTexture);
            passData.prevAccumulateTexture = renderGraph.ImportTexture(prevAccumulateTexture);
            passData.currNumFramesAccumTexture = renderGraph.ImportTexture(currNumFramesAccumTexture);
            passData.prevNumFramesAccumTexture = renderGraph.ImportTexture(prevNumFramesAccumTexture);

            passData.usedAlgo = passData.requireRayTracing ? ScreenSpaceReflectionAlgorithm.PBRAccumulation : m_volumeSettings.usedAlgorithm.value;
        }

        void UpdateSSRConstantBuffer(SSRPassData passData,
            UniversalResourceData resourceData, 
            UniversalCameraData cameraData, 
            HistoryFrameRTSystem historyRTSystem, 
            int colorPyramidHistoryMipCount)
        {
            float n = cameraData.camera.nearClipPlane;
            float f = cameraData.camera.farClipPlane;
            float thickness = m_volumeSettings.depthBufferThickness.value;
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

            float ssrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
            float roughnessFadeStart = 1 - m_volumeSettings.smoothnessFadeStart;
            float roughnessFadeLength = ssrRoughnessFadeEnd - roughnessFadeStart;

            // ColorPyramidSize 
            Vector4 colorPyramidUvScaleAndLimitPrev = RenderingUtils.ComputeViewportScaleAndLimit(historyRTSystem.rtHandleProperties.previousViewportSize, historyRTSystem.rtHandleProperties.previousRenderTargetSize);

            
            // Constant Params
            {
                MotionVectorsPersistentData motionData = null;
                if (cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                    motionData = additionalCameraData.motionVectorsPersistentData;
                if (motionData != null)
                {
                    passData.constantBuffer._SSR_MATRIX_CLIP_TO_PREV_CLIP = motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection);
                }

                passData.constantBuffer._SsrTraceScreenSize = new Vector4(passData.TraceTextureSize.x, passData.TraceTextureSize.y, 1.0f / passData.TraceTextureSize.x, 1.0f / passData.TraceTextureSize.y);
                passData.constantBuffer._SsrThicknessScale = thicknessScale;
                passData.constantBuffer._SsrThicknessBias = thicknessBias;
                passData.constantBuffer._SsrIterLimit = m_volumeSettings.rayMaxIterations;
                passData.constantBuffer._SsrFrameCount = Time.frameCount;

                passData.constantBuffer._SsrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
                passData.constantBuffer._SsrRoughnessFadeRcpLength = (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0;
                passData.constantBuffer._SsrRoughnessFadeEndTimesRcpLength = ((roughnessFadeLength != 0) ? (ssrRoughnessFadeEnd * (1.0f / roughnessFadeLength)) : 1);
                passData.constantBuffer._SsrEdgeFadeRcpLength = Mathf.Min(1.0f / m_volumeSettings.screenFadeDistance.value, float.MaxValue);

                passData.constantBuffer._ColorPyramidUvScaleAndLimitPrevFrame = colorPyramidUvScaleAndLimitPrev;

                passData.constantBuffer._SsrDepthPyramidMaxMip = resourceData.cameraDepthPyramidInfo.mipLevelCount - 1;
                passData.constantBuffer._SsrColorPyramidMaxMip = colorPyramidHistoryMipCount - 1;
                passData.constantBuffer._SsrReflectsSky = m_volumeSettings.reflectSky.value ? 1 : 0;
                passData.constantBuffer._SsrAccumulationAmount = m_volumeSettings.accumulationFactor.value;


                var prevRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceReflectionAccumulation);
                Vector4 historyFrameRTSize = new Vector4(prevRT.rt.width, prevRT.rt.height, prevRT.rt.texelSize.x, prevRT.rt.texelSize.y);
                passData.constantBuffer._HistoryFrameRTSize = historyFrameRTSize;

                passData.constantBuffer._SsrPBRBias = m_volumeSettings.biasFactor.value;
                passData.constantBuffer._SsrMixWithRayTracing = passData.requireRayTracing ? 1 : 0;
            }


            // RayTracing constant buffer
            if (passData.requireRayTracing)
            {
                var stack = VolumeManager.instance.stack;
                var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                passData.rayTracingCB = cameraData.rayTracingSystem.GetShaderVariablesRaytracingCB(passData.TraceTextureSize, rayTracingSettings);
                passData.rayTracingCB._RaytracingRayMaxLength = m_volumeSettings.rayLength;
                passData.rayTracingCB._RayTracingClampingFlag = 1;
                passData.rayTracingCB._RaytracingIntensityClamp = m_volumeSettings.clampValue;
                passData.rayTracingCB._RaytracingPreExposition = 0;
                passData.rayTracingCB._RayTracingDiffuseLightingOnly = 0;
                passData.rayTracingCB._RayTracingAPVRayMiss = 0;
                passData.rayTracingCB._RayTracingRayMissFallbackHierarchy = 0;
                passData.rayTracingCB._RayTracingRayMissUseAmbientProbeAsSky = 0;
                passData.rayTracingCB._RayTracingLastBounceFallbackHierarchy = 0;
                passData.rayTracingCB._RayTracingAmbientProbeDimmer = m_volumeSettings.ambientProbeDimmer.value;
            }
        }

        static void ExecutePass(SSRPassData data, ComputeCommandBuffer cmd)
        {
            // Default Approximation we use lighingTexture as SSRResolve handle.
            var hitColorHandle = data.rayHitColorTexture;
            var currAccumHandle = data.currAccumulateTexture;
            var prevAccumHandle = data.prevAccumulateTexture;

            if (data.usedAlgo == ScreenSpaceReflectionAlgorithm.Approximation)
            {
                data.cs.EnableKeyword("SSR_APPROX");
                hitColorHandle = data.ssrLightingTexture;
            }
            else
            {
                data.cs.DisableKeyword("SSR_APPROX");
            }

            // Push ConstantBuffer to compute shader
            ConstantBuffer.Push(cmd, data.constantBuffer, data.cs, _ShaderVariablesScreenSpaceReflection);

            // ClassifyTiles
            using (new ProfilingScope(cmd, m_SSRClassifyTilesProfilingSampler))
            {
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.gDispatchIndirectBuffer, data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.gTileList, data.tileListBuffer);

                cmd.DispatchCompute(data.cs, data.classifyTilesKernel, RenderingUtils.DivRoundUp(data.TraceTextureSize.x, 8), RenderingUtils.DivRoundUp(data.TraceTextureSize.y, 8), 1);
            }

            // ScreenSpace Tracing
            using (new ProfilingScope(cmd, m_SSRTracingProfilingSampler))
            {
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.cs, data.tracingKernel, data.blueNoiseArray, data.camHistoryFrameCount);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._CameraDepthPyramidTexture, data.depthPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);

                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._ColorPyramidTexture, data.prevColorPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._RayHitColorTexture, hitColorHandle);
                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._SkyTexture, data.reflectProbe);

                cmd.SetComputeTextureParam(data.cs, data.tracingKernel, ShaderConstants._DispatchRayDirTexture, data.rayDirTexture);
                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants._DispatchRayCoordBuffer, data.raysCoordBuffer);
                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants._RayIndirectBuffer, data.dispatchRayIndirectBuffer);

                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants._DepthPyramidMipLevelOffsets, data.depthPyramidMipLevelOffsets);
                cmd.SetComputeBufferParam(data.cs, data.tracingKernel, ShaderConstants.gTileList, data.tileListBuffer);

                cmd.DispatchCompute(data.cs, data.tracingKernel, data.dispatchIndirectBuffer, 0);
            }


            if (data.usedAlgo != ScreenSpaceReflectionAlgorithm.Approximation)
            {
                // Ray Tracing
                if (data.requireRayTracing)
                {
                    using (new ProfilingScope(cmd, m_RayTracingReflectionSampler))
                    {
                        // Define the shader pass to use for the reflection pass
                        cmd.SetRayTracingShaderPass(data.rtrtShader, "IndirectDXR");
                        GPULights.BindRayTracingLights(cmd, data.rtrtShader, data.rayTracingLights);
                        // Sky Environment
                        cmd.SetGlobalBuffer(ShaderConstants._AmbientProbeData, data.ambientProbe);
                        cmd.SetGlobalTexture(ShaderConstants._SkyTexture, data.reflectProbe);

                        // Set the acceleration structure for the pass
                        cmd.SetRayTracingAccelerationStructure(data.rtrtShader, "_RaytracingAccelerationStructure", data.rtas);

                        // SetConstantBuffer
                        ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                        // Set Textures & Buffers
                        cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._DispatchRayDirTexture, data.rayDirTexture);
                        cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._RayTracingLightingTextureRW, data.rayHitColorTexture);
                        cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);


                        cmd.SetRayTracingBufferParam(data.rtrtShader, ShaderConstants._DispatchRayCoordBuffer, data.raysCoordBuffer);
                        cmd.DispatchRays(data.rtrtShader, "SingleRayGen", data.dispatchRayIndirectBuffer, 0);
                    }
                }

                // Resolve
                using (new ProfilingScope(cmd, m_SSRResolveProfilingSampler))
                {
                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._RayHitColorTexture, data.rayHitColorTexture);

                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._ColorPyramidTexture, data.prevColorPyramidTexture);
                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRAccumTexture, currAccumHandle);
                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);

                    cmd.SetComputeTextureParam(data.cs, data.resolveKernel, ShaderConstants._SSRAvgRadianceTexture, data.avgRadianceTexture);

                    cmd.SetComputeBufferParam(data.cs, data.resolveKernel, ShaderConstants.gTileList, data.tileListBuffer);
                    cmd.DispatchCompute(data.cs, data.resolveKernel, data.dispatchIndirectBuffer, 0);
                }

                // Accumulate
                using (new ProfilingScope(cmd, m_SSRAccumulateProfilingSampler))
                {
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRAccumTexture, currAccumHandle);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SsrAccumPrev, prevAccumHandle);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SsrLightingTexture, data.ssrLightingTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRRayInfoTexture, data.rayInfoTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRAvgRadianceTexture, data.avgRadianceTexture);

                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRPrevNumFramesAccumTexture, data.prevNumFramesAccumTexture);
                    cmd.SetComputeTextureParam(data.cs, data.accumulateKernel, ShaderConstants._SSRNumFramesAccumTexture, data.currNumFramesAccumTexture);

                    cmd.SetComputeBufferParam(data.cs, data.accumulateKernel, ShaderConstants.gTileList, data.tileListBuffer);
                    cmd.DispatchCompute(data.cs, data.accumulateKernel, data.dispatchIndirectBuffer, 0);
                }

            }


            // Set this keyword in deferred lighting, with this we can't use async.
            // Set Global State
            //cmd.SetKeyword(ShaderGlobalKeywords.ScreenSpaceReflection, true);
        }

        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, int colorPyramidHistoryMipCount)
        {
            // Early out
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem == null ||
                historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ColorBufferMipChain) == null)
            {
                return TextureHandle.nullHandle;
            }


            using (var builder = renderGraph.AddComputePass("Render SS Reflection", out SSRPassData passData, ProfilingSampler.Get(URPProfileId.RenderSSR)))
            {
                // Access resources.
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Ray Tracing
                passData.requireRayTracing = cameraData.supportedRayTracing && cameraData.rayTracingSystem.GetRayTracingState();


                // Set passData
                InitResource(renderGraph, passData, resourceData, cameraData, historyRTSystem);
                UpdateSSRConstantBuffer(passData, resourceData, cameraData, historyRTSystem, colorPyramidHistoryMipCount);

                // Declare input/output textures
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);

                builder.UseTexture(passData.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.depthPyramidTexture, AccessFlags.Read);
                builder.UseTexture(resourceData.gBuffer[2], AccessFlags.Read); // Normal GBuffer
                builder.UseTexture(passData.blueNoiseArray, AccessFlags.Read);
                builder.UseTexture(passData.rayHitColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.rayInfoTexture, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.depthPyramidMipLevelOffsets, AccessFlags.Read);

                builder.UseTexture(passData.rayDirTexture, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.raysCoordBuffer, AccessFlags.ReadWrite);

                builder.UseTexture(passData.motionVectorTexture, AccessFlags.Read);
                builder.UseTexture(passData.prevColorPyramidTexture, AccessFlags.Read);
                builder.UseTexture(passData.avgRadianceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ssrLightingTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.currAccumulateTexture, AccessFlags.ReadWrite);

                if (passData.usedAlgo == ScreenSpaceReflectionAlgorithm.PBRAccumulation)
                {
                    builder.UseTexture(passData.prevAccumulateTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevNumFramesAccumTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.currNumFramesAccumTexture, AccessFlags.ReadWrite);
                }


                if (passData.requireRayTracing)
                {
                    var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeShaders>();
                    passData.rtrtShader = runtimeShaders.rayTracingReflections;
                    passData.rtas = cameraData.rayTracingSystem.RequestAccelerationStructure();
                    passData.rayTracingLights = GPULights.UseRayTracingLights(builder, frameData);
                }

                // Sky Environment
                {
                    passData.ambientProbe = resourceData.skyAmbientProbe;
                    passData.reflectProbe = resourceData.skyReflectionProbe;

                    builder.UseBuffer(passData.ambientProbe);
                    builder.UseTexture(passData.reflectProbe);
                }


                // Setup builder state
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true); // enable this if raytracing, and we can not use async. due to raytraced object shading.
                //builder.EnableAsyncCompute(true);


                builder.SetRenderFunc((SSRPassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context.cmd);
                });

                return passData.ssrLightingTexture;
            }

        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            cmd.SetKeyword(ShaderGlobalKeywords.ScreenSpaceReflection, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
        }


        static class ShaderConstants
        {
            public static readonly int gDispatchIndirectBuffer = Shader.PropertyToID("gDispatchIndirectBuffer");
            public static readonly int gTileList = Shader.PropertyToID("gTileList");
            public static readonly int _CameraDepthPyramidTexture = Shader.PropertyToID("_CameraDepthPyramidTexture");
            public static readonly int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
            public static readonly int _SSRHitPointTexture = Shader.PropertyToID("_SSRHitPointTexture");
            public static readonly int _SSRRayInfoTexture = Shader.PropertyToID("_SSRRayInfoTexture");
            public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
            public static readonly int _ColorPyramidTexture = Shader.PropertyToID("_ColorPyramidTexture");
            public static readonly int _SsrAccumPrev = Shader.PropertyToID("_SsrAccumPrev");
            public static readonly int _SSRAccumTexture = Shader.PropertyToID("_SSRAccumTexture");
            public static readonly int _SSRHitDepthTexture = Shader.PropertyToID("_SSRHitDepthTexture");
            public static readonly int _SSRAvgRadianceTexture = Shader.PropertyToID("_SSRAvgRadianceTexture");
            public static readonly int _SsrLightingTexture = Shader.PropertyToID("_SsrLightingTexture");
            public static readonly int _SSRPrevNumFramesAccumTexture = Shader.PropertyToID("_SSRPrevNumFramesAccumTexture");
            public static readonly int _SSRNumFramesAccumTexture = Shader.PropertyToID("_SSRNumFramesAccumTexture");
            public static readonly int _RayHitColorTexture = Shader.PropertyToID("_RayHitColorTexture");
            public static readonly int _AmbientProbeData = Shader.PropertyToID("_AmbientProbeData");
            public static readonly int _SkyTexture = Shader.PropertyToID("_SkyTexture");
            public static readonly int _DispatchRayDirTexture = Shader.PropertyToID("_DispatchRayDirTexture");
            public static readonly int _DispatchRayCoordBuffer = Shader.PropertyToID("_DispatchRayCoordBuffer");
            public static readonly int _RayIndirectBuffer = Shader.PropertyToID("_RayIndirectBuffer");
            public static readonly int _RayTracingLightingTextureRW = Shader.PropertyToID("_RayTracingLightingTextureRW");
        }
    }
}
