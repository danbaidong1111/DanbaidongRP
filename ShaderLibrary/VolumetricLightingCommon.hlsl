#ifndef DANBAIDONG_VOLUMETRIC_LIGHTING_COMMON_INCLUDED
#define DANBAIDONG_VOLUMETRIC_LIGHTING_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
uint _FogViewIndex;
#define DANBAIDONG_VOLUMETRIC_VIEWS
#undef unity_StereoEyeIndex
#define unity_StereoEyeIndex _FogViewIndex
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/AtmosphericFog.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/VolumetricLightingTemporal.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/VolumetricLightingSampling.hlsl"
float4x4 _FogInvViewProjection;
uint3 FogStorageIndex(uint3 cell) { return uint3(cell.xy, cell.z + _FogViewIndex * (uint)_FogGrid.z); }

float FogSliceDepth(float slice)
{
    return FogDecodeDepth(slice);
}

float FogIntervalCount()
{
    return _FogOptions.y > 0.5 ? _FogGrid.z : _FogGrid.z - 1;
}

float3 FogWorldPosition(float2 uv, float eyeDepth)
{
    float3 nearP = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, _FogInvViewProjection);
    float3 farP = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, _FogInvViewProjection);
    float3 ray = normalize(farP - nearP);
    float nearDepth = dot(nearP - _FogCameraPosition.xyz, _FogCameraForward.xyz);
    if (_FogDepthEncoding.w > 0.5 && _FogCameraPosition.w < 0.5)
        return _FogCameraPosition.xyz + ray * eyeDepth;
    return nearP + ray * ((eyeDepth - nearDepth) / max(0.0001, dot(ray, _FogCameraForward.xyz)));
}

#endif
