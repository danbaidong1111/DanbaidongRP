using System.Collections.Generic;
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

class NPRToonRayTracingTests
{
    const string Root = "Packages/com.unity.render-pipelines.danbaidong/";

    // Use the pipeline's actual buffer layout without duplicating its internal structs.
    static ComputeBuffer CreatePipelineBuffer(string name, Dictionary<string, object> fields, bool constant = false)
    {
        var assembly = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipeline).Assembly;
        var type = assembly.GetType("UnityEngine.Rendering.Universal.Internal." + name, true);
        var value = System.Activator.CreateInstance(type);
        foreach (var field in fields) type.GetField(field.Key).SetValue(value, field.Value);
        var array = System.Array.CreateInstance(type, 1);
        array.SetValue(value, 0);
        var buffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf(type),
            constant ? ComputeBufferType.Constant : ComputeBufferType.Structured);
        buffer.SetData(array);
        return buffer;
    }

    [UnityTest]
    public IEnumerator RaysUseNPRRampAndRespectAlphaClip()
    {
        foreach (string mode in new[] { "Base", "Hair", "Face" })
        {
            var test = CheckMode(mode);
            try { while (test.MoveNext()) yield return test.Current; }
            finally { (test as System.IDisposable).Dispose(); }
        }
    }

    [UnityTest]
    public IEnumerator RaysShadePunctualLightsWithAndWithoutAllocatedShadows()
    {
        foreach (string mode in new[] { "Base", "Hair", "Face" })
        foreach (int punctualCase in new[] { 1, 2 })
        {
            var test = CheckMode(mode, punctualCase);
            try { while (test.MoveNext()) yield return test.Current; }
            finally { (test as System.IDisposable).Dispose(); }
        }
    }

    static IEnumerator CheckMode(string mode, int punctualCase = 0)
    {
        if (!SystemInfo.supportsRayTracing) Assert.Ignore("Requires a DXR-capable editor.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Material/NPRToon/NPRToonBase.shader");
        var rayShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(Root + "Tests/Editor/NPRToonRegression.raytrace");
        var visibilityShader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(Root + "Tests/Editor/NPRToonVisibilityRegression.raytrace");
        Assert.IsNotNull(shader);
        Assert.IsNotNull(rayShader);
        Assert.IsNotNull(visibilityShader);
        var material = new Material(shader);
        var mesh = new Mesh();
        var ramp = new Texture2D(2, 10, TextureFormat.RGBAFloat, false, true);
        var baseMap = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var hitMaterials = new List<Material>();
        bool allowAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        try
        {
            // A freshly imported hit group must finish compiling before validating its GPU output.
            ShaderUtil.allowAsyncCompilation = false;
            mesh.vertices = new[] { new Vector3(-2, -2, 0), new Vector3(0, 2, 0), new Vector3(2, -2, 0) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            mesh.uv = new[] { Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f };
            mesh.uv2 = mesh.uv;
            mesh.triangles = new[] { 0, 1, 2 };
            for (int y = 0; y < 10; ++y)
                for (int x = 0; x < 2; ++x)
                    ramp.SetPixel(x, y, new Color(0.05f * y, 0.1f, 0.2f, 1));
            ramp.Apply();
            baseMap.SetPixel(0, 0, new Color(1, 1, 1, 0.5f));
            baseMap.Apply();
            material.shaderKeywords = new[] { "_SHADINGMODE_" + mode.ToUpperInvariant(), "_SHADOW_RAMP", "_ALPHATEST_ON" };
            if (punctualCase == 2) material.EnableKeyword("_ADDITIONAL_LIGHT_SHADOWS");
            material.SetFloat("_ShadingMode", mode == "Face" ? 2 : (mode == "Hair" ? 1 : 0));
            material.SetFloat("_UseShadowRamp", 1);
            material.SetFloat("_AlphaClip", 1);
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_LightMapTex", Texture2D.blackTexture); // Alpha 0 selects material 1.
            material.SetFloat("_LightMapAO", 1);
            material.SetFloat("_NormalScale", 0);
            material.SetTexture("_PackedShadowRamp", ramp);
            material.SetFloat("_UseGlobalDayTime", 0);
            material.SetFloat("_RampDayNight", 0);
            material.SetFloat("_ShadowStrength", 1);
            material.SetFloat("_MainLightColorLerp", 1);
            material.SetColor("_SelfLight", Color.white);
            material.SetFloat("_IndirDiffIntensity", 0);
            material.SetFloat("_EnvColorLerp", 1);
            material.SetColor("_EmissionCol", Color.clear);
            material.SetFloat("_FaceNoseSpecular", 0);
            material.SetTexture("_FaceLightMap", Texture2D.blackTexture);
            material.SetFloat("_FaceSDFChannel", 0);
            material.SetFloat("_FaceSDFInvert", 0);
            material.SetFloat("_FaceShadowMask", 0);
            material.SetFloat("_FaceMaterialID", 2);
            material.SetFloat("_FaceUsePackedRamp", 1);
            material.SetVector("_FaceFrontDirWS", new Vector4(0, 0, 1, 0));
            material.SetVector("_FaceRightDirWS", new Vector4(1, 0, 0, 0));

            // Compile the exact hit-group variants before constructing the acceleration structure.
            var subshader = ShaderUtil.GetShaderData(shader).GetSubshader(1);
            for (int pass = 0; pass < subshader.PassCount; ++pass)
            {
                var compiled = subshader.GetPass(pass).CompileVariant(UnityEditor.Rendering.ShaderType.RayTracing,
                    material.shaderKeywords, UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                Assert.IsTrue(compiled.Success, mode + " " + subshader.GetPass(pass).Name);
            }

            using (var lightList = CreatePipelineBuffer("ShaderVariablesLightList",
                new Dictionary<string, object> { { "_DirectionalLightCount", punctualCase == 0 ? 1u : 0u },
                    { "_EnvLightIndexShift", punctualCase == 0 ? 0u : 1u } }, true))
            using (var lights = CreatePipelineBuffer("DirectionalLightData", new Dictionary<string, object>
                { { "lightDirection", Vector3.back }, { "lightColor", Vector3.one } }))
            using (var punctual = CreatePipelineBuffer("GPULightData", new Dictionary<string, object>
                { { "lightPosWS", Vector3.back }, { "lightColor", new Vector3(0.4f, 0.6f, 0.8f) },
                    { "lightAttenuation", new Vector4(0, 0, 0, 1) }, { "baseContribution", 1f },
                    { "shadowType", punctualCase == 2 ? 2 : 0 }, { "shadowLightIndex", -1 } }))
            using (var ambient = new ComputeBuffer(7, 16))
            using (var output = new ComputeBuffer(1, 16))
            {
                ambient.SetData(new Vector4[7]);
                rayShader.SetConstantBuffer("ShaderVariablesLightList", lightList, 0, lightList.stride);
                rayShader.SetBuffer("g_DirectionalLightDatas", lights);
                rayShader.SetBuffer("g_GPULightDatas", punctual);
                rayShader.SetBuffer("_AmbientProbeData", ambient);
                rayShader.SetBuffer("_Results", output);
                foreach (bool clipped in new[] { false, true })
                {
                    // Equality must survive alpha clipping in both hit groups.
                    // Keep each hit material immutable after its first GPU dispatch.
                    var hitMaterial = new Material(material);
                    hitMaterials.Add(hitMaterial);
                    hitMaterial.SetFloat("_Cutoff", clipped ? 0.5001f : 0.5f);
                    using (var acceleration = new RayTracingAccelerationStructure())
                    {
                        var config = new RayTracingMeshInstanceConfig(mesh, 0, hitMaterial);
                        config.subMeshFlags = RayTracingSubMeshFlags.Enabled;
                        config.enableTriangleCulling = false;
                        acceleration.AddInstance(in config, Matrix4x4.identity);
                        acceleration.Build();
                        yield return null;
                        foreach (bool visibility in new[] { false, true })
                        {
                            var activeShader = visibility ? visibilityShader : rayShader;
                            activeShader.SetAccelerationStructure("_RaytracingAccelerationStructure", acceleration);
                            activeShader.SetBuffer("_Results", output);
                            using (var command = new CommandBuffer())
                            {
                                ShaderUtil.SetAsyncCompilation(command, false);
                                command.SetRayTracingShaderPass(activeShader, visibility ? "VisibilityDXR" : "IndirectDXR");
                                command.DispatchRays(activeShader, "TraceTest", 1, 1, 1);
                                ShaderUtil.RestoreAsyncCompilation(command);
                                Graphics.ExecuteCommandBuffer(command);
                            }
                            // Let the editor install freshly compiled hit-group variants before readback.
                            yield return null;
                            while (ShaderUtil.anythingCompiling) yield return null;
                            activeShader.SetShaderPass(visibility ? "VisibilityDXR" : "IndirectDXR");
                            activeShader.Dispatch("TraceTest", 1, 1, 1);
                            var result = new Vector4[1];
                            output.GetData(result);
                            var expected = clipped
                                ? (visibility ? new Vector4(1, 1, 1, -1) : new Vector4(0, 0, 0, -1))
                                : (visibility ? new Vector4(0, 0, 0, 1) : new Vector4(mode == "Face" ? 0.4f : 0.45f, 0.1f, 0.2f, 1));
                            if (punctualCase != 0 && !clipped && !visibility)
                                expected = Vector4.Scale(expected, new Vector4(0.4f, 0.6f, 0.8f, 1));
                            Assert.That(Vector4.Distance(result[0], expected), Is.LessThan(0.001f),
                                mode + " punctualCase=" + punctualCase + " visibility=" + visibility + " clipped=" + clipped + " actual=" + result[0]
                                + " keywords=" + string.Join(",", hitMaterial.shaderKeywords));
                        }
                    }
                }
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = allowAsyncCompilation;
            foreach (var hitMaterial in hitMaterials) Object.DestroyImmediate(hitMaterial);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(ramp);
            Object.DestroyImmediate(baseMap);
        }
    }
}
