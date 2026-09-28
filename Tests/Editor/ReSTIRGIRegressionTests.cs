using NUnit.Framework;
using UnityEditor;
using UnityEngine;

class ReSTIRGIRegressionTests
{
    const int k_Count = 4096;
    const string k_ShaderPath = "Packages/com.unity.render-pipelines.danbaidong/Tests/Editor/ReSTIRGIRegression.compute";

    static ComputeShader LoadShader()
    {
        if (!SystemInfo.supportsComputeShaders)
            Assert.Ignore("ReSTIR GI regression tests require a GPU supporting compute.");
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_ShaderPath);
        Assert.IsNotNull(shader);
        return shader;
    }

    static void BindDirections(ComputeShader shader, int kernel)
    {
        var noise = UnityEngine.Rendering.Universal.BlueNoiseSystem.TryGetInstance();
        if (noise == null)
            Assert.Ignore("Initialize the render pipeline's STBN textures before GPU direction tests.");
        shader.SetTexture(kernel, "_STBNUnitVec3CosineTexture", noise.textureArrayUnitVec3Cosine);
        shader.SetInt("_STBNIndex", 0);
    }

    [Test]
    public void PackedSpatialMatchesReferenceIncludingBlackCandidates()
    {
        var shader = LoadShader();
        int kernel = shader.FindKernel("TestSpatialPacked");
        using var buffer = new ComputeBuffer(k_Count, 16);
        shader.SetBuffer(kernel, "_Results", buffer);
        shader.Dispatch(kernel, k_Count / 64, 1, 1);
        var results = new Vector4[k_Count];
        buffer.GetData(results);
        foreach (var error in results)
        {
            Assert.That(error.x, Is.LessThan(0.0001f));
            Assert.That(error.y, Is.LessThan(0.0001f));
            Assert.That(error.z, Is.LessThan(0.0001f));
            Assert.That(error.w, Is.Zero, "Packed streaming must preserve the reference sample count.");
        }
    }

    [Test]
    public void SelectionRandomIsHalfOpenAndPassesAreIndependent()
    {
        var shader = LoadShader();
        int kernel = shader.FindKernel("TestRandom");
        using var buffer = new ComputeBuffer(k_Count, 16);
        shader.SetBuffer(kernel, "_Results", buffer);
        shader.Dispatch(kernel, k_Count / 64, 1, 1);
        var results = new Vector4[k_Count];
        buffer.GetData(results);
        double mean = 0;
        int identical = 0;
        foreach (var sample in results)
        {
            for (int c = 0; c < 3; ++c)
                Assert.That(sample[c], Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            mean += sample.x;
            if (sample.x == sample.z) ++identical;
        }
        Assert.That(mean / k_Count, Is.EqualTo(0.5).Within(0.02));
        Assert.That(identical, Is.LessThan(4));
    }

    [Test]
    public void InitialRISPreservesEnergyAndBlackCandidateCounts()
    {
        var shader = LoadShader();
        int kernel = shader.FindKernel("TestInitialRIS");
        BindDirections(shader, kernel);
        using var buffer = new ComputeBuffer(k_Count, 16);
        shader.SetBuffer(kernel, "_Results", buffer);
        var results = new Vector4[k_Count];
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (int count in new[] { 1, 2, 4, 8 })
        {
            shader.SetInt("_CandidateCount", count);
            shader.SetInt("_TestMode", mode);
            shader.Dispatch(kernel, k_Count / 64, 1, 1);
            buffer.GetData(results);
            float expected = mode == 0 ? 1 : mode == 1 ? (count / 2) / (float)count : 0;
            foreach (var sample in results)
            {
                Assert.That(sample.w, Is.EqualTo(count), "Black candidates must increment M.");
                Assert.That(sample.x, Is.EqualTo(expected).Within(0.002));
                Assert.That(sample.y, Is.EqualTo(expected * 2).Within(0.004));
                Assert.That(sample.z, Is.EqualTo(expected * 4).Within(0.008));
            }
        }
    }

    [Test]
    public void MoreCandidatesReduceSmoothSignalVariance()
    {
        var shader = LoadShader();
        int kernel = shader.FindKernel("TestInitialRIS");
        BindDirections(shader, kernel);
        using var buffer = new ComputeBuffer(k_Count, 16);
        shader.SetBuffer(kernel, "_Results", buffer);
        shader.SetInt("_TestMode", 3);
        var results = new Vector4[k_Count];
        double previousVariance = double.MaxValue;
        foreach (int count in new[] { 1, 2, 4, 8 })
        {
            shader.SetInt("_CandidateCount", count);
            shader.Dispatch(kernel, k_Count / 64, 1, 1);
            buffer.GetData(results);
            double sum = 0, squaredSum = 0;
            foreach (var sample in results)
            {
                sum += sample.x;
                squaredSum += (double)sample.x * sample.x;
            }
            double mean = sum / k_Count;
            double variance = squaredSum / k_Count - mean * mean;
            Assert.That(mean, Is.EqualTo(0.6).Within(0.015));
            Assert.That(variance, Is.LessThan(previousVariance));
            previousVariance = variance;
        }
    }
}
