using System;
using UnityEngine;

#if UNITY_EDITOR
namespace UnityEditor.DanbaidongGUI
{
    public class AdvancedMatProperty
    {
        public MaterialProperty prop;
        public bool hideInInspector;
    }
    public class DanbaidongGUI : ShaderGUI
    {
        public AdvancedMatProperty[] advProps;
        public MaterialEditor materialEditor;
        public Material material;
        public Shader shader;
        public bool m_FirstTimeApply = true;

        private enum FoldoutState
        {
            Folded,
            Expand,
            ExpandNotEdit
        }
        private FoldoutState m_FoldoutState = FoldoutState.Expand;
        private string m_FoldoutEndName = "";

        /// <summary>
        /// Constructor called: switch to a new Material Window 
        /// </summary>
        public DanbaidongGUI()
        {
        }

        /// <inheritdoc/>
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            if (materialEditor == null)
                throw new ArgumentNullException("materialEditorIn");

            this.materialEditor = materialEditor;
            Material material = materialEditor.target as Material;


            // Make sure that needed setup (ie keywords/renderqueue) are set up if we're switching some existing
            // material to a universal shader.
            if (m_FirstTimeApply)
            {
                DrawerHelper.InitGUIStyle();
                m_FirstTimeApply = false;
            }


            // Init variables
            SetFoldoutInit();
            advProps = new AdvancedMatProperty[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                advProps[i] = new AdvancedMatProperty();
                advProps[i].prop = props[i];
                advProps[i].hideInInspector = (props[i].flags & MaterialProperty.PropFlags.HideInInspector) != 0;
            }

            // Properties GUI
            materialEditor.SetDefaultGUIWidths();   //Default ShaderGUI
            //EditorGUIUtility.fieldWidth += 25;    //Tex field width
            EditorGUIUtility.labelWidth -= 10;      //Color field width

            for (int i = 0; i < advProps.Length; i++)
            {
                var prop = advProps[i].prop;
                bool hide = advProps[i].hideInInspector;

                // Visibility
                if (hide || m_FoldoutState == FoldoutState.Folded)
                {
                    // Folded prop until endName
                    if (prop.name != m_FoldoutEndName)
                        continue;
                }

                GUI.enabled = (m_FoldoutState == FoldoutState.ExpandNotEdit) ? false : true;

                var label = new GUIContent(prop.displayName);

                // Reset Area
                var height = materialEditor.GetPropertyHeight(prop, label.text);
                var rect = EditorGUILayout.GetControlRect(true, height);
                rect.xMin = 25;
                rect.xMax -= 18;


                materialEditor.ShaderProperty(rect, prop, label);
            }

            GUI.enabled = true;

            DrawerHelper.DrawSingleLine();
            materialEditor.RenderQueueField();
            materialEditor.EnableInstancingField();
            materialEditor.LightmapEmissionProperty();
            materialEditor.DoubleSidedGIField();
            EditorGUILayout.Space();
            
            DrawerHelper.DrawLabel();
        }


        /// <summary>
        /// We will set IsFolded true to skip props, until endName match (meanwhile IsFolded false).
        /// Note that embed foldout is not allowed, like begin1,begin2 without end1 before bgein2.
        /// </summary>
        public void SetFoldoutInit()
        {
            m_FoldoutState = FoldoutState.Expand;
            m_FoldoutEndName = "";
        }
        public void SetFoldoutBegin(bool isFolded, string endName)
        {
            if (!String.IsNullOrEmpty(m_FoldoutEndName))
            {
                Debug.LogError("Embed foldout is not allowed!");
            }

            m_FoldoutState = isFolded ? FoldoutState.Folded : FoldoutState.Expand;
            m_FoldoutEndName = endName;

            EditorGUI.indentLevel++;
        }
        public void SetFoldoutBegin(bool isFolded, string endName, bool toogleVal)
        {
            if (!String.IsNullOrEmpty(m_FoldoutEndName))
            {
                Debug.LogError("Embed foldout is not allowed!");
            }
            m_FoldoutState = isFolded ? FoldoutState.Folded : FoldoutState.Expand;

            m_FoldoutState = (!isFolded && !toogleVal) ? FoldoutState.ExpandNotEdit : m_FoldoutState;

            m_FoldoutEndName = endName;

            EditorGUI.indentLevel++;
        }
        public void SetFoldoutEnd()
        {
            m_FoldoutState = FoldoutState.Expand;
            m_FoldoutEndName = "";
            EditorGUI.indentLevel--;
        }
        public void SetPropHideFlag(string propName, bool hideInInspector)
        {
            if (advProps.Length < 0)
            {
                return;
            }
            for (int i = 0; i < advProps.Length; i++)
            {
                if (String.Equals(advProps[i].prop.name, propName))
                {
                    advProps[i].hideInInspector = hideInInspector;
                }
            }
        }
    } /* DanbaidongGUI */

} /* namespace UnityEditor.DanbaidongGUI */

#endif