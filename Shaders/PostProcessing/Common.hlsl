#ifndef UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED
#define UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Utils/Fullscreen.hlsl"

// ----------------------------------------------------------------------------------
// Render fullscreen mesh by using a matrix set directly by the pipeline instead of
// relying on the matrix set by the C++ engine to avoid issues with XR

float4x4 _FullscreenProjMat;

float4 TransformFullscreenMesh(half3 positionOS)
{
    return mul(_FullscreenProjMat, half4(positionOS, 1));
}

Varyings VertFullscreenMesh(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if _USE_DRAW_PROCEDURAL
    GetProceduralQuad(input.vertexID, output.positionCS, output.uv);
#else
    output.positionCS = TransformFullscreenMesh(input.positionOS.xyz);
    output.uv = input.uv;
#endif

    return output;
}

// ----------------------------------------------------------------------------------
// Samplers

SAMPLER(sampler_LinearClamp);
SAMPLER(sampler_LinearRepeat);
SAMPLER(sampler_PointClamp);
SAMPLER(sampler_PointRepeat);

// ----------------------------------------------------------------------------------
// Utility functions

half GetLuminance(half3 colorLinear)
{
#if _TONEMAP_ACES
    return AcesLuminance(colorLinear);
#else
    return Luminance(colorLinear);
#endif
}

// ----------------------------------------------------------------------------------
// Shared functions for uber & fast path (on-tile)
// These should only process an input color, don't sample in neighbor pixels!

half3 ApplyVignette(half3 input, float2 uv, float2 center, float intensity, float roundness, float smoothness, half3 color)
{
    center = UnityStereoTransformScreenSpaceTex(center);
    float2 dist = abs(uv - center) * intensity;

#if defined(UNITY_SINGLE_PASS_STEREO)
    dist.x /= unity_StereoScaleOffset[unity_StereoEyeIndex].x;
#endif

    dist.x *= roundness;
    float vfactor = pow(saturate(1.0 - dot(dist, dist)), smoothness);
    return input * lerp(color, (1.0).xxx, vfactor);
}

//画面变白，饱和度有下降
half3 MyAcesTonemap(float3 aces)
{
#if TONEMAPPING_USE_FULL_ACES

    float3 oces = RRT(aces);
    float3 odt = ODT_RGBmonitor_100nits_dim(oces);
    return odt;

#else

    // --- Glow module --- //
    float saturation = rgb_2_saturation(aces);
    float ycIn = rgb_2_yc(aces);
    float s = sigmoid_shaper((saturation - 0.4) / 0.2);
    float addedGlow = 1.0 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
    aces *= addedGlow;

    // --- Red modifier --- //
    float hue = rgb_2_hue(aces);
    float centeredHue = center_hue(hue, RRT_RED_HUE);
    float hueWeight;
    {
        //hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);
        hueWeight = smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH));
        hueWeight *= hueWeight;
    }

    aces.r += hueWeight * saturation * (RRT_RED_PIVOT - aces.r) * (1.0 - RRT_RED_SCALE);

    // --- ACES to RGB rendering space --- //
    float3 acescg = max(0.0, ACES_to_ACEScg(aces));

    // --- Global desaturation --- //
    //acescg = mul(RRT_SAT_MAT, acescg);
    acescg = lerp(dot(acescg, AP1_RGB2Y).xxx, acescg, RRT_SAT_FACTOR.xxx);

    // Luminance fitting of *RRT.a1.0.3 + ODT.Academy.RGBmonitor_100nits_dim.a1.0.3*.
    // https://github.com/colour-science/colour-unity/blob/master/Assets/Colour/Notebooks/CIECAM02_Unity.ipynb
    // RMSE: 0.0012846272106
#if defined(SHADER_API_SWITCH) // Fix floating point overflow on extremely large values.
    const float a = 2.51 * 0.01;
    const float b = 0.03 * 0.01;
    const float c = 2.43 * 0.01;
    const float d = 0.59 * 0.01;
    const float e = 0.14 * 0.01;
    float3 x = acescg;
   float3 rgbPost = ((a * x + b)) / ((c * x + d) + e/(x + FLT_MIN));
#else
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    float3 x = acescg;
    float3 rgbPost = (x * (a * x + b)) / (x * (c * x + d) + e);
#endif

    // Scale luminance to linear code value
    // float3 linearCV = Y_2_linCV(rgbPost, CINEMA_WHITE, CINEMA_BLACK);

    // Apply gamma adjustment to compensate for dim surround
    float3 linearCV = darkSurround_to_dimSurround(rgbPost);

    // Apply desaturation to compensate for luminance difference
    //linearCV = mul(ODT_SAT_MAT, color);
    linearCV = lerp(dot(linearCV, AP1_RGB2Y).xxx, linearCV, ODT_SAT_FACTOR.xxx);

    // Convert to display primary encoding
    // Rendering space RGB to XYZ
    float3 XYZ = mul(AP1_2_XYZ_MAT, linearCV);

    // Apply CAT from ACES white point to assumed observer adapted white point
    XYZ = mul(D60_2_D65_CAT, XYZ);

    // CIE XYZ to display primaries
    linearCV = mul(XYZ_2_REC709_MAT, XYZ);

    return linearCV;

#endif
}

