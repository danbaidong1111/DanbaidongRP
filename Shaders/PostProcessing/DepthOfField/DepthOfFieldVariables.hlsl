#ifndef DANBAIDONG_DEPTH_OF_FIELD_VARIABLES
#define DANBAIDONG_DEPTH_OF_FIELD_VARIABLES
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#if !defined(STEREO_INSTANCING_ON) && !defined(STEREO_MULTIVIEW_ON)
    #define DISABLE_TEXTURE2D_X_ARRAY
#endif
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"

SAMPLER(s_point_clamp_sampler);
SAMPLER(s_linear_clamp_sampler);
SAMPLER(s_trilinear_clamp_sampler);
TEXTURE2D_X(_CameraDepthTexture);
TEXTURE2D_X(_CameraMotionVectorsTexture);
float4 _DofScreenSize, _DofBlurSize, _DofDepthSize, _DofDispatchSize, _DofSourceScale;
float4 _DofProjection, _DofZBufferParams, _DofLens;
float4 _TaaJitterStrength, _TaaFrameInfo;
#define _ScreenSize _DofScreenSize
#define _PostProcessScreenSize _DofScreenSize
#define _ZBufferParams _DofZBufferParams
#define _RTHandlePostProcessScale float4(1, 1, 1, 1)

float2 ClampAndScaleUV(float2 uv, float2 texel, float margin, float2 scale)
{
    float2 border = min(texel * margin, 0.5);
    return clamp(uv, border, 1.0 - border) * scale;
}
float2 ClampAndScaleUVPostProcessTexture(float2 uv, float2 texel, float margin)
{
    return ClampAndScaleUV(uv, texel, margin, 1.0);
}
float2 ClampAndScaleUVForBilinearPostProcessTexture(float2 uv, float2 texel)
{
    return ClampAndScaleUV(uv, texel, 0.5, 1.0);
}
float2 ClampAndScaleUVForBilinearPostProcessTexture(float2 uv)
{
    return ClampAndScaleUVForBilinearPostProcessTexture(uv, _DofScreenSize.zw);
}
uint2 ClampAndScaleUVPostProcessTextureForPoint(float2 pixel)
{
    return clamp(int2(pixel), 0, int2(_DofScreenSize.xy) - 1);
}
float2 ClampAndScaleUVForPoint(float2 uv)
{
    return ClampAndScaleUV(uv, _DofDepthSize.zw, 0.5, _DofSourceScale.zw);
}
float2 FromOutputPosSSToPreupsampleUV(float2 pixel)
{
    return (pixel + 0.5) * _DofScreenSize.zw;
}
uint2 FromOutputPosSSToPreupsamplePosSS(float2 pixel)
{
    return min(uint2(FromOutputPosSSToPreupsampleUV(pixel) * _DofDepthSize.xy), uint2(_DofDepthSize.xy) - 1);
}
float LoadCameraDepth(int2 pixel)
{
    return LOAD_TEXTURE2D_X(_CameraDepthTexture, clamp(pixel, 0, int2(_DofDepthSize.xy) - 1)).r;
}
float DofLinearEyeDepth(float depth)
{
    if (_DofProjection.z > 0.5)
    {
        #if UNITY_REVERSED_Z
            depth = 1.0 - depth;
        #endif
        return lerp(_DofProjection.x, _DofProjection.y, depth);
    }
    return LinearEyeDepth(depth, _DofZBufferParams);
}
void DecodeMotionVector(float4 encoded, out float2 velocity) { velocity = encoded.xy; }
#endif
