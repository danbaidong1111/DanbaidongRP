using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal.Internal
{
    internal class GPULightsDataBuildSystem
    {
        public const int ArrayCapacity = 100;

        JobHandle m_CreateGpuLightDataJobHandle;

        // For reflection probes / decals.
        //private int m_probeBoundsOffset;
        private int m_boundsCount;

        private NativeArray<SFiniteLightBound> m_LightBounds;
        private NativeArray<LightVolumeData> m_LightVolumes;
        private int m_LightBoundsCapacity = 0;
        private int m_LightBoundsCount = 0;

        private NativeArray<GPULightData> m_GPULightsData;
        private int m_LightCapacity = 0;
        private int m_LightCount = 0;

        private NativeArray<DirectionalLightData> m_DirectionalLightsData;
        private int m_DirectionalLightCapacity = 0;
        private int m_DirectionalLightCount = 0;

        private NativeArray<EnvLightData> m_EnvLightsData;
        private int m_EnvLightCapacity = 0;
        private int m_EnvLightsCount = 0;

        private AdditionalLightsShadowCasterPass m_AdditionalLightsShadowCasterPass;
        private LightCookieManager m_LightCookieManager;

        //Auxiliary GPU arrays for coarse culling
        public NativeArray<SFiniteLightBound> lightBounds => m_LightBounds;
        public NativeArray<LightVolumeData> lightVolumes => m_LightVolumes;
        public NativeArray<GPULightData> gpuLightsData => m_GPULightsData;
        public NativeArray<DirectionalLightData> directionalLightsData => m_DirectionalLightsData;
        public NativeArray<EnvLightData> envLightsData => m_EnvLightsData;

        public int lightsCount => m_LightCount;
        public int directionalLightCount => m_DirectionalLightCount;
        public int boundsCount => m_boundsCount;
        public int envLightsCount => m_EnvLightsCount;

        // Constants
        internal static readonly Vector3 k_BoxCullingExtentThreshold = Vector3.one * 0.01f;

        //Preallocates number of lights for bounds arrays and resets all internal counters. Must be called once per frame per view always.
        public void NewFrame(int maxBoundsCount, AdditionalLightsShadowCasterPass addShadowCaster, LightCookieManager cookieManager)
        {
            int requestedBoundsCount = Math.Max(maxBoundsCount, 1);
            if (requestedBoundsCount > m_LightBoundsCapacity)
            {
                m_LightBoundsCapacity = Math.Max(Math.Max(m_LightBoundsCapacity * 2, requestedBoundsCount), ArrayCapacity);
                m_LightBounds.ResizeArray(m_LightBoundsCapacity);
                m_LightVolumes.ResizeArray(m_LightBoundsCapacity);
            }
            m_LightBoundsCount = maxBoundsCount;

            //m_probeBoundsOffset = maxBoundsCount;
            m_boundsCount = 0;

            m_AdditionalLightsShadowCasterPass = addShadowCaster;
            m_LightCookieManager = cookieManager;
        }


        private void AllocateGPULightsData(int lightCount, int directionalLightCount)
        {
            int requestedLightCount = Math.Max(1, lightCount);
            if (requestedLightCount > m_LightCapacity)
            {
                m_LightCapacity = Math.Max(Math.Max(m_LightCapacity * 2, requestedLightCount), ArrayCapacity);
                m_GPULightsData.ResizeArray(m_LightCapacity);
            }
            m_LightCount = lightCount;

            int requestedDurectinalCount = Math.Max(1, directionalLightCount);
            if (requestedDurectinalCount > m_DirectionalLightCapacity)
            {
                m_DirectionalLightCapacity = Math.Max(Math.Max(m_DirectionalLightCapacity * 2, requestedDurectinalCount), ArrayCapacity);
                m_DirectionalLightsData.ResizeArray(m_DirectionalLightCapacity);
            }
            m_DirectionalLightCount = directionalLightCount;
        }

        //Adds bounds for a new light type. Reflection probes / decals add their bounds here.
        public void AddLightBounds(in SFiniteLightBound lightBound, in LightVolumeData volumeData)
        {
            m_LightBounds[m_boundsCount] = lightBound;
            m_LightVolumes[m_boundsCount] = volumeData;
            m_boundsCount++;
        }

        /// <summary>
        /// We should wait for lightcookie pass nad additionalLightsShadowCaster pass result.
        /// </summary>
        /// <param name="renderingData"></param>
        public void ReBuildGPULightsDataBuffer(UniversalLightData lightData)
        {
            var visibleLights = lightData.visibleLights;
            var dirlightCount = lightData.directionalLightsCount;
            AllocateGPULightsData(visibleLights.Length - dirlightCount, dirlightCount);

            for (int visLightIndex = 0; visLightIndex < visibleLights.Length; visLightIndex++)
            {
                var light = visibleLights[visLightIndex].light;
                if (visibleLights[visLightIndex].lightType == LightType.Directional)
                {
                    var additionalLightData = light.GetUniversalAdditionalLightData();

                    var directionalLightData = new DirectionalLightData();

                    Vector4 lightPos, lightColor, lightAttenuation, lightSpotDir, lightOcclusionChannel;
                    // Directional lightPos is direction
                    UniversalRenderPipeline.InitializeLightConstants_Common(visibleLights, visLightIndex, out lightPos, out lightColor, out lightAttenuation, out lightSpotDir, out lightOcclusionChannel);
                    uint lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalLightData.renderingLayers);

                    int lightFlags = 0;
                    if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
                        lightFlags |= (int)LightFlag.SubtractiveMixedLighting;

                    // As we said before.
                    directionalLightData.lightPosWS = visibleLights[visLightIndex].GetPosition();
                    directionalLightData.lightDirection = lightPos;
                    directionalLightData.lightColor = lightColor;
                    directionalLightData.lightAttenuation = lightAttenuation;
                    //directionalLightData.lightOcclusionProbInfo = lightOcclusionChannel;
                    directionalLightData.lightFlags = lightFlags;
                    //directionalLightData.shadowlightIndex = shadowLightIndex;
                    directionalLightData.lightLayerMask = lightLayerMask;

                    //Value of max smoothness is derived from AngularDiameter. Formula results from eyeballing. Angular diameter of 0 results in 1 and angular diameter of 80 results in 0.
                    float maxSmoothness = Mathf.Clamp01(1.35f / (1.0f + Mathf.Pow(1.15f * (0.0315f * additionalLightData.angularDiameter + 0.4f), 2f)) - 0.11f);
                    // Value of max smoothness is from artists point of view, need to convert from perceptual smoothness to roughness
                    directionalLightData.minRoughness = (1.0f - maxSmoothness) * (1.0f - maxSmoothness);
                    directionalLightData.lightDimmer = 1;
                    directionalLightData.diffuseDimmer = 1;
                    directionalLightData.specularDimmer = 1;
                    directionalLightData.volumetricDimmer = Mathf.Max(0f, additionalLightData.volumetricDimmer);
                    directionalLightData.volumetricShadowDimmer = Mathf.Clamp01(additionalLightData.volumetricShadowDimmer);


                    m_DirectionalLightsData[visLightIndex] = directionalLightData;
                }
                else
                {
                    var additionalLightData = light.GetUniversalAdditionalLightData();

                    var gpuLightsData = new GPULightData();

                    Vector4 lightPos, lightColor, lightAttenuation, lightSpotDir, lightOcclusionChannel;
                    UniversalRenderPipeline.InitializeLightConstants_Common(visibleLights, visLightIndex, out lightPos, out lightColor, out lightAttenuation, out lightSpotDir, out lightOcclusionChannel);
                    uint lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalLightData.renderingLayers);

                    int lightFlags = 0;
                    if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
                        lightFlags |= (int)LightFlag.SubtractiveMixedLighting;
                    int shadowLightIndex = m_AdditionalLightsShadowCasterPass != null ? m_AdditionalLightsShadowCasterPass.GetShadowLightIndexFromLightIndex(visLightIndex) : -1;

                    // The current camera's atlas mapping becomes available during graph execution.
                    gpuLightsData.cookieLightIndex = -1;

                    gpuLightsData.lightPosWS = lightPos;
                    gpuLightsData.lightColor = lightColor;
                    gpuLightsData.lightAttenuation = lightAttenuation;
                    gpuLightsData.lightDirection = lightSpotDir;
                    gpuLightsData.lightOcclusionProbInfo = lightOcclusionChannel;
                    gpuLightsData.lightFlags = lightFlags;
                    gpuLightsData.shadowLightIndex = shadowLightIndex;
                    gpuLightsData.shadowType = (int)light.shadows;
                    gpuLightsData.lightLayerMask = lightLayerMask;
                    //Value of max smoothness is derived from Radius. Formula results from eyeballing. Radius of 0 results in 1 and radius of 2.5 results in 0.
                    float maxSmoothness = Mathf.Clamp01(1.1725f / (1.01f + Mathf.Pow(1.0f * (additionalLightData.shapeRadius + 0.1f), 2f)) - 0.15f);
                    // Value of max smoothness is from artists point of view, need to convert from perceptual smoothness to roughness
                    gpuLightsData.minRoughness = (1.0f - maxSmoothness) * (1.0f - maxSmoothness);
                    gpuLightsData.baseContribution = additionalLightData.baseContribution;
                    gpuLightsData.rimContribution = additionalLightData.rimContribution;
                    gpuLightsData.outlineContribution = additionalLightData.outlineContribution;
                    gpuLightsData.volumetricDimmer = Mathf.Max(0f, additionalLightData.volumetricDimmer);
                    gpuLightsData.volumetricShadowDimmer = Mathf.Clamp01(additionalLightData.volumetricShadowDimmer);
                    gpuLightsData.volumetricLightRadius = Mathf.Max(0f, additionalLightData.volumetricLightRadius);

                    m_GPULightsData[visLightIndex - dirlightCount] = gpuLightsData;
                }
            }
        }

        internal void RefreshCookieIndices(UniversalLightData lightData)
        {
            var visibleLights = lightData.visibleLights;
            for (int visibleIndex = 0; visibleIndex < visibleLights.Length; ++visibleIndex)
            {
                if (visibleLights[visibleIndex].lightType == LightType.Directional) continue;
                int index = visibleIndex - lightData.directionalLightsCount;
                var light = m_GPULightsData[index];
                light.cookieLightIndex = m_LightCookieManager != null
                    ? m_LightCookieManager.GetLightCookieShaderDataIndex(visibleIndex) : -1;
                m_GPULightsData[index] = light;
            }
        }

        // TODO: visible lights should handles envLights(probe) and decals as HDRP.
        /// <summary>
        /// Builds the GPU light list.
        /// </summary>
        public void BuildGPULightList(UniversalLightData lightData, UniversalCameraData cameraData)
        {
            int totalLightCount = lightData.additionalLightsCount;
            if (totalLightCount == 0)
            {
                return;
            }

            var lightCount = lightData.visibleLights.Length;
            var lightOffset = 0;
            while (lightOffset < lightCount && lightData.visibleLights[lightOffset].lightType == LightType.Directional)
            {
                lightOffset++;
            }
            lightCount -= lightOffset;
            var visibleAdditionalLights = lightData.visibleLights.GetSubArray(lightOffset, lightCount);

            //AllocateGPULightsData(lightCount, 0);

            m_boundsCount += lightCount;

            // StartCreateGpuLightDataJob

            var worldToCamMatrix = cameraData.GetViewMatrix(); // Right-handed coordinate system
            // camera.worldToCameraMatrix is RHS and Unity's transforms are LHS, we need to flip it to work with transforms.
            // Note that this is equivalent to s_FlipMatrixLHSRHS * viewMatrix, but faster given that it doesn't need full matrix multiply
            // However if for some reason s_FlipMatrixLHSRHS changes from Matrix4x4.Scale(new Vector3(1, 1, -1)), this need to change as well.
            worldToCamMatrix.m20 *= -1;
            worldToCamMatrix.m21 *= -1;
            worldToCamMatrix.m22 *= -1;
            worldToCamMatrix.m23 *= -1;

            var createJob = new CreateGPULightDataJob()
            {
                lightLayersEnabled = lightData.supportsLightLayers,
                worldToViewMatrix = worldToCamMatrix,

                visibleLights = visibleAdditionalLights,

                lightBounds = m_LightBounds,
                lightVolumes = m_LightVolumes,
                //gpuLightsData = m_LightsData
            };
            m_CreateGpuLightDataJobHandle = createJob.Schedule(lightCount, 32);
            // CompeleteJob
            m_CreateGpuLightDataJobHandle.Complete();


            //string debugstr = "";
            //for (int i = 0; i < m_LightBounds.Length; i++)
            //{
            //    var light = m_LightBounds[i];
            //    debugstr += "center: " + light.center + ", radius: " + light.radius + "\n";
            //}
            //Debug.Log(debugstr);
        }

        public void BuildEnvLightList(ref NativeArray<VisibleReflectionProbe> reflectionProbes, int reflectionProbeCount, UniversalCameraData cameraData)
        {
            // Same as ForwardLights, sort it.
            // Should probe come after otherProbe?
            static bool IsProbeGreater(VisibleReflectionProbe probe, VisibleReflectionProbe otherProbe)
            {
                return probe.importance < otherProbe.importance ||
                    (probe.importance == otherProbe.importance && probe.bounds.extents.sqrMagnitude > otherProbe.bounds.extents.sqrMagnitude);
            }

            for (var i = 1; i < reflectionProbeCount; i++)
            {
                var probe = reflectionProbes[i];
                var j = i - 1;
                while (j >= 0 && IsProbeGreater(reflectionProbes[j], probe))
                {
                    reflectionProbes[j + 1] = reflectionProbes[j];
                    j--;
                }

                reflectionProbes[j + 1] = probe;
            }

            // Camera Matrix
            var worldToCamMatrix = cameraData.GetViewMatrix(); // Right-handed coordinate system
            // camera.worldToCameraMatrix is RHS and Unity's transforms are LHS, we need to flip it to work with transforms.
            // Note that this is equivalent to s_FlipMatrixLHSRHS * viewMatrix, but faster given that it doesn't need full matrix multiply
            // However if for some reason s_FlipMatrixLHSRHS changes from Matrix4x4.Scale(new Vector3(1, 1, -1)), this need to change as well.
            worldToCamMatrix.m20 *= -1;
            worldToCamMatrix.m21 *= -1;
            worldToCamMatrix.m22 *= -1;
            worldToCamMatrix.m23 *= -1;

            BuildEnvLightsDataBuffer(ref reflectionProbes, reflectionProbeCount);

            for (int i = 0; i < reflectionProbeCount; i++)
            {
                var probe = reflectionProbes[i];
                GetEnvLightVolumeDataAndBound(ref probe, ref worldToCamMatrix);
            }
        }

        public void BuildEnvLightsDataBuffer(ref NativeArray<VisibleReflectionProbe> reflectionProbes, int reflectionProbeCount)
        {
            // Allocate envLightData
            int requestedDurectinalCount = Math.Max(1, reflectionProbeCount);
            if (requestedDurectinalCount > m_EnvLightCapacity)
            {
                m_EnvLightCapacity = Math.Max(Math.Max(m_EnvLightCapacity * 2, requestedDurectinalCount), ArrayCapacity);
                m_EnvLightsData.ResizeArray(m_EnvLightCapacity);
            }
            m_EnvLightsCount = reflectionProbeCount;

            for (int envLightIndex = 0; envLightIndex < reflectionProbeCount; envLightIndex++)
            {
                var probe = reflectionProbes[envLightIndex];
                var envLightData = new EnvLightData();
                // EnvLightData is handled by ReflectionProbeManager, we just add it here for indexing.


                m_EnvLightsData[envLightIndex] = envLightData;
            }
        }

        private void GetEnvLightVolumeDataAndBound(ref VisibleReflectionProbe probe, ref Matrix4x4 worldToCamMatrix)
        {
            var bound = new SFiniteLightBound();
            var lightVolumeData = new LightVolumeData();

            var centerWS = (float3)probe.bounds.center;
            var extentsWS = (float3)probe.bounds.extents;

            Vector3 influenceExtents = extentsWS;
            Matrix4x4 influenceToWorld = probe.localToWorldMatrix;

            var influenceRightVS = worldToCamMatrix.MultiplyVector(influenceToWorld.GetColumn(0).normalized);
            var influenceUpVS = worldToCamMatrix.MultiplyVector(influenceToWorld.GetColumn(1).normalized);
            var influenceForwardVS = worldToCamMatrix.MultiplyVector(influenceToWorld.GetColumn(2).normalized);
            var influencePositionVS = worldToCamMatrix.MultiplyPoint(centerWS); // We need use bounds cneter.

            var lightVolumeType = LightVolumeType.Box; // TODO: Sphere?
            lightVolumeData.lightCategory = (uint)LightCategory.Env;
            lightVolumeData.lightVolume = (uint)lightVolumeType;
            lightVolumeData.featureFlags = (uint)LightFeatureFlags.Env;

            switch (lightVolumeType)
            {
                case LightVolumeType.Sphere:
                    {
                        lightVolumeData.lightPos = influencePositionVS;
                        lightVolumeData.radiusSq = influenceExtents.x * influenceExtents.x;
                        lightVolumeData.lightAxisX = influenceRightVS;
                        lightVolumeData.lightAxisY = influenceUpVS;
                        lightVolumeData.lightAxisZ = influenceForwardVS;

                        bound.center = influencePositionVS;
                        bound.boxAxisX = influenceRightVS * influenceExtents.x;
                        bound.boxAxisY = influenceUpVS * influenceExtents.x;
                        bound.boxAxisZ = influenceForwardVS * influenceExtents.x;
                        bound.scaleXY = 1.0f;
                        bound.radius = influenceExtents.x;
                        break;
                    }
                case LightVolumeType.Box:
                    {
                        bound.center = influencePositionVS;
                        bound.boxAxisX = influenceExtents.x * influenceRightVS;
                        bound.boxAxisY = influenceExtents.y * influenceUpVS;
                        bound.boxAxisZ = influenceExtents.z * influenceForwardVS;
                        bound.scaleXY = 1.0f;
                        bound.radius = influenceExtents.magnitude;

                        // The culling system culls pixels that are further
                        //   than a threshold to the box influence extents.
                        // So we use an arbitrary threshold here (k_BoxCullingExtentOffset)
                        lightVolumeData.lightPos = influencePositionVS;
                        lightVolumeData.lightAxisX = influenceRightVS;
                        lightVolumeData.lightAxisY = influenceUpVS;
                        lightVolumeData.lightAxisZ = influenceForwardVS;
                        lightVolumeData.boxInnerDist = influenceExtents - k_BoxCullingExtentThreshold;
                        lightVolumeData.boxInvRange.Set(1.0f / k_BoxCullingExtentThreshold.x, 1.0f / k_BoxCullingExtentThreshold.y, 1.0f / k_BoxCullingExtentThreshold.z);
                        break;
                    }
            }

            AddLightBounds(bound, lightVolumeData);
        }

        public void Cleanup()
        {
            if (m_GPULightsData.IsCreated)
                m_GPULightsData.Dispose();

            if (m_DirectionalLightsData.IsCreated)
                m_DirectionalLightsData.Dispose();

            if (m_EnvLightsData.IsCreated)
                m_EnvLightsData.Dispose();

            if (m_LightBounds.IsCreated)
                m_LightBounds.Dispose();

            if (m_LightVolumes.IsCreated)
                m_LightVolumes.Dispose();
        }




        #region JobSystem


