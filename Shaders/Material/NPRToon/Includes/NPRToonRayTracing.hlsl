#ifndef NPR_TOON_RAY_TRACING_INCLUDED
#define NPR_TOON_RAY_TRACING_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonInput.hlsl"

#define ATTRIBUTES_NEED_TEXCOORD0
#if defined(PASS_INDIRECTDXR)
    #define ATTRIBUTES_NEED_TEXCOORD1
    #define ATTRIBUTES_NEED_TEXCOORD2
    #define ATTRIBUTES_NEED_TEXCOORD3
    #define ATTRIBUTES_NEED_NORMAL
    #define ATTRIBUTES_NEED_TANGENT
#endif

#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/RayTracing/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/RayTracing/RaytracingIntersection.hlsl"

#if defined(PASS_INDIRECTDXR)
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/RayTracing/RaytracingFragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonLighting.hlsl"

// -------------------------------------
// NPR Ray-Traced Lighting
ShadingResult RayTracedNPRToon(ShadingData data, SubMaterialData subMaterial)
{
    ShadingResult result = (ShadingResult)0;
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; ++lightIndex)
    {
        DirectionalLightData light = g_DirectionalLightDatas[lightIndex];
        if (!MatchesLightLayer(light.lightLayerMask, data.renderingLayers)) continue;
        // Use world-space shadow maps for secondary hits, never a primary-screen shadow sample.
        float shadow = lightIndex == 0 ? MainLightRealtimeShadow(TransformWorldToShadowCoord(data.positionWS)) : 1.0;
        PrepareDirectionalLight(light, shadow, data);
        ShadingResult lighting = DirectShading(data, subMaterial);
        result.diffuse += lighting.diffuse;
        result.specular += lighting.specular;
    }

    // _EnvLightIndexShift is the number of punctual lights preceding environment probes.
    // Secondary hit positions cannot use the primary camera's screen-space light cluster.
    for (uint punctualLightIndex = 0; punctualLightIndex < _EnvLightIndexShift; ++punctualLightIndex)
    {
        GPULightData light = FetchLight(punctualLightIndex);
        if (!MatchesLightLayer(light.lightLayerMask, data.renderingLayers)) continue;
        PreparePunctualLight(light, data);
        if (data.distanceAttenuation <= 0.0) continue;
        ShadingResult lighting = DirectShading(data, subMaterial);
        result.diffuse += lighting.diffuse;
        result.specular += lighting.specular;
    }

    ShadingResult indirectLighting = IndirectShading(data);
    result.diffuse += indirectLighting.diffuse;
    result.specular += indirectLighting.specular;
    if (_RayTracingDiffuseLightingOnly != 0)
        result.specular = 0.0;
    return result;
}

// -------------------------------------
// Indirect Closest Hit
[shader("closesthit")]
void ClosestHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    rayIntersection.t = RayTCurrent();
    IntersectionVertex vertex;
    FragInputs input;
    GetCurrentVertexAndBuildFragInputs(attributeData, vertex, input);

    // Input and texture preparation.
    float4 positionCS = 0.0;
    float3 positionWS = input.positionRWS;
    float3 viewDirWS = -WorldRayDirection();
    float4 uv0 = input.texCoord0;
    float4 uv1 = input.texCoord1;
    float4 uv2 = input.texCoord2;
    float4 uv3 = input.texCoord3;
    float2 baseUV = GetBaseUV(uv0.xy);
    float4 mainTex = SampleMainTex(baseUV);
    float4 lightmapTex = SampleLightMap(baseUV);
    float4 normalTex = SampleNormalTex(baseUV);
    float3 normalWS = DecodeNormalWS(normalTex, input.tangentToWorld[2], input.tangentToWorld);

    // Property preparation and lighting.
    SubMaterialData subMaterial = DecodeSubMaterial(lightmapTex.a);
    ShadingData shadingData;
    InitializeShadingData();
    ShadingResult lighting = RayTracedNPRToon(shadingData, subMaterial);
    rayIntersection.packedNormalWS = PackNormalOctQuadEncode(shadingData.normalWS);
    rayIntersection.color = lighting.diffuse + lighting.specular + shadingData.emission;
}

#endif // PASS_INDIRECTDXR

// -------------------------------------
// Shared Alpha Test
bool IsRayAlphaClipped(AttributeData attributeData)
{
#if defined(_ALPHATEST_ON)
    IntersectionVertex vertex;
    GetCurrentIntersectionVertex(attributeData, vertex);
    float2 uv = GetBaseUV(vertex.texCoord0.xy);
    return IsAlphaClipped(SampleMainTex(uv).a);
#else
    return false;
#endif
}

// -------------------------------------
// Indirect Any Hit
#if defined(PASS_INDIRECTDXR)
[shader("anyhit")]
void AnyHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    if (IsRayAlphaClipped(attributeData)) IgnoreHit();
}
#endif

// -------------------------------------
// Visibility Hit Shaders
#if defined(PASS_VISIBILITYDXR)
[shader("closesthit")]
void ClosestHitMain(inout RayIntersectionVisibility rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    rayIntersection.t = RayTCurrent();
    rayIntersection.color = 0;
}

[shader("anyhit")]
void AnyHitVisibility(inout RayIntersectionVisibility rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    if (IsRayAlphaClipped(attributeData))
    {
        IgnoreHit();
        return;
    }
    rayIntersection.t = RayTCurrent();
    rayIntersection.color = 0;
    AcceptHitAndEndSearch();
}
#endif

#endif // NPR_TOON_RAY_TRACING_INCLUDED
