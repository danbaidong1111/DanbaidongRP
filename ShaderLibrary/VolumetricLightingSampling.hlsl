#ifndef DANBAIDONG_FOG_SAMPLING_INCLUDED
#define DANBAIDONG_FOG_SAMPLING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

float2 FogSampleOffset2D(uint sequence)
{
    const float radius = 0.1705406887;
    const float height = radius * 1.7320508076;
    float2 pattern[7] = { float2(0,0), float2(-2*radius,0), float2(2*radius,0),
        float2(-radius,-height), float2(radius,height), float2(radius,-height), float2(-radius,height) };
    float2 p = pattern[sequence % 7];
    return float2(p.x * 0.9659258263 - p.y * 0.2588190451, p.x * 0.2588190451 + p.y * 0.9659258263);
}

float PhysicalFogSampleOffset(uint2 pixel, uint sequence, bool temporal)
{
    const float offsets[7] = { 7.0 / 14.0, 3.0 / 14.0, 11.0 / 14.0,
        5.0 / 14.0, 9.0 / 14.0, 1.0 / 14.0, 13.0 / 14.0 };
    return frac(GenerateHashedRandomFloat(pixel) + (temporal ? offsets[sequence % 7] : 0.5));
}

float FogSegmentIntegral(float extinction, float length)
{
    float d = extinction * length;
    return d < 0.001 ? length * (1 - d * 0.5 + d*d / 6) : (1 - exp(-min(80, d))) / extinction;
}

float FogSampleMedium(float random, float extinction, float length)
{
    float d = extinction * length;
    return d < 0.001 ? length * random : -log(max(1e-30, 1 - random * (1 - exp(-min(80, d))))) / extinction;
}

void FogSamplePunctual(float random, float3 lightPosition, float radius, float3 origin, float3 direction,
    float tMin, float tMax, out float distance, out float reciprocalPdf)
{
    float3 toLight = lightPosition - origin;
    float projection = dot(toLight, direction);
    float perpendicular2 = max(1e-6, dot(toLight, toLight) - projection*projection + radius*radius);
    float perpendicular = sqrt(perpendicular2);
    float a = tMin - projection, b = tMax - projection;
    float angle = atan2(perpendicular * (b-a), perpendicular2 + a*b);
    float theta = atan(a / perpendicular) + random * angle;
    float relative = perpendicular * tan(theta);
    distance = clamp(projection + relative, tMin, tMax);
    reciprocalPdf = max(0, angle * (perpendicular2 + relative*relative) / perpendicular);
}


#endif