#if ENABLE_BURST_1_5_0_OR_NEWER
        [Unity.Burst.BurstCompile]
#endif
        internal struct CreateGPULightDataJob : IJobParallelFor
        {
            #region Parameters
            public bool lightLayersEnabled;
            [ReadOnly]
            public Matrix4x4 worldToViewMatrix;
            #endregion

            #region input visible lights processed
            [ReadOnly]
            public NativeArray<VisibleLight> visibleLights;
            #endregion

            #region output processed lights
            [WriteOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<SFiniteLightBound> lightBounds;
            [WriteOnly]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<LightVolumeData> lightVolumes;
            //[WriteOnly]
            //[NativeDisableContainerSafetyRestriction]
            //public NativeArray<GPULightData> gpuLightsData; // For shader use
            #endregion

            public void Execute(int index)
            {
                var lightIndex = index;
                if (lightIndex >= visibleLights.Length)
                {
                    return;
                }

                var light = visibleLights[lightIndex];

                var lightCategory = LightCategory.Punctual;
                var gpuLightType = GPULightType.Point;
                var lightVolumeType = LightVolumeType.Sphere;
                switch (light.lightType)
                {
                    case LightType.Point:

                        break;

                    case LightType.Spot:
                        gpuLightType = GPULightType.Spot;
                        lightVolumeType = LightVolumeType.Cone;
                        break;

                    default:
                        Debug.Assert(false, "Encountered an unknown LightType.");
                        break;
                }

                ComputeLightVolumeDataAndBound(lightIndex, lightCategory, gpuLightType, lightVolumeType,
                                               light, new Vector3(0.5f, 0.5f, light.range), worldToViewMatrix);

            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="lightCategory"></param>
            /// <param name="gpuLightType"></param>
            /// <param name="lightVolumeType"></param>
            /// <param name="light"></param>
            /// <param name="lightData"></param>
            /// <param name="lightDimensions"></param> Only for Rectangle/Line/box projector lights.(0.5f, 0.5f, light.range)
            /// <param name="worldToView"></param>
            private void ComputeLightVolumeDataAndBound(int lightIndex, LightCategory lightCategory, GPULightType gpuLightType, LightVolumeType lightVolumeType,
                                                        in VisibleLight light, in Vector3 lightDimensions, in Matrix4x4 worldToView)
            {
                var range = light.range;
                var lightToWorld = light.localToWorldMatrix;

                Vector3 positionWS = light.GetPosition(); // Currently not including camera relative transform
                Vector3 positionVS = worldToView.MultiplyPoint(positionWS);

                Vector3 xAxisVS = worldToView.MultiplyVector(lightToWorld.GetColumn(0));
                Vector3 yAxisVS = worldToView.MultiplyVector(lightToWorld.GetColumn(1));
                Vector3 zAxisVS = worldToView.MultiplyVector(lightToWorld.GetColumn(2));

                // Fill bounds
                var bound = new SFiniteLightBound();
                var lightVolumeData = new LightVolumeData();

                lightVolumeData.lightCategory = (uint)lightCategory;
                lightVolumeData.lightVolume = (uint)lightVolumeType;

                
                if (gpuLightType == GPULightType.Point)
                {
                    // Construct a view-space axis-aligned bounding cube around the bounding sphere.
                    // This allows us to utilize the same polygon clipping technique for all lights.
                    // Non-axis-aligned vectors may result in a larger screen-space AABB.
                    Vector3 vx = new Vector3(1, 0, 0);
                    Vector3 vy = new Vector3(0, 1, 0);
                    Vector3 vz = new Vector3(0, 0, 1);

                    bound.center = positionVS;
                    bound.boxAxisX = vx * range;
                    bound.boxAxisY = vy * range;
                    bound.boxAxisZ = vz * range;
                    bound.scaleXY = 1.0f;
                    bound.radius = range;

                    // fill up ldata
                    lightVolumeData.lightAxisX = vx;
                    lightVolumeData.lightAxisY = vy;
                    lightVolumeData.lightAxisZ = vz;
                    lightVolumeData.lightPos = bound.center;
                    lightVolumeData.radiusSq = range * range;
                    lightVolumeData.featureFlags = (uint)LightFeatureFlags.Punctual;
                }
                else if (gpuLightType == GPULightType.Spot || gpuLightType == GPULightType.ProjectorPyramid)
                {
                    Vector3 lightDir = lightToWorld.GetColumn(2);

                    // represents a left hand coordinate system in world space since det(worldToView)<0
                    Vector3 vx = xAxisVS;
                    Vector3 vy = yAxisVS;
                    Vector3 vz = zAxisVS;
                    
                    var sa = light.spotAngle;
                    var cs = Mathf.Cos(0.5f * sa * Mathf.Deg2Rad);
                    var si = Mathf.Sin(0.5f * sa * Mathf.Deg2Rad);

                    if (gpuLightType == GPULightType.ProjectorPyramid)
                    {
                        Vector3 lightPosToProjWindowCorner = (0.5f * lightDimensions.x) * vx + (0.5f * lightDimensions.y) * vy + 1.0f * vz;
                        cs = Vector3.Dot(vz, Vector3.Normalize(lightPosToProjWindowCorner));
                        si = Mathf.Sqrt(1.0f - cs * cs);
                    }

                    const float FltMax = 3.402823466e+38F;
                    var ta = cs > 0.0f ? (si / cs) : FltMax;
                    var cota = si > 0.0f ? (cs / si) : FltMax;

                    //const float cotasa = l.GetCotanHalfSpotAngle();

                    // apply nonuniform scale to OBB of spot light
                    var squeeze = true;//sa < 0.7f * 90.0f;      // arb heuristic
                    var fS = squeeze ? ta : si;
                    bound.center = worldToView.MultiplyPoint(positionWS + ((0.5f * range) * lightDir));    // use mid point of the spot as the center of the bounding volume for building screen-space AABB for tiled lighting.

                    // scale axis to match box or base of pyramid
                    bound.boxAxisX = (fS * range) * vx;
                    bound.boxAxisY = (fS * range) * vy;
                    bound.boxAxisZ = (0.5f * range) * vz;

                    // generate bounding sphere radius
                    var fAltDx = si;
                    var fAltDy = cs;
                    fAltDy = fAltDy - 0.5f;
                    //if(fAltDy<0) fAltDy=-fAltDy;

                    fAltDx *= range; fAltDy *= range;

                    // Handle case of pyramid with this select (currently unused)
                    var altDist = Mathf.Sqrt(fAltDy * fAltDy + (true ? 1.0f : 2.0f) * fAltDx * fAltDx);
                    bound.radius = altDist > (0.5f * range) ? altDist : (0.5f * range);       // will always pick fAltDist
                    bound.scaleXY = squeeze ? 0.01f : 1.0f;

                    lightVolumeData.lightAxisX = vx;
                    lightVolumeData.lightAxisY = vy;
                    lightVolumeData.lightAxisZ = vz;
                    lightVolumeData.lightPos = positionVS;
                    lightVolumeData.radiusSq = range * range;
                    lightVolumeData.cotan = cota;
                    lightVolumeData.featureFlags = (uint)LightFeatureFlags.Punctual;
                }
                //else if (gpuLightType == GPULightType.Tube)
                //{
                //    Vector3 dimensions = new Vector3(lightDimensions.x + 2 * range, 2 * range, 2 * range); // Omni-directional
                //    Vector3 extents = 0.5f * dimensions;
                //    Vector3 centerVS = positionVS;

                //    bound.center = centerVS;
                //    bound.boxAxisX = extents.x * xAxisVS;
                //    bound.boxAxisY = extents.y * yAxisVS;
                //    bound.boxAxisZ = extents.z * zAxisVS;
                //    bound.radius = extents.x;
                //    bound.scaleXY = 1.0f;

                //    lightVolumeData.lightPos = centerVS;
                //    lightVolumeData.lightAxisX = xAxisVS;
                //    lightVolumeData.lightAxisY = yAxisVS;
                //    lightVolumeData.lightAxisZ = zAxisVS;
                //    lightVolumeData.boxInvRange.Set(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z);
                //    lightVolumeData.featureFlags = (uint)LightFeatureFlags.Area;
                //}
                //else if (gpuLightType == GPULightType.Rectangle)
                //{
                //    Vector3 dimensions = new Vector3(lightDimensions.x + 2 * range, lightDimensions.y + 2 * range, range); // One-sided
                //    Vector3 extents = 0.5f * dimensions;
                //    Vector3 centerVS = positionVS + extents.z * zAxisVS;

                //    float d = range + 0.5f * Mathf.Sqrt(lightDimensions.x * lightDimensions.x + lightDimensions.y * lightDimensions.y);

                //    bound.center = centerVS;
                //    bound.boxAxisX = extents.x * xAxisVS;
                //    bound.boxAxisY = extents.y * yAxisVS;
                //    bound.boxAxisZ = extents.z * zAxisVS;
                //    bound.radius = Mathf.Sqrt(d * d + (0.5f * range) * (0.5f * range));
                //    bound.scaleXY = 1.0f;

                //    lightVolumeData.lightPos = centerVS;
                //    lightVolumeData.lightAxisX = xAxisVS;
                //    lightVolumeData.lightAxisY = yAxisVS;
                //    lightVolumeData.lightAxisZ = zAxisVS;
                //    lightVolumeData.boxInvRange.Set(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z);
                //    lightVolumeData.featureFlags = (uint)LightFeatureFlags.Area;
                //}
                else if (gpuLightType == GPULightType.ProjectorBox)
                {
                    Vector3 dimensions = new Vector3(lightDimensions.x, lightDimensions.y, range);  // One-sided
                    Vector3 extents = 0.5f * dimensions;
                    Vector3 centerVS = positionVS + extents.z * zAxisVS;

                    bound.center = centerVS;
                    bound.boxAxisX = extents.x * xAxisVS;
                    bound.boxAxisY = extents.y * yAxisVS;
                    bound.boxAxisZ = extents.z * zAxisVS;
                    bound.radius = extents.magnitude;
                    bound.scaleXY = 1.0f;

                    lightVolumeData.lightPos = centerVS;
                    lightVolumeData.lightAxisX = xAxisVS;
                    lightVolumeData.lightAxisY = yAxisVS;
                    lightVolumeData.lightAxisZ = zAxisVS;
                    lightVolumeData.boxInvRange.Set(1.0f / extents.x, 1.0f / extents.y, 1.0f / extents.z);
                    lightVolumeData.featureFlags = (uint)LightFeatureFlags.Punctual;
                }
                //else if (gpuLightType == GPULightType.Disc)
                //{
                //    //not supported at real time at the moment
                //}
                else
                {
                    Debug.Assert(false, "TODO: encountered an unknown GPULightType.");
                }

                lightBounds[lightIndex] = bound;
                lightVolumes[lightIndex] = lightVolumeData;
            }
        }

        #endregion JobSystem



    }
}
