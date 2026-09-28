#ifndef NPR_TOON_PASS_INCLUDED
#define NPR_TOON_PASS_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonInput.hlsl"
#if defined(PASS_GBUFFERBASE) || defined(PASS_CHARACTERFORWARD) || defined(PASS_CHARACTEROUTLINE)
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonOutline.hlsl"
#endif
#if defined(PASS_GBUFFERBASE)
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonGBuffer.hlsl"
#endif
#if defined(PASS_CHARACTERFORWARD) || defined(PASS_CHARACTEROUTLINE)
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonRim.hlsl"
#endif

// -------------------------------------
// Vertex Inputs And Interpolators
#if defined(PASS_GBUFFERBASE) || defined(PASS_CHARACTERFORWARD) || defined(PASS_CHARACTEROUTLINE)
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    float4 uv0 : TEXCOORD3;
    float4 uv1 : TEXCOORD4;
    float4 uv2 : TEXCOORD5;
    float4 uv3 : TEXCOORD6;
    float4 color : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// -------------------------------------
// Character Vertex
Varyings CommonVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
    output.uv0 = input.uv0;
    output.uv1 = input.uv1;
    output.uv2 = input.uv2;
    output.uv3 = input.uv3;
    output.color = input.color;
    ApplyOutlineOffset(input.normalOS, input.tangentOS, input.color, input.uv2.xy, output.positionCS, output.normalWS);
    return output;
}

#endif

// -------------------------------------
// GBuffer Fragment
#if defined(PASS_GBUFFERBASE)
void GBufferPassFragment(Varyings input, out float4 gbuffer0 : SV_Target0,
    out float4 gbuffer1 : SV_Target1, out float4 gbuffer2 : SV_Target2)
{
    InitializeInputData();
    float4 mainTex = SampleMainTex(baseUV);
    ApplyAlphaClip(mainTex.a);
    ApplyLODCrossFade(positionCS);
    float4 normalTex = SampleNormalTex(baseUV);
    normalWS = DecodeNormalWS(normalTex, normalWS, tangentWS);
    EncodeCharacterGBuffer(normalWS, positionCS.z, gbuffer0, gbuffer1, gbuffer2);
}
#endif

// -------------------------------------
// Character Forward And Outline Fragment
#if defined(PASS_CHARACTERFORWARD) || defined(PASS_CHARACTEROUTLINE)
float4 CharacterPassFragment(Varyings input) : SV_Target0
{
    // Input and texture sampling.
    InitializeInputData();
    float4 mainTex = SampleMainTex(baseUV);
    float4 lightmapTex = SampleLightMap(baseUV);
    float4 normalTex = SampleNormalTex(baseUV);
    normalWS = DecodeNormalWS(normalTex, normalWS, tangentWS);
    ApplyAlphaClip(mainTex.a);
    ApplyLODCrossFade(positionCS);

    // Property preparation.
    SubMaterialData subMaterial = DecodeSubMaterial(lightmapTex.a);
    ShadingData shadingData;
    InitializeShadingData();

    ShadingResult directLighting = (ShadingResult)0;
    float3 rimColor = 0.0;
    float directRimArea = GetDirectRimArea(shadingData);

    // Directional lights.
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; ++lightIndex)
    {
        DirectionalLightData light = g_DirectionalLightDatas[lightIndex];
        if (!MatchesLightLayer(light.lightLayerMask, shadingData.renderingLayers)) continue;
        PrepareDirectionalLight(light, SampleDirectionalShadow(lightIndex, positionCS), shadingData);
        ShadingResult lighting = DirectShading(shadingData, subMaterial);
        directLighting.diffuse += lighting.diffuse;
        directLighting.specular += lighting.specular;
        ApplyRim(shadingData, directRimArea, rimColor);
    }

    // Punctual lights from the current screen-space cluster.
    PositionInputs posInput = GetPositionInput(positionCS.xy, _ScreenSize.zw, positionCS.z, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
    uint lightStart, lightCount;
    GetCountAndStart(posInput, LIGHTCATEGORY_PUNCTUAL, lightStart, lightCount);
    if (lightCount > 0)
    {
        for (uint offset = 0; offset < lightCount; ++offset)
        {
            uint lightIndex = FetchIndex(lightStart, offset);
            if (lightIndex == (uint)-1) break;
            GPULightData light = FetchLight(lightIndex);
            if (!MatchesLightLayer(light.lightLayerMask, shadingData.renderingLayers)) continue;
            PreparePunctualLight(light, shadingData);
            ShadingResult lighting = DirectShading(shadingData, subMaterial);
            directLighting.diffuse += lighting.diffuse;
            directLighting.specular += lighting.specular;
            ApplyRim(shadingData, directRimArea, rimColor);
        }
    }

    // Indirect light and final composition.
    ShadingResult indirectLighting = IndirectShading(shadingData);
    float3 color = directLighting.diffuse + directLighting.specular
        + indirectLighting.diffuse + indirectLighting.specular + rimColor;
    ApplyEmission(shadingData, color);
    ApplyOutline(shadingData, subMaterial, color);


    return float4(color, 1.0);
return float4(shadingData.albedo, 1.0);
}
#endif

