#ifndef DANBAIDONG_VOLUMETRIC_LIGHTING_INTEGRATION_INCLUDED
#define DANBAIDONG_VOLUMETRIC_LIGHTING_INTEGRATION_INCLUDED

float4 IntegratePhysicalFogCell(float4 medium, float length, inout float3 radiance, inout float opticalDepth)
{
    float segmentDepth = medium.a * length;
    radiance += exp(-opticalDepth) * medium.rgb * FogSegmentIntegral(medium.a, length);
    opticalDepth += 0.5 * segmentDepth;
    float4 encoded = max(0, LinearizeRGBD(float4(radiance, opticalDepth)));
    opticalDepth += 0.5 * segmentDepth;
    return encoded;
}

float4 IntegrateToonFogCell(float4 medium, float length, inout float3 radiance, inout float transmittance)
{
    float segmentT = exp(-min(80, medium.a * length));
    float integral = medium.a > 1e-5 ? (1 - segmentT) / medium.a : length;
    radiance += transmittance * medium.rgb * integral;
    transmittance *= segmentT;
    float opticalDepth = -log(max(transmittance, 1e-35));
    return min(60000, LinearizeRGBD(float4(min(radiance, 60000), opticalDepth)));
}

#endif
