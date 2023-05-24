using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class MainLight8CascadeShadowCasterPass : ScriptableRenderPass
    {
        private static class MainLight8CascadeShadowConstantBuffer
        {
            public static int _WorldToShadow;
            public static int _ShadowParams;

            public static int _CascadeShadowSplitSpheresArray;
            public static int _CascadeZDistanceArray;
            public static int _ShadowOffsetArray;

            public static int _ShadowmapSize;
        }

        const int k_MaxCascades = 8;
        const int k_ShadowmapBufferBits = 16;
        float m_MaxShadowDistance;
        int m_ShadowmapWidth;
        int m_ShadowmapHeight;
        int m_ShadowCasterCascadesCount;
        bool m_SupportsBoxFilterForShadows;

        int m_ShadowmapCacheWidth;
        int m_ShadowmapCacheHeight;
        int m_ShadowCascadeResolution;
        int m_FarCascadesCount;
        int m_CurrentCacheCascadesIndex;
        bool m_NeedRefreshAllShadowmapCache;
        int[] m_RenderCascadeIndexArray;

        const int SHADER_NUMTHREAD_X = 8; //match compute shader's [numthread(x)]
        const int SHADER_NUMTHREAD_Y = 8; //match compute shader's [numthread(y)]
        ComputeShader m_CacheCompute;

        public RenderTargetHandle m_MainLightShadowmapCacahe;
        public RenderTexture m_MainLightShadowmapCacaheTexture;

        Matrix4x4[] m_MainLightShadowMatrices;
        ShadowSliceData[] m_CascadeSlices;
        Vector4[] m_CascadeSplitDistances;

        ProfilingSampler m_ProfilingSetupSampler = new ProfilingSampler("Setup Custom Main Shadowmap");


        public MainLight8CascadeShadowCasterPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler(nameof(MainLight8CascadeShadowCasterPass));
            renderPassEvent = evt;

            m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
            m_CascadeSlices = new ShadowSliceData[k_MaxCascades];
            m_CascadeSplitDistances = new Vector4[k_MaxCascades];

            MainLight8CascadeShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
            MainLight8CascadeShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");

            MainLight8CascadeShadowConstantBuffer._CascadeShadowSplitSpheresArray = Shader.PropertyToID("_CascadeShadowSplitSpheresArray");
            MainLight8CascadeShadowConstantBuffer._CascadeZDistanceArray = Shader.PropertyToID("_CascadeZDistanceArray");
            MainLight8CascadeShadowConstantBuffer._ShadowOffsetArray = Shader.PropertyToID("_MainLightShadowOffsetArray");

            MainLight8CascadeShadowConstantBuffer._ShadowmapSize = Shader.PropertyToID("_MainLightShadowmapSize");

            if (m_CacheCompute == null)
            {
                m_CacheCompute = (ComputeShader)(Resources.Load("CustomShadowmapCopyCache"));
            }


            m_MainLightShadowmapCacahe.Init("_MainLightShadowmapCacheTex");

            m_NeedRefreshAllShadowmapCache = false;
            //m_MainLightShadowmap.Init("_MainLightShadowmapTexture");
            m_SupportsBoxFilterForShadows = Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Switch;
        }

        public Vector2 GetShadowmapSize()
        {
            return new Vector2(m_ShadowmapCacheWidth, m_ShadowmapCacheHeight);
        }

        public int GetCascadeResolution()
        {
            return m_ShadowCascadeResolution;
        }

        public bool IsShadowmapReady()
        {
            if (!m_MainLightShadowmapCacaheTexture)
                return false;
            if (!m_MainLightShadowmapCacaheTexture.IsCreated())
                return false;

            return true;
        }
        public bool Setup(ref RenderingData renderingData)
        {
            using var profScope = new ProfilingScope(null, m_ProfilingSetupSampler);

            if (!renderingData.shadowData.supportsMainLightShadows)
                return false;

            Clear();

            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return false;

            VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
            Light light = shadowLight.light;
            if (light.shadows == LightShadows.None)
                return false;

            if (shadowLight.lightType != LightType.Directional)
            {
                Debug.LogWarning("Only directional lights are supported as main light.");
            }

            Bounds bounds;
            if (!renderingData.cullResults.GetShadowCasterBounds(shadowLightIndex, out bounds))
                return false;

            m_ShadowCasterCascadesCount = renderingData.shadowData.mainLightShadowCascadesCount;

            m_ShadowCascadeResolution = ShadowUtils.GetMaxTileResolutionInAtlasFor8Cascade(renderingData.shadowData.mainLightShadowmapWidth,
                renderingData.shadowData.mainLightShadowmapHeight, m_ShadowCasterCascadesCount);

            //for 8 cascades
            m_ShadowmapWidth = renderingData.shadowData.mainLightShadowmapWidth;
            m_ShadowmapHeight = (m_ShadowCasterCascadesCount == 1) ?
                                renderingData.shadowData.mainLightShadowmapHeight :
                                (m_ShadowCasterCascadesCount + 1) / 2 * renderingData.shadowData.mainLightShadowmapHeight / 2;

            HandleCacheCascadesCountChanged();
            m_RenderCascadeIndexArray = GetRenderCascadeIndexArray();

            for (int Index = 0; Index < m_RenderCascadeIndexArray.Length; ++Index)
            {
                int cascadeIndex = m_RenderCascadeIndexArray[Index];
                if (cascadeIndex < 0)
                {
                    continue;
                }

                bool success = ShadowUtils.ExtractDirectionalLightMatrix(ref renderingData.cameraData, ref renderingData.shadowData, cascadeIndex
                    , m_ShadowmapWidth, m_ShadowmapHeight, m_ShadowCascadeResolution, light
                    , out m_CascadeSplitDistances[cascadeIndex], out m_CascadeSlices[cascadeIndex], out m_CascadeSlices[cascadeIndex].viewMatrix, out m_CascadeSlices[cascadeIndex].projectionMatrix);

                //show cullsphere
                //DrawCullSphere script = renderingData.cameraData.camera.GetComponent<DrawCullSphere>();
                //if (script)
                //{
                //    script.cullsphere[cascadeIndex] = m_CascadeSplitDistances[cascadeIndex];
                //}

                if (!success)
                    return false;
            }



            //if(!renderingData.cameraData.isSceneViewCamera)
            //{

            //    DrawCullSphere script = renderingData.cameraData.camera.GetComponent<DrawCullSphere>();

            //    if (script)
            //    {
            //        //script.cullsphere[0] = cullSphere;
            //        script.drawFrustum.position = renderingData.cameraData.camera.transform.position;
            //        script.drawFrustum.fieldOfView = renderingData.cameraData.camera.fieldOfView;
            //        //script.drawFrustum.farClipPlane = clipPlane.y;
            //        //script.drawFrustum.nearClipPlane = clipPlane.x;
            //        script.drawFrustum.aspect = renderingData.cameraData.camera.aspect;

            //    }
            //}

            //TODO: Why?
            m_MaxShadowDistance = renderingData.cameraData.maxShadowDistance * renderingData.cameraData.maxShadowDistance;

            return true;
        }


        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (m_FarCascadesCount > 0)
            {
                if (m_NeedRefreshAllShadowmapCache)
                {
                    ClearShadowMapCacheTex();

                    bool result = ShadowUtils.GetShadowCacheTexture(ref m_MainLightShadowmapCacaheTexture, m_ShadowmapCacheWidth, m_ShadowmapCacheHeight, 16);
                    Debug.Log("CacheTexture: get " + result);
                }

                if (!m_MainLightShadowmapCacaheTexture || !m_MainLightShadowmapCacaheTexture.IsCreated())
                {
                    m_FarCascadesCount = -1;
                    return;
                }

                ConfigureTarget(m_MainLightShadowmapCacaheTexture);

                //Clear part of cacheTexture
                if (m_NeedRefreshAllShadowmapCache)
                {
                    //Use ConfigureClear to clear all
                    ConfigureClear(ClearFlag.All, Color.black);

                    m_NeedRefreshAllShadowmapCache = false;
                }
                else
                {
                    ConfigureClear(ClearFlag.Depth, Color.black);
                    //Compute shader clear
                    int dispatchThreadGroupXCount = m_ShadowmapWidth / SHADER_NUMTHREAD_X; //divide by shader's numthreads.x
                    int dispatchThreadGroupYCount = m_ShadowmapHeight / SHADER_NUMTHREAD_Y; //divide by shader's numthreads.y
                    int dispatchThreadGroupZCount = 1; //divide by shader's numthreads.z



                    //int kernelIndex = m_CacheCompute.FindKernel("CopyCacheShadowmap");
                    //cmd.SetComputeTextureParam(m_CacheCompute, kernelIndex, m_MainLightShadowmapCacahe.id, m_MainLightShadowmapCacaheTexture);
                    //cmd.SetComputeIntParam(m_CacheCompute, "_ShadowmapCacheWidth", m_ShadowmapWidth);
                    //cmd.SetComputeIntParam(m_CacheCompute, "_ShadowmapCacheHeight", m_ShadowmapHeight);
                    //cmd.SetComputeIntParam(m_CacheCompute, "_CurrentCacheCascadesIndex", m_CurrentCacheCascadesIndex);

                    //cmd.DispatchCompute(m_CacheCompute, kernelIndex, dispatchThreadGroupXCount, dispatchThreadGroupYCount, dispatchThreadGroupZCount);
                }

            }
            else
            {
                if (m_NeedRefreshAllShadowmapCache)
                {
                    ClearShadowMapCacheTex();

                    bool result = ShadowUtils.GetShadowCacheTexture(ref m_MainLightShadowmapCacaheTexture, m_ShadowmapCacheWidth, m_ShadowmapCacheHeight, 16);
                    Debug.Log("CacheTexture: get " + result);
                }

                if (!m_MainLightShadowmapCacaheTexture || !m_MainLightShadowmapCacaheTexture.IsCreated())
                {
                    m_FarCascadesCount = -1;
                    return;
                }

                ConfigureTarget(m_MainLightShadowmapCacaheTexture);

                //Clear all of cacheTexture
                ConfigureClear(ClearFlag.All, Color.black);
                //no need to reConstrust texture
                m_NeedRefreshAllShadowmapCache = false;

                //m_MainLightShadowmapTexture = ShadowUtils.GetTemporaryShadowTexture(m_ShadowmapWidth, m_ShadowmapHeight, 16);

                //ConfigureTarget(new RenderTargetIdentifier(m_MainLightShadowmapTexture));
                //ConfigureClear(ClearFlag.All, Color.black);
            }
        }


        //clean rendertarget
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            if (m_FarCascadesCount > 0)
            {
                m_CurrentCacheCascadesIndex++;
                m_CurrentCacheCascadesIndex %= m_FarCascadesCount;
            }
        }

        void Clear()
        {

            //for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
            //    m_MainLightShadowMatrices[i] = Matrix4x4.identity;

            //for (int i = 0; i < m_CascadeSplitDistances.Length; ++i)
            //    m_CascadeSplitDistances[i] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);


            //for (int i = 0; i < m_CascadeSlices.Length; ++i)
            //    m_CascadeSlices[i].Clear();
        }

        public void ClearShadowMapCacheTex()
        {
            if (m_MainLightShadowmapCacaheTexture != null)
            {
                m_MainLightShadowmapCacaheTexture.Release();
                Debug.Log("CacheTexture: release");
            }
            m_MainLightShadowmapCacaheTexture = null;
        }

        void HandleCacheCascadesCountChanged()
        {

            if (m_FarCascadesCount != (m_ShadowCasterCascadesCount - 4))
            {
                m_FarCascadesCount = m_ShadowCasterCascadesCount - 4;

                if (m_FarCascadesCount > 0)
                {
                    m_NeedRefreshAllShadowmapCache = true;
                    m_CurrentCacheCascadesIndex = 0;
                    m_ShadowmapCacheWidth = m_ShadowmapWidth;
                    m_ShadowmapCacheHeight = m_ShadowmapHeight;
                }
                else
                {
                    m_NeedRefreshAllShadowmapCache = true;
                    m_CurrentCacheCascadesIndex = 0;
                    m_ShadowmapCacheWidth = m_ShadowmapWidth;
                    m_ShadowmapCacheHeight = m_ShadowmapHeight;

                    ////Do not need cache
                    //ClearShadowMapCacheTex();
                }
            }
        }

        int[] GetRenderCascadeIndexArray()
        {
            int[] needRenderCascadeIndexArray = new int[m_ShadowCasterCascadesCount];
            for (int i = 0; i < m_ShadowCasterCascadesCount; i++)
            {
                needRenderCascadeIndexArray[i] = i;
            }

            if (m_FarCascadesCount > 0 && !m_NeedRefreshAllShadowmapCache)
            {
                for (int i = 4; i < m_ShadowCasterCascadesCount; i++)
                {
                    needRenderCascadeIndexArray[i] = -1;
                }
                needRenderCascadeIndexArray[m_CurrentCacheCascadesIndex + 4] = m_CurrentCacheCascadesIndex + 4;
            }

            return needRenderCascadeIndexArray;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("MainShadow8Cascade")))
            {
                ShadowData shadowData = renderingData.shadowData;
                ShadowDrawingSettings settings = new ShadowDrawingSettings(renderingData.cullResults, shadowLightIndex);

                // clear close shadowmap
                cmd.SetViewport(new Rect(0, 0, m_ShadowmapWidth, m_ShadowmapWidth));
                cmd.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                for (int Index = 0; Index < m_RenderCascadeIndexArray.Length; ++Index)
                {
                    int cascadeIndex = m_RenderCascadeIndexArray[Index];
                    if (cascadeIndex < 0)
                    {
                        continue;
                    }

                    var splitData = settings.splitData;
                    splitData.cullingSphere = m_CascadeSplitDistances[cascadeIndex];

                    settings.splitData = splitData;
                    Vector4 shadowBias = ShadowUtils.GetShadowBias(ref shadowLight, shadowLightIndex, ref shadowData, m_CascadeSlices[cascadeIndex].projectionMatrix, m_CascadeSlices[cascadeIndex].resolution);
                    ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, shadowBias);
                    ShadowUtils.RenderShadowSlice(cmd, ref context, ref m_CascadeSlices[cascadeIndex], ref settings, cascadeIndex >= 4,
                        m_CascadeSlices[cascadeIndex].projectionMatrix, m_CascadeSlices[cascadeIndex].viewMatrix);

                }



                bool softShadows = shadowLight.light.shadows == LightShadows.Soft && shadowData.supportsSoftShadows;
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, true);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.CustomMainLightShadows, true);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, shadowData.mainLightShadowCascadesCount > 1);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.SoftShadows, softShadows);


                SetupMainLightShadowReceiverConstants(cmd, shadowLight, shadowData.supportsSoftShadows);
            }



            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

        }

        void SetupMainLightShadowReceiverConstants(CommandBuffer cmd, VisibleLight shadowLight, bool supportsSoftShadows)
        {
            Light light = shadowLight.light;
            bool softShadows = shadowLight.light.shadows == LightShadows.Soft && supportsSoftShadows;

            int cascadeCount = m_ShadowCasterCascadesCount;
            for (int i = 0; i < k_MaxCascades; ++i)
                m_MainLightShadowMatrices[i] = m_CascadeSlices[i].shadowTransform;

            // We setup and additional a no-op WorldToShadow matrix in the last index
            // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
            // out of bounds. (position not inside any cascade) and we want to avoid branching
            Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
            noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
            for (int i = cascadeCount; i <= k_MaxCascades; ++i)
                m_MainLightShadowMatrices[i] = noOpShadowMatrix;

            float invShadowAtlasWidth = 1.0f / m_ShadowmapWidth;
            float invShadowAtlasHeight = 1.0f / m_ShadowmapHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;
            float softShadowsProp = softShadows ? 1.0f : 0.0f;

            //To make the shadow fading fit into a single MAD instruction:
            //distanceCamToPixel2 * oneOverFadeDist + minusStartFade (single MAD)
            float startFade = m_MaxShadowDistance * 0.9f;
            float oneOverFadeDist = 1 / (m_MaxShadowDistance - startFade);
            float minusStartFade = -startFade * oneOverFadeDist;



            cmd.SetGlobalTexture(m_MainLightShadowmapCacahe.id, m_MainLightShadowmapCacaheTexture);



            cmd.SetGlobalMatrixArray(MainLight8CascadeShadowConstantBuffer._WorldToShadow, m_MainLightShadowMatrices);
            cmd.SetGlobalVector(MainLight8CascadeShadowConstantBuffer._ShadowParams, new Vector4(light.shadowStrength, softShadowsProp, oneOverFadeDist, minusStartFade));

            if (m_ShadowCasterCascadesCount > 1)
            {
                cmd.SetGlobalVectorArray(MainLight8CascadeShadowConstantBuffer._CascadeShadowSplitSpheresArray, m_CascadeSplitDistances);
            }

            float[] mZDistanceArray = new float[k_MaxCascades];
            for (int i = 0; i < k_MaxCascades; i++)
            {
                mZDistanceArray[i] = m_CascadeSlices[i].ZDistance;
            }
            cmd.SetGlobalFloatArray(MainLight8CascadeShadowConstantBuffer._CascadeZDistanceArray, mZDistanceArray);

            // Inside shader soft shadows are controlled through global keyword.
            // If any additional light has soft shadows it will force soft shadows on main light too.
            // As it is not trivial finding out which additional light has soft shadows, we will pass main light properties if soft shadows are supported.
            // This workaround will be removed once we will support soft shadows per light.
            if (supportsSoftShadows)
            {
                if (m_SupportsBoxFilterForShadows)
                {
                    Vector4[] mainLightShadowOffsetArray = new Vector4[4] { new Vector4(-invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight, 0.0f, 0.0f),
                        new Vector4(invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight, 0.0f, 0.0f),
                        new Vector4(-invHalfShadowAtlasWidth, invHalfShadowAtlasHeight, 0.0f, 0.0f),
                        new Vector4(invHalfShadowAtlasWidth, invHalfShadowAtlasHeight, 0.0f, 0.0f) };
                    cmd.SetGlobalVectorArray(MainLight8CascadeShadowConstantBuffer._ShadowOffsetArray, mainLightShadowOffsetArray);
                }

                // Currently only used when !SHADER_API_MOBILE but risky to not set them as it's generic
                // enough so custom shaders might use it.
                cmd.SetGlobalVector(MainLight8CascadeShadowConstantBuffer._ShadowmapSize, new Vector4(invShadowAtlasWidth,
                    invShadowAtlasHeight,
                    m_ShadowmapWidth, m_ShadowmapHeight));
            }
        }
    }
}