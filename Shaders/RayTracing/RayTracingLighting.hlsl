#ifndef UNITY_RAYTRACING_LIGHTING_INCLUDED
#define UNITY_RAYTRACING_LIGHTING_INCLUDED

/*
 * This file defines ray tracing lighting helper. Used for ray traced object's point lighting.
 * Ray traced lighting shloud depends on RayTraced-cluster lights and env lights.
 */

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

// TODO: We should use ray tracing cluster light.
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/GPUCulledLights.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/PreIntegratedFGD.hlsl"

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/GlobalIllumination.hlsl"

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Shadows.hlsl"


struct RayContext
{
    // Signal that came from a bounced ray
    float3 reflection;
    // Weight for the bounced ray
    float reflectionWeight;

    // Signal that came from a transmitted ray
    float3 transmission;
    // Weight for the transmitted ray
    float transmissionWeight;

    // Should the APV be used for the lightloop ? (in case of multibounce GI)
    int useAPV;
};

struct RayTracingLightingOutput
{
    float3 diffuseLighting;
    float3 specularLighting;
};

struct RayTracingShadingData
{
    float3 normalWS;
    float3 viewDirWS; // this could be ray dir.

    float3 albedo;
    float metallic;
    float occlusion;
    float smoothness;
    // uint materialFlags;

    float perceptualRoughness;
    float roughness;
    float roughness2;

    float3 diffuseColor;
    float3 fresnel0;
};

RayTracingShadingData InitRayTracingShadingData(PositionInputs posInput, float3 albedo, float metallic, float smoothness, float occlusion, float3 normalWS, float3 viewDirWS)
{
    RayTracingShadingData shadingData;
    ZERO_INITIALIZE(RayTracingShadingData, shadingData);

    shadingData.normalWS            = normalWS;
    shadingData.viewDirWS           = viewDirWS;

    shadingData.albedo              = albedo.rgb;
    shadingData.metallic            = metallic;
    shadingData.occlusion           = occlusion;
    shadingData.smoothness          = smoothness;
    // shadingData.materialFlags       = UnpackMaterialFlags(gbuffer0.a);

    shadingData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(shadingData.smoothness);
    shadingData.roughness           = PerceptualRoughnessToRoughness(shadingData.perceptualRoughness); // We need to max this with Angular Diameter, which result in minRoughness.
    shadingData.roughness2          = max(shadingData.roughness * shadingData.roughness, FLT_MIN);
    
    shadingData.diffuseColor        = ComputeDiffuseColor(shadingData.albedo, shadingData.metallic);
    shadingData.fresnel0            = ComputeFresnel0(shadingData.albedo, shadingData.metallic, DEFAULT_SPECULAR_VALUE);

    return shadingData;
}

RayTracingLightingOutput RayTracedLit(PositionInputs posInput, RayTracingShadingData shadingData, RayContext rayContext)
{
    RayTracingLightingOutput lightOutput;
    ZERO_INITIALIZE(RayTracingLightingOutput, lightOutput);

    float3 positionWS       = posInput.positionWS;
    float3 normalWS         = shadingData.normalWS;
    float3 viewDirWS        = shadingData.viewDirWS;
    bool includeSpecular = _RayTracingDiffuseLightingOnly == 0;


    float  NdotV = dot(normalWS, viewDirWS);
    float  clampedNdotV = ClampNdotV(NdotV);
    float3 specularFGD;
    float  diffuseFGD;
    float  reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, shadingData.perceptualRoughness, shadingData.fresnel0, specularFGD, diffuseFGD, reflectivity);
    float energyCompensation = 1.0 / reflectivity - 1.0;

    float3 directDiffuse = 0;
    float3 directSpecular = 0;
    float3 indirectDiffuse = 0;
    float3 indirectSpecular = 0;

    // Shading
    // Accumulate Direct (Directional Lights, Punctual Lights, TODO: Area Lights)
        uint dirLightIndex = 0;
        for (dirLightIndex = 0; dirLightIndex < _DirectionalLightCount; dirLightIndex++)
        {

            DirectionalLightData dirLight = g_DirectionalLightDatas[dirLightIndex];
            #ifdef _LIGHT_LAYERS
            if (IsMatchingLightLayer(dirLight.lightLayerMask, shadingData.meshRenderingLayers))
            #endif
            {
                float3 lightDirWS = dirLight.lightDirection;
                float NdotL = dot(normalWS, lightDirWS);
                
                float clampedNdotL = saturate(NdotL);
                // float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);
                float diffTerm = Lambert();

                diffTerm *= clampedNdotL;
                directDiffuse += shadingData.diffuseColor * diffTerm * dirLight.lightColor;
                if (includeSpecular)
                {
                    float clampedRoughness = max(shadingData.roughness, dirLight.minRoughness);
                    float LdotV, NdotH, LdotH, invLenLV;
                    GetBSDFAngle(viewDirWS, lightDirWS, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);
                    float3 F = F_Schlick(shadingData.fresnel0, LdotH);
                    float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
                    directSpecular += F * DV * clampedNdotL * dirLight.lightColor;
                }
            }

        }
        // Apply Shadows
        // TODO: add different direct light shadowmap
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
        half4 shadowParams = GetMainLightShadowParams();
