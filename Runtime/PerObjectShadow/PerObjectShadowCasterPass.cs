using System;
using System.Collections.Generic;
using Unity.Collections;
using static UnityEngine.Rendering.Universal.RenderingLayerUtils;

namespace UnityEngine.Rendering.Universal
{
    internal class PerObjectShadowCasterPass : ScriptableRenderPass
    {
        private static class PerObjectShadowConstantBuffer
        {
            public static int _WorldToShadow;
        }
        // Profiling tag
        private static string m_ProfilerTag = "PerObjectShadow";
        private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        // Public Variables

        // Private Variables
        private PerObjectShadowSettings m_CurrentSettings;
        private ObjectShadowEntityManager m_EntityManager;
        private ObjectShadowCreateDrawCallSystem m_DrawCallSystem;
        private List<PerObjectShadowData> m_ObjectsShadowDataList;
        private int m_TileResolution = 0;
        private int m_ValidObjectsNum = 0;
        /// Summary:    m_ObjectsListUpdateStamp controls UpdataObjectsShadowDataList() timing;
        /// Values:     0: update; <0: noupdate; >0: interval--;
        /// Note:       "Manually" UpdateMode should implements a public method;
        private int m_PerObjectShadowmapID;
        private RTHandle m_PerObjectShadowMapTexture;

        private Matrix4x4[] m_PerObjectShadowMatrices;

        // Constants
        private const int k_ShadowmapBufferBits = 16;

        // Statics



        internal PerObjectShadowCasterPass(ObjectShadowCreateDrawCallSystem drawCallSystem)
        {
            m_DrawCallSystem = drawCallSystem;
            m_CurrentSettings = new PerObjectShadowSettings();
            
            m_PerObjectShadowMatrices = new Matrix4x4[PerObjectShadowUtils.k_MaxObjectsNum];

            PerObjectShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_PerObjectWorldToShadowArray");

            m_PerObjectShadowmapID = Shader.PropertyToID("_PerObjectShadowmapTexture");
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_PerObjectShadowMapTexture?.Release();
        }

        /// <summary>
        /// Sets up the pass.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="renderingData"></param>
        /// <returns>True if the pass should be enqueued, otherwise false.</returns>
        internal bool Setup(PerObjectShadowSettings settings, ref RenderingData renderingData, ObjectShadowEntityManager entityManager)
        {
            m_CurrentSettings = settings;
            m_EntityManager = entityManager;

            // MainLight directional check outside.

            // No need to ConfigureInput


            m_ValidObjectsNum = 0;
            for (int chunkIndex = 0; chunkIndex < m_EntityManager.culledChunks.Count; chunkIndex++)
            {
                m_ValidObjectsNum += m_EntityManager.culledChunks[chunkIndex].visibleObjectShadowCount;
            }

            // Resolution calculate
            int resolutionSetting = m_CurrentSettings.GetEquivalentShadowMapResolution();
            Vector2Int resolution = PerObjectShadowUtils.GetPerObjectShadowMapResolution(resolutionSetting, m_ValidObjectsNum);
            m_TileResolution = PerObjectShadowUtils.GetPerObjectTileResolutionInAtlas(resolution.x, resolution.y, m_ValidObjectsNum);


            m_DrawCallSystem.Execute(m_TileResolution, resolution.x, resolution.y);
            
            // RTHandle.ReAllocateIfNeeded
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_PerObjectShadowMapTexture, resolution.x, resolution.y, k_ShadowmapBufferBits, name: "_PerObjectShadowmapTexture");

            return true;
        }

        // TODO: Need to setup For Empty Rendering?
        //bool SetupForEmptyRendering()


        internal bool SetupObjectsShadowDataList(PerObjectShadowSettings settings)
        {
            if (!InitObjectsShadowDataList(settings.maxObjectsCount, ref m_ObjectsShadowDataList))
                return false;

            // Update List, set renderers, view matrix, proj matrix
            m_ValidObjectsNum = UpdateObjectsShadowDataList(settings.maxObjectsCount, ref m_ObjectsShadowDataList);

            return true;
        }

        internal bool InitObjectsShadowDataList(int maxSize, ref List<PerObjectShadowData> objShadowDataList)
        {
            bool shouldInit = false;
            if (objShadowDataList == null)
            {
                objShadowDataList = new List<PerObjectShadowData>(maxSize);
                shouldInit = true;
            }

            if (objShadowDataList.Capacity != maxSize)
            {
                objShadowDataList.Clear();
                objShadowDataList.Capacity = maxSize;
                shouldInit = true;
            }


            if (shouldInit)
            {
                for (int i = 0; i < objShadowDataList.Capacity; i++)
                {
                    objShadowDataList.Add(new PerObjectShadowData());
                }
            }

            return objShadowDataList != null && objShadowDataList.Capacity == maxSize;
        }

