using System;
using static UnityEngine.Rendering.Universal.DepthOfFieldParameters;
using Settings = UnityEngine.Rendering.Universal.DepthOfFieldParameters.Settings;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldCompatibility : IDisposable
    {
        internal enum Target { Input, CoC, StableCoC, Near0, Near1, NearCoC, NearAlpha, Dilated0, Dilated1,
            Far0, Far1, FarCoC, Tile0, Tile1, Spatial, Pyramid, Low, Resolved, Depth,
            MipColor0, MipColor1, MipColor2, MipColor3, MipScalar0, MipScalar1, MipScalar2, MipScalar3, Output, Count }
        internal const int TargetCount = (int)Target.Count;
        internal static RenderTextureDescriptor Descriptor(RenderTextureDescriptor original, Settings s, Target target)
        {
            original.msaaSamples = 1; original.bindMS = false;
            original.depthStencilFormat = GraphicsFormat.None; original.depthBufferBits = 0;
            original.memoryless = RenderTextureMemoryless.None;
            original.useDynamicScale = false; original.enableRandomWrite = true;
            original.useMipMap = target == Target.Far0 || target == Target.FarCoC || target == Target.Pyramid;
            original.autoGenerateMips = false; original.mipCount = original.useMipMap ? 0 : 1;
            bool low = target >= Target.Near0 && target <= Target.FarCoC || target == Target.Low;
            original.width = low ? s.blurWidth : s.width; original.height = low ? s.blurHeight : s.height;
            if (target == Target.Tile0 || target == Target.Tile1) { original.width = s.tileWidth; original.height = s.tileHeight; }
            if (target >= Target.MipColor0 && target <= Target.MipScalar3)
            {
                int mip = ((int)target - (int)Target.MipColor0) % 4 + 1;
                original.width = Mathf.Max(1, (s.physicalAlgorithm ? s.width : s.blurWidth) >> mip);
                original.height = Mathf.Max(1, (s.physicalAlgorithm ? s.height : s.blurHeight) >> mip);
            }
            bool scalar = target == Target.CoC || target == Target.StableCoC || target == Target.NearCoC || target == Target.NearAlpha
                || target == Target.Dilated0 || target == Target.Dilated1 || target == Target.FarCoC
                || target >= Target.MipScalar0 && target <= Target.MipScalar3;
            original.graphicsFormat = target == Target.Depth ? GraphicsFormat.R32_SFloat : scalar ? GraphicsFormat.R16_SFloat : GraphicsFormat.R16G16B16A16_SFloat;
            return original;
        }
        internal static bool Needed(Target target, Settings s)
        {
            if (target == Target.Output) return QualitySettings.activeColorSpace == ColorSpace.Gamma;
            if (s.physicalAlgorithm) return !(target >= Target.Near0 && target <= Target.FarCoC) && target < Target.MipScalar0;
            return !(target >= Target.Tile0 && target <= Target.Tile1) && target != Target.Pyramid && target != Target.Low;
        }
        internal sealed class Work
        {
            internal Settings settings;
            internal ComputeShader[] shaders;
            internal readonly RTHandle[] targets = new RTHandle[TargetCount];
            internal readonly GraphicsBuffer[] buffers = new GraphicsBuffer[6];
            internal RTHandle source, depth, motion, previousCoC, previousColor, previousVelocity, previousDepth, currentVelocity, exposureCurrent, exposurePrevious;
            internal bool historyValid, temporal, exposure, array;
            internal int views, frame;
            internal Vector4 sourceScale, depthSize;
        }
        internal static BufferDesc BufferDescriptor(int i, Settings s)
        {
            int tiles = ((s.blurWidth + 7) / 8) * ((s.blurHeight + 7) / 8);
            if (i == 0 || i == 1) { int axis = (int)(i == 0 ? s.counts.x : s.counts.y); return new BufferDesc(axis * axis, 4) { name = "Aperture Samples" }; }
            if (i == 2) return new BufferDesc(6, 4, GraphicsBuffer.Target.IndirectArguments) { name = "Depth of Field Dispatch" };
            if (i == 3 || i == 4) return new BufferDesc(tiles, 4, GraphicsBuffer.Target.Append) { name = "Depth of Field Active Tiles" };
            return new BufferDesc(256, 8) { name = "Aperture Shape" };
        }
        readonly ComputeShader[] m_Shaders;
        readonly RTHandle[] m_Textures = new RTHandle[TargetCount];
        readonly GraphicsBuffer[] m_Buffers = new GraphicsBuffer[6];
        readonly DepthOfFieldPass m_Owner;
        internal DepthOfFieldCompatibility(ComputeShader[] shaders, DepthOfFieldPass owner)
        {
            m_Shaders = shaders;
            m_Owner = owner;
        }
        internal void Render(CommandBuffer cmd, UniversalCameraData cameraData, RenderTextureDescriptor descriptor, DepthOfField volume,
            RTHandle source, RTHandle destination, RTHandle depth, RTHandle motion)
        {
            RenderStandalone(cmd, cameraData.camera, descriptor, volume, source, destination, depth, motion,
                cameraData.IsTemporalAAEnabled() ? DepthOfFieldParameters.CalculateTextureDepthJitter(cameraData) : Vector2.zero, cameraData);
        }
        internal void Render(CommandBuffer cmd, UniversalCameraData cameraData, DepthOfField volume,
            RTHandle source, RTHandle destination, RTHandle depth, RTHandle motion)
            => Render(cmd, cameraData, cameraData.cameraTargetDescriptor, volume, source, destination, depth, motion);
        internal void Render(CommandBuffer cmd, Camera camera, RenderTextureDescriptor descriptor, DepthOfField volume, Material material,
            RTHandle source, RTHandle destination, RTHandle depth, Vector2 depthJitter = default)
            => RenderStandalone(cmd, camera, descriptor, volume, source, destination, depth, null, depthJitter);

        void RenderStandalone(CommandBuffer cmd, Camera camera, RenderTextureDescriptor descriptor, DepthOfField volume,
            RTHandle source, RTHandle destination, RTHandle depth, RTHandle motion, Vector2 jitter, UniversalCameraData cameraData = null)
        {
            var s = PrepareSettings(volume, camera, descriptor.width, descriptor.height);
            s.alpha = cameraData == null || cameraData.isAlphaOutputEnabled;
            if (cameraData != null) s.temporal.w = cameraData.taaSettings.baseBlendFactor;
            bool jittered = cameraData != null && cameraData.IsTemporalAAEnabled() && motion != null;
            bool temporal = jittered && s.physicalAlgorithm && !s.debug && !s.debugTiles;
            s.depthJitter = jitter; s.stabilize = s.stabilize && jittered;
            var w = new Work { settings = s, shaders = m_Shaders, source = source, depth = depth, motion = motion ?? depth,
                frame = Time.frameCount, array = descriptor.dimension == TextureDimension.Tex2DArray,
                views = descriptor.dimension == TextureDimension.Tex2DArray ? descriptor.volumeDepth : 1,
                depthSize = new Vector4(descriptor.width, descriptor.height, 1f / descriptor.width, 1f / descriptor.height),
                sourceScale = new Vector4(source.useScaling ? source.rtHandleProperties.rtHandleScale.x : 1,
                    source.useScaling ? source.rtHandleProperties.rtHandleScale.y : 1,
                    depth.useScaling ? depth.rtHandleProperties.rtHandleScale.x : 1,
                    depth.useScaling ? depth.rtHandleProperties.rtHandleScale.y : 1) };
            for (int i = 0; i < TargetCount; ++i)
            {
                if (!Needed((Target)i, s)) continue;
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_Textures[i], Descriptor(descriptor, s, (Target)i), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "Depth of Field " + (Target)i);
                w.targets[i] = m_Textures[i];
            }
            for (int i = 0; i < m_Buffers.Length; ++i)
            {
                var desc = BufferDescriptor(i, s);
                if (m_Buffers[i] == null || m_Buffers[i].count != desc.count)
                { m_Buffers[i]?.Dispose(); m_Buffers[i] = new GraphicsBuffer(desc.target, desc.count, desc.stride); }
                w.buffers[i] = m_Buffers[i];
            }
            w.previousCoC = w.targets[(int)Target.CoC]; w.previousDepth = depth;
            DepthOfFieldPass.CameraState cameraState = null;
            int eye = Mathf.Clamp(cameraData?.xr?.multipassId ?? 0, 0, 1);
            if ((s.stabilize || temporal) && cameraData != null)
            {
                var system = HistoryFrameRTSystem.GetOrCreate(cameraData.camera, eye);
                w.frame = system.historyFrameCount;
                w.historyValid = m_Owner.PrepareHistory(system, cameraData, descriptor,
                    DepthOfFieldParameters.SettingsKey(s, camera), temporal, out cameraState);
                w.temporal = temporal;
                w.targets[(int)Target.StableCoC] = system.GetCurrentFrameRT(HistoryFrameType.DepthOfFieldCoC);
                w.targets[(int)Target.Resolved] = system.GetCurrentFrameRT(HistoryFrameType.DepthOfFieldColor);
                w.targets[(int)Target.Depth] = system.GetCurrentFrameRT(HistoryFrameType.DepthOfFieldDepth);
                w.previousCoC = system.GetPreviousFrameRT(HistoryFrameType.DepthOfFieldCoC);
                w.previousColor = system.GetPreviousFrameRT(HistoryFrameType.DepthOfFieldColor);
                w.previousDepth = system.GetPreviousFrameRT(HistoryFrameType.DepthOfFieldDepth);
                w.previousVelocity = system.GetPreviousFrameRT(HistoryFrameType.DepthOfFieldVelocity);
                w.currentVelocity = system.GetCurrentFrameRT(HistoryFrameType.DepthOfFieldVelocity);
                w.exposureCurrent = system.GetCurrentFrameRT(HistoryFrameType.Exposure);
                w.exposurePrevious = system.GetPreviousFrameRT(HistoryFrameType.Exposure);
                w.exposure = w.exposureCurrent != null && w.exposurePrevious != null
                    && VolumeManager.instance.stack.GetComponent<Exposure>().isAutomatic;
            }
            else if (cameraData != null)
                m_Owner.ResetHistory(HistoryFrameRTSystem.GetOrCreate(cameraData.camera, eye));
            Execute(cmd, w);
            cameraState?.Commit(w.frame);
            Blitter.BlitCameraTexture(cmd, w.targets[(int)OutputTarget(w)], destination);
        }
        public void Dispose()
        {
            foreach (var rt in m_Textures) rt?.Release();
            foreach (var buffer in m_Buffers) buffer?.Dispose();
        }
    }
}
