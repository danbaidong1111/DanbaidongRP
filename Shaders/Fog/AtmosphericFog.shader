Shader "Hidden/Danbaidong/AtmosphericFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/AtmosphericFog.hlsl"
            TEXTURE2D_X(_FogColorPyramid);
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float depth = SampleSceneDepth(uv);
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6 && _FogOptions.z < 0.5) return color;
                bool skyPixel = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6;
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 p = skyPixel
                    ? ComputeWorldSpacePosition(uv, depth, FogGetInverseViewProjection())
                    : ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                color.rgb = ApplyAtmosphericFog(color.rgb, p, uv);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Multiple Scattering"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Scatter
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/AtmosphericFog.hlsl"
            TEXTURE2D_X(_FogColorPyramid);
            half4 Scatter(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float depth = SampleSceneDepth(uv);
                bool sky = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (sky && _FogOptions.z < 0.5) return color;
                #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 p = sky ? ComputeWorldSpacePosition(uv, depth, FogGetInverseViewProjection())
                    : ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float transmittance;
                ApplyAtmosphericFog(0, p, uv, transmittance);
                color.rgb = SAMPLE_TEXTURE2D_X_LOD(_FogColorPyramid, sampler_LinearClamp, uv,
                    saturate(1 - transmittance) * _FogAdvanced.w).rgb;
                return color;
            }
            ENDHLSL
        }
    }
}
