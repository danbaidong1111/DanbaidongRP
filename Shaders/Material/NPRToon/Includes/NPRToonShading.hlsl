#ifndef NPR_TOON_SHADING_INCLUDED
#define NPR_TOON_SHADING_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonInput.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonSubMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/GlobalIllumination.hlsl"

// -------------------------------------
// Shading Interface
struct ShadingData
{
    // Geometry and original mesh UVs.
    float4 positionCS;
    float3 positionWS;
    float3 normalWS;
    float3 normalVS;
    float3 viewDirWS;
    float4 uv0;
    float4 uv1;
    float4 uv2;
    float4 uv3;
    uint renderingLayers;

    // Raw texture samples and decoded properties.
    float4 mainTex;
    float4 normalTex;
    float4 lightmapTex;
    float3 albedo;
    float3 emission;
    float lightMapAO;
    float indirectOcclusion;
    float specularIntensity;
    float specularMask;
    bool isMetal;

    // Current light, refreshed before each DirectShading call.
    float3 lightDirWS;
    float3 lightColor;
    float distanceAttenuation;
    float shadowAttenuation;
    float baseContribution;
    float rimContribution;
    float outlineContribution;
    bool isPunctual;
    float lightLit;
};

struct ShadingResult
{
    float3 diffuse;
    float3 specular;
};

// -------------------------------------
// Property Preparation
bool IsMetal(float lightMapR)
{
#if defined(_SHADINGMODE_FACE)
    return false;
#else
    return lightMapR > 0.9;
#endif
}

float GetIndirectOcclusion(float lightMapG)
{
#if defined(_SHADINGMODE_FACE)
    return 1.0;
#else
    return lerp(1.0, saturate(lightMapG * 2.0), _IndirectOcclusion);
#endif
}

// Reads the fixed fragment locals after texture sampling and normal decoding.
#define InitializeShadingData() \
    shadingData = (ShadingData)0; \
    shadingData.positionCS = positionCS; \
    shadingData.positionWS = positionWS; \
    shadingData.normalWS = SafeNormalize(normalWS); \
    shadingData.normalVS = SafeNormalize(TransformWorldToViewNormal(shadingData.normalWS)); \
    shadingData.viewDirWS = SafeNormalize(viewDirWS); \
    shadingData.uv0 = uv0; \
    shadingData.uv1 = uv1; \
    shadingData.uv2 = uv2; \
    shadingData.uv3 = uv3; \
    shadingData.mainTex = mainTex; \
    shadingData.normalTex = normalTex; \
    shadingData.lightmapTex = lightmapTex; \
    shadingData.albedo = mainTex.rgb * _BaseColor.rgb; \
    shadingData.specularIntensity = lightmapTex.r; \
    shadingData.lightMapAO = lerp(0.5, lightmapTex.g, _LightMapAO); \
    shadingData.specularMask = lightmapTex.b; \
    shadingData.isMetal = IsMetal(lightmapTex.r); \
    shadingData.indirectOcclusion = GetIndirectOcclusion(lightmapTex.g); \
    shadingData.renderingLayers = GetMeshRenderingLayer(); \
    shadingData.emission = mainTex.a * lerp(_EmissionCol.rgb, _EmissionCol.rgb * shadingData.albedo, _EmissionCol.a)

// -------------------------------------
// Non-Metal And Metal Shading
float3 ShadeNonMetal(ShadingData data, SubMaterialData subMaterial, float NdotH)
{
    float specTerm = pow(max(NdotH, 0.0001), max(subMaterial.specPow, 0.001));
    float specular = SoftStep(1.0 - data.specularMask, specTerm, _SpecSoftness);
    return specular * data.specularIntensity * subMaterial.specMul * subMaterial.specColor;
}

ShadingResult ShadeMetal(ShadingData data, float NdotH, float lit)
{
    ShadingResult result = (ShadingResult)0;
    float matcap = SampleMetalMatCap(data.normalVS);
    float3 metalColor = lerp(_MTMapDarkColor.rgb, _MTMapLightColor.rgb, matcap);
    result.diffuse = data.albedo * metalColor * lerp(_MTMapShadowMulColor.rgb, float3(1, 1, 1), lit);

    float metalSpec = pow(max(NdotH, 0.0001), max(_MTSpecPow, 0.001));
    result.specular = SampleMetalSpecular(metalSpec);
    result.specular *= data.specularMask;
    result.specular = lerp(result.specular, _MTTopLayerColor.rgb, SoftStep(_MTTopLayerRange, metalSpec, _SpecSoftness));
    result.specular *= _MTSpecMul;
    return result;
}