half realtimeShadow = MainLightRealtimeShadow(shadowCoord);
// half realtimeShadow = SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, false);

        float shadowAttenuation = realtimeShadow;

        directDiffuse *= shadowAttenuation;
        directSpecular *= shadowAttenuation;


        // TODO: Punctual Lights




    // Accumulate Indirect (Reflection probe, ScreenSpace Reflection/Refraction)
    // Reflection / Refraction hierarchy is
    //  1. Screen Space Refraction / Reflection
    //  2. Environment Reflection / Refraction
    //  3. Sky Reflection / Refraction
    
    float3 SHColor = SampleSH9(_AmbientProbeData, normalWS) * _RayTracingAmbientProbeDimmer;//EvaluateAmbientProbe(normalWS);
    indirectDiffuse += diffuseFGD * SHColor * shadingData.diffuseColor;
    // TODO: ModifyBakedDiffuseLighting Function


    if (includeSpecular)
    {
        float3 reflectDirWS = reflect(-viewDirWS, normalWS);
        float3 specDominantDirWS = GetSpecularDominantDir(normalWS, reflectDirWS, shadingData.perceptualRoughness, clampedNdotV);
        // When we are rough, we tend to see outward shifting of the reflection when at the boundary of the projection volume
        // Also it appear like more sharp. To avoid these artifact and at the same time get better match to reference we lerp to original unmodified reflection.
        // Formula is empirical.
        reflectDirWS = lerp(specDominantDirWS, reflectDirWS, saturate(smoothstep(0, 1, shadingData.roughness2)));
        float3 envReflection = SampleSkyEnvironment(reflectDirWS, shadingData.perceptualRoughness).rgb;
        indirectSpecular += specularFGD * envReflection;
    }

    // Post evaluate indirect diffuse or energy.
    indirectDiffuse *= shadingData.occlusion;
    indirectSpecular *= shadingData.occlusion;
    lightOutput.diffuseLighting = directDiffuse + indirectDiffuse;
    if (includeSpecular)
        lightOutput.specularLighting = (directSpecular + indirectSpecular)
            * (1.0 + shadingData.fresnel0 * energyCompensation);

    return lightOutput;
}


