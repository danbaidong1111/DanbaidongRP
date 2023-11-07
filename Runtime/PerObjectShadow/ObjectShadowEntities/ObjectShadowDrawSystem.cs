using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    internal class ObjectShadowDrawSystem
    {
        private static class PerObjectShadowDrawConstant
        {
            public static int _PerObjectWorldToShadow;
            public static int _PerObjectUVScaleOffset;
            public static int _PerObjectShadowScaledScreenParams;
        }
        private ObjectShadowEntityManager m_EntityManager;
        private ProfilingSampler m_Sampler;
        private Matrix4x4[] m_shadowToWorlds;
        private Matrix4x4[] m_worldToShadows;
        private Vector4[] m_uvScaleOffsets;

        private static int s_MaxObjectsNum = 64;

        public ObjectShadowDrawSystem(ObjectShadowEntityManager entityManager)
        {
            m_EntityManager = entityManager;
            m_Sampler = new ProfilingSampler("ObjectShadowDrawSystem.Execute");
            m_shadowToWorlds = new Matrix4x4[s_MaxObjectsNum];
            m_worldToShadows = new Matrix4x4[s_MaxObjectsNum];
            m_uvScaleOffsets = new Vector4[s_MaxObjectsNum];

            PerObjectShadowDrawConstant._PerObjectWorldToShadow = Shader.PropertyToID("_PerObjectWorldToShadow");
            PerObjectShadowDrawConstant._PerObjectUVScaleOffset = Shader.PropertyToID("_PerObjectUVScaleOffset");
            PerObjectShadowDrawConstant._PerObjectShadowScaledScreenParams = Shader.PropertyToID("_PerObjectShadowScaledScreenParams");
        }

        public void Execute(CommandBuffer cmd, Vector2 rtSize)
        {
            using (new ProfilingScope(cmd, m_Sampler))
            {
                for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                {
                    Execute(
                        cmd, rtSize,
                        m_EntityManager.entityChunks[i],
                        m_EntityManager.cachedChunks[i],
                        m_EntityManager.drawCallChunks[i],
                        m_EntityManager.entityChunks[i].count);
                }
            }
        }

        private void Execute(CommandBuffer cmd, Vector2 rtSize, ObjectShadowEntityChunk entityChunk, ObjectShadowCachedChunk cacheChunk, ObjectShadowDrawCallChunk drawCallChunk, int count)
        {
            cacheChunk.currentJobHandle.Complete();
            drawCallChunk.currentJobHandle.Complete();

            Material material = entityChunk.material;
            int passIndex = cacheChunk.screenSpaceShadowPassIndex;

            if (count == 0 || passIndex == -1 || material == null)
                return;

            if (SystemInfo.supportsInstancing && material.enableInstancing)
            {
                DrawInstanced(cmd, rtSize, entityChunk, cacheChunk, drawCallChunk, material, passIndex);
            }
            else
            {
                //Draw(cmd, entityChunk, cacheChunk, drawCallChunk, material, passIndex);
            }

        }

        private void DrawInstanced(CommandBuffer cmd, Vector2 rtSize, ObjectShadowEntityChunk entityChunk, ObjectShadowCachedChunk cacheChunk, ObjectShadowDrawCallChunk drawCallChunk, Material material, int passIndex)
        {
            var mesh = PerObjectShadowUtils.shadowProjectorMesh;
            int instanceCount = drawCallChunk.drawCallCount;
            Vector4 scaledScreenParams = new Vector4(rtSize.x, rtSize.y, 1.0f / rtSize.x, 1.0f / rtSize.y);
            material.SetVector(PerObjectShadowDrawConstant._PerObjectShadowScaledScreenParams, scaledScreenParams);


            var shadowPToWorld = drawCallChunk.shadowToWorldMatrices.Reinterpret<Matrix4x4>();
            NativeArray<Matrix4x4>.Copy(shadowPToWorld, 0, m_shadowToWorlds, 0, instanceCount);

            var shadowTransform = drawCallChunk.shadowTransforms.Reinterpret<Matrix4x4>();
            NativeArray<Matrix4x4>.Copy(shadowTransform, 0, m_worldToShadows, 0, instanceCount);

            var usScaleOffset = drawCallChunk.uvScaleOffsets.Reinterpret<Vector4>();
            NativeArray<Vector4>.Copy(usScaleOffset, 0, m_uvScaleOffsets, 0, instanceCount);

            cacheChunk.propertyBlock.SetMatrixArray(PerObjectShadowDrawConstant._PerObjectWorldToShadow, m_worldToShadows);
            cacheChunk.propertyBlock.SetVectorArray(PerObjectShadowDrawConstant._PerObjectUVScaleOffset, m_uvScaleOffsets);

            cmd.DrawMeshInstanced(mesh, 0, material, passIndex, m_shadowToWorlds, instanceCount, cacheChunk.propertyBlock);

        }

    }
}