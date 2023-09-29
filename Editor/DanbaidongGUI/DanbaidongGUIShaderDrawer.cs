using System;
using UnityEngine;

#if UNITY_EDITOR
namespace UnityEditor.DanbaidongGUI
{
    public class DrawerHelper
    {
        // Based on internal properties, init in InitGUIStyle
        private static GUIStyle m_FoldoutStyle;
        private static GUIStyle m_FoldoutToggleStyle;
        private static GUIStyle m_BackgroundStyle;
        private static GUIContent m_EditBtnContent;

        // Shader GUI Label
        private static string m_ShaderGUILabel = "Danbaidong Shader GUI";
        private static GUIStyleState m_GUIStyleState = new GUIStyleState() { textColor = Color.grey };
        private static GUIStyle m_LabelStyle = new GUIStyle()
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            fontStyle = FontStyle.BoldAndItalic,
            normal = m_GUIStyleState
        };

        /// <summary>
        /// Must called before you used this helper. wait for base GUIStyle so init it in OnGUI()
        /// </summary>
        public static void InitGUIStyle()
        {
            m_FoldoutStyle = new GUIStyle("minibutton")
            {
                contentOffset = new Vector2(7, 0),
                fixedHeight = 27,
                alignment = TextAnchor.MiddleLeft,
                font = EditorStyles.boldLabel.font,
                fontSize = EditorStyles.boldLabel.fontSize
            };

            m_FoldoutToggleStyle = new GUIStyle("Toggle");

            m_BackgroundStyle = new GUIStyle(GUI.skin.box);
            m_BackgroundStyle.padding.top = 0;
            m_BackgroundStyle.fontStyle = FontStyle.Italic;
            m_BackgroundStyle.normal.textColor = new Color(1, 1, 1, 0.4f);
            m_BackgroundStyle.active.textColor = new Color(1, 1, 1, 0.9f);
            m_BackgroundStyle.hover.textColor = new Color(1, 1, 1, 0.9f);

            m_EditBtnContent = new GUIContent(EditorGUIUtility.IconContent("editicon.sml").image, "Edit");
        }