//颜色会变白，应该是因为没有在ACES空间计算
float3 SampleACESFilm(float3 x)
{
    float a = 2.51f;
    float b = 0.03f;
    float c = 2.43f;
    float d = 0.59f;
    float e = 0.14f;
    return ((x * (a * x + b)) / (x * (c * x + d) + e));
}

float3 H_f(float3 x, float t1, float t2) {
	return clamp((x - t1) / (t2 - t1), 0.0, 1.0); ;
}

float3 GranTurismoTonemap(float3 x) {
	float P = 1.0;//Maximum brightness
	float a = 1.0;//Contrast
	float m = 0.22;//Linear section start
	float l = 0.4;//Linear section length
	float c = 1.0;//Black tightness
	float b = 0.0;
	float l0 = (P - m)*l / a;
	float L0 = m - m / a;
	float L1 = m + (1 - m) / a;

	float3 L_x = m + a * (x - m);
	float3 T_x = m * pow(x / m, c) + b;

	float S0 = m + l0;
	float S1 = m + a * l0;
	float C2 = a * P / (P - S1);
	float3 S_x = P - (P - S1)*exp(-(C2*(x-S0)/P));

	float3 w0_x = 1 - smoothstep(x, 0, m);
	float3 w2_x = H_f(x, m + l0, m + l0);
	float3 w1_x = 1 - w0_x - w2_x;
	float3 f_x = T_x * w0_x + L_x * w1_x + S_x * w2_x;
	return f_x;
}

half3 ApplyTonemap(half3 input)
{
#if _TONEMAP_ACES
    float3 aces = unity_to_ACES(input);
    // input = AcesTonemap(aces);
    input = GranTurismoTonemap(input);
#elif _TONEMAP_NEUTRAL
    input = NeutralTonemap(input);
#endif

    return saturate(input);
}

half3 ApplyColorGrading(half3 input, float postExposure, TEXTURE2D_PARAM(lutTex, lutSampler), float3 lutParams, TEXTURE2D_PARAM(userLutTex, userLutSampler), float3 userLutParams, float userLutContrib)
{
    // Artist request to fine tune exposure in post without affecting bloom, dof etc
    input *= postExposure;

    // HDR Grading:
    //   - Apply internal LogC LUT
    //   - (optional) Clamp result & apply user LUT
    #if _HDR_GRADING
    {
        float3 inputLutSpace = saturate(LinearToLogC(input)); // LUT space is in LogC
        input = ApplyLut2D(TEXTURE2D_ARGS(lutTex, lutSampler), inputLutSpace, lutParams);

        UNITY_BRANCH
        if (userLutContrib > 0.0)
        {
            input = saturate(input);
            input.rgb = LinearToSRGB(input.rgb); // In LDR do the lookup in sRGB for the user LUT
            half3 outLut = ApplyLut2D(TEXTURE2D_ARGS(userLutTex, userLutSampler), input, userLutParams);
            input = lerp(input, outLut, userLutContrib);
            input.rgb = SRGBToLinear(input.rgb);
        }
    }

    // LDR Grading:
    //   - Apply tonemapping (result is clamped)
    //   - (optional) Apply user LUT
    //   - Apply internal linear LUT
    #else
    {
        input = ApplyTonemap(input);

        UNITY_BRANCH
        if (userLutContrib > 0.0)
        {
            input.rgb = LinearToSRGB(input.rgb); // In LDR do the lookup in sRGB for the user LUT
            half3 outLut = ApplyLut2D(TEXTURE2D_ARGS(userLutTex, userLutSampler), input, userLutParams);
            input = lerp(input, outLut, userLutContrib);
            input.rgb = SRGBToLinear(input.rgb);
        }

        input = ApplyLut2D(TEXTURE2D_ARGS(lutTex, lutSampler), input, lutParams);
    }
    #endif

    return input;
}

half3 ApplyGrain(half3 input, float2 uv, TEXTURE2D_PARAM(GrainTexture, GrainSampler), float intensity, float response, float2 scale, float2 offset)
{
    // Grain in range [0;1] with neutral at 0.5
    half grain = SAMPLE_TEXTURE2D(GrainTexture, GrainSampler, uv * scale + offset).w;

    // Remap [-1;1]
    grain = (grain - 0.5) * 2.0;

    // Noisiness response curve based on scene luminance
    float lum = 1.0 - sqrt(Luminance(input));
    lum = lerp(1.0, lum, response);

    return input + input * grain * intensity * lum;
}

half3 ApplyDithering(half3 input, float2 uv, TEXTURE2D_PARAM(BlueNoiseTexture, BlueNoiseSampler), float2 scale, float2 offset)
{
    // Symmetric triangular distribution on [-1,1] with maximal density at 0
    float noise = SAMPLE_TEXTURE2D(BlueNoiseTexture, BlueNoiseSampler, uv * scale + offset).a * 2.0 - 1.0;
    noise = FastSign(noise) * (1.0 - sqrt(1.0 - abs(noise)));

#if UNITY_COLORSPACE_GAMMA
    input += noise / 255.0;
#else
    input = SRGBToLinear(LinearToSRGB(input) + noise / 255.0);
#endif

    return input;
}

#endif // UNIVERSAL_POSTPROCESSING_COMMON_INCLUDED
