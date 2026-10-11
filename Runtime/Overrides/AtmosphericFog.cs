using System;

namespace UnityEngine.Rendering.Universal
{
    [Flags] public enum FogDenoisingMode { None = 0, Reprojection = 1, Gaussian = 2, Both = 3 }
    public enum FogColorMode { Constant, Sky }
    public enum FogQualityControl { Manual, Balance }
    public enum FogQualityLevel { Low, Medium, High, Custom }
    [Serializable] public sealed class FogDenoisingModeParameter : VolumeParameter<FogDenoisingMode>
    { public FogDenoisingModeParameter(FogDenoisingMode value) : base(value) { } }
    [Serializable] public sealed class FogColorModeParameter : VolumeParameter<FogColorMode>
    { public FogColorModeParameter(FogColorMode value) : base(value) { } }
    [Serializable] public sealed class FogQualityControlParameter : VolumeParameter<FogQualityControl>
    { public FogQualityControlParameter(FogQualityControl value) : base(value) { } }
    [Serializable] public sealed class FogQualityLevelParameter : VolumeParameter<FogQualityLevel>
    { public FogQualityLevelParameter(FogQualityLevel value) : base(value) { } }
    public enum AtmosphericFogMode
    {
        Toon = 0,
        Physical = 1
    }
    [Serializable] public sealed class AtmosphericFogModeParameter : VolumeParameter<AtmosphericFogMode>
    {
        public AtmosphericFogModeParameter(AtmosphericFogMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Atmospheric Fog")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class AtmosphericFog : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Enable atmospheric fog. Volume overrides must also be enabled for the settings to affect the camera.")]
        public BoolParameter enabled = new BoolParameter(false);
        [Tooltip("Toon uses two height layers and directional color. Physical uses an attenuation distance and a single exponential atmosphere.")]
        public AtmosphericFogModeParameter mode = new AtmosphericFogModeParameter(AtmosphericFogMode.Toon);
        [Tooltip("Distance in meters over which the physical fog transmits 37% of the light at its base height.")]
        public MinFloatParameter meanFreePath = new MinFloatParameter(400, 1);
        [Tooltip("World-space altitude where physical fog density reaches 0.1% of its base density.")]
        public FloatParameter maximumHeight = new FloatParameter(50);
        [Tooltip("Extinction at the base height, in inverse meters.")]
        public MinFloatParameter density = new MinFloatParameter(0.01f, 0);
        [Tooltip("World-space Y where the global layer has the specified Density, in meters.")]
        public FloatParameter baseHeight = new FloatParameter(0);
        [Tooltip("Altitude increase that reduces the fog density by a factor of e.")]
        public MinFloatParameter heightScale = new MinFloatParameter(50, 0.1f);
        [Tooltip("Base color of analytic fog. With Volumetric enabled, also supplies ambient illumination scaled by Ambient Intensity; actual lights supply direct illumination.")]
        public ColorParameter color = new ColorParameter(new Color(0.65f, 0.72f, 0.8f), true, false, true);
        [Tooltip("Sample the sky radiance cubemap, or use a constant fog color.")]
        public FogColorModeParameter colorMode = new FogColorModeParameter(FogColorMode.Constant);
        public ColorParameter skyTint = new ColorParameter(Color.white, true, false, true);
        [Tooltip("Sky detail reached at long distances. Zero keeps the coarsest sky mip; one reaches full sky detail. Nearby fog uses the coarsest mip.")]
        public ClampedFloatParameter mipFogMaxMip = new ClampedFloatParameter(0.5f, 0, 1);
        public MinFloatParameter mipFogNear = new MinFloatParameter(0, 0);
        public MinFloatParameter mipFogFar = new MinFloatParameter(1000, 0);
        [Tooltip("Keep the medium constant below its base and exponential above it. Applies to both height layers.")]
        public BoolParameter constantDensityBelowBase = new BoolParameter(true);
        [Tooltip("Fog-free distance for the global height layer along each camera ray, in meters. Does not remove local fog regions.")]
        public MinFloatParameter startDistance = new MinFloatParameter(0, 0);
        [Tooltip("Maximum integration length along each camera ray, in meters. Height fog continues analytically beyond Volumetric Distance up to this limit.")]
        public MinFloatParameter maxDistance = new MinFloatParameter(1000, 1);
        [Tooltip("Upper limit on Toon fog opacity.")]
        public ClampedFloatParameter maxOpacity = new ClampedFloatParameter(1, 0, 1);
        [Tooltip("Apply fog to sky pixels as well as scene geometry.")]
        public BoolParameter affectSky = new BoolParameter(true);

