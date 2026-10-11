#ifndef DANBAIDONG_TRANSPARENT_FOG_INCLUDED
#define DANBAIDONG_TRANSPARENT_FOG_INCLUDED
float3 ApplyDanbaidongTransparentFog(float3 color, float alpha, float4 positionCS, float blendMode)
{
    if (_FogEnabled < 0.5) return color;
    float2 uv = GetNormalizedScreenSpaceUV(positionCS.xy);
    float depth = positionCS.z;
    #if !UNITY_REVERSED_Z
        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
    #endif
    float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
    if (blendMode > 1.5 && blendMode < 2.5)
    {
        // Additive emission is attenuated; the background already contains in-scattering.
        return ApplyAtmosphericFog(color, positionWS, uv) - ApplyAtmosphericFog(0, positionWS, uv);
    }
    if (blendMode > 2.5)
    {
        // Multiplicative color approaches the neutral multiplier in dense fog.
        return 1.0 + ApplyAtmosphericFog(color, positionWS, uv) - ApplyAtmosphericFog(1, positionWS, uv);
    }
    #if defined(_ALPHAPREMULTIPLY_ON)
        const bool premultiplied = true;
    #else
        bool premultiplied = blendMode > 0.5 && blendMode < 1.5;
    #endif
    if (premultiplied)
    {
        float3 scattering = ApplyAtmosphericFog(0, positionWS, uv);
        return ApplyAtmosphericFog(color, positionWS, uv) - scattering * (1 - alpha);
    }
    return ApplyAtmosphericFog(color, positionWS, uv);
}

float3 ApplyDanbaidongTransparentFog(float3 color, float alpha, float4 positionCS)
{
    #if defined(_ALPHAMODULATE_ON)
        return ApplyDanbaidongTransparentFog(color, alpha, positionCS, 3.0);
    #else
        return ApplyDanbaidongTransparentFog(color, alpha, positionCS, 0.0);
    #endif
}
#endif
