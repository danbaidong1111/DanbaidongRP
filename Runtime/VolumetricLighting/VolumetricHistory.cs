using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed class VolumetricHistory : IDisposable
    {
        readonly HistoryFrameRTSystem m_System;
        int width, height, slices, views, hash, sequence, lastFrame, accumulatedFrames;
        bool valid, playing;
        Vector3Int storageSize;
        double lastRenderTime;
        Matrix4x4 projection;
        Vector3 position, forward;
        float near, far;
        Vector4 depthEncoding;
        Matrix4x4[] eyeViewProjection = new Matrix4x4[2];
        Vector4[] eyePosition = new Vector4[2], eyeForward = new Vector4[2];
#if UNITY_EDITOR
        readonly EditorWarmup m_Warmup = new EditorWarmup();
        UnityEditor.SceneView m_SceneView;
        bool m_RepaintScheduled;
#endif

        internal VolumetricHistory(HistoryFrameRTSystem system) { m_System = system; }

        internal bool Reallocate(VBufferParameters buffer, bool temporal = true)
        {
            var current = m_System.GetCurrentFrameRT(HistoryFrameType.VolumetricLighting);
            var previous = m_System.GetPreviousFrameRT(HistoryFrameType.VolumetricLighting);
            if (views != buffer.views)
                valid = false;
            width = buffer.width;
            height = buffer.height;
            slices = buffer.slices;
            views = buffer.views;
            storageSize = Vector3Int.Max(storageSize,
                new Vector3Int(width, height, Mathf.Max(slices, buffer.allocationSlices) * views));
            if (!temporal)
            {
                m_System.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
                valid = false;
                return current != null || previous != null;
            }
            var descriptor = new RenderTextureDescriptor(storageSize.x, storageSize.y, GraphicsFormat.R16G16B16A16_SFloat, 0)
            { dimension = TextureDimension.Tex3D, volumeDepth = storageSize.z, enableRandomWrite = true, msaaSamples = 1 };
            if (current == null || current.rt == null || previous == null || previous.rt == null)
            {
                m_System.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
                m_System.AllocHistoryFrameRT((int)HistoryFrameType.VolumetricLighting, m_System.name, Allocate, descriptor, 2);
                valid = false;
                return true;
            }
            if (!previous.rt.IsCreated())
            {
                previous.rt.Create();
                valid = false;
            }
            if (current.rt.IsCreated() && current.rt.width >= storageSize.x
                && current.rt.height >= storageSize.y && current.rt.volumeDepth >= storageSize.z)
                return false;
            // Only feedback may grow. The previous frame retains its contents and viewport.
            m_System.ReAllocateCurrentHistoryFrameRT(HistoryFrameType.VolumetricLighting, Allocate, descriptor);
            return true;
        }

        static RTHandle Allocate(RenderTextureDescriptor descriptor, string cameraName, int frame, RTHandleSystem system)
        {
            return system.Alloc(descriptor.width, descriptor.height, slices: descriptor.volumeDepth,
                dimension: TextureDimension.Tex3D, colorFormat: descriptor.graphicsFormat,
                enableRandomWrite: true, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                name: cameraName + " Volumetric Lighting " + frame);
        }

        internal static bool IsHistoryContinuous(bool playing, bool previousPlaying, int frame, int previousFrame, double time, double previousTime)
        {
            if (playing != previousPlaying || time < previousTime) return false;
            // Editor repaints do not correspond to the global rendered-frame counter.
            // Track this camera's elapsed time so unrelated views cannot reset its accumulation.
            return playing ? frame >= previousFrame && frame - previousFrame <= 1 : time - previousTime <= 2.0;
        }

        internal static bool IsProjectionStable(Matrix4x4 previous, Matrix4x4 current)
        {
            for (int i = 0; i < 16; ++i)
                if (Mathf.Abs(previous[i] - current[i]) > 1e-4f * Mathf.Max(1f, Mathf.Abs(current[i]))) return false;
            return true;
        }

        internal static float GetHistoryWeight(int accumulatedFrames, float maximum)
        {
            return Mathf.Min(maximum, accumulatedFrames / (accumulatedFrames + 1f));
        }

        internal void Update(RenderGraph graph, VolumetricLightingPass.PassData data, UniversalCameraData cameraData, AtmosphericFog fog, int settingsHash)
        {
            var camera = cameraData.camera;
            bool temporal = (fog.effectiveDenoising & FogDenoisingMode.Reprojection) != 0;
            data.previousViewport = new Vector4(width, height, slices, views);
            Reallocate(data.buffer, temporal);
            data.temporal = temporal;
            double now = Time.realtimeSinceStartupAsDouble;
            bool continuous = IsHistoryContinuous(Application.isPlaying, playing, Time.frameCount, lastFrame, now, lastRenderTime);
            bool historyValid = valid && continuous && hash == settingsHash && !cameraData.resetHistory
                && Vector3.Distance(position, camera.transform.position) < data.buffer.far * 0.1f
                && Vector3.Dot(forward, camera.transform.forward) > 0.9f
                && IsProjectionStable(projection, camera.nonJitteredProjectionMatrix);
            if (!historyValid) accumulatedFrames = sequence = 0;
            float weight = historyValid && temporal ? (fog.isPhysical ? 6f / 7f : GetHistoryWeight(accumulatedFrames, fog.historyWeight.value)) : 0;
            data.historyParams = new Vector4(weight, Mathf.Max(0.1f, near), Mathf.Max(1, far), temporal ? 1 : 0);
            data.previousDepthEncoding = depthEncoding;
            data.previousViewProjections = eyeViewProjection;
            data.previousPositions = eyePosition;
            data.previousForwards = eyeForward;
            data.grid = new Vector4(width, height, slices, sequence++);
            data.storageSize = storageSize;
            if (temporal)
            {
                data.feedback = graph.ImportTexture(m_System.GetCurrentFrameRT(HistoryFrameType.VolumetricLighting));
                data.previous = graph.ImportTexture(m_System.GetPreviousFrameRT(HistoryFrameType.VolumetricLighting));
            }
            projection = camera.nonJitteredProjectionMatrix;
            position = camera.transform.position;
            forward = camera.transform.forward;
            near = data.buffer.near;
            far = data.buffer.far;
            hash = settingsHash;
            valid = temporal;
            lastFrame = Time.frameCount;
            lastRenderTime = now;
            playing = Application.isPlaying;
            depthEncoding = data.buffer.depthEncoding;
            eyeViewProjection = data.buffer.viewProjections;
            eyePosition = data.buffer.positions;
            eyeForward = data.buffer.forwards;
            accumulatedFrames = Mathf.Min(accumulatedFrames + 1, 128);
#if UNITY_EDITOR
            if (!Application.isPlaying && cameraData.isSceneViewCamera)
                RequestEditorRepaint(camera, temporal, historyValid);
#endif
        }

        internal void Disable()
        {
            m_System.ReleaseHistoryFrameRT(HistoryFrameType.VolumetricLighting);
            valid = false;
            accumulatedFrames = sequence = 0;
            storageSize = Vector3Int.zero;
            Dispose();
        }

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
        void RequestEditorRepaint(Camera camera, bool temporal, bool historyValid)
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
            if (!Application.isPlaying && m_System.camera != null && m_SceneView != null)
            {
                m_SceneView.Repaint();
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
            else m_Warmup.Cancel();
        }
#endif
    }
}
