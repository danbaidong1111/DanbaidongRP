using System;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A volume component that holds settings for the global illumination (screen space and ray traced).
    /// </summary>
    [Serializable, VolumeComponentMenu("Lighting/Screen Space Global Illumination")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed partial class GlobalIllumination : VolumeComponent, IPostProcessComponent
    {
        #region General
        /// <summary>
        /// Enable screen space global illumination.
        /// </summary>
        [Tooltip("Enable screen space global illumination.")]
        public BoolParameter enabled = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        /// <summary>
        /// Controls the casting technique used to evaluate the effect.
        /// </summary>
        [Tooltip("Controls the casting technique used to evaluate the effect. Ray marching uses a ray-marched screen-space solution, Ray tracing uses a hardware accelerated world-space solution. Mixed uses first Ray marching, then Ray tracing if it fails to intersect on-screen geometry.")]
        public RayCastingModeParameter tracing = new RayCastingModeParameter(RayCastingMode.RayMarching);

        /// <summary>
        /// Controls the fallback hierarchy for indirect diffuse in case the ray misses.
        /// </summary>
        [Tooltip("Controls the fallback hierarchy for indirect diffuse in case the ray misses.")]
        [FormerlySerializedAs("fallbackHierarchy")]
        [AdditionalProperty]
        public RayTracingFallbackHierachyParameter rayMiss = new RayTracingFallbackHierachyParameter(RayTracingFallbackHierachy.ReflectionProbesAndSky);
        #endregion

        #region RayMarching
        /// <summary>
        /// The thickness of the depth buffer value used for the ray marching step.
        /// </summary>
        [Tooltip("Controls the thickness of the depth buffer used for ray marching.")]
        public ClampedFloatParameter depthBufferThickness = new ClampedFloatParameter(0.1f, 0.0f, 0.5f);

        /// <summary>
        /// Defines if the screen space global illumination should be evaluated at full resolution.
        /// </summary>
        public BoolParameter fullResolutionSS = new BoolParameter(true);

        /// <summary>
        /// The number of steps that should be used during the ray marching pass.
        /// </summary>
        public int maxRaySteps
        {
            get { return m_MaxRaySteps.value; }
            set { m_MaxRaySteps.value = value; }
        }
        [SerializeField]
        [Tooltip("Controls the number of steps used for ray marching.")]
        private NoInterpClampedIntParameter m_MaxRaySteps = new NoInterpClampedIntParameter(32, 0, 128);

        /// <summary>
        /// Defines if the screen space global illumination should be denoised.
        /// </summary>
        public bool denoiseSS
        {
            get { return m_DenoiseSS.value; }
            set { m_DenoiseSS.value = value; }
        }
        [SerializeField, FormerlySerializedAs("denoise")]
        private BoolParameter m_DenoiseSS = new BoolParameter(true);

        /// <summary>
        /// Defines if the denoiser should be evaluated at half resolution.
        /// </summary>
        public bool halfResolutionDenoiserSS
        {
            get { return m_HalfResolutionDenoiserSS.value; }
            set { m_HalfResolutionDenoiserSS.value = value; }
        }
        [SerializeField]
        [Tooltip("Use a half resolution denoiser.")]
        private BoolParameter m_HalfResolutionDenoiserSS = new BoolParameter(false);

        /// <summary>
        /// Controls the radius of the global illumination denoiser (First Pass).
        /// </summary>
        public float denoiserRadiusSS
        {
            get { return m_DenoiserRadiusSS.value; }
            set { m_DenoiserRadiusSS.value = value; }
        }
        [SerializeField]
        [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
        private ClampedFloatParameter m_DenoiserRadiusSS = new ClampedFloatParameter(0.6f, 0.001f, 1.0f);

        /// <summary>
        /// Defines if the second denoising pass should be enabled.
        /// </summary>
        public bool secondDenoiserPassSS
        {
            get { return m_SecondDenoiserPassSS.value; }
            set { m_SecondDenoiserPassSS.value = value; }
        }
        [SerializeField]
        [Tooltip("Enable second denoising pass.")]
        private BoolParameter m_SecondDenoiserPassSS = new BoolParameter(true);
        #endregion

        #region RayTracing
        /// <summary>
        /// Controls the fallback hierarchy for lighting the last bounce.
        /// </summary>
        [Tooltip("Controls the fallback hierarchy for lighting the last bounce.")]
        [AdditionalProperty]
        public RayTracingFallbackHierachyParameter lastBounceFallbackHierarchy = new RayTracingFallbackHierachyParameter(RayTracingFallbackHierachy.ReflectionProbesAndSky);

        /// <summary>
        /// Controls the dimmer applied to the ambient and legacy light probes.
        /// </summary>
        [Tooltip("Controls the dimmer applied to the ambient and legacy light probes.")]
        [AdditionalProperty]
        public ClampedFloatParameter ambientProbeDimmer = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Defines the layers that GI should include.
        /// </summary>
        [Tooltip("Defines the layers that GI should include.")]
        public LayerMaskParameter layerMask = new LayerMaskParameter(-1);

        /// <summary>
        /// The LOD Bias that is added to texture sampling in the global illumination.
        /// </summary>
        [Tooltip("The LOD Bias applied to textures in the global illumination. A higher value increases performance and makes denoising easier, but it might reduce visual fidelity.")]
        public ClampedFloatParameter textureLodBias = new ClampedFloatParameter(7.0f, 0.0f, 7.0f);

        /// <summary>
        /// Controls the length of GI rays in meters.
        /// </summary>
        public float rayLength
        {
            get { return m_RayLength.value; }
            set { m_RayLength.value = value; }
        }
        [SerializeField, FormerlySerializedAs("rayLength")]
        private MinFloatParameter m_RayLength = new MinFloatParameter(50.0f, 0.01f);

        /// <summary>
        /// Controls the clamp of intensity.
        /// </summary>
        public float clampValue
        {
            get { return m_ClampValue.value; }
            set { m_ClampValue.value = value; }
        }
        [SerializeField, FormerlySerializedAs("clampValue")]
        [Tooltip("Controls the clamp of intensity.")]
        private MinFloatParameter m_ClampValue = new MinFloatParameter(100.0f, 0.001f);

        /// <summary>
        /// Controls which version of the effect should be used.
        /// </summary>
        [Tooltip("Controls which version of the effect should be used.")]
        public RayTracingModeParameter mode = new RayTracingModeParameter(RayTracingMode.Quality);

        /// <summary>
        /// Defines if the effect should be evaluated at full resolution.
        /// </summary>
        public bool fullResolution
        {
            get { return m_FullResolution.value; }
            set { m_FullResolution.value = value; }
        }
        [SerializeField, FormerlySerializedAs("fullResolution")]
        [Tooltip("Full Resolution")]
        private BoolParameter m_FullResolution = new BoolParameter(false);

        /// <summary>
        /// Number of samples for evaluating the effect.
        /// </summary>
        [Tooltip("Number of samples for GI.")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(1, 1, 8);

        /// <summary>
        /// Number of bounces for evaluating the effect.
        /// </summary>
        [Tooltip("Number of bounces for GI.")]
        public ClampedIntParameter bounceCount = new ClampedIntParameter(1, 1, 8);

        /// <summary>
        /// Defines if temporal denoising should be applied to ray traced global illumination.
        /// </summary>
        public bool temporalDenoise
        {
            get { return m_TemporalDenoise.value; }
            set { m_TemporalDenoise.value = value; }
        }
        [SerializeField, FormerlySerializedAs("m_Denoise"), FormerlySerializedAs("denoise")]
        [Tooltip("Enable temporal denoising for ray-traced GI.")]
        private BoolParameter m_TemporalDenoise = new BoolParameter(true);

        /// <summary>
        /// Controls the contribution of valid history during temporal denoising.
        /// </summary>
        public float temporalDenoiseAccumulation
        {
            get { return m_TemporalDenoiseAccumulation.value; }
            set { m_TemporalDenoiseAccumulation.value = value; }
        }
        [SerializeField]
        [Tooltip("Controls how much valid history contributes to temporal denoising. Higher values are more stable but react more slowly to lighting changes.")]
        private ClampedFloatParameter m_TemporalDenoiseAccumulation = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);

        /// <summary>
        /// Defines if the denoiser should be evaluated at half resolution.
        /// </summary>
        public bool halfResolutionDenoiser
        {
            get { return m_HalfResolutionDenoiser.value; }
            set { m_HalfResolutionDenoiser.value = value; }
        }
        [SerializeField, FormerlySerializedAs("halfResolutionDenoiser")]
        [Tooltip("Use a half resolution denoiser.")]
        private BoolParameter m_HalfResolutionDenoiser = new BoolParameter(false);

        /// <summary>
        /// Controls the radius of the global illumination denoiser (First Pass).
        /// </summary>
        public float denoiserRadius
        {
            get { return m_DenoiserRadius.value; }
            set { m_DenoiserRadius.value = value; }
        }
        [SerializeField, FormerlySerializedAs("denoiserRadius")]
        [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
        private ClampedFloatParameter m_DenoiserRadius = new ClampedFloatParameter(0.6f, 0.001f, 1.0f);

        /// <summary>
        /// Defines if spatial A-Trous denoising should be applied to ray traced global illumination.
        /// </summary>
        public bool spatialDenoise
        {
            get { return m_SpatialDenoise.value; }
            set { m_SpatialDenoise.value = value; }
        }
        [SerializeField, FormerlySerializedAs("m_SecondDenoiserPass"), FormerlySerializedAs("secondDenoiserPass")]
        [Tooltip("Enable spatial A-Trous denoising for ray-traced GI.")]
        private BoolParameter m_SpatialDenoise = new BoolParameter(true);

        /// <summary>
        /// Controls the number of steps used for the mixed tracing.
        /// </summary>
        public int maxMixedRaySteps
        {
            get { return m_MaxMixedRaySteps.value; }
            set { m_MaxMixedRaySteps.value = value; }
        }
        [SerializeField]
        [Tooltip("Controls the number of steps used for mixed tracing.")]
        private MinIntParameter m_MaxMixedRaySteps = new MinIntParameter(48, 0);

        /// <summary>
        /// When enabled, global illumination generated by moving objects will not be accumulated, generating less ghosting but introducing additional noise.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("When enabled, global illumination generated by moving objects will not be accumulated, generating less ghosting but introducing additional noise.")]
        public BoolParameter receiverMotionRejection = new BoolParameter(true);

        /// <summary>Runs ReSTIR GI at half width and height using fixed receivers.</summary>
        [Tooltip("Use one fixed receiver per 2x2 source pixels, then reconstruct the full-resolution result using depth and normals.")]
        public BoolParameter restirHalfResolution = new BoolParameter(true);

        /// <summary>Enables temporal reservoir reuse for ReSTIR GI.</summary>
        [Tooltip("Reuse the previous frame reservoir after motion-vector reprojection and geometric validation.")]
        public BoolParameter restirTemporalReuse = new BoolParameter(true);

        /// <summary>Enables spatial reservoir reuse for ReSTIR GI.</summary>
        [Tooltip("Reuse reservoirs from nearby pixels after geometric validation.")]
        public BoolParameter restirSpatialReuse = new BoolParameter(true);

        /// <summary>Number of neighboring reservoirs at full resolution.</summary>
        [Tooltip("Full-resolution spatial neighbor attempts. The RTXDI GI default is 2.")]
        public ClampedIntParameter restirSpatialSamples = new ClampedIntParameter(1, 0, 16);

        /// <summary>Full-resolution spatial reuse radius in source pixels.</summary>
        [Tooltip("Full-resolution spatial search radius in source pixels. The RTXDI GI default is 32.")]
        public ClampedFloatParameter restirSpatialRadius = new ClampedFloatParameter(16.0f, 1.0f, 64.0f);

        /// <summary>Number of neighboring reservoirs at half resolution.</summary>
        [Tooltip("Half-resolution spatial neighbor attempts.")]
        public ClampedIntParameter restirHalfSpatialSamples = new ClampedIntParameter(2, 0, 16);

        /// <summary>Half-resolution spatial search radius in source pixels.</summary>
        [Tooltip("Half-resolution spatial search radius in source pixels.")]
        public ClampedFloatParameter restirHalfSpatialRadius = new ClampedFloatParameter(32.0f, 1.0f, 64.0f);

        /// <summary>Maximum effective temporal reservoir length in frames.</summary>
        [Tooltip("Limits history in frame-equivalent samples. The candidate budget is this value multiplied by Sample Count; sample age is limited separately.")]
        public ClampedIntParameter restirMaxHistoryLength = new ClampedIntParameter(20, 1, 64);

        /// <summary>Maximum reservoir age accepted by temporal or spatial reuse.</summary>
        [Tooltip("Maximum number of frames a reused reservoir may survive.")]
        public ClampedIntParameter restirMaxSampleAge = new ClampedIntParameter(20, 1, 64);

        /// <summary>Maximum accepted surface-normal difference for reuse.</summary>
        [Tooltip("Minimum normal dot product required to reuse a reservoir.")]
        public ClampedFloatParameter restirNormalThreshold = new ClampedFloatParameter(0.85f, 0.0f, 1.0f);

        /// <summary>Relative depth/position threshold for reuse.</summary>
        [Tooltip("Relative world-space distance threshold used for temporal and spatial surface validation.")]
        public ClampedFloatParameter restirDepthThreshold = new ClampedFloatParameter(0.1f, 0.001f, 1.0f);

        /// <summary>Apply the old implementation's temporal Jacobian correction.</summary>
        [Tooltip("Apply the geometry Jacobian when a temporal sample is moved to the current receiver.")]
        public BoolParameter restirTemporalJacobian = new BoolParameter(false);

        /// <summary>Apply the geometry Jacobian during spatial reservoir reuse.</summary>
        [Tooltip("Apply the geometry Jacobian when a neighboring reservoir sample is moved to the current receiver. Disabling this can be more stable but introduces additional bias.")]
        public BoolParameter restirSpatialJacobian = new BoolParameter(false);

        /// <summary>Multiplier applied to resolved indirect diffuse lighting.</summary>
        [Tooltip("Scales the resolved ReSTIR GI contribution.")]
        public MinFloatParameter restirIntensity = new MinFloatParameter(1.0f, 0.0f);
        #endregion

        GlobalIllumination()
        {
            displayName = "Screen Space Global Illumination";
        }

        internal static bool RayTracingActive(GlobalIllumination volume)
        {
            return volume.tracing.value != RayCastingMode.RayMarching;
        }

        /// <inheritdoc/>
        public bool IsActive() => enabled.value;

        /// <inheritdoc/>
        public bool IsTileCompatible() => false;
    }
}
