using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(GlobalIllumination))]
    sealed class GlobalIlluminationEditor : VolumeComponentEditor
    {
        enum GIResolution
        {
            Full,
            Half
        }

        SerializedDataParameter m_Enable;
        SerializedDataParameter m_Tracing;
        SerializedDataParameter m_RayMiss;

        SerializedDataParameter m_FullResolutionSS;
        SerializedDataParameter m_DepthBufferThickness;
        SerializedDataParameter m_RaySteps;
        SerializedDataParameter m_RayLength;
        SerializedDataParameter m_SampleCount;
        SerializedDataParameter m_ClampValue;
        SerializedDataParameter m_TemporalDenoise;
        SerializedDataParameter m_TemporalDenoiseAccumulation;
        SerializedDataParameter m_SpatialDenoise;
        SerializedDataParameter m_HalfResolution;
        SerializedDataParameter m_TemporalReuse;
        SerializedDataParameter m_SpatialReuse;
        SerializedDataParameter m_SpatialSamples;
        SerializedDataParameter m_SpatialRadius;
        SerializedDataParameter m_HalfSpatialSamples;
        SerializedDataParameter m_HalfSpatialRadius;
        SerializedDataParameter m_MaxHistoryLength;
        SerializedDataParameter m_MaxSampleAge;
        SerializedDataParameter m_NormalThreshold;
        SerializedDataParameter m_DepthThreshold;
        SerializedDataParameter m_TemporalJacobian;
        SerializedDataParameter m_SpatialJacobian;
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_LayerMask;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<GlobalIllumination>(serializedObject);

            m_Enable = Unpack(o.Find(x => x.enabled));
            m_Tracing = Unpack(o.Find(x => x.tracing));
            m_RayMiss = Unpack(o.Find(x => x.rayMiss));

            m_FullResolutionSS = Unpack(o.Find(x => x.fullResolutionSS));
            m_DepthBufferThickness = Unpack(o.Find(x => x.depthBufferThickness));
            m_RaySteps = Unpack(o.Find(x => x.maxRaySteps));
            m_RayLength = Unpack(o.Find(x => x.rayLength));
            m_SampleCount = Unpack(o.Find(x => x.sampleCount));
            m_ClampValue = Unpack(o.Find(x => x.clampValue));
            m_TemporalDenoise = Unpack(o.Find(x => x.temporalDenoise));
            m_TemporalDenoiseAccumulation = Unpack(o.Find(x => x.temporalDenoiseAccumulation));
            m_SpatialDenoise = Unpack(o.Find(x => x.spatialDenoise));
            m_HalfResolution = Unpack(o.Find(x => x.restirHalfResolution));
            m_TemporalReuse = Unpack(o.Find(x => x.restirTemporalReuse));
            m_SpatialReuse = Unpack(o.Find(x => x.restirSpatialReuse));
            m_SpatialSamples = Unpack(o.Find(x => x.restirSpatialSamples));
            m_SpatialRadius = Unpack(o.Find(x => x.restirSpatialRadius));
            m_HalfSpatialSamples = Unpack(o.Find(x => x.restirHalfSpatialSamples));
            m_HalfSpatialRadius = Unpack(o.Find(x => x.restirHalfSpatialRadius));
            m_MaxHistoryLength = Unpack(o.Find(x => x.restirMaxHistoryLength));
            m_MaxSampleAge = Unpack(o.Find(x => x.restirMaxSampleAge));
            m_NormalThreshold = Unpack(o.Find(x => x.restirNormalThreshold));
            m_DepthThreshold = Unpack(o.Find(x => x.restirDepthThreshold));
            m_TemporalJacobian = Unpack(o.Find(x => x.restirTemporalJacobian));
            m_SpatialJacobian = Unpack(o.Find(x => x.restirSpatialJacobian));
            m_Intensity = Unpack(o.Find(x => x.restirIntensity));
            m_LayerMask = Unpack(o.Find(x => x.layerMask));

            base.OnEnable();
        }

        static public readonly GUIContent k_Enabled = EditorGUIUtility.TrTextContent("State", "Enable Screen Space Global Illumination.");
        static public readonly GUIContent k_TracingText = EditorGUIUtility.TrTextContent("Tracing", "Controls the technique used to compute the global illumination. Ray marching uses a ray-marched screen-space solution, Ray tracing uses a hardware accelerated world-space solution. Mixed uses first Ray marching, then Ray tracing if it fails to intersect on-screen geometry.");
        static public readonly GUIContent k_FullResolutionSSText = EditorGUIUtility.TrTextContent("Full Resolution", "Controls if the screen space global illumination should be evaluated at full resolution.");
        static public readonly GUIContent k_DepthBufferThicknessText = EditorGUIUtility.TrTextContent("Depth Tolerance", "Controls the tolerance when comparing the depth of two pixels.");
        static public readonly GUIContent k_RayStepsText = EditorGUIUtility.TrTextContent("Max Ray Steps", "Sets the maximum number of steps used for ray marching. Affects both correctness and performance.");
        static public readonly GUIContent k_RayMissFallbackHierarchyText = EditorGUIUtility.TrTextContent("Ray Miss", "Controls the fallback hierarchy for indirect diffuse in case the ray misses.");
        static readonly GUIContent k_ResolutionText = EditorGUIUtility.TrTextContent("Resolution", "Full runs at source resolution. Half uses one fixed receiver per 2x2 source pixels. Changing this selection resets sampling, reuse and denoising to the mode defaults, which can then be edited. Ray length, clamp, intensity and layers are preserved.");
        static readonly GUIContent k_SpatialSamplesText = EditorGUIUtility.TrTextContent("Neighbors", "Maximum spatial neighbor attempts for the selected resolution.");
        static readonly GUIContent k_SpatialRadiusText = EditorGUIUtility.TrTextContent("Radius", "Spatial search radius in source pixels for the selected resolution.");

        void DrawResolution()
        {
            // Keep the serialized bool so existing Volume profiles retain their
            // resolution setting; expose Full/Half instead of an on/off toggle.
            using (var scope = new OverridablePropertyScope(m_HalfResolution, k_ResolutionText, this))
            {
                if (!scope.displayed)
                    return;

                var resolution = m_HalfResolution.value.boolValue ? GIResolution.Half : GIResolution.Full;
                EditorGUI.BeginChangeCheck();
                resolution = (GIResolution)EditorGUILayout.EnumPopup(scope.label, resolution);
                if (EditorGUI.EndChangeCheck())
                {
                    m_HalfResolution.value.boolValue = resolution == GIResolution.Half;
                    ApplyResolutionPreset(m_HalfResolution.value.boolValue);
                }
            }
        }

        void ApplyResolutionPreset(bool halfResolution)
        {
            // Only an explicit dropdown change applies this preset. Inspector
            // repaint and the override checkbox never reset user tuning.
            SetPreset(m_SampleCount, halfResolution ? 2 : 1);
            SetPreset(m_TemporalReuse, true);
            SetPreset(m_MaxHistoryLength, 20);
            SetPreset(m_MaxSampleAge, 20);
            SetPreset(m_TemporalJacobian, false);
            SetPreset(m_SpatialReuse, true);
            SetPreset(halfResolution ? m_HalfSpatialSamples : m_SpatialSamples, halfResolution ? 2 : 1);
            SetPreset(halfResolution ? m_HalfSpatialRadius : m_SpatialRadius, halfResolution ? 32.0f : 16.0f);
            SetPreset(m_SpatialJacobian, false);
            SetPreset(m_NormalThreshold, 0.85f);
            SetPreset(m_DepthThreshold, 0.1f);
            SetPreset(m_TemporalDenoise, true);
            SetPreset(m_TemporalDenoiseAccumulation, 0.9f);
            SetPreset(m_SpatialDenoise, halfResolution);
        }

        static void SetPreset(SerializedDataParameter parameter, bool value)
        {
            parameter.value.boolValue = value;
            parameter.overrideState.boolValue = true;
        }

        static void SetPreset(SerializedDataParameter parameter, int value)
        {
            parameter.value.intValue = value;
            parameter.overrideState.boolValue = true;
        }

        static void SetPreset(SerializedDataParameter parameter, float value)
        {
            parameter.value.floatValue = value;
            parameter.overrideState.boolValue = true;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enable, k_Enabled);
            PropertyField(m_Tracing, k_TracingText);
            DrawResolution();

            bool rayTracing = m_Tracing.value.GetEnumValue<RayCastingMode>() != RayCastingMode.RayMarching;
            EditorGUILayout.LabelField("ReSTIR GI", EditorStyles.boldLabel);
            if (rayTracing)
            {
                PropertyField(m_LayerMask);
            }
            else
            {
                PropertyField(m_DepthBufferThickness, k_DepthBufferThicknessText);
                m_DepthBufferThickness.value.floatValue = Mathf.Clamp(m_DepthBufferThickness.value.floatValue, 0.001f, 0.5f);
                PropertyField(m_RaySteps, k_RayStepsText);
                PropertyField(m_RayMiss, k_RayMissFallbackHierarchyText);
                EditorGUILayout.HelpBox("Screen-space GI uses visible depth and previous scene color. Off-screen / invalid hits use sky when enabled; local reflection-probe fallback is not implemented.", MessageType.Info);
            }
            PropertyField(m_RayLength);
            PropertyField(m_SampleCount);
            m_SampleCount.value.intValue = Mathf.Clamp(m_SampleCount.value.intValue, 1, 8);
            PropertyField(m_ClampValue);
            PropertyField(m_Intensity);
            PropertyField(m_TemporalReuse);
            using (new IndentLevelScope())
            {
                PropertyField(m_MaxHistoryLength);
                PropertyField(m_MaxSampleAge);
                PropertyField(m_TemporalJacobian);
            }
            PropertyField(m_SpatialReuse);
            using (new IndentLevelScope())
            {
                bool halfResolution = m_HalfResolution.value.boolValue;
                PropertyField(halfResolution ? m_HalfSpatialSamples : m_SpatialSamples, k_SpatialSamplesText);
                PropertyField(halfResolution ? m_HalfSpatialRadius : m_SpatialRadius, k_SpatialRadiusText);
                PropertyField(m_SpatialJacobian);
            }
            PropertyField(m_NormalThreshold);
            PropertyField(m_DepthThreshold);
            PropertyField(m_TemporalDenoise);
            using (new IndentLevelScope())
            {
                PropertyField(m_TemporalDenoiseAccumulation);
            }
            PropertyField(m_SpatialDenoise);
        }
    }
}
