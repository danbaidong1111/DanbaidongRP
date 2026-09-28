using NUnit.Framework;
using UnityEditor;
using UnityEngine;

class NPRToonRegressionTests
{
    const string Root = "Packages/com.unity.render-pipelines.danbaidong/";

    static string[] WithMode(string[] keywords, string mode)
    {
        var result = new string[keywords.Length + 1];
        keywords.CopyTo(result, 0);
        result[keywords.Length] = "_SHADINGMODE_" + mode.ToUpperInvariant();
        return result;
    }

    [TestCase("Base")]
    [TestCase("Hair")]
    [TestCase("Face")]
    public void RayTracingPassesCompile(string name)
    {
        if (!SystemInfo.supportsRayTracing) Assert.Ignore("Requires a DXR-capable editor.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Material/NPRToon/NPRToonBase.shader");
        Assert.IsNotNull(shader);
        var subshader = ShaderUtil.GetShaderData(shader).GetSubshader(1);
        for (int pass = 0; pass < subshader.PassCount; ++pass)
        {
            foreach (var keywords in new[] { new string[0], new[] { "_ALPHATEST_ON", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOW_RAMP", "_MTSPEC_RAMP", "_ALLDIR_SDF", "_LIGHT_LAYERS", "_ADDITIONAL_LIGHT_SHADOWS", "_SHADOWS_SOFT" } })
            {
                var result = subshader.GetPass(pass).CompileVariant(UnityEditor.Rendering.ShaderType.RayTracing,
                    WithMode(keywords, name), UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                Assert.IsTrue(result.Success, name + " " + subshader.GetPass(pass).Name);
            }
        }
    }

    [Test]
    public void FaceSDFUsesSelectedUVAndAtlasHandlesPoles()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        var face = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        var atlas = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        try
        {
            face.SetPixels(new[] { new Color(0.2f, 0, 0, 1), new Color(0.8f, 0, 0, 1), new Color(0.2f, 0, 0, 1), new Color(0.8f, 0, 0, 1) });
            atlas.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
            face.Apply();
            atlas.Apply();
            shader.SetVector("_FaceRightDirWS", Vector3.right);
            shader.SetVector("_FaceFrontDirWS", Vector3.forward);
            shader.SetFloat("_FaceSDFChannel", 0);
            shader.SetFloat("_FaceSDFMirror", 0);
            shader.SetFloat("_FaceSDFInvert", 0);
            shader.SetFloat("_FaceShadowMask", 0);
            shader.SetFloat("_FaceShadowOffset", 0);
            shader.SetFloat("_FaceShadowSmooth", 0.001f);
            using (var input = new ComputeBuffer(4, 16))
            using (var output = new ComputeBuffer(4, 16))
            {
                input.SetData(new[] { new Vector4(1, 0, 0, 0), new Vector4(-1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0) });
                int kernel = shader.FindKernel("FaceSDFContract");
                shader.SetBuffer(kernel, "_Inputs", input);
                shader.SetBuffer(kernel, "_Results", output);
                shader.SetTexture(kernel, "_FaceLightMap", face);
                var result = new Vector4[4];
                shader.SetFloat("_FaceUV", 0);
                shader.Dispatch(kernel, 4, 1, 1);
                output.GetData(result);
                Assert.AreEqual(1, result[0].x);
                Assert.AreEqual(0, result[1].x);
                shader.SetFloat("_FaceUV", 1);
                shader.Dispatch(kernel, 4, 1, 1);
                output.GetData(result);
                Assert.AreEqual(0, result[0].x);
                Assert.AreEqual(1, result[1].x);

                kernel = shader.FindKernel("FaceAtlasContract");
                shader.SetBuffer(kernel, "_Inputs", input);
                shader.SetBuffer(kernel, "_Results", output);
                shader.SetTexture(kernel, "_FaceLightMap", face);
                shader.SetTexture(kernel, "_AllDirSDFMap", atlas);
                shader.SetVector("_AllDirSDFMap_TexelSize", new Vector4(0.5f, 0.5f, 2, 2));
                shader.SetFloat("_FaceSDFInvert", 1);
                foreach (float tiles in new[] { 0f, 1f, 9f })
                {
                    shader.SetFloat("_AllDirSDFMapTileSize", tiles);
                    shader.Dispatch(kernel, 4, 1, 1);
                    output.GetData(result);
                    foreach (var value in result) Assert.That(value.x, Is.InRange(0f, 1f));
                    Assert.That(result[2].x, Is.EqualTo(0.5f).Within(0.0001f));
                    Assert.AreEqual(1, result[3].x);
                }
            }
        }
        finally { Object.DestroyImmediate(face); Object.DestroyImmediate(atlas); }
    }

    [TestCase("Base")]
    [TestCase("Hair")]
    [TestCase("Face")]
    public void RasterPassesCompileWithCharacterFeatures(string name)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "Shaders/Material/NPRToon/NPRToonBase.shader");
        Assert.IsNotNull(shader);
        var material = new Material(shader);
        try
        {
            foreach (var pass in new[] { "GBufferBase", "CharacterForward", "CharacterOutline", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Assert.That(material.FindPass(pass), Is.GreaterThanOrEqualTo(0), pass);
            foreach (var keywords in new[]
            {
                new string[0],
                new[] { "_ALPHATEST_ON", "_SHADOW_RAMP", "_MTSPEC_RAMP", "_ALLDIR_SDF", "FLAG_EYELASH" },
                new[] { "_ALPHATEST_ON", "_SHADOW_RAMP", "_GPU_LIGHTS_CLUSTER", "_LIGHT_LAYERS", "_SELF_SHADOW", "_PEROBJECT_SCREEN_SPACE_SHADOW", "_MAIN_LIGHT_SHADOWS_SCREEN", "_ADDITIONAL_LIGHT_SHADOWS", "_SHADOWS_SOFT", "_GBUFFER_NORMALS_OCT", "_WRITE_RENDERING_LAYERS" },
                new[] { "_GPU_LIGHTS_CLUSTER", "_RAYTRACING_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "LOD_FADE_CROSSFADE", "FLAG_HAIRSHADOW" }
            })
            {
                material.shaderKeywords = WithMode(keywords, name);
                for (int pass = 0; pass < material.passCount; ++pass)
                    ShaderUtil.CompilePass(material, pass, true);
            }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.severity, message.message);
        }
        finally { Object.DestroyImmediate(material); }
    }

    [Test]
    public void SharedOutlineFragmentKeepsOutlineColorAndGeometricNormal()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        int kernel = shader.FindKernel("OutlineCompositionContract");
        shader.SetVector("_BaseColor", Vector4.one);
        shader.SetVector("_OutlineColor3", new Vector4(0.8f, 0.2f, 0.1f, 0.5f));
        shader.SetFloat("_IndirDiffIntensity", 2);
        shader.SetFloat("_EnvColorLerp", 1);
        shader.SetVector("_SelfEnvColor", Vector4.one);
        shader.SetVector("_EmissionCol", Vector4.one * 10);
        shader.SetFloat("_NormalScale", 1);
        using (var output = new ComputeBuffer(2, 16))
        {
            shader.SetBuffer(kernel, "_Results", output);
            shader.Dispatch(kernel, 1, 1, 1);
            var result = new Vector4[2];
            output.GetData(result);
            Assert.That(Vector4.Distance(result[0], new Vector4(0.53f, 0.34f, 0.4f, 1)), Is.LessThan(0.0001f));
            Assert.AreEqual(new Vector4(0, 0, 1, 1), result[1]);
        }
    }

    [Test]
    public void SubMaterialParametersFollowPixelIDAndFaceOverride()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        for (int i = 0; i < 5; ++i)
        {
            shader.SetFloat("_SpecPow" + i, 8f * (i + 1));
            shader.SetFloat("_SpecMul" + i, 0.25f * (i + 1));
            shader.SetVector("_OutlineColor" + i, new Vector4(i / 5f, 0.25f, 0.75f, 0.5f));
        }
        shader.SetVector("_SpecColor", new Vector4(0.75f, 0.5f, 0.25f, 1));
        shader.SetFloat("_FaceMaterialID", 4);
        shader.SetFloat("_FaceRampRow", 0.35f);
        using (var input = new ComputeBuffer(5, 16))
        using (var output = new ComputeBuffer(15, 16))
        {
            input.SetData(new[] { new Vector4(0.1f, 0, 0, 0), new Vector4(0.3f, 0, 0, 0),
                new Vector4(0.5f, 0, 0, 0), new Vector4(0.7f, 0, 0, 0), new Vector4(0.9f, 0, 0, 0) });
            var ids = new[] { 1, 4, 3, 5, 2 };
            foreach (bool face in new[] { false, true })
            {
                int kernel = shader.FindKernel(face ? "FaceSubMaterialContract" : "SubMaterialContract");
                shader.SetBuffer(kernel, "_Inputs", input);
                shader.SetBuffer(kernel, "_Results", output);
                foreach (bool packed in new[] { false, true })
                {
                    shader.SetFloat("_FaceUsePackedRamp", packed ? 1 : 0);
                    shader.Dispatch(kernel, 5, 1, 1);
                    var result = new Vector4[15];
                    output.GetData(result);
                    for (int i = 0; i < 5; ++i)
                    {
                        int materialID = face ? 4 : ids[i];
                        Assert.AreEqual(new Vector4(materialID, 8f * materialID, 0.25f * materialID,
                            0.75f), result[i * 3]);
                        Assert.That(Vector4.Distance(new Vector4((materialID - 1) / 5f, 0.25f, 0.75f, 0.5f),
                            result[i * 3 + 1]), Is.LessThan(0.0001f));
                        float day = face && !packed ? 0.35f : 1.05f - 0.1f * materialID;
                        float night = face && !packed ? 0.35f : day - 0.5f;
                        Assert.That(result[i * 3 + 2].x, Is.EqualTo(day).Within(0.0001f));
                        Assert.That(result[i * 3 + 2].y, Is.EqualTo(night).Within(0.0001f));
                    }
                }
            }
        }
    }

    [Test]
    public void MetalSelectionAndSpecularChannelsRemainValidOnGPU()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        int kernel = shader.FindKernel("MetalLightMapContract");
        shader.SetFloat("_ShadowStrength", 0);
        shader.SetFloat("_LightMapAO", 0);
        shader.SetFloat("_LightArea", 0.5f);
        shader.SetFloat("_ShadowRampWidth", 1);
        shader.SetFloat("_SpecSoftness", 0.01f);
        shader.SetFloat("_SpecPow0", 1);
        shader.SetFloat("_SpecMul0", 1);
        shader.SetVector("_SpecColor", Vector4.one);
        shader.SetFloat("_MTMapScale", 1);
        shader.SetFloat("_MTMapMul", 1);
        shader.SetFloat("_MTSpecPow", 4);
        shader.SetFloat("_MTSpecMul", 1);
        shader.SetFloat("_MTTopLayerRange", 1);
        shader.SetVector("_MTSpecColor", Vector4.one);
        shader.SetVector("_MTTopLayerColor", Vector4.one);
        shader.SetVector("_PackedShadowRamp_TexelSize", new Vector4(1, 1, 1, 1));
        shader.SetTexture(kernel, "_MTMap", Texture2D.whiteTexture);
        shader.SetTexture(kernel, "_PackedShadowRamp", Texture2D.whiteTexture);
        using (var input = new ComputeBuffer(4, 16))
        using (var output = new ComputeBuffer(4, 16))
        {
            input.SetData(new[] { new Vector4(0.9f, 0.5f, 0.5f, 0), new Vector4(0.9001f, 0.5f, 0.25f, 0),
                new Vector4(0.95f, 0.5f, 0.75f, 0), new Vector4(0.9001f, 0.5f, 0, 0) });
            shader.SetBuffer(kernel, "_Inputs", input);
            shader.SetBuffer(kernel, "_Results", output);
            shader.Dispatch(kernel, 4, 1, 1);
            var result = new Vector4[4];
            output.GetData(result);
            // R == 0.9 stays non-metal; R > 0.9 switches to metal. B scales metal specular.
            Assert.That(result[0].x, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(result[1].x, Is.EqualTo(0.0625f).Within(0.0001f));
            Assert.That(result[2].x, Is.EqualTo(0.1875f).Within(0.0001f));
            Assert.That(result[3].x, Is.EqualTo(0).Within(0.0001f));
        }
    }

    [Test]
    public void LightMapBoundariesAndZeroWidthRemainValidOnGPU()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        // Check both sides of every threshold and the authored always-dark / always-lit values.
        float[] alpha = { 0, 0.1999f, 0.2f, 0.3999f, 0.4f, 0.5999f, 0.6f, 0.7999f, 0.8f, 1, 0, 0, 0, 0, 0, 0 };
        int[] ids = { 1, 1, 4, 4, 3, 3, 5, 5, 2, 2, 1, 1, 1, 1, 1, 1 };
        var inputs = new Vector4[16];
        for (int i = 0; i < 16; ++i) inputs[i] = new Vector4(alpha[i], 0.5f, 0.5f, 1);
        inputs[10] = new Vector4(0, 1, 0, 0);
        inputs[11] = new Vector4(0, 0, 1, 0);
        inputs[12] = new Vector4(0, 1, 0.05f, 0);
        inputs[13] = new Vector4(0, 0, 0.95f, 0);
        inputs[14] = new Vector4(0, 0, 0, 0);
        inputs[15] = new Vector4(0, 1, 1, 0);
        using (var input = new ComputeBuffer(16, 16))
        using (var output = new ComputeBuffer(32, 16))
        {
            int kernel = shader.FindKernel("LightMapContract");
            input.SetData(inputs);
            shader.SetBuffer(kernel, "_Inputs", input);
            shader.SetBuffer(kernel, "_Results", output);
            shader.Dispatch(kernel, 16, 1, 1);
            var result = new Vector4[32];
            output.GetData(result);
            for (int i = 0; i < 16; ++i)
            {
                Assert.AreEqual(ids[i], result[i].x, "Material ID at alpha=" + alpha[i]);
                Assert.That(result[i].y, Is.EqualTo(1.05f - 0.1f * ids[i]).Within(0.00001f));
                Assert.That(result[i].y - result[i].z, Is.EqualTo(0.5f).Within(0.00001f));
                Assert.That(result[i + 16].x, Is.InRange(0f, 1f));
                Assert.That(result[i + 16].y, Is.InRange(0f, 1f));
            }
            Assert.AreEqual(0, result[10].w);
            Assert.AreEqual(1, result[11].w);
            Assert.AreEqual(0, result[12].w);
            Assert.AreEqual(1, result[13].w);
        }
    }

