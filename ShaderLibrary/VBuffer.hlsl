#ifndef DANBAIDONG_VBUFFER_INCLUDED
#define DANBAIDONG_VBUFFER_INCLUDED

float FogDecodeDepth(float slice)
{
    if (_FogDepthDecoding.y <= 0)
        return _FogVolume.y * (exp2(slice * log2(1 + _FogVolume.z / _FogVolume.y)) - 1);
    return _FogDepthDecoding.x * exp2(slice * _FogDepthDecoding.y) + _FogDepthDecoding.z;
}

float4 FogDecodeIntegral(float4 value)
{
    float4 decoded = DelinearizeRGBD(value);
    return float4(decoded.rgb, exp(-min(80, decoded.a)));
}

float3 VBufferViewportUV(float3 uvw, float3 viewportSize, float3 textureSize)
{
    // Retained capacity may extend beyond the active grid. Never filter stale texels.
    return clamp(uvw * viewportSize, 0.5, viewportSize - 0.5) / textureSize;
}

float4 FogSampleIntegral(float2 uv, float z)
{
    if (_FogBufferSize.x <= 0)
        return FogDecodeIntegral(SAMPLE_TEXTURE3D_LOD(_FogIntegral, sampler_FogIntegral, float3(uv,z), 0));
    uint width, height, depth;
    _FogIntegral.GetDimensions(width, height, depth);
    float3 textureSize = float3(width, height, depth);
    float3 viewportSize = float3(_FogBufferSize.xy, _FogVolume.w * max(1, _FogViewCount));
    if (_FogFilterMode > 0.5)
        return FogDecodeIntegral(SAMPLE_TEXTURE3D_LOD(_FogIntegral, sampler_FogIntegral,
            VBufferViewportUV(float3(uv,z), viewportSize, textureSize), 0));
    float2 position = uv * _FogBufferSize.xy;
    float2 base = floor(position), fraction = frac(position);
    float2 weights[2], offsets[2];
    BiquadraticFilter(1 - fraction, weights, offsets);
    float4 value = 0;
    for (int y = 0; y < 2; ++y)
    for (int x = 0; x < 2; ++x)
    {
        float2 tap = (base + float2(offsets[x].x, offsets[y].y)) * _FogBufferSize.zw;
        value += weights[x].x * weights[y].y * SAMPLE_TEXTURE3D_LOD(_FogIntegral, sampler_FogIntegral,
            VBufferViewportUV(float3(tap,z), viewportSize, textureSize), 0);
    }
    return FogDecodeIntegral(value);
}

float FogEncodeDepth(float distance)
{
    if (_FogDepthEncoding.y <= 0)
        return log2(1 + distance / _FogVolume.y) / log2(1 + _FogVolume.z / _FogVolume.y);
    return _FogDepthEncoding.x + _FogDepthEncoding.y * log2(max(1e-6, distance - _FogDepthEncoding.z));
}

#endif
