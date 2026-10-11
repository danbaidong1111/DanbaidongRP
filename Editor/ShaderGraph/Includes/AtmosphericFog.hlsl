#ifndef DANBAIDONG_SHADER_GRAPH_FOG_INCLUDED
#define DANBAIDONG_SHADER_GRAPH_FOG_INCLUDED

float3 ApplyShaderGraphAtmosphericFog(float3 color, float alpha, float4 positionCS)
{
#if defined(_SURFACE_TYPE_TRANSPARENT)
    #if defined(_DBD_FOG_MATERIAL_BLEND)
        float blendMode = _Blend;
    #elif defined(_DBD_FOG_BLEND_ADDITIVE)
        float blendMode = 2.0;
    #elif defined(_DBD_FOG_BLEND_MULTIPLY)
        float blendMode = 3.0;
    #elif defined(_DBD_FOG_BLEND_PREMULTIPLY)
        float blendMode = 1.0;
    #else
        float blendMode = 0.0;
    #endif
    return ApplyDanbaidongTransparentFog(color, alpha, positionCS, blendMode);
#else
    return color;
#endif
}

#endif