        internal int UpdateObjectsShadowDataList(int maxSize, ref List<PerObjectShadowData> objShadowDataList)
        {
            int validObjectNum = 0;
            
            for (int chunkIndex = 0; chunkIndex < m_EntityManager.culledChunks.Count; chunkIndex++)
            {
                for (int countIndex = 0; countIndex < m_EntityManager.culledChunks[chunkIndex].visibleObjectShadowCount; countIndex++)
                {
                    int entityIndex = m_EntityManager.culledChunks[chunkIndex].visibleObjectShadowIndexArray[countIndex];
                    var viewMatrix = m_EntityManager.cachedChunks[chunkIndex].viewMatrices[entityIndex];
                    var projMatrix = m_EntityManager.cachedChunks[chunkIndex].projMatrices[entityIndex];

                    var renderers = m_EntityManager.entityChunks[chunkIndex].objectShadowProjectors[entityIndex].childRenderers;
                    var material = m_EntityManager.entityChunks[chunkIndex].material;
                    var shadowPassIndex = m_EntityManager.cachedChunks[chunkIndex].shadowPassIndex;

                    objShadowDataList[validObjectNum].SetRenderers(renderers);
                    objShadowDataList[validObjectNum].sliceData.viewMatrix = viewMatrix;
                    objShadowDataList[validObjectNum].sliceData.projectionMatrix = projMatrix;
                    objShadowDataList[validObjectNum].material = material;
                    objShadowDataList[validObjectNum].shadowPassIndex = shadowPassIndex;

                    validObjectNum++;
                    if (validObjectNum >= maxSize)
                        return validObjectNum;
                }
            }

            return validObjectNum;
        }

        /// <inheritdoc />
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(m_PerObjectShadowMapTexture);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RenderPerObjectTileShadowmap(ref context, ref renderingData);
            renderingData.commandBuffer.SetGlobalTexture(m_PerObjectShadowmapID, m_PerObjectShadowMapTexture.nameID);
        }

        /// <summary>
        /// Render each tile shadow in atlas
        /// </summary>
        /// <param name="context"></param>
        /// <param name="renderingData"></param>
        internal void RenderPerObjectTileShadowmap(ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var lightData = renderingData.lightData;

            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // Need to start by setting the Camera position as that is not set for passes executed before normal rendering
                cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, renderingData.cameraData.worldSpaceCameraPos);

                for (int i = 0; i < m_EntityManager.chunkCount; i++)
                {
                    var entityChunks = m_EntityManager.entityChunks[i];
                    var cachedChunk = m_EntityManager.cachedChunks[i];
                    var drawCallChunk = m_EntityManager.drawCallChunks[i];

                    cachedChunk.currentJobHandle.Complete();
                    drawCallChunk.currentJobHandle.Complete();

                    Material chunkMaterial = entityChunks.material;
                    int shadowPassIndex = cachedChunk.shadowPassIndex;

                    int instanceCount = drawCallChunk.drawCallCount;

                    for (int instanceIndex = 0; instanceIndex < instanceCount; instanceIndex++)
                    {
                        int entityIndex = drawCallChunk.entityIndices[instanceIndex];

                        Renderer[] renderers = entityChunks.objectShadowProjectors[entityIndex].childRenderers;
                        PerObjectShadowSliceData sliceData = new PerObjectShadowSliceData();
                        sliceData.viewMatrix = cachedChunk.viewMatrices[entityIndex];
                        sliceData.projectionMatrix = cachedChunk.projMatrices[entityIndex];

                        sliceData.shadowTransform = drawCallChunk.shadowTransforms[instanceIndex];
                        sliceData.shadowToWorldMatrix = drawCallChunk.shadowToWorldMatrices[instanceIndex];

                        sliceData.offsetX = drawCallChunk.offsets[instanceIndex].x;
                        sliceData.offsetY = drawCallChunk.offsets[instanceIndex].y;
                        sliceData.resolution = m_TileResolution;
                        sliceData.uvScaleOffset = drawCallChunk.uvScaleOffsets[instanceIndex];

                        // Render sclice shadows
                        Vector4 shadowBias = ShadowUtils.GetShadowBias(ref shadowLight, shadowLightIndex, ref renderingData.shadowData, sliceData.projectionMatrix, sliceData.resolution);
                        PerObjectShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, shadowBias);
                        PerObjectShadowUtils.RenderPerObjectShadowSlice(renderers, cmd, ref context, ref sliceData, chunkMaterial, shadowPassIndex);
                    }
                }

                //SetupPerObjectShadowReceiverConstants(cmd, ref shadowLight);
            }

            return;
        }

        /// <summary>
        /// Setup ShadowReceiver Constants
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="shadowLight"></param>
        internal void SetupPerObjectShadowReceiverConstants(CommandBuffer cmd, ref VisibleLight shadowLight)
        {
            int matricesIndex = 0;
            foreach (PerObjectShadowData data in m_ObjectsShadowDataList)
            {
                if (data == null || !data.IsDataValid())
                    continue;
                m_PerObjectShadowMatrices[matricesIndex] = data.sliceData.shadowTransform;
                matricesIndex++;
            }

            for (int i = matricesIndex; i < m_PerObjectShadowMatrices.Length; i++)
            {
                m_PerObjectShadowMatrices[i] = Matrix4x4.zero;
            }


            cmd.SetGlobalMatrixArray(PerObjectShadowConstantBuffer._WorldToShadow, m_PerObjectShadowMatrices);
        }


        /// <summary>
        /// ObjectsShadowDataList used for shadow projector
        /// </summary>
        /// <returns>Null if no perobjectshadow data</returns>
        public List<PerObjectShadowData> GetObjectsShadowDataList()
        {
            if (m_ObjectsShadowDataList == null || m_ObjectsShadowDataList.Count == 0)
            {
                return null;
            }
            return m_ObjectsShadowDataList;
        }

        public int GetValidObjectsNum()
        {
            return m_ValidObjectsNum;
        }
    }

}
