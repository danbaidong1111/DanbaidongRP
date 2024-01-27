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

            // -------------------------------------
            // Shader Stages
            #pragma vertex ToonOutlineVert
            #pragma fragment ToonOutlineFrag

			// -------------------------------------
            // Material Keywords


			// -------------------------------------
            // Universal Pipeline keywords
			// #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            // #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            // #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            // #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
			// #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            // #pragma multi_compile _ SHADOWS_SHADOWMASK
            // #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            // #pragma multi_compile _ LIGHTMAP_ON
            // #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/UnityGBuffer.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DanbaidongToon.hlsl"


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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ToonOutline_v2f
            {
                float4 positionHCS   :SV_POSITION;
                float3 positionWS   :TEXCOORD0;
                float3 normalWS     :TEXCOORD1;
                float4 uv           :TEXCOORD2;
                float4 color        :TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ToonOutline_v2f ToonOutlineVert(ToonOutline_a2v v)
            {
                ToonOutline_v2f o = (ToonOutline_v2f)0;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 scaledScreenParams = GetScaledScreenParams();
                float ScaleX = abs(scaledScreenParams.x / scaledScreenParams.y);

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
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 mainTexColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,i.uv);
            #if _USE_ALPHA_CLIPPING
                clip(mainTexColor.a - _AlphaClip);
            #endif

                float3 normalWS = SafeNormalize(i.normalWS);

                half3 oulineColor = mainTexColor.rgb * _OutlineColor.rgb;
				oulineColor = lerp(mainTexColor.rgb, _OutlineColor.rgb, _OutlineColor.a);

                half isFace = 0.0;
                half isOutline = 1.0;
                return CharacterDataToGbuffer(mainTexColor, half3(0,0,0), oulineColor, 0, 0, normalWS, isFace, isOutline);
            }

            ENDHLSL
        }

    }

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}