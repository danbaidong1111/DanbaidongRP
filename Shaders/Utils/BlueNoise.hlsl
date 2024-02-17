#ifndef UNITY_BLUENOISE_INCLUDED
#define UNITY_BLUENOISE_INCLUDED

Texture2D<float>  _STBNVec1Texture;
Texture2D<float2> _STBNVec2Texture;

float GetSpatiotemporalBlueNoiseVec1(uint2 pixelCoord)
{
    return _STBNVec1Texture[uint2(pixelCoord.x % 128, pixelCoord.y % 128)].x;
}

float2 GetSpatiotemporalBlueNoiseVec2(uint2 pixelCoord)
{
    return _STBNVec2Texture[uint2(pixelCoord.x % 128, pixelCoord.y % 128)].xy;
}


#endif //UNITY_BLUENOISE_INCLUDED