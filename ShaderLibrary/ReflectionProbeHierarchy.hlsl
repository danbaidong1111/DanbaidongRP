#ifndef DANBAIDONG_REFLECTION_PROBE_HIERARCHY_INCLUDED
#define DANBAIDONG_REFLECTION_PROBE_HIERARCHY_INCLUDED

bool PlanarProbePrecedesCubemap(uint planarIndex, uint probeIndex)
{
    float2 planarKey = _PlanarReflectionSortKeys[planarIndex].xy;
    float4 boxMin = urp_ReflProbes_BoxMin[probeIndex];
    float3 size = urp_ReflProbes_BoxMax[probeIndex].xyz - boxMin.xyz;
    float volume = size.x * size.y * size.z;
    return planarKey.x > boxMin.w || (planarKey.x == boxMin.w && planarKey.y <= volume);
}

// Two words cover the camera's maximum of 64 reflection probes. The rank mask
// retains cluster culling while visiting receivers in importance/volume order.
float3 EvaluateReflectionProbeHierarchy(uint2 probeRanks, float3 positionWS, float3 normalWS,
    float3 viewDirectionWS, float3 reflectDirWS, float perceptualRoughness, uint renderingLayers,
    inout float hierarchyWeight)
{
    float3 radiance = 0.0;
    uint planarIndex = 0u;
    [loop]
    while (hierarchyWeight < 1.0 && (any(probeRanks != 0u) || planarIndex < (uint)_PlanarReflectionCount))
    {
        bool hasCubemap = any(probeRanks != 0u);
        uint word = probeRanks.x != 0u ? 0u : 1u;
        uint bit = hasCubemap ? firstbitlow(word == 0u ? probeRanks.x : probeRanks.y) : 0u;
        uint probeIndex = hasCubemap ? (uint)urp_ReflProbes_ReceiverOrder[word * 32u + bit].y : 0u;
        bool usePlanar = planarIndex < (uint)_PlanarReflectionCount;
        if (usePlanar && hasCubemap)
            usePlanar = PlanarProbePrecedesCubemap(planarIndex, probeIndex);

        if (usePlanar)
        {
            float4 reflection = SamplePlanarReflectionProbe(planarIndex++, positionWS, normalWS,
                viewDirectionWS, perceptualRoughness, renderingLayers);
            radiance += AccumulateReflection(reflection, hierarchyWeight);
        }
        else
        {
            if (word == 0u) probeRanks.x &= ~(1u << bit);
            else probeRanks.y &= ~(1u << bit);
            float weight = CalculateProbeWeight(positionWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
            UpdateLightingHierarchyWeights(hierarchyWeight, weight);
            if (weight > 0.0)
                radiance += SampleEnvironmentProbe(probeIndex, reflectDirWS, perceptualRoughness) * weight;
        }
    }
    return radiance;
}

float3 EvaluateReflectionProbes(PositionInputs posInput, float3 normalWS, float3 viewDirectionWS,
    float3 reflectDirWS, float perceptualRoughness, uint renderingLayers, inout float hierarchyWeight)
{
    float3 radiance = 0.0;
    // Preserve the existing probe blend exactly when planar captures are absent.
    if (_PlanarReflectionCount == 0)
    {
        radiance = EvaluateEnvProbes(posInput, reflectDirWS, perceptualRoughness, hierarchyWeight);
    }
    else
    {
        uint start = 0u, count = 0u;
        GetCountAndStart(posInput, LIGHTCATEGORY_ENV, start, count);
        uint2 probeRanks = 0u;
        [loop]
        for (uint offset = 0u; offset < count; ++offset)
        {
            uint index = FetchIndex(start, offset);
            if (index == 0xffffffffu)
                break;
            if (index >= (uint)urp_ReflProbes_Count)
                continue;
            uint rank = (uint)urp_ReflProbes_ReceiverOrder[index].x;
            if (rank < 32u) probeRanks.x |= 1u << rank;
            else probeRanks.y |= 1u << (rank - 32u);
        }
        radiance = EvaluateReflectionProbeHierarchy(probeRanks, posInput.positionWS, normalWS, viewDirectionWS,
            reflectDirWS, perceptualRoughness, renderingLayers, hierarchyWeight);
    }
    return radiance;
}

#endif
