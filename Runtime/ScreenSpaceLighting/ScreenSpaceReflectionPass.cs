using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSRTracingProfilerTag = "SSRTracing";
        private static string m_SSRReprojectionProfilerTag = "SSRReprojection";
        private static ProfilingSampler m_SSRTracingProfilingSampler = new ProfilingSampler(m_SSRTracingProfilerTag);
        private static ProfilingSampler m_SSRReprojectionProfilingSampler = new ProfilingSampler(m_SSRReprojectionProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;
        private ScreenSpaceReflectionSettings m_CurrentSettings;

        private RTHandle m_SSRHitPointTexture;
        private RTHandle m_SSRAccumTexture;

        private int m_SSRTracingKernel;
        private int m_SSRReprojectionKernel;
        private ScreenSpaceReflection m_volumeSettings;
        private UniversalRenderer m_Renderer;

        // Constants

        // Statics

        internal ScreenSpaceReflectionPass(ComputeShader computeShader)
        {
            m_CurrentSettings = new ScreenSpaceReflectionSettings();
            m_Compute = computeShader;

            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
            m_SSRReprojectionKernel = m_Compute.FindKernel("ScreenSpaceReflectionsReprojection");
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

            ConfigureInput(ScriptableRenderPassInput.HistoryColor);

            return m_Compute != null && m_volumeSettings != null;
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16_UNorm;
            desc.enableRandomWrite = true;

            RenderingUtils.ReAllocateIfNeeded(ref m_SSRHitPointTexture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSRHitPointTexture");
            
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_SSRAccumTexture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "m_SSRAccumTexture");

            cmd.SetGlobalTexture(m_SSRHitPointTexture.name, m_SSRHitPointTexture.nameID);
            cmd.SetGlobalTexture(m_SSRAccumTexture.name, m_SSRAccumTexture.nameID);
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
            using (new ProfilingScope(cmd, m_SSRTracingProfilingSampler))
            {
                cmd.SetRenderTarget(m_SSRHitPointTexture);
                cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 0, 0);

                var stencilBuffer = renderingData.cameraData.renderer.cameraDepthTargetHandle.rt.depthBuffer;
                float n = renderingData.cameraData.camera.nearClipPlane;
                float f = renderingData.cameraData.camera.farClipPlane;
                float thickness = m_volumeSettings.depthBufferThickness.value;
                float thicknessScale = 1.0f / (1.0f + thickness);
                float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

                cmd.EnableShaderKeyword("SSR_APPROX");
                //cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_StencilTexture", stencilBuffer, 0, RenderTextureSubElement.Stencil);

                cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_SSRHitPointTexture", m_SSRHitPointTexture);

                // Constant Params
                // Should use ConstantBuffer
                // ConstantBuffer.Push(ctx.cmd, data.cb, cs, HDShaderIDs._ShaderVariablesScreenSpaceReflection);
                {
                    cmd.SetComputeIntParam(m_Compute, "_SsrStencilBit", (int)(1 << 3));
                    cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeEnd", 1 - m_volumeSettings.minSmoothness);
                    cmd.SetComputeIntParam(m_Compute, "_SsrDepthPyramidMaxMip", m_Renderer.depthBufferMipChainInfo.mipLevelCount - 1);
                    cmd.SetComputeIntParam(m_Compute, "_SsrReflectsSky", m_volumeSettings.reflectSky.value ? 1 : 0);
                    cmd.SetComputeIntParam(m_Compute, "_SsrIterLimit", m_volumeSettings.rayMaxIterations);
                    cmd.SetComputeFloatParam(m_Compute, "_SsrThicknessScale", thicknessScale);
                    cmd.SetComputeFloatParam(m_Compute, "_SsrThicknessBias", thicknessBias);

                    cmd.SetComputeBufferParam(m_Compute, m_SSRTracingKernel, "_DepthPyramidMipLevelOffsets", m_Renderer.depthBufferMipChainInfo.GetOffsetBufferData(m_Renderer.depthPyramidMipLevelOffsetsBuffer));
                }


                cmd.DispatchCompute(m_Compute, m_SSRTracingKernel, RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRHitPointTexture.rt.height, 8), 1);
            }

            using (new ProfilingScope(cmd, m_SSRReprojectionProfilingSampler))
            {

                cmd.SetRenderTarget(m_SSRAccumTexture);
                cmd.ClearRenderTarget(RTClearFlags.Color, new Color(0,0,0,0), 0, 0);

                cmd.SetComputeTextureParam(m_Compute, m_SSRReprojectionKernel, "_SSRHitPointTexture", m_SSRHitPointTexture);
                cmd.SetComputeTextureParam(m_Compute, m_SSRReprojectionKernel, "_ColorPyramidTexture", renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.SetComputeTextureParam(m_Compute, m_SSRReprojectionKernel, "_SSRAccumTexture", m_SSRAccumTexture);

                // Constant Params
                {
                    float ssrRoughnessFadeEnd = 1 - m_volumeSettings.minSmoothness;
                    float roughnessFadeStart = 1 - m_volumeSettings.smoothnessFadeStart;
                    float roughnessFadeLength = ssrRoughnessFadeEnd - roughnessFadeStart;

                    cmd.SetComputeFloatParam(m_Compute, "_SsrEdgeFadeRcpLength", Mathf.Min(1.0f / m_volumeSettings.screenFadeDistance.value, float.MaxValue));
                    cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeRcpLength", (roughnessFadeLength != 0) ? (1.0f / roughnessFadeLength) : 0);
                    cmd.SetComputeFloatParam(m_Compute, "_SsrRoughnessFadeEndTimesRcpLength", ((roughnessFadeLength != 0) ? (ssrRoughnessFadeEnd * (1.0f / roughnessFadeLength)) : 1));
                }

                cmd.DispatchCompute(m_Compute, m_SSRReprojectionKernel, RenderingUtils.DivRoundUp(m_SSRAccumTexture.rt.width, 8), RenderingUtils.DivRoundUp(m_SSRAccumTexture.rt.height, 8), 1);
            }

            cmd.SetGlobalTexture("_SsrLightingTexture", m_SSRAccumTexture);
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
            m_SSRAccumTexture?.Release();
        }
    }
}