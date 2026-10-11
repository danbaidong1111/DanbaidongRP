using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(LocalVolumetricFog)), CanEditMultipleObjects]
    sealed class LocalVolumetricFogEditor : Editor
    {
        SerializedProperty m_Size, m_ScaleMode, m_Spherical, m_Density, m_Albedo, m_BlendingMode, m_Priority;
        SerializedProperty m_MaskMode, m_Texture, m_Material, m_MaskResolution, m_Tiling, m_Offset, m_Scroll;
        SerializedProperty m_PerAxis, m_EdgeFade, m_PositiveFade, m_NegativeFade, m_Invert, m_Falloff, m_DistanceStart, m_DistanceEnd;
        SerializedProperty m_NoiseAmount, m_NoiseScale, m_NoiseVelocity;

        void OnEnable()
        {
            m_Size = serializedObject.FindProperty("size");
            m_ScaleMode = serializedObject.FindProperty("scaleMode");
            m_Spherical = serializedObject.FindProperty("spherical");
            m_Density = serializedObject.FindProperty("density");
            m_Albedo = serializedObject.FindProperty("albedo");
            m_BlendingMode = serializedObject.FindProperty("blendingMode");
            m_Priority = serializedObject.FindProperty("priority");
            m_MaskMode = serializedObject.FindProperty("maskMode");
            m_Texture = serializedObject.FindProperty("volumeMask");
            m_Material = serializedObject.FindProperty("materialMask");
            m_MaskResolution = serializedObject.FindProperty("materialMaskResolution");
            m_Tiling = serializedObject.FindProperty("textureTiling");
            m_Offset = serializedObject.FindProperty("textureOffset");
            m_Scroll = serializedObject.FindProperty("textureScrollingSpeed");
            m_PerAxis = serializedObject.FindProperty("perAxisFade");
            m_EdgeFade = serializedObject.FindProperty("edgeFade");
            m_PositiveFade = serializedObject.FindProperty("positiveFade");
            m_NegativeFade = serializedObject.FindProperty("negativeFade");
            m_Invert = serializedObject.FindProperty("invertFade");
            m_Falloff = serializedObject.FindProperty("falloffMode");
            m_DistanceStart = serializedObject.FindProperty("distanceFadeStart");
            m_DistanceEnd = serializedObject.FindProperty("distanceFadeEnd");
            m_NoiseAmount = serializedObject.FindProperty("noiseAmount");
            m_NoiseScale = serializedObject.FindProperty("noiseScale");
            m_NoiseVelocity = serializedObject.FindProperty("noiseVelocity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Section("Bounds and Medium");
            EditorGUILayout.PropertyField(m_Size);
            EditorGUILayout.PropertyField(m_ScaleMode);
            EditorGUILayout.PropertyField(m_Spherical);
            EditorGUILayout.PropertyField(m_Density);
            EditorGUI.showMixedValue = m_Density.hasMultipleDifferentValues;
            float attenuationDistance = m_Density.floatValue > 0 ? 1f / m_Density.floatValue : float.PositiveInfinity;
            EditorGUI.BeginChangeCheck();
            float editedDistance = EditorGUILayout.FloatField(new GUIContent("Fog Attenuation Distance", "Distance in meters that transmits 1/e of background light. Infinity means zero density. This edits the same medium as Density."), attenuationDistance);
            if (EditorGUI.EndChangeCheck())
                m_Density.floatValue = float.IsPositiveInfinity(editedDistance) ? 0f : 1f / Mathf.Max(0.05f, float.IsNaN(editedDistance) ? 10f : editedDistance);
            EditorGUI.showMixedValue = false;
            EditorGUILayout.PropertyField(m_Albedo);
            EditorGUILayout.PropertyField(m_BlendingMode);
            EditorGUILayout.PropertyField(m_Priority);

            Section("Volume Mask");
            EditorGUILayout.PropertyField(m_MaskMode);
            if (m_MaskMode.hasMultipleDifferentValues || m_MaskMode.intValue == (int)LocalVolumetricFogMaskMode.Texture)
            {
                EditorGUILayout.PropertyField(m_Texture);
                var texture = m_Texture.objectReferenceValue as Texture;
                if (texture != null && texture.dimension != UnityEngine.Rendering.TextureDimension.Tex3D)
                    EditorGUILayout.HelpBox("The mask must be a 3D texture or a RenderTexture with dimension Tex3D. This texture will be ignored.", MessageType.Warning);
            }
            if (m_MaskMode.hasMultipleDifferentValues || m_MaskMode.intValue == (int)LocalVolumetricFogMaskMode.Material)
            {
                EditorGUILayout.PropertyField(m_Material);
                EditorGUILayout.PropertyField(m_MaskResolution);
                var material = m_Material.objectReferenceValue as Material;
                if (material == null)
                    EditorGUILayout.HelpBox("Assign a material using Danbaidong/Fog Volume Mask or a compatible DanbaidongFogVolume pass. RGB multiplies Albedo and alpha multiplies Density.", MessageType.Info);
                else if (material.FindPass("DanbaidongFogVolume") < 0)
                    EditorGUILayout.HelpBox("This material has no DanbaidongFogVolume pass. Its first pass will be used; it must follow the volume mask output contract.", MessageType.Warning);
            }
            EditorGUILayout.PropertyField(m_Tiling);
            EditorGUILayout.PropertyField(m_Offset);
            EditorGUILayout.PropertyField(m_Scroll);

            Section("Boundary and Distance Fade");
            EditorGUILayout.PropertyField(m_PerAxis);
            Vector3 axisSize = GetAxisSize();
            if (m_PerAxis.boolValue || m_PerAxis.hasMultipleDifferentValues)
            {
                FaceFade(m_PositiveFade, "Positive Blend Distance", axisSize);
                FaceFade(m_NegativeFade, "Negative Blend Distance", axisSize);
            }
            else if (targets.Length == 1)
            {
                float shortestAxis = Mathf.Min(axisSize.x, Mathf.Min(axisSize.y, axisSize.z));
                EditorGUI.BeginChangeCheck();
                float distance = EditorGUILayout.Slider(new GUIContent("Blend Distance", "Uniform boundary blend width in meters. Limited to half of the shortest scaled axis."), m_EdgeFade.floatValue * shortestAxis, 0f, shortestAxis * 0.5f);
                if (EditorGUI.EndChangeCheck())
                    m_EdgeFade.floatValue = distance / shortestAxis;
            }
            else
            {
                EditorGUILayout.PropertyField(m_EdgeFade, new GUIContent("Uniform Fade Fraction", "Fraction of the shortest scaled axis. Each selected object's blend distance is derived from its own bounds."));
            }
            EditorGUILayout.PropertyField(m_Invert);
            EditorGUILayout.PropertyField(m_Falloff);
            EditorGUILayout.PropertyField(m_DistanceStart);
            EditorGUILayout.PropertyField(m_DistanceEnd);
            if (!m_DistanceStart.hasMultipleDifferentValues && !m_DistanceEnd.hasMultipleDifferentValues && m_DistanceEnd.floatValue < m_DistanceStart.floatValue)
                EditorGUILayout.HelpBox("Distance Fade End is below Start. Rendering clamps End to Start, producing an abrupt distance cutoff.", MessageType.Info);

            Section("Additional Density Noise");
            EditorGUILayout.PropertyField(m_NoiseAmount);
            if (m_NoiseAmount.floatValue > 0 || m_NoiseAmount.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(m_NoiseScale);
                EditorGUILayout.PropertyField(m_NoiseVelocity);
            }
            serializedObject.ApplyModifiedProperties();
        }

        Vector3 GetAxisSize()
        {
            var volume = (LocalVolumetricFog)target;
            Vector3 dimensions = m_Size.vector3Value;
            if (m_ScaleMode.intValue == (int)LocalVolumetricFogScaleMode.InheritFromHierarchy)
                dimensions = Vector3.Scale(dimensions, volume.transform.lossyScale);
            return new Vector3(Mathf.Max(0.001f, Mathf.Abs(dimensions.x)), Mathf.Max(0.001f, Mathf.Abs(dimensions.y)), Mathf.Max(0.001f, Mathf.Abs(dimensions.z)));
        }

        void FaceFade(SerializedProperty property, string label, Vector3 axisSize)
        {
            if (targets.Length > 1)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label + " (fraction)", "Normalized widths along each selected object's local axes, from zero to one."));
                return;
            }
            EditorGUI.BeginChangeCheck();
            Vector3 distance = EditorGUILayout.Vector3Field(new GUIContent(label, "Blend distance in meters along the volume's local X, Y and Z axes."), Vector3.Scale(property.vector3Value, axisSize));
            if (EditorGUI.EndChangeCheck())
                property.vector3Value = new Vector3(Mathf.Clamp01(distance.x / axisSize.x), Mathf.Clamp01(distance.y / axisSize.y), Mathf.Clamp01(distance.z / axisSize.z));
        }

        static void Section(string label)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}
