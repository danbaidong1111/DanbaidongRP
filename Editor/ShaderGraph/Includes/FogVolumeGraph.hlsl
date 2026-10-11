#ifndef DANBAIDONG_FOG_VOLUME_GRAPH_INCLUDED
#define DANBAIDONG_FOG_VOLUME_GRAPH_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

float _FogLocalSlice;
float _FogLocalTime;
float4 _FogLocalMaskSize;
float4x4 _FogLocalVolumeToWorld;

struct FogVolumeVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

FogVolumeVaryings FogVolumeVertex(uint vertexID : SV_VertexID)
{
    FogVolumeVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    output.uv = GetFullScreenTriangleTexCoord(vertexID);
    return output;
}

#endif
