using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Focusing modes for the depth of field effect.
    /// </summary>
    public enum DepthOfFieldMode
    {
        /// <summary>
        /// Disables depth of field.
        /// </summary>
        Off,

        /// <summary>
        /// Use this for faster but non physical depth of field.
        /// </summary>
        Gaussian, // Non physical, fast, small radius, far blur only

        /// <summary>
        /// Use this for a more realistic but slower depth of field.
        /// </summary>
        Bokeh
    }

    /// <summary>
    /// A volume component that holds settings for the Depth Of Field effect.
    /// </summary>
    [Serializable, VolumeComponentMenu("Post-processing/Depth Of Field")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("post-processing-depth-of-field")]
    public sealed class DepthOfField : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Use this to select Focusing modes for the depth of field effect.
        /// </summary>
        [Tooltip("Use \"Gaussian\" for a faster but non physical depth of field; \"Bokeh\" for a more realistic but slower depth of field.")]
        public DepthOfFieldModeParameter mode = new DepthOfFieldModeParameter(DepthOfFieldMode.Off);

        /// <summary>
        /// The distance at which the blurring will start.
        /// </summary>
        [Tooltip("The distance at which the blurring will start.")]
        public MinFloatParameter gaussianStart = new MinFloatParameter(10f, 0f);

        /// <summary>
        /// The distance at which the blurring will reach its maximum radius.
        /// </summary>
        [Tooltip("The distance at which the blurring will reach its maximum radius.")]
        public MinFloatParameter gaussianEnd = new MinFloatParameter(30f, 0f);

        /// <summary>
        /// The maximum radius of the gaussian blur. Values above 1 may show under-sampling artifacts.
        /// </summary>
        [Tooltip("The maximum radius of the gaussian blur. Values above 1 may show under-sampling artifacts.")]
        public ClampedFloatParameter gaussianMaxRadius = new ClampedFloatParameter(1f, 0.5f, 1.5f);

        /// <summary>
        /// Use higher quality sampling to reduce flickering and improve the overall blur smoothness.
        /// </summary>
        [Tooltip("Use higher quality sampling to reduce flickering and improve the overall blur smoothness.")]
        public BoolParameter highQualitySampling = new BoolParameter(false);

        /// <summary>
        /// The distance to the point of focus.
        /// </summary>
        [Tooltip("The distance to the point of focus.")]
        public MinFloatParameter focusDistance = new MinFloatParameter(10f, 0.1f);

        /// <summary>
        /// The ratio of aperture (known as f-stop or f-number). The smaller the value is, the shallower the depth of field is.
        /// </summary>
        [Tooltip("The ratio of aperture (known as f-stop or f-number). The smaller the value is, the shallower the depth of field is.")]
        public ClampedFloatParameter aperture = new ClampedFloatParameter(5.6f, 1f, 32f);

        /// <summary>
        /// The distance between the lens and the film. The larger the value is, the shallower the depth of field is.
        /// </summary>
        [Tooltip("The distance between the lens and the film. The larger the value is, the shallower the depth of field is.")]
        public ClampedFloatParameter focalLength = new ClampedFloatParameter(50f, 1f, 300f);

        /// <summary>
        /// The number of aperture blades.
        /// </summary>
        [Tooltip("The number of aperture blades.")]
        public ClampedIntParameter bladeCount = new ClampedIntParameter(5, 3, 9);

        /// <summary>
        /// The curvature of aperture blades. The smaller the value is, the more visible aperture blades are. A value of 1 will make the bokeh perfectly circular.
        /// </summary>
        [Tooltip("The curvature of aperture blades. The smaller the value is, the more visible aperture blades are. A value of 1 will make the bokeh perfectly circular.")]
        public ClampedFloatParameter bladeCurvature = new ClampedFloatParameter(1f, 0f, 1f);

        /// <summary>
        /// The rotation of aperture blades in degrees.
        /// </summary>
        [Tooltip("The rotation of aperture blades in degrees.")]
        public ClampedFloatParameter bladeRotation = new ClampedFloatParameter(0f, -180f, 180f);

        [Tooltip("Volume Lens uses this override's lens settings. Physical Camera uses the camera lens and sensor. Manual Ranges provides separate artistic near and far focus ranges.")]
        public DepthOfFieldFocusModeParameter focusMode = new DepthOfFieldFocusModeParameter(DepthOfFieldFocusMode.VolumeLens);

        [Tooltip("Read focus distance from the physical camera instead of this Volume.")]
        public BoolParameter useCameraFocusDistance = new BoolParameter(false);

        [Tooltip("Sensor height in millimeters when using Volume Lens.")]
        public ClampedFloatParameter sensorHeight = new ClampedFloatParameter(24f, 1f, 70f);

        [Tooltip("Near blur is at its maximum before this distance.")]
        public MinFloatParameter nearFocusStart = new MinFloatParameter(0f, 0f);

        [Tooltip("Near blur reaches zero at this distance.")]
        public MinFloatParameter nearFocusEnd = new MinFloatParameter(4f, 0f);

        [Tooltip("Far blur starts at this distance.")]
        public MinFloatParameter farFocusStart = new MinFloatParameter(10f, 0f);

        [Tooltip("Far blur reaches its maximum at this distance.")]
        public MinFloatParameter farFocusEnd = new MinFloatParameter(30f, 0f);

        [Tooltip("Maximum foreground blur radius in pixels at 1080p. Scales with render resolution.")]
        public ClampedFloatParameter nearMaxBlur = new ClampedFloatParameter(20f, 0f, 256f);

        [Tooltip("Maximum background blur radius in pixels at 1080p. Scales with render resolution.")]
        public ClampedFloatParameter farMaxBlur = new ClampedFloatParameter(16f, 0f, 256f);

        [Tooltip("Foreground aperture grid budget at the reference resolution. The square root sets grid width or ring count; the selected algorithm scales it with resolution.")]
        public ClampedIntParameter nearSampleCount = new ClampedIntParameter(64, 9, 256);

        [Tooltip("Background aperture grid budget at the reference resolution. The square root sets grid width or ring count; the selected algorithm scales it with resolution.")]
        public ClampedIntParameter farSampleCount = new ClampedIntParameter(64, 9, 256);

        [Tooltip("Full resolution preserves fine silhouettes and bright bokeh. Half resolution reduces the blur cost.")]
        public DepthOfFieldResolutionParameter resolution = new DepthOfFieldResolutionParameter(DepthOfFieldResolution.Half);

        [Tooltip("Display signed circle of confusion: blue foreground, black focus, red background.")]
        public BoolParameter debugCircleOfConfusion = new BoolParameter(false);

        [Tooltip("Select a quality preset or use the individual sampling controls.")]
        public DepthOfFieldQualityParameter quality = new DepthOfFieldQualityParameter(DepthOfFieldQuality.Custom);

        [Tooltip("Use ordered aperture rings, visibility accumulation, and adaptive tile processing.")]
        public BoolParameter physicallyBased = new BoolParameter(true);

        [Tooltip("Use a multi-tap prefilter and bicubic reconstruction for the layered algorithm.")]
        public BoolParameter highQualityFiltering = new BoolParameter(true);

        [Tooltip("Stabilize circle of confusion with motion vectors when temporal anti-aliasing or temporal upscaling is active.")]
        public BoolParameter coCStabilization = new BoolParameter(true);

        [Tooltip("Stability of the independent depth-of-field history in areas with changing luminance.")]
        public ClampedFloatParameter temporalAntiFlicker = new ClampedFloatParameter(.7f, 0f, 1f);

        [Tooltip("Reject depth-of-field history when motion speed changes. Requires camera temporal antialiasing.")]
        public ClampedFloatParameter temporalMotionRejection = new ClampedFloatParameter(.5f, 0f, 1f);

        [Tooltip("Sharpen the current depth-of-field sample before temporal accumulation.")]
        public ClampedFloatParameter temporalSharpening = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Allocate more aperture rings to mixed-focus tiles below 1, or to defocused tiles above 1.")]
        public ClampedFloatParameter adaptiveSamplingWeight = new ClampedFloatParameter(0.75f, 0.5f, 4f);

        [Tooltip("Limit foreground blending by depth in manual ranges with the independent near/far algorithm.")]
        public BoolParameter limitManualRangeNearBlur = new BoolParameter(false);

        [Tooltip("Stretch the aperture vertically for positive values or horizontally for negative values. Physical Camera reads this from the Camera.")]
        public ClampedFloatParameter anamorphism = new ClampedFloatParameter(0f, -1f, 1f);

        [Tooltip("Optical vignetting for the layered algorithm. Physical Camera reads this from the Camera. The physical algorithm does not apply barrel clipping.")]
        public ClampedFloatParameter barrelClipping = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Display in-focus, mixed-focus, and defocused tile classifications.")]
        public BoolParameter debugTileClassification = new BoolParameter(false);
        /// <inheritdoc/>
        public bool IsActive()
        {
            if (mode.value == DepthOfFieldMode.Off || SystemInfo.graphicsShaderLevel < 35)
                return false;

            if (mode.value == DepthOfFieldMode.Bokeh && (SystemInfo.graphicsShaderLevel < 45 || !SystemInfo.supportsComputeShaders))
                return false;

            return mode.value != DepthOfFieldMode.Gaussian || SystemInfo.supportedRenderTargetCount > 1;
        }

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => false;
    }

    /// <summary>Source of the focal plane and blur distribution.</summary>
    public enum DepthOfFieldFocusMode { VolumeLens, PhysicalCamera, ManualRanges }

    /// <summary>Resolution used for aperture sampling.</summary>
    public enum DepthOfFieldResolution { Full = 1, Half = 2, Quarter = 4 }

    public enum DepthOfFieldQuality { Custom, Low, Medium, High }

    [Serializable]
    public sealed class DepthOfFieldQualityParameter : VolumeParameter<DepthOfFieldQuality>
    {
        public DepthOfFieldQualityParameter(DepthOfFieldQuality value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>Volume parameter selecting the focus model.</summary>
    [Serializable]
    public sealed class DepthOfFieldFocusModeParameter : VolumeParameter<DepthOfFieldFocusMode>
    {
        public DepthOfFieldFocusModeParameter(DepthOfFieldFocusMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>Volume parameter selecting the aperture sampling resolution.</summary>
    [Serializable]
    public sealed class DepthOfFieldResolutionParameter : VolumeParameter<DepthOfFieldResolution>
    {
        public DepthOfFieldResolutionParameter(DepthOfFieldResolution value, bool overrideState = false) : base(value, overrideState) { }
    }
    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="DepthOfFieldMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class DepthOfFieldModeParameter : VolumeParameter<DepthOfFieldMode>
    {
        /// <summary>
        /// Creates a new <see cref="DepthOfFieldModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DepthOfFieldModeParameter(DepthOfFieldMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}
