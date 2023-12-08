using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Contains <see cref="PerObjectShadowProjector"/> cached properties needed for rendering shadow.
    /// </summary>
    internal class ObjectShadowCachedChunk : ObjectShadowChunk
    {
        public MaterialPropertyBlock propertyBlock;
        public int shadowPassIndex;
        public int screenSpaceShadowPassIndex;
        public bool isCreated;

        // For rendering shadows
        public NativeArray<float4x4> viewMatrices;
        public NativeArray<float4x4> projMatrices;
        public NativeArray<Bounds> boundingBoxes;
        public NativeArray<BoundingSphere> boundingSpheres;
        // TRS information used for job check execute
        public NativeArray<float> farPlaneScales;
        public NativeArray<float3> positions;
        public NativeArray<quaternion> rotation;
        public NativeArray<float3> scales;
        public NativeArray<bool> dirty;

        public BoundingSphere[] boundingSphereArray;

        public override void RemoveAtSwapBack(int entityIndex)
        {
            RemoveAtSwapBack(ref viewMatrices, entityIndex, count);
            RemoveAtSwapBack(ref projMatrices, entityIndex, count);
            RemoveAtSwapBack(ref farPlaneScales, entityIndex, count);
            RemoveAtSwapBack(ref positions, entityIndex, count);
            RemoveAtSwapBack(ref rotation, entityIndex, count);
            RemoveAtSwapBack(ref scales, entityIndex, count);
            RemoveAtSwapBack(ref boundingBoxes, entityIndex, count);
            RemoveAtSwapBack(ref boundingSpheres, entityIndex, count);
            RemoveAtSwapBack(ref dirty, entityIndex, count);

            count--;
        }

        public override void SetCapacity(int capacityIn)
        {
            viewMatrices.ResizeArray(capacityIn);
            projMatrices.ResizeArray(capacityIn);
            farPlaneScales.ResizeArray(capacityIn);
            positions.ResizeArray(capacityIn);
            rotation.ResizeArray(capacityIn);
            scales.ResizeArray(capacityIn);
            boundingBoxes.ResizeArray(capacityIn);
            boundingSpheres.ResizeArray(capacityIn);
            dirty.ResizeArray(capacityIn);

            ArrayExtensions.ResizeArray(ref boundingSphereArray, capacityIn);
            
            capacity = capacityIn;
        }

        public override void Dispose()
        {
            if (capacity == 0)
                return;

            viewMatrices.Dispose();
            projMatrices.Dispose();
            farPlaneScales.Dispose();
            positions.Dispose();
            rotation.Dispose();
            scales.Dispose();
            boundingBoxes.Dispose();
            boundingSpheres.Dispose();
            dirty.Dispose();

            count = 0;
            capacity = 0;
        }
    }

    /// <summary>
    /// Caches <see cref="PerObjectShadowProjector"/> properties into <see cref="ObjectShadowCachedChunk"/>.
    /// Uses jobs with <see cref="IJobParallelForTransform"/>.
    /// </summary>
    internal class ObjectShadowUpdateCachedSystem
    {
        private struct LightTransformData
        {
            public float3 forward;
            public float3 right;
            public float3 up;
        }

        private ObjectShadowEntityManager m_EntityManager;
        private ProfilingSampler m_ProfilerSampler;
        private ProfilingSampler m_JobProfilerSampler;
        private ProfilingSampler m_EncapsulateProfilerSampler;

        private LightTransformData m_LightTransformData;
        public ObjectShadowUpdateCachedSystem(ObjectShadowEntityManager entityManager)
        {
            m_EntityManager = entityManager;
            m_LightTransformData = new LightTransformData();
            m_ProfilerSampler = new ProfilingSampler("ObjectShadowUpdateCachedSystem.Execute");
            m_JobProfilerSampler = new ProfilingSampler("ObjectShadowUpdateCachedSystem.ExecuteJob");
            m_EncapsulateProfilerSampler = new ProfilingSampler("ObjectShadowUpdateCachedSystem.EncapsulateBounds");
        }

        public void Execute(Light light)
        {
            m_LightTransformData.forward = light.transform.forward;
            m_LightTransformData.right = light.transform.right;
            m_LightTransformData.up = light.transform.up;
            using (new ProfilingScope(null, m_ProfilerSampler))
            {
                for (int i = 0; i < m_EntityManager.chunkCount; ++i)
                    Execute(m_EntityManager.entityChunks[i], m_EntityManager.cachedChunks[i], m_EntityManager.entityChunks[i].count);
            }
        }

        private void Execute(ObjectShadowEntityChunk entityChunk, ObjectShadowCachedChunk cachedChunk, int count)
        {
            if (count == 0)
                return;

            cachedChunk.currentJobHandle.Complete();

            var material = entityChunk.material;

            // Shader can change any time in editor, so we have to update passes each time
#if !UNITY_EDITOR
            if (!cachedChunk.isCreated)
#endif
            {
                int passIndex = material.FindPass(PerObjectShadowShaderPassNames.PerObjectShadowProjector);
                cachedChunk.shadowPassIndex = passIndex;

                passIndex = material.FindPass(PerObjectShadowShaderPassNames.PerObjectScreenSpaceShadow);
                cachedChunk.screenSpaceShadowPassIndex = passIndex;

                cachedChunk.isCreated = true;
            }

            using (new ProfilingScope(null, m_EncapsulateProfilerSampler))
            {
                // Update bounds any time, entity's boundingBox should Encapsulate children boundingBoxes.
                // The entity's children num is not fixed, could not add it to job.
                for (int arrayIndex = 0; arrayIndex < entityChunk.objectShadowProjectors.Length; arrayIndex++)
                {
                    var projector = entityChunk.objectShadowProjectors[arrayIndex];
                    if (projector == null)
                        continue;
                    var childrenderers = projector.childRenderers;
                    var bounds = childrenderers[0].bounds;
                    for (int j = 1; j < childrenderers.Length; j++)
                    {
                        bounds.Encapsulate(childrenderers[j].bounds);
                    }

                    cachedChunk.boundingBoxes[arrayIndex] = bounds;
                }
            }
            
            using (new ProfilingScope(null, m_JobProfilerSampler))
            {
                UpdateObjectShadowMatricesAndBoundingSphereJob updateJob = new UpdateObjectShadowMatricesAndBoundingSphereJob()
                {
                    positions = cachedChunk.positions,
                    rotations = cachedChunk.rotation,
                    scales = cachedChunk.scales,
                    farPlaneScales = cachedChunk.farPlaneScales,
                    dirty = cachedChunk.dirty,

                    minDistance = System.Single.Epsilon,

                    lightTransformData = m_LightTransformData,
                    boundingBoxes = cachedChunk.boundingBoxes,

                    viewMatrices = cachedChunk.viewMatrices,
                    projMatrices = cachedChunk.projMatrices,
                    boundingSpheres = cachedChunk.boundingSpheres,
                };

                var handle = updateJob.Schedule(entityChunk.transformAccessArray);
                cachedChunk.currentJobHandle = handle;
            }
        }



#if ENABLE_BURST_1_0_0_OR_NEWER
        [Unity.Burst.BurstCompile]
#endif
        private unsafe struct UpdateObjectShadowMatricesAndBoundingSphereJob : IJobParallelForTransform
        {
            public NativeArray<float3> positions;
            public NativeArray<quaternion> rotations;
            public NativeArray<float3> scales;
            public NativeArray<float> farPlaneScales;
            public NativeArray<bool> dirty;

            // Transform changed greater than this, then execute.
            public float minDistance;

            [ReadOnly] public LightTransformData lightTransformData;
            [ReadOnly] public NativeArray<Bounds> boundingBoxes;

            [WriteOnly] public NativeArray<float4x4> viewMatrices;
            [WriteOnly] public NativeArray<float4x4> projMatrices;
            [WriteOnly] public NativeArray<BoundingSphere> boundingSpheres;

            private float DistanceBetweenQuaternions(quaternion a, quaternion b)
            {
                return math.distancesq(a.value, b.value);
            }
            public void Execute(int index, TransformAccess transform)
            {
                // Check if transform changed
                bool positionChanged = math.distancesq(transform.position, positions[index]) > minDistance;
                if (positionChanged)
                    positions[index] = transform.position;
                bool rotationChanged = DistanceBetweenQuaternions(transform.rotation, rotations[index]) > minDistance;
                if (rotationChanged)
                    rotations[index] = transform.rotation;
                bool scaleChanged = math.distancesq(transform.localScale, scales[index]) > minDistance;
                if (scaleChanged)
                    scales[index] = transform.localScale;

                // Early out if transform did not changed
                if (!positionChanged && !rotationChanged && !scaleChanged && !dirty[index])
                    return;


                // Set ShadowData
                Matrix4x4 viewMatrix = new Matrix4x4();
                Matrix4x4 projMatrix = new Matrix4x4();
                Vector3 sphereCenter = Vector3.zero;
                float sphereRadius = 0;
                
                PerObjectShadowUtils.ComputePerObjectShadowMatricesAndCullingSphere(lightTransformData.forward, lightTransformData.up, lightTransformData.right
                                                            , boundingBoxes[index], farPlaneScales[index], out viewMatrix, out projMatrix, out sphereCenter, out sphereRadius);
                BoundingSphere boundSphere = new BoundingSphere(sphereCenter, sphereRadius);
                boundingSpheres[index] = boundSphere;

                viewMatrices[index] = viewMatrix;
                projMatrices[index] = projMatrix;

            }

        }
    }
}