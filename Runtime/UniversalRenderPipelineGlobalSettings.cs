using System;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Universal Render Pipeline's Global Settings.
    /// Global settings are unique per Render Pipeline type. In URP, Global Settings contain:
    /// - light layer names
    /// - Runtime shaders
    /// </summary>
    [URPHelpURL("urp-global-settings")]
    partial class UniversalRenderPipelineGlobalSettings : RenderPipelineGlobalSettings, ISerializationCallbackReceiver
    {
        #region Version system

#pragma warning disable CS0414
        [SerializeField] int k_AssetVersion = 3;
#pragma warning restore CS0414

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
#if UNITY_EDITOR
            if (k_AssetVersion != 3)
            {
                EditorApplication.delayCall += () => UpgradeAsset(this.GetInstanceID());
            }
#endif
        }

#if UNITY_EDITOR
        static void UpgradeAsset(int assetInstanceID)
        {
            UniversalRenderPipelineGlobalSettings asset = EditorUtility.InstanceIDToObject(assetInstanceID) as UniversalRenderPipelineGlobalSettings;

            if (asset.k_AssetVersion < 2)
            {
#pragma warning disable 618 // Obsolete warning
                // Renamed supportRuntimeDebugDisplay => stripDebugVariants, which results in inverted logic
                asset.m_StripDebugVariants = !asset.supportRuntimeDebugDisplay;
                asset.k_AssetVersion = 2;
#pragma warning restore 618 // Obsolete warning

                // For old test projects lets keep post processing stripping enabled, as huge chance they did not used runtime profile creating
#if UNITY_INCLUDE_TESTS
                asset.m_StripUnusedPostProcessingVariants = true;
#endif
            }

            if (asset.k_AssetVersion < 3)
            {
                int index = 0;
                asset.m_RenderingLayerNames = new string[8];
#pragma warning disable 618 // Obsolete warning
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName0;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName1;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName2;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName3;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName4;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName5;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName6;
                asset.m_RenderingLayerNames[index++] = asset.lightLayerName7;
#pragma warning restore 618 // Obsolete warning
                asset.k_AssetVersion = 3;
                asset.UpdateRenderingLayerNames();
            }

            EditorUtility.SetDirty(asset);
        }

#endif
        #endregion

        private static UniversalRenderPipelineGlobalSettings cachedInstance = null;
        /// <summary>
        /// Active URP Global Settings asset. If the value is null then no UniversalRenderPipelineGlobalSettings has been registered to the Graphics Settings with the UniversalRenderPipeline.
        /// </summary>
        public static UniversalRenderPipelineGlobalSettings instance
        {
            get
            {
#if !UNITY_EDITOR
                // The URP Global Settings could have been changed by script, undo/redo (case 1342987), or file update - file versioning, let us make sure we display the correct one
                // In a Player, we do not need to worry about those changes as we only support loading one
                if (cachedInstance == null)
#endif
                    cachedInstance = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>() as UniversalRenderPipelineGlobalSettings;
                return cachedInstance;
            }
        }

        static internal void UpdateGraphicsSettings(UniversalRenderPipelineGlobalSettings newSettings)
        {
            if (newSettings == cachedInstance)
                return;
            if (newSettings != null)
                GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(newSettings as RenderPipelineGlobalSettings);
            else
                GraphicsSettings.UnregisterRenderPipelineSettings<UniversalRenderPipeline>();
            cachedInstance = newSettings;
        }

        /// <summary>Default name when creating an URP Global Settings asset.</summary>
        public static readonly string defaultAssetName = "UniversalRenderPipelineGlobalSettings";

