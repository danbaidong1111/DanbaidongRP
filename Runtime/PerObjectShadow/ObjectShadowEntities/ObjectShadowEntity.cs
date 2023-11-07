using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Assertions;
using UnityEngine.Jobs;

namespace UnityEngine.Rendering.Universal
{
    internal class ObjectShadowEntityIndexer
    {
        public struct ObjectShadowEntityItem
        {
            public int chunkIndex;
            public int arrayIndex;
            public int version;
        }

        private List<ObjectShadowEntityItem> m_Entities = new List<ObjectShadowEntityItem>();
        private Queue<int> m_FreeIndices = new Queue<int>();

        public bool IsValid(ObjectShadowEntity entity)
        {
            if (m_Entities.Count <= entity.index)
                return false;

            return m_Entities[entity.index].version == entity.version;
        }

        /// <summary>
        /// Create ObjectShadowEntity relative to current chunk
        /// </summary>
        /// <param name="arrayIndex"></param>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public ObjectShadowEntity CreateEntity(int arrayIndex, int chunkIndex)
        {
            // Reuse m_Entities
            if (m_FreeIndices.Count != 0)
            {
                int entityIndex = m_FreeIndices.Dequeue();
                int newVersion = m_Entities[entityIndex].version + 1;

                m_Entities[entityIndex] = new ObjectShadowEntityItem()
                {
                    arrayIndex = arrayIndex,
                    chunkIndex = chunkIndex,
                    version = newVersion,
                };

                return new ObjectShadowEntity()
                {
                    index = entityIndex,
                    version = newVersion,
                };
            }

            // Create new one add to m_Entities
            {
                int entityIndex = m_Entities.Count;
                int version = 1;

                m_Entities.Add(new ObjectShadowEntityItem()
                {
                    arrayIndex = arrayIndex,
                    chunkIndex = chunkIndex,
                    version = version,
                });


                return new ObjectShadowEntity()
                {
                    index = entityIndex,
                    version = version,
                };
            }
        }

        public void DestroyEntity(ObjectShadowEntity entity)
        {
            Assert.IsTrue(IsValid(entity));
            m_FreeIndices.Enqueue(entity.index);

            // Update version that everything that points to it will have outdated version
            var item = m_Entities[entity.index];
            item.version++;
            m_Entities[entity.index] = item;
        }

        public ObjectShadowEntityItem GetItem(ObjectShadowEntity entity)
        {
            Assert.IsTrue(IsValid(entity));
            return m_Entities[entity.index];
        }

        public void UpdateIndex(ObjectShadowEntity entity, int newArrayIndex)
        {
            Assert.IsTrue(IsValid(entity));
            var item = m_Entities[entity.index];
            item.arrayIndex = newArrayIndex;
            item.version = entity.version;
            m_Entities[entity.index] = item;
        }

        public void RemapChunkIndices(List<int> remaper)
        {
            for (int i = 0; i < m_Entities.Count; ++i)
            {
                int newChunkIndex = remaper[m_Entities[i].chunkIndex];
                var item = m_Entities[i];
                item.chunkIndex = newChunkIndex;
                m_Entities[i] = item;
            }
        }

        public void Clear()
        {
            m_Entities.Clear();
            m_FreeIndices.Clear();
        }
    }

    internal struct ObjectShadowEntity
    {
        public int index;
        public int version;
    }

    /// <summary>
    /// Contains <see cref="ObjectShadowEntity"/> and shared material.
    /// </summary>
    internal class ObjectShadowEntityChunk : ObjectShadowChunk
    {
        public Material material;
        public NativeArray<ObjectShadowEntity> objectShadowEntities;
        public PerObjectShadowProjector[] objectShadowProjectors;
        public TransformAccessArray transformAccessArray;

        public override void Push()
        {
            count++;
        }

        public override void RemoveAtSwapBack(int entityIndex)
        {
            RemoveAtSwapBack(ref objectShadowEntities, entityIndex, count);
            RemoveAtSwapBack(ref objectShadowProjectors, entityIndex, count);
            transformAccessArray.RemoveAtSwapBack(entityIndex);
            count--;
        }

        public override void SetCapacity(int capacityIn)
        {
            objectShadowEntities.ResizeArray(capacityIn);
            ResizeNativeArray(ref transformAccessArray, objectShadowProjectors, capacityIn);
            ArrayExtensions.ResizeArray(ref objectShadowProjectors, capacityIn);
            capacity = capacityIn;
        }

        public override void Dispose()
        {
            if (capacity == 0)
                return;

            objectShadowEntities.Dispose();
            transformAccessArray.Dispose();
            objectShadowProjectors = null;
            count = 0;
            capacity = 0;
        }
    }
}
