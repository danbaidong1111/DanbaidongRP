#ifndef DANBAIDONG_VOLUMETRIC_LIGHTING_TEMPORAL_INCLUDED
#define DANBAIDONG_VOLUMETRIC_LIGHTING_TEMPORAL_INCLUDED

#include "Packages/com.unity.render-pipelines.danbaidong/Runtime/Fog/ShaderVariablesAtmosphericFog.cs.hlsl"

#if !defined(FOG_NO_REPROJECTION)
Texture3D<float4> _FogHistory;
#endif
float4 _FogGrid;
float4 _FogHistoryViewport; // Previous width, height, slices per view, view count.
float4 _FogHistoryParams; // blend, previous near, previous far, unused
float4x4 _FogPreviousViewProjection;
float4 _FogPreviousPosition;
float4 _FogPreviousForward;
float4 _FogPreviousDepthEncoding;

float FogSampleOffset(uint3 cell, uint sequence)
{
    uint hash = cell.x * 1973u + cell.y * 9277u + cell.z * 26699u + 911u;
    hash = (hash ^ (hash >> 16)) * 2246822519u;
    hash = (hash ^ (hash >> 13)) * 3266489917u;
    hash ^= hash >> 16;
    float offset = (hash & 65535u) * (1.0 / 65536.0);
    return frac(offset + (sequence & 1023u) * 0.61803398875);
}

float4 FogTemporalBlend(float4 current, float3 centerWS)
{
#if defined(FOG_NO_REPROJECTION)
    return current;
#else
    if (_FogHistoryParams.x <= 0) return current;
    float4 clip = mul(_FogPreviousViewProjection, float4(centerWS, 1));
    if (clip.w <= 0) return current;
    float2 previousUV = clip.xy / clip.w * 0.5 + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
        previousUV.y = 1 - previousUV.y;
    #endif
    float previousDepth = _FogPreviousDepthEncoding.w > 0.5 && _FogPreviousPosition.w < 0.5
        ? length(centerWS - _FogPreviousPosition.xyz) : dot(centerWS - _FogPreviousPosition.xyz, _FogPreviousForward.xyz);
    if (previousDepth <= 0 || previousDepth >= _FogHistoryParams.z || any(previousUV <= 0) || any(previousUV >= 1))
        return current;
    float previousSlice = _FogPreviousDepthEncoding.y > 0
        ? _FogPreviousDepthEncoding.x + _FogPreviousDepthEncoding.y * log2(max(1e-6, previousDepth - _FogPreviousDepthEncoding.z))
        : log2(1 + previousDepth / _FogHistoryParams.y) / log2(1 + _FogHistoryParams.z / _FogHistoryParams.y);
    bool physical = _FogOptions.y > 0.5;
    if (physical && (previousSlice <= 0 || previousSlice >= 1)) return current;
    float4 viewport = _FogHistoryViewport;
    float3 historyUVW = float3(previousUV, physical ? previousSlice : previousSlice * (viewport.z - 1) / viewport.z);
#if defined(DANBAIDONG_VOLUMETRIC_VIEWS)
    historyUVW.z = (clamp(historyUVW.z, 0.5 / viewport.z, 1 - 0.5 / viewport.z) + _FogViewIndex) / max(1, viewport.w);
#endif
    uint historyWidth, historyHeight, historyDepth;
    _FogHistory.GetDimensions(historyWidth, historyHeight, historyDepth);
    float3 viewportSize = viewport.xyz * float3(1, 1, max(1, viewport.w));
    historyUVW = clamp(historyUVW * viewportSize, 0.5, viewportSize - 0.5)
        / float3(historyWidth, historyHeight, historyDepth);
    float4 history = SAMPLE_TEXTURE3D_LOD(_FogHistory, sampler_LinearClamp, historyUVW, 0);
    if (AnyIsNaN(history) || AnyIsInf(history)) return current;
    if (physical)
        return history.a > 0 ? lerp(current, max(0, history), _FogHistoryParams.x) : current;
    float densityAgreement = saturate(1 - abs(history.a - current.a) / max(0.005, current.a * 0.5));
    // A single shadow sample cannot bound the accumulated radiance. Doing so erases
    // lit history on every dark sample and prevents a visibility boundary converging.
    return lerp(current, max(0, history), _FogHistoryParams.x * densityAgreement);
#endif
}

#endif
