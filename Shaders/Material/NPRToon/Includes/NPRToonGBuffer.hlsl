#ifndef NPR_TOON_GBUFFER_INCLUDED
#define NPR_TOON_GBUFFER_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonInput.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/PBRToon/PBRToon.hlsl"

// -------------------------------------
// Character GBuffer Encoding
void EncodeCharacterGBuffer(float3 normalWS, float depth, out float4 gbuffer0, out float4 gbuffer1, out float4 gbuffer2)
{
    uint flags = 0;
#if defined(_SHADINGMODE_FACE)
    flags |= kToonFlagFace;
#elif defined(_SHADINGMODE_HAIR)
    flags |= kToonFlagHairMask;
#elif defined(FLAG_HAIRSHADOW)
    flags |= kToonFlagHairShadow;
#elif defined(FLAG_EYELASH)
    flags |= kToonFlagEyelash;
#elif defined(FLAG_HAIRMASK)
    flags |= kToonFlagHairMask;
#endif
    gbuffer0 = float4(0, 0, 0, EncodeToonFlags(flags));
    gbuffer1 = 0;
#if defined(FLAG_EYELASH)
    gbuffer1 = EncodeDepthToRGBA(depth);
#endif
    gbuffer2 = float4(PackNormal(normalWS), 0);
}

#endif // NPR_TOON_GBUFFER_INCLUDED
