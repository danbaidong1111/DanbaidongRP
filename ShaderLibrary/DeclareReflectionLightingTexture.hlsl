#ifndef UNITY_DECLARE_REFLECTION_LIGHTING_TEXTURE_INCLUDED
#define UNITY_DECLARE_REFLECTION_LIGHTING_TEXTURE_INCLUDED
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"

TEXTURE2D_X_FLOAT(_ReflectionLightingTexture);

float4 SampleSceneReflectionLighting(float2 uv)
{
    float4 reflectionLighting = SAMPLE_TEXTURE2D_X(_ReflectionLightingTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv));
    return reflectionLighting;
}

float4 LoadSceneReflectionLighting(uint2 coordSS)
{
    float4 reflectionLighting = LOAD_TEXTURE2D_X(_ReflectionLightingTexture, coordSS);
    return reflectionLighting;
}

#endif /* UNITY_DECLARE_REFLECTION_LIGHTING_TEXTURE_INCLUDED */