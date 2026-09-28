#ifndef UNIVERSAL_RESTIR_GI_SPATIAL_INCLUDED
#define UNIVERSAL_RESTIR_GI_SPATIAL_INCLUDED

// The spatial path keeps the selected hit normal/radiance packed. Transport
// without Jacobian only needs hit position and radiance, not decoded hit normals.
struct SpatialGIState
{
    uint4 hit;
    uint4 light;
    uint M;
    float weightSum;
    float selectedTarget;
    float selectedCosine;
    float3 selectedRadiance;
};

void StreamPackedSpatialCandidate(uint4 hit, uint4 light, ReSTIRGISurface surface, float randomValue,
    inout SpatialGIState state)
{
    uint M = light.y >> 16u;
    if (M == 0u)
        return;

    // Black reservoirs still contribute M. Avoid their hit fetch / decoding.
    state.M += M;
    float avgWeight = asfloat(light.z);
    if (avgWeight <= 0.0)
        return;

    float3 toSample = asfloat(hit.xyz) - surface.positionWS;
    float distanceSq = dot(toSample, toSample);
    if (distanceSq <= 1e-8)
        return;
    float cosine = saturate(dot(surface.normalWS, toSample) * rsqrt(distanceSq));
    if (cosine <= 0.0)
        return;

    float3 radiance = float3(f16tof32(light.x & 0xffffu),
        f16tof32(light.x >> 16u), f16tof32(light.y & 0xffffu));
    float target = Luminance(radiance * max(cosine, 0.001));
    float weight = avgWeight * M * target;
    state.weightSum += weight;
    if (weight > 0.0 && randomValue * state.weightSum < weight)
    {
        state.hit = hit;
        state.light = light;
        state.selectedTarget = target;
        state.selectedCosine = cosine;
        state.selectedRadiance = radiance;
    }
}


#endif
