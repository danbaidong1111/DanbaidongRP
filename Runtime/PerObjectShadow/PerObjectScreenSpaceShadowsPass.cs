using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace UnityEngine.Rendering.Universal
{
    internal class PerObjectScreenSpaceShadowsPass : ScriptableRenderPass
    {
        private static class PerObjectShadowProjectorConstant
        {
            public static int _PerObjectWorldToShadow;
            public static int _PerObjectUVScaleOffset;
            public static int _PerObjectShadowParams;
            public static int _PerObjectShadowScaledScreenParams;
        }
        // Profiling tag
        private static string m_ProfilerTag = "PerObjectScreenSpaceShadows";
        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        // Public Variables

        // Private Variables
        private PerObjectShadowSettings m_CurrentSettings;
        private RTHandle m_ScreenSpaceShadowTexture;
        private ObjectShadowDrawSystem m_DrawSystem;
        private ShaderTagId m_ShaderTagId;

        // Constants


        // Statics


        internal PerObjectScreenSpaceShadowsPass(ObjectShadowDrawSystem drawSystem)
        {
            m_DrawSystem = drawSystem;
            m_ShaderTagId = new ShaderTagId(PerObjectShadowShaderPassNames.PerObjectScreenSpaceShadow);

            PerObjectShadowProjectorConstant._PerObjectWorldToShadow = Shader.PropertyToID("_PerObjectWorldToShadow");
            PerObjectShadowProjectorConstant._PerObjectUVScaleOffset = Shader.PropertyToID("_PerObjectUVScaleOffset");
            PerObjectShadowProjectorConstant._PerObjectShadowParams = Shader.PropertyToID("_PerObjectShadowParams");
            PerObjectShadowProjectorConstant._PerObjectShadowScaledScreenParams = Shader.PropertyToID("_PerObjectShadowScaledScreenParams");
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_ScreenSpaceShadowTexture?.Release();
        }

        internal bool Setup(PerObjectShadowSettings settings)
        {
            m_CurrentSettings = settings;

            ConfigureInput(ScriptableRenderPassInput.Depth);

            return true;
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            int downSampleScale = m_CurrentSettings.GetScreenSpaceShadowTexScale();
            desc.width = desc.width >> downSampleScale;
            desc.height = desc.height >> downSampleScale;
            desc.useMipMap = false;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowTexture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_PerObjectScreenSpaceShadowmapTexture");
            cmd.SetGlobalTexture(m_ScreenSpaceShadowTexture.name, m_ScreenSpaceShadowTexture.nameID);

            ConfigureTarget(m_ScreenSpaceShadowTexture);
            ConfigureClear(ClearFlag.Color, Color.white);
        }


        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // Params
                float softShadowQuality = (float)m_CurrentSettings.GetSoftShadowQuality();
                float shadowStrength = 1.0f;
                cmd.SetGlobalVector(PerObjectShadowProjectorConstant._PerObjectShadowParams, new Vector4(softShadowQuality, shadowStrength, 0, 0));

                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.PerObjectScreenSpaceShadow, true);

                // Draw System
                Vector2 rtSize = new Vector2(m_ScreenSpaceShadowTexture.rt.width, m_ScreenSpaceShadowTexture.rt.height);
                m_DrawSystem.Execute(cmd, rtSize);

                CullingResults cullingResults = renderingData.cullResults;
                SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, sortingCriteria);
                FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            }
        }

        /// <summary>
        /// Clear Keyword.
        /// </summary>
        /// <param name="cmd"></param>
        public void ClearRenderingState(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.PerObjectScreenSpaceShadow, false);
        }
    }

}