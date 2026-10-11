#ifndef PBR_TOON_INCLUDED
#define PBR_TOON_INCLUDED

#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/PlanarReflections.hlsl"

// These Flag encoded to GBuffer0.a
#define kToonFlagHairShadow     1 // Hair Shadow mask
#define kToonFlagEyelash        2 // Eyelash
#define kToonFlagHairMask       4 // HairMask
#define kToonFlagFace           8 // Face

float EncodeToonFlags(uint toonFlags)
{
    return toonFlags * (1.0h / 255.0h);
}

uint DecodeToonFlags(float encodedToonFlags)
{
    return uint((encodedToonFlags * 255.0h) + 0.5h);
}

float HasHairShadowFlag(float4 gbuffer0)
{
    uint toonFlags = DecodeToonFlags(gbuffer0.r);
    if ((toonFlags & kToonFlagHairShadow) != 0)
    {
        return 1.0;
    }
    return 0.0;
}

float4 EncodeDepthToRGBA(float value) {
    uint intValue = asuint(value);
    float4 rgba;
    rgba.r = ((intValue >> 24) & 0xFF) / 255.0;
    rgba.g = ((intValue >> 16) & 0xFF) / 255.0;
    rgba.b = ((intValue >> 8) & 0xFF) / 255.0;
    rgba.a = (intValue & 0xFF) / 255.0;
    return rgba;
}

float DecodeRGBAToDepth(float4 rgba) {
    uint r = (uint)(rgba.r * 255.0);
    uint g = (uint)(rgba.g * 255.0);
    uint b = (uint)(rgba.b * 255.0);
    uint a = (uint)(rgba.a * 255.0);
    uint intValue = (r << 24) | (g << 16) | (b << 8) | a;
    return asfloat(intValue);
}


float4 SampleDirectShadowRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float lightRange, float rampY = 0.125)
{
    return SAMPLE_TEXTURE2D(RampTex, RampSampler, float2(lightRange, rampY));
}

float4 SampleDirectSpecularRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float specRange, float rampY = 0.375)
{
    return SAMPLE_TEXTURE2D(RampTex, RampSampler, float2(specRange, rampY));
}

float GetCharacterDirectRimLightArea(float3 normalVS, float2 screenUV, float d, float rimWidth)
{
    // RimLight
    float normalExtendLeftOffset = normalVS.x > 0 ? 1.0 : -1.0;
    normalExtendLeftOffset *= rimWidth * 0.0044;

    float eyeDepth = LinearEyeDepth(d, _ZBufferParams);

    float2 extendUV = screenUV;
    extendUV.x += normalExtendLeftOffset / (eyeDepth + 3.0);

    float extendedRawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_LinearClamp, extendUV, 0).x;
    float extendedEyeDepth = LinearEyeDepth(extendedRawDepth, _ZBufferParams);

    float depthOffset = extendedEyeDepth - eyeDepth;

    float rimArea = saturate(depthOffset * 4);

    return rimArea;
}

float GetCharacterPunctualRimLightArea(float3 lightDirVS, float2 screenUV, float d, float rimWidth)
{
    // RimLight
    float2 normalExtendDirVS = normalize(lightDirVS.xy);
    normalExtendDirVS *= rimWidth * 0.0044;

    float eyeDepth = LinearEyeDepth(d, _ZBufferParams);

    float2 extendUV = screenUV;
    extendUV.xy += normalExtendDirVS.xy / (eyeDepth + 3.0);

    float extendedRawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_LinearClamp, extendUV, 0).x;
    float extendedEyeDepth = LinearEyeDepth(extendedRawDepth, _ZBufferParams);

    float depthOffset = extendedEyeDepth - eyeDepth;

    float rimArea = saturate(depthOffset * 1);

    return rimArea;
}

float3 GetRimColor(float rimArea, float3 albedo, float3 normalVS, float3 lightDirVS, float shadow, float3 frontColor, float3 backColor)
{
    float NdotLVS = dot(normalVS, lightDirVS);

    float frontRim = max(NdotLVS, 0);
    float backRim = max(-NdotLVS, 0);

    float3 frontRimColor = frontRim * frontColor;
    float3 backRimColor = backRim * backColor;
    float3 albedoRimColor = saturate(albedo + 0.3);

    float3 rimColor = (frontRimColor + backRimColor) * albedoRimColor * saturate(shadow + 0.2);
    return rimColor * rimArea;
}


//-----------------------------------------------------------------------------
// Evaluate Environment
// ----------------------------------------------------------------------------

struct DirectLighting
{
    float3 diffuse;
    float3 specular;
};

struct IndirectLighting
{
    float3 diffuse;
    float3 specular;
};

void EvaluateIndirectDiffuse(inout IndirectLighting lighting, float3 diffuseColor, float3 normalWS, float upDirScale, float4 selfEnvColor, float envColorLerp, float diffuseFGD)
{
    float3 SHNormal = lerp(normalWS, float3(0,1,0), upDirScale);
    float3 SHColor = SampleSH9(_AmbientProbeData, SHNormal);
    SHColor = lerp(SHColor, selfEnvColor.rgb, envColorLerp);  
    lighting.diffuse += diffuseFGD * SHColor * diffuseColor;
}

void EvaluateIndirectSpecular_Planar(inout IndirectLighting lighting, float3 positionWS, float3 normalWS,
    float perceptualRoughness, float3 specularFGD, uint renderingLayers, inout float reflectionHierarchyWeight)
{
    float4 reflection = SamplePlanarReflection(positionWS, normalWS, perceptualRoughness, renderingLayers);
    UpdateLightingHierarchyWeights(reflectionHierarchyWeight, reflection.a);
    lighting.specular += specularFGD * reflection.rgb * reflection.a;
}

void EvaluateIndirectSpecular_Cubemap(inout IndirectLighting lighting, TEXTURECUBE_PARAM(textureName, samplerName), float3 reflectDirWS, float perceptualRoughness, float3 specularFGD, 
                                inout float reflectionHierarchyWeight, float weight)
{
    UpdateLightingHierarchyWeights(reflectionHierarchyWeight, weight);
    float mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    float3 cubeReflection = SAMPLE_TEXTURECUBE_LOD(textureName, samplerName, reflectDirWS, mip).xyz;
    lighting.specular += specularFGD * cubeReflection * weight;
}

void EvaluateIndirectSpecular_Sky(inout IndirectLighting lighting, float3 reflectDirWS, float perceptualRoughness, float3 specularFGD, 
                                inout float reflectionHierarchyWeight, float weight)
{
    UpdateLightingHierarchyWeights(reflectionHierarchyWeight, weight);
    float3 skyReflection = SampleSkyEnvironment(reflectDirWS, perceptualRoughness);
    lighting.specular += specularFGD * skyReflection * weight;
}

float3 PostEvaluate(in DirectLighting dirLighting, in IndirectLighting indirLighting, float occlusion, float3 fresnel0, float energyCompensation, float indirDiffInten, float indirSpecInten)
{
    // Apply ambient occlusion.
    indirLighting.diffuse *= occlusion;
    indirLighting.specular *= occlusion;

    return dirLighting.diffuse + indirLighting.diffuse * indirDiffInten
        + (dirLighting.specular + indirLighting.specular * indirSpecInten) * (1.0 + fresnel0 * energyCompensation);
}
#endif /* PBR_TOON_INCLUDED */