// -------------------------------------
// ShadowCaster Pass
#if defined(PASS_SHADOWCASTER)
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Shadows.hlsl"

// -------------------------------------
// Shadow Caster Inputs And Interpolators
struct ShadowCasterAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShadowCasterVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// -------------------------------------
// Shadow Caster Vertex
ShadowCasterVaryings ShadowCasterPassVertex(ShadowCasterAttributes input)
{
    ShadowCasterVaryings output = (ShadowCasterVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirWS = SafeNormalize(_LightPosition - positionWS);
#else
    float3 lightDirWS = _LightDirection;
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif
    output.uv = GetBaseUV(input.uv);
    return output;
}

// -------------------------------------
// Shadow Caster Fragment
float4 ShadowCasterPassFragment(ShadowCasterVaryings input) : SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ApplyAlphaClip(SampleMainTex(input.uv).a);
    ApplyLODCrossFade(input.positionCS);
    return 0;
}
#endif

// -------------------------------------
// DepthOnly Pass
#if defined(PASS_DEPTHONLY)
// -------------------------------------
// Depth Inputs And Interpolators
struct DepthOnlyAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DepthOnlyVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// -------------------------------------
// Depth Vertex
DepthOnlyVaryings DepthOnlyPassVertex(DepthOnlyAttributes input)
{
    DepthOnlyVaryings output = (DepthOnlyVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = GetBaseUV(input.uv);
    return output;
}

// -------------------------------------
// Depth Fragment
float4 DepthOnlyPassFragment(DepthOnlyVaryings input) : SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ApplyAlphaClip(SampleMainTex(input.uv).a);
    ApplyLODCrossFade(input.positionCS);
    return float4(input.positionCS.zzz, 0);
}
#endif

// -------------------------------------
// DepthNormals Pass
#if defined(PASS_DEPTHNORMALS)
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/UnityGBuffer.hlsl"

// -------------------------------------
// Depth Normals Inputs And Outputs
struct DepthNormalsAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct DepthNormalsOutput
{
    float4 normal : SV_Target0;
#if defined(_WRITE_RENDERING_LAYERS)
    float4 layers : SV_Target1;
#endif
};

// -------------------------------------
// Depth Normals Vertex
DepthNormalsVaryings DepthNormalsPassVertex(DepthNormalsAttributes input)
{
    DepthNormalsVaryings output = (DepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = GetBaseUV(input.uv);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
    return output;
}

// -------------------------------------
// Rendering Layers
void ApplyRenderingLayers(inout DepthNormalsOutput output)
{
#if defined(_WRITE_RENDERING_LAYERS)
    output.layers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
#endif
}

// -------------------------------------
// Depth Normals Fragment
DepthNormalsOutput DepthNormalsPassFragment(DepthNormalsVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ApplyAlphaClip(SampleMainTex(input.uv).a);
    ApplyLODCrossFade(input.positionCS);
    float4 normalTex = SampleNormalTex(input.uv);
    float3 normalWS = DecodeNormalWS(normalTex, input.normalWS, input.tangentWS);
    DepthNormalsOutput output = (DepthNormalsOutput)0;
    output.normal = float4(PackNormal(normalWS), 0);
    ApplyRenderingLayers(output);
    return output;
}
#endif

#endif // NPR_TOON_PASS_INCLUDED
