using System;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor.Rendering;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Class containing shader resources used in URP.
    /// </summary>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Runtime Shaders", Order = 1000), HideInInspector]
    public class UniversalRenderPipelineRuntimeShaders : IRenderPipelineResources
    {
        [SerializeField][HideInInspector] private int m_Version = 0;

        /// <summary>Version of the resource. </summary>
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Shaders/Utils/PlanarReflectionFiltering.compute")]
        ComputeShader m_PlanarReflectionFilteringCS;
        public ComputeShader planarReflectionFilteringCS
        {
            get => m_PlanarReflectionFilteringCS;
            set => this.SetValueAndNotify(ref m_PlanarReflectionFilteringCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Fog/AtmosphericFog.shader")]
        private Shader m_AtmosphericFogPS;
        public Shader atmosphericFogPS
        {
            get => m_AtmosphericFogPS;
            set => this.SetValueAndNotify(ref m_AtmosphericFogPS, value);
        }
        [SerializeField, ResourcePath("Shaders/VolumetricLighting/VolumetricLighting.compute")]
        private ComputeShader m_VolumetricFogCS;
        public ComputeShader volumetricFogCS
        {
            get => m_VolumetricFogCS;
            set => this.SetValueAndNotify(ref m_VolumetricFogCS, value);
        }

        [SerializeField, ResourcePath("Shaders/VolumetricLighting/VolumeVoxelization.compute")]
        private ComputeShader m_VolumeVoxelizationCS;
        public ComputeShader volumeVoxelizationCS
        {
            get => m_VolumeVoxelizationCS;
            set => this.SetValueAndNotify(ref m_VolumeVoxelizationCS, value);
        }

        [SerializeField, ResourcePath("Shaders/VolumetricLighting/VolumetricLightingFiltering.compute")]
        private ComputeShader m_VolumetricLightingFilteringCS;
        public ComputeShader volumetricLightingFilteringCS
        {
            get => m_VolumetricLightingFilteringCS;
            set => this.SetValueAndNotify(ref m_VolumetricLightingFilteringCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Utils/FallbackError.shader")]
        Shader m_FallbackErrorShader;

        /// <summary>
        /// Fallback error shader
        /// </summary>
        public Shader fallbackErrorShader
        {
            get => m_FallbackErrorShader;
            set => this.SetValueAndNotify(ref m_FallbackErrorShader, value, nameof(m_FallbackErrorShader));
        }


        [SerializeField]
        [ResourcePath("Shaders/Utils/BlitHDROverlay.shader")]
        internal Shader m_BlitHDROverlay;

        /// <summary>
        /// Blit HDR Overlay shader.
        /// </summary>
        public Shader blitHDROverlay
        {
            get => m_BlitHDROverlay;
            set => this.SetValueAndNotify(ref m_BlitHDROverlay, value, nameof(m_BlitHDROverlay));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlit.shader")]
        internal Shader m_CoreBlitPS;

        /// <summary>
        /// Core Blit shader.
        /// </summary>
        public Shader coreBlitPS
        {
            get => m_CoreBlitPS;
            set => this.SetValueAndNotify(ref m_CoreBlitPS, value, nameof(m_CoreBlitPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlitColorAndDepth.shader")]
        internal Shader m_CoreBlitColorAndDepthPS;

        /// <summary>
        /// Core Blit Color And Depth shader.
        /// </summary>
        public Shader coreBlitColorAndDepthPS
        {
            get => m_CoreBlitColorAndDepthPS;
            set => this.SetValueAndNotify(ref m_CoreBlitColorAndDepthPS, value, nameof(m_CoreBlitColorAndDepthPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/Sampling.shader")]
        private Shader m_SamplingPS;

        /// <summary>
        /// Sampling shader.
        /// </summary>
        public Shader samplingPS
        {
            get => m_SamplingPS;
            set => this.SetValueAndNotify(ref m_SamplingPS, value, nameof(m_SamplingPS));
        }


        [SerializeField, ResourcePath("Shaders/Utils/GPUCopy.compute")]
        private ComputeShader m_CopyChannelCS;

        /// <summary>
        /// GPUCopy compute shader.
        /// </summary>
        public ComputeShader copyChannelCS
        {
            get => m_CopyChannelCS;
            set => this.SetValueAndNotify(ref m_CopyChannelCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Utils/DepthPyramid.compute")]
        private ComputeShader m_DepthPyramidCS;

        /// <summary>
        /// DepthPyramid computeshader
        /// </summary>
        public ComputeShader depthPyramidCS
        {
            get => m_DepthPyramidCS;
            set => this.SetValueAndNotify(ref m_DepthPyramidCS, value);
        }


        [SerializeField, ResourcePath("Shaders/Utils/ColorPyramid.compute")]
        private ComputeShader m_ColorPyramidCS;

        /// <summary>
        /// ColorPyramid computeshader
        /// </summary>
        public ComputeShader colorPyramidCS
        {
            get => m_ColorPyramidCS;
            set => this.SetValueAndNotify(ref m_ColorPyramidCS, value);
        }


        /// <summary>
        /// PreIntegratedFGD
        /// </summary>
        [SerializeField, ResourcePath("Shaders/PreIntegratedFGD/PreIntegratedFGD_GGXDisneyDiffuse.shader")]
        private Shader m_PreIntegratedFGD_GGXDisneyDiffusePS;

        public Shader preIntegratedFGD_GGXDisneyDiffusePS
        {
            get => m_PreIntegratedFGD_GGXDisneyDiffusePS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_GGXDisneyDiffusePS, value);
        }

        [SerializeField, ResourcePath("Shaders/PreIntegratedFGD/PreIntegratedFGD_CharlieFabricLambert.shader")]
        private Shader m_PreIntegratedFGD_CharlieFabricLambertPS;

        public Shader preIntegratedFGD_CharlieFabricLambertPS
        {
            get => m_PreIntegratedFGD_CharlieFabricLambertPS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_CharlieFabricLambertPS, value);
        }


        /// <summary>
        /// GPU lights list compute shader.
        /// </summary>
        [SerializeField, ResourcePath("Shaders/Lights/GPULightsClearLists.compute")]
        private ComputeShader m_GpuLightsClearLists;

        public ComputeShader gpuLightsClearLists
        {
            get => m_GpuLightsClearLists;
            set => this.SetValueAndNotify(ref m_GpuLightsClearLists, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsCoarseCulling.compute")]
        private ComputeShader m_GpuLightsCoarseCullingCS;

        public ComputeShader gpuLightsCoarseCullingCS
        {
            get => m_GpuLightsCoarseCullingCS;
            set => this.SetValueAndNotify(ref m_GpuLightsCoarseCullingCS, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsFPTL.compute")]
        private ComputeShader m_GpuLightsFPTL;

        public ComputeShader gpuLightsFPTL
        {
            get => m_GpuLightsFPTL;
            set => this.SetValueAndNotify(ref m_GpuLightsFPTL, value);
        }

        [SerializeField, ResourcePath("Shaders/Lights/GPULightsCluster.compute")]
        private ComputeShader m_GpuLightsCluster;

        public ComputeShader gpuLightsCluster
        {
            get => m_GpuLightsCluster;
            set => this.SetValueAndNotify(ref m_GpuLightsCluster, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceReflections.compute")]
        private ComputeShader m_ScreenSpaceReflectionsCS;

        public ComputeShader screenSpaceReflectionsCS
        {
            get => m_ScreenSpaceReflectionsCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceReflectionsCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceXeGTAO.compute")]
        private ComputeShader m_ScreenSpaceXeGTAOCS;

        public ComputeShader screenSpaceXeGTAOCS
        {
            get => m_ScreenSpaceXeGTAOCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceXeGTAOCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceAmbientOcclusionDenoiser.compute")]
        private ComputeShader m_ScreenSpaceAODenoiserCS;

        public ComputeShader screenSpaceAODenoiserCS
        {
            get => m_ScreenSpaceAODenoiserCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceAODenoiserCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ReSTIRGlobalIlluminationDenoiser.compute")]
        private ComputeShader m_ReSTIRGlobalIlluminationDenoiserCS;

        public ComputeShader restirGlobalIlluminationDenoiserCS
        {
            get => m_ReSTIRGlobalIlluminationDenoiserCS;
            set => this.SetValueAndNotify(ref m_ReSTIRGlobalIlluminationDenoiserCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ReSTIRGlobalIllumination.compute")]
        private ComputeShader m_ReSTIRGlobalIlluminationCS;

        public ComputeShader restirGlobalIlluminationCS
        {
            get => m_ReSTIRGlobalIlluminationCS;
            set => this.SetValueAndNotify(ref m_ReSTIRGlobalIlluminationCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceDirectionalShadows.compute")]
        private ComputeShader m_ScreenSpaceDirectionalShadowsCS;

        public ComputeShader screenSpaceDirectionalShadowsCS
        {
            get => m_ScreenSpaceDirectionalShadowsCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceDirectionalShadowsCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceShadowDenoiser.compute")]
        private ComputeShader m_ScreenSpaceShadowDenoiserCS;

        public ComputeShader screenSpaceShadowDenoiserCS
        {
            get => m_ScreenSpaceShadowDenoiserCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceShadowDenoiserCS, value);
        }

        [SerializeField, ResourcePath("Shaders/ScreenSpaceLighting/ScreenSpaceShadowScatter.shader")]
        private Shader m_ScreenSpaceShadowScaterPS;

        public Shader screenSpaceShadowScaterPS
        {
            get => m_ScreenSpaceShadowScaterPS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceShadowScaterPS, value);
        }

        /// <summary>
        /// Deferred lighting compute shader.
        /// </summary>
        [SerializeField, ResourcePath("Shaders/Lighting/DeferredLighting.compute")]
        private ComputeShader m_DeferredLightingCS;

        public ComputeShader deferredLightingCS
        {
            get => m_DeferredLightingCS;
            set => this.SetValueAndNotify(ref m_DeferredLightingCS, value);
        }

        // SkyBox

        /// <summary>
        /// Sky.
        /// </summary>
        [SerializeField, ResourcePath("Shaders/Sky/HDRISky.shader")]
        private Shader m_HdriSkyPS;

        public Shader hdriSkyPS
        {
            get => m_HdriSkyPS;
            set => this.SetValueAndNotify(ref m_HdriSkyPS, value);
        }

        [SerializeField, ResourcePath("Shaders/Sky/GradientSky.shader")]
        private Shader m_GradientSkyPS;

        public Shader gradientSkyPS
        {
            get => m_GradientSkyPS;
            set => this.SetValueAndNotify(ref m_GradientSkyPS, value);
        }

        [SerializeField, ResourcePath("Shaders/Sky/AmbientProbeConvolution.compute")]
        private ComputeShader m_AmbientProbeConvolutionCS;

        public ComputeShader ambientProbeConvolutionCS
        {
            get => m_AmbientProbeConvolutionCS;
            set => this.SetValueAndNotify(ref m_AmbientProbeConvolutionCS, value);
        }

        /// <summary>
        /// GGX Convolution
        /// </summary>
        [SerializeField, ResourcePath("Shaders/IBLFilter/BuildProbabilityTables.compute")]
        private ComputeShader m_BuildProbabilityTablesCS;

        public ComputeShader buildProbabilityTablesCS
        {
            get => m_BuildProbabilityTablesCS;
            set => this.SetValueAndNotify(ref m_BuildProbabilityTablesCS, value);
        }

        [SerializeField, ResourcePath("Shaders/IBLFilter/ComputeGgxIblSampleData.compute")]
        private ComputeShader m_ComputeGgxIblSampleDataCS;

        public ComputeShader computeGgxIblSampleDataCS
        {
            get => m_ComputeGgxIblSampleDataCS;
            set => this.SetValueAndNotify(ref m_ComputeGgxIblSampleDataCS, value);
        }

        [SerializeField, ResourcePath("Shaders/IBLFilter/GGXConvolve.shader")]
        private Shader m_GGXConvolvePS;

        public Shader GGXConvolvePS
        {
            get => m_GGXConvolvePS;
            set => this.SetValueAndNotify(ref m_GGXConvolvePS, value);
        }

        [SerializeField, ResourcePath("Shaders/RayTracing/RayGeneration/RayTracingReflections.raytrace")]
        private RayTracingShader m_RayTracingReflections;

        public RayTracingShader rayTracingReflections
        {
            get => m_RayTracingReflections;
            set => this.SetValueAndNotify(ref m_RayTracingReflections, value);
        }

        [SerializeField, ResourcePath("Shaders/RayTracing/RayGeneration/RayTracingShadows.raytrace")]
        private RayTracingShader m_RayTracingShadows;

        public RayTracingShader rayTracingShadows
        {
            get => m_RayTracingShadows;
            set => this.SetValueAndNotify(ref m_RayTracingShadows, value);
        }

        [SerializeField, ResourcePath("Shaders/RayTracing/RayGeneration/RayTracingAmbientOcclusion.raytrace")]
        private RayTracingShader m_RayTracingAmbientOcclusion;

        public RayTracingShader rayTracingAmbientOcclusion
        {
            get => m_RayTracingAmbientOcclusion;
            set => this.SetValueAndNotify(ref m_RayTracingAmbientOcclusion, value);
        }

        [SerializeField, ResourcePath("Shaders/RayTracing/RayGeneration/ReSTIRGlobalIllumination.raytrace")]
        private RayTracingShader m_ReSTIRGlobalIllumination;

        public RayTracingShader restirGlobalIllumination
        {
            get => m_ReSTIRGlobalIllumination;
            set => this.SetValueAndNotify(ref m_ReSTIRGlobalIllumination, value);
        }

#if UNITY_EDITOR
        public void EnsureShadersCompiled()
        {
            void CheckComputeShaderMessages(ComputeShader computeShader)
            {
                foreach (var message in UnityEditor.ShaderUtil.GetComputeShaderMessages(computeShader))
                {
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        // Will be catched by the try in HDRenderPipelineAsset.CreatePipeline()
                        throw new System.Exception(System.String.Format(
                            "Compute Shader compilation error on platform {0} in file {1}:{2}: {3}{4}\n" +
                            "RenderPipeline will not run until the error is fixed.\n",
                            message.platform, message.file, message.line, message.message, message.messageDetails
                        ));
                    }
                }
            }

            // We iterate over all compute shader to verify if they are all compiled, if it's not the case then
            // we throw an exception to avoid allocating resources and crashing later on by using a null compute kernel.
            this.ForEachFieldOfType<ComputeShader>(CheckComputeShaderMessages, BindingFlags.Public | BindingFlags.Instance);
        }
#endif

    }
}
