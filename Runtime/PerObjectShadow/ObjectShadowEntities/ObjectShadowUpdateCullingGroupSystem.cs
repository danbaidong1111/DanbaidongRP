using System;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Contains culling results.
    /// </summary>
    internal class ObjectShadowCulledChunk : ObjectShadowChunk
    {
        public Vector3 camPosition;
        //public int cullingMask;

        public CullingGroup cullingGroups;
        public int visibleObjectShadowCount;
        public int[] visibleObjectShadowIndexArray;
        // Contains copy of IndexArray
        public NativeArray<int> visibleObjectShadowIndices;

        public override void RemoveAtSwapBack(int entityIndex)
        {
            RemoveAtSwapBack(ref visibleObjectShadowIndexArray, entityIndex, count);
            RemoveAtSwapBack(ref visibleObjectShadowIndices, entityIndex, count);
            count--;
        }

        public override void SetCapacity(int newCapacity)
        {
            ArrayExtensions.ResizeArray(ref visibleObjectShadowIndexArray, newCapacity);
            visibleObjectShadowIndices.ResizeArray(newCapacity);
            if (cullingGroups == null)
                cullingGroups = new CullingGroup();
            capacity = newCapacity;
        }

        public override void Dispose()
        {
            if (capacity == 0)
                return;

            visibleObjectShadowIndices.Dispose();
            visibleObjectShadowIndexArray = null;
            count = 0;
            capacity = 0;
            cullingGroups.Dispose();
            cullingGroups = null;
        }
    }

    /// <summary>
    /// Issues culling job with <see cref="CullingGroup"/>.
    /// </summary>
    internal class ObjectShadowUpdateCullingGroupSystem
    {
        private Camera m_Camera;
        private ObjectShadowEntityManager m_EntityManager;
        private ProfilingSampler m_Sampler;

        // Culling bound, is an array, treat it as an value.
        private float[] m_BoundingDistance = new float[1];
        public float boundingDistance
        {
            get { return m_BoundingDistance[0]; }
            set { m_BoundingDistance[0] = value; }
        }

        public ObjectShadowUpdateCullingGroupSystem(ObjectShadowEntityManager entityManager, float cullingDistance)
        {
            m_EntityManager = entityManager;
            m_BoundingDistance[0] = cullingDistance;
            m_Sampler = new ProfilingSampler("ObjectShadowUpdateCullingGroupSystem.Execute");
        }

        public void Execute(Camera camera)
        {
            using (new ProfilingScope(null, m_Sampler))
            {
                m_Camera = camera;
                for (int i = 0; i < m_EntityManager.chunkCount; i++)
                    Execute(m_EntityManager.cachedChunks[i], m_EntityManager.culledChunks[i], m_EntityManager.culledChunks[i].count);
            }
        }

        public void Execute(ObjectShadowCachedChunk cachedChunk, ObjectShadowCulledChunk culledChunk, int count)
        {
            cachedChunk.currentJobHandle.Complete();

            cachedChunk.boundingSpheres.CopyTo(cachedChunk.boundingSphereArray);

            CullingGroup cullingGroup = culledChunk.cullingGroups;
            cullingGroup.targetCamera = m_Camera;
            cullingGroup.SetDistanceReferencePoint(m_Camera.transform.position);
            cullingGroup.SetBoundingDistances(m_BoundingDistance);
            cullingGroup.SetBoundingSpheres(cachedChunk.boundingSphereArray);
            cullingGroup.SetBoundingSphereCount(count);

            culledChunk.camPosition = m_Camera.transform.position;


        }
    }

}
