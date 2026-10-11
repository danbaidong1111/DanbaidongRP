using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed class AtmosphericFogPass : IDisposable
    {
        readonly Material m_Material;
        RTHandle m_BlackIntegral;

        sealed class PassData
        {
            public Material material;
            public VBufferParameters buffer;
            public ShaderVariablesAtmosphericFog constantBuffer;
            public TextureHandle source, destination, depth, sky, integral, colorPyramid;
            public bool multipleScattering;
        }
        sealed class CompositePassData { public PassData fog; public TextureHandle source; public int materialPass; }
        sealed class ResetData { }

        internal AtmosphericFogPass(Shader shader)
        {
            if (shader != null) m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_BlackIntegral = RTHandles.Alloc(CoreUtils.blackVolumeTexture);
        }

        internal void Setup(RenderGraph graph, ContextContainer frameData, bool supportsVolumetrics)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var data = frameData.Create<FogRenderingData>();
            data.settings = VolumeManager.instance.stack.GetComponent<AtmosphericFog>();
            data.active = m_Material != null && data.settings != null && data.settings.IsActive()
                && !cameraData.isPreviewCamera && CoreUtils.IsSceneViewFogEnabled(cameraData.camera);
            if (!data.active) return;
            data.volumetric = data.settings.volumetric.value && supportsVolumetrics;
            data.gaussian = (data.settings.effectiveDenoising & FogDenoisingMode.Gaussian) != 0;
            data.buffer = new VBufferParameters(cameraData, data.settings);
            UpdateFogConstantBuffer(data, graph, cameraData, frameData.Get<UniversalLightData>(), frameData.Get<UniversalResourceData>().skyReflectionProbe);
        }

        public void ResetGlobals(RenderGraph graph)
        {
            if (m_BlackIntegral == null || m_BlackIntegral.externalTexture == null)
            {
                m_BlackIntegral?.Release();
                m_BlackIntegral = RTHandles.Alloc(CoreUtils.blackVolumeTexture);
            }
            using (var builder = graph.AddComputePass<ResetData>("Reset Atmospheric Fog", out _))
            {
                var integral = graph.ImportTexture(m_BlackIntegral);
                builder.UseTexture(integral, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(integral, ShaderConstants.Integral);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (ResetData data, ComputeGraphContext context) => context.cmd.SetGlobalFloat(ShaderConstants.Enabled, 0));
            }
        }

        void InitResource(RenderGraph graph, PassData data, UniversalResourceData resources, FogRenderingData fog, TextureHandle lighting)
        {
            data.material = m_Material;
            data.buffer = fog.buffer;
            data.constantBuffer = fog.constantBuffer;
            data.source = resources.activeColorTexture;
            data.depth = resources.cameraDepthTexture;
            data.sky = resources.skyReflectionProbe;
            data.integral = lighting.IsValid() ? lighting : graph.ImportTexture(m_BlackIntegral);
            data.multipleScattering = !fog.settings.isPhysical && fog.settings.multipleScatteringIntensity.value > 0;
            var descriptor = graph.GetTextureDesc(data.source);
            descriptor.name = "Atmospheric Fog Color";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.clearBuffer = false;
            data.destination = graph.CreateTexture(descriptor);
            if (data.multipleScattering)
            {
                descriptor.name = "Fog Multiple Scattering Pyramid";
                descriptor.useMipMap = true;
                data.colorPyramid = graph.CreateTexture(descriptor);
            }
        }

        void UpdateFogConstantBuffer(FogRenderingData data, RenderGraph graph, UniversalCameraData cameraData, UniversalLightData lightData, TextureHandle sky)
        {
            var fog = data.settings;
            var camera = cameraData.camera;
            bool physical = fog.isPhysical;
            Vector3 sun = lightData.mainLightIndex >= 0
                ? -lightData.visibleLights[lightData.mainLightIndex].localToWorldMatrix.GetColumn(2) : Vector3.up;
            PlanarReflectionSystem.TryGetReflectionPlane(camera, out var reflectionPlane);
            var encoding = data.buffer.depthEncoding;
            var decoding = data.buffer.depthDecoding;

            ref var cb = ref data.constantBuffer;
            cb._FogDensity = new Vector4(fog.extinction, fog.baseHeight.value, 1f / fog.scaleHeight, physical ? 0 : fog.startDistance.value);
            cb._FogGround = physical ? Vector4.zero : new Vector4(fog.groundDensity.value, fog.groundHeight.value,
                1f / fog.groundHeightScale.value, fog.groundStartDistance.value);
            cb._FogColor = fog.color.value;
            cb._FogColor.w = physical ? 1 : fog.maxOpacity.value;
            cb._FogOptions = new Vector4(Mathf.Min(camera.farClipPlane, fog.maxDistance.value), (float)fog.mode.value,
                physical || fog.affectSky.value ? 1 : 0, 0);
            cb._FogSunColor = fog.sunColor.value;
            cb._FogSunDirection = sun;
            cb._FogScatter = new Vector4(fog.anisotropy.value, physical ? 0 : fog.scattering.value, 0, Time.realtimeSinceStartup);
            cb._FogVolume = new Vector4(data.volumetric ? 1 : 0, data.buffer.near, data.buffer.far, data.buffer.slices);
            cb._FogCameraPosition = camera.transform.position;
            cb._FogCameraPosition.w = camera.orthographic ? 1 : 0;
            cb._FogCameraForward = camera.transform.forward;
            cb._FogReflectionPlane = reflectionPlane;
            cb._FogDepthEncoding = encoding;
            cb._FogDepthDecoding = decoding;
            cb._FogAdvanced = new Vector4(physical || fog.constantDensityBelowBase.value ? 1 : 0,
                (float)fog.colorMode.value, fog.globalLightProbeDimmer.value, physical ? 0 : fog.multipleScatteringIntensity.value);
            cb._FogSkyMip = new Vector4(fog.mipFogNear.value, fog.mipFogFar.value, fog.mipFogMaxMip.value,
                Mathf.Min(6, Mathf.Log(Mathf.Max(1, graph.GetTextureDesc(sky).width), 2)));
            cb._FogSkyTint = fog.skyTint.value;
            cb._FogAlbedo = fog.albedo.value;
            cb._FogBufferSize = data.volumetric ? new Vector4(data.buffer.width, data.buffer.height, 1f / data.buffer.width, 1f / data.buffer.height) : Vector4.zero;
            cb._FogFilterMode = data.gaussian ? 1 : 0;
            cb._FogViewCount = data.buffer.views;
        }

        static void UpdateShaderGlobals(PassData data, ComputeCommandBuffer cmd)
        {
            ConstantBuffer.PushGlobal(cmd, data.constantBuffer, ShaderConstants.ConstantBuffer);
            cmd.SetGlobalFloat(ShaderConstants.Enabled, 1);
            cmd.SetGlobalMatrix(ShaderConstants.RayInvViewProjection, data.buffer.inverseViewProjection);
            cmd.SetGlobalMatrix(ShaderConstants.RayViewProjection, data.buffer.viewProjection);
            cmd.SetGlobalMatrixArray(ShaderConstants.StereoViewProjection, data.buffer.viewProjections);
            cmd.SetGlobalMatrixArray(ShaderConstants.StereoInvViewProjection, data.buffer.inverseViewProjections);
            cmd.SetGlobalVectorArray(ShaderConstants.StereoPosition, data.buffer.positions);
            cmd.SetGlobalVectorArray(ShaderConstants.StereoForward, data.buffer.forwards);
            cmd.SetGlobalTexture(ShaderConstants.CameraDepthTexture, data.depth);
        }

        static void PublishGlobals(RenderGraph graph, PassData data)
        {
            using (var builder = graph.AddComputePass<PassData>("Atmospheric Fog Globals", out var pass))
            {
                pass.buffer = data.buffer;
                pass.constantBuffer = data.constantBuffer;
                pass.depth = data.depth;
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.UseTexture(data.integral, AccessFlags.Read);
                builder.UseTexture(data.sky, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(data.integral, ShaderConstants.Integral);
                builder.SetGlobalTextureAfterPass(data.sky, ShaderConstants.SkyRadiance);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) => UpdateShaderGlobals(pass, context.cmd));
            }
        }
        static void RenderComposite(RenderGraph graph, PassData data, TextureHandle source, TextureHandle destination, int materialPass)
        {
            using (var builder = graph.AddRasterRenderPass<CompositePassData>(
                materialPass < 0 ? "Copy Fog Color" : materialPass == 0 ? "Composite Atmospheric Fog" : "Fog Multiple Scattering", out var pass))
            {
                pass.fog = data;
                pass.source = source;
                pass.materialPass = materialPass;
                builder.UseTexture(source, AccessFlags.Read);
                if (materialPass >= 0)
                {
                    builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.UseTexture(data.sky, AccessFlags.Read);
                    builder.UseTexture(data.integral, AccessFlags.Read);
                }
                if (materialPass == 1)
                    builder.UseTexture(data.colorPyramid, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositePassData pass, RasterGraphContext context) =>
                {
                    RTHandle sourceHandle = pass.source;
                    Vector4 scale = sourceHandle.useScaling ? sourceHandle.rtHandleProperties.rtHandleScale : Vector4.one;
                    var scaleBias = new Vector4(scale.x, scale.y, 0, 0);
                    if (pass.materialPass < 0)
                        Blitter.BlitTexture(context.cmd, sourceHandle, scaleBias, 0, false);
                    else
                        Blitter.BlitTexture(context.cmd, sourceHandle, scaleBias, pass.fog.material, pass.materialPass);
                });
            }
        }
        static TextureHandle RenderMultipleScattering(RenderGraph graph, PassData data)
        {
            RenderComposite(graph, data, data.destination, data.colorPyramid, -1);
            using (var builder = graph.AddUnsafePass<CompositePassData>("Fog Color Pyramid", out var pass))
            {
                pass.fog = data;
                builder.UseTexture(data.colorPyramid, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(data.colorPyramid, ShaderConstants.ColorPyramid);
                builder.SetRenderFunc(static (CompositePassData pass, UnsafeGraphContext context) =>
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).GenerateMips(pass.fog.colorPyramid));
            }
            var descriptor = graph.GetTextureDesc(data.destination);
            descriptor.name = "Fog Scattered Color";
            var destination = graph.CreateTexture(descriptor);
            RenderComposite(graph, data, data.destination, destination, 1);
            return destination;
        }

        public void Render(RenderGraph graph, ContextContainer frameData, TextureHandle lighting)
        {
            var fog = frameData.Get<FogRenderingData>();
            if (!fog.active) return;
            var resources = frameData.Get<UniversalResourceData>();
            var data = new PassData();
            InitResource(graph, data, resources, fog, lighting);
            PublishGlobals(graph, data);
            RenderComposite(graph, data, data.source, data.destination, 0);
            TextureHandle result = data.multipleScattering ? RenderMultipleScattering(graph, data) : data.destination;
            if (resources.activeColorID == UniversalResourceData.ActiveID.Camera)
                resources.cameraColor = result;
            else
                RenderComposite(graph, data, result, data.source, -1);
        }

        public void Dispose()
        {
            m_BlackIntegral.Release();
            CoreUtils.Destroy(m_Material);
            Shader.SetGlobalFloat(ShaderConstants.Enabled, 0);
        }

        static class ShaderConstants
        {
            public static readonly int ConstantBuffer = Shader.PropertyToID("ShaderVariablesAtmosphericFog");
            public static readonly int Enabled = Shader.PropertyToID("_FogEnabled");
            public static readonly int Integral = Shader.PropertyToID("_FogIntegral");
            public static readonly int SkyRadiance = Shader.PropertyToID("_FogSkyRadiance");
            public static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int RayInvViewProjection = Shader.PropertyToID("_FogRayInvViewProjection");
            public static readonly int RayViewProjection = Shader.PropertyToID("_FogRayViewProjection");
            public static readonly int StereoViewProjection = Shader.PropertyToID("_FogStereoViewProjection");
            public static readonly int StereoInvViewProjection = Shader.PropertyToID("_FogStereoInvViewProjection");
            public static readonly int StereoPosition = Shader.PropertyToID("_FogStereoPosition");
            public static readonly int StereoForward = Shader.PropertyToID("_FogStereoForward");
            public static readonly int ColorPyramid = Shader.PropertyToID("_FogColorPyramid");
        }
    }
}
