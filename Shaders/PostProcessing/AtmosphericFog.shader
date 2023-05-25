Shader "Hidden/AtmosphericFog"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _FogDensity("Fog Density",float) = 0.5
        _FogColor("Fog Color", color) = (1,1,1,1)

        _SampleCount("_SampleCount",int) = 16
        _MieG("_MieG",float) = 0.682
        _InscatteringExponent("_InscatteringExponent",float) = 1.0
        _MieScatterColor("_MieScatterColor", color) = (1,1,1,1)
        _MoonMieScatterColor("_MoonMieScatterColor", color) = (1,1,1,1)
        _FogMieStrength("_FogMieStrength",float) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        
        #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
        float _FogDensity;
        half4 _FogColor;
        float _HeightFogEnd;
        float _HeightFalloff;
        
        int _SampleCount;

        float _ExtinctionR;

        float _ScatteringM;
        float _ExtinctionM;

        float _MieG;

        float _InscatteringExponent;
        //Mie
        float4 _MieScatterColor;
        float4 _MoonMieScatterColor;
        float _FogMieStrength;

        float _GroundFogDensity;      
        float _GroundHeightFogEnd;    
        float _GroundHeightFalloff;   
        float _GroundFogHeightLimit;  
        float _GroundFogDistanceLimit;
        float _GroundFogDistanceFalloff;
        
        float4 _HeightMap_ST;
        float _HeightScale;
        float4 _HeightMapNoise_ST;

        #define _AtmosphereHeight   80000
        #define _PlanetRadius       6371000
        #define _DensityScaleHeight 1200
        
        // #define skyrayLength (intersection.y)
        #define skyrayLength 10000

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        TEXTURE2D(_LightShaftMaskTex);
        SAMPLER(sampler_LightShaftMaskTex);

        TEXTURE2D(_HeightMap);
        SAMPLER(sampler_HeightMap);

        TEXTURE2D(_HeightMapNoise);
        SAMPLER(sampler_HeightMapNoise);


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

        //只计算mie部分
        float MiePhaseFunction(float cosAngle, float mieG)
        {
            float g = mieG;
            float g2 = g * g;
            float phase = (1.0 / (4.0 * PI)) * ((3.0 * (1.0 - g2)) / (2.0 * (2.0 + g2))) * ((1 + cosAngle * cosAngle) / (pow((1 + g2 - 2 * g*cosAngle), 3.0 / 2.0)));
            return phase;
        }


        void ComputeOutLocalDensity(float3 position, out float localDPA, out float DPC)
        {
            float3 planetCenter = float3(0,-_PlanetRadius,0);
            float height = distance(position,planetCenter) - _PlanetRadius;
            
            localDPA = exp(-(height/_DensityScaleHeight));

            DPC = 0;
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

        float4 IntegrateInscattering(float3 rayStart,float3 rayDir,float rayLength, float3 lightDir,float sampleCount, out float heightFogDensity )
        {
            float3 stepVector = rayDir * (rayLength / sampleCount);
            float stepSize = length(stepVector);
            
            float scatterMie = 0;

            float densityCP = 0;
            float densityPA = 0;
            float localDPA = 0;

            float prevLocalDPA = 0;
            float prevTransmittance = 0;
            
            ComputeOutLocalDensity(rayStart, localDPA, densityCP);
            
            densityPA += localDPA * stepSize;
            prevLocalDPA = localDPA;

            float Transmittance = exp(-(densityCP + densityPA) * _ExtinctionM/100000)*localDPA;
            
            prevTransmittance = Transmittance;
            
            float fog = 0;

            for(float i = 1.0; i < sampleCount; i += 1.0)
            {
                float3 P = rayStart + stepVector * i;
                
                ComputeOutLocalDensity(P, localDPA, densityCP);
                densityPA += (prevLocalDPA + localDPA) * stepSize/2;

                Transmittance = exp(-(densityCP + densityPA) * _ExtinctionM/1000000)*localDPA;

                scatterMie += (prevTransmittance + Transmittance) * stepSize/2;
                

                    P.y = max(P.y,0);
                    float3 planetCenter = float3(0, - _PlanetRadius,0);
                    float height = distance(P,planetCenter) - _PlanetRadius;
                    float falloff = _HeightFalloff * 0.01 * (height - _HeightFogEnd);
                    
                    fog += exp2(-falloff) * stepSize;

                prevTransmittance = Transmittance;
                prevLocalDPA = localDPA;
            }

            heightFogDensity = _FogDensity * fog/1000;


            scatterMie *= _ScatteringM/100000;
            float lightInscatter = scatterMie * MiePhaseFunction(dot(rayDir,-lightDir.xyz), _MieG);
            float moonInscatter = scatterMie * MiePhaseFunction(dot(rayDir,lightDir.xyz), _MieG);

            return float4(ACESFilm(lightInscatter.x*10), moonInscatter, 0, 0);
        }

        ENDHLSL

        ZTest Always Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            
            #pragma multi_compile_local _ _ENABLE_LIGHTSHAFT
            //#pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag

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

            v2f vert(a2v v)
            {
                v2f o;
                o.positionHCS = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;

                return o;
            }
            
            half4 frag(v2f i):SV_Target
            {
                float3 lightDir = _MainLightPosition.xyz;
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
                rayEnd = rayStart + rayLength * rayDir;

                float heightFogDensity = 0;
                float4 inscattering = IntegrateInscattering(rayStart, rayDir, rayLength, -sunPosWS.xyz, _SampleCount, heightFogDensity);
                inscattering.a = inscattering.a * _FogMieStrength;


                //地面雾效
                    float3 heightMapdir = _HeightMap_ST.zyw - rayStart;
                    float heightMapDistance = length(heightMapdir);
                    heightMapdir/=heightMapDistance;

                    float2 heightMapUV = (rayEnd.xz - _HeightMap_ST.zw) * _HeightMap_ST.x + 0.5;
                    float2 heightMapNoiseUV = (rayDir.xy - heightMapdir.xy) * _HeightMapNoise_ST.x;
                    heightMapNoiseUV *= (heightMapDistance/800);
                    heightMapNoiseUV += _HeightMapNoise_ST.zw * _Time.x;

                    float heightMap = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, heightMapUV).r;
                    float heightMapNoise = SAMPLE_TEXTURE2D(_HeightMapNoise, sampler_HeightMapNoise, heightMapNoiseUV ).r;

                    float endHeight = heightMap * heightMapNoise;
                    
                    endHeight *= _HeightScale * step(rayLength, 5000);
                    endHeight += _GroundHeightFogEnd;
                
                float falloff = _GroundHeightFalloff*0.01 * (rayEnd.y - min(_WorldSpaceCameraPos.y,_GroundFogHeightLimit) - endHeight);
                float fogDensity =  _GroundFogDensity * exp2(-falloff);
                float fogFactor = (1-exp2(-falloff))/falloff;

                float distanceFalloff = max(rayLength - _GroundFogDistanceLimit,0);
                float distanceFactor = distanceFalloff/(distanceFalloff + _GroundFogDistanceFalloff);
                float groundFog  = fogDensity * fogFactor * distanceFactor;


                //整体雾效 + 散射
                float mixedFogDensity = max(heightFogDensity, groundFog);
                float fogScatter = pow(mixedFogDensity * saturate(dot(rayDir.xz,lightDir.xz)), _InscatteringExponent) * _FogMieStrength;

                float3 mixedFogColor = lerp(_FogColor.rgb, _MieScatterColor.rgb, clamp(fogScatter, 0, _MieScatterColor.a));
                float4 mainColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                float3 finalColor = lerp(mainColor.rgb, mixedFogColor, ACESFilm(mixedFogDensity) * _FogColor.a);
                finalColor = lerp(finalColor, _MieScatterColor.rgb, inscattering.x * _MieScatterColor.a);
                finalColor = lerp(finalColor, _MoonMieScatterColor.rgb, inscattering.y * _MoonMieScatterColor.a);
            #if _ENABLE_LIGHTSHAFT
                half lightShaftMask = SAMPLE_TEXTURE2D(_LightShaftMaskTex, sampler_LightShaftMaskTex, i.uv).r;
                half4 lightShaftColor = step(0, sunPosWS.y) * _MieScatterColor + step(sunPosWS.y, 0) * _MoonMieScatterColor / 3;
                finalColor += lightShaftColor.rgb * lightShaftMask * lightShaftColor.a * 0.5;
            #endif

                return half4(finalColor, 1);


                // 降分辨率方式输出
                // float3 finalColor = lerp(0, mixedFogColor, saturate(mixedFogDensity));
                // finalColor = lerp(finalColor, _MieScatterColor.rgb, saturate(inscattering.r * _MieScatterColor.a));

                // float alpha = lerp(saturate(mixedFogDensity), 1, saturate(inscattering.r * _MieScatterColor.a));
                
                // return half4(finalColor, alpha);
            
            }


            ENDHLSL
        
        }

        pass
        {
            Blend One OneMinusSrcAlpha 
            
            HLSLPROGRAM
            
            #pragma vertex vert_blend
            #pragma fragment frag_blend

            
            struct a2v
            {
                float4 vertex: POSITION;
                float2 uv: TEXCOORD0;
            };
            
            struct v2f
            {
                float4 positionHCS: SV_POSITION;
                float2 uv: TEXCOORD0;
            };
            
            v2f vert_blend(a2v v)
            {
                v2f o;
                o.positionHCS = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            half4 frag_blend(v2f i): SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            }
            
            ENDHLSL
            
        }
    }
    FallBack "Diffuse"
}