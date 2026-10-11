using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldPass : IDisposable
    {
        internal static readonly string[] ShaderNames =
        {
            "DepthOfFieldKernel",
            "DepthOfFieldCoC",
            "DepthOfFieldCoCReproject",
            "DepthOfFieldPrefilter",
            "DepthOfFieldMip",
            "DepthOfFieldCoCDilate",
            "DepthOfFieldClearIndirectArgs",
            "DepthOfFieldTileMax",
            "DepthOfFieldGather",
            "DepthOfFieldPreCombineFar",
            "DepthOfFieldCombine",
            "DoFCircleOfConfusion",
            "DoFCoCMinMax",
            "DoFMinMaxDilate",
            "DoFApertureShape",
            "DoFComputeSlowTiles",
            "DoFGather",
            "DoFCombine",
            "DepthOfFieldTemporal",
            "DepthOfFieldUtilities",
            "DepthOfFieldMipSafe"
        };
        readonly ComputeShader[] m_ComputeShaders;
        readonly ShaderResources m_Shaders;
        DepthOfFieldCompatibility m_Compatibility;
        internal bool isValid => m_Shaders != null;
        internal bool IsReady => isValid;

        internal DepthOfFieldPass(ComputeShader[] shaders = null)
        {
            m_ComputeShaders = shaders;
            if (shaders != null && shaders.Length == ShaderNames.Length && Array.TrueForAll(shaders, shader => shader != null))
                m_Shaders = new ShaderResources(shaders);
        }

        internal static float CalculateTemporalContrast(float value) => .7f - Mathf.Lerp(0f, .3f, Mathf.SmoothStep(.5f, 1f, value));
        internal static float CalculateCoCScale(float focalLength, float aperture, float focusDistance, float sensorHeight, int height) => DepthOfFieldParameters.CalculateCoCScale(focalLength, aperture, focusDistance, sensorHeight, height);
        internal static Settings PrepareSettings(DepthOfField volume, Camera camera, int width, int height) => DepthOfFieldParameters.PrepareSettings(volume, camera, width, height);
        internal static Vector2 CalculateDepthJitter(Matrix4x4 projection, Matrix4x4 nonJitteredProjection) => DepthOfFieldParameters.CalculateDepthJitter(projection, nonJitteredProjection);
        internal static Vector2 CalculateTextureDepthJitter(UniversalCameraData cameraData) => DepthOfFieldParameters.CalculateTextureDepthJitter(cameraData);
        struct Resources
        {
            internal Settings settings;
            internal ShaderResources shaders;
            internal FrameScale scale;
            internal bool historyValid;
            internal bool temporal;
            internal bool exposure;
            internal bool array;
            internal int views;
            internal int frame;
            internal int eye;
            internal Vector4 depthSize;
            internal DepthOfFieldHistory history;
            internal TextureHandle input;
            internal TextureHandle coc;
            internal TextureHandle stableCoC;
            internal TextureHandle near0;
            internal TextureHandle near1;
            internal TextureHandle nearCoC;
            internal TextureHandle nearAlpha;
            internal TextureHandle dilated0;
            internal TextureHandle dilated1;
            internal TextureHandle far0;
            internal TextureHandle far1;
            internal TextureHandle farCoC;
            internal TextureHandle tile0;
            internal TextureHandle tile1;
            internal TextureHandle spatial;
            internal TextureHandle pyramid;
            internal TextureHandle low;
            internal TextureHandle resolved;
            internal TextureHandle resolvedDepth;
            internal TextureHandle mipColor0;
            internal TextureHandle mipColor1;
            internal TextureHandle mipColor2;
            internal TextureHandle mipColor3;
            internal TextureHandle mipScalar0;
            internal TextureHandle mipScalar1;
            internal TextureHandle mipScalar2;
            internal TextureHandle mipScalar3;
            internal TextureHandle output;
            internal TextureHandle source;
            internal TextureHandle depth;
            internal TextureHandle motion;
            internal TextureHandle previousCoC;
            internal TextureHandle previousColor;
            internal TextureHandle previousVelocity;
            internal TextureHandle previousDepth;
            internal TextureHandle currentVelocity;
            internal TextureHandle exposureCurrent;
            internal TextureHandle exposurePrevious;
            internal TextureHandle black;
            internal BufferHandle nearSamples;
            internal BufferHandle farSamples;
            internal BufferHandle indirectDispatch;
            internal BufferHandle nearTiles;
            internal BufferHandle farTiles;
            internal BufferHandle apertureShape;
        }

        sealed class PassData
        {
            internal Resources resources;
        }

        sealed class FrameScale : ContextItem
        {
            internal Vector4 value = Vector4.one;
            public override void Reset()
            {
                value = Vector4.one;
            }
        }

        static TextureHandle CreateTexture(RenderGraph graph, RenderTextureDescriptor descriptor, string name, int width, int height, GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat, bool mipmaps = false)
        {
            descriptor.width = width;
            descriptor.height = height;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = 0;
            descriptor.memoryless = RenderTextureMemoryless.None;
            descriptor.useDynamicScale = false;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = mipmaps;
            descriptor.autoGenerateMips = false;
            descriptor.mipCount = mipmaps ? 0 : 1;
            descriptor.graphicsFormat = format;
            return UniversalRenderer.CreateRenderGraphTexture(graph, descriptor, "Depth of Field " + name, false, FilterMode.Bilinear);
        }

        Resources InitResource(RenderGraph graph, ContextContainer frameData, TextureHandle source, Settings settings)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var cameraResources = frameData.Get<UniversalResourceData>();
            var sourceDesc = graph.GetTextureDesc(source);
            bool jittered = cameraData.IsTemporalAAEnabled();
            if (jittered)
                settings.depthJitter = CalculateTextureDepthJitter(cameraData);
            settings.stabilize &= jittered && cameraResources.motionVectorColor.IsValid();
            bool temporal = jittered && cameraResources.motionVectorColor.IsValid() && settings.physicalAlgorithm && !settings.debug && !settings.debugTiles;
            var descriptor = cameraData.cameraTargetDescriptor;
            descriptor.dimension = sourceDesc.dimension;
            descriptor.volumeDepth = Mathf.Max(1, sourceDesc.slices);
            descriptor.width = settings.width;
            descriptor.height = settings.height;
            var depthDesc = graph.GetTextureDesc(cameraResources.cameraDepthTexture);
            var data = new Resources
            {
                settings = settings,
                shaders = m_Shaders,
                source = source,
                depth = cameraResources.cameraDepthTexture,
                motion = cameraResources.motionVectorColor.IsValid() ? cameraResources.motionVectorColor : cameraResources.cameraDepthTexture,
                scale = frameData.GetOrCreate<FrameScale>(),
                eye = cameraData.xr.multipassId,
                temporal = temporal,
                frame = Time.frameCount,
                array = descriptor.dimension == TextureDimension.Tex2DArray,
                views = descriptor.dimension == TextureDimension.Tex2DArray ? descriptor.volumeDepth : 1,
                depthSize = Size(depthDesc.width, depthDesc.height),
                black = graph.defaultResources.blackTexture
            };
            var system = HistoryFrameRTSystem.GetOrCreate(cameraData.camera, data.eye);
            if (settings.stabilize || temporal)
            {
                data.history = system.depthOfFieldHistory ?? (system.depthOfFieldHistory = new DepthOfFieldHistory(system));
                data.historyValid = data.history.Prepare(cameraData, data.eye, descriptor, DepthOfFieldParameters.SettingsKey(settings, cameraData.camera), temporal);
            }
            else
                system.depthOfFieldHistory?.Reset();
            data.input = CreateTexture(graph, descriptor, "Input", settings.width, settings.height);
            data.coc = CreateTexture(graph, descriptor, "CoC", settings.width, settings.height, GraphicsFormat.R16_SFloat);
            data.spatial = CreateTexture(graph, descriptor, "Spatial", settings.width, settings.height);
            if (data.history != null)
            {
                data.stableCoC = graph.ImportTexture(data.history.Current(data.eye, DepthOfFieldHistory.Surface.CoC));
                data.resolvedDepth = graph.ImportTexture(data.history.Current(data.eye, DepthOfFieldHistory.Surface.Depth));
                data.previousCoC = graph.ImportTexture(data.history.Previous(data.eye, DepthOfFieldHistory.Surface.CoC));
                data.previousDepth = graph.ImportTexture(data.history.Previous(data.eye, DepthOfFieldHistory.Surface.Depth));
            }
            else
            {
                data.stableCoC = CreateTexture(graph, descriptor, "StableCoC", settings.width, settings.height, GraphicsFormat.R16_SFloat);
                data.resolvedDepth = CreateTexture(graph, descriptor, "Depth", settings.width, settings.height, GraphicsFormat.R32_SFloat);
                data.previousCoC = data.coc;
                data.previousDepth = data.depth;
            }

            if (temporal)
            {
                data.resolved = graph.ImportTexture(data.history.Current(data.eye, DepthOfFieldHistory.Surface.Color));
                data.previousColor = graph.ImportTexture(data.history.Previous(data.eye, DepthOfFieldHistory.Surface.Color));
                data.currentVelocity = graph.ImportTexture(data.history.Current(data.eye, DepthOfFieldHistory.Surface.Velocity));
                data.previousVelocity = graph.ImportTexture(data.history.Previous(data.eye, DepthOfFieldHistory.Surface.Velocity));
                var exposure = system.exposureHistory;
                if (exposure?.Current() != null && exposure.Previous() != null && VolumeManager.instance.stack.GetComponent<Exposure>().isAutomatic)
                {
                    data.exposureCurrent = graph.ImportTexture(exposure.Current());
                    data.exposurePrevious = graph.ImportTexture(exposure.Previous());
                    data.exposure = true;
                }
            }

            if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                data.output = CreateTexture(graph, descriptor, "Output", settings.width, settings.height);
            if (!settings.debug)
            {
                if (settings.physicalAlgorithm)
                    InitPhysicalResource(graph, descriptor, ref data);
                else
                    InitLayeredResource(graph, descriptor, ref data);
            }

            return data;
        }

        static void InitMipResource(RenderGraph graph, RenderTextureDescriptor descriptor, ref Resources data, int width, int height, bool scalar)
        {
            data.mipColor0 = CreateTexture(graph, descriptor, "MipColor0", Mathf.Max(1, width >> 1), Mathf.Max(1, height >> 1));
            data.mipColor1 = CreateTexture(graph, descriptor, "MipColor1", Mathf.Max(1, width >> 2), Mathf.Max(1, height >> 2));
            data.mipColor2 = CreateTexture(graph, descriptor, "MipColor2", Mathf.Max(1, width >> 3), Mathf.Max(1, height >> 3));
            data.mipColor3 = CreateTexture(graph, descriptor, "MipColor3", Mathf.Max(1, width >> 4), Mathf.Max(1, height >> 4));
            if (!scalar)
                return;
            data.mipScalar0 = CreateTexture(graph, descriptor, "MipScalar0", Mathf.Max(1, width >> 1), Mathf.Max(1, height >> 1), GraphicsFormat.R16_SFloat);
            data.mipScalar1 = CreateTexture(graph, descriptor, "MipScalar1", Mathf.Max(1, width >> 2), Mathf.Max(1, height >> 2), GraphicsFormat.R16_SFloat);
            data.mipScalar2 = CreateTexture(graph, descriptor, "MipScalar2", Mathf.Max(1, width >> 3), Mathf.Max(1, height >> 3), GraphicsFormat.R16_SFloat);
            data.mipScalar3 = CreateTexture(graph, descriptor, "MipScalar3", Mathf.Max(1, width >> 4), Mathf.Max(1, height >> 4), GraphicsFormat.R16_SFloat);
        }

        static void InitPhysicalResource(RenderGraph graph, RenderTextureDescriptor descriptor, ref Resources data)
        {
            var s = data.settings;
            data.tile0 = CreateTexture(graph, descriptor, "Tile0", s.tileWidth, s.tileHeight);
            data.tile1 = CreateTexture(graph, descriptor, "Tile1", s.tileWidth, s.tileHeight);
            data.low = CreateTexture(graph, descriptor, "Low", s.blurWidth, s.blurHeight);
            data.apertureShape = graph.CreateBuffer(new BufferDesc(256, 8) { name = "Aperture Shape" });
            if (s.downsample != 1)
            {
                data.pyramid = CreateTexture(graph, descriptor, "Pyramid", s.width, s.height, mipmaps: true);
                InitMipResource(graph, descriptor, ref data, s.width, s.height, false);
            }
        }

        static void InitLayeredResource(RenderGraph graph, RenderTextureDescriptor descriptor, ref Resources data)
        {
            var s = data.settings;
            int tiles = ((s.blurWidth + 7) / 8) * ((s.blurHeight + 7) / 8);
            if (s.lens.z > 0)
            {
                data.near0 = CreateTexture(graph, descriptor, "Near0", s.blurWidth, s.blurHeight);
                data.near1 = CreateTexture(graph, descriptor, "Near1", s.blurWidth, s.blurHeight);
                data.nearCoC = CreateTexture(graph, descriptor, "NearCoC", s.blurWidth, s.blurHeight, GraphicsFormat.R16_SFloat);
                data.nearAlpha = CreateTexture(graph, descriptor, "NearAlpha", s.blurWidth, s.blurHeight, GraphicsFormat.R16_SFloat);
                data.dilated0 = CreateTexture(graph, descriptor, "Dilated0", s.blurWidth, s.blurHeight, GraphicsFormat.R16_SFloat);
                data.dilated1 = CreateTexture(graph, descriptor, "Dilated1", s.blurWidth, s.blurHeight, GraphicsFormat.R16_SFloat);
                data.nearSamples = graph.CreateBuffer(new BufferDesc((int)(s.counts.x * s.counts.x), 4) { name = "Near Aperture Samples" });
            }

            if (s.lens.w > 0)
            {
                data.far0 = CreateTexture(graph, descriptor, "Far0", s.blurWidth, s.blurHeight, mipmaps: true);
                data.far1 = CreateTexture(graph, descriptor, "Far1", s.blurWidth, s.blurHeight);
                data.farCoC = CreateTexture(graph, descriptor, "FarCoC", s.blurWidth, s.blurHeight, GraphicsFormat.R16_SFloat, true);
                data.farSamples = graph.CreateBuffer(new BufferDesc((int)(s.counts.y * s.counts.y), 4) { name = "Far Aperture Samples" });
                InitMipResource(graph, descriptor, ref data, s.blurWidth, s.blurHeight, true);
            }

            if (!data.array)
            {
                data.indirectDispatch = graph.CreateBuffer(new BufferDesc(6, 4, GraphicsBuffer.Target.IndirectArguments) { name = "Depth of Field Dispatch" });
                data.nearTiles = graph.CreateBuffer(new BufferDesc(tiles, 4, GraphicsBuffer.Target.Append) { name = "Near Active Tiles" });
                data.farTiles = graph.CreateBuffer(new BufferDesc(tiles, 4, GraphicsBuffer.Target.Append) { name = "Far Active Tiles" });
            }
        }

        internal TextureHandle Render(RenderGraph graph, ContextContainer frameData, TextureHandle source)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var volume = VolumeManager.instance.stack.GetComponent<DepthOfField>();
            if (!isValid || volume == null || !volume.IsActive() || volume.mode.value != DepthOfFieldMode.Bokeh)
            {
                DepthOfFieldHistory.Get(cameraData.camera, cameraData.xr.multipassId)?.Reset();
                return source;
            }

            var sourceDesc = graph.GetTextureDesc(source);
            var settings = PrepareSettings(volume, cameraData.camera, sourceDesc.width, sourceDesc.height);
            settings.alpha = cameraData.isAlphaOutputEnabled;
            settings.temporal.w = cameraData.taaSettings.baseBlendFactor;
            if (settings.lens.z <= 0 && settings.lens.w <= 0)
            {
                DepthOfFieldHistory.Get(cameraData.camera, cameraData.xr.multipassId)?.Reset();
                return source;
            }

            var resources = InitResource(graph, frameData, source, settings);
            RenderPrepareInput(graph, resources);
            RenderCircleOfConfusion(graph, resources);
            RenderReprojectCoC(graph, resources);
            if (settings.debug)
                RenderDebugCoC(graph, resources);
            else
            {
                if (settings.physicalAlgorithm)
                    RenderPhysical(graph, resources);
                else
                    RenderLayered(graph, resources);
                if (resources.temporal)
                    RenderTemporal(graph, resources);
            }

            if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
            {
                RenderEncodeOutput(graph, resources);
                return resources.output;
            }

            return resources.temporal ? resources.resolved : resources.spatial;
        }

        static void RenderPhysical(RenderGraph graph, in Resources resources)
        {
            RenderPhysicalMinMax(graph, resources);
            RenderPhysicalDilation(graph, resources);
            RenderPhysicalAperture(graph, resources);
            RenderPhysicalSlowGather(graph, resources);
            if (resources.settings.downsample != 1)
                RenderPhysicalPyramid(graph, resources);
            RenderPhysicalGather(graph, resources);
            RenderPhysicalCombine(graph, resources);
        }

        static void RenderLayered(RenderGraph graph, in Resources resources)
        {
            bool near = resources.settings.lens.z > 0, far = resources.settings.lens.w > 0;
            RenderLayeredAperture(graph, resources);
            RenderLayeredPrefilter(graph, resources);
            if (far)
                RenderLayeredPyramids(graph, resources);
            if (near)
                RenderLayeredDilation(graph, resources);
            if (!resources.array)
                RenderLayeredTiles(graph, resources);
            if (far)
                RenderLayeredFarGather(graph, resources);
            if (near && far)
                RenderLayeredPreCombine(graph, resources);
            if (near)
                RenderLayeredNearGather(graph, resources);
            RenderLayeredCombine(graph, resources);
        }

        DepthOfFieldCompatibility compatibility => m_Compatibility ?? (m_Compatibility = new DepthOfFieldCompatibility(m_ComputeShaders));

        internal void Render(CommandBuffer cmd, UniversalCameraData cameraData, RenderTextureDescriptor descriptor, DepthOfField volume, RTHandle source, RTHandle destination, RTHandle depth, RTHandle motion) => compatibility.Render(cmd, cameraData, descriptor, volume, source, destination, depth, motion);
        internal void Render(CommandBuffer cmd, UniversalCameraData cameraData, DepthOfField volume, RTHandle source, RTHandle destination, RTHandle depth, RTHandle motion) => compatibility.Render(cmd, cameraData, volume, source, destination, depth, motion);
        internal void Render(CommandBuffer cmd, Camera camera, RenderTextureDescriptor descriptor, DepthOfField volume, Material material, RTHandle source, RTHandle destination, RTHandle depth, Vector2 depthJitter = default) => compatibility.Render(cmd, camera, descriptor, volume, material, source, destination, depth, depthJitter);
        public void Dispose() => m_Compatibility?.Dispose();
    }
}
