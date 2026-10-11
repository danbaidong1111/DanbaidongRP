using System;
using UnityEditor.Rendering.Fullscreen.ShaderGraph;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    [GenerateBlocks("Fog Volume")]
    internal struct FogVolumeBlocks
    {
        public static BlockFieldDescriptor Albedo = new BlockFieldDescriptor(BlockFields.SurfaceDescription.name,
            "FogVolumeAlbedo", "Albedo", "SURFACEDESCRIPTION_FOG_ALBEDO", new ColorControl(Color.white, false), ShaderStage.Fragment);
        public static BlockFieldDescriptor Density = new BlockFieldDescriptor(BlockFields.SurfaceDescription.name,
            "FogVolumeDensity", "Density", "SURFACEDESCRIPTION_FOG_DENSITY", new FloatControl(1), ShaderStage.Fragment);
    }

    sealed class UniversalFogVolumeSubTarget : FullscreenSubTarget<UniversalTarget>
    {
        static readonly GUID kSourceCodeGuid = new GUID("a9f95b50b8f4a5d4592047edca3b449e");
        const string kRoot = "Packages/com.unity.render-pipelines.danbaidong/Editor/ShaderGraph/";
        protected override string pipelineTag => UniversalTarget.kPipelineTag;

        public UniversalFogVolumeSubTarget() => displayName = "Fog Volume";

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(kSourceCodeGuid, AssetCollection.Flags.SourceDependency);
            context.AddSubShader(GenerateSubShader());
        }

        public override SubShaderDescriptor GenerateSubShader()
        {
            var result = new SubShaderDescriptor { pipelineTag = pipelineTag, generatesPreview = true, passes = new PassCollection() };
            result.passes.Add(new PassDescriptor {
                displayName = "DanbaidongFogVolume", referenceName = "SHADERPASS_FOG_VOLUME", lightMode = "DanbaidongFogVolume", useInPreview = true,
                passTemplatePath = kRoot + "Templates/FogVolumePass.template",
                sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,
                validVertexBlocks = Array.Empty<BlockFieldDescriptor>(),
                validPixelBlocks = new[] { FogVolumeBlocks.Albedo, FogVolumeBlocks.Density },
                structs = new StructCollection { { Structs.SurfaceDescriptionInputs } },
                fieldDependencies = FieldDependencies.Default,
                requiredFields = new FieldCollection { StructFields.SurfaceDescriptionInputs.uv0 },
                renderStates = new RenderStateCollection {
                    { RenderState.Cull(Cull.Off) }, { RenderState.ZWrite(ZWrite.Off) },
                    { RenderState.ZTest(ZTest.Always) }, { RenderState.Blend("Blend Off") }
                },
                pragmas = new PragmaCollection {
                    { Pragma.Target(ShaderModel.Target45) }, { Pragma.Vertex("FogVolumeVertex") }, { Pragma.Fragment("FogVolumeFragment") }
                },
                includes = new IncludeCollection {
                    { "Packages/com.unity.render-pipelines.danbaidong/ShaderLibrary/Core.hlsl", IncludeLocation.Pregraph },
                    { "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl", IncludeLocation.Pregraph },
                    { "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl", IncludeLocation.Pregraph },
                    { "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl", IncludeLocation.Pregraph },
                    { "Packages/com.unity.shadergraph/ShaderGraphLibrary/Functions.hlsl", IncludeLocation.Pregraph },
                    { kRoot + "Includes/FogVolumeGraph.hlsl", IncludeLocation.Pregraph }
                }
            });
            return result;
        }

        public override void GetFields(ref TargetFieldContext context) => context.AddField(Fields.GraphPixel);

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(FogVolumeBlocks.Albedo);
            context.AddBlock(FogVolumeBlocks.Density);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode) { }
        public override void ProcessPreviewMaterial(Material material) { }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            context.AddHelpBox(MessageType.Info, "Albedo and Density multiply the Local Volumetric Fog component. UV0.xyz is the local 0–1 volume coordinate. Position uses the volume transform; Time also updates in Edit mode. Blending and priority are controlled by the component.");
        }

        public override bool IsNodeAllowedBySubTarget(Type nodeType)
        {
            // A volume mask is independent of the camera's opaque surface buffers.
            string name = nodeType.Name;
            if (name == "SceneDepthNode" || name == "SceneColorNode" || name == "SceneNormalNode"
                || name == "BakedGINode" || name == "MainLightDirectionNode" || name == "AmbientNode"
                || name == "CustomInterpolatorNode" || name == "CustomInterpolatorBlockNode") return false;
            return base.IsNodeAllowedBySubTarget(nodeType);
        }
    }
}
