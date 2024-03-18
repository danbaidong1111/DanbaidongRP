namespace UnityEngine.Rendering.Universal
{
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct ShaderVariablesScreenSpaceReflection
    {
        public Matrix4x4 _SSR_MATRIX_VP;
        public Matrix4x4 _SSR_MATRIX_I_VP;

        public Matrix4x4 _SSR_PREV_MATRIX_VP;
        public Matrix4x4 _SSR_MATRIX_CLIP_TO_PREV_CLIP;

        public Vector4 _SsrTraceScreenSize;

        public float _SsrThicknessScale;
        public float _SsrThicknessBias;
        public int _SsrIterLimit;
        public float _SsrFrameCount;

        public float _SsrRoughnessFadeEnd;
        public float _SsrRoughnessFadeRcpLength;
        public float _SsrRoughnessFadeEndTimesRcpLength;
        public float _SsrEdgeFadeRcpLength;

        public Vector4 _ColorPyramidUvScaleAndLimitPrevFrame;

        public int _SsrDepthPyramidMaxMip;
        public int _SsrColorPyramidMaxMip;
        public int _SsrReflectsSky;
        public float _SsrAccumulationAmount;

        public Vector4 _HistoryFrameRTSize;// width height 1/width 1/height

        public float _SsrPBRBias;
    }
}