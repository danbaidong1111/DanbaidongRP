Shader "DanbaidongRP/NPRToon/Base"
{
    Properties
    {
        [KeywordEnum(Base, Hair, Face)]_ShadingMode ("Shading Mode", Float) = 0

        [FoldoutBegin(_FoldoutTexEnd)]_FoldoutTex("Textures", Float) = 0
            _BaseColor ("BaseColor", Color) = (1,1,1,1)
            _BaseMap ("BaseMap (RGB: Diffuse A: Emission / Clip)", 2D) = "white" {}
            [NoScaleOffset]_LightMapTex ("LightMap (R: Spec / Metal G: AO B: Metal Spec / Threshold A: Sub Material ID)", 2D) = "gray" {}
            [NoScaleOffset]_NormalMap ("NormalMap", 2D) = "bump" {}
            _NormalScale ("NormalScale", Range(0, 1)) = 1
            _LightMapAO ("LightMap Shadow Influence", Range(0, 1)) = 1
        [FoldoutEnd]_FoldoutTexEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutDirectEnd)]_FoldoutDirect("Direct Light", Float) = 0
            [HDR]_SelfLight ("SelfLight", Color) = (1,1,1,1)
            _MainLightColorLerp ("Unity Light or SelfLight", Range(0, 1)) = 0.5
            [Title(Shadow)]
            [Toggle(_SELF_SHADOW)]_UseSELF_SHADOW ("Per Object Self Shadow", Float) = 0
            _LightArea ("LightArea", Range(0, 1)) = 0.5
            _ShadowSmooth ("Shadow Edge Softness", Range(0, 0.2)) = 0.01
            _ShadowStrength ("Shadow Strength", Range(0, 1)) = 1
            _ShadowColor ("Shadow Color (Without Ramp)", Color) = (0.65,0.55,0.6,1)
            [Toggle(_SHADOW_RAMP)]_UseShadowRamp ("Use Packed Shadow Ramp", Float) = 0
            [NoScaleOffset]_PackedShadowRamp ("Packed Shadow Ramp (5 Day + 5 Night Rows)", 2D) = "white" {}
            _ShadowRampWidth ("ShadowRampWidth", Range(0, 2)) = 1
            _RampDayNight ("Day / Night Blend", Range(0, 1)) = 0
            [Toggle]_UseGlobalDayTime ("Use Global _DAYTIME (Negative = Night)", Float) = 0
        [FoldoutEnd]_FoldoutDirectEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutSpecEnd)]_FoldoutSpec("Specular - Material IDs 1 to 5", Float) = 0
            [HDR]_SpecColor ("SpecColor", Color) = (1,1,1,1)
            _SpecSoftness ("Specular Edge Softness", Range(0, 0.2)) = 0.01
            _SpecPow0 ("Material 1 SpecPow", Range(0, 100)) = 10
            _SpecMul0 ("Material 1 SpecMul", Range(0, 2)) = 1
            _SpecPow1 ("Material 2 SpecPow", Range(0, 100)) = 10
            _SpecMul1 ("Material 2 SpecMul", Range(0, 2)) = 1
            _SpecPow2 ("Material 3 SpecPow", Range(0, 100)) = 10
            _SpecMul2 ("Material 3 SpecMul", Range(0, 2)) = 1
            _SpecPow3 ("Material 4 SpecPow", Range(0, 100)) = 10
            _SpecMul3 ("Material 4 SpecMul", Range(0, 2)) = 1
            _SpecPow4 ("Material 5 SpecPow", Range(0, 100)) = 10
            _SpecMul4 ("Material 5 SpecMul", Range(0, 2)) = 1
        [FoldoutEnd]_FoldoutSpecEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutMetalEnd)]_FoldoutMetal("Metal MatCap", Float) = 0
            _MTMapLightColor ("LightColor", Color) = (1,1,1,1)
            _MTMapDarkColor ("DarkColor", Color) = (0.48,0.26,0.19,1)
            _MTMapShadowMulColor ("ShadowMulColor", Color) = (0.74,0.73,0.81,1)
            [NoScaleOffset]_MTMap ("MTMap", 2D) = "black" {}
            _MTMapScale ("MTMapScale", Float) = 1
            _MTMapMul ("MTMapMul", Float) = 3
            [HDR]_MTSpecColor ("MTSpecColor", Color) = (1,1,1,1)
            _MTSpecPow ("MTSpecPow", Range(0, 100)) = 4
            _MTSpecMul ("MTSpecMul", Range(0, 10)) = 1
            [Toggle(_MTSPEC_RAMP)]_UseMTSPEC_RAMP ("Use Metal Specular Ramp", Float) = 0
            [Ramp]_MTSpecRampTex ("MTSpecRampTex", 2D) = "white" {}
            [HDR]_MTTopLayerColor ("MTTopLayerColor", Color) = (1,1,1,1)
            _MTTopLayerRange ("MTTopLayerRange", Range(0, 1)) = 1
        [FoldoutEnd]_FoldoutMetalEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutFaceEnd)]_FoldoutFace("Face SDF", Float) = 0
            [NoScaleOffset]_FaceLightMap ("FaceLightMap", 2D) = "white" {}
            [Enum(UV0, 0, UV1, 1)]_FaceUV ("FaceLightMap UV", Float) = 1
            [Toggle]_FaceUsePackedRamp ("Use 5 Material Packed Face Ramp", Float) = 0
            _FaceRampRow ("Single Face Ramp Row", Range(0, 1)) = 0.5
            [IntRange]_FaceMaterialID ("Face Ramp / Outline Material ID", Range(1, 5)) = 1
            [Enum(R, 0, G, 1, B, 2, A, 3)]_FaceSDFChannel ("SDF Channel", Float) = 0
            [Toggle]_FaceSDFInvert ("Invert SDF", Float) = 0
            [Toggle]_FaceSDFMirror ("Flip SDF Mirror Direction", Float) = 0
            [Toggle]_FaceShadowMask ("Use FaceLightMap Alpha Shadow Mask", Float) = 0
            _FaceShadowOffset ("Face Shadow Offset", Range(-0.5, 0.5)) = 0
            _FaceShadowSmooth ("Face Shadow Softness", Range(0, 0.2)) = 0.01
            _FaceNoseSpecular ("Nose Specular (G * B)", Range(0, 2)) = 0
            [HDR]_NoseSpecColor ("Nose Specular Color", Color) = (1,1,1,1)
            [Toggle(_ALLDIR_SDF)]_ALLDIR_SDF ("Use All Direction SDF Atlas", Float) = 0
            [NoScaleOffset]_AllDirSDFMap ("AllDirSDFMap (Selected Face UV)", 2D) = "white" {}
            _AllDirSDFMapTileSize ("AllDirSDFMapTileSize", Float) = 9
            [HideInInspector]_FaceRightDirWS ("Face Right WS", Vector) = (0,0,0,0)
            [HideInInspector]_FaceFrontDirWS ("Face Forward WS", Vector) = (0,0,0,0)
        [FoldoutEnd]_FoldoutFaceEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutIndirectEnd)]_FoldoutIndirect("Indirect Light", Float) = 0
            [HDR]_SelfEnvColor ("SelfEnvColor", Color) = (0.5,0.5,0.5,1)
            _EnvColorLerp ("Unity SH or SelfEnv", Range(0, 1)) = 0.5
            _IndirDiffUpDirSH ("IndirDiffUpDirSH", Range(0, 1)) = 1
            _IndirDiffIntensity ("Environment Tint Intensity", Range(0, 2)) = 0.2
            _IndirectOcclusion ("LightMap Indirect Occlusion", Range(0, 1)) = 0.5
        [FoldoutEnd]_FoldoutIndirectEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutEmissRimEnd)]_FoldoutEmissRim("Emission, Rim", Float) = 0
            [HDR]_EmissionCol ("Emission Color (A: Albedo Blend)", Color) = (0,0,0,1)
            [HDR]_DirectRimFrontCol ("DirectRimFrontCol", Color) = (1,1,1,0.5)
            [HDR]_DirectRimBackCol ("DirectRimBackCol", Color) = (0.2,0.2,0.2,0.5)
            _DirectRimWidth ("DirectRimWidth", Range(0, 10)) = 2.5
            _PunctualRimWidth ("PunctualRimWidth", Range(0, 10)) = 2.75
        [FoldoutEnd]_FoldoutEmissRimEnd("_FoldoutEnd", Float) = 0

        [FoldoutBegin(_FoldoutOutlineEnd, PassSwitch, CharacterOutline)]_FoldoutOutline("Outline", Float) = 0
            [Enum(UV2, 0, VertexColor, 1, VertexNormal, 2)]_OutLineNormalSource ("Smooth Normal Source", Float) = 2
            _OutlineWidth ("Width", Range(0, 10)) = 1
            _OutlineClampScale ("ClampScale", Range(0.01, 5)) = 1
            [Toggle]_OutlineVertexAlpha ("Use Vertex Alpha Width", Float) = 1
            _OutlineColor0 ("Material 1 Outline Color", Color) = (0,0,0,0.8)
            _OutlineColor1 ("Material 2 Outline Color", Color) = (0,0,0,0.8)
            _OutlineColor2 ("Material 3 Outline Color", Color) = (0,0,0,0.8)
            _OutlineColor3 ("Material 4 Outline Color", Color) = (0,0,0,0.8)
            _OutlineColor4 ("Material 5 Outline Color", Color) = (0,0,0,0.8)
            [HDR]_OutlineDirectLightingColor ("DirectColor", Color) = (1,1,1,0.5)
            _OutlineDirectLightingOffset ("DirectOffset", Range(-1, 1)) = -1
            [HDR]_OutlinePunctualLightingColor ("PunctualColor", Color) = (1,1,1,0.5)
            _OutlinePunctualLightingOffset ("PunctualOffset", Range(-1, 1)) = -1
        [FoldoutEnd]_FoldoutOutlineEnd("_FoldoutEnd", Float) = 0

        [Title(OtherSettings)]
            [KeysEnum(FLAG_HAIRSHADOW, FLAG_EYELASH, FLAG_HAIRMASK)]_ToonFlagsKeywords ("ToonFlags", Float) = -1
            [Enum(UnityEngine.Rendering.CullMode)]_Cull ("Cull Mode", Float) = 2
            [Toggle(_ALPHATEST_ON)]_AlphaClip ("Alpha Clip", Float) = 0
            _Cutoff ("Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-100" "UniversalMaterialType"="Character" "IgnoreProjector"="True" }
        LOD 300

        Pass
        {
            Name "GBufferBase"
            Tags { "LightMode" = "UniversalGBuffer" }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex CommonVertex
            #pragma fragment GBufferPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ FLAG_HAIRSHADOW FLAG_EYELASH FLAG_HAIRMASK

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_GBUFFERBASE 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CharacterForward"
            Tags { "LightMode" = "CharacterForward" }

            // -------------------------------------
            // Render State Commands
            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex CommonVertex
            #pragma fragment CharacterPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALLDIR_SDF
            #pragma shader_feature_local _SELF_SHADOW
            #pragma shader_feature_local _SHADOW_RAMP
            #pragma shader_feature_local _MTSPEC_RAMP

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _GPU_LIGHTS_CLUSTER
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _PEROBJECT_SCREEN_SPACE_SHADOW
            #pragma multi_compile _ _RAYTRACING_SHADOWS

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_CHARACTERFORWARD 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CharacterOutline"
            Tags { "LightMode" = "CharacterOutline" }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            Cull Front

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex CommonVertex
            #pragma fragment CharacterPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _GPU_LIGHTS_CLUSTER
            #pragma multi_compile _ _LIGHT_LAYERS

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_CHARACTEROUTLINE 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowCasterPassVertex
            #pragma fragment ShadowCasterPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_SHADOWCASTER 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyPassVertex
            #pragma fragment DepthOnlyPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_DEPTHONLY 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsPassVertex
            #pragma fragment DepthNormalsPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            // -------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_DEPTHNORMALS 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonPass.hlsl"
            ENDHLSL
        }

    }

    SubShader
    {
        Tags { "RayTracingRenderPipeline"="DanbaidongRP" }

        Pass
        {
            Name "IndirectDXR"
            Tags { "LightMode" = "IndirectDXR" }

            // -------------------------------------
            // Render State Commands
            // Ray-tracing passes have no raster render state.

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma only_renderers d3d11 xboxseries ps5

            // -------------------------------------
            // Shader Stages
            #pragma raytracing character_shader

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SHADOW_RAMP
            #pragma shader_feature_local _MTSPEC_RAMP
            #pragma shader_feature_local _ALLDIR_SDF

            // -------------------------------------
            // Pipeline Keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_LAYERS

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_INDIRECTDXR 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonRayTracing.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VisibilityDXR"
            Tags { "LightMode" = "VisibilityDXR" }

            // -------------------------------------
            // Render State Commands
            // Ray-tracing passes have no raster render state.

            HLSLPROGRAM

            // -------------------------------------
            // Shader Target
            #pragma only_renderers d3d11 xboxseries ps5

            // -------------------------------------
            // Shader Stages
            #pragma raytracing character_shader

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _SHADINGMODE_BASE _SHADINGMODE_HAIR _SHADINGMODE_FACE
            #pragma shader_feature_local _ALPHATEST_ON

            // -------------------------------------
            // Pass Selection And Includes
            #define PASS_VISIBILITYDXR 1
            #include "Packages/com.unity.render-pipelines.danbaidong/Shaders/Material/NPRToon/Includes/NPRToonRayTracing.hlsl"
            ENDHLSL
        }

    }

    CustomEditor "UnityEditor.DanbaidongGUI.DanbaidongGUI"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
