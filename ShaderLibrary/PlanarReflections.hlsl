#ifndef DANBAIDONG_PLANAR_REFLECTIONS_INCLUDED
#define DANBAIDONG_PLANAR_REFLECTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"

TEXTURE2D(_PlanarReflectionTexture0);
TEXTURE2D(_PlanarReflectionTexture1);
TEXTURE2D(_PlanarReflectionTexture2);
TEXTURE2D(_PlanarReflectionTexture3);
SAMPLER(sampler_PlanarReflectionTexture0);

int _PlanarReflectionCount;
float4x4 _PlanarReflectionWorldToClip[4];
float4x4 _PlanarReflectionProjectionWorldToLocal[4];
float4 _PlanarReflectionPlanes[4];
float4 _PlanarReflectionCenters[4];
float4 _PlanarReflectionRights[4];
float4 _PlanarReflectionForwards[4];
float4 _PlanarReflectionParameters[4];
float4 _PlanarReflectionLayers[4];
float4 _PlanarReflectionSortKeys[4]; // Importance, influence volume.
float4 _PlanarReflectionUVScaleBias;

float3 SamplePlanarReflectionTexture(uint index, float2 uv, float mip)
{
    float3 radiance = 0.0;
    if (index == 0u) radiance = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTexture0, sampler_PlanarReflectionTexture0, uv, mip).rgb;
    else if (index == 1u) radiance = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTexture1, sampler_PlanarReflectionTexture0, uv, mip).rgb;
    else if (index == 2u) radiance = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTexture2, sampler_PlanarReflectionTexture0, uv, mip).rgb;
    else radiance = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTexture3, sampler_PlanarReflectionTexture0, uv, mip).rgb;
    return radiance;
}

float3 ProjectPlanarReflection(uint index, float3 positionWS, float3 reflectedDirection)
{
    float distance = 65536.0;
    uint shape = (uint)_PlanarReflectionLayers[index].w;
    // Orthographic rays have no vanishing point. A finite proxy supplies the depth for normal distortion.
    if (shape == 0u && !IsPerspectiveProjection())
        return positionWS;
    if (shape != 0u)
    {
        float3 origin = mul(_PlanarReflectionProjectionWorldToLocal[index], float4(positionWS, 1)).xyz;
        float3 direction = mul((float3x3)_PlanarReflectionProjectionWorldToLocal[index], reflectedDirection);
        if (shape == 1u)
        {
            float3 safeDirection = (step(0.0, direction) * 2.0 - 1.0) * max(abs(direction), 1e-6);
            float3 first = (-1.0 - origin) / safeDirection, second = (1.0 - origin) / safeDirection;
            float3 exitDistances = max(first, second);
            float exitDistance = min(exitDistances.x, min(exitDistances.y, exitDistances.z));
            float3 entryDistances = min(first, second);
            float entryDistance = max(entryDistances.x, max(entryDistances.y, entryDistances.z));
            if (exitDistance >= max(entryDistance, 0)) distance = max(0, exitDistance);
        }
        else
        {
            float a = dot(direction, direction), b = dot(origin, direction), c = dot(origin, origin) - 1.0;
            float discriminant = b * b - a * c;
            if (discriminant >= 0 && a > 1e-8) distance = max(0, (-b + sqrt(discriminant)) / a);
        }
    }
    return positionWS + reflectedDirection * distance;
}

// RGB contains reflected radiance; alpha is the available weight in the reflection hierarchy.
float4 SamplePlanarReflectionProbe(uint i, float3 positionWS, float3 normalWS, float3 viewDirectionWS, float perceptualRoughness, uint renderingLayers)
{
    uint layers = (uint)_PlanarReflectionLayers[i].x | ((uint)_PlanarReflectionLayers[i].y << 16u);
    if ((layers & renderingLayers) == 0u)
        return 0.0;
    float4 plane = _PlanarReflectionPlanes[i];
    float3 offset = positionWS - _PlanarReflectionCenters[i].xyz;
    float planeDistance = abs(dot(plane.xyz, positionWS) + plane.w);
    float thickness = _PlanarReflectionCenters[i].w;
    float alignment = abs(dot(plane.xyz, normalWS));
    float4 parameters = _PlanarReflectionParameters[i];
    if (planeDistance >= thickness || alignment < parameters.z)
        return 0.0;
    float2 edgeDistances = float2(_PlanarReflectionRights[i].w - abs(dot(offset, _PlanarReflectionRights[i].xyz)),
        _PlanarReflectionForwards[i].w - abs(dot(offset, _PlanarReflectionForwards[i].xyz)));
    float edgeDistance = min(edgeDistances.x, edgeDistances.y);
    if (edgeDistance <= 0.0)
        return 0.0;
    float weight = saturate(edgeDistance / max(parameters.y, 0.0001));
    weight *= 1.0 - smoothstep(thickness * 0.5, thickness, planeDistance);
    weight *= parameters.z >= 0.9999 ? 1.0 : saturate((alignment - parameters.z) / max(0.001, 1.0 - parameters.z));

    float3 projected = ProjectPlanarReflection(i, positionWS, reflect(-viewDirectionWS, normalWS));
    float4 positionCS = mul(_PlanarReflectionWorldToClip[i], float4(projected, 1.0));
    if (positionCS.w <= 0.00001)
        return 0.0;
    float2 uv = positionCS.xy / positionCS.w * _PlanarReflectionUVScaleBias.xy + _PlanarReflectionUVScaleBias.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
        return 0.0;
    weight *= saturate(_PlanarReflectionLayers[i].z);
    float roughness = saturate(perceptualRoughness);
    float mip = roughness * (1.7 - 0.7 * roughness) * parameters.w;
    return float4(max(SamplePlanarReflectionTexture(i, uv, mip), 0.0) * parameters.x, weight);
}

float4 SamplePlanarReflection(float3 positionWS, float3 normalWS, float3 viewDirectionWS, float perceptualRoughness, uint renderingLayers)
{
    float3 radiance = 0.0;
    float coverage = 0.0;
    [loop]
    for (uint i = 0u; i < (uint)_PlanarReflectionCount && coverage < 1.0; ++i)
    {
        float4 reflection = SamplePlanarReflectionProbe(i, positionWS, normalWS, viewDirectionWS, perceptualRoughness, renderingLayers);
        reflection.a = min(reflection.a, 1.0 - coverage);
        radiance += reflection.rgb * reflection.a;
        coverage += reflection.a;
    }
    return float4(coverage > 0.0 ? radiance / coverage : 0.0, coverage);
}

float4 SamplePlanarReflection(float3 positionWS, float3 normalWS, float perceptualRoughness, uint renderingLayers)
{
    return SamplePlanarReflection(positionWS, normalWS, GetWorldSpaceNormalizeViewDir(positionWS), perceptualRoughness, renderingLayers);
}

#endif
