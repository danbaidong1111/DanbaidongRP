using System;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    public enum ExposureMode { Off, Fixed, Automatic, AutomaticHistogram, CurveMapping, UsePhysicalCamera }
    public enum ExposureMeteringMode { Average, CenterWeighted, Spot, MaskWeighted, ProceduralMask }
    public enum ExposureAdaptationMode { Fixed, Progressive }
    public enum ExposureMidGrey { Grey125, Grey14, Grey18 }

    [Serializable]
    public sealed class ExposureModeParameter : VolumeParameter<ExposureMode>
    {
        public ExposureModeParameter(ExposureMode value, bool overrideState = false) : base(value, overrideState) { }
    }
    [Serializable]
    public sealed class ExposureMeteringModeParameter : VolumeParameter<ExposureMeteringMode>
    {
        public ExposureMeteringModeParameter(ExposureMeteringMode value, bool overrideState = false) : base(value, overrideState) { }
    }
    [Serializable]
    public sealed class ExposureAdaptationModeParameter : VolumeParameter<ExposureAdaptationMode>
    {
        public ExposureAdaptationModeParameter(ExposureAdaptationMode value, bool overrideState = false) : base(value, overrideState) { }
    }
    [Serializable]
    public sealed class ExposureMidGreyParameter : VolumeParameter<ExposureMidGrey>
    {
        public ExposureMidGreyParameter(ExposureMidGrey value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>Photographic exposure, metering, and eye adaptation before optical post-processing.</summary>
    [Serializable, VolumeComponentMenu("Post-processing/Exposure")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class Exposure : VolumeComponent, IPostProcessComponent, ISerializationCallbackReceiver
    {
        public ExposureModeParameter mode = new ExposureModeParameter(ExposureMode.Off);
        [Tooltip("Exposure value at ISO 100. Increasing EV100 by one halves image brightness.")]
        public FloatParameter fixedExposure = new FloatParameter(0f);
        [Tooltip("Exposure compensation in stops. Positive values brighten the image.")]
        public FloatParameter compensation = new FloatParameter(0f);
        public ExposureMeteringModeParameter metering = new ExposureMeteringModeParameter(ExposureMeteringMode.CenterWeighted);
        [Tooltip("Minimum metered exposure in EV100.")]
        public FloatParameter limitMin = new FloatParameter(-1f);
        [Tooltip("Maximum metered exposure in EV100.")]
        public FloatParameter limitMax = new FloatParameter(14f);
        public ExposureMidGreyParameter targetMidGrey = new ExposureMidGreyParameter(ExposureMidGrey.Grey125);
        [Tooltip("Discard this percentage of the darkest weighted samples.")]
        public ClampedFloatParameter histogramLowPercent = new ClampedFloatParameter(40f, 0f, 99f);
        [Tooltip("Discard weighted samples above this percentile.")]
        public ClampedFloatParameter histogramHighPercent = new ClampedFloatParameter(90f, 1f, 100f);
        public BoolParameter histogramUseCurveRemapping = new BoolParameter(false);
        public AnimationCurveParameter curveMap = new AnimationCurveParameter(AnimationCurve.Linear(-10, -10, 20, 20));
        public AnimationCurveParameter limitMinCurveMap = new AnimationCurveParameter(AnimationCurve.Linear(-10, -12, 20, 18));
        public AnimationCurveParameter limitMaxCurveMap = new AnimationCurveParameter(AnimationCurve.Linear(-10, -8, 20, 22));
        public ExposureAdaptationModeParameter adaptationMode = new ExposureAdaptationModeParameter(ExposureAdaptationMode.Progressive);
        [Tooltip("Adaptation speed when moving from a bright scene into a dark scene.")]
        public MinFloatParameter speedLightToDark = new MinFloatParameter(1f, 0f);
        [Tooltip("Adaptation speed when moving from a dark scene into a bright scene.")]
        public MinFloatParameter speedDarkToLight = new MinFloatParameter(3f, 0f);
        [Tooltip("The red channel supplies the screen-space metering weight.")]
        public Texture2DParameter weightTextureMask = new Texture2DParameter(null);
        [Tooltip("Offset the procedural mask center by the camera's exposure target viewport position.")]
        public BoolParameter centerAroundExposureTarget = new BoolParameter(false);
        public Vector2Parameter proceduralCenter = new Vector2Parameter(new Vector2(.5f, .5f));
        public Vector2Parameter proceduralRadii = new Vector2Parameter(new Vector2(.3f, .3f));
        public ClampedFloatParameter proceduralSoftness = new ClampedFloatParameter(.5f, .001f, 1f);
        [Tooltip("Reject procedural-mask samples below this luminance, expressed in EV100.")]
        public FloatParameter maskMinIntensity = new FloatParameter(-30f);
        [Tooltip("Reject procedural-mask samples above this luminance, expressed in EV100.")]
        public FloatParameter maskMaxIntensity = new FloatParameter(30f);

        [Serializable]
        sealed class LegacyGrey
        {
            public float m_Value;
            public bool m_OverrideState;
        }
        [SerializeField, HideInInspector, FormerlySerializedAs("targetGrey")]
        LegacyGrey m_LegacyGrey = new LegacyGrey();

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize()
        {
            if (m_LegacyGrey == null || m_LegacyGrey.m_Value <= 0) return;
            // Preserve authored brightness when loading profiles that stored relative exposure stops.
            float offset = Mathf.Log(1.2f, 2);
            fixedExposure.value = -fixedExposure.value - offset;
            float min = -limitMax.value - offset;
            limitMax.value = -limitMin.value - offset;
            limitMin.value = min;
            bool minOverride = limitMin.overrideState;
            limitMin.overrideState = limitMax.overrideState;
            limitMax.overrideState = minOverride;
            float speed = speedDarkToLight.value;
            speedDarkToLight.value = speedLightToDark.value / Mathf.Log(2);
            speedLightToDark.value = speed / Mathf.Log(2);
            bool speedOverride = speedDarkToLight.overrideState;
            speedDarkToLight.overrideState = speedLightToDark.overrideState;
            speedLightToDark.overrideState = speedOverride;
            if (mode.value == ExposureMode.Automatic)
            {
                mode.value = ExposureMode.AutomaticHistogram;
                targetMidGrey.value = ExposureMidGrey.Grey18;
                targetMidGrey.overrideState |= m_LegacyGrey.m_OverrideState;
                compensation.value += Mathf.Log(m_LegacyGrey.m_Value / .15f, 2);
                compensation.overrideState |= m_LegacyGrey.m_OverrideState;
            }
            m_LegacyGrey.m_Value = 0;
        }

        internal bool isAutomatic => mode.value == ExposureMode.Automatic || mode.value == ExposureMode.AutomaticHistogram || mode.value == ExposureMode.CurveMapping;
        internal bool usesCurve => mode.value == ExposureMode.CurveMapping || (mode.value == ExposureMode.AutomaticHistogram && histogramUseCurveRemapping.value);
        public bool IsActive() => mode.value != ExposureMode.Off;
        public bool IsTileCompatible() => false;
    }
}
