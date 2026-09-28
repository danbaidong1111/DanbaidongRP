#ifndef UNIVERSAL_RESTIR_GI_SCREEN_SPACE_INCLUDED
#define UNIVERSAL_RESTIR_GI_SCREEN_SPACE_INCLUDED

#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/ScreenSpaceLighting/ReSTIRGIDirections.hlsl"
#include "Packages/com.unity.render-pipelines.danbaidong/Runtime/Raytracing/RayTracingFallbackHierarchy.cs.hlsl"

TEXTURE2D(_CameraDepthPyramidTexture);
TEXTURE2D_X(_ColorPyramidTexture);
TEXTURECUBE(_SSGISkyTexture);
StructuredBuffer<int2> _DepthPyramidMipLevelOffsets;
float _SSGIRayLength;
float _SSGIClampValue;
int _SSGIColorHistoryValid;

float ReSTIRGIReverseDepth(float depth)
{
#if UNITY_REVERSED_Z
    return depth;
#else
    return 1.0 - depth;
#endif
}

// Adapted from HDRP Runtime/Lighting/ScreenSpaceLighting/RayMarching.hlsl:
// traverse the packed hierarchical depth, descend on a potential intersection,
// use thickness to distinguish a hit from a ray already behind the depth layer.
bool TraceReSTIRGIScreenRay(float3 originWS, float3 directionWS, uint2 sourceCoord, out uint2 hitCoord)
{
    hitCoord = 0u;
    float3 startNDC = ComputeNormalizedDeviceCoordinatesWithZ(originWS, UNITY_MATRIX_VP);
    float3 endNDC = ComputeNormalizedDeviceCoordinatesWithZ(originWS + directionWS, UNITY_MATRIX_VP);
    float3 origin = float3((float2)sourceCoord + 0.5, ReSTIRGIReverseDepth(startNDC.z));
    float3 direction = float3(endNDC.xy * _SSGISourceSize.xy, ReSTIRGIReverseDepth(endNDC.z)) - origin;
    if (origin.z <= 0.0 || origin.z >= 1.0 || ReSTIRGIReverseDepth(endNDC.z) <= 0.0)
        return false;
    // Finite reciprocals for axis-aligned directions (avoid 0 * infinity).
    float3 raySign = float3(direction.x >= 0.0 ? 1.0 : -1.0,
        direction.y >= 0.0 ? 1.0 : -1.0, direction.z >= 0.0 ? 1.0 : -1.0);
    float3 inverseDirection = raySign / max(abs(direction), 1e-8);
    int2 rayStep = int2(direction.x >= 0.0, direction.y >= 0.0);
    float3 limits = float3(direction.x >= 0.0 ? _SSGISourceSize.x - 0.5 : 0.5,
        direction.y >= 0.0 ? _SSGISourceSize.y - 0.5 : 0.5,
        direction.z >= 0.0 ? 1.0 : 0.00000024);
    float3 endDistance = (limits - origin) * inverseDirection;
    float tMax = Min3(endDistance.x, endDistance.y, endDistance.z);
    float t = min(abs(0.5 * inverseDirection.x), abs(0.5 * inverseDirection.y));
    int mip = 0;
    bool belowMip0 = false;
    const float edgeEpsilon = 0.00024414;
    UNITY_LOOP
    for (int step = 0; step < _RayMarchingSteps && t <= tMax; ++step)
    {
        float3 position = origin + t * direction;
        position.xy += raySign.xy * clamp(raySign.xy * (round(position.xy) - position.xy) + edgeEpsilon, 0.0, edgeEpsilon);
        if (any(position.xy < 0.0) || any(position.xy >= _SSGISourceSize.xy))
            break;
        int2 mipCoord = (int2)position.xy >> mip;
        float depth = ReSTIRGIReverseDepth(LOAD_TEXTURE2D(_CameraDepthPyramidTexture,
            _DepthPyramidMipLevelOffsets[mip] + mipCoord).r);
        float2 walls = (mipCoord + rayStep) << mip;
        float2 wallDistances = (walls - origin.xy) * inverseDirection.xy;
        float distWall = min(wallDistances.x, wallDistances.y);
        float distFloor = (depth - origin.z) * inverseDirection.z;
        bool belowFloor = position.z < depth;
        bool insideFloor = belowFloor && position.z >= depth * _RayMarchingThicknessScale + _RayMarchingThicknessBias;
        bool hitFloor = t <= distFloor && distFloor <= distWall;
        if (belowMip0 && insideFloor)
            return false;
        if (mip == 0 && (hitFloor || insideFloor))
        {
            hitCoord = (uint2)position.xy;
            return depth > 0.0 && any(hitCoord != sourceCoord);
        }
        belowMip0 = mip == 0 && belowFloor;
        t = hitFloor ? distFloor : (mip != 0 && belowFloor ? t : distWall);
        mip = clamp(mip + ((hitFloor || belowFloor || direction.z >= 0.0) ? -1 : 1),
            0, min(_SSGIDepthPyramidMaxMip, 6));
    }
    return false;
}

