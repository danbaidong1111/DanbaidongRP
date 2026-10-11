using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class DepthOfFieldPass
    {
        internal sealed class CameraState
        {
            internal Hash128 descriptor, settings;
            internal bool colorHistory, valid, previousWasValid;
            internal int lastFrame = -1, accumulatedFrames;

            internal void Commit(int frame)
            {
                lastFrame = frame;
                valid = true;
                accumulatedFrames = previousWasValid ? accumulatedFrames + 1 : 1;
            }
        }

        readonly Dictionary<HistoryFrameRTSystem, CameraState> m_CameraStates = new Dictionary<HistoryFrameRTSystem, CameraState>();

        CameraState GetCameraState(HistoryFrameRTSystem historyRTSystem)
        {
            if (!m_CameraStates.TryGetValue(historyRTSystem, out var state))
            {
                state = new CameraState();
                m_CameraStates.Add(historyRTSystem, state);
                historyRTSystem.disposed += RemoveCameraState;
            }
            return state;
        }

        void RemoveCameraState(HistoryFrameRTSystem historyRTSystem)
        {
            historyRTSystem.disposed -= RemoveCameraState;
            m_CameraStates.Remove(historyRTSystem);
        }

        static void ReleaseHistoryTextures(HistoryFrameRTSystem historyRTSystem)
        {
            historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.DepthOfFieldCoC);
            historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.DepthOfFieldColor);
            historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.DepthOfFieldVelocity);
            historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.DepthOfFieldDepth);
        }

        internal void ResetHistory(HistoryFrameRTSystem historyRTSystem)
        {
            ReleaseHistoryTextures(historyRTSystem);
            RemoveCameraState(historyRTSystem);
        }

        void DisposeCameraStates()
        {
            foreach (var entry in m_CameraStates)
            {
                entry.Key.disposed -= RemoveCameraState;
                ReleaseHistoryTextures(entry.Key);
            }
            m_CameraStates.Clear();
        }

        static RTHandle AllocateHistoryTexture(RenderTextureDescriptor descriptor, string name, int frame, RTHandleSystem system)
        {
            return system.Alloc(descriptor.width, descriptor.height, slices: descriptor.volumeDepth, dimension: descriptor.dimension,
                colorFormat: descriptor.graphicsFormat, enableRandomWrite: true, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: name + " " + frame);
        }

        static void ReAllocateHistoryTexturesIfNeeded(HistoryFrameRTSystem historyRTSystem, CameraState state,
            RenderTextureDescriptor descriptor, bool colorHistory)
        {
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = 0;
            descriptor.memoryless = RenderTextureMemoryless.None;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = descriptor.autoGenerateMips = descriptor.useDynamicScale = false;
            descriptor.mipCount = 1;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            var descriptorKey = Hash128.Compute(ref descriptor);
            if (historyRTSystem.GetCurrentFrameRT(HistoryFrameType.DepthOfFieldCoC) != null
                && state.descriptor == descriptorKey && state.colorHistory == colorHistory)
                return;

            ReleaseHistoryTextures(historyRTSystem);
            descriptor.graphicsFormat = GraphicsFormat.R16_SFloat;
            historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.DepthOfFieldCoC, "Depth of Field CoC", AllocateHistoryTexture, descriptor, 2);
            descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.DepthOfFieldDepth, "Depth of Field Depth", AllocateHistoryTexture, descriptor, 2);
            if (colorHistory)
            {
                descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.DepthOfFieldColor, "Depth of Field Color", AllocateHistoryTexture, descriptor, 2);
                descriptor.graphicsFormat = GraphicsFormat.R16_SFloat;
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.DepthOfFieldVelocity, "Depth of Field Velocity", AllocateHistoryTexture, descriptor, 2);
            }
            state.descriptor = descriptorKey;
            state.colorHistory = colorHistory;
            state.valid = state.previousWasValid = false;
            state.accumulatedFrames = 0;
        }

        internal bool PrepareHistory(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData,
            RenderTextureDescriptor descriptor, Hash128 settings, bool colorHistory, out CameraState state)
        {
            state = GetCameraState(historyRTSystem);
            ReAllocateHistoryTexturesIfNeeded(historyRTSystem, state, descriptor, colorHistory);
            bool valid = state.valid && state.lastFrame == historyRTSystem.historyFrameCount - 1
                && state.settings == settings && !cameraData.resetHistory;
            state.settings = settings;
            state.valid = false;
            state.previousWasValid = valid;
            return valid;
        }
    }
}
