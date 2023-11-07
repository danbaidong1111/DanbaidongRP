namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Writes culling results into <see cref="ObjectShadowCulledChunk"/>.
    /// </summary>
    internal class ObjectShadowUpdateCulledSystem
    {
        private ObjectShadowEntityManager m_EntityManager;
        private ProfilingSampler m_Sampler;

        public ObjectShadowUpdateCulledSystem(ObjectShadowEntityManager entityManager)
        {
            m_EntityManager = entityManager;
            m_Sampler = new ProfilingSampler("ObjectShadowUpdateCulledSystem.Execute");
        }

        public void Execute()
        {
            using (new ProfilingScope(null, m_Sampler))
            {
                for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                    Execute(m_EntityManager.culledChunks[i], m_EntityManager.culledChunks[i].count);
            }
        }

        private void Execute(ObjectShadowCulledChunk culledChunk, int count)
        {
            if (count == 0)
                return;

            culledChunk.currentJobHandle.Complete();

            CullingGroup cullingGroup = culledChunk.cullingGroups;
            culledChunk.visibleObjectShadowCount = cullingGroup.QueryIndices(true, culledChunk.visibleObjectShadowIndexArray, 0);
            culledChunk.visibleObjectShadowIndices.CopyFrom(culledChunk.visibleObjectShadowIndexArray);
        }
    }
}