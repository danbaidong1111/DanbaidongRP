#ifndef NPR_TOON_INPUT_INCLUDED
#define NPR_TOON_INPUT_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

// -------------------------------------
// Material Constants
// One fixed layout for every raster and ray-tracing pass.
CBUFFER_START(UnityPerMaterial)

    // -------------------------------------
    // Textures
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float _NormalScale;
    float _LightMapAO;

    // -------------------------------------
    // Direct light and shadow
    float4 _SelfLight;
    float _MainLightColorLerp;
    float _LightArea;
    float _ShadowRampWidth;
    float _ShadowSmooth;
    float _ShadowStrength;
    float4 _ShadowColor;
    float _RampDayNight;
    float _UseGlobalDayTime;

    // -------------------------------------
    // Five material specular groups
    float4 _SpecColor;
    float _SpecSoftness;
    float _SpecPow0;
    float _SpecPow1;
    float _SpecPow2;
    float _SpecPow3;
    float _SpecPow4;
    float _SpecMul0;
    float _SpecMul1;
    float _SpecMul2;
    float _SpecMul3;
    float _SpecMul4;

    // -------------------------------------
    // Metal MatCap
    float4 _MTMapLightColor;
    float4 _MTMapDarkColor;
    float4 _MTMapShadowMulColor;
    float _MTMapScale;
    float _MTMapMul;
    float4 _MTSpecColor;
    float _MTSpecPow;
    float _MTSpecMul;
    float4 _MTTopLayerColor;
    float _MTTopLayerRange;

    // -------------------------------------
    // Indirect light
    float4 _SelfEnvColor;
    float _EnvColorLerp;
    float _IndirDiffUpDirSH;
    float _IndirDiffIntensity;
    float _IndirectOcclusion;

    // -------------------------------------
    // Emission and rim
    float4 _EmissionCol;
    float4 _DirectRimFrontCol;
    float4 _DirectRimBackCol;
    float _DirectRimWidth;
    float _PunctualRimWidth;

    // -------------------------------------
    // Outline
    float _OutLineNormalSource;
    float _OutlineWidth;
    float _OutlineClampScale;
    float _OutlineVertexAlpha;
    float4 _OutlineColor0;
    float4 _OutlineColor1;
    float4 _OutlineColor2;
    float4 _OutlineColor3;
    float4 _OutlineColor4;
    float4 _OutlineDirectLightingColor;
    float _OutlineDirectLightingOffset;
    float4 _OutlinePunctualLightingColor;
    float _OutlinePunctualLightingOffset;

    // -------------------------------------
    // Face SDF
    float _FaceUV;
    float _FaceMaterialID;
    float _FaceUsePackedRamp;
    float _FaceRampRow;
    float _FaceSDFChannel;
    float _FaceSDFInvert;
    float _FaceSDFMirror;
    float _FaceShadowMask;
    float _FaceShadowOffset;
    float _FaceShadowSmooth;
    float _FaceNoseSpecular;
    float4 _NoseSpecColor;
    float _AllDirSDFMapTileSize;

    // -------------------------------------
    // Per-material runtime data, set by CharacterRenderHelper
    float4 _FaceRightDirWS;
    float4 _FaceFrontDirWS;

    // -------------------------------------
    // Alpha clip
    float _Cutoff;

    // -------------------------------------
    // Texture dimensions supplied by Unity
    float4 _PackedShadowRamp_TexelSize;
    float4 _AllDirSDFMap_TexelSize;
CBUFFER_END

// -------------------------------------
// Texture Resources
TEXTURE2D(_BaseMap);             SAMPLER(sampler_BaseMap);
TEXTURE2D(_LightMapTex);         SAMPLER(sampler_LightMapTex);
TEXTURE2D(_NormalMap);           SAMPLER(sampler_NormalMap);
TEXTURE2D(_PackedShadowRamp);
TEXTURE2D(_MTMap);
TEXTURE2D(_MTSpecRampTex);
TEXTURE2D(_FaceLightMap);
TEXTURE2D(_AllDirSDFMap);

// -------------------------------------
// Runtime Parameters
// Set by the time-of-day controller and the pipeline ShadowCaster pass.
float _DAYTIME;
float3 _LightDirection;
float3 _LightPosition;

// -------------------------------------
// Common Helpers
// Green is an artist-authored lighting bias, not a conventional baked lightmap.
float RemapLightMapAO(float halfLambert, float lightMapAO)
{
    float value = saturate(halfLambert) * saturate(lightMapAO * 2.0);
    value = lightMapAO >= 0.95 ? 1.0 : value;
    return lightMapAO <= 0.05 ? 0.0 : value;
}

float GetRampCoordinate(float lightValue, float lightArea, float rampWidth)
{
    return saturate(1.0 - (1.0 - lightValue / max(lightArea, 0.0001)) / max(rampWidth, 0.0001));
}

float SoftStep(float threshold, float value, float softness)
{
    float width = max(softness, 0.0001);
    return smoothstep(threshold - width, threshold + width, value);
}

