#ifndef DANBAIDONG_DECLARE_GLOBAL_ILLUMINATION_TEXTURE_INCLUDED
#define DANBAIDONG_DECLARE_GLOBAL_ILLUMINATION_TEXTURE_INCLUDED

TEXTURE2D_X(_GlobalIlluminationTexture);

// RGB is diffuse incident irradiance divided by PI; A is estimate validity.
float4 LoadSceneGlobalIlluminationAndValidity(uint2 pixelCoord)
{
    return LOAD_TEXTURE2D_X(_GlobalIlluminationTexture, pixelCoord);
}

float3 LoadSceneGlobalIllumination(uint2 pixelCoord)
{
    return LoadSceneGlobalIlluminationAndValidity(pixelCoord).rgb;
}

#endif
