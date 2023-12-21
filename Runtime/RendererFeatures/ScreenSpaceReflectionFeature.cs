using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    internal class ScreenSpaceReflectionSettings
    {

    }

    [DisallowMultipleRendererFeature("Screen Space Reflection")]
    [Tooltip("The GPU compute screenSpace reflection.")]
    internal class ScreenSpaceReflectionFeature : ScriptableRendererFeature
    {
        // Serialized Fields
        [SerializeField] private ScreenSpaceReflectionSettings m_Settings = new ScreenSpaceReflectionSettings();

        [SerializeField, HideInInspector]
        [Reload("Shaders/ScreenSpaceLighting/ScreenSpaceReflections.compute")]
        private ComputeShader m_Shader;

        // Private Fields
        private ScreenSpaceReflectionPass m_SSRPass = null;

        // Constants

        /// <inheritdoc/>
        public override void Create()
        {
#if UNITY_EDITOR
            ResourceReloader.TryReloadAllNullIn(this, UniversalRenderPipelineAsset.packagePath);
#endif

            if (m_SSRPass == null)
                m_SSRPass = new ScreenSpaceReflectionPass(m_Shader);

            m_SSRPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Shader == null)
            {
                Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing computeShader. {1} render pass will not be added.", GetType().Name, name);
                return;
            }

            bool shouldEnqueue = m_SSRPass.Setup(m_Settings, (UniversalRenderer)renderer);
            if (shouldEnqueue)
            {
                renderer.EnqueuePass(m_SSRPass);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            m_SSRPass?.Dispose();
            m_SSRPass = null;
        }

    }
}

