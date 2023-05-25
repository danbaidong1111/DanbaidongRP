using System;
using System.ComponentModel;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Post-processing/AtmosphericFog")]
    public sealed class AtmosphericFog : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enableAtmosphericFog = new BoolParameter(false, true);
        public BoolParameter AtmosphericFogUseController = new BoolParameter(true, false);
        public ClampedIntParameter sampleCount = new ClampedIntParameter(5, 1, 8);

        [Header("大气Mie散射")]
        public ColorParameter mieScatterColor = new ColorParameter(Color.white, true, true, true, false);
        public ColorParameter moonMieScatterColor = new ColorParameter(Color.white, true, true, true, false);
        public ClampedFloatParameter mieScatterFactor = new ClampedFloatParameter(1.8f, 0.0f, 10.0f);
        public ClampedFloatParameter mieExtinctionFactor = new ClampedFloatParameter(2.2f, 0.0f, 10.0f);
        public ClampedFloatParameter mieG = new ClampedFloatParameter(0.7f, 0.0f, 0.999f);


        [Header("大气高度雾")]
        public ColorParameter fogColor = new ColorParameter(Color.white, true, true, true, false);
        public FloatParameter fogDensity = new FloatParameter(0.3f);
        public FloatParameter heightFogEnd = new FloatParameter(-500.0f);
        public FloatParameter heightFalloff = new FloatParameter(0.18f);


        //Fixed values, defined in shader
        //public FloatParameter AtmosphereHeight = new FloatParameter(80000.0f);
        //public FloatParameter PlanetRadius = new FloatParameter(6371000.0f);
        //public Vector4Parameter DensityScale = new Vector4Parameter(new Vector4(1200.0f, 1200.0f, 0, 0));
        //public readonly Vector4 MieSct = new Vector4(2.0f, 2.0f, 2.0f, 0.0f);

        [Header("大气高度雾散射")]
        public FloatParameter inscatteringExponent = new FloatParameter(1.26f);
        public ClampedFloatParameter fogMieStrength = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Space]
        [Header("地面高度雾")]
        public FloatParameter groundFogDensity = new FloatParameter(0.0f);
        public FloatParameter groundHeightFogEnd = new FloatParameter(0.0f);
        public FloatParameter groundHeightFalloff = new FloatParameter(0.28f);
        public FloatParameter groundFogHeightLimit = new FloatParameter(220.0f);
        public FloatParameter groundFogDistanceLimit = new FloatParameter(200.0f);
        public FloatParameter groundFogDistanceFalloff = new FloatParameter(800.0f);

        [Space]
        [Header("HeightMap")]
        public FloatParameter heightScale = new FloatParameter(100.0f);
        public Vector4Parameter heightMapST = new Vector4Parameter(new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        public TextureParameter heightMap2D = new TextureParameter(null);
        public Vector4Parameter heightMapNoiseST = new Vector4Parameter(new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        public TextureParameter heightMapNoise = new TextureParameter(null);


        [Header("径向模糊体积光束")]
        public BoolParameter enableLightShaft = new BoolParameter(false, false);
        public ClampedFloatParameter lightShaftMieG = new ClampedFloatParameter(0.88f, 0.0f, 1.0f);
        public ClampedFloatParameter lightShaftBlurDistance = new ClampedFloatParameter(12.0f, 0.0f, 20.0f);
        public ClampedFloatParameter lightShaftIntensity = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        public ClampedFloatParameter lightShaftRevertScale = new ClampedFloatParameter(0.0f, -2.0f, 2.0f);

        public bool IsActive() => enableAtmosphericFog.value;

        public bool IsTileCompatible() => false;
    }
}
