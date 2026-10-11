using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed partial class VolumetricLightingPass
    {
        internal sealed class CameraState : IDisposable
        {
            internal int width, height, slices, views, hash, sequence, lastFrame, accumulatedFrames;
            internal bool valid;
            internal Vector3Int storageSize;
            internal float near, far;
            internal Vector4 depthEncoding;
            internal Matrix4x4[] eyeViewProjection = new Matrix4x4[2];
            internal Vector4[] eyePosition = new Vector4[2], eyeForward = new Vector4[2];
#if UNITY_EDITOR
            readonly EditorWarmup m_Warmup = new EditorWarmup();
            UnityEditor.SceneView m_SceneView;
            internal bool m_RepaintScheduled;
#endif

            public void Dispose()
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update -= RepaintSceneView;
                m_RepaintScheduled = false;
                m_SceneView = null;
                m_Warmup.Cancel();
#endif
            }
#if UNITY_EDITOR
            internal sealed class EditorWarmup
            {
                internal const int TargetFrames = 32;
                int m_Remaining;
                bool m_Requested;

                internal bool Request(bool temporal, bool historyValid, int accumulatedFrames)
                {
                    bool ownRepaint = m_Requested;
                    m_Requested = false;
                    if (!temporal)
                    {
                        m_Remaining = 0;
                        return false;
                    }
                    // A fresh external edit starts a new bounded burst. Invalidating the
                    // history during our own burst must not generate an endless repaint loop.
                    if (!historyValid && !ownRepaint) m_Remaining = TargetFrames;
                    if (accumulatedFrames >= TargetFrames || m_Remaining <= 0) return false;
                    --m_Remaining;
                    m_Requested = true;
                    return true;
                }

                internal void Cancel()
                {
                    m_Remaining = 0;
                    m_Requested = false;
                }
            }
            internal void RequestEditorRepaint(Camera camera, bool temporal, bool historyValid)
            {
                if (!m_Warmup.Request(temporal, historyValid, accumulatedFrames))
                {
                    Dispose();
                    return;
                }
                foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
                {
                    if (view == null || view.camera != camera) continue;
                    m_SceneView = view;
                    if (!m_RepaintScheduled)
                    {
                        m_RepaintScheduled = true;
                        UnityEditor.EditorApplication.update += RepaintSceneView;
                    }
                    return;
                }
                m_Warmup.Cancel();
            }

            void RepaintSceneView()
            {
                UnityEditor.EditorApplication.update -= RepaintSceneView;
                m_RepaintScheduled = false;
                if (!Application.isPlaying && m_SceneView != null)
                {
                    m_SceneView.Repaint();
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                }
                else m_Warmup.Cancel();
            }
#endif
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
            if (m_CameraStates.TryGetValue(historyRTSystem, out var state))
            {
                state.Dispose();
                m_CameraStates.Remove(historyRTSystem);
            }
        }

        void ResetHistory(HistoryFrameRTSystem historyRTSystem)
        {
            historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
            RemoveCameraState(historyRTSystem);
        }

        void DisposeCameraStates()
        {
            foreach (var entry in m_CameraStates)
            {
                entry.Key.disposed -= RemoveCameraState;
                entry.Value.Dispose();
                entry.Key.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
            }
            m_CameraStates.Clear();
        }

        internal static bool ReAllocateHistoryTexturesIfNeeded(HistoryFrameRTSystem historyRTSystem, CameraState state, VBufferParameters buffer, bool temporal = true)
        {
            var current = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.VolumetricLighting);
            var previous = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.VolumetricLighting);
            if (state.views != buffer.views)
                state.valid = false;
            state.width = buffer.width;
            state.height = buffer.height;
            state.slices = buffer.slices;
            state.views = buffer.views;
            state.storageSize = Vector3Int.Max(state.storageSize,
                new Vector3Int(state.width, state.height, Mathf.Max(state.slices, buffer.allocationSlices) * state.views));
            if (!temporal)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
                state.valid = false;
                return current != null || previous != null;
            }
            var descriptor = new RenderTextureDescriptor(state.storageSize.x, state.storageSize.y, GraphicsFormat.R16G16B16A16_SFloat, 0)
            { dimension = TextureDimension.Tex3D, volumeDepth = state.storageSize.z, enableRandomWrite = true, msaaSamples = 1 };
            if (current == null || current.rt == null || previous == null || previous.rt == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.VolumetricLighting, historyRTSystem.name, AllocateHistoryTexture, descriptor, 2);
                state.valid = false;
                return true;
            }
            if (!previous.rt.IsCreated())
            {
                previous.rt.Create();
                state.valid = false;
            }
            if (current.rt.IsCreated() && current.rt.width >= state.storageSize.x
                && current.rt.height >= state.storageSize.y && current.rt.volumeDepth >= state.storageSize.z)
                return false;
            // Only feedback may grow. The previous frame retains its contents and viewport.
            historyRTSystem.ReAllocateCurrentHistoryFrameRT(HistoryFrameType.VolumetricLighting, AllocateHistoryTexture, descriptor);
            return true;
        }

        static RTHandle AllocateHistoryTexture(RenderTextureDescriptor descriptor, string cameraName, int frame, RTHandleSystem system)
        {
            return system.Alloc(descriptor.width, descriptor.height, slices: descriptor.volumeDepth,
                dimension: TextureDimension.Tex3D, colorFormat: descriptor.graphicsFormat,
                enableRandomWrite: true, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                name: cameraName + " Volumetric Lighting " + frame);
        }

        internal static float GetHistoryWeight(int accumulatedFrames, float maximum)
        {
            return Mathf.Min(maximum, accumulatedFrames / (accumulatedFrames + 1f));
        }

        void UpdateHistory(RenderGraph graph, HistoryFrameRTSystem historyRTSystem, VolumetricLightingPass.PassData data, UniversalCameraData cameraData, AtmosphericFog fog, int settingsHash)
        {
            var state = GetCameraState(historyRTSystem);
            var camera = cameraData.camera;
            bool temporal = (fog.effectiveDenoising & FogDenoisingMode.Reprojection) != 0;
            data.previousViewport = new Vector4(state.width, state.height, state.slices, state.views);
            ReAllocateHistoryTexturesIfNeeded(historyRTSystem, state, data.buffer, temporal);
            data.temporal = temporal;
            bool historyValid = state.valid && state.lastFrame == historyRTSystem.historyFrameCount - 1
                && state.hash == settingsHash && !cameraData.resetHistory;
            if (!historyValid) state.accumulatedFrames = state.sequence = 0;
            float weight = historyValid && temporal ? (fog.isPhysical ? 6f / 7f : GetHistoryWeight(state.accumulatedFrames, fog.historyWeight.value)) : 0;
            data.historyParams = new Vector4(weight, Mathf.Max(0.1f, state.near), Mathf.Max(1, state.far), temporal ? 1 : 0);
            data.previousDepthEncoding = state.depthEncoding;
            data.previousViewProjections = state.eyeViewProjection;
            data.previousPositions = state.eyePosition;
            data.previousForwards = state.eyeForward;
            data.grid = new Vector4(state.width, state.height, state.slices, state.sequence++);
            data.storageSize = state.storageSize;
            if (temporal)
            {
                data.feedback = graph.ImportTexture(historyRTSystem.GetCurrentFrameRT(HistoryFrameType.VolumetricLighting));
                data.previous = graph.ImportTexture(historyRTSystem.GetPreviousFrameRT(HistoryFrameType.VolumetricLighting));
            }
            state.near = data.buffer.near;
            state.far = data.buffer.far;
            state.hash = settingsHash;
            state.valid = temporal;
            state.lastFrame = historyRTSystem.historyFrameCount;
            state.depthEncoding = data.buffer.depthEncoding;
            state.eyeViewProjection = data.buffer.viewProjections;
            state.eyePosition = data.buffer.positions;
            state.eyeForward = data.buffer.forwards;
            state.accumulatedFrames = Mathf.Min(state.accumulatedFrames + 1, 128);
#if UNITY_EDITOR
            if (!Application.isPlaying && cameraData.isSceneViewCamera)
                state.RequestEditorRepaint(camera, temporal, historyValid);
#endif
        }

    }
}
