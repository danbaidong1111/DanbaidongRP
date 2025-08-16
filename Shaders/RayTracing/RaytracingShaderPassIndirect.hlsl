
#ifndef RAYTRACING_SHADERPASS_INDIRECT_INCLUDED
#define RAYTRACING_SHADERPASS_INDIRECT_INCLUDED

// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Make sure to add the additional travel distance
    rayIntersection.t = RayTCurrent();
    rayIntersection.cone.width += rayIntersection.t * rayIntersection.cone.spreadAngle;

    // Hit point data.
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);
    PositionInputs posInput = GetPositionInput(rayIntersection.pixelCoord, _ScreenSize.zw, fragInput.positionRWS);

    float3 viewDirWS = -WorldRayDirection();


                
    float3 reflected = float3(0.0, 0.0, 0.0);
    float reflectedWeight = 0.0;
    // Multi bounce indirect if needed
    #ifdef MULTI_BOUNCE_INDIRECT
    if (rayIntersection.remainingDepth < _RaytracingMaxRecursion)
    {
        // TODO: Multi bounce
    }
    #endif /* MULTI_BOUNCE_INDIRECT */

    // Fill the ray context
    RayContext rayContext;
    rayContext.reflection = reflected;
    rayContext.reflectionWeight = reflectedWeight;
    rayContext.transmission = 0.0;
    rayContext.transmissionWeight = 0.0;
    #ifdef MULTI_BOUNCE_INDIRECT
    rayContext.useAPV = _RayTracingDiffuseLightingOnly ? rayIntersection.remainingDepth == _RaytracingMaxRecursion : 1;
    #else
    rayContext.useAPV = 1;
    #endif


    float2 uv = fragInput.texCoord0.xy;
    uv = TRANSFORM_TEX(uv, _BaseMap);
    float4 albedoAlpha = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0);
    float alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
    float3 albedo = albedoAlpha.rgb * _BaseColor.rgb;

#ifndef _EMISSION
    float3 emission = 0;
#else
    float3 emission = SAMPLE_TEXTURE2D_LOD(_EmissionMap, sampler_EmissionMap, uv, 0).rgb * _EmissionColor.rgb;
#endif

    float3 normalWS = fragInput.tangentToWorld[2];

    // Ray traced Lighting
    RayTracingShadingData shadingData = InitRayTracingShadingData(posInput, albedo, _Metallic, _Smoothness, _OcclusionStrength, normalWS, viewDirWS);


    RayTracingLightingOutput output = RayTracedLit(posInput, shadingData, rayContext);


    rayIntersection.color = output.diffuseLighting + output.specularLighting + emission;
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    rayIntersection.color = 0.4;
    IgnoreHit();
}

#endif /* RAYTRACING_SHADERPASS_INDIRECT_INCLUDED */