        [Tooltip("Toon fog radiance added toward the main light. Alpha controls its strength. With volumetrics, this affects only the distant analytic fog.")]
        public ColorParameter sunColor = new ColorParameter(new Color(1, 0.85f, 0.65f), true, true, true);
        [Tooltip("Angular scattering bias for analytic fog and volumetric scene lights. Positive values favor looking toward the light, negative values favor the opposite direction.")]
        public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.65f, -1, 1);
        [Tooltip("Strength of the Toon sun tint. Volumetric lighting is controlled by scene lights.")]
        public MinFloatParameter scattering = new MinFloatParameter(0.5f, 0);

        [Tooltip("Extinction of the second height layer at Ground Height, in inverse meters. Zero disables this layer; the global layer and local regions remain independent.")]
        public MinFloatParameter groundDensity = new MinFloatParameter(0, 0);
        [Tooltip("World-space Y of the second Toon height layer, in meters.")]
        public FloatParameter groundHeight = new FloatParameter(0);
        [Tooltip("Altitude increase that reduces the ground layer density by a factor of e, in meters.")]
        public MinFloatParameter groundHeightScale = new MinFloatParameter(5, 0.1f);
        [Tooltip("Fog-free distance for the ground height layer along each camera ray, in meters.")]
        public MinFloatParameter groundStartDistance = new MinFloatParameter(0, 0);
        [Tooltip("Integrate scene lights, shadows and local fog regions in a 3D camera grid.")]
        public BoolParameter volumetric = new BoolParameter(false);
        [Tooltip("Scattering reflectance of the global medium. In Physical mode, this also applies to the distant continuation when volumetrics are enabled. Local regions use their own Albedo.")]
        public ColorParameter albedo = new ColorParameter(new Color(0.9f, 0.9f, 0.9f), false, false, true);
        [Tooltip("World-distance extent beyond the near plane. The volume grid is sphere-capped; analytic height fog continues beyond it.")]
        public MinFloatParameter volumetricDistance = new MinFloatParameter(128, 1);
        [Tooltip("Volume grid width and height are divided by this value. Smaller values improve screen-space detail at higher GPU and memory cost.")]
        public ClampedIntParameter resolutionDivisor = new ClampedIntParameter(8, 4, 16);
        [Tooltip("Number of depth intervals. More intervals resolve thinner density and light features. Toon stores an extra boundary; Physical stores each interval at its center.")]
        public ClampedIntParameter sliceCount = new ClampedIntParameter(64, 1, 512);
        [Tooltip("Maximum visible local regions per camera. Higher priorities survive the budget; equal priorities use stable instance order. Zero disables local regions.")]
        public MinIntParameter maxLocalFogVolumes = new MinIntParameter(512, 0);
        public FogQualityControlParameter qualityControl = new FogQualityControlParameter(FogQualityControl.Manual);
        [Tooltip("Low, Medium and High choose balanced screen/depth budgets. Custom uses the explicit controls below.")]
        public FogQualityLevelParameter qualityLevel = new FogQualityLevelParameter(FogQualityLevel.Custom);
        [Tooltip("Percentage of camera width and height used for the volume grid.")]
        public ClampedFloatParameter screenResolutionPercentage = new ClampedFloatParameter(12.5f, 6.25f, 50);
        public ClampedFloatParameter volumetricFogBudget = new ClampedFloatParameter(0.25f, 0, 1);
        [Tooltip("Allocate quality toward depth (one) or screen resolution (zero).")]
        public ClampedFloatParameter resolutionDepthRatio = new ClampedFloatParameter(0.5f, 0, 1);
        [Tooltip("Zero concentrates slices near the camera; one approaches uniform world-distance spacing.")]
        public ClampedFloatParameter sliceDistributionUniformity = new ClampedFloatParameter(0.75f, 0, 1);
        [Tooltip("Environment illumination from the sky's directional spherical harmonics, convolved with the scattering phase.")]
        public ClampedFloatParameter globalLightProbeDimmer = new ClampedFloatParameter(1, 0, 1);
        [Tooltip("Approximate multiple scattering with a density-dependent blur of scene radiance.")]
        public ClampedFloatParameter multipleScatteringIntensity = new ClampedFloatParameter(0, 0, 2);
        [Tooltip("Optional artistic ambient fill supplied by Color, added to the physical sky or probe environment.")]
        public MinFloatParameter ambientIntensity = new MinFloatParameter(0, 0);
        [Tooltip("Multiplier for scene lights inside the volume grid. Independent of the artistic Scattering tint strength.")]
        public MinFloatParameter lightIntensity = new MinFloatParameter(1, 0);
        [Tooltip("Limit volumetric scene lighting to directional lights. Otherwise point lights and spotlights also illuminate fog.")]
        public BoolParameter directionalOnly = new BoolParameter(false);
        [Tooltip("Sample available real-time shadows for the main directional light, point lights and spotlights inside the volume grid.")]
        public BoolParameter shadows = new BoolParameter(true);
        [Tooltip("Accumulate reprojected volumetric lighting over frames to reduce sampling noise.")]
        public BoolParameter temporalDenoising = new BoolParameter(true);
        [Tooltip("Reprojection accumulates phase-independent illumination; Gaussian filters neighboring rays. Both combines them.")]
        public FogDenoisingModeParameter denoisingMode = new FogDenoisingModeParameter(FogDenoisingMode.Both);
        [Tooltip("Toon history weight. Higher values converge more smoothly but respond more slowly to lighting changes. Physical uses a fixed seven-frame blend.")]
        public ClampedFloatParameter historyWeight = new ClampedFloatParameter(0.85f, 0, 0.95f);

        public bool IsActive() => enabled.value && (mode.value == AtmosphericFogMode.Physical || density.value > 0 || groundDensity.value > 0 || volumetric.value);
        public bool IsTileCompatible() => false;

        internal bool isPhysical => mode.value == AtmosphericFogMode.Physical;
        internal float extinction => isPhysical ? 1f / meanFreePath.value : density.value;
        internal float scaleHeight => isPhysical ? Mathf.Max(0.01f, maximumHeight.value - baseHeight.value) * 0.144765f : heightScale.value;

        internal FogDenoisingMode effectiveDenoising => denoisingMode.overrideState ? denoisingMode.value
            : temporalDenoising.value ? denoisingMode.value : FogDenoisingMode.None;

        internal void GetGridSize(int width, int height, out int x, out int y, out int z)
        {
            float fraction;
            FogQualityControl control = qualityControl.value;
            float budget = volumetricFogBudget.value, depthRatio = resolutionDepthRatio.value;
            switch (qualityLevel.value)
            {
                case FogQualityLevel.Low: control = FogQualityControl.Balance; budget = 0.166f; depthRatio = 0.666f; break;
                case FogQualityLevel.Medium: control = FogQualityControl.Balance; budget = 0.33f; depthRatio = 0.666f; break;
                case FogQualityLevel.High: control = FogQualityControl.Balance; budget = 0.666f; depthRatio = 0.5f; break;
            }
            if (control == FogQualityControl.Balance)
            {
                float maxResolution = Mathf.Lerp(50, 6.25f, depthRatio);
                fraction = Mathf.Lerp(6.25f, maxResolution, budget) * 0.01f;
                z = Mathf.Max(1, (int)Mathf.Lerp(1, Mathf.Max(1, depthRatio * 512), budget));
            }
            else
            {
                fraction = !screenResolutionPercentage.overrideState && resolutionDivisor.overrideState
                    ? 1f / resolutionDivisor.value : screenResolutionPercentage.value * 0.01f;
                z = sliceCount.value;
            }
            x = Mathf.Max(1, Mathf.CeilToInt(width * fraction));
            y = Mathf.Max(1, Mathf.CeilToInt(height * fraction));
            // A boundary sample closes the last integration interval.
            if (!isPhysical)
                ++z;
        }
    }
}
