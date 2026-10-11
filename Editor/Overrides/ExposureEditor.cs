using System.Collections.Generic;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(Exposure))]
    sealed class ExposureEditor : VolumeComponentEditor
    {
        readonly Dictionary<string, SerializedDataParameter> m_Fields = new Dictionary<string, SerializedDataParameter>();
        public override void OnEnable()
        {
            m_Fields.Clear();
            foreach (string name in new[] { "mode", "fixedExposure", "compensation", "metering", "limitMin", "limitMax", "targetMidGrey", "histogramLowPercent", "histogramHighPercent", "histogramUseCurveRemapping", "curveMap", "limitMinCurveMap", "limitMaxCurveMap", "adaptationMode", "speedLightToDark", "speedDarkToLight", "weightTextureMask", "centerAroundExposureTarget", "proceduralCenter", "proceduralRadii", "proceduralSoftness", "maskMinIntensity", "maskMaxIntensity" })
                m_Fields.Add(name, Unpack(serializedObject.FindProperty(name)));
        }
        void Draw(string name) => PropertyField(m_Fields[name]);
        public override void OnInspectorGUI()
        {
            Draw("mode");
            var mode = (ExposureMode)m_Fields["mode"].value.intValue;
            if (mode == ExposureMode.Off) return;
            if (mode == ExposureMode.Fixed) Draw("fixedExposure");
            Draw("compensation");
            if (mode == ExposureMode.UsePhysicalCamera)
                EditorGUILayout.HelpBox("Exposure uses the rendering camera's aperture, shutter speed, and ISO.", MessageType.Info);
            if (mode == ExposureMode.Fixed || mode == ExposureMode.UsePhysicalCamera) return;
            Draw("metering");
            var metering = (ExposureMeteringMode)m_Fields["metering"].value.intValue;
            if (metering == ExposureMeteringMode.MaskWeighted) Draw("weightTextureMask");
            if (metering == ExposureMeteringMode.ProceduralMask)
            {
                Draw("centerAroundExposureTarget");
                Draw("proceduralCenter"); Draw("proceduralRadii"); Draw("proceduralSoftness");
                Draw("maskMinIntensity"); Draw("maskMaxIntensity");
            }
            Draw("targetMidGrey");
            bool curve = mode == ExposureMode.CurveMapping;
            if (mode == ExposureMode.AutomaticHistogram)
            {
                Draw("histogramLowPercent"); Draw("histogramHighPercent"); Draw("histogramUseCurveRemapping");
                curve = m_Fields["histogramUseCurveRemapping"].value.boolValue;
            }
            if (curve) { Draw("curveMap"); Draw("limitMinCurveMap"); Draw("limitMaxCurveMap"); }
            else { Draw("limitMin"); Draw("limitMax"); }
            Draw("adaptationMode");
            if ((ExposureAdaptationMode)m_Fields["adaptationMode"].value.intValue == ExposureAdaptationMode.Progressive)
            { Draw("speedLightToDark"); Draw("speedDarkToLight"); }
        }
    }
}
