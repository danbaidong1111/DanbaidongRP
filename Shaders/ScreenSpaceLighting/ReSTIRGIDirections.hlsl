#ifndef UNIVERSAL_RESTIR_GI_DIRECTIONS_INCLUDED
#define UNIVERSAL_RESTIR_GI_DIRECTIONS_INCLUDED

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/BlueNoise.hlsl"

float3 SampleReSTIRGICosineDirection(uint2 sourceCoord, uint frame, uint candidate)
{
    // Keep the precomputed STBN cosine distribution (including the 1-spp path).
    // Translate the entire tiled mask after each 64-frame cycle, rather than
    // changing its directions independently per pixel and losing blue-noise structure.
    uint2 noiseCoord = sourceCoord + (frame >> 6u) * uint2(47u, 73u);
    return GetSpatiotemporalBlueNoiseUnitVec3Cosine(noiseCoord, candidate * 17u);
}

#endif
