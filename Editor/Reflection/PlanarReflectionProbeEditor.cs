using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(PlanarReflectionProbe)), CanEditMultipleObjects]
    sealed class PlanarReflectionProbeEditor : Editor
    {
        Camera m_PreviewCamera;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Captures"))
            {
                foreach (Object selected in targets)
                    ((PlanarReflectionProbe)selected).RequestRender();
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            if (targets.Length != 1)
                return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Capture Preview", EditorStyles.boldLabel);
            m_PreviewCamera = (Camera)EditorGUILayout.ObjectField("Source Camera", m_PreviewCamera, typeof(Camera), true);
            Camera source = m_PreviewCamera != null ? m_PreviewCamera : Camera.main;
            if (source == null && SceneView.lastActiveSceneView != null)
                source = SceneView.lastActiveSceneView.camera;
            RenderTexture texture = ((PlanarReflectionProbe)target).GetTexture(source);
            if (texture != null)
            {
                Rect rect = GUILayoutUtility.GetAspectRect(texture.width / (float)texture.height);
                EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
                EditorGUILayout.LabelField($"{source.name}: {texture.width} × {texture.height}, {texture.mipmapCount} mip levels", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Render a camera that can see the plane region to generate a capture. The plane's local Y axis points toward its viewing side.", MessageType.Info);
            }
        }

        public override bool RequiresConstantRepaint() => targets.Length == 1;
    }
}
