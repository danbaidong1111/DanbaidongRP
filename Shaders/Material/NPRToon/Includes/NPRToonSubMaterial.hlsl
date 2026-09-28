#ifndef NPR_TOON_SUB_MATERIAL_INCLUDED
#define NPR_TOON_SUB_MATERIAL_INCLUDED

// -------------------------------------
// Includes
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonInput.hlsl"

// -------------------------------------
// Sub-Material Data
// Resolved once per pixel, before any light loop. No five-way selection in shading.
struct SubMaterialData
{
    uint materialID;
    float specPow;
    float specMul;
    float3 specColor;
    float4 outlineColor;
    float dayRampRow;
    float nightRampRow;
};

// -------------------------------------
// Material ID And Parameter Selection
// LightMap A encodes IDs in the authored order 1, 4, 3, 5, 2.
uint UnpackSubMaterialID(float alpha)
{
    return alpha >= 0.8 ? 2u : (alpha >= 0.6 ? 5u : (alpha >= 0.4 ? 3u : (alpha >= 0.2 ? 4u : 1u)));
}

float GetRampRow(uint materialID, float night)
{
    return 1.05 - 0.1 * clamp(materialID, 1u, 5u) - 0.5 * saturate(night);
}

float SelectMaterialValue(uint id, float value0, float value1, float value2, float value3, float value4)
{
    return id == 1u ? value0 : (id == 2u ? value1 : (id == 3u ? value2 : (id == 4u ? value3 : value4)));
}

float4 SelectMaterialColor(uint id, float4 value0, float4 value1, float4 value2, float4 value3, float4 value4)
{
    return id == 1u ? value0 : (id == 2u ? value1 : (id == 3u ? value2 : (id == 4u ? value3 : value4)));
}

SubMaterialData GetSubMaterialData(uint materialID)
{
    SubMaterialData data;
    data.materialID = clamp(materialID, 1u, 5u);
    data.specPow = SelectMaterialValue(data.materialID, _SpecPow0, _SpecPow1, _SpecPow2, _SpecPow3, _SpecPow4);
    data.specMul = SelectMaterialValue(data.materialID, _SpecMul0, _SpecMul1, _SpecMul2, _SpecMul3, _SpecMul4);
    data.specColor = _SpecColor.rgb;
    data.outlineColor = SelectMaterialColor(data.materialID, _OutlineColor0, _OutlineColor1, _OutlineColor2, _OutlineColor3, _OutlineColor4);
    data.dayRampRow = GetRampRow(data.materialID, 0.0);
    data.nightRampRow = GetRampRow(data.materialID, 1.0);
#if defined(_SHADINGMODE_FACE)
    data.dayRampRow = lerp(_FaceRampRow, data.dayRampRow, _FaceUsePackedRamp);
    data.nightRampRow = lerp(_FaceRampRow, data.nightRampRow, _FaceUsePackedRamp);
#endif
    return data;
}

SubMaterialData DecodeSubMaterial(float lightMapAlpha)
{
#if defined(_SHADINGMODE_FACE)
    uint materialID = (uint)clamp(round(_FaceMaterialID), 1.0, 5.0);
#else
    uint materialID = UnpackSubMaterialID(lightMapAlpha);
#endif
    return GetSubMaterialData(materialID);
}

#endif // NPR_TOON_SUB_MATERIAL_INCLUDED