        public static void DrawSingleLine()
        {
            EditorGUILayout.Space();
            var rect = EditorGUILayout.GetControlRect(true, 1);
            rect.x = 0;
            rect.width = EditorGUIUtility.currentViewWidth;
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.45f));
            EditorGUILayout.Space();
        }
        public static void DrawLabel()
        {
            // Label
            var labelRect = EditorGUILayout.GetControlRect(true, 40);
            labelRect.x = 0;
            labelRect.width = EditorGUIUtility.currentViewWidth;
            EditorGUI.LabelField(labelRect, m_ShaderGUILabel, m_LabelStyle);
            EditorGUILayout.Space(5);
        }
        public static bool DrawFoldoutBegin(Rect rect, ref bool isFolded, GUIContent label)
        {
            // FoldButton
            var enabled = GUI.enabled;
            GUI.enabled = true;
            var guiColor = GUI.backgroundColor;
            GUI.backgroundColor = isFolded ? new Color(0.8f, 0.8f, 0.8f) : Color.white;
            if (GUI.Button(rect, label, m_FoldoutStyle))
            {
                isFolded = !isFolded;
            }
            GUI.backgroundColor = guiColor;
            GUI.enabled = enabled;

            return true;
        }
        public static bool DrawFoldoutBegin(Rect rect, ref bool isFolded, GUIContent label, ref bool keyToogleVal, string keyword)
        {
            label.text += " ( " + keyword + " ) ";
            var toggleRect = new Rect(rect.width + 5f, rect.y + 7f, 13f, 13f);
            if (Event.current.type == EventType.MouseDown && toggleRect.Contains(Event.current.mousePosition))
            {
                keyToogleVal = !keyToogleVal;
                Event.current.Use();
                GUI.changed = true;
            }


            // FoldButton
            var enabled = GUI.enabled;
            GUI.enabled = true;
            var guiColor = GUI.backgroundColor;
            GUI.backgroundColor = isFolded ? new Color(0.8f, 0.8f, 0.8f) : Color.white;
            if (GUI.Button(rect, label, m_FoldoutStyle))
            {
                isFolded = !isFolded;
            }
            GUI.backgroundColor = guiColor;
            GUI.enabled = enabled;

            // Keyword Toogle

            GUI.Toggle(toggleRect, keyToogleVal, String.Empty, m_FoldoutToggleStyle);


            return true;
        }

        public static bool DrawFoldoutEnd()
        {
            //EditorGUILayout.Space();
            //var rect = EditorGUILayout.GetControlRect(true, 1);

            //rect.width -= 15;
            //EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.3f));
            //EditorGUILayout.Space();

            return true;
        }

        public static bool DrawRamp(Rect rect, GUIContent label, ref MaterialProperty prop, MaterialEditor editor)
        {
            // Calc Area
            float btnRectWidth = 65f;
            float splitWidth = 13f;
            var labelWidthOri = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0;
            Rect priviewTexRect = MaterialEditor.GetRectAfterLabelWidth(rect);
            EditorGUIUtility.labelWidth = labelWidthOri;
            priviewTexRect.width -= btnRectWidth + splitWidth;

            Rect editBtnRect = priviewTexRect;
            editBtnRect.width = btnRectWidth;
            editBtnRect.x += priviewTexRect.width + splitWidth;

            var miniTexRect = editBtnRect;
            miniTexRect.x -= splitWidth + 3 + 15 * (EditorGUI.indentLevel + 1);

            //Draw MiniTex
            editor.TexturePropertyMiniThumbnail(miniTexRect, prop, "", "RampTexture");

            // Draw Label (ovveride miniTex label)
            EditorGUI.PrefixLabel(rect, label);

            // Draw background Button
            if (prop.textureValue == null)
            {
                EditorGUI.DrawTextureTransparent(priviewTexRect, Texture2D.blackTexture);
            }
            if (GUI.Button(priviewTexRect, "RampTexture", m_BackgroundStyle) && prop.textureValue != null)
            {
                EditorGUIUtility.PingObject(prop.textureValue);
            }


            if (prop.textureValue == null)
            {
                GUI.enabled = false;
            }

            // Draw Edit Button
            Color guiColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.9f, 0.9f, 0.6f);
            if (GUI.Button(editBtnRect, m_EditBtnContent))
            {
                if (prop.textureValue != null)
                {
                    GradientsRampEditorWindow.ShowWindow((Texture2D)prop.textureValue);
                }
            }
            GUI.backgroundColor = guiColor;


            // Draw preview texture
            if (prop.textureValue != null && priviewTexRect.width > 20)
            {
                EditorGUI.DrawTextureTransparent(priviewTexRect, prop.textureValue);
            }
            else
            {
                GUI.enabled = true;
            }

            // Drag texture event
            Event currentEvent = Event.current;
            switch (currentEvent.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (priviewTexRect.Contains(currentEvent.mousePosition))
                    {
                        if (DragAndDrop.objectReferences.Length > 0
                            && DragAndDrop.objectReferences[0] is Texture2D)
                        {
                            // Visual settings
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                            // Set dragged texture
                            if (currentEvent.type == EventType.DragPerform)
                            {
                                DragAndDrop.AcceptDrag();
                                var draggedObjs = DragAndDrop.objectReferences;
                                var draggedTexture = (Texture2D)draggedObjs[0];
                                if (draggedTexture != null)
                                    prop.textureValue = draggedTexture;
                            }

                        }
                    }
                    break;
            }

            return true;
        }
        public static bool DrawRangeSlider(Rect rect, GUIContent label, ref float startVal, ref float endVal, float min, float max)
        {
            // Rect area setting 
            float splitWidth = 3f;
            float floatRectWidth = 31;
            Rect sliderRect;
            Rect floatRect1;
            Rect floatRect2;
            var labelWidthOri = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0;
            sliderRect = MaterialEditor.GetRectAfterLabelWidth(rect);
            EditorGUIUtility.labelWidth = labelWidthOri;
            sliderRect.width -= (floatRectWidth + splitWidth) * 2;
            floatRect1 = sliderRect;
            floatRect1.x += sliderRect.width + splitWidth;
            floatRect1.width = floatRectWidth;
            floatRect2 = floatRect1;
            floatRect2.x += splitWidth + floatRectWidth;

            // Draw Label
            EditorGUI.PrefixLabel(rect, label);


            EditorGUI.MinMaxSlider(sliderRect, ref startVal, ref endVal, min, max);

            int indentLevelStep = 1;
            if (EditorGUI.indentLevel < 1)
                indentLevelStep = 0;

            EditorGUI.indentLevel -= indentLevelStep;
            startVal = EditorGUI.FloatField(floatRect1, startVal);
            endVal = EditorGUI.FloatField(floatRect2, endVal);
            EditorGUI.indentLevel += indentLevelStep;

            return true;
        }
        public static bool DrawTitle(Rect rect, string title)
        {
            rect = EditorGUI.IndentedRect(rect);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.alignment = TextAnchor.LowerLeft;
            style.border.bottom = 2;
            GUI.Label(rect, title, style);

            return true;
        }
        public static void AdaptiveFieldWidth(GUIStyle style, GUIContent content, float extraWidth = 0)
        {
            var extraTextWidth = Mathf.Max(0, style.CalcSize(content).x + extraWidth - EditorGUIUtility.fieldWidth);
            EditorGUIUtility.labelWidth -= extraTextWidth;
            EditorGUIUtility.fieldWidth += extraTextWidth;
        }
        public static void SetShaderKeyWord(UnityEngine.Object[] materials, string keyWord, bool isEnable)
        {
            if (string.IsNullOrEmpty(keyWord))
                return;

            foreach (Material m in materials)
            {
                // delete "_" keywords
                if (keyWord == "_")
                {
                    if (m.IsKeywordEnabled(keyWord))
                    {
                        m.DisableKeyword(keyWord);
                    }
                    continue;
                }

                if (m.IsKeywordEnabled(keyWord))
                {
                    if (!isEnable) m.DisableKeyword(keyWord);
                }
                else
                {
                    if (isEnable) m.EnableKeyword(keyWord);
                }
            }
        }
    }

    public class FoldoutBeginDrawer : MaterialPropertyDrawer
    {
        private string m_FoldEndName = "";
        private string m_Keyword = "";

        private bool m_UseKeyword = false;
        public FoldoutBeginDrawer(string endName)
        {
            m_FoldEndName = endName;
        }

        public FoldoutBeginDrawer(string endName, string keyword)
        {
            m_FoldEndName = endName;
            m_Keyword = keyword;
            m_UseKeyword = true;
        }

        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            int bitwiseVal = (int)prop.floatValue;
            int foldStateMask = 0b_0000_0001;
            int keywordMask = 0b_0000_0010;
            bool isFolded = (bitwiseVal & foldStateMask) > 0;
            bool keyToogleVal = (bitwiseVal & keywordMask) > 0;
            DanbaidongGUI customGUI = (DanbaidongGUI)editor.customShaderGUI;

            MaterialEditor.BeginProperty(rect, prop);
            EditorGUI.BeginChangeCheck();

            //DrawFoldout
            if (m_UseKeyword)
            {
                DrawerHelper.DrawFoldoutBegin(rect, ref isFolded, label, ref keyToogleVal, m_Keyword);
            }
            else
            {
                DrawerHelper.DrawFoldoutBegin(rect, ref isFolded, label);
            }


            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);

                int resultVal = 0;
                if (m_UseKeyword)
                {
                    DrawerHelper.SetShaderKeyWord(editor.targets, m_Keyword, keyToogleVal);
                    resultVal |= keyToogleVal ? keywordMask : 0;
                }

                resultVal |= isFolded ? foldStateMask : 0;


                prop.floatValue = resultVal;
            }

            MaterialEditor.EndProperty();

            customGUI?.SetFoldoutBegin(isFolded, m_FoldEndName, m_UseKeyword ? keyToogleVal : true);
        }


        // Called in custom shader gui
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return 28f;
        }
    }

    public class FoldoutEndDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            DanbaidongGUI customGUI = (DanbaidongGUI)editor.customShaderGUI;
            MaterialEditor.BeginProperty(rect, prop);


            DrawerHelper.DrawFoldoutEnd();

            MaterialEditor.EndProperty();
            customGUI?.SetFoldoutEnd();
        }

        // Called in custom shader gui
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return 1f;
        }
    }

    public class RampDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            MaterialEditor.BeginProperty(rect, prop);

            DrawerHelper.DrawRamp(rect, label, ref prop, editor);

            MaterialEditor.EndProperty();
        }

        // Called in custom shader gui
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }

    public class RangeSliderDrawer : MaterialPropertyDrawer
    {
        private string m_StartPropName;
        private string m_EndPropName;
        private bool m_hideChildProp;
        private MaterialProperty m_Startprop;
        private MaterialProperty m_Endprop;

        public RangeSliderDrawer(string startName, string endName) : this(startName, endName, "true") { }
        public RangeSliderDrawer(string startName, string endName, string hideChildProp)
        {
            m_StartPropName = startName;
            m_EndPropName = endName;
            m_hideChildProp = hideChildProp.Equals("true");
        }

        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            if (prop.type != MaterialProperty.PropType.Range)
            {
                Debug.LogError("Property " + prop.name + " type should be \"Range\" ");
                return;
            }

            // Get prop
            Material mat = editor.target as Material;
            UnityEngine.Object[] objects = { mat };
            m_Startprop = MaterialEditor.GetMaterialProperty(objects, m_StartPropName);
            m_Endprop = MaterialEditor.GetMaterialProperty(objects, m_EndPropName);

            if (m_Startprop == null || m_Endprop == null)
            {
                Debug.LogError("Range Slider property " + m_StartPropName + " or " + m_EndPropName + " not found!");
                return;
            }
            if (m_hideChildProp)
            {
                DanbaidongGUI customGUI = (DanbaidongGUI)editor.customShaderGUI;
                customGUI.SetPropHideFlag(m_StartPropName, true);
                customGUI.SetPropHideFlag(m_EndPropName, true);
            }

            float startVal = m_Startprop.floatValue;
            float endVal = m_Endprop.floatValue;

            MaterialEditor.BeginProperty(rect, prop);
            EditorGUI.BeginChangeCheck();

            label.text += " (" + prop.rangeLimits.x + "," + prop.rangeLimits.y + ")";
            DrawerHelper.DrawRangeSlider(rect, label, ref startVal, ref endVal, prop.rangeLimits.x, prop.rangeLimits.y);

            if (EditorGUI.EndChangeCheck())
            {
                startVal = Mathf.Clamp(startVal, prop.rangeLimits.x, prop.rangeLimits.y);
                endVal = Mathf.Clamp(endVal, prop.rangeLimits.x, prop.rangeLimits.y);
                startVal = Mathf.Min(startVal, m_Endprop.floatValue);
                endVal = Mathf.Max(endVal, m_Startprop.floatValue);
                m_Startprop.floatValue = startVal;
                m_Endprop.floatValue = endVal;
            }

            MaterialEditor.EndProperty();
        }
    }

    /// <summary>
    /// Only one Key Avaliabe, max 8 values
    /// #pragma shader_feature_local _ _KEY1 _KEY2 _KEY3...
    /// </summary>
    public class KeysEnumDrawer : MaterialPropertyDrawer
    {
        private static readonly int s_MaxKeywordsNum = 8;
        private GUIContent[] m_GUINames;
        private string[] m_Keywords = new string[s_MaxKeywordsNum];
        private int m_CurKeywordsNum = 0;

        #region Construct
        public KeysEnumDrawer(string key1)
            : this(key1, null, null, null, null, null, null, null) { }
        public KeysEnumDrawer(string key1, string key2)
            : this(key1, key2, null, null, null, null, null, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3)
            : this(key1, key2, key3, null, null, null, null, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3, string key4)
            : this(key1, key2, key3, key4, null, null, null, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3, string key4, string key5)
            : this(key1, key2, key3, key4, key5, null, null, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3, string key4, string key5, string key6)
            : this(key1, key2, key3, key4, key5, key6, null, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3, string key4, string key5, string key6, string key7)
            : this(key1, key2, key3, key4, key5, key6, key7, null) { }
        public KeysEnumDrawer(string key1, string key2, string key3, string key4, string key5, string key6, string key7, string key8)
        {
            m_Keywords[0] = key1;
            m_Keywords[1] = key2;
            m_Keywords[2] = key3;
            m_Keywords[3] = key4;
            m_Keywords[4] = key5;
            m_Keywords[5] = key6;
            m_Keywords[6] = key7;
            m_Keywords[7] = key8;

            for (int i = 0; i < s_MaxKeywordsNum; i++)
            {
                if (String.IsNullOrEmpty(m_Keywords[i]))
                {
                    m_CurKeywordsNum = i;
                    break;
                }
            }
            m_GUINames = new GUIContent[m_CurKeywordsNum + 1];
            m_GUINames[0] = new GUIContent("None");
            for (int i = 1; i < m_CurKeywordsNum + 1; i++)
            {
                m_GUINames[i] = new GUIContent(m_Keywords[i - 1]);
            }

        }
        #endregion

        private int TransformFloatToGUINameIndex(float floatVal)
        {
            return (int)floatVal + 1;
        }
        private float TransformGUINameIndexToFloat(int index)
        {
            return (float)index - 1;
        }
        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            MaterialEditor.BeginProperty(rect, prop);
            EditorGUI.BeginChangeCheck();
            //DrawerHelper.DrawTitle(rect, m_TitleStr);

            int index = TransformFloatToGUINameIndex(prop.floatValue);
            DrawerHelper.AdaptiveFieldWidth(EditorStyles.popup, m_GUINames[index], EditorStyles.popup.lineHeight);
            int newIndex = EditorGUI.Popup(rect, label, index, m_GUINames);

            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);

                prop.floatValue = TransformGUINameIndexToFloat(newIndex);
                for (int i = 0; i < m_CurKeywordsNum; i++)
                {
                    DrawerHelper.SetShaderKeyWord(editor.targets, m_Keywords[i], i == prop.floatValue);
                }

            }
            MaterialEditor.EndProperty();
        }

    }


    public class TitleDecorator : MaterialPropertyDrawer
    {
        private string m_TitleStr;
        private float m_Height;

        public static readonly float s_DefaultHeight = EditorGUIUtility.singleLineHeight + 6f;

        public TitleDecorator(string title) : this(title, s_DefaultHeight) { }
        public TitleDecorator(string title, float height)
        {
            m_TitleStr = title;
            m_Height = height;
        }

        public override void OnGUI(Rect rect, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            MaterialEditor.BeginProperty(rect, prop);
            DrawerHelper.DrawTitle(rect, m_TitleStr);
            MaterialEditor.EndProperty();
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return m_Height;
        }
    }
}
#endif