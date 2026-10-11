Shader "Hidden/DanbaidongRP/ExposureApply"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Apply Exposure"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_ExposureTexture);
            float4 _ExposureApplyParams;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float exposure = _ExposureApplyParams.x > 0.5
                    ? LOAD_TEXTURE2D(_ExposureTexture, uint2(0, 0)).r
                    : _ExposureApplyParams.y;
                exposure = (IsNaN(exposure) || IsInf(exposure)) ? 1.0 : max(exposure, 0.0);
                #ifdef UNITY_COLORSPACE_GAMMA
                color.rgb = SRGBToLinear(color.rgb);
#endif
                float3 exposed = color.rgb * exposure;
                // Keep half-float scene color finite before optical post-processing.
                color.rgb = AnyIsNaN(exposed) ? 0.0 : clamp(exposed, -65504.0, 65504.0);
#ifdef UNITY_COLORSPACE_GAMMA
                color.rgb = LinearToSRGB(color.rgb);
#endif
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
