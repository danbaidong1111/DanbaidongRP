using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>Light Layers.</summary>
    [Flags]
    public enum LightLayerEnum
    {
        /// <summary>The light will no affect any object.</summary>
        Nothing = 0,   // Custom name for "Nothing" option
        /// <summary>Light Layer 0.</summary>
        LightLayerDefault = 1 << 0,
        /// <summary>Light Layer 1.</summary>
        LightLayer1 = 1 << 1,
        /// <summary>Light Layer 2.</summary>
        LightLayer2 = 1 << 2,
        /// <summary>Light Layer 3.</summary>
        LightLayer3 = 1 << 3,
        /// <summary>Light Layer 4.</summary>
        LightLayer4 = 1 << 4,
        /// <summary>Light Layer 5.</summary>
        LightLayer5 = 1 << 5,
        /// <summary>Light Layer 6.</summary>
        LightLayer6 = 1 << 6,
        /// <summary>Light Layer 7.</summary>
        LightLayer7 = 1 << 7,
        /// <summary>Everything.</summary>
        Everything = 0xFF, // Custom name for "Everything" option
    }

    /// <summary>
    /// Shape of a spot light
    /// </summary>
    public enum SpotLightShape
    {
        /// <summary>Cone shape. The default shape of the spot light.</summary>
        Cone,
        /// <summary>Pyramid shape.</summary>
        //Pyramid,
        /// <summary>Box shape. Similar to a directional light but with bounds.</summary>
        //Box
    }

    /// <summary>
    /// Contains extension methods for Light class.
    /// </summary>
    public static class LightExtensions
    {
        /// <summary>
        /// Universal Render Pipeline exposes additional light data in a separate component.
        /// This method returns the additional data component for the given light or create one if it doesn't exist yet.
        /// </summary>
        /// <param name="light"></param>
        /// <returns>The <c>UniversalAdditionalLightData</c> for this light.</returns>
        /// <see cref="UniversalAdditionalLightData"/>
        public static UniversalAdditionalLightData GetUniversalAdditionalLightData(this Light light)
        {
            var gameObject = light.gameObject;
            bool componentExists = gameObject.TryGetComponent<UniversalAdditionalLightData>(out var lightData);
            if (!componentExists)
                lightData = gameObject.AddComponent<UniversalAdditionalLightData>();

            return lightData;
        }
    }

    /// <summary>
    /// Class containing various additional light data used by URP.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [URPHelpURL("universal-additional-light-data")]
    public class UniversalAdditionalLightData : MonoBehaviour, ISerializationCallbackReceiver, IAdditionalData
    {
        // Version 0 means serialized data before the version field.
        [SerializeField] int m_Version = 3;
        internal int version
        {
            get => m_Version;
        }

        [Tooltip("Controls if light Shadow Bias parameters use pipeline settings.")]
        [SerializeField] bool m_UsePipelineSettings = true;

        /// <summary>
        /// Controls if light Shadow Bias parameters use pipeline settings or not.
        /// </summary>
        public bool usePipelineSettings
        {
            get { return m_UsePipelineSettings; }
            set { m_UsePipelineSettings = value; }
        }

        /// <summary>
        /// Value used to indicate custom shadow resolution tier for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowResolutionTierCustom = -1;

        /// <summary>
        /// Value used to indicate low shadow resolution tier for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowResolutionTierLow = 0;

        /// <summary>
        /// Value used to indicate medium shadow resolution tier for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowResolutionTierMedium = 1;

        /// <summary>
        /// Value used to indicate high shadow resolution tier for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowResolutionTierHigh = 2;

        /// <summary>
        /// The default shadow resolution tier for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowDefaultResolutionTier = AdditionalLightsShadowResolutionTierHigh;

        /// <summary>
        /// The default custom shadow resolution for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowDefaultCustomResolution = 128;

        [NonSerialized] private Light m_Light;

        /// <summary>
        /// Returns the cached light component associated with the game object that owns this light data.
        /// </summary>
#if UNITY_EDITOR
        internal new Light light
#else
        internal Light light
#endif
        {
            get
            {
                if (!m_Light)
                    TryGetComponent(out m_Light);
                return m_Light;
            }
        }

        /// <summary>
        /// The minimum shadow resolution for additional lights.
        /// </summary>
        public static readonly int AdditionalLightsShadowMinimumResolution = 128;

        [Tooltip("Controls if light shadow resolution uses pipeline settings.")]
        [SerializeField] int m_AdditionalLightsShadowResolutionTier = AdditionalLightsShadowDefaultResolutionTier;

        /// <summary>
        /// Returns the selected shadow resolution tier.
        /// </summary>
        public int additionalLightsShadowResolutionTier
        {
            get { return m_AdditionalLightsShadowResolutionTier; }
        }

        // The layer(s) this light belongs too.
        [Obsolete("This is obsolete, please use m_RenderingLayerMask instead.", false)]
        [SerializeField] LightLayerEnum m_LightLayerMask = LightLayerEnum.LightLayerDefault;


        /// <summary>
        /// The layer(s) this light belongs to.
        /// </summary>
        [Obsolete("This is obsolete, please use renderingLayerMask instead.", true)]
        public LightLayerEnum lightLayerMask
        {
            get { return m_LightLayerMask; }
            set { m_LightLayerMask = value; }
        }

        [SerializeField] uint m_RenderingLayers = 1;

        /// <summary>
        /// Specifies which rendering layers this light will affect.
        /// </summary>
        public uint renderingLayers
        {
            get
            {
                return m_RenderingLayers;
            }
            set
            {
                if (m_RenderingLayers != value)
                {
                    m_RenderingLayers = value;
                    SyncLightAndShadowLayers();
                }
            }
        }

        /// <summary>
        /// Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees).
        /// Used to render the sun/moon disk.
        /// </summary>
        [SerializeField] float m_AngularDiameter = 0.5f;
        public float angularDiameter
        {
            get => m_AngularDiameter;
            set { m_AngularDiameter = value; }
        }

        /// <summary>
        /// Only for Punctual/Sphere/Disc. Default shape radius is not 0 so that specular highlight is visible by default, it matches the previous default of 0.99 for MaxSmoothness.
        /// </summary>
        [SerializeField] float m_ShapeRadius = 0.025f;
        public float shapeRadius
        {
            get => m_ShapeRadius;
            set { m_ShapeRadius = value; }
        }

        [SerializeField, Min(0f)] float m_VolumetricDimmer = 1f;
        [SerializeField, Range(0f, 1f)] float m_VolumetricShadowDimmer = 1f;
        [SerializeField, Min(0f)] float m_VolumetricLightRadius;

        /// <summary>Multiplier for this light's scattering in volumetric fog.</summary>
        public float volumetricDimmer
        {
            get => m_VolumetricDimmer;
            set => m_VolumetricDimmer = Mathf.Max(0f, value);
        }

        /// <summary>Opacity of this light's shadows within volumetric fog.</summary>
        public float volumetricShadowDimmer
        {
            get => m_VolumetricShadowDimmer;
            set => m_VolumetricShadowDimmer = Mathf.Clamp01(value);
        }

        /// <summary>Emitter radius in metres used to stabilize scattering near punctual lights.</summary>
        public float volumetricLightRadius
        {
            get => m_VolumetricLightRadius;
            set => m_VolumetricLightRadius = Mathf.Max(0f, value);
        }

        // Light contributions
        /// <summary>
        /// Base Light contribution.
        /// </summary>
        [SerializeField] float m_BaseContribution = 1.0f;
        public float baseContribution
        {
            get => m_BaseContribution;
            set { m_BaseContribution = value; }
        }

        /// <summary>
        /// Rim Light contribution.
        /// </summary>
        [SerializeField] float m_RimContribution = 1.0f;
        public float rimContribution
        {
            get => m_RimContribution;
            set { m_RimContribution = value; }
        }

        /// <summary>
        /// Outline Light contribution.
        /// </summary>
        [SerializeField] float m_OutlineContribution = 1.0f;
        public float outlineContribution
        {
            get => m_OutlineContribution;
            set { m_OutlineContribution = value; }
        }


        [SerializeField] bool m_CustomShadowLayers = false;

        /// <summary>
        /// Indicates whether shadows need custom layers.
        /// If not, then it uses the same settings as lightLayerMask.
        /// </summary>
        public bool customShadowLayers
        {
            get
            {
                return m_CustomShadowLayers;
            }
            set
            {
                if (m_CustomShadowLayers != value)
                {
                    m_CustomShadowLayers = value;
                    SyncLightAndShadowLayers();
                }
            }
        }

        // The layer(s) used for shadow casting.
        [SerializeField] LightLayerEnum m_ShadowLayerMask = LightLayerEnum.LightLayerDefault;

        /// <summary>
        /// The layer(s) for shadow.
        /// </summary>
        [Obsolete("This is obsolete, please use shadowRenderingLayerMask instead.", true)]
        public LightLayerEnum shadowLayerMask
        {
            get { return m_ShadowLayerMask; }
            set { m_ShadowLayerMask = value; }
        }

        [SerializeField] uint m_ShadowRenderingLayers = 1;
        /// <summary>
        /// Specifies which rendering layers this light shadows will affect.
        /// </summary>
        public uint shadowRenderingLayers
        {
            get
            {
                return m_ShadowRenderingLayers;
            }
            set
            {
                if (value != m_ShadowRenderingLayers)
                {
                    m_ShadowRenderingLayers = value;
                    SyncLightAndShadowLayers();
                }
            }
        }

        /// <summary>
        /// Controls the size of the cookie mask currently assigned to the light.
        /// </summary>
        [Tooltip("Controls the size of the cookie mask currently assigned to the light.")]
        public Vector2 lightCookieSize
        {
            get => m_LightCookieSize;
            set => m_LightCookieSize = value;
        }
        [SerializeField] Vector2 m_LightCookieSize = Vector2.one;

        /// <summary>
        /// Controls the offset of the cookie mask currently assigned to the light.
        /// </summary>
        [Tooltip("Controls the offset of the cookie mask currently assigned to the light.")]
        public Vector2 lightCookieOffset
        {
            get => m_LightCookieOffset;
            set => m_LightCookieOffset = value;
        }
        [SerializeField] Vector2 m_LightCookieOffset = Vector2.zero;

        /// <summary>
        /// Light soft shadow filtering quality.
        /// </summary>
        [Tooltip("Controls the filtering quality of soft shadows. Higher quality has lower performance.")]
        public SoftShadowQuality softShadowQuality
        {
            get => m_SoftShadowQuality;
            set => m_SoftShadowQuality = value;
        }
        [SerializeField] private SoftShadowQuality m_SoftShadowQuality = SoftShadowQuality.UsePipelineSettings;

        /// <inheritdoc/>
        public void OnBeforeSerialize()
        {
        }

        /// <inheritdoc/>
        public void OnAfterDeserialize()
        {
            if (m_Version < 2)
            {
#pragma warning disable 618 // Obsolete warning
                m_RenderingLayers = (uint)m_LightLayerMask;
                m_ShadowRenderingLayers = (uint)m_ShadowLayerMask;
#pragma warning restore 618 // Obsolete warning
                m_Version = 2;
            }

            if (m_Version < 3)
            {
                // SoftShadowQuality.UsePipelineSettings added at index 0. Bump existing serialized values by 1. e.g. Low(0) -> Low(1).
                m_SoftShadowQuality = (SoftShadowQuality)(Math.Clamp((int)m_SoftShadowQuality + 1, 0, (int)SoftShadowQuality.High));
                m_Version = 3;
            }
        }

        private void SyncLightAndShadowLayers()
        {
            if (light)
                light.renderingLayerMask = m_CustomShadowLayers ? (int)m_ShadowRenderingLayers : (int)m_RenderingLayers;
        }
    }
}
