using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(AtmosphericFog))]
    sealed class AtmosphericFogEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Enabled, m_Mode, m_Volumetric;
        SerializedDataParameter m_Density, m_BaseHeight, m_HeightScale, m_Color, m_StartDistance, m_MaxDistance, m_MaxOpacity, m_AffectSky;
        SerializedDataParameter m_SunColor, m_Anisotropy, m_Scattering;
        SerializedDataParameter m_GroundDensity, m_GroundHeight, m_GroundHeightScale, m_GroundStartDistance;
        SerializedDataParameter m_MeanFreePath, m_MaximumHeight;
        SerializedDataParameter m_Albedo, m_VolumetricDistance, m_ResolutionDivisor, m_SliceCount;
        SerializedDataParameter m_AmbientIntensity, m_LightIntensity, m_DirectionalOnly, m_Shadows, m_TemporalDenoising, m_HistoryWeight;
        SerializedDataParameter m_ColorMode, m_SkyTint, m_MipNear, m_MipFar, m_MipMax, m_ConstantBelowBase;
        SerializedDataParameter m_QualityLevel, m_QualityControl, m_ScreenPercentage, m_Budget, m_DepthRatio, m_SliceUniformity, m_Denoising, m_ProbeDimmer, m_MultipleScattering, m_MaxLocalVolumes;
        bool m_GroundExpanded;
        bool m_QualityExpanded = true;

        public override void OnEnable()
        {
            var properties = new PropertyFetcher<AtmosphericFog>(serializedObject);
            m_Enabled = Unpack(properties.Find(x => x.enabled));
            m_Mode = Unpack(properties.Find(x => x.mode));
            m_MeanFreePath = Unpack(properties.Find(x => x.meanFreePath));
            m_MaximumHeight = Unpack(properties.Find(x => x.maximumHeight));
            m_Volumetric = Unpack(properties.Find(x => x.volumetric));
            m_Density = Unpack(properties.Find(x => x.density));
            m_BaseHeight = Unpack(properties.Find(x => x.baseHeight));
            m_HeightScale = Unpack(properties.Find(x => x.heightScale));
            m_Color = Unpack(properties.Find(x => x.color));
            m_StartDistance = Unpack(properties.Find(x => x.startDistance));
            m_MaxDistance = Unpack(properties.Find(x => x.maxDistance));
            m_MaxOpacity = Unpack(properties.Find(x => x.maxOpacity));
            m_AffectSky = Unpack(properties.Find(x => x.affectSky));
            m_SunColor = Unpack(properties.Find(x => x.sunColor));
            m_Anisotropy = Unpack(properties.Find(x => x.anisotropy));
            m_Scattering = Unpack(properties.Find(x => x.scattering));
            m_GroundDensity = Unpack(properties.Find(x => x.groundDensity));
            m_GroundHeight = Unpack(properties.Find(x => x.groundHeight));
            m_GroundHeightScale = Unpack(properties.Find(x => x.groundHeightScale));
            m_GroundStartDistance = Unpack(properties.Find(x => x.groundStartDistance));
            m_Albedo = Unpack(properties.Find(x => x.albedo));
            m_VolumetricDistance = Unpack(properties.Find(x => x.volumetricDistance));
            m_ResolutionDivisor = Unpack(properties.Find(x => x.resolutionDivisor));
            m_SliceCount = Unpack(properties.Find(x => x.sliceCount));
            m_AmbientIntensity = Unpack(properties.Find(x => x.ambientIntensity));
            m_LightIntensity = Unpack(properties.Find(x => x.lightIntensity));
            m_DirectionalOnly = Unpack(properties.Find(x => x.directionalOnly));
            m_Shadows = Unpack(properties.Find(x => x.shadows));
            m_TemporalDenoising = Unpack(properties.Find(x => x.temporalDenoising));
            m_HistoryWeight = Unpack(properties.Find(x => x.historyWeight));
            m_ColorMode = Unpack(properties.Find(x => x.colorMode));
            m_SkyTint = Unpack(properties.Find(x => x.skyTint));
            m_MipNear = Unpack(properties.Find(x => x.mipFogNear));
            m_MipFar = Unpack(properties.Find(x => x.mipFogFar));
            m_MipMax = Unpack(properties.Find(x => x.mipFogMaxMip));
            m_ConstantBelowBase = Unpack(properties.Find(x => x.constantDensityBelowBase));
            m_QualityControl = Unpack(properties.Find(x => x.qualityControl));
            m_QualityLevel = Unpack(properties.Find(x => x.qualityLevel));
            m_MaxLocalVolumes = Unpack(properties.Find(x => x.maxLocalFogVolumes));
            m_ScreenPercentage = Unpack(properties.Find(x => x.screenResolutionPercentage));
            m_Budget = Unpack(properties.Find(x => x.volumetricFogBudget));
            m_DepthRatio = Unpack(properties.Find(x => x.resolutionDepthRatio));
            m_SliceUniformity = Unpack(properties.Find(x => x.sliceDistributionUniformity));
            m_Denoising = Unpack(properties.Find(x => x.denoisingMode));
            m_ProbeDimmer = Unpack(properties.Find(x => x.globalLightProbeDimmer));
            m_MultipleScattering = Unpack(properties.Find(x => x.multipleScatteringIntensity));
            m_GroundExpanded = m_GroundDensity.value.floatValue > 0 || m_GroundDensity.value.hasMultipleDifferentValues;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled);
            PropertyField(m_Mode);
            PropertyField(m_Volumetric);

            bool physical = (AtmosphericFogMode)m_Mode.value.intValue == AtmosphericFogMode.Physical;
            bool volumetric = m_Volumetric.value.boolValue || m_Volumetric.value.hasMultipleDifferentValues;

            Section("Height Fog");
            PropertyField(physical ? m_MeanFreePath : m_Density);
            PropertyField(m_BaseHeight);
            PropertyField(physical ? m_MaximumHeight : m_HeightScale);
            if (!physical)
                PropertyField(m_ConstantBelowBase);
            PropertyField(m_ColorMode);
            if ((FogColorMode)m_ColorMode.value.intValue == FogColorMode.Sky)
            {
                PropertyField(m_SkyTint);
                PropertyField(m_MipNear);
                PropertyField(m_MipFar);
                PropertyField(m_MipMax);
            }
            else
                PropertyField(m_Color);
            PropertyField(m_MaxDistance);

            if (!physical)
            {
                PropertyField(m_StartDistance);
                PropertyField(m_MaxOpacity);
                PropertyField(m_AffectSky);
                PropertyField(m_MultipleScattering);

                Section("Directional Color");
                PropertyField(m_SunColor);
                PropertyField(m_Scattering);
                if (!volumetric)
                    PropertyField(m_Anisotropy);

                EditorGUILayout.Space();
                m_GroundExpanded = EditorGUILayout.Foldout(m_GroundExpanded, "Ground Layer", true);
                if (m_GroundExpanded)
                {
                    PropertyField(m_GroundDensity);
                    PropertyField(m_GroundHeight);
                    PropertyField(m_GroundHeightScale);
                    PropertyField(m_GroundStartDistance);
                }
            }

            if (!volumetric)
                return;

            Section("Volumetric Medium and Lighting");
            PropertyField(m_VolumetricDistance);
            PropertyField(m_Albedo);
            PropertyField(m_Anisotropy);
            if (!physical)
            {
                PropertyField(m_AmbientIntensity);
                PropertyField(m_LightIntensity);
                PropertyField(m_DirectionalOnly);
            }
            PropertyField(m_ProbeDimmer);
            PropertyField(m_Shadows);
            EditorGUILayout.Space();
            m_QualityExpanded = EditorGUILayout.Foldout(m_QualityExpanded, "Volumetric Quality", true);
            if (m_QualityExpanded)
            {
                PropertyField(m_QualityLevel);
                if ((FogQualityLevel)m_QualityLevel.value.intValue == FogQualityLevel.Custom || m_QualityLevel.value.hasMultipleDifferentValues)
                {
                    PropertyField(m_QualityControl);
                    if ((FogQualityControl)m_QualityControl.value.intValue == FogQualityControl.Balance)
                    { PropertyField(m_Budget); PropertyField(m_DepthRatio); }
                    else
                    { PropertyField(m_ScreenPercentage); PropertyField(m_SliceCount); }
                }
                else EditorGUILayout.HelpBox("Balanced presets: Low uses budget 0.166 / depth ratio 0.666; Medium 0.33 / 0.666; High 0.666 / 0.5. Custom exposes the underlying controls.", MessageType.Info);
                PropertyField(m_MaxLocalVolumes);
                PropertyField(m_SliceUniformity);
                PropertyField(m_Denoising);
                if (!physical && (((FogDenoisingMode)m_Denoising.value.intValue & FogDenoisingMode.Reprojection) != 0 || m_Denoising.value.hasMultipleDifferentValues))
                    PropertyField(m_HistoryWeight);
            }
        }

        static void Section(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
