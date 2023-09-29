using UnityEngine;


#if UNITY_EDITOR
using UnityEditorInternal;

namespace UnityEditor.DanbaidongGUI
{
    public class GradientsRampEditorWindow : EditorWindow
    {
        public GradientsRamp m_GradientRampObject;

        private SerializedObject serializedObject;
        private SerializedProperty gradientRampObectProp;
        private SerializedProperty gradientRampTexProp;
        private SerializedProperty gradientsListProp;
        private ReorderableList gradientsList;
        private Texture2D checkerboardTexture;

        // Created with texture(close OnLostFocus)
        public bool editWithTex = false;

        [MenuItem("Tools/GradientsRampEditor")]
        public static void ShowWindow()
        {
            var window = EditorWindow.GetWindow(typeof(GradientsRampEditorWindow));
            window.position = new Rect(800, 300, 500, 809);
        }

        public static void ShowWindow(Texture2D rampTex)
        {
            GradientsRampEditorWindow window = (GradientsRampEditorWindow)EditorWindow.GetWindow(typeof(GradientsRampEditorWindow));
            window.position = new Rect(800, 300, 500, 809);
            window.m_GradientRampObject = new GradientsRamp(rampTex);
            window.editWithTex = true;
        }

        private void OnEnable()
        {
            if (m_GradientRampObject == null)
                m_GradientRampObject = new GradientsRamp();
            serializedObject = new SerializedObject(this);

            gradientRampObectProp = serializedObject.FindProperty("m_GradientRampObject");
            gradientRampTexProp = gradientRampObectProp.FindPropertyRelative("rampTexture");
            gradientsListProp = gradientRampObectProp.FindPropertyRelative("gradients");
            InitReorderableListGUI();
            InitCheckerboardTexture(4, 4);

        }

        private void InitReorderableListGUI()
        {
            // Create the reorderable list
            gradientsList = new ReorderableList(serializedObject, gradientsListProp, true, true, true, true);

            // Define the list element GUI content and draw callback
            gradientsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = gradientsList.serializedProperty.GetArrayElementAtIndex(index);
                rect.y += 2;

                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
            };
            gradientsList.onAddCallback = (ReorderableList list) =>
            {
                int newIndex = list.count;
                gradientsListProp.arraySize = newIndex + 1;
                serializedObject.ApplyModifiedProperties();

                m_GradientRampObject.gradients[newIndex] = GradientsRamp.CreateSampleGradient();
                serializedObject.Update();
            };

            // Set the header label of the list
            gradientsList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Gradient List");
            };
        }
        private void InitCheckerboardTexture(int width, int height)
        {
            if (checkerboardTexture == null)
            {
                checkerboardTexture = new Texture2D(width, height);
                checkerboardTexture.filterMode = FilterMode.Point;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color color = (x + y) % 2 == 0 ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);
                        checkerboardTexture.SetPixel(x, y, color);
                    }
                }

                checkerboardTexture.Apply();
            }
        }
        void OnGUI()
        {
            serializedObject.Update();

            // Texture input
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(gradientRampTexProp);
            if (EditorGUI.EndChangeCheck())
            {
                // Get Tex from Prop to Obj
                serializedObject.ApplyModifiedProperties();

                m_GradientRampObject.LoadGradientRamp(m_GradientRampObject.rampTexture);

                // Set data from Obj to Prop(List)
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
            if (m_GradientRampObject.rampTexture == null)
                GUI.enabled = false;

            // Draw Texture Preview
            GUILayout.Space(10);
            if (m_GradientRampObject.rampTexture != null)
            {
                var rect = EditorGUILayout.GetControlRect(true, 64);
                rect.xMin += 5;
                rect.xMax -= 5;

                var filterModeOri = m_GradientRampObject.rampTexture.filterMode;
                m_GradientRampObject.rampTexture.filterMode = FilterMode.Point;

                Rect texCoords = new Rect(0, 0, 0.5f * rect.width / (float)checkerboardTexture.width, 0.5f * rect.height / (float)checkerboardTexture.height);
                GUI.DrawTextureWithTexCoords(rect, checkerboardTexture, texCoords);

                GUI.DrawTexture(rect, m_GradientRampObject.rampTexture);
                m_GradientRampObject.rampTexture.filterMode = filterModeOri;
            }
            GUILayout.Space(10);


            // Draw Gradients List
            gradientsList.DoLayoutList();
            GUILayout.Space(10);


            // Draw Size Save
            GUILayout.BeginHorizontal();
            Vector2Int singleRampSize = m_GradientRampObject.singleRampSize;
            var rampSizeStyle = new GUIStyle(EditorStyles.boldLabel);
            rampSizeStyle.normal.textColor = (singleRampSize.y > 100) ? Color.red : rampSizeStyle.normal.textColor;
            GUILayout.Label("SingleRampSize: ", rampSizeStyle, GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            singleRampSize = EditorGUILayout.Vector2IntField("", singleRampSize, GUILayout.Width(100));
            if (EditorGUI.EndChangeCheck())
            {
                m_GradientRampObject.singleRampSize = singleRampSize;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", GUILayout.Width(80)))
            {
                m_GradientRampObject.SaveRampData(true);
            }
            if (GUILayout.Button("Close", GUILayout.Width(80)))
            {
                Close();
            }
            GUILayout.EndHorizontal();


            GUI.enabled = true;
            serializedObject.ApplyModifiedProperties();
        }

    } /* GradientsRampEditorWindow */

}

#endif