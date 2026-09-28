#ifndef NPR_TOON_OUTLINE_INCLUDED
#define NPR_TOON_OUTLINE_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonShading.hlsl"

// -------------------------------------
// Outline Vertex Offset
void ApplyOutlineOffset(float3 normalOS, float4 tangentOS, float4 vertexColor, float2 smoothNormalUV,
    inout float4 positionCS, inout float3 normalWS)
{
#if defined(PASS_CHARACTEROUTLINE)
    if (_OutLineNormalSource < 1.5)
    {
        float3 smoothTS = vertexColor.rgb * 2.0 - 1.0;
        if (_OutLineNormalSource < 0.5)
        {
            smoothTS.xy = smoothNormalUV * 2.0 - 1.0;
            smoothTS.z = sqrt(saturate(1.0 - dot(smoothTS.xy, smoothTS.xy)));
        }
        float3 bitangentOS = cross(normalOS, tangentOS.xyz) * tangentOS.w;
        normalOS = SafeNormalize(mul(smoothTS, float3x3(tangentOS.xyz, bitangentOS, normalOS)));
    }
    normalWS = TransformObjectToWorldNormal(normalOS);
    float2 extend = SafeNormalize(TransformWorldToHClipDir(normalWS)).xy;
    float4 screen = GetScaledScreenParams();
    extend.x *= screen.y / max(screen.x, 1.0);
    float width = _OutlineWidth * lerp(1.0, vertexColor.a, _OutlineVertexAlpha) * 0.01;
    float scale = saturate(rcp(max(positionCS.w + _OutlineClampScale, 0.0001)));
    positionCS.xy += extend * width * positionCS.w * scale;
#endif
}

// -------------------------------------
// Outline Color Composition
void ApplyOutline(ShadingData data, SubMaterialData subMaterial, inout float3 color)
{
#if defined(PASS_CHARACTEROUTLINE)
    color += lerp(data.albedo, subMaterial.outlineColor.rgb, subMaterial.outlineColor.a);
#endif
}

#endif // NPR_TOON_OUTLINE_INCLUDED
