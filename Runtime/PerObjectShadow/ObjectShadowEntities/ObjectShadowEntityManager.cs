using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace UnityEngine.Rendering.Universal
{
    internal class SharedObjectShadowEntityManager : System.IDisposable
    {
        private ObjectShadowEntityManager m_ObjectShadowEntityManager;
        private int m_ReferenceCounter;

        public ObjectShadowEntityManager Get()
        {
            if (m_ObjectShadowEntityManager == null)
            {
                Assert.AreEqual(m_ReferenceCounter, 0);

                m_ObjectShadowEntityManager = new ObjectShadowEntityManager();

                var shadowProjectors = GameObject.FindObjectsOfType<PerObjectShadowProjector>();
                foreach (var shadowProjector in shadowProjectors)
                {
                    if (!shadowProjector.isActiveAndEnabled || m_ObjectShadowEntityManager.IsValid(shadowProjector.objectShadowEntity))
                        continue;

                    shadowProjector.objectShadowEntity = m_ObjectShadowEntityManager.CreateEntity(shadowProjector);
                }

                PerObjectShadowProjector.onPerObjectShadowAdd += onPerObjectShadowAdd;
                PerObjectShadowProjector.onPerObjectShadowRemove += onPerObjectShadowRemove;
                PerObjectShadowProjector.onPerObjectShadowPropertyChange += onPerObjectShadowPropertyChange;
                PerObjectShadowProjector.onPerObjectShadowMaterialChange += onPerObjectShadowMaterialChange;
                PerObjectShadowProjector.onAllPerObjectShadowPropertyChange += onAllPerObjectShadowPropertyChange;
            }

            m_ReferenceCounter++;
            return m_ObjectShadowEntityManager;
        }

        public void Release(ObjectShadowEntityManager manager)
        {
            if (m_ReferenceCounter == 0)
                return;

            m_ReferenceCounter--;

            if (m_ReferenceCounter == 0)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            m_ObjectShadowEntityManager?.Dispose();
            m_ObjectShadowEntityManager = null;
            m_ReferenceCounter = 0;

            PerObjectShadowProjector.onPerObjectShadowAdd -= onPerObjectShadowAdd;
            PerObjectShadowProjector.onPerObjectShadowRemove -= onPerObjectShadowRemove;
            PerObjectShadowProjector.onPerObjectShadowPropertyChange -= onPerObjectShadowPropertyChange;
            PerObjectShadowProjector.onPerObjectShadowMaterialChange -= onPerObjectShadowMaterialChange;
            PerObjectShadowProjector.onAllPerObjectShadowPropertyChange -= onAllPerObjectShadowPropertyChange;
        }

        private void onPerObjectShadowAdd(PerObjectShadowProjector projector)
        {
            if (!m_ObjectShadowEntityManager.IsValid(projector.objectShadowEntity))
                projector.objectShadowEntity = m_ObjectShadowEntityManager.CreateEntity(projector);
        }
        private void onPerObjectShadowRemove(PerObjectShadowProjector projector)
        {
            m_ObjectShadowEntityManager.DestoryEntity(projector.objectShadowEntity);
        }
        private void onPerObjectShadowPropertyChange(PerObjectShadowProjector projector)
        {
            if (m_ObjectShadowEntityManager.IsValid(projector.objectShadowEntity))
                m_ObjectShadowEntityManager.UpdateEntityData(projector.objectShadowEntity, projector);
        }
        private void onAllPerObjectShadowPropertyChange()
        {
            m_ObjectShadowEntityManager.UpdateAllEntitiesData();
        }
        private void onPerObjectShadowMaterialChange(PerObjectShadowProjector projector)
        {
            // ObjectShadow will end up in new chunk after material change
            onPerObjectShadowRemove(projector);
            onPerObjectShadowAdd(projector);
        }
    }

    /// <summary>
    /// Manages lifetime between <see cref="PerObjectShadowProjector"></see> and <see cref="ObjectShadowEntity"/>.
    /// Contains all <see cref="ObjectShadowChunk"/>.
    /// </summary>
    internal class ObjectShadowEntityManager : IDisposable
    {
        // Chunks: entity, cached, culled, drawCall
        public List<ObjectShadowEntityChunk> entityChunks = new List<ObjectShadowEntityChunk>();
        public List<ObjectShadowCachedChunk> cachedChunks = new List<ObjectShadowCachedChunk>();
        public List<ObjectShadowCulledChunk> culledChunks = new List<ObjectShadowCulledChunk>();
        public List<ObjectShadowDrawCallChunk> drawCallChunks = new List<ObjectShadowDrawCallChunk>();

        public int chunkCount;

        // ProfilingSampler
        private ProfilingSampler m_AddObjectShadowSampler;
        private ProfilingSampler m_ResizeChunks;
        private ProfilingSampler m_SortChunks;

        // EntityIndexer
        private ObjectShadowEntityIndexer m_EntityIndexer = new ObjectShadowEntityIndexer();
        private Dictionary<Material, int> m_MaterialToChunkIndex = new Dictionary<Material, int>();

        // CombinedChunks
        private struct CombinedChunks
        {
            public ObjectShadowEntityChunk entityChunk;
            public ObjectShadowCachedChunk cachedChunk;
            public ObjectShadowCulledChunk culledChunk;
            public ObjectShadowDrawCallChunk drawCallChunk;
            public int prevChunkIndex;
            public bool valid;
        }
        private List<CombinedChunks> m_CombinedChunks = new List<CombinedChunks>();
        private List<int> m_CombinedChunkRemmap = new List<int>();

        private Material m_ErrorMaterial;
        public Material errorMaterial
        {
            get
            {
                if (m_ErrorMaterial == null)
                    m_ErrorMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/InternalErrorShader"));
                return m_ErrorMaterial;
            }
        }

        public ObjectShadowEntityManager()
        {
            // Init ProfilingSampler
            m_AddObjectShadowSampler = new ProfilingSampler("ObjectShadowEntityManager.CreateObjectShadowEntity");
            m_ResizeChunks = new ProfilingSampler("ObjectShadowEntityManager.ResizeChunks");
            m_SortChunks = new ProfilingSampler("ObjectShadowEntityManager.SortChunks");
        }

        /// <summary>
        /// Validate entity state
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public bool IsValid(ObjectShadowEntity entity)
        {
            return m_EntityIndexer.IsValid(entity);
        }


        /// <summary>
        /// Check projector's material, same material use one chunk.
        /// Create chunks if need and check their capacity.
        /// </summary>
        /// <param name="projector"></param>
        /// <returns></returns>
        public ObjectShadowEntity CreateEntity(PerObjectShadowProjector projector)
        {
            Material material = projector.material;
            if (material == null)
                material = errorMaterial;

            using (new ProfilingScope(null, m_AddObjectShadowSampler))
            {
                // Same materials have same chunk, current entityChunk size is new entity index.
                int chunkIndex = CreateChunkIndex(material);
                int entityIndex = entityChunks[chunkIndex].count;

                ObjectShadowEntity entity = m_EntityIndexer.CreateEntity(entityIndex, chunkIndex);

                // Comfirm chunk
                var entityChunk = entityChunks[chunkIndex];
                var cachedChunk = cachedChunks[chunkIndex];
                var culledChunk = culledChunks[chunkIndex];
                var drawCallChunk = drawCallChunks[chunkIndex];

                // Check if there is no space to add new entity
                if (entityChunk.capacity == entityChunk.count)
                {
                    using (new ProfilingScope(null, m_ResizeChunks))
                    {
                        // Double capacity and initial size is 8;
                        int newCapacity = entityChunk.capacity + entityChunk.capacity;
                        newCapacity = math.max(8, newCapacity);

                        entityChunk.SetCapacity(newCapacity);
                        cachedChunk.SetCapacity(newCapacity);
                        culledChunk.SetCapacity(newCapacity);
                        drawCallChunk.SetCapacity(newCapacity);
                    }
                }

                entityChunk.Push();
                cachedChunk.Push();
                culledChunk.Push();
                drawCallChunk.Push();

                entityChunk.objectShadowProjectors[entityIndex] = projector;
                entityChunk.objectShadowEntities[entityIndex] = entity;
                entityChunk.transformAccessArray.Add(projector.transform);

                UpdateEntityData(entity, projector);

                return entity;

            }
        }

        /// <summary>
        /// Find material index in MaterialToChunk Dictionary. Not exist then create chunks.
        /// </summary>
        /// <param name="material"></param>
        /// <returns></returns>
        private int CreateChunkIndex(Material material)
        {
            if (!m_MaterialToChunkIndex.TryGetValue(material, out int chunkIndex))
            {
                entityChunks.Add(new ObjectShadowEntityChunk() { material = material });
                cachedChunks.Add(new ObjectShadowCachedChunk() { propertyBlock = new MaterialPropertyBlock()});

                culledChunks.Add(new ObjectShadowCulledChunk());
                drawCallChunks.Add(new ObjectShadowDrawCallChunk() { drawCallCounts = new NativeArray<int>(1, Allocator.Persistent) });

                m_CombinedChunks.Add(new CombinedChunks());
                m_CombinedChunkRemmap.Add(0);

                m_MaterialToChunkIndex.Add(material, chunkCount);
                return chunkCount++;
            }

            return chunkIndex;
        }

        /// <summary>
        /// Every chunk, every entity update
        /// </summary>
        public void UpdateAllEntitiesData()
        {
            foreach (var entityChunk in entityChunks)
            {
                for (int i = 0; i < entityChunk.count; ++i)
                {
                    var projector = entityChunk.objectShadowProjectors[i];
                    if (projector == null)
                        continue;

                    var entity = entityChunk.objectShadowEntities[i];
                    if (!IsValid(entity))
                        continue;

                    UpdateEntityData(entity, projector);
                }

            }
        }

        /// <summary>
        /// PerObjectShadowProjector data to entity.
        /// Get entity chunk index and array(in the chunk)index. Modify the cachedChunk.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="projector"></param>
        public void UpdateEntityData(ObjectShadowEntity entity, PerObjectShadowProjector projector)
        {
            var entityItem = m_EntityIndexer.GetItem(entity);

            int chunkIndex = entityItem.chunkIndex;
            int arrayIndex = entityItem.arrayIndex;

            var cachedChunk = cachedChunks[chunkIndex];


            cachedChunk.positions[arrayIndex] = projector.transform.position;
            cachedChunk.rotation[arrayIndex] = projector.transform.rotation;
            cachedChunk.scales[arrayIndex] = projector.transform.lossyScale;
            cachedChunk.farPlaneScales[arrayIndex] = projector.farPlaneScale;
            cachedChunk.dirty[arrayIndex] = true;
        }

        /// <summary>
        /// Destory entity int the indexer. Find chunk with entity's chunkIndex, then destory entity
        /// </summary>
        /// <param name="entity"></param>
        public void DestoryEntity(ObjectShadowEntity entity)
        {
            if (!m_EntityIndexer.IsValid(entity))
                return;

            var entityItem = m_EntityIndexer.GetItem(entity);
            m_EntityIndexer.DestroyEntity(entity);

            int chunkIndex = entityItem.chunkIndex;
            int arrayIndex = entityItem.arrayIndex;

            var entityChunk = entityChunks[chunkIndex];
            var cachedChunk = cachedChunks[chunkIndex];
            var culledChunk = culledChunks[chunkIndex];
            var drawCallChunk = drawCallChunks[chunkIndex];

            // We use removeAtSwayBack, the lastEntity will swap to destoryedEntity index.
            // So, in the indexer entityItem is Destoryed, let lastItem's arrayIndex points to destoryedItem's arrayIndex.
            int lastArrayIndex = entityChunk.count - 1;
            if (arrayIndex != lastArrayIndex)
                m_EntityIndexer.UpdateIndex(entityChunk.objectShadowEntities[lastArrayIndex], arrayIndex);

            entityChunk.RemoveAtSwapBack(arrayIndex);
            cachedChunk.RemoveAtSwapBack(arrayIndex);
            culledChunk.RemoveAtSwapBack(arrayIndex);
            drawCallChunk.RemoveAtSwapBack(arrayIndex);
        }

        /// <summary>
        /// Combine chunks and sort by drawOrder.
        /// Update all chunks and destory invalid chunk.
        /// </summary>
        public void Update()
        {
            using (new ProfilingScope(null, m_SortChunks))
            {
                for (int i = 0; i < chunkCount; ++i)
                {
                    if (entityChunks[i].material == null)
                        entityChunks[i].material = errorMaterial;
                }

                // Combine chunks into single array
                for (int i = 0; i < chunkCount; ++i)
                {
                    m_CombinedChunks[i] = new CombinedChunks()
                    {
                        entityChunk = entityChunks[i],
                        cachedChunk = cachedChunks[i],
                        culledChunk = culledChunks[i],
                        drawCallChunk = drawCallChunks[i],
                        prevChunkIndex = i,
                        valid = entityChunks[i].count != 0,
                    };
                }

                // Sort
                m_CombinedChunks.Sort((a, b) =>
                {
                    if (a.valid && !b.valid)
                        return -1;
                    if (!a.valid && b.valid)
                        return 1;

                    return a.entityChunk.material.GetHashCode().CompareTo(b.entityChunk.material.GetHashCode());
                });

                // Early out if nothing changed
                bool dirty = false;
                for (int i = 0; i < chunkCount; ++i)
                {
                    if (m_CombinedChunks[i].prevChunkIndex != i || !m_CombinedChunks[i].valid)
                    {
                        dirty = true;
                        break;
                    }
                }
                if (!dirty)
                    return;

                // Update chunks
                int count = 0;
                m_MaterialToChunkIndex.Clear();
                for (int i = 0; i < chunkCount; i++)
                {
                    var combinedChunk = m_CombinedChunks[i];

                    // Destroy invalid chunk for cleanup
                    if (!m_CombinedChunks[i].valid)
                    {
                        combinedChunk.entityChunk.currentJobHandle.Complete();
                        combinedChunk.cachedChunk.currentJobHandle.Complete();
                        combinedChunk.culledChunk.currentJobHandle.Complete();
                        combinedChunk.drawCallChunk.currentJobHandle.Complete();

                        combinedChunk.entityChunk.Dispose();
                        combinedChunk.cachedChunk.Dispose();
                        combinedChunk.culledChunk.Dispose();
                        combinedChunk.drawCallChunk.Dispose();

                        continue;
                    }

                    entityChunks[i] = combinedChunk.entityChunk;
                    cachedChunks[i] = combinedChunk.cachedChunk;
                    culledChunks[i] = combinedChunk.culledChunk;
                    drawCallChunks[i] = combinedChunk.drawCallChunk;

                    // Refill materialToChunkIndex Dictionary
                    if (!m_MaterialToChunkIndex.ContainsKey(entityChunks[i].material))
                        m_MaterialToChunkIndex.Add(entityChunks[i].material, i);
                    m_CombinedChunkRemmap[combinedChunk.prevChunkIndex] = i;
                    count++;
                }

                // In case some chunks where destroyed resize the arrays
                if (chunkCount > count)
                {
                    entityChunks.RemoveRange(count, chunkCount - count);
                    cachedChunks.RemoveRange(count, chunkCount - count);
                    culledChunks.RemoveRange(count, chunkCount - count);
                    drawCallChunks.RemoveRange(count, chunkCount - count);

                    m_CombinedChunks.RemoveRange(count, chunkCount - count);
                    chunkCount = count;
                }

                // Remap entities chunk index with new sorted ones
                m_EntityIndexer.RemapChunkIndices(m_CombinedChunkRemmap);
            }
        }

        /// <summary>
        /// Clean
        /// </summary>
        public void Dispose()
        {
            CoreUtils.Destroy(m_ErrorMaterial);

            foreach (var entityChunk in entityChunks)
                entityChunk.currentJobHandle.Complete();
            foreach (var cachedChunk in cachedChunks)
                cachedChunk.currentJobHandle.Complete();
            foreach (var culledChunk in culledChunks)
                culledChunk.currentJobHandle.Complete();
            foreach (var drawCallChunk in drawCallChunks)
                drawCallChunk.currentJobHandle.Complete();

            foreach (var entityChunk in entityChunks)
                entityChunk.Dispose();
            foreach (var cachedChunk in cachedChunks)
                cachedChunk.Dispose();
            foreach (var culledChunk in culledChunks)
                culledChunk.Dispose();
            foreach (var drawCallChunk in drawCallChunks)
                drawCallChunk.Dispose();

            m_EntityIndexer.Clear();
            m_MaterialToChunkIndex.Clear();
            entityChunks.Clear();
            cachedChunks.Clear();
            culledChunks.Clear();
            drawCallChunks.Clear();
            m_CombinedChunks.Clear();
            chunkCount = 0;
        }
    }
}
