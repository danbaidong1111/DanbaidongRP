using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Contains information about <see cref="ObjectShadowEntity"/> draw calls.
    /// </summary>
    internal class ObjectShadowDrawCallChunk : ObjectShadowChunk
    {
        // CurrentChunk to world matrix
        public NativeArray<float4x4> shadowToWorldMatrices;
        // WorldPos to shadowMap uv
        public NativeArray<float4x4> shadowTransforms;
        // ShadowMap pixel offset
        public NativeArray<int2> offsets;
        // ShadowMap uv scale offset
        public NativeArray<float4> uvScaleOffsets;

        
        // CurrentChunk drawCalls
        public NativeArray<int> drawCallCounts;
        public NativeArray<int> entityIndices;

        public int drawCallCount { set { drawCallCounts[0] = value; } get => drawCallCounts[0]; }
        public override void RemoveAtSwapBack(int entityIndex)
        {
            RemoveAtSwapBack(ref shadowToWorldMatrices, entityIndex, count);
            RemoveAtSwapBack(ref shadowTransforms, entityIndex, count);
            RemoveAtSwapBack(ref offsets, entityIndex, count);
            RemoveAtSwapBack(ref uvScaleOffsets, entityIndex, count);
            RemoveAtSwapBack(ref entityIndices, entityIndex, count);
            count--;
        }

        public override void SetCapacity(int newCapacity)
        {
            shadowToWorldMatrices.ResizeArray(newCapacity);
            shadowTransforms.ResizeArray(newCapacity);
            offsets.ResizeArray(newCapacity);
            uvScaleOffsets.ResizeArray(newCapacity);
            entityIndices.ResizeArray(newCapacity);
            capacity = newCapacity;
        }

        public override void Dispose()
        {
            drawCallCounts.Dispose();

            if (capacity == 0)
                return;

            shadowToWorldMatrices.Dispose();
            shadowTransforms.Dispose();
            offsets.Dispose();
            uvScaleOffsets.Dispose();
            entityIndices.Dispose();
            count = 0;
            capacity = 0;
        }
    }

    /// <summary>
    /// Outputs draw calls into <see cref="ObjectShadowDrawCallChunk"/>.
    /// Mission: Visiable entities to draw instance
    /// </summary>
    internal class ObjectShadowCreateDrawCallSystem
    {
        private ObjectShadowEntityManager m_EntityManager;
        private ProfilingSampler m_Sampler;
        private float m_MaxDrawDistance;

        /// <summary>
        /// Provides acces to the maximum draw distance.
        /// </summary>
        public float maxDrawDistance
        {
            get { return m_MaxDrawDistance; }
            set { m_MaxDrawDistance = value; }
        }

        public ObjectShadowCreateDrawCallSystem(ObjectShadowEntityManager entityManager, float maxDrawDistance)
        {
            m_EntityManager = entityManager;
            m_Sampler = new ProfilingSampler("ObjectShadowCreateDrawCallSystem.Execute");
            this.maxDrawDistance = maxDrawDistance;
        }

        public void Execute(int tileResolution, int shadowmapWidth, int shadowmapHeight)
        {
            using (new ProfilingScope(null, m_Sampler))
            {
                int shadowmapTileIndex = 0;
                for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                {
                    Execute(m_EntityManager.cachedChunks[i], m_EntityManager.culledChunks[i], m_EntityManager.drawCallChunks[i], 
                        shadowmapTileIndex, tileResolution, shadowmapWidth, shadowmapHeight, m_EntityManager.cachedChunks[i].count);
                    shadowmapTileIndex += m_EntityManager.culledChunks[i].visibleObjectShadowCount;
                }
                    
            }
        }

        private void Execute(ObjectShadowCachedChunk cachedChunk, ObjectShadowCulledChunk culledChunk, ObjectShadowDrawCallChunk drawCallChunk, 
                                int tileIndex, int tileResolution, int shadowmapWidth, int shadowmapHeight, int count)
        {
            if (count == 0)
                return;

            ObjectShadowDrawCallJob drawCallJob = new ObjectShadowDrawCallJob()
            {
                viewMatrices = cachedChunk.viewMatrices,
                projMatrices = cachedChunk.projMatrices,
                usesReversedZBuffer = SystemInfo.usesReversedZBuffer,
                curChunkTileIndexBegin = tileIndex,
                tileResolution = tileResolution,
                shadowmapWidth = shadowmapWidth,
                shadowmapHeight = shadowmapHeight,

                visibleObjectShadowIndices = culledChunk.visibleObjectShadowIndices,
                visibleObjectShadowCount = culledChunk.visibleObjectShadowCount,
                maxDrawDistance = m_MaxDrawDistance,

                shadowToWorldMatrices = drawCallChunk.shadowToWorldMatrices,
                shadowTransforms = drawCallChunk.shadowTransforms,
                offsets = drawCallChunk.offsets,
                uvScaleOffsets = drawCallChunk.uvScaleOffsets,
                drawCallCounts = drawCallChunk.drawCallCounts,
                entityIndices = drawCallChunk.entityIndices,
            };

            var handle = drawCallJob.Schedule(cachedChunk.currentJobHandle);
            drawCallChunk.currentJobHandle = handle;
            cachedChunk.currentJobHandle = handle;
        }


#if ENABLE_BURST_1_0_0_OR_NEWER
        [Unity.Burst.BurstCompile]
#endif
        private struct ObjectShadowDrawCallJob : IJob
        {
            [ReadOnly] public NativeArray<float4x4> viewMatrices;
            [ReadOnly] public NativeArray<float4x4> projMatrices;
            [ReadOnly] public bool usesReversedZBuffer;
            public int curChunkTileIndexBegin;
            public int tileResolution;
            public int shadowmapWidth;
            public int shadowmapHeight;

            [ReadOnly] public NativeArray<int> visibleObjectShadowIndices;
            public int visibleObjectShadowCount;
            public float maxDrawDistance;

            [WriteOnly] public NativeArray<float4x4> shadowToWorldMatrices;
            [WriteOnly] public NativeArray<float4x4> shadowTransforms;
            [WriteOnly] public NativeArray<int2> offsets;
            [WriteOnly] public NativeArray<float4> uvScaleOffsets;
            [WriteOnly] public NativeArray<int> drawCallCounts;
            [WriteOnly] public NativeArray<int> entityIndices;
            public void Execute()
            {
                int instanceIndex = 0;

                for (int i = 0; i < visibleObjectShadowCount; ++i)
                {
                    int entityIndex = visibleObjectShadowIndices[i];

                    float4x4 shadowTransform = PerObjectShadowUtils.GetShadowTransformFloat44(projMatrices[entityIndex], viewMatrices[entityIndex], usesReversedZBuffer);
                    shadowToWorldMatrices[instanceIndex] = PerObjectShadowUtils.GetShadowProjectorToWorldMatrix(projMatrices[entityIndex], viewMatrices[entityIndex]);

                    int2 offset = PerObjectShadowUtils.ComputeSliceOffsetInt2(curChunkTileIndexBegin + instanceIndex, tileResolution);
                    float4 uvScaleOffset = float4.zero;
                    uvScaleOffset.x = (float)tileResolution / shadowmapWidth;
                    uvScaleOffset.y = (float)tileResolution / shadowmapHeight;
                    uvScaleOffset.z = (float)offset.x / shadowmapWidth;
                    uvScaleOffset.w = (float)offset.y / shadowmapHeight;

                    shadowTransform = PerObjectShadowUtils.ApplySliceTransformFloat44(shadowTransform, tileResolution, offset, shadowmapWidth, shadowmapHeight);

                    shadowTransforms[instanceIndex] = shadowTransform;
                    offsets[instanceIndex] = offset;
                    uvScaleOffsets[instanceIndex] = uvScaleOffset;
                    entityIndices[instanceIndex] = entityIndex;

                    instanceIndex++;
                }

                drawCallCounts[0] = instanceIndex;
            }
        }
    }
}