float3 GetHeadLightDirection(float3 lightDirWS, float3 right, float3 front)
{
    float3 up = SafeNormalize(cross(front, right));
    return SafeNormalize(float3(dot(lightDirWS, right), dot(lightDirWS, up), dot(lightDirWS, front)));
}

// -------------------------------------
// Texture Sampling
float2 GetBaseUV(float2 uv)
{
    return TRANSFORM_TEX(uv, _BaseMap);
}

float4 SampleMainTex(float2 uv)
{
#if defined(SHADER_STAGE_RAY_TRACING) || defined(SHADER_STAGE_COMPUTE)
    return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0);
#else
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
#endif
}

float4 SampleLightMap(float2 uv)
{
#if defined(SHADER_STAGE_RAY_TRACING) || defined(SHADER_STAGE_COMPUTE)
    return SAMPLE_TEXTURE2D_LOD(_LightMapTex, sampler_LightMapTex, uv, 0);
#else
    return SAMPLE_TEXTURE2D(_LightMapTex, sampler_LightMapTex, uv);
#endif
}

float4 SampleNormalTex(float2 uv)
{
#if defined(SHADER_STAGE_RAY_TRACING) || defined(SHADER_STAGE_COMPUTE)
    return SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, uv, 0);
#else
    return SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);
#endif
}

float3 DecodeNormalTS(float4 normalTex)
{
    return UnpackNormalScale(normalTex, _NormalScale);
}

float3 DecodeNormalWS(float4 normalTex, float3 normalWS, float3x3 tangentToWorld)
{
#if defined(_SHADINGMODE_FACE) || defined(PASS_CHARACTEROUTLINE)
    return SafeNormalize(normalWS);
#else
    return SafeNormalize(TransformTangentToWorld(DecodeNormalTS(normalTex), tangentToWorld));
#endif
}

float3 DecodeNormalWS(float4 normalTex, float3 normalWS, float4 tangentWS)
{
    normalWS = SafeNormalize(normalWS);
    float3 tangent = SafeNormalize(tangentWS.xyz);
    float3 bitangent = cross(normalWS, tangent) * tangentWS.w;
    return DecodeNormalWS(normalTex, normalWS, float3x3(tangent, bitangent, normalWS));
}

float SampleMetalMatCap(float3 normalVS)
{
    float2 uv = normalVS.xy * float2(_MTMapScale, 1.0) * 0.5 + 0.5;
    return saturate(SAMPLE_TEXTURE2D_LOD(_MTMap, sampler_LinearClamp, uv, 0).r * _MTMapMul);
}

float3 SampleMetalSpecular(float specular)
{
#if defined(_MTSPEC_RAMP)
    return SAMPLE_TEXTURE2D_LOD(_MTSpecRampTex, sampler_LinearClamp, float2(specular, 0.5), 0).rgb;
#else
    return specular * _MTSpecColor.rgb;
#endif
}

// -------------------------------------
// Shadow Ramp Sampling
float GetNightBlend()
{
    return _UseGlobalDayTime > 0.5 ? (_DAYTIME < 0.0 ? 1.0 : 0.0) : _RampDayNight;
}

float3 SampleShadowRamp(float2 rampRows, float rampX, float lit)
{
    float3 shadowColor = _ShadowColor.rgb;
#if defined(_SHADOW_RAMP)
    // Sample the day and night rows independently; blending UV.y crosses other materials.
    float x = clamp(rampX, _PackedShadowRamp_TexelSize.x * 0.5, 1.0 - _PackedShadowRamp_TexelSize.x * 0.5);
    float dayRow = rampRows.x;
    float nightRow = rampRows.y;
#if defined(_SHADINGMODE_FACE)
    // The original CharacterFace uses a single ramp row, sampled even on the lit side.
    lit *= _FaceUsePackedRamp;
#endif
    float3 day = SAMPLE_TEXTURE2D_LOD(_PackedShadowRamp, sampler_LinearClamp, float2(x, dayRow), 0).rgb;
    float3 night = SAMPLE_TEXTURE2D_LOD(_PackedShadowRamp, sampler_LinearClamp, float2(x, nightRow), 0).rgb;
    shadowColor = lerp(day, night, saturate(GetNightBlend()));
#endif
    return lerp(shadowColor, float3(1, 1, 1), lit);
}

// -------------------------------------
// Face SDF Sampling
float3 GetHeadLightDirection(float3 lightDirWS)
{
    // Same world-space axes as CharacterRenderHelper. Fall back to the object axes.
    float3 right = _FaceRightDirWS.xyz;
    float3 front = _FaceFrontDirWS.xyz;
    right = dot(right, right) > 0.001 ? SafeNormalize(right) : SafeNormalize(TransformObjectToWorldDir(float3(1, 0, 0)));
    front = dot(front, front) > 0.001 ? SafeNormalize(front) : SafeNormalize(TransformObjectToWorldDir(float3(0, 0, 1)));
    return GetHeadLightDirection(lightDirWS, right, front);
}