    [Test]
    public void PackedRampBlendsColorsWithoutCrossingMaterialRows()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        Assert.IsNotNull(shader);
        var ramp = new Texture2D(2, 10, TextureFormat.RGBAFloat, false, true);
        try
        {
            for (int y = 0; y < 10; ++y)
                for (int x = 0; x < 2; ++x)
                    ramp.SetPixel(x, y, new Color(y / 10f, 1f - y / 10f, y % 2, 1));
            ramp.Apply();
            using (var output = new ComputeBuffer(10, 16))
            {
                int kernel = shader.FindKernel("PackedRampContract");
                shader.SetBuffer(kernel, "_Results", output);
                shader.SetTexture(kernel, "_PackedShadowRamp", ramp);
                shader.SetVector("_PackedShadowRamp_TexelSize", new Vector4(0.5f, 0.1f, 2, 10));
                shader.SetFloat("_UseGlobalDayTime", 0);
                foreach (float night in new[] { 0f, 0.5f, 1f })
                {
                    shader.SetFloat("_RampDayNight", night);
                    shader.Dispatch(kernel, 5, 1, 1);
                    var result = new Vector4[10];
                    output.GetData(result);
                    for (int i = 0; i < 5; ++i)
                    {
                        var expected = Color.Lerp(ramp.GetPixel(0, 9 - i), ramp.GetPixel(0, 4 - i), night);
                        Assert.That(Vector4.Distance(result[i], expected), Is.LessThan(0.0001f));
                        Assert.That(Vector4.Distance(result[i + 5], Vector4.one), Is.LessThan(0.0001f));
                    }
                }
            }
        }
        finally { Object.DestroyImmediate(ramp); }
    }
    [Test]
    public void DirectLightWeightsAndIndirectTintRemainIndependent()
    {
        if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires compute shader support.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "Tests/Editor/NPRToonRegression.compute");
        int kernel = shader.FindKernel("DirectAndIndirectContract");
        var ramp = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            ramp.SetPixel(0, 0, new Color(0.2f, 0.4f, 0.6f, 1));
            ramp.Apply();
            shader.SetTexture(kernel, "_PackedShadowRamp", ramp);
            shader.SetVector("_PackedShadowRamp_TexelSize", Vector4.one);
            shader.SetFloat("_ShadowStrength", 1);
            shader.SetFloat("_LightArea", 0.5f);
            shader.SetFloat("_ShadowSmooth", 0.01f);
            shader.SetFloat("_ShadowRampWidth", 1);
            shader.SetFloat("_SpecSoftness", 0.01f);
            shader.SetFloat("_IndirDiffUpDirSH", 0);
            shader.SetFloat("_EnvColorLerp", 1);
            shader.SetVector("_SelfEnvColor", new Vector4(0.2f, 0.4f, 0.6f, 1));
            shader.SetFloat("_IndirDiffIntensity", 2);
            using (var ambient = new ComputeBuffer(7, 16))
            using (var output = new ComputeBuffer(12, 16))
            {
                ambient.SetData(new Vector4[7]);
                shader.SetBuffer(kernel, "_AmbientProbeData", ambient);
                shader.SetBuffer(kernel, "_Results", output);
                shader.Dispatch(kernel, 4, 1, 1);
                var result = new Vector4[12];
                output.GetData(result);
                var diffuse = new[] { new Vector4(0.2f, 0.3f, 1.8f, 0), new Vector4(0.1f, 0.075f, 0.3f, 0),
                    Vector4.zero, new Vector4(1, 0.75f, 3, 0) };
                var specular = new[] { Vector4.zero, new Vector4(0.1f, 0.15f, 0.2f, 0), Vector4.zero, new Vector4(1, 1.5f, 2, 0) };
                for (int i = 0; i < 4; ++i)
                {
                    Assert.That(Vector4.Distance(result[i * 3], diffuse[i]), Is.LessThan(0.0001f), "Direct diffuse " + i);
                    Assert.That(Vector4.Distance(result[i * 3 + 1], specular[i]), Is.LessThan(0.0001f), "Direct specular " + i);
                    Assert.That(Vector4.Distance(result[i * 3 + 2], new Vector4(0.1f, 0.1f, 0.45f, 0)),
                        Is.LessThan(0.0001f), "Indirect must not inherit the current light " + i);
                }
            }
        }
        finally { Object.DestroyImmediate(ramp); }
    }

}
