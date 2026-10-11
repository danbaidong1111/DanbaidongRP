Shader "Hidden/Danbaidong/AdvancedDepthOfField"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/PostProcessing/Common.hlsl"

    TEXTURE2D_X(_AdvancedCoC);
    TEXTURE2D_X(_AdvancedNear);
    TEXTURE2D_X(_AdvancedFar);
    TEXTURE2D_X(_AdvancedTiles);
    TEXTURE2D_X(_AdvancedNearResult);
    TEXTURE2D_X(_AdvancedFarResult);
    float4 _AdvancedLens;       // Focus distance, thin-lens coefficient, near radius, far radius.
    float4 _AdvancedRanges;     // Near start/end, far start/end.
    float4 _AdvancedParams;     // Manual ranges, reduction factor, tile width.
    float4 _AdvancedCounts;
    float4 _AdvancedProjection;
    float2 _AdvancedDepthJitter;
    float4 _AdvancedSize;
    float4 _AdvancedBlurSize;
    float4 _AdvancedTileSize;
    float4 _AdvancedNearKernel[128];
    float4 _AdvancedFarKernel[128];

    float2 ScreenUV(Varyings input)
    {
        return UnityStereoTransformScreenSpaceTex(input.texcoord);
    }

    float2 SafeUV(float2 uv, float2 texel)
    {
        return clamp(uv, texel * 0.5, 1.0 - texel * 0.5);
    }

    float2 ReadCoCData(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X(_AdvancedCoC, sampler_PointClamp, SafeUV(uv, _AdvancedSize.zw)).rg;
    }

    float ReadCoC(float2 uv) { return ReadCoCData(uv).x; }

    float3 ReadNearColor(float2 uv)
    {
        float2 pixel = uv * _AdvancedBlurSize.xy - 0.5;
        float2 first = floor(pixel);
        float2 fraction = frac(pixel);
        float3 color = 0.0;
        float weightSum = 0.0;
        [unroll] for (int y = 0; y < 2; ++y)
        [unroll] for (int x = 0; x < 2; ++x)
        {
            float2 sampleUV = SafeUV((first + float2(x, y) + 0.5) * _AdvancedBlurSize.zw, _AdvancedBlurSize.zw);
            float4 tap = SAMPLE_TEXTURE2D_X(_AdvancedNear, sampler_PointClamp, sampleUV);
            float weight = (x == 0 ? 1.0 - fraction.x : fraction.x) * (y == 0 ? 1.0 - fraction.y : fraction.y);
            weight *= step(0.00001, tap.a);
            color += tap.rgb * weight;
            weightSum += weight;
        }
        return color / max(weightSum, 0.00001);
    }

    float3 ReadSource(float2 uv)
    {
        float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, SafeUV(uv, _AdvancedSize.zw)).rgb;
        #if defined(UNITY_COLORSPACE_GAMMA)
            color = GetSRGBToLinear(color);
        #endif
        return max(color, 0.0);
    }

    float2 FragCoC(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 depthOffset = _AdvancedDepthJitter;
        #if UNITY_UV_STARTS_AT_TOP
            depthOffset.y = -depthOffset.y;
        #endif
        float depth = SampleSceneDepth(input.texcoord + depthOffset);
        float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
        if (_AdvancedProjection.z > 0.5)
        {
            #if UNITY_REVERSED_Z
                depth = 1.0 - depth;
            #endif
            linearDepth = lerp(_AdvancedProjection.x, _AdvancedProjection.y, depth);
        }
        float coc = _AdvancedLens.y * (1.0 - _AdvancedLens.x / max(linearDepth, 0.0001));
        if (_AdvancedParams.x > 0.5)
        {
            float nearCoC = 1.0 - saturate((linearDepth - _AdvancedRanges.x) / max(_AdvancedRanges.y - _AdvancedRanges.x, 0.001));
            float farCoC = saturate((linearDepth - _AdvancedRanges.z) / max(_AdvancedRanges.w - _AdvancedRanges.z, 0.001));
            coc = farCoC * _AdvancedLens.w - nearCoC * _AdvancedLens.z;
        }
        return float2(clamp(coc, -_AdvancedLens.z, _AdvancedLens.w), min(linearDepth, 65000.0));
    }

    float4 Prefilter(float2 uv, bool nearField)
    {
        int reduction = (int)_AdvancedParams.y;
        int2 firstPixel = int2(uv * _AdvancedBlurSize.xy) * reduction;
        float3 colorSum = 0.0;
        float weightSum = 0.0;
        float radius = 0.0;
        [loop] for (int y = 0; y < reduction; ++y)
        [loop] for (int x = 0; x < reduction; ++x)
        {
            float2 sampleUV = (firstPixel + int2(x, y) + 0.5) * _AdvancedSize.zw;
            float coc = ReadCoC(sampleUV);
            float weight = nearField ? saturate(-coc) : step(0.0, coc);
            colorSum += ReadSource(sampleUV) * weight;
            weightSum += weight;
            radius = max(radius, nearField ? -coc : coc);
        }
        if (!nearField)
        {
            // Premultiplied validity survives mip reduction without darkening background
            // colors next to foreground surfaces excluded from this layer.
            float sampleCount = reduction * reduction;
            return float4(colorSum / sampleCount, weightSum / sampleCount);
        }
        return float4(colorSum / max(weightSum, 0.0001), radius);
    }

    float4 FragNearPrefilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Prefilter(ScreenUV(input), true);
    }
    float4 FragFarPrefilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Prefilter(ScreenUV(input), false);
    }

    float FragTileMax(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        int2 firstPixel = int2(ScreenUV(input) * _AdvancedTileSize.xy) * 8;
        float radius = 0.0;
        [unroll] for (int y = 0; y < 8; ++y)
        [unroll] for (int x = 0; x < 8; ++x)
        {
            int2 pixel = min(firstPixel + int2(x, y), (int2)_AdvancedBlurSize.xy - 1);
            radius = max(radius, LOAD_TEXTURE2D_X(_BlitTexture, pixel).a);
        }
        return radius;
    }

    float Dilate(float2 uv, int2 direction)
    {
        int2 pixel = int2(uv * _AdvancedTileSize.xy);
        int extent = (int)ceil(_AdvancedLens.z / (_AdvancedParams.y * 8.0)) + 1;
        float radius = 0.0;
        [loop] for (int i = -extent; i <= extent; ++i)
        {
            int2 tap = clamp(pixel + direction * i, int2(0, 0), (int2)_AdvancedTileSize.xy - 1);
            float sampleRadius = LOAD_TEXTURE2D_X(_BlitTexture, tap).r;
            // An extra tile allows for the destination's position inside its tile.
            if (sampleRadius >= max(0, abs(i) - 1) * 8.0 * _AdvancedParams.y)
                radius = max(radius, sampleRadius);
        }
        return radius;
    }
    float FragDilateX(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Dilate(ScreenUV(input), int2(1, 0));
    }
    float FragDilateY(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Dilate(ScreenUV(input), int2(0, 1));
    }

    float4 FragNearBlur(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = ScreenUV(input);
        // Tile boundaries follow integer groups of eight blur pixels, including partial edge tiles.
        float2 tileUV = ((floor(uv * _AdvancedBlurSize.xy / 8.0)) + 0.5) * _AdvancedTileSize.zw;
        float radius = SAMPLE_TEXTURE2D_X(_AdvancedTiles, sampler_PointClamp, tileUV).r;
        if (radius < 0.5) return 0.0;
        float3 colorSum = 0.0;
        float weightSum = 0.0;
        float coverage = 0.0;
        float softEdge = max(1.0, _AdvancedParams.y);
        int count = (int)_AdvancedCounts.x;
        [loop] for (int i = 0; i < count; ++i)
        {
            float3 offset = _AdvancedNearKernel[i].xyz * radius;
            float2 sampleUV = SafeUV(uv + offset.xy * _AdvancedSize.zw, _AdvancedBlurSize.zw);
            float sampleRadius = max(0.0, -ReadCoC(sampleUV));
            float weight = saturate((sampleRadius - offset.z + softEdge) / softEdge) * saturate(sampleRadius);
            // Compensate the sampled disc area to retain foreground coverage when CoC varies.
            // Limit it to one tap's footprint so subpixel CoCs cannot dominate a large search disc.
            float minimumArea = max(1.0, radius * radius / count);
            weight *= radius * radius / max(sampleRadius * sampleRadius, minimumArea);
            float3 color = ReadNearColor(sampleUV);
            colorSum += color * weight;
            weightSum += weight;
            coverage += weight / count;
        }
        if (weightSum < 0.0001)
        {
            float4 center = SAMPLE_TEXTURE2D_X(_AdvancedNear, sampler_PointClamp, uv);
            float alpha = smoothstep(0.5, 2.0, -ReadCoC(uv));
            return float4(center.rgb * alpha, alpha);
        }
        coverage = saturate(coverage);
        return float4(colorSum / weightSum * coverage, coverage);
    }

    float4 FragFarBlur(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = ScreenUV(input);
        float4 center = SAMPLE_TEXTURE2D_X(_AdvancedFar, sampler_PointClamp, uv);
        float2 centerCoC = ReadCoCData(uv);
        float coc = centerCoC.x;
        bool reconstructBackground = coc < -0.5;
        float radius = abs(coc);
        float3 centerColor = coc < 0.0 ? SAMPLE_TEXTURE2D_X(_AdvancedNear, sampler_PointClamp, uv).rgb
            : center.rgb / max(center.a, 0.0001);
        if (radius < 0.5) return float4(centerColor, coc);
        float3 colorSum = 0.0;
        float weightSum = 0.0;
        float softEdge = max(1.0, _AdvancedParams.y);
        int count = (int)_AdvancedCounts.y;
        // Each aperture tap integrates a finite footprint. Mips avoid sparse bright texels
        // turning into a constellation of repeated point samples at large blur radii.
        float footprint = radius * sqrt(PI / count) / _AdvancedParams.y;
        float lod = max(0.0, log2(max(footprint, 1.0)));
        [loop] for (int i = 0; i < count; ++i)
        {
            float3 offset = _AdvancedFarKernel[i].xyz * radius;
            float2 sampleUV = SafeUV(uv + offset.xy * _AdvancedSize.zw, _AdvancedBlurSize.zw);
            float4 tap = SAMPLE_TEXTURE2D_X_LOD(_AdvancedFar, sampler_LinearClamp, sampleUV, reconstructBackground ? 0.0 : lod);
            float2 sampleCoC = ReadCoCData(sampleUV);
            float weight;
            float3 color;
            if (reconstructBackground)
            {
                // A farther surface is a valid backdrop even when it is also in front of
                // the focus plane. CoC sign alone would create a seam at the focus boundary.
                weight = step(centerCoC.y + max(0.001, centerCoC.y * 0.002), sampleCoC.y) / (1.0 + offset.z);
                color = sampleCoC.x < 0.0 ? ReadNearColor(sampleUV)
                    : tap.rgb / max(tap.a, 0.0001);
            }
            else
            {
                weight = saturate((min(radius, max(sampleCoC.x, 0.0)) - offset.z + softEdge) / softEdge);
                weight *= step(0.0, sampleCoC.x) * tap.a;
                color = tap.rgb / max(tap.a, 0.0001);
            }
            colorSum += color * weight;
            weightSum += weight;
        }
        return float4(weightSum > 0.0001 ? colorSum / weightSum : centerColor, coc);
    }

    float4 Postfilter(float2 uv, bool nearField)
    {
        float4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
        float4 result = center * 4.0;
        float weights = 4.0;
        const float2 offsets[4] = { float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1) };
        [unroll] for (int i = 0; i < 4; ++i)
        {
            float2 sampleUV = SafeUV(uv + offsets[i] * _AdvancedBlurSize.zw, _AdvancedBlurSize.zw);
            float4 tap = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, sampleUV);
            float weight = nearField ? 1.0 : rcp(1.0 + abs(tap.a - center.a));
            result += tap * weight;
            weights += weight;
        }
        return result / weights;
    }
    float4 FragNearFilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Postfilter(ScreenUV(input), true);
    }
    float4 FragFarFilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Postfilter(ScreenUV(input), false);
    }

    float4 FragComposite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = ScreenUV(input);
        float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float coc = ReadCoC(uv);
        float2 pixel = uv * _AdvancedBlurSize.xy - 0.5;
        float2 basePixel = floor(pixel);
        float2 fraction = frac(pixel);
        float3 farColor = 0.0;
        float farWeight = 0.0;
        float4 nearColor = 0.0;
        [unroll] for (int y = 0; y < 2; ++y)
        [unroll] for (int x = 0; x < 2; ++x)
        {
            float2 sampleUV = SafeUV((basePixel + float2(x, y) + 0.5) * _AdvancedBlurSize.zw, _AdvancedBlurSize.zw);
            float weight = (x == 0 ? 1.0 - fraction.x : fraction.x) * (y == 0 ? 1.0 - fraction.y : fraction.y);
            float4 farTap = SAMPLE_TEXTURE2D_X(_AdvancedFarResult, sampler_PointClamp, sampleUV);
            float bilateralWeight = weight / (1.0 + abs(coc - farTap.a));
            farColor += farTap.rgb * bilateralWeight;
            farWeight += bilateralWeight;
            nearColor += SAMPLE_TEXTURE2D_X(_AdvancedNearResult, sampler_PointClamp, sampleUV) * weight;
        }
        #if defined(UNITY_COLORSPACE_GAMMA)
            source.rgb = GetSRGBToLinear(source.rgb);
        #endif
        float farBlend = smoothstep(0.5, 2.0, abs(coc));
        float3 color = lerp(source.rgb, farColor / max(farWeight, 0.0001), farBlend);
        float nearAlpha = saturate(nearColor.a);
        float3 foreground = nearColor.a > 0.0001 ? nearColor.rgb / nearColor.a : source.rgb;
        color = lerp(color, foreground, nearAlpha);
        #if defined(UNITY_COLORSPACE_GAMMA)
            color = GetLinearToSRGB(color);
        #endif
        return float4(color, source.a);
    }

    float4 FragDebug(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float coc = ReadCoC(ScreenUV(input));
        return float4(saturate(coc / max(_AdvancedLens.w, 0.001)), 0.0, saturate(-coc / max(_AdvancedLens.z, 0.001)), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "Circle of Confusion"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragCoC
            ENDHLSL
        }
        Pass
        {
            Name "Near Prefilter"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragNearPrefilter
            ENDHLSL
        }
        Pass
        {
            Name "Far Prefilter"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragFarPrefilter
            ENDHLSL
        }
        Pass
        {
            Name "Foreground Tile Max"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragTileMax
            ENDHLSL
        }
        Pass
        {
            Name "Dilate Foreground X"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDilateX
            ENDHLSL
        }
        Pass
        {
            Name "Dilate Foreground Y"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDilateY
            ENDHLSL
        }
        Pass
        {
            Name "Near Aperture"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragNearBlur
            ENDHLSL
        }
        Pass
        {
            Name "Far Aperture"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragFarBlur
            ENDHLSL
        }
        Pass
        {
            Name "Near Filter"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragNearFilter
            ENDHLSL
        }
        Pass
        {
            Name "Far Filter"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragFarFilter
            ENDHLSL
        }
        Pass
        {
            Name "Depth Aware Composite"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
        Pass
        {
            Name "Circle of Confusion Debug"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDebug
            ENDHLSL
        }
    }
    Fallback Off
}
