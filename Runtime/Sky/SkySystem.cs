using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// SkySystem used for rendering sky in the main view.
    /// TODO: SkySystem also used for lighting the scene(ambient probe and sky reflection).
    /// </summary>
    internal class SkySystem : IDisposable
    {
        //Material m_StandardSkyboxMat;

        //SphericalHarmonicsL2 m_BlackAmbientProbe = new SphericalHarmonicsL2();

        //bool m_UpdateRequired = false;
        //bool m_StaticSkyUpdateRequired = false;
        int m_Resolution, m_LowResolution;

        // Sky used for static lighting. It will be used for ambient lighting if Ambient Mode is set to Static (even when realtime GI is enabled)
        // It will also be used for lightmap and light probe baking
        //SkyUpdateContext m_StaticLightingSky = new SkyUpdateContext();

        // This interpolation volume stack is used to interpolate the lighting override separately from the visual sky.
        // If a sky setting is present in this volume then it will be used for lighting override.
        public VolumeStack lightingOverrideVolumeStack { get; private set; }

        //public LayerMask lightingOverrideLayerMask { get; private set; } = -1;

        //static Dictionary<int, Type> m_SkyTypesDict = null;
        //public static Dictionary<int, Type> skyTypesDict { get { if (m_SkyTypesDict == null) UpdateSkyTypes(); return m_SkyTypesDict; } }

        //static Dictionary<int, Type> m_CloudTypesDict = null;
        //public static Dictionary<int, Type> cloudTypesDict { get { if (m_CloudTypesDict == null) UpdateCloudTypes(); return m_CloudTypesDict; } }

        // This list will hold the static lighting sky that should be used for baking ambient probe.
        // In practice we will always use the last one registered but we use a list to be able to roll back to the previous one once the user deletes the superfluous instances.
        //private static List<StaticLightingSky> m_StaticLightingSkies = new List<StaticLightingSky>();

        RTHandle m_BlackCubmapRT;
        GraphicsBuffer m_BlackAmbientProbeBuffer;
        GraphicsBuffer m_BlackAmbientRawProbeBuffer;

        // SkyUpdate
        private SkySettings m_SkySettings;
        private SkyRenderer m_SkyRenderer;
        private float m_LastUpdateTime = -1.0f;
        private int m_SkyParametersHash = -1;

        // SkyRendering
        private SphericalHarmonicsL2 m_AmbientProbe;
        private GraphicsBuffer m_AmbientProbeResult;
        private GraphicsBuffer m_DiffuseAmbientProbeBuffer;
        private RTHandle m_SkyboxCubemapRT;
        private RTHandle m_SkyboxBSDFCubemapRT;
        //private CubemapArray m_SkyboxBSDFCubemapArray;

        private Vector4 m_CubemapScreenSize, m_LowResCubemapScreenSize;
        private Matrix4x4[] m_FacePixelCoordToViewDirMatrices = new Matrix4x4[6];
        private Matrix4x4[] m_FacePixelCoordToViewDirMatricesLowRes = new Matrix4x4[6];
        private Matrix4x4[] m_CameraRelativeViewMatrices = new Matrix4x4[6];

        private ComputeShader m_ComputeAmbientProbeCS;
        private int m_ComputeAmbientProbeKernel;

        public bool ambientProbeIsReady = false;

        public SkyRenderer skyRenderer
        {
            get { return m_SkyRenderer; }
            set { m_SkyRenderer = value; }
        }
        public SkySettings skySettings
        {
            get { return m_SkySettings; }
            set 
            {
                // We cleanup the renderer first here because in some cases, after scene unload, the skySettings field will be "null" because the object got destroyed.
                // In this case, the renderer might stay allocated until a non null value is set. To avoid a lingering allocation, we cleanup first before anything else.
                // So next frame after scene unload, renderer will be freed.
                if (skyRenderer != null && (value == null || value.GetSkyRendererType() != skyRenderer.GetType()))
                {
                    skyRenderer.Cleanup();
                    skyRenderer = null;
                }

                //if (m_SkySettings == null)

                if (m_SkySettings == value)
                    return;

                m_SkyParametersHash = -1;
                m_SkySettings = value;
                m_LastUpdateTime = float.MinValue;// Last update should small enough for first update.

                if (m_SkySettings != null && skyRenderer == null)
                {
                    var rendererType = m_SkySettings.GetSkyRendererType();
                    skyRenderer = (SkyRenderer)Activator.CreateInstance(rendererType);
                    skyRenderer.Build();
                }
            }
        }

        static SkySystem m_Instance = null;

        internal static SkySystem instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new SkySystem();

                return m_Instance;
            }
        }

        SkySystem()
        {
        }

        internal SkySettings GetSkySettings(VolumeStack volumeStack)
        {
            var visualSky = volumeStack.GetComponent<VisualSky>();
            SkyType typeEnum = (SkyType)visualSky.skyType.value;
            SkySettings skySettings = null;
            Type skyType = null;
            switch (typeEnum)
            {
                case SkyType.ProceduralToon:
                    skyType = typeof(ProceduralToonSky);
                    break;
                case SkyType.Gradient:
                    skyType = typeof(GradientSky);
                    break;
                case SkyType.HDRI:
                default:
                    skyType = typeof(HDRISky);
                    break;
            }

            skySettings = (SkySettings)volumeStack.GetComponent(skyType);

            return skySettings;
        }

        internal void UpdateCurrentSky()
        {
            var volumeStack = VolumeManager.instance.stack;

            skySettings = GetSkySettings(volumeStack);

        }

        internal void SetGlobalSkyData(CommandBuffer cmd, SkySettings skySettings)
        {

        }

        /// <summary>
        /// Build when create Pipeline.
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="runtimeResources"></param>
        public void Build(UniversalRenderPipelineAsset asset, UniversalRenderPipelineRuntimeShaders runtimeShaders)
        {
            m_LowResolution = 16;
            m_Resolution = (int)asset.skyReflectionSize;

            //lightingOverrideVolumeStack = VolumeManager.instance.CreateStack();

            m_ComputeAmbientProbeCS = runtimeShaders.ambientProbeConvolutionCS;
            m_ComputeAmbientProbeKernel = m_ComputeAmbientProbeCS.FindKernel("AmbientProbeConvolutionDiffuse");

            m_CubemapScreenSize = new Vector4(m_Resolution, m_Resolution, 1.0f / m_Resolution, 1.0f / m_Resolution);
            m_LowResCubemapScreenSize = new Vector4(m_LowResolution, m_LowResolution, 1.0f / m_LowResolution, 1.0f / m_LowResolution);

            for (int i = 0; i < 6; ++i)
            {
                var lookAt = Matrix4x4.LookAt(Vector3.zero, CoreUtils.lookAtList[i], CoreUtils.upVectorList[i]);
                var worldToView = lookAt * Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)); // Need to scale -1.0 on Z to match what is being done in the camera.wolrdToCameraMatrix API. ...

                m_FacePixelCoordToViewDirMatrices[i] = RenderingUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(0.5f * Mathf.PI, Vector2.zero, m_CubemapScreenSize, worldToView, true);
                m_FacePixelCoordToViewDirMatricesLowRes[i] = RenderingUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(0.5f * Mathf.PI, Vector2.zero, m_LowResCubemapScreenSize, worldToView, true);
                m_CameraRelativeViewMatrices[i] = worldToView;
            }

            InitializeBlackCubemapArray(cubemapCount: 1);

            // Initialize black ambient probe buffer
            if (m_BlackAmbientProbeBuffer == null)
            {
                // 27 SH Coeffs in 7 float4
                m_BlackAmbientProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 7, 16);
                float[] blackValues = new float[28];
                for (int i = 0; i < 28; ++i)
                    blackValues[i] = 0.0f;
                m_BlackAmbientProbeBuffer.SetData(blackValues);
            }

        }

        internal RTHandle AllocSkyboxCubeMapRT(int resolution, string name)
        {
            return RTHandles.Alloc(resolution, resolution, 
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, 
                dimension: TextureDimension.Cube, 
                useMipMap: true, 
                autoGenerateMips: false, 
                filterMode: FilterMode.Trilinear, 
                name: name);
        }

        internal RTHandle AllocSkyboxBSDFCubemapRT(int resolution, string name)
        {
            return RTHandles.Alloc(resolution, resolution,
                 colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                 dimension: TextureDimension.Cube,
                 useMipMap: true,
                 autoGenerateMips: false,
                 filterMode: FilterMode.Trilinear,
                 name: name);
        }

        internal CubemapArray AllocSkyboxCubemapArray(int resolution, int bsdfCount)
        {
            return new CubemapArray(resolution, bsdfCount, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.MipChain)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0,
                name = "SkyboxCubemapConvolution"
            };
        }

        private void InitializeBlackCubemapArray(int cubemapCount = 1)
        {
            if (m_BlackCubmapRT == null || (m_BlackCubmapRT.rt == null && m_BlackCubmapRT.externalTexture == null))
            {
                m_BlackCubmapRT?.Release();
                m_BlackCubmapRT = RTHandles.Alloc(CoreUtils.blackCubeTexture);
            }
        }

        public bool IsValid()
        {
            // We need to check m_SkySettings because it can be "nulled" when destroying the volume containing the settings (as it's a ScriptableObject) without the context knowing about it.
            return m_SkySettings != null;
        }

        internal RTHandle GetSkyCubemap()
        {
            InitializeBlackCubemapArray();
            if (IsValid() && m_SkyboxCubemapRT != null)
            {
                return m_SkyboxCubemapRT;
            }
            else
            {
                return m_BlackCubmapRT;
            }
        }

        internal RTHandle GetReflectionTexture()
        {
            InitializeBlackCubemapArray();
            if (IsValid() && m_SkyboxBSDFCubemapRT != null)
            {
                return m_SkyboxBSDFCubemapRT;
            }
            else
            {
                return m_BlackCubmapRT;
            }
        }

        internal GraphicsBuffer GetDiffuseAmbientProbeBuffer()
        {
            if (IsValid() && m_DiffuseAmbientProbeBuffer != null && m_DiffuseAmbientProbeBuffer.IsValid())
            {
                return m_DiffuseAmbientProbeBuffer; 
            }

            return m_BlackAmbientProbeBuffer;
        }

        // Nine polynomial coefficients per colour channel, including the clamped-cosine band factors.
        internal GraphicsBuffer GetAmbientRawProbeBuffer()
        {
            if (IsValid() && m_AmbientProbeResult != null && m_AmbientProbeResult.IsValid())
                return m_AmbientProbeResult;

            if (m_BlackAmbientRawProbeBuffer == null || !m_BlackAmbientRawProbeBuffer.IsValid())
            {
                m_BlackAmbientRawProbeBuffer?.Release();
                m_BlackAmbientRawProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 27, sizeof(float))
                {
                    name = "BlackAmbientRawProbe"
                };
                m_BlackAmbientRawProbeBuffer.SetData(new float[27]);
            }
            return m_BlackAmbientRawProbeBuffer;
        }

        private class RenderSkyToCubemapPassData
        {
            internal SkyRenderer skyRenderer;
            internal SkySettings skySettings;
            internal Matrix4x4[] facePixelCoordToViewDirMatrices;
            internal TextureHandle cubemap;
        }

        internal void RenderSkyToCubemap(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle cubemap)
        {
            using (var builder = renderGraph.AddUnsafePass<RenderSkyToCubemapPassData>("RenderSkyToCubemap", out var passData, ProfilingSampler.Get(URPProfileId.RenderSkyToCubemap)))
            {
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                passData.facePixelCoordToViewDirMatrices = m_FacePixelCoordToViewDirMatrices;
                passData.skyRenderer = m_SkyRenderer;
                passData.skySettings = m_SkySettings;
                passData.cubemap = cubemap;

                builder.UseTexture(passData.cubemap, AccessFlags.Write);

                builder.SetRenderFunc((RenderSkyToCubemapPassData data, UnsafeGraphContext context) =>
                {
                    var basePassData = new SkyBasePassData();
                    basePassData.lightData = lightData;
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    for (int i = 0; i < 6; ++i)
                    {
                        basePassData.pixelCoordToViewDirMatrix = data.facePixelCoordToViewDirMatrices[i];
                        CoreUtils.SetRenderTarget(cmd, data.cubemap, ClearFlag.None, 0, (CubemapFace)i);
                        data.skyRenderer.RenderSky(cmd, basePassData, data.skySettings, renderForCubemap: true);
                    }
                });
            }
        }

        private class GenerateMipmapsPassData
        {
            public TextureHandle texture;
        }

        internal void GenerateMipmaps(RenderGraph renderGraph, TextureHandle texture)
        {
            using (var builder = renderGraph.AddUnsafePass<GenerateMipmapsPassData>("CubemapMipmaps", out var passData, ProfilingSampler.Get(URPProfileId.GenerateMipmaps)))
            {
                passData.texture = texture;

                builder.UseTexture(texture, AccessFlags.ReadWrite);

                builder.SetRenderFunc((GenerateMipmapsPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmd.GenerateMips(texture);
                });
            }
        }

        TextureHandle GenerateSkyCubemap(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_SkyboxCubemapRT == null)
                m_SkyboxCubemapRT = AllocSkyboxCubeMapRT(m_Resolution, "SkyboxCubemapRT");

            //RenderSkyToCubemap
            var outCubemap = renderGraph.ImportTexture(m_SkyboxCubemapRT);
            RenderSkyToCubemap(renderGraph, frameData, in outCubemap);

            //GenerateMipmaps
            GenerateMipmaps(renderGraph, outCubemap);

            return outCubemap;
        }

        class UpdateAmbientProbePassData
        {
            public ComputeShader computeAmbientProbeCS;
            public int computeAmbientProbeKernel;
            public TextureHandle skyCubemap;
            public BufferHandle ambientProbeResult;
            public BufferHandle diffuseAmbientProbeResult;
            public BufferHandle scratchBuffer;
            public Action<AsyncGPUReadbackRequest> callback;
        }

        internal void UpdateAmbientProbe(RenderGraph renderGraph, TextureHandle skyCubemap, Action<AsyncGPUReadbackRequest> callback)
        {
            // Compute buffer storing the resulting SH from diffuse convolution. L2 SH => 9 float per component.
            if (m_AmbientProbeResult == null)
                m_AmbientProbeResult = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 27, 4) { name = "ambientProbeResult" };
            // Compute buffer storing the diffuse convolution SH For diffuse ambient lighting. L2 SH => 9 float per component.
            if (m_DiffuseAmbientProbeBuffer == null)
                m_DiffuseAmbientProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 7, 16) { name = "diffuseAmbientProbeBuffer" };

            using (var builder = renderGraph.AddUnsafePass<UpdateAmbientProbePassData>("UpdateAmbientProbe", out var passData, ProfilingSampler.Get(URPProfileId.UpdateSkyAmbientProbe)))
            {
                passData.computeAmbientProbeCS = m_ComputeAmbientProbeCS;
                passData.computeAmbientProbeKernel = m_ComputeAmbientProbeKernel;
                passData.skyCubemap = skyCubemap;
                passData.ambientProbeResult = renderGraph.ImportBuffer(m_AmbientProbeResult);
                passData.diffuseAmbientProbeResult = renderGraph.ImportBuffer(m_DiffuseAmbientProbeBuffer);
                passData.scratchBuffer = renderGraph.CreateBuffer(new BufferDesc(27, sizeof(uint))); // L2 = 9 channel per component
                passData.callback = callback;

                builder.UseTexture(passData.skyCubemap, AccessFlags.Read);
                builder.UseBuffer(passData.ambientProbeResult, AccessFlags.Write);
                builder.UseBuffer(passData.diffuseAmbientProbeResult, AccessFlags.Write);
                builder.UseBuffer(passData.scratchBuffer, AccessFlags.Write);

                builder.SetRenderFunc((UpdateAmbientProbePassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    cmd.SetComputeBufferParam(data.computeAmbientProbeCS, data.computeAmbientProbeKernel, ShaderConstants._AmbientProbeOutputBufferParam, data.ambientProbeResult);
                    cmd.SetComputeBufferParam(data.computeAmbientProbeCS, data.computeAmbientProbeKernel, ShaderConstants._ScratchBufferParam, data.scratchBuffer);
                    cmd.SetComputeTextureParam(data.computeAmbientProbeCS, data.computeAmbientProbeKernel, ShaderConstants._AmbientProbeInputCubemap, data.skyCubemap);
                    cmd.SetComputeBufferParam(data.computeAmbientProbeCS, data.computeAmbientProbeKernel, ShaderConstants._DiffuseAmbientProbeOutputBufferParam, data.diffuseAmbientProbeResult);

                    Hammersley.BindConstants(cmd, data.computeAmbientProbeCS);

                    cmd.DispatchCompute(data.computeAmbientProbeCS, data.computeAmbientProbeKernel, 1, 1, 1);

                    // Current no need to readback.
                    //cmd.RequestAsyncReadback(data.ambientProbeResult, data.callback);
                });
            }
        }

        public void OnComputeAmbientProbeDone(AsyncGPUReadbackRequest request)
        {
            if (!request.hasError)
            {
                var result = request.GetData<float>();
                for (int channel = 0; channel < 3; ++channel)
                {
                    for (int coeff = 0; coeff < 9; ++coeff)
                    {
                        m_AmbientProbe[channel, coeff] = result[channel * 9 + coeff];
                    }
                }

                ambientProbeIsReady = true;
            }
        }

        class SkyEnvironmentConvolutionPassData
        {
            public TextureHandle input;
            //public TextureHandle intermediateTexture;
            public RTHandle output;
            //public IBLFilterBSDF[] bsdfs;
        }

        internal void RenderCubemapGGXConvolution(RenderGraph renderGraph, TextureHandle input)
        {
            if (m_SkyboxBSDFCubemapRT == null)
                m_SkyboxBSDFCubemapRT = AllocSkyboxBSDFCubemapRT(m_Resolution, "SkyboxCubemapConvolution");


            using (var builder = renderGraph.AddUnsafePass<SkyEnvironmentConvolutionPassData>("SkyEnvConvolution", out var passData, ProfilingSampler.Get(URPProfileId.UpdataSkyEnvConvolution)))
            {
                passData.input = input;
                passData.output = m_SkyboxBSDFCubemapRT;
                //passData.intermediateTexture = builder.CreateTransientTexture(new TextureDesc(m_Resolution, m_Resolution)
                //{ colorFormat = GraphicsFormat.R16G16B16A16_SFloat, dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear, name = "SkyboxBSDFIntermediate" });


                builder.UseTexture(passData.input, AccessFlags.Read);
                //builder.UseTexture(passData.intermediateTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((SkyEnvironmentConvolutionPassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    IBLFilterGGX.instance.FilterCubemap(cmd, data.input, passData.output);

                    //for (int i = 0; i < 6; ++i)
                    //{
                    //    cmd.CopyTexture(data.intermediateTexture, i, data.output, i);
                    //}
                });

            }
        }

        int GetSunLightHashCode(Light light)
        {
            unchecked
            {
                // Sun could influence the sky (like for procedural sky). We need to handle this possibility. If sun property change, then we need to update the sky
                int hash = 13;
                hash = hash * 23 + light.transform.position.GetHashCode();
                hash = hash * 23 + light.transform.rotation.GetHashCode();
                hash = hash * 23 + light.color.GetHashCode();
                hash = hash * 23 + light.colorTemperature.GetHashCode();
                hash = hash * 23 + light.intensity.GetHashCode();

                return hash;
            }
        }

        int ComputeSkyHash(Light sunLight, SkyAmbientMode ambientMode, bool staticSky = false)
        {
            int sunHash = 0;
            if (sunLight != null && skyRenderer.SupportDynamicSunLight)
            {
                sunHash = GetSunLightHashCode(sunLight);
            }

            // No check for camera

            int skyHash = sunHash * 23 + skySettings.GetHashCode();

            skyHash = skyHash * 23 + (staticSky ? 1 : 0);
            skyHash = skyHash * 23 + (ambientMode == SkyAmbientMode.Static ? 1 : 0);

            return skyHash;
        }

        private void UpdateEnvironment_Internal(
            RenderGraph renderGraph,
            ContextContainer frameData,
            UniversalLightData lightData,
            bool updateRequired,
            bool updateAmbientProbe,
            bool staticSky,
            SkyAmbientMode ambientMode)
        {
            if (IsValid())
            {
                Light sunLight = null;
                int mainLightIndex = lightData.mainLightIndex;
                if (mainLightIndex < 0)
                {
                    sunLight = null;
                }
                else
                {
                    sunLight = lightData.visibleLights[mainLightIndex].light;
                }

                var timeSinceLastUpdate = Time.realtimeSinceStartup - m_LastUpdateTime;

                // When update is not requested and the context is already valid (ie: already computed at least once),
                // we need to early out in two cases:
                // - updateMode is "OnDemand" in which case we never update unless explicitly requested
                // - updateMode is "Realtime" in which case we only update if the time threshold for realtime update is passed.
                // TODO: Check ambient and cubemap texture valid, exist: we need set it for shader, not exist: render.
                if (!updateRequired)
                {
                    if (skySettings.updateMode.value == EnvironmentUpdateMode.OnDemand)
                        return;
                    else if (skySettings.updateMode.value == EnvironmentUpdateMode.Realtime && timeSinceLastUpdate < skySettings.updatePeriod.value)
                        return;
                }

                int skyHash = ComputeSkyHash(sunLight, ambientMode, staticSky);
                bool forceUpdate = updateRequired;

                // TODO: need AcquireSkyRenderingContext, if the context was invalid or the hash has changed, this will request for an update.?
                //forceUpdate |= AcquireSkyRenderingContext;

                forceUpdate |= skyRenderer.DoUpdate(Time.frameCount);

                forceUpdate |= skySettings.updateMode.value == EnvironmentUpdateMode.OnChanged && skyHash != m_SkyParametersHash;
                forceUpdate |= skySettings.updateMode.value == EnvironmentUpdateMode.Realtime && timeSinceLastUpdate > skySettings.updatePeriod.value;


                if (forceUpdate)
                {
                    // Render sky to cubemap for indirect diffuse(SH, SphericalHarmonics) and specular(convolution cubemap)
                    TextureHandle skyCubemap = GenerateSkyCubemap(renderGraph, frameData);

                    // Compute ambient for shader SH use.
                    updateAmbientProbe = true;
                    if (updateAmbientProbe)
                    {
                        UpdateAmbientProbe(renderGraph, skyCubemap, OnComputeAmbientProbeDone);
                    }

                    // Render skyboxBSDFCubemapArray for shader use.
                    // TODO: HDRP only use staticSky, need we check other params?
                    bool supportsConvolution = !staticSky;
                    if (supportsConvolution)
                    {
                        RenderCubemapGGXConvolution(renderGraph, skyCubemap);
                    }

                    m_SkyParametersHash = skyHash;
                    m_LastUpdateTime = Time.realtimeSinceStartup;
                }

            }
            else
            {
                ambientProbeIsReady = false;
            }
        }

        /// <summary>
        /// SkySystem is global sky environment.
        /// This use skyHash or realTime to ensure update only once in one frame.
        /// </summary>
        internal void UpdateEnvironment(
            RenderGraph renderGraph, 
            ContextContainer frameData,
            UniversalLightData lightData, 
            bool updateRequired,
            bool updateAmbientProbe,
            bool staticSky, 
            SkyAmbientMode ambientMode)
        {
            UpdateEnvironment_Internal(renderGraph, frameData, lightData, updateRequired, updateAmbientProbe, staticSky, ambientMode);

            // Always inject to renderGraph
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            resourceData.skyAmbientProbe = renderGraph.ImportBuffer(GetDiffuseAmbientProbeBuffer(), bufferName: "skyAmbientProbe");
            resourceData.skyReflectionProbe = renderGraph.ImportTexture(GetReflectionTexture());
            resourceData.skyAmbientRawProbe = renderGraph.ImportBuffer(GetAmbientRawProbeBuffer(), bufferName: "skyAmbientRawProbe");
        }

        /// <summary>
        /// Call this in renderPass
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="renderingData"></param>
        internal void RenderSky(CommandBuffer cmd, UniversalCameraData cameraData, UniversalLightData lightData)
        {
            var camera = cameraData.camera;
            if (camera.clearFlags != CameraClearFlags.Skybox)
                return;

            if (IsValid())
            {
                m_SkyRenderer.DoUpdate(Time.frameCount);
                var basePassData = new SkyBasePassData();
                basePassData.pixelCoordToViewDirMatrix = cameraData.GetPixelCoordToViewDirWSMatrix();
                basePassData.lightData = lightData;

                m_SkyRenderer.RenderSky(cmd, basePassData, skySettings, renderForCubemap: false);
            }
        }



        internal void RenderOpaqueAtmosphericScattering(RasterCommandBuffer cmd, UniversalCameraData cameraData)
        {

        }

        public static void ClearAll()
        {
            if (m_Instance != null)
                m_Instance.Dispose();

            m_Instance = null;
        }

        public void Dispose()
        {
            if (skyRenderer != null)
                skyRenderer.Cleanup();
            skyRenderer = null;

            RTHandles.Release(m_BlackCubmapRT);
            RTHandles.Release(m_SkyboxCubemapRT);
            RTHandles.Release(m_SkyboxBSDFCubemapRT);

            if (m_BlackAmbientProbeBuffer != null)
                m_BlackAmbientProbeBuffer.Release();
            if (m_BlackAmbientRawProbeBuffer != null)
                m_BlackAmbientRawProbeBuffer.Release();
            if (m_AmbientProbeResult != null)
                m_AmbientProbeResult.Release();
            if (m_DiffuseAmbientProbeBuffer != null)
                m_DiffuseAmbientProbeBuffer.Release();

        }

        public static class ShaderConstants
        {
            public static readonly int _AmbientProbeOutputBufferParam = Shader.PropertyToID("_AmbientProbeOutputBuffer");
            public static readonly int _VolumetricAmbientProbeOutputBufferParam = Shader.PropertyToID("_VolumetricAmbientProbeOutputBuffer");
            public static readonly int _DiffuseAmbientProbeOutputBufferParam = Shader.PropertyToID("_DiffuseAmbientProbeOutputBuffer");
            public static readonly int _ScratchBufferParam = Shader.PropertyToID("_ScratchBuffer");
            public static readonly int _AmbientProbeInputCubemap = Shader.PropertyToID("_AmbientProbeInputCubemap");
            public static readonly int _FogParameters = Shader.PropertyToID("_FogParameters");
        }
    }
}
