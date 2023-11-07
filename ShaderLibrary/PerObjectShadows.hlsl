#ifndef PER_OBJECT_SHADOWS_INCLUDED
#define PER_OBJECT_SHADOWS_INCLUDED
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Shadows.hlsl"

float4 _PerObjectShadowmapTexture_TexelSize;
TEXTURE2D_SHADOW(_PerObjectShadowmapTexture);

// x: softShadowQuality
// y: shadowStrength
half4 _PerObjectShadowParams;

float4 TransformWorldToPerObjectShadowCoord(float3 positionWS, float4x4 worldToShadowMatrix)
{
    float4 shadowCoord = mul(worldToShadowMatrix, float4(positionWS, 1.0));
    return float4(shadowCoord.xyz, 0);
}

real SamplePerObjectShadowmapFiltered(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, float4 shadowMapTexelSize, half softShadowQuality)
{
    real attenuation = real(1.0);

    if (softShadowQuality == SOFT_SHADOW_QUALITY_LOW)
    {
        real fetchesWeights[4];
        real2 fetchesUV[4];
        SampleShadow_ComputeSamples_Tent_3x3(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);
        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
                    + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
                    + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
                    + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z));
    }
    else if(softShadowQuality == SOFT_SHADOW_QUALITY_MEDIUM)
    {
        real fetchesWeights[9];
        real2 fetchesUV[9];
        SampleShadow_ComputeSamples_Tent_5x5(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);

        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
                    + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
                    + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
                    + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z))
                    + fetchesWeights[4] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[4].xy, shadowCoord.z))
                    + fetchesWeights[5] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[5].xy, shadowCoord.z))
                    + fetchesWeights[6] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[6].xy, shadowCoord.z))
                    + fetchesWeights[7] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[7].xy, shadowCoord.z))
                    + fetchesWeights[8] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[8].xy, shadowCoord.z));
    }
    else // SOFT_SHADOW_QUALITY_HIGH
    {
        real fetchesWeights[16];
        real2 fetchesUV[16];
        SampleShadow_ComputeSamples_Tent_7x7(shadowMapTexelSize, shadowCoord.xy, fetchesWeights, fetchesUV);

        attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z))
                    + fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z))
                    + fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z))
                    + fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z))
                    + fetchesWeights[4] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[4].xy, shadowCoord.z))
                    + fetchesWeights[5] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[5].xy, shadowCoord.z))
                    + fetchesWeights[6] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[6].xy, shadowCoord.z))
                    + fetchesWeights[7] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[7].xy, shadowCoord.z))
                    + fetchesWeights[8] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[8].xy, shadowCoord.z))
                    + fetchesWeights[9] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[9].xy, shadowCoord.z))
                    + fetchesWeights[10] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[10].xy, shadowCoord.z))
                    + fetchesWeights[11] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[11].xy, shadowCoord.z))
                    + fetchesWeights[12] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[12].xy, shadowCoord.z))
                    + fetchesWeights[13] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[13].xy, shadowCoord.z))
                    + fetchesWeights[14] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[14].xy, shadowCoord.z))
                    + fetchesWeights[15] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[15].xy, shadowCoord.z));
    }

    return attenuation;
}

real SamplePerObjectShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, half4 perObjectShadowParams)
{
    real attenuation;
    real shadowStrength = perObjectShadowParams.y;
    half softShadowQuality = perObjectShadowParams.x;
#if (_SHADOWS_SOFT)
    if(softShadowQuality > SOFT_SHADOW_QUALITY_OFF)
    {
        attenuation = SamplePerObjectShadowmapFiltered(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, _PerObjectShadowmapTexture_TexelSize, softShadowQuality);
    }
    else
#endif
    {
        // 1-tap hardware comparison
        attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
    }

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    return attenuation;
}

// PerObjectShadowParams
// x: SoftShadowQuality
// y: shadowStrength
// TODO: set object independent shadowStrength
half4 GetPerObjectShadowParams()
{
    half4 params = _PerObjectShadowParams;
    return _PerObjectShadowParams;
}


half PerObjectRealtimeShadow(float4 shadowCoord)
{
    half4 perObjectShadowParams = GetPerObjectShadowParams();
    return SamplePerObjectShadowmap(TEXTURE2D_ARGS(_PerObjectShadowmapTexture, sampler_LinearClampCompare), shadowCoord, perObjectShadowParams);
}

#endif /* PER_OBJECT_SHADOWS_INCLUDED */