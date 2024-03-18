//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef SHADERVARIABLESSCREENSPACEREFLECTION_CS_HLSL
#define SHADERVARIABLESSCREENSPACEREFLECTION_CS_HLSL
// Generated from UnityEngine.Rendering.Universal.ShaderVariablesScreenSpaceReflection
// PackingRules = Exact
CBUFFER_START(ShaderVariablesScreenSpaceReflection)
    float4x4 _SSR_MATRIX_VP;
    float4x4 _SSR_MATRIX_I_VP;
    float4x4 _SSR_PREV_MATRIX_VP;
    float4x4 _SSR_MATRIX_CLIP_TO_PREV_CLIP;
    float4 _SsrTraceScreenSize;
    float _SsrThicknessScale;
    float _SsrThicknessBias;
    int _SsrIterLimit;
    float _SsrFrameCount;
    float _SsrRoughnessFadeEnd;
    float _SsrRoughnessFadeRcpLength;
    float _SsrRoughnessFadeEndTimesRcpLength;
    float _SsrEdgeFadeRcpLength;
    float4 _ColorPyramidUvScaleAndLimitPrevFrame;
    int _SsrDepthPyramidMaxMip;
    int _SsrColorPyramidMaxMip;
    int _SsrReflectsSky;
    float _SsrAccumulationAmount;
    float4 _HistoryFrameRTSize;
    float _SsrPBRBias;
CBUFFER_END


#endif
