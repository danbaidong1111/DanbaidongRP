#ifndef NPR_TOON_RIM_INCLUDED
#define NPR_TOON_RIM_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonShading.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/PBRToon/PBRToon.hlsl"

// -------------------------------------
// Rim Lighting
float GetDirectRimArea(ShadingData data)
{
#if defined(PASS_CHARACTERFORWARD)
    float2 screenUV = GetNormalizedScreenSpaceUV(data.positionCS.xy);
    return GetCharacterDirectRimLightArea(data.normalVS, screenUV, data.positionCS.z, _DirectRimWidth);
#else
    return 0.0;
#endif
}

float3 EvaluateDirectionalRim(ShadingData data, float rimArea, float3 lightDirWS, float3 lightColor, float lit)
{
#if defined(PASS_CHARACTERFORWARD)
    float3 front = lerp(_DirectRimFrontCol.rgb, _DirectRimFrontCol.rgb * lightColor, _DirectRimFrontCol.a);
    float3 back = lerp(_DirectRimBackCol.rgb, _DirectRimBackCol.rgb * lightColor, _DirectRimBackCol.a);
    float3 lightDirVS = SafeNormalize(TransformWorldToViewDir(lightDirWS));
    return GetRimColor(rimArea, data.albedo, data.normalVS, lightDirVS, lit, front, back);
#else
    return 0.0;
#endif
}

float3 EvaluatePunctualRim(ShadingData data, float3 lightDirWS, float3 lightColor, float lit)
{
    float3 rimColor = 0.0;
#if defined(PASS_CHARACTERFORWARD)
    float3 lightDirVS = SafeNormalize(TransformWorldToViewDir(lightDirWS));
    // The depth-offset helper normalizes XY; skip lights aligned with the view axis.
    if (dot(lightDirVS.xy, lightDirVS.xy) > 0.00001 && _PunctualRimWidth > 0.0)
    {
        float2 screenUV = GetNormalizedScreenSpaceUV(data.positionCS.xy);
        float rimArea = GetCharacterPunctualRimLightArea(lightDirVS, screenUV, data.positionCS.z, _PunctualRimWidth);
        rimColor = GetRimColor(rimArea, data.albedo, data.normalVS, lightDirVS, lit, lightColor, float3(0, 0, 0));
    }
#endif
    return rimColor;
}

void ApplyRim(ShadingData data, float directRimArea, inout float3 color)
{
#if defined(PASS_CHARACTERFORWARD)
    if (data.isPunctual)
        color += EvaluatePunctualRim(data, data.lightDirWS, data.lightColor, data.lightLit)
            * data.distanceAttenuation * data.shadowAttenuation * data.rimContribution;
    else
        color += EvaluateDirectionalRim(data, directRimArea, data.lightDirWS, data.lightColor, data.lightLit);
#endif
}

#endif // NPR_TOON_RIM_INCLUDED
