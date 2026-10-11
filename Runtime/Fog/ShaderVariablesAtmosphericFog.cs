namespace UnityEngine.Rendering.Universal
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    internal struct ShaderVariablesAtmosphericFog
    {
        public Vector4 _FogDensity;
        public Vector4 _FogGround;
        public Vector4 _FogColor;
        public Vector4 _FogOptions;
        public Vector4 _FogSunColor;
        public Vector4 _FogSunDirection;
        public Vector4 _FogScatter;
        public Vector4 _FogVolume;
        public Vector4 _FogCameraPosition;
        public Vector4 _FogCameraForward;
        public Vector4 _FogReflectionPlane;
        public Vector4 _FogDepthEncoding;
        public Vector4 _FogDepthDecoding;
        public Vector4 _FogAdvanced;
        public Vector4 _FogSkyMip;
        public Vector4 _FogSkyTint;
        public Vector4 _FogAlbedo;
        public Vector4 _FogBufferSize;
        public float _FogFilterMode;
        public int _FogViewCount;
        public Vector2 _FogPadding;
    }
}
