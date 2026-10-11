namespace UnityEngine.Rendering.Universal
{
    internal sealed class FogRenderingData : ContextItem
    {
        internal AtmosphericFog settings;
        internal bool active, volumetric, gaussian;
        internal VBufferParameters buffer;
        internal ShaderVariablesAtmosphericFog constantBuffer;

        public override void Reset()
        {
            settings = null;
            buffer = default;
            active = volumetric = gaussian = false;
            constantBuffer = default;
        }
    }
}
