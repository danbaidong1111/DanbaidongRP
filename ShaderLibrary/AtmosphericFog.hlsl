#ifndef DANBAIDONG_ATMOSPHERIC_FOG_INCLUDED
#define DANBAIDONG_ATMOSPHERIC_FOG_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

float _FogEnabled;
#include "Packages/com.unity.render-pipelines.danbaidong/Runtime/Fog/ShaderVariablesAtmosphericFog.cs.hlsl"
float4x4 _FogRayInvViewProjection;
float4x4 _FogRayViewProjection;
float4x4 _FogStereoViewProjection[2], _FogStereoInvViewProjection[2];
float4 _FogStereoPosition[2], _FogStereoForward[2];
TEXTURE3D(_FogIntegral);
SAMPLER(sampler_FogIntegral);
TEXTURECUBE(_FogSkyRadiance);

float4 FogGetCameraPosition()
{
#if defined(USING_STEREO_MATRICES)
    return _FogStereoPosition[unity_StereoEyeIndex];
#else
    return _FogCameraPosition;
#endif
}
float3 FogGetCameraForward()
{
#if defined(USING_STEREO_MATRICES)
    return _FogStereoForward[unity_StereoEyeIndex].xyz;
#else
    return _FogCameraForward.xyz;
#endif
}
float4x4 FogGetViewProjection()
{
#if defined(USING_STEREO_MATRICES)
    return _FogStereoViewProjection[unity_StereoEyeIndex];
#else
    return _FogRayViewProjection;
#endif
}
float4x4 FogGetInverseViewProjection()
{
#if defined(USING_STEREO_MATRICES)
    return _FogStereoInvViewProjection[unity_StereoEyeIndex];
#else
    return _FogRayInvViewProjection;
#endif
}

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/VBuffer.hlsl"

float FogDensityAtHeight(float height, float4 layer)
{
    float relativeHeight = height - layer.y;
    if (_FogAdvanced.x > 0.5) relativeHeight = max(0, relativeHeight);
    return layer.x * exp(clamp(-relativeHeight * layer.z, -80, 20));
}

float FogRayStartDistance(float3 origin, float3 direction)
{
    float signedDistance = dot(_FogReflectionPlane.xyz, origin) + _FogReflectionPlane.w;
    if (dot(_FogReflectionPlane.xyz, _FogReflectionPlane.xyz) < 0.5 || signedDistance >= 0.0)
        return 0.0;
    float facing = dot(_FogReflectionPlane.xyz, direction);
    return facing > 1e-5 ? -signedDistance / facing : 1e20;
}

// Integral of exponential height density, with a stable horizontal-ray limit.
float FogOpticalDepth(float3 origin, float3 direction, float distance, float4 layer)
{
    float begin = min(distance, layer.w);
    float length = max(0, distance - begin);
    float h = origin.y + direction.y * begin - layer.y;
    float constantLength = 0;
    if (_FogAdvanced.x > 0.5)
    {
        float endHeight = h + direction.y * length;
        if (max(h, endHeight) <= 0) return min(80, layer.x * length);
        if (min(h, endHeight) < 0)
        {
            constantLength = min(length, -min(h, endHeight) / max(abs(direction.y), 1e-6));
            length -= constantLength;
            h = max(h, 0);
        }
    }
    // Evaluate from the denser endpoint so long downward rays never multiply
    // an underflowed starting density by an overflowing exponential.
    float denseHeight = min(h, h + direction.y * length);
    float peakDensity = layer.x * exp(clamp(-denseHeight * layer.z, -80.0, 20.0));
    float x = abs(direction.y * length * layer.z);
    float integral = abs(x) < 0.01 ? (1.0 - 0.5 * x + x * x / 6.0) : (1.0 - exp(clamp(-x, -40.0, 20.0))) / x;
    return min(80.0, max(0.0, peakDensity * length * integral + layer.x * constantLength));
}

float FogPhase(float cosine, float g)
{
    g = clamp(g, -0.999, 0.999);
    cosine = clamp(cosine, -1, 1);
    float denominator = max(1.192092896e-7, 1 + g*g - 2*g*cosine);
    float reciprocal = rsqrt(denominator);
    return abs(g) < 1e-5 ? INV_FOUR_PI
        : (3 / (8 * PI)) * (1 - g*g) / (2 + g*g) * (1 + cosine*cosine) * reciprocal * reciprocal * reciprocal;
}

float3 GetFogColor(float3 direction, float distance)
{
    if (_FogAdvanced.y < 0.5)
        return _FogColor.rgb;

    float mip = saturate((distance - _FogSkyMip.x) / max(0.001, _FogSkyMip.y - _FogSkyMip.x));
    return SAMPLE_TEXTURECUBE_LOD(_FogSkyRadiance, sampler_LinearClamp, direction,
        (1 - mip * _FogSkyMip.z) * _FogSkyMip.w).rgb * _FogSkyTint.rgb;
}

