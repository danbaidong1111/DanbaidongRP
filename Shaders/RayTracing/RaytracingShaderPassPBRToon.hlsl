
#ifndef RAYTRACING_SHADERPASS_PBRTOON_INCLUDED
#define RAYTRACING_SHADERPASS_PBRTOON_INCLUDED 

// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Make sure to add the additional travel distance
    rayIntersection.t = RayTCurrent();
    // rayIntersection.cone.width += rayIntersection.t * rayIntersection.cone.spreadAngle;

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


    // Tex Sample
    float4 mainTex = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0);
    float4 pbrMask = SAMPLE_TEXTURE2D_LOD(_PBRMask, sampler_PBRMask, uv, 0);
    // float3 bumpTS = UnpackNormalScale(SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, uv, 0), _NormalScale);

    // Property prepare
    float emission          = 1 - pbrMask.a;
    float metallic          = lerp(0, _Metallic, pbrMask.r);
    float smoothness        = lerp(0, _Smoothness, pbrMask.g);
    float occlusion         = lerp(1 - _Occlusion, 1, pbrMask.b);
    float3 albedo           = mainTex.rgb * _BaseColor.rgb;

    float3 emissResult = emission * lerp(_EmissionCol.rgb, _EmissionCol.rgb * albedo.rgb, _EmissionCol.a);


    float3 normalWS = fragInput.tangentToWorld[2];
    rayIntersection.packedNormalWS = PackNormalOctQuadEncode(normalize(normalWS));

    // Ray traced Lighting
    RayTracingShadingData shadingData = InitRayTracingShadingData(posInput, albedo, metallic, smoothness, occlusion, normalWS, viewDirWS);


    RayTracingLightingOutput output = RayTracedToon(posInput, shadingData, rayContext, _SelfLight, _MainLightColorLerp, _SelfEnvColor, _EnvColorLerp);


    rayIntersection.color = output.diffuseLighting + emissResult;
    if (_RayTracingDiffuseLightingOnly == 0)
        rayIntersection.color += output.specularLighting;
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitMain(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Hit point data.
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

    // Compute the distance of the ray
    rayIntersection.t = RayTCurrent();

    bool isVisible = true;
    #if defined(_ALPHATEST_ON)
    float2 uv = fragInput.texCoord0.xy;
    uv = TRANSFORM_TEX(uv, _BaseMap);
    float4 albedoAlpha = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0);
    isVisible = (albedoAlpha.a - _Cutoff) > 0;
    #endif

    // If this fella should be culled, then we cull it
    if (!isVisible)
    {
        IgnoreHit();
        return;
    }
}

#endif /* RAYTRACING_SHADERPASS_PBRTOON_INCLUDED */
