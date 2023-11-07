using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Data for gameObjects to render shadow.
    /// </summary>
    public class PerObjectShadowData
    {
        //private GameObject m_Object = null;
        private Renderer[] m_Renderers;
        //private Vector3 m_CullingSphereCenter;
        //private float m_CullingSphereRadius;

        // Per Frame Data
        //public Bounds bounds;
        public PerObjectShadowSliceData sliceData;
        //private bool m_IsCulled;
        public Material material;
        public int shadowPassIndex;

        public PerObjectShadowData()
        {
            //bounds = new Bounds(Vector3.zero, Vector3.zero);
            sliceData = new PerObjectShadowSliceData();
            //m_IsCulled = false;

            //m_CullingSphereCenter = Vector3.zero;
            //m_CullingSphereRadius = 0;
        }

        /// <summary>
        /// Init target object and get childern renderers.
        /// </summary>
        /// <param name="gameObject"></param>
        /// <returns></returns>
        //public void InitFromGameObject(GameObject gameObject)
        //{
        //    m_Object = gameObject;
        //    m_Renderers = gameObject.GetComponentsInChildren<Renderer>();

        //    foreach (Renderer renderer in m_Renderers)
        //    {
        //        renderer.shadowCastingMode = ShadowCastingMode.Off;
        //    }
        //}

        /// <summary>
        /// Calculate bounds.
        /// </summary>
        /// <returns></returns>
        //public void UpdateBounds()
        //{
        //    if (!IsDataValid())
        //        return;

        //    bounds = GetRenderersBounds(m_Renderers);

        //    return;
        //}

        //private Bounds GetRenderersBounds(Renderer[] renderers)
        //{
        //    Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        //    if (m_Renderers.Length <= 0)
        //    {
        //        return bounds;
        //    }

        //    bounds = m_Renderers[0].bounds;
        //    for (int i = 1; i < m_Renderers.Length; i++)
        //    {
        //        bounds.Encapsulate(m_Renderers[i].bounds);
        //    }

        //    return bounds;
        //}

        public Renderer[] GetRenderers()
        {
            return m_Renderers;
        }
        public void SetRenderers(Renderer[] renderers)
        {
            m_Renderers = renderers;
        }

        //public void GetCullingSphere(out Vector3 center, out float radius)
        //{
        //    center = Vector3.zero;
        //    center.x = m_CullingSphereCenter.x;
        //    center.y = m_CullingSphereCenter.y;
        //    center.z = m_CullingSphereCenter.z;
        //    radius = m_CullingSphereRadius;
        //}

        //public void SetCullingSphere(Vector3 center, float radius)
        //{
        //    m_CullingSphereCenter = center;
        //    m_CullingSphereRadius = radius;
        //}

        public void Clear()
        {
            //m_Object = null;
            m_Renderers = null;
        }

        //public void SetCulledState(bool state)
        //{
        //    m_IsCulled = state;
        //}

        //public bool IsDataValid() => m_Object != null && m_Renderers != null;
        public bool IsDataValid() => m_Renderers != null;
        //public bool IsCulled() => m_IsCulled;
    }

    /// <summary>
    /// Struct container for shadow slice data.Copy from ShadowUtils ShadowSliceData.
    /// </summary>
    public struct PerObjectShadowSliceData
    {
        /// <summary>
        /// The view matrix.
        /// </summary>
        public Matrix4x4 viewMatrix;

        /// <summary>
        /// The projection matrix.
        /// </summary>
        public Matrix4x4 projectionMatrix;

        /// <summary>
        /// The shadow transform matrix.
        /// </summary>
        public Matrix4x4 shadowTransform;

        /// <summary>
        /// The shadow projector frustum to world matrix.
        /// </summary>
        public Matrix4x4 shadowToWorldMatrix;

        /// <summary>
        /// The shadowcoord in shadowmap scale and offset.
        /// </summary>
        public Vector4 uvScaleOffset;

        /// <summary>
        /// The X offset to the shadow map.
        /// </summary>
        public int offsetX;

        /// <summary>
        /// The Y offset to the shadow map.
        /// </summary>
        public int offsetY;

        /// <summary>
        /// The maximum tile resolution in an Atlas.
        /// </summary>
        public int resolution;

        /// <summary>
        /// Clears and resets the data.
        /// </summary>
        public void Clear()
        {
            viewMatrix = Matrix4x4.identity;
            projectionMatrix = Matrix4x4.identity;
            shadowTransform = Matrix4x4.identity;
            shadowToWorldMatrix = Matrix4x4.identity;
            offsetX = offsetY = 0;
            resolution = 1024;
        }
    }

    public static class PerObjectShadowUtils
    {
        public const int k_MaxObjectsNum = 32;

        /// <summary>
        /// Arrangements of shadow Slice.
        /// Size corresponds to k_MaxObjects.
        /// </summary>
        private readonly static int[] sliceArray =
        {
            0,  1,  4,  5,  16, 17, 20, 21,
            2,  3,  6,  7,  18, 19, 22, 23,
            8,  9,  12, 13, 24, 25, 28, 29,
            10, 11, 14, 15, 26, 27, 30, 31,
        };

        /// <summary>
        /// Shadow Projector
        /// </summary>
        private static Mesh m_ShadowProjectorMesh;
        public static Mesh shadowProjectorMesh
        {
            get
            {
                // TODO: Why DecalSystem Use 0.5?
                //if (m_ShadowProjectorMesh == null)
                //    m_ShadowProjectorMesh = CoreUtils.CreateCubeMesh(new Vector4(-0.5f, -0.5f, -0.5f, 1.0f), new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                //return m_ShadowProjectorMesh;
                if (m_ShadowProjectorMesh == null)
                    m_ShadowProjectorMesh = CoreUtils.CreateCubeMesh(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
                return m_ShadowProjectorMesh;
            }
        }

        /// <summary>
        /// Collect perObjectShadow game objects by tag.
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="objDataList"></param>
        /// <returns>The collected objects num.</returns>
        //public static int CollectGameObjects(string tag, ref List<PerObjectShadowData> objDataList)
        //{
        //    if (string.IsNullOrEmpty(tag))
        //    {
        //        Debug.LogWarning("Find gameObjects with tag: " + tag + " is not appropriate");
        //        return -1;
        //    }
        //    if (objDataList == null)
        //    {
        //        Debug.LogError("objDataList null");
        //        return -1;
        //    }


        //    GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
        //    int loopSize = objects.Length < objDataList.Capacity ? objects.Length : objDataList.Capacity;
        //    int index = 0;
        //    for (; index < loopSize; index++)
        //    {
        //        GameObject obj = objects[index];
        //        objDataList[index].InitFromGameObject(obj);
        //    }

        //    // Clear the rest.
        //    for (; index < objDataList.Capacity; index++)
        //    {
        //        objDataList[index].Clear();
        //    }

        //    return loopSize;
        //}

        /// <summary>
        /// Calculates the shadowMapResolution by objectsCount.
        /// </summary>
        /// <param name="shadowMapResolution"></param>
        /// <param name="tileCount"></param>
        /// <returns>The resolution for shadowMap Texture.</returns>
        public static Vector2Int GetPerObjectShadowMapResolution(int shadowMapResolution, int tileCount)
        {
            Vector2Int resolution = new Vector2Int(shadowMapResolution, shadowMapResolution);

            
            if (tileCount == 2)
            {
                // width:N height:N/2
                resolution.y = resolution.y >> 1;
            }
            else if (tileCount >= 5 && tileCount <= 8)
            {
                // width:2N height:N
                resolution.x = resolution.x << 1;
            }
            //else if (tileCount >= 9 && tileCount <= 16)
            //{
            //    // width:N height:N
            //}
            else if (tileCount >= 17 && tileCount <= 32)
            {
                // width:2N height:N
                resolution.x = resolution.x << 1;
            }


            return resolution;
        }

        /// <summary>
        /// Calculates the perObject tile resolution in an Atlas.
        /// </summary>
        /// <param name="atlasWidth"></param>
        /// <param name="atlasHeight"></param>
        /// <param name="tileCount"></param>
        /// <returns>The maximum tile resolution in an Atlas.</returns>
        public static int GetPerObjectTileResolutionInAtlas(int atlasWidth, int atlasHeight, int tileCount)
        {
            int resolution = Mathf.Min(atlasWidth, atlasHeight);
            int currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
            while (currentTileCount < tileCount)
            {
                resolution = resolution >> 1;
                currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
            }

            return resolution;
        }

        public static bool ExtractDirectionalLightMatrix
            (int tileIndex, Light light, Bounds boundsWS, float farPlaneScale, int shadowmapWidth, int shadowmapHeight, int tileResolution, out PerObjectShadowSliceData outSliceData)
        {
            Matrix4x4 viewMatrix;
            Matrix4x4 projMatrix;
            bool success = ComputePerObjectShadowMatrices(light, boundsWS, farPlaneScale, tileResolution, out viewMatrix, out projMatrix);

            var sliceData = new PerObjectShadowSliceData();
            sliceData.viewMatrix = viewMatrix;
            sliceData.projectionMatrix = projMatrix;
            sliceData.shadowTransform = GetShadowTransform(projMatrix, viewMatrix);
            sliceData.shadowToWorldMatrix = GetShadowProjectorToWorldMatrix(projMatrix, viewMatrix);

            Vector2Int offset = ComputeSliceOffset(tileIndex, tileResolution);
            sliceData.offsetX = offset.x;
            sliceData.offsetY = offset.y;
            sliceData.resolution = tileResolution;
            sliceData.uvScaleOffset = new Vector4((float)tileResolution / shadowmapWidth, (float)tileResolution / shadowmapHeight
                                                , (float)offset.x / shadowmapWidth, (float)offset.y / shadowmapHeight);

            ApplySliceTransform(ref sliceData, shadowmapWidth, shadowmapHeight);


            outSliceData = sliceData;

            return success;
        }

        public static bool ExtractDirectionalLightMatrix
            (int tileIndex, Matrix4x4 viewMatrix, Matrix4x4 projMatrix, int shadowmapWidth, int shadowmapHeight, int tileResolution, out PerObjectShadowSliceData outSliceData)
        {
            outSliceData.viewMatrix = viewMatrix;
            outSliceData.projectionMatrix = projMatrix;
            outSliceData.shadowTransform = GetShadowTransform(projMatrix, viewMatrix);
            outSliceData.shadowToWorldMatrix = GetShadowProjectorToWorldMatrix(projMatrix, viewMatrix);

            Vector2Int offset = ComputeSliceOffset(tileIndex, tileResolution);
            outSliceData.offsetX = offset.x;
            outSliceData.offsetY = offset.y;
            outSliceData.resolution = tileResolution;
            outSliceData.uvScaleOffset = new Vector4((float)tileResolution / shadowmapWidth, (float)tileResolution / shadowmapHeight
                                                , (float)offset.x / shadowmapWidth, (float)offset.y / shadowmapHeight);

            ApplySliceTransform(ref outSliceData, shadowmapWidth, shadowmapHeight);

            return true;
        }

        /// <summary>
        /// Calculates the depth and normal bias from a light.
        /// </summary>
        /// <param name="shadowLight"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="shadowData"></param>
        /// <param name="lightProjectionMatrix"></param>
        /// <param name="shadowResolution"></param>
        /// <returns>The depth and normal bias from a visible light.</returns>
        public static Vector4 GetShadowBias(ref VisibleLight shadowLight, int shadowLightIndex, ref ShadowData shadowData, Matrix4x4 lightProjectionMatrix, float shadowResolution)
        {
            if (shadowLightIndex < 0 || shadowLightIndex >= shadowData.bias.Count)
            {
                Debug.LogWarning(string.Format("{0} is not a valid light index.", shadowLightIndex));
                return Vector4.zero;
            }

            float frustumSize;
            if (shadowLight.lightType == LightType.Directional)
            {
                // Frustum size is guaranteed to be a cube as we wrap shadow frustum around a sphere
                frustumSize = 2.0f / lightProjectionMatrix.m00;
            }
            else
            {
                Debug.LogWarning("Only directional shadow casters are supported in per object shadow");
                frustumSize = 0.0f;
            }

            // depth and normal bias scale is in shadowmap texel size in world space
            float texelSize = frustumSize / shadowResolution;
            float depthBias = -shadowData.bias[shadowLightIndex].x * texelSize;
            float normalBias = -shadowData.bias[shadowLightIndex].y * texelSize;

            if (shadowData.supportsSoftShadows && shadowLight.light.shadows == LightShadows.Soft)
            {
                // TODO: depth and normal bias assume sample is no more than 1 texel away from shadowmap
                // This is not true with PCF. Ideally we need to do either
                // cone base bias (based on distance to center sample)
                // or receiver place bias based on derivatives.
                // For now we scale it by the PCF kernel size of non-mobile platforms (5x5)
                float kernelRadius = 2.5f;

                depthBias *= kernelRadius;
                normalBias *= kernelRadius;
            }

            return new Vector4(depthBias, normalBias, 0.0f, 0.0f);
        }

        /// <summary>
        /// Sets up the shadow bias, light direction and position for rendering.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="shadowLight"></param>
        /// <param name="shadowBias"></param>
        public static void SetupShadowCasterConstantBuffer(CommandBuffer cmd, ref VisibleLight shadowLight, Vector4 shadowBias)
        {
            cmd.SetGlobalVector("_ShadowBias", shadowBias);

            // Light direction is currently used in shadow caster pass to apply shadow normal offset (normal bias).
            Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
            cmd.SetGlobalVector("_LightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f));

            // For punctual lights, computing light direction at each vertex position provides more consistent results (shadow shape does not change when "rotating the point light" for example)
            Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
            cmd.SetGlobalVector("_LightPosition", new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1.0f));
        }

        /// <summary>
        /// RenderPerObjectShadowSlice
        /// </summary>
        /// <param name="renderers"></param>
        /// <param name="cmd"></param>
        /// <param name="context"></param>
        /// <param name="shadowSliceData"></param>
        public static void RenderPerObjectShadowSlice(Renderer[] renderers, CommandBuffer cmd, ref ScriptableRenderContext context,
            ref PerObjectShadowSliceData shadowSliceData, Material material, int passIndex)
        {
            Matrix4x4 view = shadowSliceData.viewMatrix;
            Matrix4x4 proj = shadowSliceData.projectionMatrix;

            cmd.SetGlobalDepthBias(1.0f, 2.5f); // these values match HDRP defaults (see https://github.com/Unity-Technologies/Graphics/blob/9544b8ed2f98c62803d285096c91b44e9d8cbc47/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAtlas.cs#L197 )

            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(view, proj);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            foreach (Renderer r in renderers)
            {
                int submeshCount = 0;
                switch (r)
                {
                    case MeshRenderer meshRenderer:
                        submeshCount = meshRenderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                        break;
                    case SkinnedMeshRenderer skinnedMeshRenderer:
                        submeshCount = skinnedMeshRenderer == null? 0 : skinnedMeshRenderer.sharedMesh.subMeshCount;
                        break;
                    default:
                        break;
                }

                for (int i = 0; i < submeshCount; ++i)
                {
                    cmd.DrawRenderer(r, material, i, passIndex);
                }
            }

            cmd.DisableScissorRect();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.SetGlobalDepthBias(0.0f, 0.0f); // Restore previous depth bias values
        }


        /// <summary>
        /// Compute slice offset with fixed arrangements.
        /// </summary>
        /// <param name="tileIndex"></param>
        /// <param name="tileResolution"></param>
        /// <returns>sliceOffset position</returns>
        public static Vector2Int ComputeSliceOffset(int tileIndex, int tileResolution)
        {
            Vector2Int offset = Vector2Int.zero;

            int x = 0;
            for (; x < sliceArray.Length; x++)
            {
                if (tileIndex == sliceArray[x])
                    break;
            }

            offset.x = x % 8 * tileResolution;
            offset.y = x / 8 * tileResolution;

            return offset;
        }

        public static int2 ComputeSliceOffsetInt2(int tileIndex, int tileResolution)
        {
            int2 offset = int2.zero;

            int x = 0;
            for (; x < sliceArray.Length; x++)
            {
                if (tileIndex == sliceArray[x])
                    break;
            }

            offset.x = x % 8 * tileResolution;
            offset.y = x / 8 * tileResolution;

            return offset;
        }

        /// <summary>
        /// Used for baking each tile transforms in each shadow matrix.
        /// </summary>
        /// <param name="shadowSliceData"></param>
        /// <param name="atlasWidth"></param>
        /// <param name="atlasHeight"></param>
        public static void ApplySliceTransform(ref PerObjectShadowSliceData shadowSliceData, int atlasWidth, int atlasHeight)
        {
            Matrix4x4 sliceTransform = Matrix4x4.identity;
            float oneOverAtlasWidth = 1.0f / atlasWidth;
            float oneOverAtlasHeight = 1.0f / atlasHeight;
            sliceTransform.m00 = shadowSliceData.resolution * oneOverAtlasWidth;
            sliceTransform.m11 = shadowSliceData.resolution * oneOverAtlasHeight;
            sliceTransform.m03 = shadowSliceData.offsetX * oneOverAtlasWidth;
            sliceTransform.m13 = shadowSliceData.offsetY * oneOverAtlasHeight;

            // Apply shadow slice scale and offset
            shadowSliceData.shadowTransform = sliceTransform * shadowSliceData.shadowTransform;
        }

        public static Matrix4x4 ApplySliceTransformFloat44(Matrix4x4 shadowTransform, int resolution, int2 offset, int atlasWidth, int atlasHeight)
        {
            Matrix4x4 sliceTransform = Matrix4x4.identity;
            float oneOverAtlasWidth = 1.0f / atlasWidth;
            float oneOverAtlasHeight = 1.0f / atlasHeight;
            sliceTransform.m00 = resolution * oneOverAtlasWidth;
            sliceTransform.m11 = resolution * oneOverAtlasHeight;
            sliceTransform.m03 = offset.x * oneOverAtlasWidth;
            sliceTransform.m13 = offset.y * oneOverAtlasHeight;

            // Apply shadow slice scale and offset
            return sliceTransform * shadowTransform;
            //float4x4 sliceTransform = float4x4.identity;
            //float oneOverAtlasWidth = 1.0f / atlasWidth;
            //float oneOverAtlasHeight = 1.0f / atlasHeight;
            //sliceTransform.c0.x = resolution * oneOverAtlasWidth;
            //sliceTransform.c1.y = resolution * oneOverAtlasHeight;
            //sliceTransform.c0.w = offset.x * oneOverAtlasWidth;
            //sliceTransform.c1.w = offset.y * oneOverAtlasHeight;

            //// Apply shadow slice scale and offset
            //return sliceTransform * shadowTransform;
        }

        /// <summary>
        /// Chose Fixed Size Box Bounding.
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <returns></returns>
        [Obsolete("Use Culling Sphere")]
        public static bool ComputePerObjectShadowMatricesAndCullingBox(Light light, Bounds boundsWS, float farPlaneScale, int shadowResolution, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            // Anti-Shimmering
            {
                float squrePixelWidth = 2 * boxExtendLength / shadowResolution;
                Vector3 sphereCenterLS = light.transform.worldToLocalMatrix.MultiplyPoint(boxCenter);
                sphereCenterLS.x /= squrePixelWidth;
                sphereCenterLS.x = Mathf.Floor(sphereCenterLS.x);
                sphereCenterLS.x *= squrePixelWidth;
                sphereCenterLS.y /= squrePixelWidth;
                sphereCenterLS.y = Mathf.Floor(sphereCenterLS.y);
                sphereCenterLS.y *= squrePixelWidth;
                boxCenter = light.transform.localToWorldMatrix.MultiplyPoint(sphereCenterLS);
            }

            Vector3 shadowMapEye = boxCenter - light.transform.forward * boxExtendLength;
            Vector3 shadowMapAt = boxCenter;
            var lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, light.transform.up);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;
            projMatrix = Matrix4x4.Ortho(-boxExtendLength, boxExtendLength, -boxExtendLength, boxExtendLength, 0.0f, 2.0f * boxExtendLength * farPlaneScale);

            return true;
        }

        /// <summary>
        /// ComputePerObjectShadowMatricesAndCullingBox
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <returns></returns>
        [Obsolete("Use Culling Sphere")]
        public static bool ComputePerObjectShadowMatricesAndCullingBox(Light light, Bounds boundsWS, float farPlaneScale, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Vector3[] cullingBoxVertex)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            Vector3 shadowMapEye = boxCenter - light.transform.forward * boxExtendLength;
            Vector3 shadowMapAt = boxCenter;
            var lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, light.transform.up);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            float zFar = 2.0f * boxExtendLength * farPlaneScale;
            projMatrix = Matrix4x4.Ortho(-boxExtendLength, boxExtendLength, -boxExtendLength, boxExtendLength, 0.0f, zFar);

            // Culling Box
            Vector3 axisX = light.transform.right;
            Vector3 axisY = light.transform.up;
            Vector3 cullingBoxNearTL = shadowMapEye - axisX * boxExtendLength + axisY * boxExtendLength;
            Vector3 cullingBoxNearTR = cullingBoxNearTL + 2 * axisX * boxExtendLength;
            Vector3 cullingBoxNearBL = cullingBoxNearTL - 2 * axisY * boxExtendLength;
            Vector3 cullingBoxNearBR = cullingBoxNearBL + 2 * axisX * boxExtendLength;
            Vector3 cullingBoxFarTL = cullingBoxNearTL + zFar * light.transform.forward;
            Vector3 cullingBoxFarTR = cullingBoxNearTR + zFar * light.transform.forward;
            Vector3 cullingBoxFarBL = cullingBoxNearBL + zFar * light.transform.forward;
            Vector3 cullingBoxFarBR = cullingBoxNearBR + zFar * light.transform.forward;

            Vector3[] cullingBox = new Vector3[8];
            cullingBox[0] = cullingBoxNearTL;
            cullingBox[1] = cullingBoxNearTR;
            cullingBox[2] = cullingBoxNearBL;
            cullingBox[3] = cullingBoxNearBR;
            cullingBox[4] = cullingBoxFarTL;
            cullingBox[5] = cullingBoxFarTR;
            cullingBox[6] = cullingBoxFarBL;
            cullingBox[7] = cullingBoxFarBR;

            cullingBoxVertex = cullingBox;

            return true;
        }

        /// <summary>
        /// Compute Culling Sphere.
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <param name="farPlaneScale"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <param name="sphereCenter"></param>
        /// <param name="sphereRadius"></param>
        /// <returns></returns>
        public static bool ComputePerObjectShadowMatricesAndCullingSphere(Light light, Bounds boundsWS, float farPlaneScale, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Vector3 sphereCenter, out float sphereRadius)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            Vector3 shadowMapEye = boxCenter - light.transform.forward * boxExtendLength;
            Vector3 shadowMapAt = boxCenter;
            var lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, light.transform.up);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            float zFar = 2.0f * boxExtendLength * farPlaneScale;
            projMatrix = Matrix4x4.Ortho(-boxExtendLength, boxExtendLength, -boxExtendLength, boxExtendLength, 0.0f, zFar);

            // Culling Sphere
            Vector3 axisX = light.transform.right;
            Vector3 axisY = light.transform.up;
            Vector3 cullingBoxNearTL = shadowMapEye - axisX * boxExtendLength + axisY * boxExtendLength;
            sphereCenter = shadowMapEye + zFar * 0.5f * light.transform.forward;
            sphereRadius = Vector3.Distance(cullingBoxNearTL, sphereCenter);

            return true;
        }


        /// <summary>
        /// Compute Culling Sphere.Used for JobSystem
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <param name="farPlaneScale"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <param name="sphereCenter"></param>
        /// <param name="sphereRadius"></param>
        /// <returns></returns>
        public static bool ComputePerObjectShadowMatricesAndCullingSphere(Vector3 forwardDir, Vector3 upDir, Vector3 rightDir, Bounds boundsWS, float farPlaneScale, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Vector3 sphereCenter, out float sphereRadius)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            Vector3 shadowMapEye = boxCenter - forwardDir * boxExtendLength;
            Vector3 shadowMapAt = boxCenter;
            var lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, upDir);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            float zFar = 2.0f * boxExtendLength * farPlaneScale;
            projMatrix = Matrix4x4.Ortho(-boxExtendLength, boxExtendLength, -boxExtendLength, boxExtendLength, 0.0f, zFar);

            // Culling Sphere
            Vector3 axisX = rightDir;
            Vector3 axisY = upDir;
            Vector3 cullingBoxNearTL = shadowMapEye - axisX * boxExtendLength + axisY * boxExtendLength;
            sphereCenter = shadowMapEye + zFar * 0.5f * forwardDir;
            sphereRadius = Vector3.Distance(cullingBoxNearTL, sphereCenter);

            return true;
        }

        /// <summary>
        /// Compute object cullingSphere to match shadowmap frustum, used for camera culling
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <param name="farPlaneScale"></param>
        /// <param name="sphereCenter"></param>
        /// <param name="sphereRadius"></param>
        /// <returns></returns>
        public static bool ComputePerObjectShadowCullingSphere(Light light, Bounds boundsWS, float farPlaneScale, out Vector3 sphereCenter, out float sphereRadius)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            Vector3 shadowMapEye = boxCenter - light.transform.forward * boxExtendLength;
            float zFar = 2.0f * boxExtendLength * farPlaneScale;

            // Culling Sphere
            Vector3 axisX = light.transform.right;
            Vector3 axisY = light.transform.up;
            Vector3 cullingBoxNearTL = shadowMapEye - axisX * boxExtendLength + axisY * boxExtendLength;
            sphereCenter = shadowMapEye + zFar * 0.5f * light.transform.forward;
            sphereRadius = Vector3.Distance(cullingBoxNearTL, sphereCenter);

            return true;
        }


        /// <summary>
        /// Compute object shadowmap matrices.
        /// Chose Fixed Size Box Bounding.
        /// No cascade here so we have no use for sphere Bounding.
        /// </summary>
        /// <param name="light"></param>
        /// <param name="boundsWS"></param>
        /// <param name="farPlaneScale"></param>
        /// <param name="tileResolution"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <returns></returns>
        public static bool ComputePerObjectShadowMatrices(Light light, Bounds boundsWS, float farPlaneScale, int tileResolution, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix)
        {
            Vector3 boxCenter = boundsWS.center;

            // Center to edge extend, lightViewSpace length equals to worldSpace length.
            float boxExtendLength = boundsWS.extents.magnitude;

            //// Object Shadow Center follow boxCenter, no need to Anti-Shimmering
            //{
            //    float squrePixelWidth = 2 * boxExtendLength / tileResolution;
            //    Vector3 sphereCenterLS = light.transform.worldToLocalMatrix.MultiplyPoint(boxCenter);
            //    sphereCenterLS.x /= squrePixelWidth;
            //    sphereCenterLS.x = Mathf.Floor(sphereCenterLS.x);
            //    sphereCenterLS.x *= squrePixelWidth;
            //    sphereCenterLS.y /= squrePixelWidth;
            //    sphereCenterLS.y = Mathf.Floor(sphereCenterLS.y);
            //    sphereCenterLS.y *= squrePixelWidth;
            //    boxCenter = light.transform.localToWorldMatrix.MultiplyPoint(sphereCenterLS);
            //}

            Vector3 shadowMapEye = boxCenter - light.transform.forward * boxExtendLength;
            Vector3 shadowMapAt = boxCenter;
            var lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, light.transform.up);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            var scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            float zFar = 2.0f * boxExtendLength * farPlaneScale;
            projMatrix = Matrix4x4.Ortho(-boxExtendLength, boxExtendLength, -boxExtendLength, boxExtendLength, 0.0f, zFar);

            return true;
        }

        /// <summary>
        /// Use camera frustum do boxCulling
        /// </summary>
        /// <param name="cullingBoxVertex"></param>
        /// <param name="camera"></param>
        /// <returns>true: IsNotCulling. false: culling</returns>
        public static bool ObjectShadowCameraFrustumBoxCulling(Vector3[] cullingBoxVertex, Camera camera)
        {
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);

            if (cullingBoxVertex.Length != 8 || frustumPlanes.Length != 6 || camera == null)
                return false;

            for (int i = 0; i < 6; i++)
            {
                Plane plane = frustumPlanes[i];
                for (int j = 0; j < 8; j++)
                {
                    Vector3 point = cullingBoxVertex[j];

                    float d = Vector3.Dot(point, plane.normal);

                    // Inside, check next plane
                    if (plane.distance + d > 0)
                        break;

                    // All vertices are outside this plane
                    if (j == 7)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Use camera frustum do sphereCulling
        /// </summary>
        /// <param name="sphereCenter"></param>
        /// <param name="sphereRadius"></param>
        /// <param name="camera"></param>
        /// <returns>true: IsCulled. false: not culled</returns>
        public static bool ObjectShadowCameraFrustumSphereCulling(Plane[] frustumPlanes, Vector3 sphereCenter, float sphereRadius)
        {
            if (sphereCenter == null || sphereRadius == 0)
                return true;

            for (int i = 0; i < 6; i++)
            {
                Plane plane = frustumPlanes[i];
                float d = Vector3.Dot(sphereCenter, plane.normal);

                // Outside, return
                if (plane.distance + d + sphereRadius < 0)
                    return true;

            }

            return false;
        }

        /// <summary>
        /// Copy from ShadowUtils.Apply z reversal to projection matrix
        /// </summary>
        /// <param name="proj"></param>
        /// <param name="view"></param>
        /// <returns>ShadowTransform VP</returns>
        public static Matrix4x4 GetShadowTransform(Matrix4x4 proj, Matrix4x4 view)
        {
            // apply z reversal to projection matrix. We need to do it manually here.
            if (SystemInfo.usesReversedZBuffer)
            {
                proj.m20 = -proj.m20;
                proj.m21 = -proj.m21;
                proj.m22 = -proj.m22;
                proj.m23 = -proj.m23;
            }

            Matrix4x4 worldToShadow = proj * view;

            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            // textureScaleAndBias maps texture space coordinates from [-1,1] to [0,1]

            // Apply texture scale and offset to save a MAD in shader.
            return textureScaleAndBias * worldToShadow;
        }

        public static Matrix4x4 GetShadowTransformFloat44(Matrix4x4 proj, Matrix4x4 view, bool usesReversedZBuffer)
        {
            // apply z reversal to projection matrix. We need to do it manually here.
            if (usesReversedZBuffer)
            {
                proj.m20 = -proj.m20;
                proj.m21 = -proj.m21;
                proj.m22 = -proj.m22;
                proj.m23 = -proj.m23;
            }

            Matrix4x4 worldToShadow = proj * view;

            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            // textureScaleAndBias maps texture space coordinates from [-1,1] to [0,1]

            // Apply texture scale and offset to save a MAD in shader.
            return textureScaleAndBias * worldToShadow;

            //// apply z reversal to projection matrix. We need to do it manually here.
            //if (usesReversedZBuffer)
            //{
            //    proj.c2.x = -proj.c2.x;
            //    proj.c2.y = -proj.c2.y;
            //    proj.c2.z = -proj.c2.z;
            //    proj.c2.w = -proj.c2.w;
            //}

            //float4x4 worldToShadow = proj * view;

            //var textureScaleAndBias = float4x4.identity;
            //textureScaleAndBias.c0.x = 0.5f;
            //textureScaleAndBias.c1.y = 0.5f;
            //textureScaleAndBias.c2.z = 0.5f;
            //textureScaleAndBias.c0.w = 0.5f;
            //textureScaleAndBias.c2.w = 0.5f;
            //textureScaleAndBias.c1.w = 0.5f;
            //// textureScaleAndBias maps texture space coordinates from [-1,1] to [0,1]

            //// Apply texture scale and offset to save a MAD in shader.
            //return textureScaleAndBias * worldToShadow;
        }

        public static Matrix4x4 GetShadowProjectorToWorldMatrix(Matrix4x4 proj, Matrix4x4 view)
        {
            Matrix4x4 worldToShadow = proj * view;
            Matrix4x4 shadowToworld = worldToShadow.inverse;
            
            return shadowToworld;
        }

        /// <summary>
        /// Use URP ShadowUtils.
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null).</param>
        /// <param name="width">Width of the Shadow Map.</param>
        /// <param name="height">Height of the Shadow Map.</param>
        /// <param name="bits">Minimum depth bits of the Shadow Map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level of the Shadow Map.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering of the Shadow Map.</param>
        /// <param name="name">Name of the Shadow Map.</param>
        /// <returns>If the RTHandle was re-allocated</returns>
        public static bool ShadowRTReAllocateIfNeeded(ref RTHandle handle, int width, int height, int bits, int anisoLevel = 1, float mipMapBias = 0, string name = "")
        {
            return ShadowUtils.ShadowRTReAllocateIfNeeded(ref handle, width, height, bits, anisoLevel, mipMapBias, name);
        }
    }
}