float PhysicalFogOpticalDepth(float3 origin, float3 direction, float distance)
{
    float scaleHeight = rcp(_FogDensity.z);
    return OpticalDepthHeightFog(_FogDensity.x, _FogDensity.y, float2(_FogDensity.z, scaleHeight),
        direction.y, origin.y, distance);
}

float EvaluateFogOpticalDepth(float3 origin, float3 direction, float distance)
{
    if (_FogOptions.y > 0.5)
        return PhysicalFogOpticalDepth(origin, direction, distance);

    return FogOpticalDepth(origin, direction, distance, _FogDensity)
         + FogOpticalDepth(origin, direction, distance, _FogGround);
}

float3 FogComposite(float3 color, float3 scattering, float transmittance, float maximumOpacity)
{
    float opacity = 1.0 - saturate(transmittance);
    float scale = opacity > maximumOpacity ? maximumOpacity / max(opacity, 1e-6) : 1.0;
    return color * (1.0 - opacity * scale) + max(0.0, scattering) * scale;
}

float3 ApplyAtmosphericFog(float3 color, float3 positionWS, float2 uv, out float fogTransmittance)
{
    fogTransmittance = 1;
    if (_FogEnabled < 0.5) return color;
    float4 cameraPosition = FogGetCameraPosition();
    float3 cameraForward = FogGetCameraForward();
    float3 origin = cameraPosition.xyz;
    float3 ray = positionWS - origin;
    // Orthographic rays begin at the camera plane at the same screen coordinate.
    if (cameraPosition.w > 0.5)
    {
        origin = positionWS - cameraForward * dot(ray, cameraForward);
        ray = positionWS - origin;
    }
    float3 direction = normalize(ray + 1e-8);
    float rayStart = FogRayStartDistance(origin, direction);
    float distance = min(max(0.0, length(ray) - rayStart), _FogOptions.x);
    if (distance <= 0.0) return color;
    origin += direction * rayStart;
    float opticalDepth = EvaluateFogOpticalDepth(origin, direction, distance);
    float opacity = 1.0 - exp(-opticalDepth);
    float3 fogColor = GetFogColor(direction, distance);
    if (_FogOptions.y < 0.5)
    {
        float sunPhase = FogPhase(dot(direction, _FogSunDirection.xyz), _FogScatter.x);
        fogColor += _FogSunColor.rgb * sunPhase * _FogScatter.y * _FogSunColor.a;
    }
    else if (_FogVolume.x > 0.5)
    {
        fogColor *= _FogAlbedo.rgb;
    }

    if (_FogVolume.x > 0.5)
    {
        float rayForward = max(0.001, dot(direction, cameraForward));
        float eyeDepth = _FogDepthEncoding.w > 0.5 ? rayStart + distance : (rayStart + distance) * rayForward;
        float slice = saturate(FogEncodeDepth(min(eyeDepth, _FogVolume.z)));
        float z = _FogOptions.y > 0.5
            ? slice - 0.8660254 / _FogVolume.w
            : (slice * (_FogVolume.w - 1.0) + 0.5) / _FogVolume.w;
        float4 gridPositionCS = mul(FogGetViewProjection(), float4(positionWS, 1.0));
        float2 gridUV = gridPositionCS.xy / max(gridPositionCS.w, 1e-6) * 0.5 + 0.5;
        #if UNITY_UV_STARTS_AT_TOP
        gridUV.y = 1.0 - gridUV.y;
        #endif
        #if defined(USING_STEREO_MATRICES)
        z = (clamp(z, 0.5 / _FogVolume.w, 1 - 0.5 / _FogVolume.w) + unity_StereoEyeIndex) / max(1, _FogViewCount);
        #endif
        float4 volume = FogSampleIntegral(gridUV, z);
        float lastSliceDistance = _FogOptions.y > 0.5
            ? FogDecodeDepth(1 - 0.5 / _FogVolume.w) : _FogVolume.z;
        float integratedDistance = clamp(lastSliceDistance / (_FogDepthEncoding.w > 0.5 ? 1 : rayForward) - rayStart, 0.0, distance);
        float integratedOpticalDepth = EvaluateFogOpticalDepth(origin, direction, integratedDistance);
        float tailOpticalDepth = _FogOptions.y > 0.5
            ? PhysicalFogOpticalDepth(origin + direction * integratedDistance, direction, distance - integratedDistance)
            : max(0, opticalDepth - integratedOpticalDepth);
        float tailOpacity = 1.0 - exp(-tailOpticalDepth);
        float transmittance = saturate(volume.a) * (1.0 - tailOpacity);
        float3 scattering = max(0, volume.rgb) + fogColor * tailOpacity * saturate(volume.a);
        fogTransmittance = 1 - min(1 - transmittance, _FogColor.a);
        return FogComposite(color, scattering, transmittance, _FogColor.a);
    }
    fogTransmittance = 1 - min(opacity, _FogColor.a);
    return lerp(color, fogColor, min(opacity, _FogColor.a));
}

float3 ApplyAtmosphericFog(float3 color, float3 positionWS, float2 uv)
{
    float transmittance;
    return ApplyAtmosphericFog(color, positionWS, uv, transmittance);
}
#endif
