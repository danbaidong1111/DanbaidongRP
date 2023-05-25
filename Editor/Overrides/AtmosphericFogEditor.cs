using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;


namespace UnityEditor.Rendering.Universal
{
    [VolumeComponentEditor(typeof(AtmosphericFog))]
    sealed class AtmosphericFogEditor : VolumeComponentEditor
    {
        //SerializedDataParameter m_Mode;
        //SerializedDataParameter m_Threshold;
        //SerializedDataParameter m_Intensity;
        //SerializedDataParameter m_Scatter;
        //SerializedDataParameter m_Clamp;
        //SerializedDataParameter m_Tint;
        //SerializedDataParameter m_HighQualityFiltering;
        //SerializedDataParameter m_SkipIterations;
        //SerializedDataParameter m_DirtTexture;
        //SerializedDataParameter m_DirtIntensity;

        SerializedDataParameter m_enableAtmosphericFog;
        SerializedDataParameter m_useController;
        SerializedDataParameter m_sampleCount;
        SerializedDataParameter m_mieScatterColor;
        SerializedDataParameter m_moonMieScatterColor;
        SerializedDataParameter m_mieScatterFacto;
        SerializedDataParameter m_mieExtinctionFactor;
        SerializedDataParameter m_mieG;

        SerializedDataParameter m_fogColor;
        SerializedDataParameter m_fogDensity;
        SerializedDataParameter m_heightFogEnd;
        SerializedDataParameter m_heightFalloff;

        SerializedDataParameter m_inscatteringExponent;
        SerializedDataParameter m_fogMieStrength;

        SerializedDataParameter m_groundFogDensity;
        SerializedDataParameter m_groundHeightFogEnd;
        SerializedDataParameter m_groundHeightFalloff;
        SerializedDataParameter m_groundFogHeightLimit;
        SerializedDataParameter m_groundFogDistanceLimit;
        SerializedDataParameter m_groundFogDistanceFalloff;

        SerializedDataParameter m_heightScale;
        SerializedDataParameter m_heightMapST;
        SerializedDataParameter m_heightMap2D;
        SerializedDataParameter m_heightMapNoiseST;
        SerializedDataParameter m_heightMapNoise;

        SerializedDataParameter m_enableLightShaft;
        SerializedDataParameter m_lightShaftMieG;
        SerializedDataParameter m_lightShaftBlurDistance;
        SerializedDataParameter m_lightShaftIntensity;
        SerializedDataParameter m_lightShaftRevertScale;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AtmosphericFog>(serializedObject);

            //m_Mode = Unpack(o.Find(x => x.mode));
            //m_Threshold = Unpack(o.Find(x => x.threshold));
            //m_Intensity = Unpack(o.Find(x => x.intensity));
            //m_Scatter = Unpack(o.Find(x => x.scatter));
            //m_Clamp = Unpack(o.Find(x => x.clamp));
            //m_Tint = Unpack(o.Find(x => x.tint));
            //m_HighQualityFiltering = Unpack(o.Find(x => x.highQualityFiltering));
            //m_SkipIterations = Unpack(o.Find(x => x.skipIterations));
            //m_DirtTexture = Unpack(o.Find(x => x.dirtTexture));
            //m_DirtIntensity = Unpack(o.Find(x => x.dirtIntensity));
            m_enableAtmosphericFog     = Unpack(o.Find(x => x.enableAtmosphericFog));
            m_useController            = Unpack(o.Find(x => x.AtmosphericFogUseController));
            m_sampleCount              = Unpack(o.Find(x => x.sampleCount));
            m_mieScatterColor          = Unpack(o.Find(x => x.mieScatterColor));
            m_moonMieScatterColor      = Unpack(o.Find(x => x.moonMieScatterColor));
            m_mieScatterFacto          = Unpack(o.Find(x => x.moonMieScatterColor));
            m_mieExtinctionFactor      = Unpack(o.Find(x => x.mieExtinctionFactor));
            m_mieG                     = Unpack(o.Find(x => x.mieG));
            
