using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(PerObjectShadowProjector))]
    [CanEditMultipleObjects]
    partial class PerObjectShadowProjectorEditor : Editor
    {
        SerializedProperty m_MaterialProperty;
        SerializedProperty m_RenderersProperty;
        SerializedProperty m_FarPlaneScaleProperty;

        static Color fullColor = Color.white;
        static Color transColor = new Color(1,1,1,0.3f);
        static HierarchicalBox s_BoxHandle;
        static HierarchicalBox boxHandle
        {
            get
            {
                if (s_BoxHandle == null || s_BoxHandle.Equals(null))
                {
                    Color c = fullColor;
                    s_BoxHandle = new HierarchicalBox(fullColor, new[] { c, c, c, c, c, c });
                    s_BoxHandle.SetBaseColor(fullColor);
                    s_BoxHandle.monoHandle = false;
                }
                return s_BoxHandle;
            }
        }
        static GUIContent debugContent;

        private void OnEnable()
        {
            m_MaterialProperty = serializedObject.FindProperty("m_Material");
            m_RenderersProperty = serializedObject.FindProperty("m_Renderers");
            m_FarPlaneScaleProperty = serializedObject.FindProperty("m_FarPlaneScale");

            if (debugContent == null)
                debugContent = new GUIContent("Global Debug Settings");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var projector = (PerObjectShadowProjector)target;
            bool isDefaultMaterial = false;
            bool isValidObjectShadowMaterial = true;

            EditorGUI.BeginChangeCheck();
            {
                EditorGUILayout.PropertyField(m_MaterialProperty);
                EditorGUILayout.PropertyField(m_FarPlaneScaleProperty);

                foreach (var target in targets)
                {
                    var objectShadowProjector = target as PerObjectShadowProjector;
                    var mat = objectShadowProjector.material;

                    isDefaultMaterial |= objectShadowProjector.material == PerObjectShadowProjector.defaultMaterial;
                    isValidObjectShadowMaterial &= objectShadowProjector.IsValid();
                }

                if (!isValidObjectShadowMaterial)
                {
                    CoreEditorUtils.DrawFixMeBox("ObjectShadow Material is invalid.", () =>
                    {
                        m_MaterialProperty.objectReferenceValue = PerObjectShadowProjector.defaultMaterial;
                    });
                }

                EditorGUILayout.Space(5);

                EditorGUILayout.HelpBox("Renderers for rendering, collected from children, use Collect Button if you modified child renderer", MessageType.Info);
                EditorGUILayout.BeginHorizontal();

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Collect", GUILayout.Width(150)))
                {
                    projector.CollectRenderers();
                }

                EditorGUILayout.EndHorizontal();

                var preEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUILayout.PropertyField(m_RenderersProperty);
                GUI.enabled = preEnabled;
            }

            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginChangeCheck();
            {

                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox(debugContent);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("ShowDebugGzimos");
                GUILayout.FlexibleSpace();
                PerObjectShadowProjector.showDebugGzimos = EditorGUILayout.Toggle(PerObjectShadowProjector.showDebugGzimos, GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmosSelected(PerObjectShadowProjector objectShadowProjector, GizmoType gizmoType)
        {
            if (!PerObjectShadowProjector.showDebugGzimos)
            {
                return;
            }
            Renderer[] renderers = objectShadowProjector.childRenderers;
            Light[] lights = GameObject.FindObjectsOfType<Light>();
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }
            if (lights == null || lights.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (int j = 1; j < renderers.Length; j++)
            {
                bounds.Encapsulate(renderers[j].bounds);
            }

            Light directLight = null;
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light.type == LightType.Directional)
                {
                    directLight = light;
                    break;
                }
            }
            
            if (directLight == null)
            {
                return;
            }

            Matrix4x4 viewMatrix = new Matrix4x4();
            Matrix4x4 projMatrix = new Matrix4x4();
            Vector3 sphereCenter = Vector3.zero;
            float sphereRadius = 0;

            PerObjectShadowUtils.ComputePerObjectShadowMatricesAndCullingSphere(directLight.transform.forward, directLight.transform.up, directLight.transform.right
                                                        , bounds, objectShadowProjector.farPlaneScale, out viewMatrix, out projMatrix, out sphereCenter, out sphereRadius);

            using (new Handles.DrawingScope(fullColor, PerObjectShadowUtils.GetShadowProjectorToWorldMatrix(projMatrix, viewMatrix)))
            {
                Vector3 scaledPivot = new Vector3(0, 0, 0.0f);
                // PerObjectShadowUtils.shadowProjectorMesh size
                Vector3 scaledSize = Vector3.one * 2;

                boxHandle.center = scaledPivot;
                boxHandle.size = scaledSize;

                boxHandle.DrawHull(false);

                Vector3 projectedPivot = new Vector3(0, 0, scaledPivot.z - .5f * scaledSize.z);
                float arrowSize = scaledSize.z * 0.25f / objectShadowProjector.farPlaneScale;
                Handles.color = transColor;
                Handles.ArrowHandleCap(0, projectedPivot, Quaternion.identity, arrowSize, EventType.Repaint);
            }

        }
    }
}
