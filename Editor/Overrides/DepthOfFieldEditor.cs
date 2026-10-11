using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(DepthOfField))]
    sealed class DepthOfFieldEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode;

        SerializedDataParameter m_GaussianStart;
        SerializedDataParameter m_GaussianEnd;
        SerializedDataParameter m_GaussianMaxRadius;
        SerializedDataParameter m_HighQualitySampling;

        SerializedDataParameter m_FocusDistance;
        SerializedDataParameter m_FocalLength;
        SerializedDataParameter m_Aperture;
        SerializedDataParameter m_BladeCount;
        SerializedDataParameter m_BladeCurvature;
        SerializedDataParameter m_BladeRotation;
        SerializedDataParameter m_FocusMode, m_UseCameraFocusDistance, m_SensorHeight;
        SerializedDataParameter m_NearStart, m_NearEnd, m_FarStart, m_FarEnd;
        SerializedDataParameter m_NearRadius, m_FarRadius, m_NearSamples, m_FarSamples, m_Resolution, m_DebugCoC;
        SerializedDataParameter m_Quality, m_PhysicallyBased, m_HighQualityFiltering, m_CoCStabilization, m_AdaptiveSampling, m_LimitNear, m_Anamorphism, m_BarrelClipping, m_DebugTiles;
        SerializedDataParameter m_TemporalAntiFlicker, m_TemporalMotionRejection, m_TemporalSharpening;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<DepthOfField>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));
            m_GaussianStart = Unpack(o.Find(x => x.gaussianStart));
            m_GaussianEnd = Unpack(o.Find(x => x.gaussianEnd));
            m_GaussianMaxRadius = Unpack(o.Find(x => x.gaussianMaxRadius));
            m_HighQualitySampling = Unpack(o.Find(x => x.highQualitySampling));

            m_FocusDistance = Unpack(o.Find(x => x.focusDistance));
            m_FocalLength = Unpack(o.Find(x => x.focalLength));
            m_Aperture = Unpack(o.Find(x => x.aperture));
            m_BladeCount = Unpack(o.Find(x => x.bladeCount));
            m_BladeCurvature = Unpack(o.Find(x => x.bladeCurvature));
            m_BladeRotation = Unpack(o.Find(x => x.bladeRotation));
            m_FocusMode = Unpack(o.Find(x => x.focusMode));
            m_UseCameraFocusDistance = Unpack(o.Find(x => x.useCameraFocusDistance));
            m_SensorHeight = Unpack(o.Find(x => x.sensorHeight));
            m_NearStart = Unpack(o.Find(x => x.nearFocusStart));
            m_NearEnd = Unpack(o.Find(x => x.nearFocusEnd));
            m_FarStart = Unpack(o.Find(x => x.farFocusStart));
            m_FarEnd = Unpack(o.Find(x => x.farFocusEnd));
            m_NearRadius = Unpack(o.Find(x => x.nearMaxBlur));
            m_FarRadius = Unpack(o.Find(x => x.farMaxBlur));
            m_NearSamples = Unpack(o.Find(x => x.nearSampleCount));
            m_FarSamples = Unpack(o.Find(x => x.farSampleCount));
            m_Resolution = Unpack(o.Find(x => x.resolution));
            m_DebugCoC = Unpack(o.Find(x => x.debugCircleOfConfusion));
            m_Quality = Unpack(o.Find(x => x.quality));
            m_PhysicallyBased = Unpack(o.Find(x => x.physicallyBased));
            m_HighQualityFiltering = Unpack(o.Find(x => x.highQualityFiltering));
            m_CoCStabilization = Unpack(o.Find(x => x.coCStabilization));
            m_AdaptiveSampling = Unpack(o.Find(x => x.adaptiveSamplingWeight));
            m_LimitNear = Unpack(o.Find(x => x.limitManualRangeNearBlur));
            m_Anamorphism = Unpack(o.Find(x => x.anamorphism));
            m_BarrelClipping = Unpack(o.Find(x => x.barrelClipping));
            m_DebugTiles = Unpack(o.Find(x => x.debugTileClassification));
            m_TemporalAntiFlicker = Unpack(o.Find(x => x.temporalAntiFlicker));
            m_TemporalMotionRejection = Unpack(o.Find(x => x.temporalMotionRejection));
            m_TemporalSharpening = Unpack(o.Find(x => x.temporalSharpening));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);

            if (m_Mode.value.intValue == (int)DepthOfFieldMode.Gaussian)
            {
                PropertyField(m_GaussianStart, EditorGUIUtility.TrTextContent("Start"));
                PropertyField(m_GaussianEnd, EditorGUIUtility.TrTextContent("End"));
                PropertyField(m_GaussianMaxRadius, EditorGUIUtility.TrTextContent("Max Radius"));
                PropertyField(m_HighQualitySampling);
            }
            else if (m_Mode.value.intValue == (int)DepthOfFieldMode.Bokeh)
            {
                PropertyField(m_FocusMode);
                PropertyField(m_PhysicallyBased);
                if (m_PhysicallyBased.value.boolValue)
                    EditorGUILayout.HelpBox("Enable Temporal Anti-aliasing or temporal upscaling on the camera to reduce aperture sampling noise over frames. Without it, this mode uses a fixed single-frame sample pattern.", MessageType.Info);
                var focusMode = (DepthOfFieldFocusMode)m_FocusMode.value.intValue;
                if (focusMode == DepthOfFieldFocusMode.ManualRanges)
                {
                    PropertyField(m_NearStart);
                    PropertyField(m_NearEnd);
                    PropertyField(m_FarStart);
                    PropertyField(m_FarEnd);
                }
                else
                {
                    if (focusMode == DepthOfFieldFocusMode.PhysicalCamera)
                        PropertyField(m_UseCameraFocusDistance);
                    if (focusMode != DepthOfFieldFocusMode.PhysicalCamera || !m_UseCameraFocusDistance.value.boolValue)
                        PropertyField(m_FocusDistance);
                    if (focusMode == DepthOfFieldFocusMode.VolumeLens)
                    {
                        PropertyField(m_FocalLength);
                        PropertyField(m_Aperture);
                        PropertyField(m_SensorHeight);
                    }
                }
                if (focusMode != DepthOfFieldFocusMode.PhysicalCamera)
                {
                    PropertyField(m_BladeCount);
                    PropertyField(m_BladeCurvature);
                    PropertyField(m_Anamorphism);
                    if (!m_PhysicallyBased.value.boolValue) PropertyField(m_BarrelClipping);
                }
                PropertyField(m_BladeRotation);
                PropertyField(m_Quality);
                if (m_Quality.value.intValue == (int)DepthOfFieldQuality.Custom)
                {
                    PropertyField(m_NearRadius);
                    PropertyField(m_FarRadius);
                    PropertyField(m_NearSamples);
                    PropertyField(m_FarSamples);
                    PropertyField(m_Resolution);
                    if (!m_PhysicallyBased.value.boolValue) PropertyField(m_HighQualityFiltering);
                    if (m_PhysicallyBased.value.boolValue) PropertyField(m_AdaptiveSampling);
                }
                PropertyField(m_CoCStabilization);
                if (m_PhysicallyBased.value.boolValue)
                {
                    PropertyField(m_TemporalAntiFlicker);
                    PropertyField(m_TemporalMotionRejection);
                    PropertyField(m_TemporalSharpening);
                }
                if (!m_PhysicallyBased.value.boolValue && focusMode == DepthOfFieldFocusMode.ManualRanges) PropertyField(m_LimitNear);
                PropertyField(m_DebugCoC);
                if (m_PhysicallyBased.value.boolValue) PropertyField(m_DebugTiles);
            }
        }
    }
}