float SampleFaceLighting(float4 uv, float3 lightDirWS, out float noseMask)
{
    float3 headLight = GetHeadLightDirection(lightDirWS);
    float2 faceUV = lerp(uv.xy, uv.zw, saturate(_FaceUV));
    float mirror = headLight.x >= 0.0 ? 1.0 : 0.0;
    if (_FaceSDFMirror > 0.5) mirror = 1.0 - mirror;
    faceUV.x = lerp(faceUV.x, 1.0 - faceUV.x, mirror);
    float4 faceMap = SAMPLE_TEXTURE2D_LOD(_FaceLightMap, sampler_LinearClamp, faceUV, 0);
    noseMask = faceMap.g * faceMap.b * saturate(headLight.z);

#if defined(_ALLDIR_SDF)
    // Preserve the old atlas layout: X = folded azimuth, Y = elevation.
    float horizontalLength = max(length(headLight.xz), 0.0001);
    float elevation = 1.0 - acos(clamp(headLight.y, -1.0, 1.0)) * INV_PI;
    float azimuth = acos(clamp(headLight.x / horizontalLength, -1.0, 1.0)) * INV_PI;
    azimuth = saturate(1.0 - abs(2.0 * azimuth - 1.0));
    float tiles = max(round(_AllDirSDFMapTileSize), 1.0);
    float2 tilePosition = float2(azimuth, elevation) * max(tiles - 1.0, 0.0);
    float2 tile0 = floor(tilePosition);
    float2 tile1 = min(tile0 + 1.0, tiles - 1.0);
    float2 blend = frac(tilePosition);
    // CharacterFace uses UV1; the selector also accommodates atlases authored for UV0.
    float2 atlasUV = lerp(uv.xy, uv.zw, saturate(_FaceUV));
    if (headLight.x < 0.0) atlasUV.x = 1.0 - atlasUV.x;
    if (_FaceSDFMirror > 0.5) atlasUV.x = 1.0 - atlasUV.x;
    float2 inset = min(_AllDirSDFMap_TexelSize.xy * tiles * 0.5, 0.49);
    atlasUV = clamp(atlasUV, inset, 1.0 - inset);
    float lb = SAMPLE_TEXTURE2D_LOD(_AllDirSDFMap, sampler_LinearClamp, (atlasUV + tile0) / tiles, 0).r;
    float rb = SAMPLE_TEXTURE2D_LOD(_AllDirSDFMap, sampler_LinearClamp, (atlasUV + float2(tile1.x, tile0.y)) / tiles, 0).r;
    float lt = SAMPLE_TEXTURE2D_LOD(_AllDirSDFMap, sampler_LinearClamp, (atlasUV + float2(tile0.x, tile1.y)) / tiles, 0).r;
    float rt = SAMPLE_TEXTURE2D_LOD(_AllDirSDFMap, sampler_LinearClamp, (atlasUV + tile1) / tiles, 0).r;
    float sdf = lerp(lerp(lb, rb, blend.x), lerp(lt, rt, blend.x), blend.y);
    sdf = lerp(sdf, 1.0 - sdf, saturate(_FaceSDFInvert));
    return SoftStep(0.5 + _FaceShadowOffset, sdf, _FaceShadowSmooth) * smoothstep(-0.1, 0.1, headLight.z);
#else
    float sdf = _FaceSDFChannel < 0.5 ? faceMap.r : (_FaceSDFChannel < 1.5 ? faceMap.g : (_FaceSDFChannel < 2.5 ? faceMap.b : faceMap.a));
    sdf = lerp(sdf, 1.0 - sdf, saturate(_FaceSDFInvert));
    float forward = headLight.z / max(length(headLight.xz), 0.0001);
    float threshold = saturate(0.5 - forward * 0.5 + _FaceShadowOffset);
    return SoftStep(threshold, sdf, _FaceShadowSmooth) * lerp(1.0, faceMap.a, saturate(_FaceShadowMask));
#endif
}

// -------------------------------------
// Fragment Clipping
bool IsAlphaClipped(float alpha)
{
#if defined(_ALPHATEST_ON)
    return alpha < _Cutoff;
#else
    return false;
#endif
}

#if !defined(SHADER_STAGE_RAY_TRACING) && !defined(SHADER_STAGE_COMPUTE)
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/LODCrossFade.hlsl"
#endif

void ApplyAlphaClip(float alpha)
{
#if defined(_ALPHATEST_ON)
    clip(alpha - _Cutoff);
#endif
}

void ApplyLODCrossFade(float4 positionCS)
{
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(positionCS);
#endif
}
#endif

// -------------------------------------
// Fragment Local Variables
// Expand the fixed Varyings variable 'input' into locals; no intermediate input structure.
#define InitializeInputData() \
    UNITY_SETUP_INSTANCE_ID(input); \
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); \
    float4 positionCS = input.positionCS; \
    float3 positionWS = input.positionWS; \
    float3 normalWS = SafeNormalize(input.normalWS); \
    float4 tangentWS = input.tangentWS; \
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS); \
    float4 vertexColor = input.color; \
    float4 uv0 = input.uv0; \
    float4 uv1 = input.uv1; \
    float4 uv2 = input.uv2; \
    float4 uv3 = input.uv3; \
    float2 baseUV = GetBaseUV(uv0.xy)

#endif // NPR_TOON_INPUT_INCLUDED
