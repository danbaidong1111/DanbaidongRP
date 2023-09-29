using System.IO;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR

namespace UnityEditor.DanbaidongGUI
{
    public class GradientSerializeHelper : ScriptableObject
    {
        public Gradient gradient;
        public GradientSerializeHelper(Gradient g)
        {
            gradient = g;
        }
    }

    /// <summary>
    /// Class GradientsRamp: gradients attached to texture, serialized together
    /// Edit and save to texture meta file
    /// </summary>
    [System.Serializable]
    public class GradientsRamp
    {
        public Texture2D rampTexture;
        public List<Gradient> gradients;

        public Vector2Int singleRampSize;
        public int singleRampPixelHeight = 2;

        private string m_TexPath;
        private AssetImporter m_TexImporter;
        private static string s_JSONSpliterChar = "#";

        public GradientsRamp() { }
        public GradientsRamp(Texture2D rampTex)
        {
            LoadGradientRamp(rampTex);
        }

        public bool LoadGradientRamp(Texture2D rampTex)
        {
            this.rampTexture = rampTex;
            this.m_TexPath = AssetDatabase.GetAssetPath(rampTex);
            InitDataFromFile(m_TexPath);

            this.singleRampSize = new Vector2Int(rampTex.width, gradients.Count > 0 ? rampTex.height / gradients.Count : 2);

            return true;
        }

        public bool SaveRampData(bool writeTexture)
        {
            if (m_TexImporter == null || string.IsNullOrEmpty(m_TexPath) || !HasGradientData())
            {
                return false;
            }

            if (writeTexture)
            {
                // TexImporter null check so m_TexPath is correct path.
                Texture2D resultRamp = GradientsListToRampTexture(gradients, this.singleRampSize.x, this.singleRampSize.y);
                File.WriteAllBytes(m_TexPath, resultRamp.EncodeToPNG());
            }

            // Importer setting && write gradients data to userData
            ((TextureImporter)m_TexImporter).mipmapEnabled = false;
            ((TextureImporter)m_TexImporter).textureCompression = TextureImporterCompression.Uncompressed;
            ((TextureImporter)m_TexImporter).wrapMode = TextureWrapMode.Clamp;
            ((TextureImporter)m_TexImporter).filterMode = FilterMode.Point;
            m_TexImporter.userData = GradientsListToJSON(gradients);
            m_TexImporter.SaveAndReimport();

            return true;
        }

        private void InitDataFromFile(string path)
        {
            m_TexImporter = AssetImporter.GetAtPath(path);

            if (m_TexImporter != null)
            {
                if (gradients != null)
                {
                    gradients.Clear();
                }
                gradients = JSONToGradientsList(m_TexImporter.userData);
            }
        }

        /// <summary>
        /// HasGradientData will only check gradients list, should call LoadGradientRamp Before
        /// </summary>
        public bool HasGradientData()
        {
            return gradients != null && gradients.Count > 0;
        }
        public static Gradient CreateSampleGradient()
        {
            Gradient gradient = new Gradient();
            GradientColorKey[] colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(new Color(1.0f, 0.5f, 0.0f), 0.0f),
                new GradientColorKey(Color.yellow, 0.25f),
                new GradientColorKey(Color.green, 0.5f),
                new GradientColorKey(Color.cyan, 0.75f),
            };
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f)
            };
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }
        private static Texture2D GradientsListToRampTexture(List<Gradient> gradients, int rampWidth, int rampHeight)
        {
            if (gradients.Count <= 0 || rampWidth <= 0 || rampHeight <= 0)
            {
                Debug.LogError("Gradient list num:" + gradients.Count + ", rampWidth:" + rampWidth + ", rampHeight:" + rampHeight);
                return null;
            }
            Texture2D rampTexture = new Texture2D(rampWidth, rampHeight * gradients.Count, TextureFormat.RGBA32, false);

            for (int x = 0; x < rampWidth; x++)
            {
                for (int gIndex = 0; gIndex < gradients.Count; gIndex++)
                {
                    int heightOffset = (gradients.Count - 1 - gIndex) * rampHeight;
                    Color color = gradients[gIndex].Evaluate((float)x / (rampWidth - 1));
                    for (int y = 0; y < rampHeight; y++)
                    {
                        rampTexture.SetPixel(x, y + heightOffset, color);
                    }
                }
            }

            rampTexture.Apply();
            return rampTexture;
        }
        private static string GradientsListToJSON(List<Gradient> gradients)
        {
            string[] gradientJSONArray = new string[gradients.Count];
            for (int i = 0; i < gradientJSONArray.Length; i++)
            {
                Gradient gradient = gradients[i];
                gradientJSONArray[i] = GradientToJSON(gradient);
            }
            return string.Join(s_JSONSpliterChar, gradientJSONArray);
        }
        private static List<Gradient> JSONToGradientsList(string gradientsJSON)
        {
            List<Gradient> gradients = new List<Gradient>();
            string[] gradienJSONArray = gradientsJSON.Split(s_JSONSpliterChar);

            for (int i = 0; i < gradienJSONArray.Length; i++)
            {
                string gradientJSON = gradienJSONArray[i];
                if (string.IsNullOrEmpty(gradientJSON))
                    continue;
                Gradient gradient = JSONToGradient(gradientJSON);
                if (gradient != null)
                {
                    gradients.Add(gradient);
                }
            }

            return gradients;
        }
        private static string GradientToJSON(Gradient gradient)
        {
            GradientSerializeHelper helper = ScriptableObject.CreateInstance<GradientSerializeHelper>();
            helper.gradient = gradient;
            return EditorJsonUtility.ToJson(helper);
        }
        private static Gradient JSONToGradient(string gradientJSON)
        {
            Gradient gradient = new Gradient();
            GradientSerializeHelper helper = ScriptableObject.CreateInstance<GradientSerializeHelper>();
            helper.gradient = gradient;
            EditorJsonUtility.FromJsonOverwrite(gradientJSON, helper);
            return helper.gradient;
        }
    }

} /* namespace UnityEditor.DanbaidongGUI */

#endif