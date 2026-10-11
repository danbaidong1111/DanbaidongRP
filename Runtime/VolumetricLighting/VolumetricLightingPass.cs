using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class VolumetricLightingPass : IDisposable
    {
        static readonly ProfilingSampler s_VoxelizationSampler = new ProfilingSampler("Voxelize Volumetric Medium");
        static readonly ProfilingSampler s_DepthSampler = new ProfilingSampler("Build Volumetric Depth");
        static readonly ProfilingSampler s_LightingSampler = new ProfilingSampler("Trace Volumetric Lighting");
        static readonly ProfilingSampler s_FilteringSampler = new ProfilingSampler("Filter Volumetric Lighting");

        readonly ComputeShader m_Compute, m_Voxelization, m_Filtering;
        readonly LightCookieManager m_Cookies;
        readonly Dictionary<Texture, RTHandle> m_ExternalTextures = new Dictionary<Texture, RTHandle>();
        readonly int m_Lighting, m_LightingNoHistory, m_MaxDepth, m_DilateDepth, m_Voxelize, m_Filter, m_Local;
        AtmosphericFog m_Settings;

        internal bool isAvailable => m_Compute != null && m_Voxelization != null && m_Filtering != null && SystemInfo.supports3DRenderTextures && SystemInfo.supportsComputeShaders;

        internal sealed class PassData
        {
            public ComputeShader compute, voxelization, filtering;
            public int lightingKernel, maxDepthKernel, dilateDepthKernel, voxelize, filter, localKernel;
            public VBufferParameters buffer;
            public Vector3Int storageSize;
            public bool gaussian, depthIsArray, temporal;
            public Vector4 sceneDepthSize;
            public ShaderVariablesAtmosphericFog constantBuffer;
            public TextureHandle depth, reducedDepth, maxDepth, feedback, medium, previous, integral, filtered;
            public TextureHandle dirShadows, additionalShadows, mainCookie, additionalCookies;
            public BufferHandle ambientProbe;
            public GPULights.GPULightsOutPassData lights;
            public Vector4 grid, lighting, shadowFlags, historyParams, previousDepthEncoding, previousViewport;
            public Matrix4x4[] previousViewProjections;
            public Vector4[] previousPositions, previousForwards;
            public LocalVolumetricFogData[] locals = Array.Empty<LocalVolumetricFogData>();
            public TextureHandle[] materialMasks = Array.Empty<TextureHandle>();
            public TextureHandle[] localMasks = Array.Empty<TextureHandle>();
            public int localCount => locals.Length;
        }
        sealed class ComputePassData { public PassData fog; }

        internal VolumetricLightingPass(ComputeShader compute, ComputeShader voxelization, ComputeShader filtering, LightCookieManager cookies)
        {
            m_Compute = compute;
            m_Cookies = cookies;
            m_Voxelization = voxelization;
            m_Filtering = filtering;
            if (!isAvailable) return;
            m_Lighting = compute.FindKernel("VolumetricLighting");
            m_LightingNoHistory = compute.FindKernel("VolumetricLightingNoHistory");
            m_MaxDepth = compute.FindKernel("BuildMaxDepth");
            m_DilateDepth = compute.FindKernel("DilateMaxDepth");
            m_Voxelize = voxelization.FindKernel("VoxelizeFog");
            m_Filter = filtering.FindKernel("FilterFog");
            m_Local = voxelization.FindKernel("VoxelizeLocalFog");
        }

        void InitResource(RenderGraph graph, PassData data, ContextContainer frameData, FogRenderingData fog)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            var lightData = frameData.Get<UniversalLightData>();
            data.compute = m_Compute;
            data.voxelization = m_Voxelization;
            data.filtering = m_Filtering;
            data.lightingKernel = m_Lighting;
            data.maxDepthKernel = m_MaxDepth;
            data.dilateDepthKernel = m_DilateDepth;
            data.voxelize = m_Voxelize;
            data.filter = m_Filter;
            data.localKernel = m_Local;
            data.buffer = fog.buffer;
            data.constantBuffer = fog.constantBuffer;
            data.gaussian = fog.gaussian;
            data.depth = resources.cameraDepthTexture;
            var depthDesc = graph.GetTextureDesc(data.depth);
            data.depthIsArray = depthDesc.dimension == TextureDimension.Tex2DArray;
            data.sceneDepthSize = new Vector4(depthDesc.width, depthDesc.height, 1f / depthDesc.width, 1f / depthDesc.height);
            var mainCookie = lightData.mainLightIndex >= 0 ? lightData.visibleLights[lightData.mainLightIndex].light.cookie : null;
            data.mainCookie = ImportTexture(graph, mainCookie != null ? mainCookie : Texture2D.whiteTexture);
            var cookieAtlas = m_Cookies?.AdditionalLightsCookieAtlasTexture;
            data.additionalCookies = cookieAtlas != null && cookieAtlas.rt != null
                ? graph.ImportTexture(cookieAtlas) : ImportTexture(graph, Texture2D.whiteTexture);
            InitVolumetricResource(graph, data, frameData);
        }
        void InitVolumetricResource(RenderGraph graph, PassData data, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            int hash = m_Settings.GetHashCode();
            if (!m_Settings.isPhysical)
                hash = GetLightingHash(frameData.Get<UniversalLightData>(), hash);
            data.locals = LocalVolumetricFogManager.Capture(m_Settings, data.buffer, cameraData.camera, ref hash);
            InitMaterialMaskResources(graph, data);
            UpdateHistory(graph, GetHistoryRTSystem(cameraData), data, cameraData, m_Settings, hash);
            data.lightingKernel = data.temporal ? m_Lighting : m_LightingNoHistory;
            InitVolumeTextures(graph, data);

            var fog = m_Settings;
            data.lighting = new Vector4(fog.isPhysical ? 0 : fog.ambientIntensity.value,
                fog.isPhysical ? 1 : fog.lightIntensity.value, fog.shadows.value ? 1 : 0,
                !fog.isPhysical && fog.directionalOnly.value ? 1 : 0);
            data.shadowFlags = new Vector4(resources.directionalShadowsTexture.IsValid() ? 1 : 0,
                resources.additionalShadowsTexture.IsValid() ? 1 : 0, 0,
                cameraData.xr.enabled || cameraData.camera.allowDynamicResolution ? 1 : 0);
            data.ambientProbe = resources.skyAmbientRawProbe;
            data.lights = frameData.Get<GPULights.GPULightsOutPassData>();
            data.dirShadows = resources.directionalShadowsTexture.IsValid()
                ? resources.directionalShadowsTexture : graph.defaultResources.defaultShadowArrayTexture;
            data.additionalShadows = resources.additionalShadowsTexture.IsValid()
                ? resources.additionalShadowsTexture : graph.defaultResources.defaultShadowTexture;
        }

        static int GetLightingHash(UniversalLightData lightData, int hash)
        {
            // Lighting edits should not drag an unrelated illumination state into a new frame.
            foreach (var light in lightData.visibleLights)
            {
                hash = unchecked(hash * 23 + light.localToWorldMatrix.GetHashCode() + light.finalColor.GetHashCode()
                    + light.range.GetHashCode() + light.spotAngle.GetHashCode());
                if (light.light != null && light.light.TryGetComponent<UniversalAdditionalLightData>(out var extra))
                    hash = unchecked(hash * 23 + extra.volumetricDimmer.GetHashCode() + extra.volumetricShadowDimmer.GetHashCode() + extra.volumetricLightRadius.GetHashCode());
                if (light.light != null && light.light.cookie != null)
                    hash = unchecked(hash * 23 + light.light.cookie.GetInstanceID());
            }
            return hash;
        }

        void InitMaterialMaskResources(RenderGraph graph, PassData data)
        {
            data.materialMasks = new TextureHandle[data.localCount];
            data.localMasks = new TextureHandle[data.localCount];
            for (int i = 0; i < data.localCount; ++i)
            {
                var local = data.locals[i];
                if (local.maskMode != 1 || local.material == null)
                {
                    data.localMasks[i] = ImportTexture(graph, local.texture != null ? local.texture : CoreUtils.blackVolumeTexture);
                    continue;
                }
                int size = local.maskResolution;
                var maskDesc = new TextureDesc(size, size) { dimension = TextureDimension.Tex3D, slices = size,
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat, name = "Local Fog Material Mask" };
                data.materialMasks[i] = graph.CreateTexture(maskDesc);
                data.localMasks[i] = data.materialMasks[i];

            }
        }

        static void InitVolumeTextures(RenderGraph graph, PassData data)
        {
            var descriptor = new TextureDesc(data.storageSize.x, data.storageSize.y)
            {
                dimension = TextureDimension.Tex3D,
                slices = data.storageSize.z,
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Volumetric Fog Integral"
            };
            data.integral = graph.CreateTexture(descriptor);
            descriptor.name = "Volumetric Fog Medium";
            data.medium = graph.CreateTexture(descriptor);
            data.maxDepth = graph.CreateTexture(new TextureDesc(data.buffer.width, data.buffer.height)
            {
                dimension = TextureDimension.Tex2DArray, slices = data.buffer.views,
                colorFormat = GraphicsFormat.R32_SFloat, enableRandomWrite = true,
                name = "Volumetric Max Depth"
            });
            var depthDescriptor = graph.GetTextureDesc(data.maxDepth);
            depthDescriptor.name = "Volumetric Reduced Depth";
            data.reducedDepth = graph.CreateTexture(depthDescriptor);
            if (data.gaussian)
            {
                descriptor.name = "Volumetric Fog Filtered";
                data.filtered = graph.CreateTexture(descriptor);
            }
        }

        static void SetViewConstants(PassData data, ComputeShader cs, ComputeCommandBuffer cmd, int eye)
        {
            var constants = data.constantBuffer;
            constants._FogCameraPosition = data.buffer.positions[eye];
            constants._FogCameraForward = data.buffer.forwards[eye];
            ConstantBuffer.Push(cmd, constants, cs, ShaderConstants.ConstantBuffer);
            cmd.SetComputeIntParam(cs, ShaderConstants.FogViewIndex, eye);
            cmd.SetComputeVectorParam(cs, ShaderConstants.FogGrid, data.grid);
            cmd.SetComputeMatrixParam(cs, ShaderConstants.FogInvViewProjection, data.buffer.inverseViewProjections[eye]);
        }

        static void ExecuteVoxelization(PassData data, ComputeCommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, s_VoxelizationSampler))
            {
                var cs = data.voxelization;
                cmd.SetComputeTextureParam(cs, data.voxelize, ShaderConstants.FogMedium, data.medium);
                cmd.SetComputeTextureParam(cs, data.localKernel, ShaderConstants.FogMedium, data.medium);
                for (int eye = 0; eye < data.buffer.views; ++eye)
                {
                    SetViewConstants(data, cs, cmd, eye);
                    cmd.DispatchCompute(cs, data.voxelize, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, data.buffer.slices);
                    VoxelizeLocalVolumes(data, cmd);
                }
            }
        }

        static void SetDepthConstants(PassData data, ComputeCommandBuffer cmd, int kernel)
        {
            var cs = data.compute;
            if (data.depthIsArray) cs.EnableKeyword("FOG_DEPTH_ARRAY");
            else cs.DisableKeyword("FOG_DEPTH_ARRAY");
            cmd.SetComputeVectorParam(cs, ShaderConstants.FogSceneDepthSize, data.sceneDepthSize);
            cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.FogSceneDepth, data.depth);
            cmd.SetComputeTextureParam(cs, kernel,
                kernel == data.maxDepthKernel ? ShaderConstants.FogMaxDepth : ShaderConstants.FogMaxDepthRead,
                kernel == data.maxDepthKernel ? data.reducedDepth : data.maxDepth);
        }

        static void ExecuteMaxDepth(PassData data, ComputeCommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, s_DepthSampler))
            {
                SetDepthConstants(data, cmd, data.maxDepthKernel);
                for (int eye = 0; eye < data.buffer.views; ++eye)
                {
                    SetViewConstants(data, data.compute, cmd, eye);
                    cmd.SetComputeMatrixParam(data.compute, ShaderConstants.FogDepthInvViewProjection, data.buffer.depthInverseViewProjections[eye]);
                    cmd.DispatchCompute(data.compute, data.maxDepthKernel, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, 1);
                }
                cmd.SetComputeTextureParam(data.compute, data.dilateDepthKernel, ShaderConstants.FogMaxDepthRead, data.reducedDepth);
                cmd.SetComputeTextureParam(data.compute, data.dilateDepthKernel, ShaderConstants.FogDilatedDepth, data.maxDepth);
                for (int eye = 0; eye < data.buffer.views; ++eye)
                {
                    cmd.SetComputeIntParam(data.compute, ShaderConstants.FogViewIndex, eye);
                    cmd.DispatchCompute(data.compute, data.dilateDepthKernel, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, 1);
                }
            }
        }

        static void ExecuteLighting(PassData data, ComputeCommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, s_LightingSampler))
            {
                var cs = data.compute;
                int k = data.lightingKernel;
                SetDepthConstants(data, cmd, k);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLighting, data.lighting);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogShadowFlags, data.shadowFlags);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogHistoryParams, data.historyParams);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogPreviousDepthEncoding, data.previousDepthEncoding);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogHistoryViewport, data.previousViewport);
                ConstantBuffer.Push(cmd, data.lights.lightListCB, cs, ShaderConstants.ShaderVariablesLightList);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.g_GPULightDatas, data.lights.GPULightsData);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.g_DirectionalLightDatas, data.lights.directionalLightsData);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.FogCoarseLightList, data.lights.coarseLightList);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.g_vLayeredOffsetsBuffer, data.lights.perVoxelOffset);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.g_vLightListCluster, data.lights.perVoxelLightLists);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.g_logBaseBuffer, data.lights.perTileLogBaseTweak);
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.DirectionalLightsShadowmapTexture, data.dirShadows);
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.AdditionalLightsShadowmapTexture, data.additionalShadows);
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.MainLightCookieTexture, data.mainCookie);
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.AdditionalLightsCookieAtlasTexture, data.additionalCookies);
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.FogMedium, data.medium);
                cmd.SetComputeBufferParam(cs, k, ShaderConstants.FogAmbientProbe, data.ambientProbe);
                if (data.temporal)
                {
                    cmd.SetComputeTextureParam(cs, k, ShaderConstants.FogFeedback, data.feedback);
                    cmd.SetComputeTextureParam(cs, k, ShaderConstants.FogHistory, data.previous);
                }
                cmd.SetComputeTextureParam(cs, k, ShaderConstants.FogIntegralWrite, data.integral);
                cmd.SetComputeFloatParam(cs, ShaderConstants.FogDepthCulling, 1);
                for (int eye = 0; eye < data.buffer.views; ++eye)
                {
                    SetViewConstants(data, cs, cmd, eye);
                    cmd.SetComputeMatrixParam(cs, ShaderConstants.FogDepthInvViewProjection, data.buffer.depthInverseViewProjections[eye]);
                    cmd.SetComputeMatrixParam(cs, ShaderConstants.FogPreviousViewProjection, data.previousViewProjections[eye]);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.FogPreviousPosition, data.previousPositions[eye]);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.FogPreviousForward, data.previousForwards[eye]);
                    cmd.DispatchCompute(cs, k, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, 1);
                }
            }
        }

        static void ExecuteFiltering(PassData data, ComputeCommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, s_FilteringSampler))
            {
                var cs = data.filtering;
                cmd.SetComputeTextureParam(cs, data.filter, ShaderConstants.FogFilterRead, data.integral);
                cmd.SetComputeTextureParam(cs, data.filter, ShaderConstants.FogFilterWrite, data.filtered);
                for (int eye = 0; eye < data.buffer.views; ++eye)
                {
                    SetViewConstants(data, cs, cmd, eye);
                    cmd.DispatchCompute(cs, data.filter, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, data.buffer.slices);
                }
            }
        }

        static void VoxelizeLocalVolumes(PassData data, ComputeCommandBuffer cmd)
        {
            var cs = data.voxelization;
            int kernel = data.localKernel;
            for (int i = 0; i < data.localCount; ++i)
            {
                var local = data.locals[i];
                cmd.SetComputeMatrixParam(cs, ShaderConstants.FogLocalWorldToVolume, local.worldToVolume);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalAlbedoDensity, local.albedoDensity);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalTiling, local.textureTiling);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalOffset, local.textureOffset);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalPositiveFade, ReciprocalFade(local.positiveFade));
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalNegativeFade, ReciprocalFade(local.negativeFade));
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalDistanceFade, local.distanceFade);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalShapeNoise, local.shapeNoise);
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalVelocity, local.noiseVelocity);
                bool hasMaterial = data.materialMasks[i].IsValid();
                bool hasMask = hasMaterial || local.maskMode == 0 && local.texture != null;
                cmd.SetComputeVectorParam(cs, ShaderConstants.FogLocalOptions, new Vector4(local.blendingMode, local.invertFade, local.falloffMode, hasMask ? (local.textureIsAlphaOnly && !hasMaterial ? 2 : 1) : 0));
                cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.FogLocalMask, data.localMasks[i]);
                cmd.DispatchCompute(cs, kernel, (data.buffer.width + 7) / 8, (data.buffer.height + 7) / 8, data.buffer.slices);
            }
        }

        static Vector4 ReciprocalFade(Vector3 fade) => new Vector4(1f / Mathf.Max(1e-6f, fade.x), 1f / Mathf.Max(1e-6f, fade.y), 1f / Mathf.Max(1e-6f, fade.z), 0);

        static void RenderMaterialMasks(RenderGraph graph, PassData data)
        {
            bool hasMasks = false;
            foreach (var mask in data.materialMasks)
                hasMasks |= mask.IsValid();
            if (!hasMasks)
                return;

            // Individual 3D slices require explicit render-target selection.
            using (var builder = graph.AddUnsafePass<ComputePassData>("Local Fog Material Masks", out var pass))
            {
                pass.fog = data;
                foreach (var mask in data.materialMasks)
                    if (mask.IsValid())
                        builder.UseTexture(mask, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (ComputePassData pass, UnsafeGraphContext context) =>
                    BakeMaterialMasks(pass.fog, CommandBufferHelpers.GetNativeCommandBuffer(context.cmd)));
            }
        }

        static void BakeMaterialMasks(PassData data, CommandBuffer cmd)
        {
            for (int i = 0; i < data.localCount; ++i)
            {
                if (!data.materialMasks[i].IsValid())
                    continue;
                var local = data.locals[i];
                int size = local.maskResolution;
                cmd.SetGlobalVector(ShaderConstants.LocalMaskSize, new Vector4(size, size, size, 1f / size));
                cmd.SetGlobalMatrix(ShaderConstants.LocalVolumeToWorld, local.volumeToWorld);
                cmd.SetGlobalFloat(ShaderConstants.LocalTime, data.constantBuffer._FogScatter.w);
                int pass = local.material.FindPass("DanbaidongFogVolume");
                for (int slice = 0; slice < size; ++slice)
                {
                    cmd.SetGlobalFloat(ShaderConstants.LocalSlice, (slice + 0.5f) / size);
                    cmd.SetRenderTarget(data.materialMasks[i], 0, CubemapFace.Unknown, slice);
                    cmd.SetViewport(new Rect(0, 0, size, size));
                    cmd.DrawProcedural(local.volumeToWorld, local.material, Mathf.Max(0, pass), MeshTopology.Triangles, 3);
                }
            }
        }

        static void RenderVoxelization(RenderGraph graph, PassData data)
        {
            using (var builder = graph.AddComputePass<ComputePassData>("Volume Voxelization", out var pass))
            {
                pass.fog = data;
                builder.UseTexture(data.medium, AccessFlags.ReadWrite);
                foreach (var mask in data.localMasks)
                    if (mask.IsValid()) builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderFunc(static (ComputePassData pass, ComputeGraphContext context) => ExecuteVoxelization(pass.fog, context.cmd));
            }
        }

        static void RenderMaxDepth(RenderGraph graph, PassData data)
        {
            using (var builder = graph.AddComputePass<ComputePassData>("Volumetric Max Depth", out var pass))
            {
                pass.fog = data;
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.UseTexture(data.reducedDepth, AccessFlags.ReadWrite);
                builder.UseTexture(data.maxDepth, AccessFlags.Write);
                builder.SetRenderFunc(static (ComputePassData pass, ComputeGraphContext context) => ExecuteMaxDepth(pass.fog, context.cmd));
            }
        }

        static void RenderLighting(RenderGraph graph, PassData data)
        {
            using (var builder = graph.AddComputePass<ComputePassData>("Volumetric Lighting", out var pass))
            {
                pass.fog = data;
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.UseTexture(data.maxDepth, AccessFlags.Read);
                builder.UseTexture(data.medium, AccessFlags.Read);
                if (data.temporal)
                {
                    builder.UseTexture(data.feedback, AccessFlags.Write);
                    builder.UseTexture(data.previous, AccessFlags.Read);
                }
                builder.UseTexture(data.integral, AccessFlags.Write);
                builder.UseTexture(data.dirShadows, AccessFlags.Read);
                builder.UseTexture(data.additionalShadows, AccessFlags.Read);
                builder.UseTexture(data.mainCookie, AccessFlags.Read);
                builder.UseTexture(data.additionalCookies, AccessFlags.Read);
                builder.UseBuffer(data.ambientProbe, AccessFlags.Read);
                builder.UseBuffer(data.lights.GPULightsData, AccessFlags.Read);
                builder.UseBuffer(data.lights.directionalLightsData, AccessFlags.Read);
                builder.UseBuffer(data.lights.coarseLightList, AccessFlags.Read);
                builder.UseBuffer(data.lights.perVoxelOffset, AccessFlags.Read);
                builder.UseBuffer(data.lights.perVoxelLightLists, AccessFlags.Read);
                builder.UseBuffer(data.lights.perTileLogBaseTweak, AccessFlags.Read);
                builder.SetRenderFunc(static (ComputePassData pass, ComputeGraphContext context) => ExecuteLighting(pass.fog, context.cmd));
            }
        }

        static void RenderFiltering(RenderGraph graph, PassData data)
        {
            if (!data.gaussian) return;
            using (var builder = graph.AddComputePass<ComputePassData>("Volumetric Lighting Filtering", out var pass))
            {
                pass.fog = data;
                builder.UseTexture(data.integral, AccessFlags.Read);
                builder.UseTexture(data.filtered, AccessFlags.Write);
                builder.SetRenderFunc(static (ComputePassData pass, ComputeGraphContext context) => ExecuteFiltering(pass.fog, context.cmd));
            }
        }

        static HistoryFrameRTSystem GetHistoryRTSystem(UniversalCameraData cameraData)
        {
            int eye = cameraData.xr.enabled && !cameraData.xr.singlePassEnabled ? cameraData.xr.multipassId : 0;
            return HistoryFrameRTSystem.GetOrCreate(cameraData.camera, eye);
        }

        public TextureHandle Render(RenderGraph graph, ContextContainer frameData)
        {
            var fog = frameData.Get<FogRenderingData>();
            if (!fog.active || !fog.volumetric)
            {
                ResetHistory(GetHistoryRTSystem(frameData.Get<UniversalCameraData>()));
                return TextureHandle.nullHandle;
            }
            m_Settings = fog.settings;
            var data = new PassData();
            InitResource(graph, data, frameData, fog);
            RenderMaterialMasks(graph, data);
            RenderVoxelization(graph, data);
            RenderMaxDepth(graph, data);
            RenderLighting(graph, data);
            RenderFiltering(graph, data);
            return data.gaussian ? data.filtered : data.integral;
        }

        public void Dispose()
        {
            DisposeCameraStates();
            foreach (var texture in m_ExternalTextures.Values) texture.Release();
            m_ExternalTextures.Clear();
        }

        TextureHandle ImportTexture(RenderGraph graph, Texture texture)
        {
            if (!m_ExternalTextures.TryGetValue(texture, out var handle))
            {
                handle = RTHandles.Alloc(texture);
                m_ExternalTextures.Add(texture, handle);
            }
            return graph.ImportTexture(handle);
        }

        static class ShaderConstants
        {
            public static readonly int ShaderVariablesLightList = Shader.PropertyToID("ShaderVariablesLightList");
            public static readonly int AdditionalLightsCookieAtlasTexture = Shader.PropertyToID("_AdditionalLightsCookieAtlasTexture");
            public static readonly int AdditionalLightsShadowmapTexture = Shader.PropertyToID("_AdditionalLightsShadowmapTexture");
            public static readonly int DirectionalLightsShadowmapTexture = Shader.PropertyToID("_DirectionalLightsShadowmapTexture");
            public static readonly int FogAmbientProbe = Shader.PropertyToID("_FogAmbientProbe");
            public static readonly int FogCoarseLightList = Shader.PropertyToID("_FogCoarseLightList");
            public static readonly int FogDepthInvViewProjection = Shader.PropertyToID("_FogDepthInvViewProjection");
            public static readonly int FogFeedback = Shader.PropertyToID("_FogFeedback");
            public static readonly int FogFilterRead = Shader.PropertyToID("_FogFilterRead");
            public static readonly int FogFilterWrite = Shader.PropertyToID("_FogFilterWrite");
            public static readonly int FogGrid = Shader.PropertyToID("_FogGrid");
            public static readonly int FogHistory = Shader.PropertyToID("_FogHistory");
            public static readonly int FogHistoryParams = Shader.PropertyToID("_FogHistoryParams");
            public static readonly int FogHistoryViewport = Shader.PropertyToID("_FogHistoryViewport");
            public static readonly int FogIntegralWrite = Shader.PropertyToID("_FogIntegralWrite");
            public static readonly int FogInvViewProjection = Shader.PropertyToID("_FogInvViewProjection");
            public static readonly int FogLighting = Shader.PropertyToID("_FogLighting");
            public static readonly int FogLocalAlbedoDensity = Shader.PropertyToID("_FogLocalAlbedoDensity");
            public static readonly int FogLocalDistanceFade = Shader.PropertyToID("_FogLocalDistanceFade");
            public static readonly int FogLocalMask = Shader.PropertyToID("_FogLocalMask");
            public static readonly int FogLocalNegativeFade = Shader.PropertyToID("_FogLocalNegativeFade");
            public static readonly int FogLocalOffset = Shader.PropertyToID("_FogLocalOffset");
            public static readonly int FogLocalOptions = Shader.PropertyToID("_FogLocalOptions");
            public static readonly int FogLocalPositiveFade = Shader.PropertyToID("_FogLocalPositiveFade");
            public static readonly int FogLocalShapeNoise = Shader.PropertyToID("_FogLocalShapeNoise");
            public static readonly int FogLocalTiling = Shader.PropertyToID("_FogLocalTiling");
            public static readonly int FogLocalVelocity = Shader.PropertyToID("_FogLocalVelocity");
            public static readonly int FogLocalWorldToVolume = Shader.PropertyToID("_FogLocalWorldToVolume");
            public static readonly int FogMedium = Shader.PropertyToID("_FogMedium");
            public static readonly int FogPreviousDepthEncoding = Shader.PropertyToID("_FogPreviousDepthEncoding");
            public static readonly int FogPreviousForward = Shader.PropertyToID("_FogPreviousForward");
            public static readonly int FogPreviousPosition = Shader.PropertyToID("_FogPreviousPosition");
            public static readonly int FogPreviousViewProjection = Shader.PropertyToID("_FogPreviousViewProjection");
            public static readonly int FogMaxDepth = Shader.PropertyToID("_FogMaxDepth");
            public static readonly int FogMaxDepthRead = Shader.PropertyToID("_FogMaxDepthRead");
            public static readonly int FogDilatedDepth = Shader.PropertyToID("_FogDilatedDepth");
            public static readonly int FogDepthCulling = Shader.PropertyToID("_FogDepthCulling");
            public static readonly int FogSceneDepth = Shader.PropertyToID("_FogSceneDepth");
            public static readonly int FogSceneDepthSize = Shader.PropertyToID("_FogSceneDepthSize");
            public static readonly int FogShadowFlags = Shader.PropertyToID("_FogShadowFlags");
            public static readonly int FogViewIndex = Shader.PropertyToID("_FogViewIndex");
            public static readonly int MainLightCookieTexture = Shader.PropertyToID("_MainLightCookieTexture");
            public static readonly int g_DirectionalLightDatas = Shader.PropertyToID("g_DirectionalLightDatas");
            public static readonly int g_GPULightDatas = Shader.PropertyToID("g_GPULightDatas");
            public static readonly int g_logBaseBuffer = Shader.PropertyToID("g_logBaseBuffer");
            public static readonly int g_vLayeredOffsetsBuffer = Shader.PropertyToID("g_vLayeredOffsetsBuffer");
            public static readonly int g_vLightListCluster = Shader.PropertyToID("g_vLightListCluster");
            public static readonly int ConstantBuffer = Shader.PropertyToID("ShaderVariablesAtmosphericFog");
            public static readonly int LocalMaskSize = Shader.PropertyToID("_FogLocalMaskSize");
            public static readonly int LocalVolumeToWorld = Shader.PropertyToID("_FogLocalVolumeToWorld");
            public static readonly int LocalTime = Shader.PropertyToID("_FogLocalTime");
            public static readonly int LocalSlice = Shader.PropertyToID("_FogLocalSlice");
        }
    }
}