RayTracingLightingOutput RayTracedToon(PositionInputs posInput, RayTracingShadingData shadingData, RayContext rayContext, float4 selfLight, float selfLightLerp, float4 selfEnvColor, float selfEnvLerp)
{
    RayTracingLightingOutput lightOutput;
    ZERO_INITIALIZE(RayTracingLightingOutput, lightOutput);

    float3 positionWS       = posInput.positionWS;
    float3 normalWS         = shadingData.normalWS;
    float3 viewDirWS        = shadingData.viewDirWS;
    bool includeSpecular = _RayTracingDiffuseLightingOnly == 0;


    float  NdotV = dot(normalWS, viewDirWS);
    float  clampedNdotV = ClampNdotV(NdotV);
    float3 specularFGD;
    float  diffuseFGD;
    float  reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, shadingData.perceptualRoughness, shadingData.fresnel0, specularFGD, diffuseFGD, reflectivity);
    float energyCompensation = 1.0 / reflectivity - 1.0;

    float3 directDiffuse = 0;
    float3 directSpecular = 0;
    float3 indirectDiffuse = 0;
    float3 indirectSpecular = 0;

    // Shading
    // Accumulate Direct (Directional Lights, Punctual Lights, TODO: Area Lights)
        uint dirLightIndex = 0;
        for (dirLightIndex = 0; dirLightIndex < _DirectionalLightCount; dirLightIndex++)
        {

            DirectionalLightData dirLight = g_DirectionalLightDatas[dirLightIndex];
            #ifdef _LIGHT_LAYERS
            if (IsMatchingLightLayer(dirLight.lightLayerMask, shadingData.meshRenderingLayers))
            #endif
            {
                dirLight.lightColor = lerp(dirLight.lightColor, selfLight.rgb, selfLightLerp);

                float3 lightDirWS = dirLight.lightDirection;
                float NdotL = dot(normalWS, lightDirWS);
                
                float clampedNdotL = saturate(NdotL);
                float diffTerm = Lambert();
                diffTerm *= SigmoidSharp(NdotL * 0.5 + 0.5, 0.5, 5);

                directDiffuse += shadingData.diffuseColor * diffTerm * dirLight.lightColor;
                if (includeSpecular)
                {
                    float clampedRoughness = max(shadingData.roughness, dirLight.minRoughness);
                    float LdotV, NdotH, LdotH, invLenLV;
                    GetBSDFAngle(viewDirWS, lightDirWS, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);
                    float3 F = F_Schlick(shadingData.fresnel0, LdotH);
                    float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
                    directSpecular += F * DV * clampedNdotL * dirLight.lightColor;
                }
            }

        }
        // Apply Shadows
        // TODO: add different direct light shadowmap
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
        half4 shadowParams = GetMainLightShadowParams();
half realtimeShadow = MainLightRealtimeShadow(shadowCoord);
// half realtimeShadow = SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, false);

        float shadowAttenuation = realtimeShadow;

        directDiffuse *= shadowAttenuation;
        directSpecular *= shadowAttenuation;


        // TODO: Punctual Lights




    // Accumulate Indirect (Reflection probe, ScreenSpace Reflection/Refraction)
    // Reflection / Refraction hierarchy is
    //  1. Screen Space Refraction / Reflection
    //  2. Environment Reflection / Refraction
    //  3. Sky Reflection / Refraction
    
    float3 SHColor = SampleSH9(_AmbientProbeData, normalWS) * _RayTracingAmbientProbeDimmer;
    SHColor = lerp(SHColor, selfEnvColor.rgb, selfEnvLerp);
    indirectDiffuse += diffuseFGD * SHColor * shadingData.diffuseColor;
    // TODO: ModifyBakedDiffuseLighting Function


    if (includeSpecular)
    {
        float3 reflectDirWS = reflect(-viewDirWS, normalWS);
        float3 specDominantDirWS = GetSpecularDominantDir(normalWS, reflectDirWS, shadingData.perceptualRoughness, clampedNdotV);
        // When we are rough, we tend to see outward shifting of the reflection when at the boundary of the projection volume
        // Also it appear like more sharp. To avoid these artifact and at the same time get better match to reference we lerp to original unmodified reflection.
        // Formula is empirical.
        reflectDirWS = lerp(specDominantDirWS, reflectDirWS, saturate(smoothstep(0, 1, shadingData.roughness2)));
        float3 envReflection = SampleSkyEnvironment(reflectDirWS, shadingData.perceptualRoughness).rgb;
        indirectSpecular += specularFGD * envReflection;
    }

    // Post evaluate indirect diffuse or energy.
    indirectDiffuse *= shadingData.occlusion;
    indirectSpecular *= shadingData.occlusion;
    lightOutput.diffuseLighting = directDiffuse + indirectDiffuse;
    if (includeSpecular)
        lightOutput.specularLighting = (directSpecular + indirectSpecular)
            * (1.0 + shadingData.fresnel0 * energyCompensation);

    return lightOutput;
}

#endif
