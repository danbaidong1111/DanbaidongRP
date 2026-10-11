Shader "Danbaidong/Fog Volume Mask"
{
    Properties
    {
        _ColorMultiplier("Color Multiplier", Color) = (1, 1, 1, 1)
        _DensityMultiplier("Density Multiplier", Range(0, 4)) = 1
        _NoiseAmount("Noise Amount", Range(0, 1)) = 0.5
        _NoiseScale("World Noise Scale", Float) = 0.5
        _NoiseVelocity("World Noise Velocity", Vector) = (0.1, 0, 0, 0)
        _NoiseThreshold("Noise Threshold", Range(0, 1)) = 0.25
        _NoiseSoftness("Noise Softness", Range(0.01, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "DanbaidongFogVolume"
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorMultiplier;
                float4 _NoiseVelocity;
                float _DensityMultiplier;
                float _NoiseAmount;
                float _NoiseScale;
                float _NoiseThreshold;
                float _NoiseSoftness;
            CBUFFER_END

            float _FogLocalSlice;
            float _FogLocalTime;
            float4 _FogLocalMaskSize;
            float4x4 _FogLocalVolumeToWorld;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float lower = lerp(lerp(Hash(cell), Hash(cell + float3(1, 0, 0)), blend.x),
                                   lerp(Hash(cell + float3(0, 1, 0)), Hash(cell + float3(1, 1, 0)), blend.x), blend.y);
                float upper = lerp(lerp(Hash(cell + float3(0, 0, 1)), Hash(cell + float3(1, 0, 1)), blend.x),
                                   lerp(Hash(cell + float3(0, 1, 1)), Hash(cell + float3(1, 1, 1)), blend.x), blend.y);
                return lerp(lower, upper, blend.z);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 uvw = float3(input.positionCS.xy / _FogLocalMaskSize.xy, _FogLocalSlice);
                float3 positionWS = mul(_FogLocalVolumeToWorld, float4(uvw - 0.5, 1)).xyz;
                float3 noisePosition = (positionWS - _NoiseVelocity.xyz * _FogLocalTime) * max(0.0001, _NoiseScale);
                float noise = (Noise(noisePosition) + 0.5 * Noise(noisePosition * 2.03)) / 1.5;
                float density = lerp(1.0, smoothstep(_NoiseThreshold, _NoiseThreshold + max(0.001, _NoiseSoftness), noise), saturate(_NoiseAmount));
                // RGB and alpha are independent multipliers for the component's albedo and extinction.
                return float4(max(0, _ColorMultiplier.rgb), max(0, _DensityMultiplier) * density);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
