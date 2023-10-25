Shader "Character/Outline"
{
    Properties
    {
        
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
			"RenderPipeline" = "UniversalPipeline"
			"Queue"="Geometry"
			"IgnoreProjector" = "True"
        }
        LOD 300

        // Outline
        Pass
        {
            Name "GBufferOutline"

			Tags
            {
                "LightMode" = "GBufferOutline"
            }

            Cull Front
            ZWrite On

            HLSLPROGRAM
			#pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore
            #pragma multi_compile_instancing
			// #pragma multi_compile _ LOD_FADE_CROSSFADE
			// #pragma multi_compile_fog
			#pragma exclude_renderers d3d11_9x


            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/UnityGBuffer.hlsl"

            #pragma vertex ToonOutlineVert
            #pragma fragment ToonOutlineFrag

			CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _OutlineClampScale;
			half _AlphaClip;
			CBUFFER_END

            TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

            struct ToonOutline_a2v
            {
                float4 vertex   :POSITION;
                float3 normal   :NORMAL;
                float4 tangent  :TANGENT;
                float4 color    :COLOR; // rgb:SmoothNormal a:outlineWidth
                float4 uv       :TEXCOORD0;
            };

            struct ToonOutline_v2f
            {
                float4 positionHCS   :SV_POSITION;
                float3 positionWS   :TEXCOORD0;
                float3 normalWS     :TEXCOORD1;
                float4 uv           :TEXCOORD2;
                float4 color        :TEXCOORD3;
            };

            ToonOutline_v2f ToonOutlineVert(ToonOutline_a2v v)
            {
                float4 scaledScreenParams = GetScaledScreenParams();
                float ScaleX = abs(scaledScreenParams.x / scaledScreenParams.y);

                ToonOutline_v2f o;
                o.positionHCS = TransformObjectToHClip(v.vertex);
                o.positionWS = TransformObjectToWorld(v.vertex);

                float3 tangentOS = v.tangent.xyz;
                float3 normalOS = v.normal.xyz;
                float3 biTangentOS = cross(normalOS, tangentOS) * v.tangent.w * GetOddNegativeScale();

                float3 smoothNormalTS = v.color.rgb * 2 - 1;
                float3x3 TBN_TSOS= float3x3(tangentOS, biTangentOS, normalOS);

                float3 smoothNormalOS = mul(smoothNormalTS, TBN_TSOS);
                smoothNormalOS = SafeNormalize(smoothNormalOS);
                normalOS = smoothNormalOS;

                _OutlineWidth *= v.color.a;

                o.normalWS = TransformObjectToWorldNormal(normalOS);
                float3 normalCS = TransformWorldToHClipDir(o.normalWS);
                float2 extend = normalize(normalCS) * (_OutlineWidth*0.01); 
                extend.x /= ScaleX;


                //裁剪空间描边，可以使得相机远近描边宽度一致，和屏幕空间等宽边缘光思路一致
                float ctrl = clamp(1/(o.positionHCS.w + _OutlineClampScale),0,1);
                o.positionHCS.xy += extend * o.positionHCS.w * ctrl;
                o.uv.xy = TRANSFORM_TEX(v.uv,_BaseMap);

                o.color = v.color;
                return o;
            }

            FragmentOutput ToonOutlineFrag(ToonOutline_v2f i):SV_Target
            {
                half4 mainTexColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,i.uv);
            #if _USE_ALPHA_CLIPPING
                clip(mainTexColor.a - _AlphaClip);
            #endif

                half3 oulineColor = mainTexColor.rgb * _OutlineColor.rgb;
				oulineColor = lerp(mainTexColor.rgb, _OutlineColor.rgb, _OutlineColor.a);

                half isFace = 0.0;
                half isOutline = 1.0;
                return CharacterDataToGbuffer(mainTexColor, half3(0,0,0), oulineColor, 0, 0, i.normalWS, isFace, isOutline);
            }

            ENDHLSL
        }

    }

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}