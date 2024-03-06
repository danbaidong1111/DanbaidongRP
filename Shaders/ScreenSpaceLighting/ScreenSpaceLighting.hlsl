#ifndef UNITY_SCREENSPACE_LIGHTING_INCLUDED
#define UNITY_SCREENSPACE_LIGHTING_INCLUDED

// We always need NoueNoise
#include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Utils/BlueNoise.hlsl"

TEXTURE2D_X(_CameraMotionVectorsTexture);

// Performs fading at the edge of the screen.
float EdgeOfScreenFade(float2 coordNDC, float fadeRcpLength)
{
    float2 coordCS = coordNDC * 2 - 1;
    float2 t = Remap10(abs(coordCS), fadeRcpLength, fadeRcpLength);
    return Smoothstep01(t.x) * Smoothstep01(t.y);
}

// Function that unpacks and evaluates the clear coat mask
// packedMask must be the value of GBuffer2 alpha.
// Caution: This need to be in sync with Lit.hlsl code
bool HasClearCoatMask(float4 packedMask)
{
    // We use a texture to identify if we use a clear coat constant for perceptualRoughness for SSR or use value from normal buffer.
    // When we use a forward material we can output the normal and perceptualRoughness for the coat for SSR, so we simply bind a black 1x1 texture
    // When we use deferred material we need to bind the gbuffer2 and read the coat mask
    float coatMask;
    uint materialFeatureId;
    UnpackFloatInt8bit(packedMask.a, 8, coatMask, materialFeatureId);
    return coatMask > 0.001; // If coat mask is positive, it mean we use clear coat
}


// Per-pixel camera backwards velocity
// historyUV = uv + velocity;
float2 SampleMotionVectorOffset(float2 uv, float4 screenSize)
{
    // Unity motion vectors are forward motion vectors in screen UV space
    float2 offsetUV = SAMPLE_TEXTURE2D_X_LOD(_CameraMotionVectorsTexture, sampler_LinearClamp, min(uv, 1.0f - 0.5f * screenSize.zw), 0).xy;
    return offsetUV;
}
float2 LoadMotionVectorOffset(uint2 coordSS)
{
    // Unity motion vectors are forward motion vectors in screen UV space
    float2 offsetUV = LOAD_TEXTURE2D_X(_CameraMotionVectorsTexture, coordSS).xy;
    return offsetUV;
}


float3 GetHistoryScreenPos(float2 screenUV, float deviceDepth, float reprojectDeviceDepth, float2 motionVector, float4x4 clipToPrevClipMatrix)
{
    float4 currReProjPosCS = float4(screenUV * 2.0 - 1.0, reprojectDeviceDepth, 1.0);
#if UNITY_UV_STARTS_AT_TOP
    currReProjPosCS.y = -currReProjPosCS.y;
#endif
    float4 prevReProjPosCS = mul(clipToPrevClipMatrix, currReProjPosCS); // clipToPrevClipMatrix doesn't contain AA offsets

    prevReProjPosCS *= rcp(prevReProjPosCS.w);

    float3 historyPosScreen = float3(screenUV, reprojectDeviceDepth);

    bool bIsDynamicPixel = false;
    {
        float3 velocity = currReProjPosCS.xyz - prevReProjPosCS.xyz;
    #if UNITY_UV_STARTS_AT_TOP
        velocity.y = -velocity.y;
    #endif
        velocity.xy *= 0.5;
        bIsDynamicPixel = motionVector.x > 0.0;

        // if (bIsDynamicPixel)
        // {
        //     float4 currReferencePosCS = ComputeClipSpacePosition(screenUV, deviceDepth);
        //     float4 prevReferencePosCS = mul(clipToPrevClipMatrix, currReferencePosCS);
        //     prevReferencePosCS *= rcp(prevReferencePosCS.w);

        //     velocity += float3(motionVector.xy, 0.0) - (currReferencePosCS - prevReferencePosCS);
        // }

        historyPosScreen -= velocity;
    }

    return historyPosScreen;
}

float3 GetHistoryScreenPos(float2 screenUV, float deviceDepth, float reprojectDeviceDepth, float2 motionVector, float4x4 invViewProjMatrix, float4x4 prevViewProjMatrix)
{
    float3 positionReProWS = ComputeWorldSpacePosition(screenUV, reprojectDeviceDepth, invViewProjMatrix);
    float3 prevReProNDC = ComputeNormalizedDeviceCoordinatesWithZ(positionReProWS, prevViewProjMatrix);
    float3 currReProNDC = float3(screenUV, reprojectDeviceDepth);
    
    float3 historyPosScreen = currReProNDC;

    bool bIsDynamicPixel = false;
    {
        float3 velocity = currReProNDC - prevReProNDC;
        bIsDynamicPixel = motionVector.x > 0.0;

        // if (bIsDynamicPixel)
        // {
        //     float3 positionRefWS = ComputeWorldSpacePosition(screenUV, deviceDepth, invViewProjMatrix);
        //     float3 prevRefScreen = ComputeNormalizedDeviceCoordinatesWithZ(positionRefWS, prevViewProjMatrix);
        //     float3 currRefScreen = float3(screenUV, deviceDepth);

        //     velocity += float3(motionVector.xy, 0.0) - (currRefScreen - prevRefScreen);
        // }

        historyPosScreen -= velocity;
    }

    return historyPosScreen;
}
#endif //UNITY_SCREENSPACE_LIGHTING_INCLUDED