#if UNITY_EDITOR
        //Making sure there is at least one UniversalRenderPipelineGlobalSettings instance in the project
        internal static UniversalRenderPipelineGlobalSettings Ensure(string folderPath = "", bool canCreateNewAsset = true)
        {
            if (UniversalRenderPipelineGlobalSettings.instance)
                return UniversalRenderPipelineGlobalSettings.instance;

            UniversalRenderPipelineGlobalSettings assetCreated = null;
            string path = $"Assets/{folderPath}/{defaultAssetName}.asset";
            assetCreated = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineGlobalSettings>(path);
            if (assetCreated == null)
            {
                var guidGlobalSettingsAssets = AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings");
                //If we could not find the asset at the default path, find the first one
                if (guidGlobalSettingsAssets.Length > 0)
                {
                    var curGUID = guidGlobalSettingsAssets[0];
                    path = AssetDatabase.GUIDToAssetPath(curGUID);
                    assetCreated = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineGlobalSettings>(path);
                }
                else if (canCreateNewAsset)// or create one altogether
                {
                    if (!AssetDatabase.IsValidFolder("Assets/" + folderPath))
                        AssetDatabase.CreateFolder("Assets", folderPath);
                    assetCreated = Create(path);

                    // TODO: Reenable after next urp template is published
                    //Debug.LogWarning("No URP Global Settings Asset is assigned. One will be created for you. If you want to modify it, go to Project Settings > Graphics > URP Settings.");
                }
                else
                {
                    Debug.LogError("If you are building a Player, make sure to save an URP Global Settings asset by opening the project in the Editor first.");
                    return null;
                }
            }
            Debug.Assert(assetCreated, "Could not create URP's Global Settings - URP may not work correctly - Open  Project Settings > Graphics > URP Settings for additional help.");
            UpdateGraphicsSettings(assetCreated);
            return UniversalRenderPipelineGlobalSettings.instance;
        }

        internal static UniversalRenderPipelineGlobalSettings Create(string path, UniversalRenderPipelineGlobalSettings src = null)
        {
            UniversalRenderPipelineGlobalSettings assetCreated = null;

            // make sure the asset does not already exists
            assetCreated = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineGlobalSettings>(path);
            if (assetCreated == null)
            {
                assetCreated = ScriptableObject.CreateInstance<UniversalRenderPipelineGlobalSettings>();
                if (assetCreated != null)
                {
                    assetCreated.name = System.IO.Path.GetFileName(path);
                }
                AssetDatabase.CreateAsset(assetCreated, path);
                Debug.Assert(assetCreated);
            }

            if (assetCreated)
            {
                if (src != null)
                {
                    System.Array.Copy(src.m_RenderingLayerNames, assetCreated.m_RenderingLayerNames, src.m_RenderingLayerNames.Length);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // ensure resources are here
            assetCreated.EnsureRuntimeResources(forceReload: true);

            return assetCreated;
        }

#endif

        void Reset()
        {
            UpdateRenderingLayerNames();
        }

        [SerializeField]
        string[] m_RenderingLayerNames = new string[] { "Default" };
        string[] renderingLayerNames
        {
            get
            {
                if (m_RenderingLayerNames == null)
                    UpdateRenderingLayerNames();
                return m_RenderingLayerNames;
            }
        }
        [System.NonSerialized]
        string[] m_PrefixedRenderingLayerNames;
        string[] prefixedRenderingLayerNames
        {
            get
            {
                if (m_PrefixedRenderingLayerNames == null)
                    UpdateRenderingLayerNames();
                return m_PrefixedRenderingLayerNames;
            }
        }
        /// <summary>Names used for display of rendering layer masks.</summary>
        public string[] renderingLayerMaskNames => renderingLayerNames;
        /// <summary>Names used for display of rendering layer masks with a prefix.</summary>
        public string[] prefixedRenderingLayerMaskNames => prefixedRenderingLayerNames;

        [SerializeField]
        uint m_ValidRenderingLayers;
        /// <summary>Valid rendering layers that can be used by graphics. </summary>
        public uint validRenderingLayers => m_ValidRenderingLayers;

        /// <summary>Regenerate Rendering Layer names and their prefixed versions.</summary>
        internal void UpdateRenderingLayerNames()
        {
            // Update prefixed
            if (m_PrefixedRenderingLayerNames == null)
                m_PrefixedRenderingLayerNames = new string[32];
            for (int i = 0; i < m_PrefixedRenderingLayerNames.Length; ++i)
            {
                uint renderingLayer = (uint)(1 << i);

                m_ValidRenderingLayers = i < m_RenderingLayerNames.Length ? (m_ValidRenderingLayers | renderingLayer) : (m_ValidRenderingLayers & ~renderingLayer);
                m_PrefixedRenderingLayerNames[i] = i < m_RenderingLayerNames.Length ? m_RenderingLayerNames[i] : $"Unused Layer {i}";
            }

            // Update decals
            DecalProjector.UpdateAllDecalProperties();
        }

        /// <summary>
        /// Names used for display of light layers with Layer's index as prefix.
        /// For example: "0: Light Layer Default"
        /// </summary>
        [Obsolete("This is obsolete, please use prefixedRenderingLayerMaskNames instead.", false)]
        public string[] prefixedLightLayerNames => new string[0];


        #region Light Layer Names [3D]

        /// <summary>Name for light layer 0.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName0;
        /// <summary>Name for light layer 1.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName1;
        /// <summary>Name for light layer 2.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName2;
        /// <summary>Name for light layer 3.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName3;
        /// <summary>Name for light layer 4.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName4;
        /// <summary>Name for light layer 5.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName5;
        /// <summary>Name for light layer 6.</summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string lightLayerName6;
        /// <summary>Name for light layer 7.</summary>
        [Obsolete("This is obsolete, please use renderingLayerNames instead.", false)]
        public string lightLayerName7;

        /// <summary>
        /// Names used for display of light layers.
        /// </summary>
        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead.", false)]
        public string[] lightLayerNames => new string[0];

        internal void ResetRenderingLayerNames()
        {
            m_RenderingLayerNames = new string[] { "Default"};
        }

        #endregion

        #region Resource Common
#if UNITY_EDITOR
        // Yes it is stupid to retry right away but making it called in EditorApplication.delayCall
        // from EnsureResources create GC
        void DelayedNullReload<T>(string resourcePath)
            where T : UniversalRenderPipelineResources
        {
            T resourcesDelayed = AssetDatabase.LoadAssetAtPath<T>(resourcePath);
            if (resourcesDelayed == null)
                EditorApplication.delayCall += () => DelayedNullReload<T>(resourcePath);
            else
                ResourceReloader.ReloadAllNullIn(resourcesDelayed, URPUtils.GetURPRenderPipelinePath());
        }
        void EnsureResources<T>(bool forceReload, ref T resources, string resourcePath, Func<UniversalRenderPipelineGlobalSettings, bool> checker)
            where T : UniversalRenderPipelineResources
        {
            T resourceChecked = null;

            if (checker(this))
            {
                if (!EditorUtility.IsPersistent(resources)) // if not loaded from the Asset database
                {
                    // try to load from AssetDatabase if it is ready
                    resourceChecked = AssetDatabase.LoadAssetAtPath<T>(resourcePath);
                    if (resourceChecked && !resourceChecked.Equals(null))
                        resources = resourceChecked;
                }
                if (forceReload)
                    ResourceReloader.ReloadAllNullIn(resources, URPUtils.GetURPRenderPipelinePath());
                return;
            }

            resourceChecked = AssetDatabase.LoadAssetAtPath<T>(resourcePath);
            if (resourceChecked != null && !resourceChecked.Equals(null))
            {
                resources = resourceChecked;
                if (forceReload)
                    ResourceReloader.ReloadAllNullIn(resources, URPUtils.GetURPRenderPipelinePath());
            }
            else
            {
                // Asset database may not be ready
                var objs = InternalEditorUtility.LoadSerializedFileAndForget(resourcePath);
                resources = (objs != null && objs.Length > 0) ? objs[0] as T : null;
                if (forceReload)
                {
                    try
                    {
                        if (ResourceReloader.ReloadAllNullIn(resources, URPUtils.GetURPRenderPipelinePath()))
                        {
                            InternalEditorUtility.SaveToSerializedFileAndForget(
                                new Object[] { resources },
                                resourcePath,
                                true);
                        }
                    }
                    catch (System.Exception e)
                    {
                        // This can be called at a time where AssetDatabase is not available for loading.
                        // When this happens, the GUID can be get but the resource loaded will be null.
                        // Using the ResourceReloader mechanism in CoreRP, it checks this and add InvalidImport data when this occurs.
                        if (!(e.Data.Contains("InvalidImport") && e.Data["InvalidImport"] is int dii && dii == 1))
                            Debug.LogException(e);
                        else
                            DelayedNullReload<T>(resourcePath);
                    }
                }
            }
            Debug.Assert(checker(this), $"Could not load {typeof(T).Name}.");
        }
#endif

        #endregion //Resource Common

        #region Runtime Resources
        [SerializeField]
        UniversalRenderPipelineRuntimeResources m_RenderPipelineRuntimeResources;

        internal UniversalRenderPipelineRuntimeResources renderPipelineRuntimeResources
        {
            get
            {
#if UNITY_EDITOR
                EnsureRuntimeResources(forceReload: false);
#endif
                return m_RenderPipelineRuntimeResources;
            }
        }

#if UNITY_EDITOR
        // be sure to cach result for not using GC in a frame after first one.
        static readonly string runtimeResourcesPath = URPUtils.GetURPRenderPipelinePath() + "Runtime/Data/UniversalRenderPipelineRuntimeResources.asset";

        internal void EnsureRuntimeResources(bool forceReload)
            => EnsureResources(forceReload, ref m_RenderPipelineRuntimeResources, runtimeResourcesPath, AreRuntimeResourcesCreated_Internal);

        // Passing method in a Func argument create a functor that create GC
        // If it is static it is then only computed once but the Ensure is called after first frame which will make our GC check fail
        // So create it once and store it here.
        // Expected usage: UniversalRenderPipelineGlobalSettings.AreRuntimeResourcesCreated(anyUniversalRenderPipelineRenderPipelineGlobalSettings) that will return a bool
        static Func<UniversalRenderPipelineGlobalSettings, bool> AreRuntimeResourcesCreated_Internal = global
            => global.m_RenderPipelineRuntimeResources != null && !global.m_RenderPipelineRuntimeResources.Equals(null);

        internal bool AreRuntimeResourcesCreated() => AreRuntimeResourcesCreated_Internal(this);

        internal void EnsureShadersCompiled()
        {
            // We iterate over all compute shader to verify if they are all compiled, if it's not the case
            // then we throw an exception to avoid allocating resources and crashing later on by using a null
            // compute kernel.
            foreach (var computeShader in m_RenderPipelineRuntimeResources.shaders.GetAllComputeShaders())
            {
                foreach (var message in UnityEditor.ShaderUtil.GetComputeShaderMessages(computeShader))
                {
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        // Will be catched by the try in HDRenderPipelineAsset.CreatePipeline()
                        throw new System.Exception(System.String.Format(
                            "Compute Shader compilation error on platform {0} in file {1}:{2}: {3}{4}\n" +
                            "HDRP will not run until the error is fixed.\n",
                            message.platform, message.file, message.line, message.message, message.messageDetails
                        ));
                    }
                }
            }
        }

#endif //UNITY_EDITOR

        #endregion // Runtime Resources

        #region Misc Settings

        [SerializeField] bool m_StripDebugVariants = true;

        [SerializeField] bool m_StripUnusedPostProcessingVariants = false;

        [SerializeField] bool m_StripUnusedVariants = true;

        [SerializeField] bool m_StripUnusedLODCrossFadeVariants = true;

        [SerializeField] bool m_StripScreenCoordOverrideVariants = true;

        /// <summary>
        /// Controls whether debug display shaders for Rendering Debugger are available in Player builds.
        /// </summary>
        [Obsolete("Please use stripRuntimeDebugShaders instead.", false)]
        public bool supportRuntimeDebugDisplay = false;

        /// <summary>
        /// Controls whether debug display shaders for Rendering Debugger are available in Player builds.
        /// </summary>
        public bool stripDebugVariants { get => m_StripDebugVariants; set { m_StripDebugVariants = value; } }

        /// <summary>
        /// Controls whether strips automatically post processing shader variants based on <see cref="VolumeProfile"/> components.
        /// It strips based on VolumeProfiles in project and not scenes that actually uses it.
        /// </summary>
        public bool stripUnusedPostProcessingVariants { get => m_StripUnusedPostProcessingVariants; set { m_StripUnusedPostProcessingVariants = value; } }

        /// <summary>
        /// Controls whether strip off variants if the feature is enabled.
        /// </summary>
        public bool stripUnusedVariants { get => m_StripUnusedVariants; set { m_StripUnusedVariants = value; } }

        /// <summary>
        /// If this property is true, Unity strips the LOD variants if the LOD cross-fade feature (UniversalRenderingPipelineAsset.enableLODCrossFade) is disabled.
        /// </summary>
        [Obsolete("No longer used as Shader Prefiltering automatically strips out unused LOD Crossfade variants. Please use the LOD Crossfade setting in the URP Asset to disable the feature if not used.", false)]
        public bool stripUnusedLODCrossFadeVariants { get => m_StripUnusedLODCrossFadeVariants; set { m_StripUnusedLODCrossFadeVariants = value; } }

        /// <summary>
        /// Controls whether Screen Coordinates Override shader variants are automatically stripped.
        /// </summary>
        public bool stripScreenCoordOverrideVariants { get => m_StripScreenCoordOverrideVariants; set => m_StripScreenCoordOverrideVariants = value; }

        #endregion
    }
}
