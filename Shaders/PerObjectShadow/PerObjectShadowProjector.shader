Shader "PerObjectShadow/ShadowProjector"
{
    Properties
    {
        _ColorMask("Color Mask", Float) = 15
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }
    SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // PerObjectShadowProjector
        Pass
        {
            Name "PerObjectShadowProjector"
            Tags
            {
                "LightMode" = "PerObjectShadowProjector"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // PerObjectScreenSpaceShadow
        Pass
        {
            Name "PerObjectScreenSpaceShadow"
            Tags
            {
                "LightMode" = "PerObjectScreenSpaceShadow"
            }

            // -------------------------------------
            // Render State Commands
            ZTest Greater
            ZWrite Off
            Cull Front

            Blend One Zero
            BlendOp Min

            ColorMask [_ColorMask]

            HLSLPROGRAM


            // -------------------------------------
            // Shader Stages
            #pragma vertex vert
            #pragma fragment frag

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/PerObjectShadows.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct a2v
			{
				float3 vertex :POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 positionHCS  :SV_POSITION;
                float3 positionWS   :TEXCOORD0;
                float4 positionSS   :TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
				UNITY_DEFINE_INSTANCED_PROP(float4x4, _PerObjectWorldToShadow)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PerObjectUVScaleOffset)
			UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)
            float4 _PerObjectShadowScaledScreenParams;

            v2f vert(a2v v)
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);


				o.positionHCS = TransformObjectToHClip(v.vertex);
                o.positionSS = ComputeScreenPos(o.positionHCS);
                o.positionWS = TransformObjectToWorld(v.vertex);

				return o;
			}

            half4 frag(v2f i):SV_Target
            {

                UNITY_SETUP_INSTANCE_ID(i);
                float2 screenUV = i.positionHCS.xy * _PerObjectShadowScaledScreenParams.zw;
                TransformScreenUV(screenUV);


            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(screenUV).x;
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(screenUV).x);
            #endif


                float3 positionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);

                // Clip: ShadowFrustum HClip Sapce cull
                float3 positionSHCS = TransformWorldToObject(positionWS);
                positionSHCS = positionSHCS * float3(1.0, -1.0, 1.0);

                float clipValue = 1.0 - Max3(abs(positionSHCS).x, abs(positionSHCS).y, abs(positionSHCS).z);
                clip(clipValue);


                // Prepare Instance Values
                float4x4 worldToShadowMatrix = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PerObjectWorldToShadow);
                float4 uvScaleOffset = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PerObjectUVScaleOffset);
                

                float4 shadowCoord = TransformWorldToPerObjectShadowCoord(positionWS, worldToShadowMatrix);

                float attenuation = PerObjectRealtimeShadow(shadowCoord);

                return half4(attenuation.xxx, 1);
            }
            ENDHLSL
        }
    }
}
