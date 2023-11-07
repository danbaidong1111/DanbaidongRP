using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    internal class PerObjectShadowSettings
    {
        internal enum FullScreenScale
        {
            None = 0,
            _2x = 1,
            _4x = 2,
        }
        // Parameters
        [Range(0, 32)]
        [SerializeField] internal int maxObjectsCount = 32;
        [SerializeField] internal float maxDrawDistance = 1000f;
        [SerializeField] internal ShadowResolution shadowMapResolution = ShadowResolution._512;
        [Header("ScreenSpaceShadows")]
        [SerializeField] internal bool objectsScreenSpaceShadows = false;
        [SerializeField] internal FullScreenScale fullScreenScale = FullScreenScale.None;
        [SerializeField] internal SoftShadowQuality softShadowQuality = SoftShadowQuality.Low;
        public int GetEquivalentShadowMapResolution()
        {
            switch (shadowMapResolution)
            {
                case ShadowResolution._256:
                    return 256;
                case ShadowResolution._512:
                    return 512;
                case ShadowResolution._1024:
                    return 1024;
                case ShadowResolution._2048:
                    return 2048;
                case ShadowResolution._4096:
                    return 4096;
                default: // backup
                    return 1024;
            }
        }

        public SoftShadowQuality? GetSoftShadowQuality()
        {
            return (softShadowQuality == SoftShadowQuality.UsePipelineSettings)
                    ? UniversalRenderPipeline.asset?.softShadowQuality : softShadowQuality;
        }

        public int GetScreenSpaceShadowTexScale()
        {
            return (int)fullScreenScale;
        }
    }

    /// <summary>
    /// Renders a shadow map for per Objects.
    /// TODO: Support RenderGraph.
    /// </summary>
    [DisallowMultipleRendererFeature("Per Object Shadow Feature")]
    [Tooltip("Per Object Shadow, render shadowmaps for every single object")]
    public class PerObjectShadowFeature : ScriptableRendererFeature
    {
        private static SharedObjectShadowEntityManager sharedObjectShadowEntityManager { get; } = new SharedObjectShadowEntityManager();


        // Serialized Fields
        [SerializeField] private PerObjectShadowSettings m_Settings = new PerObjectShadowSettings();

        // Private Fields
        private bool m_RecreateSystems;
        private Light m_DirectLight;// We can't get lightdata before cameraPreCull, this stores last frame light.
        private PerObjectShadowCasterPass m_PerObjectShadowCasterPass = null;
        private PerObjectScreenSpaceShadowsPass m_PerObjectScreenSpaceShadowsPass = null;

        // Entities
        private ObjectShadowEntityManager m_ObjectShadowEntityManager;
        private ObjectShadowUpdateCachedSystem m_ObjectShadowUpdateCachedSystem;
        private ObjectShadowUpdateCullingGroupSystem m_ObjectShadowUpdateCullingGroupSystem;
        private ObjectShadowUpdateCulledSystem m_ObjectShadowUpdateCulledSystem;
        private ObjectShadowCreateDrawCallSystem m_ObjectShadowCreateDrawCallSystem;
        private ObjectShadowDrawSystem m_ObjectShadowDrawSystem;

        // Constants


        /// <inheritdoc/>
        public override void Create()
        {

            m_RecreateSystems = true;
        }

        private bool RecreateSystemsIfNeeded(ScriptableRenderer renderer, in CameraData cameraData)
        {
            if (!m_RecreateSystems)
                return true;

            if (m_ObjectShadowEntityManager == null)
            {
                m_ObjectShadowEntityManager = sharedObjectShadowEntityManager.Get();
            }

            m_ObjectShadowUpdateCachedSystem = new ObjectShadowUpdateCachedSystem(m_ObjectShadowEntityManager);
            m_ObjectShadowUpdateCulledSystem = new ObjectShadowUpdateCulledSystem(m_ObjectShadowEntityManager);
            m_ObjectShadowCreateDrawCallSystem = new ObjectShadowCreateDrawCallSystem(m_ObjectShadowEntityManager, m_Settings.maxDrawDistance);
            m_ObjectShadowUpdateCullingGroupSystem = new ObjectShadowUpdateCullingGroupSystem(m_ObjectShadowEntityManager, m_Settings.maxDrawDistance);
            m_ObjectShadowDrawSystem = new ObjectShadowDrawSystem(m_ObjectShadowEntityManager);

            m_PerObjectShadowCasterPass = new PerObjectShadowCasterPass(m_ObjectShadowCreateDrawCallSystem);
            m_PerObjectScreenSpaceShadowsPass = new PerObjectScreenSpaceShadowsPass(m_ObjectShadowDrawSystem);

            m_PerObjectShadowCasterPass.renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
            m_PerObjectScreenSpaceShadowsPass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;

            m_RecreateSystems = false;
            return true;
        }

        /// <inheritdoc/>
        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            if (cameraData.cameraType == CameraType.Preview)
                return;

            if (m_DirectLight == null)
                return;

            bool isSystemsValid = RecreateSystemsIfNeeded(renderer, cameraData);
            if (!isSystemsValid)
                return;

            // Update Manager and Execute culling systems
            m_ObjectShadowEntityManager.Update();

            m_ObjectShadowUpdateCachedSystem.Execute(m_DirectLight);
            m_ObjectShadowUpdateCullingGroupSystem.Execute(cameraData.camera);

            //string chunksInfo = "Manager chunkCount: " + m_ObjectShadowEntityManager.chunkCount;
            //for (int i = 0; i < m_ObjectShadowEntityManager.chunkCount; i ++)
            //{
            //    chunksInfo += "\nEntityChunk" + i + ": count " + m_ObjectShadowEntityManager.entityChunks[i].count + " capcity " + m_ObjectShadowEntityManager.entityChunks[i].capacity;
            //}
            //Debug.Log(chunksInfo);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Exclude PreView camera
            if (renderingData.cameraData.cameraType == CameraType.Preview)
            {
                return;
            }

            // Directional mainLight check.
            {
                int shadowLightIndex = renderingData.lightData.mainLightIndex;
                if (shadowLightIndex == -1)
                    return;

                VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
                m_DirectLight = shadowLight.light;
                if (m_DirectLight.shadows == LightShadows.None)
                    return;

                if (shadowLight.lightType != LightType.Directional)
                {
                    Debug.LogWarning("Only directional lights are supported as main light.");
                }
            }

            // ObjectShadowSystem check
            bool isSystemsValid = RecreateSystemsIfNeeded(renderer, renderingData.cameraData);
            if (!isSystemsValid)
                return;


            // Execute systems
            m_ObjectShadowUpdateCulledSystem.Execute();

            //string chunksInfo = "Manager chunkCount: " + m_ObjectShadowEntityManager.chunkCount;
            //for (int i = 0; i < m_ObjectShadowEntityManager.chunkCount; i++)
            //{
            //    chunksInfo += "\nCulledChunk" + i + ": count " + m_ObjectShadowEntityManager.culledChunks[i].count + " visible[" + m_ObjectShadowEntityManager.culledChunks[i].visibleObjectShadowCount + "] ";
            //    for (int index = 0; index < m_ObjectShadowEntityManager.culledChunks[i].visibleObjectShadowCount; index++)
            //    {
            //        chunksInfo += " " + m_ObjectShadowEntityManager.culledChunks[i].visibleObjectShadowIndexArray[index];
            //    }

            //}
            //Debug.Log(chunksInfo);

            int maxVisibleCountPerChunk = 0;
            for (int i = 0; i < m_ObjectShadowEntityManager.chunkCount; i++)
            {
                maxVisibleCountPerChunk = Mathf.Max(maxVisibleCountPerChunk, m_ObjectShadowEntityManager.culledChunks[i].visibleObjectShadowCount);
            }
            // Exist when no visible entity
            if (maxVisibleCountPerChunk == 0)
            {
                ClearRenderingState(renderingData.commandBuffer);
                return;
            }



            if (m_PerObjectShadowCasterPass.Setup(m_Settings, ref renderingData, m_ObjectShadowEntityManager))
            {
                renderer.EnqueuePass(m_PerObjectShadowCasterPass);


                if (m_Settings.objectsScreenSpaceShadows)
                {
                    if (m_PerObjectScreenSpaceShadowsPass.Setup(m_Settings))
                        renderer.EnqueuePass(m_PerObjectScreenSpaceShadowsPass);
                }
                else
                {
                    ClearRenderingState(renderingData.commandBuffer);
                }
            }
        }

        /// <summary>
        /// Clear pass keywords.
        /// </summary>
        /// <param name="cmd"></param>
        private void ClearRenderingState(CommandBuffer cmd)
        {
            m_PerObjectScreenSpaceShadowsPass.ClearRenderingState(cmd);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            m_PerObjectShadowCasterPass?.Dispose();
            m_PerObjectShadowCasterPass = null;

            m_PerObjectScreenSpaceShadowsPass?.Dispose();
            m_PerObjectScreenSpaceShadowsPass = null;

            if (m_ObjectShadowEntityManager != null)
            {
                m_ObjectShadowEntityManager = null;
                sharedObjectShadowEntityManager.Release(m_ObjectShadowEntityManager);
            }
        }

    }

}