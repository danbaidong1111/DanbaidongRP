#ifndef NPR_TOON_LIGHTING_INCLUDED
#define NPR_TOON_LIGHTING_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonShading.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/GPUCulledLights.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/RealtimeLights.hlsl"

// -------------------------------------
// Light Layers
bool MatchesLightLayer(uint lightLayers, uint renderingLayers)
{
#if defined(_LIGHT_LAYERS)
    return IsMatchingLightLayer(lightLayers, renderingLayers);
#else
    return true;
#endif
}

// -------------------------------------
// Current Light Preparation
void PrepareDirectionalLight(DirectionalLightData light, float shadow, inout ShadingData data)
{
    data.lightDirWS = light.lightDirection;
#if defined(PASS_CHARACTEROUTLINE)
    data.lightColor = light.lightColor;
#else
    data.lightColor = lerp(light.lightColor, _SelfLight.rgb, _MainLightColorLerp);
#endif
    data.distanceAttenuation = 1.0;
    data.shadowAttenuation = shadow;
    data.baseContribution = 1.0;
    data.rimContribution = 1.0;
    data.outlineContribution = 1.0;
    data.isPunctual = false;
    data.lightLit = 0.0;
}

void PreparePunctualLight(GPULightData light, inout ShadingData data)
{
    float3 lightVector = light.lightPosWS - data.positionWS;
    float distanceSqr = max(dot(lightVector, lightVector), 0.0001);
    data.lightDirWS = lightVector * rsqrt(distanceSqr);
    data.lightColor = light.lightColor;
    data.distanceAttenuation = DistanceAttenuation(distanceSqr, light.lightAttenuation.xy)
        * AngleAttenuation(light.lightDirection, data.lightDirWS, light.lightAttenuation.zw);
    data.shadowAttenuation = 1.0;
    // A shadow-casting light can have no allocated shadow slice for this camera.
    if (light.shadowType != 0 && light.shadowLightIndex >= 0)
        data.shadowAttenuation = AdditionalLightShadow(light.shadowLightIndex,
            data.positionWS, data.lightDirWS, float4(1, 1, 1, 1), light.lightOcclusionProbInfo);
    data.baseContribution = light.baseContribution;
    data.rimContribution = light.rimContribution;
    data.outlineContribution = light.outlineContribution;
    data.isPunctual = true;
    data.lightLit = 0.0;
}

// -------------------------------------
// Raster Directional Shadows
#if !defined(SHADER_STAGE_RAY_TRACING)
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/PerObjectShadows.hlsl"

float SampleDirectionalShadow(uint lightIndex, float4 positionCS)
{
    float shadow = 1.0;
#if !defined(PASS_CHARACTEROUTLINE)
    if (lightIndex == 0)
    {
        float2 screenUV = GetNormalizedScreenSpaceUV(positionCS.xy);
        shadow = SAMPLE_TEXTURE2D(_ScreenSpaceShadowmapTexture, sampler_PointClamp, screenUV).r;
#if defined(_SELF_SHADOW) && defined(_PEROBJECT_SCREEN_SPACE_SHADOW) && !defined(_RAYTRACING_SHADOWS)
        shadow = min(shadow, SamplePerObjectScreenSpaceShadowmap(screenUV));
#endif
    }
#endif
    return shadow;
}
#endif

#endif // NPR_TOON_LIGHTING_INCLUDED
