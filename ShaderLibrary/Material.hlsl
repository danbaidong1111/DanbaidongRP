#ifndef DANBAIDONG_MATERIAL_INCLUDED
#define DANBAIDONG_MATERIAL_INCLUDED

//-----------------------------------------------------------------------------
// Reflection / Refraction hierarchy handling
//-----------------------------------------------------------------------------

// This function is use with reflection and refraction hierarchy of LightLoop
// It will add weight to hierarchyWeight but ensure that hierarchyWeight is not more than one
// by updating the weight value. Returned weight value must be apply on current lighting
// Example: Total hierarchyWeight is 0.8 and weight is 0.4. Function return hierarchyWeight of 1.0 and weight of 0.2
// hierarchyWeight and weight must be positive and between 0 and 1
void UpdateLightingHierarchyWeights(inout float hierarchyWeight, inout float weight)
{
    float accumulatedWeight = hierarchyWeight + weight;
    hierarchyWeight = saturate(accumulatedWeight);
    weight -= saturate(accumulatedWeight - hierarchyWeight);
}


// Reflection textures contain unweighted radiance and a separate contribution weight.
float3 AccumulateReflection(float4 reflection, inout float hierarchyWeight)
{
    UpdateLightingHierarchyWeights(hierarchyWeight, reflection.a);
    return reflection.rgb * reflection.a;
}

float3 EvaluateTracedReflection(float4 reflection, bool receiveTracedReflections, inout float hierarchyWeight)
{
    if (!receiveTracedReflections)
        return 0.0;
    return AccumulateReflection(reflection, hierarchyWeight);
}

#endif /* DANBAIDONG_MATERIAL_INCLUDED */