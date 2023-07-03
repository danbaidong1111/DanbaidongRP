Shader "Hidden/Universal Render Pipeline/Bloom"
{
    HLSLINCLUDE
        #pragma exclude_renderers gles
        #pragma multi_compile_local _ _USE_RGBM
        #pragma multi_compile _ _USE_DRAW_PROCEDURAL

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/PostProcessing/Common.hlsl"

        TEXTURE2D_X(_SourceTex);
        float4 _SourceTex_TexelSize;
        TEXTURE2D_X(_SourceTexLowMip);
        float4 _SourceTexLowMip_TexelSize;

        float4 _Params; // x: scatter, y: clamp, z: threshold (linear), w: threshold knee

        #define Scatter             _Params.x
        #define ClampMax            _Params.y
        #define Threshold           _Params.z
        #define ThresholdKnee       _Params.w

        half4 EncodeHDR(half3 color)
        {
        #if _USE_RGBM
            half4 outColor = EncodeRGBM(color);
        #else
            half4 outColor = half4(color, 1.0);
        #endif

        #if UNITY_COLORSPACE_GAMMA
            return half4(sqrt(outColor.xyz), outColor.w); // linear to γ
        #else
            return outColor;
        #endif
        }

        half3 DecodeHDR(half4 color)
        {
        #if UNITY_COLORSPACE_GAMMA
            color.xyz *= color.xyz; // γ to linear
        #endif

        #if _USE_RGBM
            return DecodeRGBM(color);
        #else
            return color.xyz;
        #endif
        }

        half4 FragPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        #if _BLOOM_HQ
            float texelSize = _SourceTex_TexelSize.x;
            half4 A = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, -1.0));
            half4 B = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.0, -1.0));
            half4 C = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, -1.0));
            half4 D = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-0.5, -0.5));
            half4 E = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.5, -0.5));
            half4 F = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, 0.0));
            half4 G = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv);
            half4 H = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, 0.0));
            half4 I = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-0.5, 0.5));
            half4 J = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.5, 0.5));
            half4 K = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(-1.0, 1.0));
            half4 L = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(0.0, 1.0));
            half4 M = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + texelSize * float2(1.0, 1.0));

            half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

            half4 o = (D + E + I + J) * div.x;
            o += (A + B + G + F) * div.y;
            o += (B + C + H + G) * div.y;
            o += (F + G + L + K) * div.y;
            o += (G + H + M + L) * div.y;

            half3 color = o.xyz;
        #else
            half3 color = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv).xyz;
        #endif

            // User controlled clamp to limit crazy high broken spec
            color = min(ClampMax, color);

            // Thresholding
            half brightness = Max3(color.r, color.g, color.b);
            half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
            softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
            half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
            color *= multiplier;

            return EncodeHDR(color);
        }

        half4 FragBlurH(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float texelSize = _SourceTex_TexelSize.x * 2.0;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

            // 9-tap gaussian blur on the downsampled source
            half3 c0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0)));
            half3 c1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0)));
            half3 c2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0)));
            half3 c3 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0)));
            half3 c4 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv                               ));
            half3 c5 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0)));
            half3 c6 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0)));
            half3 c7 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0)));
            half3 c8 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0)));

            half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                        + c4 * 0.22702703
                        + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

            return EncodeHDR(color);
        }

        half4 FragBlurV(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float texelSize = _SourceTex_TexelSize.y;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

            // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
            half3 c0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923)));
            half3 c1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538)));
            half3 c2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv                                      ));
            half3 c3 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538)));
            half3 c4 = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923)));

            half3 color = c0 * 0.07027027 + c1 * 0.31621622
                        + c2 * 0.22702703
                        + c3 * 0.31621622 + c4 * 0.07027027;

            return EncodeHDR(color);
        }

        half3 Upsample(float2 uv)
        {
            half3 highMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv));

        #if _BLOOM_HQ && !defined(SHADER_API_GLES)
            half3 lowMip = DecodeHDR(SampleTexture2DBicubic(TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, unity_StereoEyeIndex));
        #else
            half3 lowMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, uv));
        #endif

            return lerp(highMip, lowMip, Scatter);
        }

        half4 FragUpsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 color = Upsample(UnityStereoTransformScreenSpaceTex(input.uv));
            return EncodeHDR(color);
        }

        float2 _Bloomv2BlurScaler;
        float4 _Bloomv2_Params;// Intensity Threshold

        struct a2v_blur
        {
            float4 vertex   :POSITION;
            float2 uv: TEXCOORD0;
        };
        struct v2f_vlur 
        {
            float4 positionHCS : SV_POSITION;
            float2 uv: TEXCOORD0;
        };

        v2f_vlur VertBlur_v2(a2v_blur v) 
        {
            v2f_vlur o;
            o.positionHCS = TransformObjectToHClip(v.vertex.xyz);
            o.uv = v.uv;

            return o;
        }
    ENDHLSL

    // Bloom Danbaidong
    HLSLINCLUDE

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Bloom Prefilter"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragPrefilter
                #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Horizontal"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Vertical"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragBlurV
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Upsample"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragUpsample
                #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }

        // 4: Bloom Danbaidong preFilter
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertPreFilter_v2
            #pragma fragment FragPreFilter_v2

            struct a2v_preFilter
            {
                float4 vertex   :POSITION;
                float2 uv: TEXCOORD0;
            };
            struct v2f_preFilter 
            {
                float4 positionHCS : SV_POSITION;
                float2 uv0: TEXCOORD0;
                float2 uv1: TEXCOORD1;
                float2 uv2: TEXCOORD2;
                float2 uv3: TEXCOORD3;
            };

            v2f_preFilter VertPreFilter_v2(a2v_preFilter v) 
            {
                v2f_preFilter o;
                o.positionHCS = TransformObjectToHClip(v.vertex.xyz);

                o.uv0 = v.uv + half2(-1, -1) * _SourceTex_TexelSize.xy;
                o.uv1 = v.uv + half2( 1, -1) * _SourceTex_TexelSize.xy;
                o.uv2 = v.uv + half2(-1,  1) * _SourceTex_TexelSize.xy;
                o.uv3 = v.uv + half2( 1,  1) * _SourceTex_TexelSize.xy;

                return o;
            }
            
            half4 FragPreFilter_v2(v2f_preFilter i) : SV_Target
            {
                half3 mainCol = 0;
                mainCol += (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv0));
                mainCol += (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv1));
                mainCol += (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv2));
                mainCol += (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv3));  
                mainCol *= 0.25;

                // mainCol *= 1 / (1 + 0.1 * Luminance(mainCol.rgb));
                mainCol -= _Bloomv2_Params.y;
                mainCol = max(half3(0,0,0), mainCol.rgb);
                // C# mul
                mainCol *= _Bloomv2_Params.x;


                // mainCol = LinearToSRGB(mainCol);
                // mainCol -= _Bloomv2_Params.y;
                // mainCol = max(half3(0,0,0), mainCol.rgb);
                // mainCol *= _Bloomv2_Params.x;
                // mainCol = SRGBToLinear(mainCol);

                return EncodeHDR(mainCol);
            }

            ENDHLSL
        }
        
        // 5: Bloom Danbaidong downsampler
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertDownSample_v2
            #pragma fragment FragDownSample_v2
            

            struct a2v_downsampler 
            {
                float4 vertex   :POSITION;
                float2 uv: TEXCOORD0;
            };
            struct v2f_downsampler 
            {
                float4 positionHCS : SV_POSITION;
                float2 uv0: TEXCOORD0;
                float2 uv1: TEXCOORD1;
                float2 uv2: TEXCOORD2;
                float2 uv3: TEXCOORD3;
            };

            v2f_downsampler VertDownSample_v2(a2v_downsampler v) 
            {
                v2f_downsampler o;
                o.positionHCS = TransformObjectToHClip(v.vertex.xyz);
                o.uv0 = v.uv + half2(0.95999998, 0.25)      * _SourceTex_TexelSize.xy;
                o.uv1 = v.uv + half2(0.25, -0.95999998)     * _SourceTex_TexelSize.xy;
                o.uv2 = v.uv + half2(-0.95999998, -0.25)    * _SourceTex_TexelSize.xy;
                o.uv3 = v.uv + half2(-0.25, 0.95999998)     * _SourceTex_TexelSize.xy;

                // o.uv0 = v.uv + half2(-1, -1)      * _SourceTex_TexelSize.xy;
                // o.uv1 = v.uv + half2(1, -1)     * _SourceTex_TexelSize.xy;
                // o.uv2 = v.uv + half2(-1, 1)    * _SourceTex_TexelSize.xy;
                // o.uv3 = v.uv + half2(1, 1)     * _SourceTex_TexelSize.xy;

                return o;
            }

            half4 FragDownSample_v2(v2f_downsampler i) : SV_Target
            {
                half3 s;
                s =  DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv0));
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv1));
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv2));
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, i.uv3));  
                
                return EncodeHDR(s * 0.25);
            }

            ENDHLSL
        }

        // 6: first pre blur, sigma = 2.6, 加速高斯模糊, 半径5, 7次采样
        /*
        *  [0]offset: 5.307122000, weight: 0.035270680
        *  [1]offset: 3.373378000, weight: 0.127357100
        *  [2]offset: 1.444753000, weight: 0.259729700
        *  [3]offset: 0.000000000, weight: 0.155285200
        */
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertBlur_v2
            #pragma fragment FragBlur_pre
            

            half4 FragBlur_pre(v2f_vlur i) : SV_Target
            {
                float2 scaler = _Bloomv2BlurScaler * _SourceTex_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.uv.xy + scaler.xy * 5.307122000;
                float2 offsetUV1 = i.uv.xy - scaler.xy * 5.307122000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.035270680;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.035270680;

                offsetUV0 = i.uv.xy + scaler.xy * 3.373378000;
                offsetUV1 = i.uv.xy - scaler.xy * 3.373378000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.127357100;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.127357100;  

                offsetUV0 = i.uv.xy + scaler.xy * 1.444753000;
                offsetUV1 = i.uv.xy - scaler.xy * 1.444753000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.259729700;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.259729700;

                offsetUV0 = i.uv.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.155285200;

                return EncodeHDR(s);
            }

            ENDHLSL
        }

        // 7: mip 1st blur, sigma = 3.2, 加速高斯模糊, 半径8, 9次采样
        /*
        *    [0]offset: 7.324664000, weight: 0.017001690
        *    [1]offset: 5.368860000, weight: 0.058725350
        *    [2]offset: 3.415373000, weight: 0.138472900
        *    [3]offset: 1.463444000, weight: 0.222984700
        *    [4]offset: 0.000000000, weight: 0.125630700
        */
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertBlur_v2
            #pragma fragment FragBlur_first
            

            half4 FragBlur_first(v2f_vlur i) : SV_Target
            {
                float2 scaler = _Bloomv2BlurScaler * _SourceTex_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.uv.xy + scaler.xy * 7.324664000;
                float2 offsetUV1 = i.uv.xy - scaler.xy * 7.324664000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.017001690;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.017001690;

                offsetUV0 = i.uv.xy + scaler.xy * 5.368860000;
                offsetUV1 = i.uv.xy - scaler.xy * 5.368860000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.058725350;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.058725350;  

                offsetUV0 = i.uv.xy + scaler.xy * 3.415373000;
                offsetUV1 = i.uv.xy - scaler.xy * 3.415373000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.138472900;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.138472900;

                offsetUV0 = i.uv.xy + scaler.xy * 1.463444000;
                offsetUV1 = i.uv.xy - scaler.xy * 1.463444000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.222984700;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.222984700;

                offsetUV0 = i.uv.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.125630700;

                return EncodeHDR(s);
            }

            ENDHLSL
        }

        // 8: mip 2nd blur, sigma = 5.3, 加速高斯模糊，半径16, 17次采样
        /*
        *    [0]offset: 15.365450000, weight: 0.002165789
        *    [1]offset: 13.382110000, weight: 0.006026655
        *    [2]offset: 11.399060000, weight: 0.014561720
        *    [3]offset: 9.416246000,  weight: 0.030551590
        *    [4]offset: 7.433644000,  weight: 0.055660430
        *    [5]offset: 5.451206000,  weight: 0.088055510
        *    [6]offset: 3.468890000,  weight: 0.120967400
        *    [7]offset: 1.486653000,  weight: 0.144306200
        *    [8]offset: 0.000000000,  weight: 0.075409520
        */
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertBlur_v2
            #pragma fragment FragBlur_second
            

            half4 FragBlur_second(v2f_vlur i) : SV_Target
            {
                float2 scaler = _Bloomv2BlurScaler * _SourceTex_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.uv.xy + scaler.xy * 15.365450000;
                float2 offsetUV1 = i.uv.xy - scaler.xy * 15.365450000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.002165789;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.002165789;

                offsetUV0 = i.uv.xy + scaler.xy * 13.382110000;
                offsetUV1 = i.uv.xy - scaler.xy * 13.382110000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.006026655;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.006026655;  

                offsetUV0 = i.uv.xy + scaler.xy * 11.399060000;
                offsetUV1 = i.uv.xy - scaler.xy * 11.399060000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.014561720;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.014561720;

                offsetUV0 = i.uv.xy + scaler.xy * 9.416246000;
                offsetUV1 = i.uv.xy - scaler.xy * 9.416246000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.030551590;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.030551590;

                offsetUV0 = i.uv.xy + scaler.xy * 7.433644000;
                offsetUV1 = i.uv.xy - scaler.xy * 7.433644000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.055660430;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.055660430;

                offsetUV0 = i.uv.xy + scaler.xy * 5.451206000;
                offsetUV1 = i.uv.xy - scaler.xy * 5.451206000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.088055510;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.088055510;

                offsetUV0 = i.uv.xy + scaler.xy * 3.468890000;
                offsetUV1 = i.uv.xy - scaler.xy * 3.468890000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.120967400;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.120967400;

                offsetUV0 = i.uv.xy + scaler.xy * 1.486653000;
                offsetUV1 = i.uv.xy - scaler.xy * 1.486653000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.144306200;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.144306200;

                offsetUV0 = i.uv.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.075409520;

                return EncodeHDR(s);
            }

            ENDHLSL
        }

        // 9: mip 3rd blur, sigma = 6.65, 加速高斯模糊，半径20, 21次采样
        /*
        *    [0]offset: 19.391510000, weight: 0.001667595
        *    [1]offset: 17.402340000, weight: 0.003832045
        *    [2]offset: 15.413260000, weight: 0.008048251
        *    [3]offset: 13.424270000, weight: 0.015449170
        *    [4]offset: 11.435350000, weight: 0.027104610
        *    [5]offset: 9.446500000,  weight: 0.043462710
        *    [6]offset: 7.457702000,  weight: 0.063698220
        *    [7]offset: 5.468947000,  weight: 0.085324850
        *    [8]offset: 3.480224000,  weight: 0.104463000
        *    [9]offset: 1.491521000,  weight: 0.116892900
        *    [10]offset: 0.000000000, weight: 0.060113440
        */
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertBlur_v2
            #pragma fragment FragBlur_third
            

            half4 FragBlur_third(v2f_vlur i) : SV_Target
            {
                float2 scaler = _Bloomv2BlurScaler * _SourceTex_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.uv.xy + scaler.xy * 19.391510000;
                float2 offsetUV1 = i.uv.xy - scaler.xy * 19.391510000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.001667595;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.001667595;

                offsetUV0 = i.uv.xy + scaler.xy * 17.402340000;
                offsetUV1 = i.uv.xy - scaler.xy * 17.402340000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.003832045;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.003832045;  

                offsetUV0 = i.uv.xy + scaler.xy * 15.413260000;
                offsetUV1 = i.uv.xy - scaler.xy * 15.413260000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.008048251;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.008048251;

                offsetUV0 = i.uv.xy + scaler.xy * 13.424270000;
                offsetUV1 = i.uv.xy - scaler.xy * 13.424270000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.015449170;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.015449170;  

                offsetUV0 = i.uv.xy + scaler.xy * 11.435350000;
                offsetUV1 = i.uv.xy - scaler.xy * 11.435350000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.027104610;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.027104610;

                offsetUV0 = i.uv.xy + scaler.xy * 9.446500000;
                offsetUV1 = i.uv.xy - scaler.xy * 9.446500000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.043462710;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.043462710;  

                offsetUV0 = i.uv.xy + scaler.xy * 7.457702000;
                offsetUV1 = i.uv.xy - scaler.xy * 7.457702000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.063698220;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.063698220;

                offsetUV0 = i.uv.xy + scaler.xy * 5.468947000;
                offsetUV1 = i.uv.xy - scaler.xy * 5.468947000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.085324850;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.085324850;  

                offsetUV0 = i.uv.xy + scaler.xy * 3.480224000;
                offsetUV1 = i.uv.xy - scaler.xy * 3.480224000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.104463000;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.104463000;

                offsetUV0 = i.uv.xy + scaler.xy * 1.491521000;
                offsetUV1 = i.uv.xy - scaler.xy * 1.491521000;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.116892900;
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV1)) * 0.116892900;  

                offsetUV0 = i.uv.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _SourceTex_TexelSize.xy, 1 - _SourceTex_TexelSize.xy);
                s += DecodeHDR (SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, offsetUV0)) * 0.060113440;

                return EncodeHDR(s);
            }

            ENDHLSL
        }
        
        // 10: Bloom Danbaidong upsampler
        Pass
        {
            HLSLPROGRAM     
            #pragma vertex   VertUpSample_v2
            #pragma fragment FragUpSample_v2
            

            TEXTURE2D_X(_BloomMip0); 
            TEXTURE2D_X(_BloomMip1); 
            TEXTURE2D_X(_BloomMip2); 

            struct a2v_upsampler 
            {
                float4 vertex   :POSITION;
                float2 uv: TEXCOORD0;
            };
            struct v2f_upsampler 
            {
                float4 positionHCS : SV_POSITION;
                float2 uv: TEXCOORD0;
            };

            v2f_upsampler VertUpSample_v2(a2v_upsampler v) 
            {
                v2f_upsampler o;
                o.positionHCS = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 FragUpSample_v2(v2f_upsampler i) : SV_Target
            {
                half4 combineScale = half4(0.3,0.3,0.26,0.15);
                // half4 combineScale = half4(0.24, 0.24, 0.28, 0.225);//bh3
                half3 main = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex,   sampler_LinearClamp, i.uv)) * combineScale.x;
                half3 mip0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMip0, sampler_LinearClamp, i.uv))* combineScale.y;
                half3 mip1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMip1, sampler_LinearClamp, i.uv))* combineScale.z;
                half3 mip2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMip2, sampler_LinearClamp, i.uv))* combineScale.w;
                
                
                return EncodeHDR( main + mip0 + mip1  + mip2);
            }

            ENDHLSL
        }
    }
}
