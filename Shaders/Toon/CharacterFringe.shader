Shader "Character/LitFringe"
{
    Properties
    {
        [FoldoutBegin(_FoldoutTexEnd)]_FoldoutTex("Textures", float) = 0
            _BaseColor						("BaseColor", Color)    				= (0,0,0,1)
            _BaseMap						("BaseMap_d", 2D)       				= "white" {}
            [NoScaleOffset]_PBRMask         ("PBRMask(metal smooth ao)", 2D)		= "white" {}
            [NoScaleOffset]_ILMMapSpecType  ("ILMMapSpecType", 2D)          		= "white" {}
            [NoScaleOffset]_ILMMapAO        ("ILMMapAO", 2D)                		= "white" {}
            [NoScaleOffset]_ILMMapSpecMask  ("ILMMapSpecMask", 2D)          		= "white" {}
            [NoScaleOffset]_NormalMap       ("NormalMap", 2D)               		= "bump" {}
            _NormalScale					("NormalScale",Range(0,1)) 				= 1
        [FoldoutEnd]_FoldoutTexEnd("_FoldoutEnd", float) = 0

        [FoldoutBegin(_FoldoutPBRPropEnd)]_FoldoutPBRProp("PBR Properties", float) 	= 0
            _Metallic						("Metallic",Range(0,1)) 				= 0.5
            _Smoothness						("Smoothness",Range(0,1)) 				= 0.5
            _Occlusion						("Occlusion",Range(0,1)) 				= 1
            _NdotVAdd						("NdotVAdd(Leather Reflect)",Range(0,2))= 0
        [FoldoutEnd]_FoldoutPBRPropEnd("_FoldoutPBRPropEnd", float) = 0

		// Direct LIght
		[FoldoutBegin(_FoldoutDirectLightEnd)]_FoldoutDirectLight("Direct Light", float) = 0
            [HDR]_SelfLight					("SelfLight", Color) 					= (1,1,1,1)
            _MainLightColorLerp				("Unity Light or SelfLight", Range(0,1))= 0
            _DirectOcclusion				("DirectOcclusion",Range(0,1)) 			= 0.1
            
            [Title(Shadow)]
            _ShadowColor        			("ShadowColor", Color)        			= (0,0,0,1)
            _ShadowOffset       			("ShadowOffset",Range(-1,1))  			= 0.0
            _ShadowSmooth       			("ShadowSmooth", Range(0,1))  			= 0.0
            _ShadowStrength     			("ShadowStrength", Range(0,1))			= 1.0
			[Space(10)]
            _SecShadowColor     			("SecShadowColor (ILM AO)", Color)		= (0.5,0.5,0.5,1)
            _SecShadowStrength  			("SecShadowStrength", Range(0,1))		= 1.0

        [FoldoutEnd]_FoldoutDirectLightEnd("_FoldoutEnd", float) = 0

		// Ramp
        [FoldoutBegin(_FoldoutShadowRampEnd, _SHADOW_RAMP)]_FoldoutShadowRamp("ShadowRamp", float) = 0
        [HideInInspector]_SHADOW_RAMP("_SHADOW_RAMP", float) = 0
            [Ramp]_ShadowRampTex			("ShadowRampTex", 2D) 					= "white" { }
        [FoldoutEnd]_FoldoutShadowRampEnd("_FoldoutEnd", float) = 0

        [FoldoutBegin(_FoldoutHairSpecEnd)]_FoldoutHairSpec("HairSpec", float) = 0
			[NoScaleOffset]_HairSpecTex	("HairSpecTex", 2D)          			= "black" {}
			[HDR]_SpecColor				("SpecColor", color)					= (0.5, 0.5, 0.5, 0)
            _AnisotropicSlide			("AnisotropicSlide", Range(-0.5, 0.5))	= 0.3
			_AnisotropicOffset			("AnisotropicOffset", Range(-1.0, 1.0))	= 0.0
			_BlinnPhongPow				("BlinnPhongPow", Range(1, 50))			= 5
			_SpecMinimum				("SpecMinimum", Range(0, 0.5))			= 0.1
        [FoldoutEnd]_FoldoutHairSpecEnd("_FoldoutEnd", float) = 0


		// Indirect Light
		[FoldoutBegin(_FoldoutIndirectLightEnd)]_FoldoutIndirectLight("Indirect Light", float) = 0
            [Title(Diffuse)]
            [HDR]_SelfEnvColor  			("SelfEnvColor", Color) 				= (0.5,0.5,0.5,0.5)
            _EnvColorLerp       			("Unity SH or SelfEnv", Range(0,1)) 	= 0.5
            _IndirDiffUpDirSH   			("IndirDiffUpDirSH", Range(0,1))		= 0.0
            _IndirDiffIntensity 			("IndirDiffIntensity", Range(0,1))		= 0.3
            [Title(Specular)]
            [Toggle(_INDIR_CUBEMAP)]_INDIR_CUBEMAP("_INDIR_CUBEMAP", Float) 		= 0
            [NoScaleOffset]_IndirSpecCubemap("SpecCube", cube) 						= "black" {}
            [Toggle(_INDIR_MATCAP)]_INDIR_MATCAP("_INDIR_MATCAP", Float) 			= 0
            _IndirSpecMatcap    			("Matcap", 2D) 							= "black" {}

            _IndirSpecMatcapTile			("MatcapTile", float)               	= 1.0
            _IndirSpecLerp      			("Unity Reflect or Self Map", Range(0,1))= 0.3
            _IndirSpecIntensity 			("IndirSpecIntensity", Range(0.01,10))	= 1.0

        [FoldoutEnd]_FoldoutIndirectLightEnd("_FoldoutEnd", float) = 0

		// Emission, Rim, etc.
        [FoldoutBegin(_FoldoutEmissRimEnd)]_FoldoutEmissRim("Emission, Rim, etc.", float) = 0
            [HDR]_EmissionCol				("EmissionCol", color)         			= (1,1,1,1)

        [FoldoutEnd]_FoldoutEmissRimEnd("_FoldoutEnd", float) = 0

		// Outline
        [FoldoutBegin(_FoldoutOutlineEnd)]_FoldoutOutline("Outline", float) = 0
			_OutlineColor					("Outline Color", Color)				= (0, 0, 0, 0.8)
            _OutlineWidth					("OutlineWidth", Range(0, 10))			= 1.0
            _OutlineClampScale				("OutlineClampScale", Range(0.01, 5)) 	= 1
        [FoldoutEnd]_FoldoutOutlineEnd("_FoldoutEnd", float) = 0


		[FoldoutBegin(_FoldoutFringeEnd)]_FoldoutFringe("Fringe Inner Outer Stencil", float) = 0
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_Stencil("Stencil ID", Float) = 0
			[Enum(UnityEngine.Rendering.CompareFunction)]_StencilComp("Stencil Comparison", Float) = 6
			[Enum(UnityEngine.Rendering.StencilOp)]_StencilOp("Stencil Operation", Float) = 0
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_StencilWriteMask("Stencil Write Mask", Float) = 0
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_StencilReadMask("Stencil Read Mask", Float) = 0
			[Title(FringeOuter)]
			[Enum(UnityEngine.Rendering.CompareFunction)]_OuterStencilComp("Outer Stencil Comparison", Float) = 3
			_OuterAlpha("OuterAlpha", Range(0, 1))			= 0.5
        [FoldoutEnd]_FoldoutFringeEnd("_FoldoutEnd", float) = 0



		[FoldoutBegin(_FoldoutFringeShadowEnd)]_FoldoutFringeShadow("FringeShadow Caster (Stencil)", float) = 0
			_ScreenOffsetScaleX("ScreenOffsetScaleX", Range(-2, 2)) 		= 1
			_ScreenOffsetScaleY("ScreenOffsetScaleY", Range(-2, 2)) 		= 1
			[Title(Shadow Caster Stencil)]
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_FriStencil("Stencil ID", Float) = 0
			[Enum(UnityEngine.Rendering.CompareFunction)]_FriStencilComp("Stencil Comparison", Float) = 6
			[Enum(UnityEngine.Rendering.StencilOp)]_FriStencilOp("Stencil Operation", Float) = 0
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_FriStencilWriteMask("Stencil Write Mask", Float) = 0
			[Enum(UnityEngine.Rendering.Universal.Internal.StencilUsage)]_FriStencilReadMask("Stencil Read Mask", Float) = 0
			_FriColorMask("Color Mask", Float) = 0
		[FoldoutEnd]_FoldoutFringeShadowEnd("_FoldoutFringeShadowEnd", float) = 0


		// Other Settings
		[Space(10)]
        [Enum(UnityEngine.Rendering.CullMode)] 
        _Cull								("Cull Mode", Float) 					= 2
		_AlphaClip							("AlphaClip", Range(0, 1)) 				= 1

    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
			"RenderPipeline" = "UniversalPipeline"
			"Queue"="Geometry"
			"IgnoreProjector" = "True"
			"UniversalMaterialType" = "CharacterLit"
        }
        LOD 300

		ZWrite On
		ZTest LEqual
		Cull [_Cull]
		
		// FringeShadowCaster
        Pass
        {
            Name "FringeShadowCaster"

			Tags
            {
                "LightMode" = "GBufferFringeShadowCaster"
            }

			Stencil
			{
				Ref[_FriStencil]
				Comp[_FriStencilComp]
				Pass[_FriStencilOp]
				ReadMask[_FriStencilReadMask]
				WriteMask[_FriStencilWriteMask]
			}

            Cull Back
            ZWrite Off
			ColorMask [_FriColorMask]

            HLSLPROGRAM
			#pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

			// -------------------------------------
            // Shader Stages
            #pragma vertex FringeShadowCasterVert
            #pragma fragment FringeShadowCasterFrag

			// -------------------------------------
            // Material Keywords


			// -------------------------------------
            // Universal Pipeline keywords

			//--------------------------------------
            // GPU Instancing
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

			// -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/UnityGBuffer.hlsl"


            struct FringeShadowCaster_a2v
            {
                float4 vertex   :POSITION;
                float3 normal   :NORMAL;
            };

            struct FringeShadowCaster_v2f
            {
                float4 positionHCS   :SV_POSITION;
            };

			struct FringeShadowCaster_fragOut
			{
				half4 GBuffer0 : SV_Target0;
			};
			float _ScreenOffsetScaleX;
			float _ScreenOffsetScaleY;
            FringeShadowCaster_v2f FringeShadowCasterVert(FringeShadowCaster_a2v v)
            {
                FringeShadowCaster_v2f o;

				Light mainLight = GetMainLight();
				float3 lightDirWS = normalize(mainLight.direction);
				float3 lightDirVS = normalize(TransformWorldToViewDir(lightDirWS));

				// Cam is Upward: let shadow close to face.
				float3 camDirOS = normalize(TransformWorldToObject(GetCameraPositionWS()));
				float camDirFactor = 1 - smoothstep(0.1, 0.9, camDirOS.y);

				float3 positionVS = TransformWorldToView(TransformObjectToWorld(v.vertex));
				
				positionVS.x -= 0.0045 * lightDirVS.x * _ScreenOffsetScaleX;
				positionVS.y -= 0.0075 * _ScreenOffsetScaleY * camDirFactor;
                o.positionHCS = TransformWViewToHClip(positionVS);

                return o;
            }

            FragmentOutput FringeShadowCasterFrag(FringeShadowCaster_v2f i):SV_Target
            {
				FragmentOutput output;
				output.GBuffer0 = half4(0, 0, 0, 0);
				
                return output;
            }

            ENDHLSL
        }


		// GBufferBase
		Pass
        {
			Name "GBufferBase"
            Tags
            {
                "LightMode" = "GBufferFringe"
            }

			Stencil
			{
				Ref[_Stencil]
				Comp[_StencilComp]
				Pass[_StencilOp]
				ReadMask[_StencilReadMask]
				WriteMask[_StencilWriteMask]
			}

            HLSLPROGRAM
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

			// -------------------------------------
            // Shader Stages
            #pragma vertex DanbaidongToonVert
            #pragma fragment frag

			// -------------------------------------
            // Material Keywords
			#pragma shader_feature_local _SHADOW_RAMP
			#pragma shader_feature_local _INDIR_CUBEMAP
			#pragma shader_feature_local _INDIR_MATCAP

			// -------------------------------------
            // Universal Pipeline keywords
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
			half3	_BaseColor;
            float4	_BaseMap_ST;
			half	_NormalScale;

			// PBR Properties
			half	_Metallic;
			half	_Smoothness;
			half	_Occlusion;
			half	_NdotVAdd;

			// Direct Light
			half4	_SelfLight;
			half	_MainLightColorLerp;
			half	_DirectOcclusion;

			// Shadow
			half4	_ShadowColor;
			float   _ShadowOffset;
			float   _ShadowSmooth;
			float   _ShadowStrength;
			half4 	_SecShadowColor;
			float   _SecShadowStrength;

			// Indirect
            half4	_SelfEnvColor;
            half	_EnvColorLerp;
			half	_IndirDiffUpDirSH;
			half	_IndirDiffIntensity;
			half	_IndirSpecLerp;
			half	_IndirSpecMatcapTile;
			half	_IndirSpecIntensity;

			// Emission
			half4	_EmissionCol;

			// HairSpec
			half4 _SpecColor;
			half _AnisotropicSlide;
			half _AnisotropicOffset;
			half _BlinnPhongPow;
			half _SpecMinimum;
            CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

            TEXTURE2D(_PBRMask);
			SAMPLER(sampler_PBRMask);
		    TEXTURE2D(_NormalMap);
			SAMPLER(sampler_NormalMap);	

			TEXTURE2D(_ILMMapSpecType);
			TEXTURE2D(_ILMMapAO);
			TEXTURE2D(_ILMMapSpecMask);

            TEXTURE2D(_ShadowRampTex);
			SAMPLER(sampler_ShadowRampTex);

			TEXTURECUBE(_IndirSpecCubemap);

			TEXTURE2D(_IndirSpecMatcap);
			SAMPLER(sampler_IndirSpecMatcap);

			TEXTURE2D(_HairSpecTex);

            FragmentOutput frag(Toon_v2f i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 UV = i.uv.xy;
				float2 UV1 = i.uv.zw;
				float3 positionWS = i.positionWS;
				float4 shadowCoords = TransformWorldToShadowCoord(positionWS);
				Light mainLight = GetMainLight();
				mainLight.color = lerp(mainLight.color, _SelfLight.rgb, _MainLightColorLerp);

				// Tex Sample
                half4 mainTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, UV);
                half4 pbrMask = SAMPLE_TEXTURE2D(_PBRMask, sampler_PBRMask, UV);
				half3 bumpTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,UV), _NormalScale);
				half  ilmSpecMask = SAMPLE_TEXTURE2D(_ILMMapSpecMask, sampler_LinearClamp, UV).r;
				half  ilmAO = SAMPLE_TEXTURE2D(_ILMMapAO, sampler_LinearClamp, UV).r;
				ilmAO = lerp(1 - _SecShadowStrength, 1, ilmAO);


				// VectorPrepare
				float3 lightDirWS = SafeNormalize(mainLight.direction);
				float3 camDirWS = GetCameraPositionWS();
				float3 viewDirWS = SafeNormalize(camDirWS - positionWS);
				float3 normalWS = SafeNormalize(i.normalWS);

				float3x3 TBN = float3x3(i.tangentWS, i.biTangentWS, i.normalWS);
				float3 bumpWS = TransformTangentToWorld(bumpTS,TBN);
				normalWS = SafeNormalize(bumpWS);

				float3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
				float halfLambert = dot(normalWS, lightDirWS) * 0.5 + 0.5;
				float NdotL = saturate(dot(normalWS, lightDirWS));
				float NdotV = saturate(dot(normalWS, viewDirWS));
				float NdotH = saturate(dot(normalWS, halfDir));
				float HdotV = saturate(dot(halfDir,  viewDirWS));



				// Property prepare
					half emission			= 1 - mainTex.a;
					half metallic  			= lerp(0, _Metallic, pbrMask.r);
					half smoothness 		= lerp(0, _Smoothness, pbrMask.g);
					half occlusion  		= lerp(1 - _Occlusion, 1, pbrMask.b);
					half directOcclusion  	= lerp(1 - _DirectOcclusion, 1, pbrMask.b);
					half3 albedo = mainTex.rgb * _BaseColor.rgb;

					// NPR diffuse
					float shadowArea = sigmoid(1 - halfLambert, _ShadowOffset, _ShadowSmooth * 10) * _ShadowStrength;
					half3 shadowRamp = lerp(1, _ShadowColor.rgb, shadowArea);
                    //Remap NdotL for PBR Spec
                    half NdotLRemap = 1 - shadowArea;
                #if _SHADOW_RAMP
                    shadowRamp = SampleDirectShadowRamp(TEXTURE2D_ARGS(_ShadowRampTex, sampler_ShadowRampTex), 1.0 - shadowArea);
                #endif
					
                    // NdotV modify fresnel
					NdotV += _NdotVAdd;

					// CustomShadow
					shadowRamp.rgb = lerp(_SecShadowColor.rgb, shadowRamp.rgb, ilmAO);


				// Direct
					float3 directDiffColor = albedo.rgb;

					float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
					float roughness           = max(PerceptualRoughnessToRoughness(perceptualRoughness), HALF_MIN_SQRT);
					float roughnessSquare     = max(roughness * roughness, HALF_MIN);
					float3 F0 = lerp(0.04, albedo, metallic);

					float NDF = DistributionGGX(NdotH, roughnessSquare);
					float G = GeometrySmith(NdotLRemap, NdotV, pow(roughness + 1.0, 2.0) / 8.0);
					float3 F = fresnelSchlick(HdotV, F0);
                    
					// GGX specArea remap
					NDF = NDF * ilmSpecMask;

					float3 kSpec = F;
					// LightUpDiff: (1.0 - F) => (1.0 - F) * 0.5 + 0.5
					float3 kDiff = ((1.0 - F) * 0.5 + 0.5) * (1.0 - metallic);

					float3 nom = NDF * G * F;
					float3 denom = 4.0 * NdotV * NdotLRemap + 0.0001;
					float3 BRDFSpec = nom / denom;

					directDiffColor = kDiff * albedo;
					float3 directSpecColor = BRDFSpec * PI;


                #if _SHADOW_RAMP
                    float specRange= saturate(NDF * G / denom.x);
                    half4 specRampCol = SampleDirectSpecularRamp(TEXTURE2D_ARGS(_ShadowRampTex, sampler_ShadowRampTex), specRange);
                    directSpecColor = clamp(specRampCol.rgb * 3 + BRDFSpec * PI / F, 0, 10) * F * shadowRamp;
                #endif

					// Hair Spec
					float anisotropicOffsetV = - viewDirWS.y * _AnisotropicSlide + _AnisotropicOffset;
					half3 hairSpecTex = SAMPLE_TEXTURE2D(_HairSpecTex, sampler_LinearClamp, float2(UV1.x, UV1.y + anisotropicOffsetV));
					float hairSpecStrength = _SpecMinimum + pow(NdotH, _BlinnPhongPow) * NdotLRemap;
					half3 hairSpecColor = hairSpecTex * _SpecColor * hairSpecStrength;

					// Compose direct lighting
					float3 directLightResult = (directDiffColor * shadowRamp + directSpecColor * NdotLRemap + hairSpecColor)
												* mainLight.color * mainLight.shadowAttenuation * directOcclusion;


				// Indirect
					// Diffuse
					float3 indirDiffColor = IndirectDiffuse(normalWS, _IndirDiffUpDirSH, half4(_SelfEnvColor.rgb, _EnvColorLerp), albedo, F0, NdotV, roughness, metallic, occlusion);


					// Specular
					float3 indirSpecCubeColor = IndirSpeCube(normalWS, viewDirWS, roughness, occlusion);
					float3 indirSpecCubeFactor = IndirSpeFactor(roughness, smoothness, BRDFSpec, F0, NdotV);
					half3 additionalIndirSpec = 0;
				#if _INDIR_CUBEMAP // Additional cubemap
					float3 reflectDirWS = reflect(-viewDirWS, normalWS);
					roughness = roughness * (1.7 - 0.7 * roughness);
					float mipLevel= roughness * 6;
					additionalIndirSpec = SAMPLE_TEXTURECUBE_LOD(_IndirSpecCubemap, sampler_LinearRepeat, reflectDirWS, mipLevel);
				#elif _INDIR_MATCAP // Additional matcap
					float3 normalVS = TransformWorldToViewNormal(normalWS);
					normalVS = SafeNormalize(normalVS);
					float2 matcapUV = (normalVS.xy * _IndirSpecMatcapTile) * 0.5 + 0.5;
					additionalIndirSpec = SAMPLE_TEXTURE2D(_IndirSpecMatcap, sampler_IndirSpecMatcap, matcapUV);
				#endif /* _INDIR_CUBEMAP _INDIR_MATCAP */
					float3 indirSpecColor = lerp(indirSpecCubeColor, additionalIndirSpec, _IndirSpecLerp) * indirSpecCubeFactor;

					// Compose indirect lighting
					float3 indirectLightResult = indirDiffColor * _IndirDiffIntensity + indirSpecColor * _IndirSpecIntensity;


				half3 emissResult = emission * albedo * _EmissionCol.rgb * _EmissionCol.a;
				half3 lightingResult = directLightResult + indirectLightResult + emissResult;

				return CharacterDataToGbuffer(albedo, directLightResult, indirectLightResult + emissResult, smoothness, metallic, normalWS);

            }
            ENDHLSL

        }

		// GBufferFringeOuter
        Pass
        {
			Name "GBufferFringeOuter"
            Tags
            {
                "LightMode" = "GBufferFringeOuter"
            }

			Stencil
			{
				Ref[_Stencil]
				Comp[_OuterStencilComp]
				Pass[_StencilOp]
				ReadMask[_StencilReadMask]
				WriteMask[_StencilWriteMask]
			}

			ColorMask RGB
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

			// -------------------------------------
            // Shader Stages
            #pragma vertex DanbaidongToonVert
            #pragma fragment frag

			// -------------------------------------
            // Material Keywords
			#pragma shader_feature_local _SHADOW_RAMP
			#pragma shader_feature_local _INDIR_CUBEMAP
			#pragma shader_feature_local _INDIR_MATCAP

			// -------------------------------------
            // Universal Pipeline keywords
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
			// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;

			half4 _SelfLight;
			half _MainLightColorLerp;

			half3 _BaseColor;

			half3 _SkinColor;
			half _NormalScale;

			half _Metallic;
			half _Smoothness;
			half _Occlusion;
			half _NdotVAdd;

			half _DirectOcclusion;

			// Shadow
			half4  _ShadowColor;
			float   _ShadowOffset;
			float   _ShadowSmooth;
			float   _ShadowStrength;
			half4 	_SecShadowColor;
			float   _SecShadowStrength;
			half4   _HighCol; 
			half4   _MidCol;
			half4   _LowCol;  
			half    _HighOffset;
			half    _HighSharp;
			half    _MidOffset; 
			half    _MidSharp; 
			half    _LowOffset; 
			half    _LowSharp; 


			// Indirect
            half4 _SelfEnvColor;
            half _EnvColorLerp;
			half _IndirDiffUpDirSH;
			half _IndirDiffIntensity;
			half _IndirSpecLerp;
			half _IndirSpecMatcapTile;
			half _IndirSpecIntensity;

			// Emission
			half4	_EmissionCol;

			// HairSpec
			half4 _SpecColor;
			half _AnisotropicSlide;
			half _AnisotropicOffset;
			half _BlinnPhongPow;
			half _SpecMinimum;
            CBUFFER_END
			half _OuterAlpha;

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

            TEXTURE2D(_PBRMask);
			SAMPLER(sampler_PBRMask);
		    TEXTURE2D(_NormalMap);
			SAMPLER(sampler_NormalMap);	

			TEXTURE2D(_ILMMapSpecType);
			TEXTURE2D(_ILMMapAO);
			TEXTURE2D(_ILMMapSpecMask);

            TEXTURE2D(_ShadowRampTex);
			SAMPLER(sampler_ShadowRampTex);

			TEXTURECUBE(_IndirSpecCubemap);

			TEXTURE2D(_IndirSpecMatcap);
			SAMPLER(sampler_IndirSpecMatcap);

			TEXTURE2D(_HairSpecTex);

			struct FringeOuter_fragOut
			{
				half4 GBuffer0 : SV_Target0;
				half4 GBuffer1 : SV_Target1;
				half4 GBuffer2 : SV_Target2;
				half4 GBuffer3 : SV_Target3;
			};


            FringeOuter_fragOut frag(Toon_v2f i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				
                float2 UV = i.uv.xy;
				float2 UV1 = i.uv.zw;
				float3 positionWS = i.positionWS;
				float4 shadowCoords = TransformWorldToShadowCoord(positionWS);
				Light mainLight = GetMainLight();
				mainLight.color = lerp(mainLight.color, _SelfLight.rgb, _MainLightColorLerp);

				// Tex Sample
                half4 mainTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, UV);
                half4 pbrMask = SAMPLE_TEXTURE2D(_PBRMask, sampler_PBRMask, UV);
				half3 bumpTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,UV), _NormalScale);
				half  ilmSpecMask = SAMPLE_TEXTURE2D(_ILMMapSpecMask, sampler_LinearClamp, UV).r;
				half  ilmAO = SAMPLE_TEXTURE2D(_ILMMapAO, sampler_LinearClamp, UV).r;
				ilmAO = lerp(1 - _SecShadowStrength, 1, ilmAO);

				// VectorPrepare
				float3 lightDirWS = SafeNormalize(mainLight.direction);
				float3 camDirWS = GetCameraPositionWS();
				float3 viewDirWS = SafeNormalize(camDirWS - positionWS);
				float3 normalWS = SafeNormalize(i.normalWS);

				float3x3 TBN = float3x3(i.tangentWS, i.biTangentWS, i.normalWS);
				float3 bumpWS = TransformTangentToWorld(bumpTS,TBN);
				normalWS = SafeNormalize(bumpWS);

				float3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
				float halfLambert = dot(normalWS, lightDirWS) * 0.5 + 0.5;
				float NdotL = saturate(dot(normalWS, lightDirWS));
				float NdotV = saturate(dot(normalWS, viewDirWS));
				float NdotH = saturate(dot(normalWS, halfDir));
				float HdotV = saturate(dot(halfDir,  viewDirWS));

				// Property prepare
					half emission			= 1 - mainTex.a;
					half metallic  			= lerp(0, _Metallic, pbrMask.r);
					half smoothness 		= lerp(0, _Smoothness, pbrMask.g);
					half occlusion  		= lerp(1 - _Occlusion, 1, pbrMask.b);
					half directOcclusion  	= lerp(1 - _DirectOcclusion, 1, pbrMask.b);
					half3 albedo = mainTex.rgb * _BaseColor.rgb;

					// NPR diffuse
					float shadowArea = sigmoid(1 - halfLambert, _ShadowOffset, _ShadowSmooth * 10) * _ShadowStrength;
					half3 shadowRamp = lerp(1, _ShadowColor.rgb, shadowArea);
                    //Remap NdotL for PBR Spec
                    half NdotLRemap = 1 - shadowArea;
                #if _SHADOW_RAMP
                    shadowRamp = SampleDirectShadowRamp(TEXTURE2D_ARGS(_ShadowRampTex, sampler_ShadowRampTex), 1.0 - shadowArea);
                #endif
					
                    // NdotV modify fresnel
					NdotV += _NdotVAdd;

					// CustomShadow
					shadowRamp.rgb = lerp(_SecShadowColor.rgb, shadowRamp.rgb, ilmAO);



				// Direct
					float3 directDiffColor = albedo.rgb;

					float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
					float roughness           = max(PerceptualRoughnessToRoughness(perceptualRoughness), HALF_MIN_SQRT);
					float roughnessSquare     = max(roughness * roughness, HALF_MIN);
					float3 F0 = lerp(0.04, albedo, metallic);

					float NDF = DistributionGGX(NdotH, roughnessSquare);
					float G = GeometrySmith(NdotLRemap, NdotV, pow(roughness + 1.0, 2.0) / 8.0);
					float3 F = fresnelSchlick(HdotV, F0);
                    
					// GGX specArea remap
					NDF = NDF * ilmSpecMask;

					float3 kSpec = F;
					// LightUpDiff: (1.0 - F) => (1.0 - F) * 0.5 + 0.5
					float3 kDiff = ((1.0 - F) * 0.5 + 0.5) * (1.0 - metallic);

					float3 nom = NDF * G * F;
					float3 denom = 4.0 * NdotV * NdotLRemap + 0.0001;
					float3 BRDFSpec = nom / denom;

					directDiffColor = kDiff * albedo;
					float3 directSpecColor = BRDFSpec * PI;


                #if _SHADOW_RAMP
                    float specRange= saturate(NDF * G / denom.x);
                    half4 specRampCol = SampleDirectSpecularRamp(TEXTURE2D_ARGS(_ShadowRampTex, sampler_ShadowRampTex), specRange);
                    directSpecColor = clamp(specRampCol.rgb * 3 + BRDFSpec * PI / F, 0, 10) * F * shadowRamp;
                #endif

					// Hair Spec
					float anisotropicOffsetV = - viewDirWS.y * _AnisotropicSlide + _AnisotropicOffset;
					half3 hairSpecTex = SAMPLE_TEXTURE2D(_HairSpecTex, sampler_LinearClamp, float2(UV1.x, UV1.y + anisotropicOffsetV));
					float hairSpecStrength = _SpecMinimum + pow(NdotH, _BlinnPhongPow) * NdotLRemap;
					half3 hairSpecColor = hairSpecTex * _SpecColor * hairSpecStrength;

					// Compose direct lighting
					float3 directLightResult = (directDiffColor * shadowRamp + directSpecColor * NdotLRemap + hairSpecColor)
												* mainLight.color * mainLight.shadowAttenuation * directOcclusion;


				// Indirect
					// Diffuse
					float3 indirDiffColor = IndirectDiffuse(normalWS, _IndirDiffUpDirSH, half4(_SelfEnvColor.rgb, _EnvColorLerp), albedo, F0, NdotV, roughness, metallic, occlusion);


					// Specular
					float3 indirSpecCubeColor = IndirSpeCube(normalWS, viewDirWS, roughness, occlusion);
					float3 indirSpecCubeFactor = IndirSpeFactor(roughness, smoothness, BRDFSpec, F0, NdotV);
					half3 additionalIndirSpec = 0;
				#if _INDIR_CUBEMAP // Additional cubemap
					float3 reflectDirWS = reflect(-viewDirWS, normalWS);
					roughness = roughness * (1.7 - 0.7 * roughness);
					float mipLevel= roughness * 6;
					additionalIndirSpec = SAMPLE_TEXTURECUBE_LOD(_IndirSpecCubemap, sampler_LinearRepeat, reflectDirWS, mipLevel);
				#elif _INDIR_MATCAP // Additional matcap
					float3 normalVS = TransformWorldToViewNormal(normalWS);
					normalVS = SafeNormalize(normalVS);
					float2 matcapUV = (normalVS.xy * _IndirSpecMatcapTile) * 0.5 + 0.5;
					additionalIndirSpec = SAMPLE_TEXTURE2D(_IndirSpecMatcap, sampler_IndirSpecMatcap, matcapUV);
				#endif /* _INDIR_CUBEMAP _INDIR_MATCAP */
					float3 indirSpecColor = lerp(indirSpecCubeColor, additionalIndirSpec, _IndirSpecLerp) * indirSpecCubeFactor;

					// Compose indirect lighting
					float3 indirectLightResult = indirDiffColor * _IndirDiffIntensity + indirSpecColor * _IndirSpecIntensity;

				half3 emissResult = emission * albedo * _EmissionCol.rgb * _EmissionCol.a;
				half3 lightingResult = directLightResult + indirectLightResult + emissResult;


				FringeOuter_fragOut output;
				// Pack normal
    			half3 packedNormalWS = PackNormal(normalWS);

				output.GBuffer0 = half4(albedo.rgb, 0);
				output.GBuffer1 = half4(PackColorToR8G8B8(directLightResult.rgb), _OuterAlpha);
				output.GBuffer2 = half4(packedNormalWS, 0);
				output.GBuffer3 = half4(indirectLightResult, _OuterAlpha);
				
				return output;

            }
            ENDHLSL
        }
        
        // Outline
		UsePass "Character/Outline/GBufferOutline"

        // ShadowCaster
   		UsePass "Character/LitPBRToon/ShadowCaster"

        // DepthOnly
		UsePass "Character/LitPBRToon/DepthOnly"

        // // DepthNormals
		// UsePass "Character/LitPBRToon/DepthNormals"
    }

    CustomEditor "UnityEditor.DanbaidongGUI.DanbaidongGUI"
	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}