// -------------------------------------
// Direct Light Shading
ShadingResult DirectShading(inout ShadingData data, SubMaterialData subMaterial)
{
    ShadingResult result = (ShadingResult)0;
#if defined(PASS_CHARACTEROUTLINE)
    float offset = data.isPunctual ? _OutlinePunctualLightingOffset : _OutlineDirectLightingOffset;
    float4 tint = data.isPunctual ? _OutlinePunctualLightingColor : _OutlineDirectLightingColor;
    float3 lightDirVS = SafeNormalize(TransformWorldToViewDir(data.lightDirWS));
    float area = step(0.8 - offset, dot(data.normalVS.xy, lightDirVS.xy));
    result.diffuse = area * lerp(tint.rgb, tint.rgb * data.lightColor, tint.a);
    result.diffuse *= data.distanceAttenuation * data.shadowAttenuation * data.outlineContribution;
    data.lightLit = 1.0;
#else
    // Directional shadows select the ramp; punctual shadows attenuate the whole light.
    float shadow = data.isPunctual ? 1.0 : data.shadowAttenuation;
    float halfLambert = dot(data.normalWS, data.lightDirWS) * 0.5 + 0.5;
    float lightValue = RemapLightMapAO(halfLambert, data.lightMapAO);
    float rampX = GetRampCoordinate(lightValue, _LightArea, _ShadowRampWidth) * shadow;
    float lit = SoftStep(_LightArea, lightValue, _ShadowSmooth) * shadow;
    float noseMask = 0.0;
#if defined(_SHADINGMODE_FACE)
    float4 faceUV = float4(GetBaseUV(data.uv0.xy), data.uv1.xy);
    lit = SampleFaceLighting(faceUV, data.lightDirWS, noseMask) * shadow;
    rampX = lit;
#endif
    lit = lerp(1.0, lit, _ShadowStrength);
    rampX = lerp(1.0, rampX, _ShadowStrength);
    result.diffuse = data.albedo * SampleShadowRamp(float2(subMaterial.dayRampRow, subMaterial.nightRampRow), rampX, lit);
#if defined(_SHADINGMODE_FACE)
    result.specular = noseMask * _FaceNoseSpecular * _NoseSpecColor.rgb;
#else
    float NdotH = saturate(dot(data.normalWS, SafeNormalize(data.lightDirWS + data.viewDirWS)));
    if (data.isMetal)
        result = ShadeMetal(data, NdotH, lit);
    else
        result.specular = ShadeNonMetal(data, subMaterial, NdotH);
#endif
    float attenuation = data.distanceAttenuation * data.baseContribution;
    attenuation *= data.isPunctual ? data.shadowAttenuation : 1.0;
    float3 radiance = data.lightColor * attenuation;
    result.diffuse *= radiance;
    result.specular *= lit * radiance;
    data.lightLit = lit;
#endif
    return result;
}

// -------------------------------------
// Indirect Light Shading
ShadingResult IndirectShading(ShadingData data)
{
    ShadingResult result = (ShadingResult)0;
#if !defined(PASS_CHARACTEROUTLINE)
    float3 shNormal = SafeNormalize(lerp(data.normalWS, float3(0, 1, 0), _IndirDiffUpDirSH));
    float3 ambient = SampleSH9(_AmbientProbeData, shNormal);
#if defined(SHADER_STAGE_RAY_TRACING)
    ambient *= _RayTracingAmbientProbeDimmer;
#endif
    float3 environment = lerp(ambient, _SelfEnvColor.rgb, _EnvColorLerp);
    result.diffuse = data.albedo * environment * _IndirDiffIntensity * data.indirectOcclusion;
#endif
    return result;
}

// -------------------------------------
// Color Composition
void ApplyEmission(ShadingData data, inout float3 color)
{
#if !defined(PASS_CHARACTEROUTLINE)
    color += data.emission;
#endif
}

#endif // NPR_TOON_SHADING_INCLUDED
