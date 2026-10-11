using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    internal class UniversalRenderPipelineSerializedLight : ISerializedLight
    {
        /// <summary>The base settings of the light</summary>
        public LightEditor.Settings settings { get; }
        /// <summary>The light serialized</summary>
        public SerializedObject serializedObject { get; }
        /// <summary>The additional light data serialized</summary>
        public SerializedObject serializedAdditionalDataObject { get; private set; }

        public UniversalAdditionalLightData additionalLightData => lightsAdditionalData[0];
        public UniversalAdditionalLightData[] lightsAdditionalData { get; private set; }

        // Common SRP's Lights properties
        public SerializedProperty intensity { get; }

        // URP Light Properties
        public SerializedProperty useAdditionalDataProp { get; }                     // Does light use shadow bias settings defined in UniversalRP asset file?
        public SerializedProperty additionalLightsShadowResolutionTierProp { get; }  // Index of the AdditionalLights ShadowResolution Tier
        public SerializedProperty softShadowQualityProp { get; }                     // Per light soft shadow filtering quality.
        public SerializedProperty lightCookieSizeProp { get; }                       // Multi dimensional light cookie size replacing `cookieSize` in legacy light.
        public SerializedProperty lightCookieOffsetProp { get; }                     // Multi dimensional light cookie offset.

        // Light layers related
        public SerializedProperty renderingLayers { get; }
        public SerializedProperty customShadowLayers { get; }
        public SerializedProperty shadowRenderingLayers { get; }

        // Shape Directional
        public SerializedProperty angularDiameter { get; }

        // Shape Puntual
        public SerializedProperty shapeRadius { get; }

        // Light Contributions
        public SerializedProperty baseContributionProp { get; }
        public SerializedProperty rimContributionProp { get; }
        public SerializedProperty outlineContributionProp { get; }
        public SerializedProperty volumetricDimmer { get; }
        public SerializedProperty volumetricShadowDimmer { get; }
        public SerializedProperty volumetricLightRadius { get; }

        /// <summary>Method that updates the <see cref="SerializedObject"/> of the Light and the Additional Light Data</summary>
        public void Update()
        {
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
            settings.Update();
        }

        /// <summary>Method that applies the modified properties the <see cref="SerializedObject"/> of the Light and the Light Camera Data</summary>
        public void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
            settings.ApplyModifiedProperties();
        }

        /// <summary>Constructor</summary>
        /// <param name="serializedObject"><see cref="SerializedObject"/> with the light</param>
        /// <param name="settings"><see cref="LightEditor.Settings"/>with the settings</param>
        public UniversalRenderPipelineSerializedLight(SerializedObject serializedObject, LightEditor.Settings settings)
        {
            this.settings = settings;
            settings.OnEnable();

            this.serializedObject = serializedObject;

            lightsAdditionalData = CoreEditorUtils
                .GetAdditionalData<UniversalAdditionalLightData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(lightsAdditionalData);

            intensity = serializedObject.FindProperty("m_Intensity");

            useAdditionalDataProp = serializedAdditionalDataObject.FindProperty("m_UsePipelineSettings");
            additionalLightsShadowResolutionTierProp = serializedAdditionalDataObject.FindProperty("m_AdditionalLightsShadowResolutionTier");
            softShadowQualityProp = serializedAdditionalDataObject.FindProperty("m_SoftShadowQuality");
            lightCookieSizeProp = serializedAdditionalDataObject.FindProperty("m_LightCookieSize");
            lightCookieOffsetProp = serializedAdditionalDataObject.FindProperty("m_LightCookieOffset");

            renderingLayers = serializedAdditionalDataObject.FindProperty("m_RenderingLayers");
            customShadowLayers = serializedAdditionalDataObject.FindProperty("m_CustomShadowLayers");
            shadowRenderingLayers = serializedAdditionalDataObject.FindProperty("m_ShadowRenderingLayers");

            angularDiameter = serializedAdditionalDataObject.FindProperty("m_AngularDiameter");

            shapeRadius = serializedAdditionalDataObject.FindProperty("m_ShapeRadius");

            baseContributionProp = serializedAdditionalDataObject.FindProperty("m_BaseContribution");
            rimContributionProp = serializedAdditionalDataObject.FindProperty("m_RimContribution");
            outlineContributionProp = serializedAdditionalDataObject.FindProperty("m_OutlineContribution");
            volumetricDimmer = serializedAdditionalDataObject.FindProperty("m_VolumetricDimmer");
            volumetricShadowDimmer = serializedAdditionalDataObject.FindProperty("m_VolumetricShadowDimmer");
            volumetricLightRadius = serializedAdditionalDataObject.FindProperty("m_VolumetricLightRadius");

            settings.ApplyModifiedProperties();
        }
    }
}
