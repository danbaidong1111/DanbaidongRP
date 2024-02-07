using System;
using System.Reflection;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Class containing Runtime shader and texture resources needed in URP.
    /// </summary>
    /// <seealso cref="Shader"/>
    /// <seealso cref="Material"/>
    public class UniversalRenderPipelineRuntimeResources : UniversalRenderPipelineResources
    {
        [Serializable, ReloadGroup]
        public sealed class ShaderResources
        {
            /// <summary>
            /// GPUCopy compute shader.
            /// </summary>
            [Reload("Shaders/Utils/GPUCopy.compute")]
            public ComputeShader copyChannelCS;

            /// <summary>
            /// DepthPyramid compute shader.
            /// </summary>
            [Reload("Shaders/Utils/DepthPyramid.compute")]
            public ComputeShader depthPyramidCS;

            /// <summary>
            /// ColorPyramid pixel shader.Current we use computeshader instead.
            /// </summary>
            [Reload("Shaders/Utils/ColorPyramid.shader")]
            public Shader colorPyramid;

            /// <summary>
            /// ColorPyramid compute shader.
            /// </summary>
            [Reload("Shaders/Utils/ColorPyramid.compute")]
            public ComputeShader colorPyramidCS;

            /// <summary>
            /// Screen Space Reflections compute shader.
            /// </summary>
            [Reload("Shaders/ScreenSpaceLighting/ScreenSpaceReflections.compute")]
            public ComputeShader screenSpaceReflectionsCS;

#if UNITY_EDITOR
            // Iterator to retrieve all compute shaders in reflection so we don't have to keep a list of
            // used compute shaders up to date (prefer editor-only usage)
            public IEnumerable<ComputeShader> GetAllComputeShaders()
            {
                var fields = typeof(ShaderResources).GetFields(BindingFlags.Public | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if (field.GetValue(this) is ComputeShader computeShader)
                        yield return computeShader;
                }
            }

#endif
        }

        [Serializable, ReloadGroup]
        public sealed class MaterialResources
        {
            /// <summary>
            /// Lit material.
            /// </summary>
            [Reload("Runtime/Materials/Lit.mat")]
            public Material lit;

        }

        [Serializable, ReloadGroup]
        public sealed class TextureResources
        {
            // Pre-baked STBN noise
            [Reload("Textures/STBN/vec1/stbn_vec1_2Dx1D_128x128x64_{0}.png", 0, 64)]
            public Texture2D[] blueNoise128RTex;
            [Reload("Textures/STBN/vec2/stbn_vec2_2Dx1D_128x128x64_{0}.png", 0, 64)]
            public Texture2D[] blueNoise128RGTex;
        }

        /// <summary>
        /// Shader resources used in URP.
        /// </summary>
        public ShaderResources shaders;

        /// <summary>
        /// Material resources used in URP.
        /// </summary>
        public MaterialResources materials;

        /// <summary>
        /// Texture resources used in URP.
        /// </summary>
        public TextureResources textures;
    }

}
