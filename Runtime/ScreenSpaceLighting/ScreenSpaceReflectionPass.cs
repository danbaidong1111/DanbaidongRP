using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_ProfilerTag = "ScreenSpaceReflection";
        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        // Public Variables

        // Private Variables
        private ComputeShader m_Compute;
        private ScreenSpaceReflectionSettings m_CurrentSettings;
        private RTHandle m_RenderTarget;
        private int m_SSRTracingKernel;
        private ScreenSpaceReflection m_volumeSettings;
        private UniversalRenderer m_Renderer;

        // Constants

        // Statics

        internal ScreenSpaceReflectionPass(ComputeShader computeShader)
        {
            m_CurrentSettings = new ScreenSpaceReflectionSettings();
            m_Compute = computeShader;

            m_SSRTracingKernel = m_Compute.FindKernel("ScreenSpaceReflectionsTracing");
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

            RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SSR_Hit_Point_Texture");
            cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

            ConfigureTarget(m_RenderTarget);
            ConfigureClear(ClearFlag.All, Color.black);
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
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var stencilBuffer = renderingData.cameraData.renderer.cameraDepthTargetHandle.rt.depthBuffer;
                float n = renderingData.cameraData.camera.nearClipPlane;
                float f = renderingData.cameraData.camera.farClipPlane;
                float thickness = m_volumeSettings.depthBufferThickness.value;
                float thicknessScale = 1.0f / (1.0f + thickness);
                float thicknessBias = -n / (f - n) * (thickness * thicknessScale);

                cmd.EnableShaderKeyword("SSR_APPROX");
                //cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_StencilTexture", stencilBuffer, 0, RenderTextureSubElement.Stencil);

                cmd.SetComputeTextureParam(m_Compute, m_SSRTracingKernel, "_SsrHitPointTexture", m_RenderTarget);

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
                
                



                cmd.DispatchCompute(m_Compute, m_SSRTracingKernel, RenderingUtils.DivRoundUp(m_RenderTarget.rt.width, 8), RenderingUtils.DivRoundUp(m_RenderTarget.rt.height, 8), 1);
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
            m_RenderTarget?.Release();
        }
    }
}