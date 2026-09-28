#ifndef UNIVERSAL_RESTIR_GI_RESERVOIR_INCLUDED
#define UNIVERSAL_RESTIR_GI_RESERVOIR_INCLUDED

struct PackedGIReservoir
{
    uint4 creationGeometry; // Visible point position + normal.
    uint4 hitGeometry;      // Hit point position + normal.
    uint4 lightInfo;        // Radiance + reservoir info.
};

struct GIReservoir
{
    float3 creationPoint;   // Visible point's position.
    float3 creationNormal;  // Visible point's normal.
    float3 position;        // Hit point's position.
    float3 normal;          // Hit point's normal.
    float3 radiance;        // Chosen sample's radiance.
    int M;                  // Input sample count.
    float avgWeight;        // Average weight for chosen sample.
    uint age;               // Number of frames the sample has survived.
};

struct ReSTIRGISurface
{
    float3 positionWS;
    float3 normalWS;
};

uint encodeGIReservoirNormal(float3 n)
{
    float2 oct = PackNormalOctQuadEncode(n) * 0.5 + 0.5;
    return f32tof16(oct.x) | (f32tof16(oct.y) << 16);
}

float3 decodeGIReservoirNormal(uint encoded)
{
    float2 oct = float2(f16tof32(encoded & 0xffff), f16tof32(encoded >> 16)) * 2.0 - 1.0;
    return UnpackNormalOctQuadEncode(oct);
}

GIReservoir CreateEmptyGIReservoir()
{
    GIReservoir reservoir = (GIReservoir)0;
    reservoir.creationPoint = 0.0;
    reservoir.creationNormal = float3(0.0, 0.0, 1.0);
    reservoir.position = float3(0.0, 0.0, 1.0);
    reservoir.normal = float3(0.0, 0.0, -1.0);
    reservoir.radiance = 0.0;
    reservoir.M = 0;
    reservoir.avgWeight = 1.0;
    reservoir.age = 0u;
    return reservoir;
}

PackedGIReservoir PackGIReservoir(GIReservoir reservoir)
{
    PackedGIReservoir packed = (PackedGIReservoir)0;
    uint packedM = (uint)clamp(reservoir.M, 0, 0xffff);
    packed.creationGeometry.xyz = asuint(reservoir.creationPoint);
    packed.creationGeometry.w = encodeGIReservoirNormal(reservoir.creationNormal);
    packed.hitGeometry.xyz = asuint(reservoir.position);
    packed.hitGeometry.w = encodeGIReservoirNormal(reservoir.normal);
    packed.lightInfo.x = f32tof16(reservoir.radiance.x) | (f32tof16(reservoir.radiance.y) << 16);
    packed.lightInfo.y = f32tof16(reservoir.radiance.z) | (packedM << 16);
    packed.lightInfo.z = asuint(reservoir.avgWeight);
    packed.lightInfo.w = reservoir.age;
    return packed;
}

GIReservoir UnPackGIReservoir(PackedGIReservoir packed)
{
    GIReservoir reservoir = (GIReservoir)0;
    reservoir.creationPoint = asfloat(packed.creationGeometry.xyz);
    reservoir.creationNormal = decodeGIReservoirNormal(packed.creationGeometry.w);
    reservoir.position = asfloat(packed.hitGeometry.xyz);
    reservoir.normal = decodeGIReservoirNormal(packed.hitGeometry.w);
    reservoir.radiance.x = f16tof32(packed.lightInfo.x & 0xffff);
    reservoir.radiance.y = f16tof32(packed.lightInfo.x >> 16);
    reservoir.radiance.z = f16tof32(packed.lightInfo.y & 0xffff);
    reservoir.M = (int)(packed.lightInfo.y >> 16);
    reservoir.avgWeight = asfloat(packed.lightInfo.z);
    reservoir.age = packed.lightInfo.w;
    return reservoir;
}

bool isReservoirValid(GIReservoir reservoir)
{
    return reservoir.M > 0 && reservoir.avgWeight > 0.0;
}

// A zero-weight reservoir can still represent samples that must contribute to
// M, even though it has no sample that can contribute to the final estimate.
bool hasReservoirCandidates(GIReservoir reservoir)
{
    return reservoir.M > 0;
}

float evalTargetFunction(float3 radiance, float3 normal, float3 position, float3 samplePosition)
{
    float3 toSample = samplePosition - position;
    float distSq = dot(toSample, toSample);
    if (distSq <= 1e-8)
        return 0.0;

    float3 L = SafeNormalize(toSample);

    float NdotL = dot(normal, L);
    if (NdotL <= 0.0)
        return 0.0;

    float fCos = max(0.001f, saturate(NdotL));
    float targetPdf = Luminance(radiance * fCos);
    return targetPdf;

    // Option: None ReSTIR targetFunction.
    // if (dot(normal, SafeNormalize(toSample)) <= 0.0)
    //     return 0.0;
    // return max(Luminance(radiance), 0.0);
}

float evalTargetFunction(GIReservoir reservoir, ReSTIRGISurface surface)
{
    return evalTargetFunction(reservoir.radiance, surface.normalWS, surface.positionWS, reservoir.position);
}

bool updateReservoir(float weight, GIReservoir srcReservoir, float randomValue, inout float weightSum, inout GIReservoir dstReservoir)
{
    weightSum += weight;
    dstReservoir.M += srcReservoir.M;

    bool isUpdate = (weight > 0.0) && (randomValue * weightSum < weight);
    if (isUpdate)
    {
        dstReservoir.position = srcReservoir.position;
        dstReservoir.normal = srcReservoir.normal;
        dstReservoir.radiance = srcReservoir.radiance;
        dstReservoir.age = srcReservoir.age;
    }

    return isUpdate;
}

float3 evaluateReservoirContribution(GIReservoir reservoir, ReSTIRGISurface surface)
{
    if (!isReservoirValid(reservoir))
        return 0.0;

    float3 toSample = reservoir.position - surface.positionWS;
    float distSq = dot(toSample, toSample);
    if (distSq <= 1e-8)
        return 0.0;

    float3 L = toSample * rsqrt(distSq);
    float NdotL = saturate(dot(surface.normalWS, L));
    if (NdotL <= 0.0)
        return 0.0;

    return reservoir.radiance * NdotL;
}

#endif