            m_fogColor                 = Unpack(o.Find(x => x.fogColor));
            m_fogDensity               = Unpack(o.Find(x => x.fogDensity));
            m_heightFogEnd             = Unpack(o.Find(x => x.heightFogEnd));
            m_heightFalloff            = Unpack(o.Find(x => x.heightFalloff));
            
            m_inscatteringExponent     = Unpack(o.Find(x => x.inscatteringExponent));
            m_fogMieStrength           = Unpack(o.Find(x => x.fogMieStrength));
            
            m_groundFogDensity         = Unpack(o.Find(x => x.groundFogDensity));
            m_groundHeightFogEnd       = Unpack(o.Find(x => x.groundHeightFogEnd));
            m_groundHeightFalloff      = Unpack(o.Find(x => x.groundHeightFalloff));
            m_groundFogHeightLimit     = Unpack(o.Find(x => x.groundFogHeightLimit));
            m_groundFogDistanceLimit   = Unpack(o.Find(x => x.groundFogDistanceLimit));
            m_groundFogDistanceFalloff = Unpack(o.Find(x => x.groundFogDistanceFalloff));
            
            m_heightScale              = Unpack(o.Find(x => x.heightScale));
            m_heightMapST              = Unpack(o.Find(x => x.heightMapST));
            m_heightMap2D              = Unpack(o.Find(x => x.heightMap2D));
            m_heightMapNoiseST         = Unpack(o.Find(x => x.heightMapNoiseST));
            m_heightMapNoise           = Unpack(o.Find(x => x.heightMapNoise));

            m_enableLightShaft         = Unpack(o.Find(x => x.enableLightShaft));
            m_lightShaftMieG           = Unpack(o.Find(x => x.lightShaftMieG));
            m_lightShaftBlurDistance   = Unpack(o.Find(x => x.lightShaftBlurDistance));
            m_lightShaftIntensity      = Unpack(o.Find(x => x.lightShaftIntensity));
            m_lightShaftRevertScale    = Unpack(o.Find(x => x.lightShaftRevertScale));
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Bloom", EditorStyles.miniLabel);
            PropertyField(m_enableAtmosphericFog);
            PropertyField(m_useController);
            PropertyField(m_sampleCount);
            PropertyField(m_mieScatterColor);
            PropertyField(m_moonMieScatterColor);
            PropertyField(m_mieScatterFacto);
            PropertyField(m_mieExtinctionFactor);
            PropertyField(m_mieG);
            
            PropertyField(m_fogColor);
            PropertyField(m_fogDensity);
            PropertyField(m_heightFogEnd);
            PropertyField(m_heightFalloff);
            
            PropertyField(m_inscatteringExponent);
            PropertyField(m_fogMieStrength);
            
            PropertyField(m_groundFogDensity);
            PropertyField(m_groundHeightFogEnd);
            PropertyField(m_groundHeightFalloff);
            PropertyField(m_groundFogHeightLimit);
            PropertyField(m_groundFogDistanceLimit);
            PropertyField(m_groundFogDistanceFalloff);
            
            PropertyField(m_heightScale);
            PropertyField(m_heightMapST);
            PropertyField(m_heightMap2D);
            PropertyField(m_heightMapNoiseST);
            PropertyField(m_heightMapNoise);
            
            PropertyField(m_enableLightShaft);
            PropertyField(m_lightShaftMieG);
            PropertyField(m_lightShaftBlurDistance);
            PropertyField(m_lightShaftIntensity);
            PropertyField(m_lightShaftRevertScale);

            //if (m_HighQualityFiltering.overrideState.boolValue && m_HighQualityFiltering.value.boolValue && CoreEditorUtils.buildTargets.Contains(GraphicsDeviceType.OpenGLES2))
            //    EditorGUILayout.HelpBox("High Quality Bloom isn't supported on GLES2 platforms.", MessageType.Warning);

            //PropertyField(m_SkipIterations);

            //EditorGUILayout.LabelField("Lens Dirt", EditorStyles.miniLabel);

            //PropertyField(m_DirtTexture);
            //PropertyField(m_DirtIntensity);
        }
    }
}