bool LoadReSTIRGIScreenRadiance(uint2 hitCoord, float hitDepth, out float3 radiance)
{
    radiance = 0.0;
    if (_SSGIColorHistoryValid == 0)
        return false;
    float2 hitUV = TransformCoordSSToScreenUV(hitCoord, _SSGISourceSize);
    float2 previousUV = hitUV - LoadMotionVectorOffset(hitCoord);
    if (any(previousUV <= 0.0) || any(previousUV >= 1.0))
        return false;
    uint2 previousCoord = min((uint2)(previousUV * _SSGISourceSize.xy), (uint2)_SSGISourceSize.xy - 1u);
    float previousDepth = LOAD_TEXTURE2D_X(_PrevCameraDepthTexture, previousCoord).r;
    if (previousDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return false;
    float4 previousClip = mul(_ClipToPrevClipMatrix, ComputeClipSpacePosition(hitUV, hitDepth));
    if (previousClip.w <= 0.0)
        return false;
    float projectedDepth = previousClip.z / previousClip.w;
    if (projectedDepth <= 0.0 || projectedDepth >= 1.0)
        return false;
    float expectedDepth = LinearEyeDepth(projectedDepth, _ZBufferParams);
    if (abs(LinearEyeDepth(previousDepth, _ZBufferParams) - expectedDepth)
        > max(0.02 * expectedDepth, 0.01))
        return false;
    float2 colorUV = previousUV * _ColorPyramidUvScaleAndLimitPrevFrame.xy;
    colorUV = min(colorUV, _ColorPyramidUvScaleAndLimitPrevFrame.zw);
    // HDRP SSGI uses mip 1 to reduce input aliasing; history is pre-postprocess,
    // scene-linear color in this pipeline, so no exposure multiplier is needed.
    radiance = SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, sampler_LinearClamp,
        colorUV, min(1, _SSGIColorPyramidMaxMip)).rgb;
    return true;
}

[numthreads(8, 8, 1)]
void ReSTIRGIInitialScreenSpace(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 coord = dispatchThreadID.xy;
    if (!IsValidPixel(coord))
        return;
    if (_SSGIColorHistoryValid == 0)
    {
        StoreOutputReservoir(coord, CreateEmptyGIReservoir());
        return;
    }
    ReSTIRGISurface surface = LoadSurface(coord);
    if (!IsValidSurface(surface))
    {
        StoreOutputReservoir(coord, CreateEmptyGIReservoir());
        return;
    }
    uint2 sourceCoord = GetReSTIRGISourceCoord(coord);
    float3 view = GetWorldSpaceNormalizeViewDir(surface.positionWS);
    float3 cameraPosition = GetCurrentViewPosition();
    float3 origin = cameraPosition + (surface.positionWS - cameraPosition)
        * (1.0 - 0.001 / max(dot(surface.normalWS, view), 0.1));
    float3x3 frame = GetLocalFrame(surface.normalWS);
    uint random = InitReSTIRGIRandom(sourceCoord, (uint)_SSGIFrameIndex, RESTIR_GI_RANDOM_INITIAL);
    GIReservoir selected = CreateEmptyGIReservoir();
    float weightSum = 0.0;
    uint candidateCount = clamp((uint)_SSGICandidateCount, 1u, 8u);
    UNITY_LOOP
    for (uint i = 0u; i < candidateCount; ++i)
    {
        float3 direction = normalize(mul(SampleReSTIRGICosineDirection(sourceCoord, (uint)_SSGIFrameIndex, i), frame));
        float pdf = saturate(dot(surface.normalWS, direction)) / PI;
        if (pdf <= 1e-6)
            continue;
        GIReservoir candidate = CreateEmptyGIReservoir();
        candidate.M = 1;
        candidate.position = surface.positionWS + direction * _SSGIRayLength;
        candidate.normal = -direction;
        uint2 hitCoord;
        bool hit = TraceReSTIRGIScreenRay(origin, direction, sourceCoord, hitCoord);
        bool validRadiance = false;
        if (hit)
        {
            float depth = LoadSceneDepth(hitCoord);
            ReSTIRGISurface hitSurface = LoadSurfaceFromSourceCoord(hitCoord, depth);
            hit = dot(hitSurface.positionWS - surface.positionWS, hitSurface.positionWS - surface.positionWS)
                <= _SSGIRayLength * _SSGIRayLength;
            if (hit)
            {
                candidate.position = hitSurface.positionWS;
                candidate.normal = hitSurface.normalWS;
                validRadiance = LoadReSTIRGIScreenRadiance(hitCoord, depth, candidate.radiance);
                // A back-facing depth hit is an occluder, not a sky miss.
                if (dot(hitSurface.normalWS, -direction) <= 0.0)
                {
                    candidate.radiance = 0.0;
                    validRadiance = true;
                }
            }
        }
        if (!validRadiance && (_RayMarchingFallbackHierarchy & RAYTRACINGFALLBACKHIERACHY_SKY) != 0)
        {
            candidate.position = surface.positionWS + direction * _SSGIRayLength;
            candidate.normal = -direction;
            candidate.radiance = SAMPLE_TEXTURECUBE_LOD(_SSGISkyTexture, sampler_LinearClamp, direction, 0).rgb;
        }
        candidate.radiance = clamp(candidate.radiance, 0.0, _SSGIClampValue);
        float target = evalTargetFunction(candidate, surface);
        updateReservoir(target / pdf, candidate, NextReSTIRGIRandom(random), weightSum, selected);
    }
    FinalizeResampledReservoir(selected, weightSum, surface);
    StoreOutputReservoir(coord, selected);
}

#endif
