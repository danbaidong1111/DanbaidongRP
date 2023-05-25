Shader "Hidden/LightShaft"
{
    Properties
    {
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_MieG("_MieG",float) = 0.7
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

 		HLSLINCLUDE

            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"


            TEXTURE2D(_MainTex);
        	SAMPLER(sampler_MainTex);
			half4 _MainTex_TexelSize;

			float4 _LightScrPos;
			int _SampleDistance;

			half _MieG;
			half _Intensity;

			half _RevertScale;

			#define _AtmosphereHeight   80000
			#define _PlanetRadius       6371000
			#define _DensityScaleHeight 1200
			#define skyrayLength 10000

			#define NUM_SAMPLES 6

        	//直线与球面的交点
			float2 RaySphereIntersection(float3 rayOrigin, float3 rayDir, float3 sphereCenter, float sphereRadius)
			{
				rayOrigin -= sphereCenter;
				float a = dot(rayDir, rayDir);
				float b = 2.0 * dot(rayOrigin, rayDir);
				float c = dot(rayOrigin, rayOrigin) - (sphereRadius * sphereRadius);
				float d = b * b - 4 * a * c;
				if (d < 0)
				{
					return -1;
				}
				else
				{
					d = sqrt(d);
					return float2(-b - d, -b + d) / (2 * a);
				}
			}

			float MiePhaseFunction(float cosAngle, float mieG)
			{
				float g = mieG;
				float g2 = g * g;
				float phase = (1.0 / (4.0 * PI)) * ((3.0 * (1.0 - g2)) / (2.0 * (2.0 + g2))) * ((1 + cosAngle * cosAngle) / (pow((1 + g2 - 2 * g*cosAngle), 3.0 / 2.0)));
				return phase;
			}
			
			float ACESFilm(float x)
			{
				float a = 2.51f;
				float b = 0.03f;
				float c = 2.43f;
				float d = 0.59f;
				float e = 0.14f;
				return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
			}

            struct a2v
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            v2f VertImg(a2v v)
            {
                v2f o;
                o.positionHCS = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }

			half4 FragScatter (v2f i) : SV_Target
            {
                float3 lightDir = GetMainLight().direction;
                float3 sunPosWS = lightDir;

                float2 UV = i.uv;
                
                float depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, UV).r;
                float eyeDepth = LinearEyeDepth(depth, _ZBufferParams);

                float3 rayStart = GetCameraPositionWS();
                float3 rayEnd = ComputeWorldSpacePosition(UV, depth, UNITY_MATRIX_I_VP);

                float3 rayDir = rayEnd - rayStart;
                float rayLength = length(rayDir);
                rayDir /= rayLength;
                
                float3 planetCenter = float3(0, -_PlanetRadius, 0);
                float2 intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius + _AtmosphereHeight);

                //天空，rayEnd在大气层
                half skyboxDepth = 0;
                #if UNITY_REVERSED_Z
                    skyboxDepth = 0;
                #else
                    skyboxDepth = 1;
                #endif
                if(depth == skyboxDepth)
                {
                    rayLength = skyrayLength;
                }

                //地面，rayEnd在地面点
                intersection = RaySphereIntersection(rayStart, rayDir, planetCenter, _PlanetRadius);
                if (intersection.x >= 0)
                {
                    rayLength = min(rayLength, intersection.x);
                }
                
				float scatter = rayLength/10000 * MiePhaseFunction(dot(rayDir,lightDir.xyz), _MieG);
				scatter *= smoothstep(0,0.08,lightDir.y);
				return ACESFilm(scatter * _Intensity);
            }
        ENDHLSL

		// Pass0: generate sctterArea
        Pass
        {
			HLSLPROGRAM

            #pragma vertex VertImg
            #pragma fragment FragScatter
           
			ENDHLSL
        }


		// Pass1:  Radial blur
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertImg
			#pragma fragment frag



			half4 frag(v2f i) : SV_Target
			{
				float2 texcoord = i.uv;
				float2 deltaTexcoord = texcoord - _LightScrPos.xy;
				
				
				float p = 0.01;
				deltaTexcoord *= p * _SampleDistance / NUM_SAMPLES;
				half4 color0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texcoord);
				half4 color = color0;

				half illuminationDecay = 1.0f;
				half decay = 1.0;
				for (int idx = 0; idx < NUM_SAMPLES; idx++) {
					texcoord -= deltaTexcoord;
					color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texcoord) * illuminationDecay;
					illuminationDecay *= decay;
				}
				color /= NUM_SAMPLES;

				return color;
			}
			ENDHLSL
		}

		// Pass2:  Revert Radial blur
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertImg
			#pragma fragment frag

			
			half4 frag(v2f i) : SV_Target
			{
				float2 texcoord = i.uv;
				float2 deltaTexcoord = texcoord - _LightScrPos.xy;
				
				
				float p = 0.01;
				deltaTexcoord *= -p * _SampleDistance / NUM_SAMPLES / (3 + _RevertScale);
				half4 color0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texcoord);
				half4 color = color0;

				half illuminationDecay = 1.0f;
				half decay = 1.0;
				for (int idx = 0; idx < NUM_SAMPLES; idx++) {
					texcoord -= deltaTexcoord;
					color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texcoord) * illuminationDecay;
					illuminationDecay *= decay;
				}
				color /= NUM_SAMPLES;

				return color;
			}
			ENDHLSL
		}
		
		// Pass3: upsample
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertImg
			#pragma fragment frag

			TEXTURE2D(_DownSampleTex);
        	SAMPLER(sampler_DownSampleTex);
			float4 _DownSampleTex_TexelSize;

			
			float GaussWeight2D(float x, float y, float sigma)
			{
				float gaussianWeight[5] = {0.4026, 0.2442, 0.0545, 0.2442, 0.4026};
				float xValue = gaussianWeight[x-1];
				float yValue = gaussianWeight[y-1];

				return xValue * yValue;
			}

			float3 Gauss5x5(TEXTURE2D_PARAM(tex, samplerName), float2 uv, int n, float2 stride, float sigma)
			{
				float3 color = float3(0,0,0);
				int r = 5 / 2;
				float weight = 0.0;
				
				for (int i = -r; i <= r; i++)
				{
					for (int j = -r; j <= r; j++)
					{
						float w = GaussWeight2D(i, j, sigma);
						float2 coord = uv + float2(i,j) * stride;
						color += SAMPLE_TEXTURE2D(tex, samplerName, coord).rgb * w;
						weight += w;
					}
				}
				color /= weight;
				return color;
			}

			half4 frag(v2f i) : SV_Target
			{
				// half3 maintex = tex2D(_MainTex, i.uv);
				// half3 downTex = tex2D(_DownSampleTex, i.uv);

				half3 maintex = Gauss5x5(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.uv, 5, _MainTex_TexelSize.xy, 1.0);
				half3 downTex = Gauss5x5(TEXTURE2D_ARGS(_DownSampleTex, sampler_DownSampleTex), i.uv, 5, _DownSampleTex_TexelSize.xy, 1.0);

				return half4((maintex + downTex)/2 , 1);
			}
			ENDHLSL
		}

		// Pass4: downSample
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertImg
			#pragma fragment frag

			half4 frag(v2f i) : SV_Target
			{
				half4 maintex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
				return maintex;
			}
			ENDHLSL
		}


    }
}