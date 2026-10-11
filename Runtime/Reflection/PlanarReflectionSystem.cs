using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    // Captures are scheduled before the source camera allocates its renderer frame data.
    internal static class PlanarReflectionSystem
    {
        internal const int maxProbeCount = 4;
        static readonly List<PlanarReflectionProbe> s_Probes = new List<PlanarReflectionProbe>();
        static readonly Dictionary<Camera, CameraState> s_Cameras = new Dictionary<Camera, CameraState>();
        static readonly List<Camera> s_DeadCameras = new List<Camera>();
        static readonly List<PlanarReflectionProbe> s_DeadProbes = new List<PlanarReflectionProbe>();
        static readonly List<PlanarReflectionProbe> s_Candidates = new List<PlanarReflectionProbe>();
        static readonly Plane[] s_FrustumPlanes = new Plane[6];
        static readonly Matrix4x4[] s_ProjectionWorldToLocal = new Matrix4x4[maxProbeCount];
        static readonly Matrix4x4[] s_WorldToClip = new Matrix4x4[maxProbeCount];
        static readonly Vector4[] s_Planes = new Vector4[maxProbeCount];
        static readonly Vector4[] s_Centers = new Vector4[maxProbeCount];
        static readonly Vector4[] s_Rights = new Vector4[maxProbeCount];
        static readonly Vector4[] s_Forwards = new Vector4[maxProbeCount];
        static readonly Vector4[] s_Parameters = new Vector4[maxProbeCount];
        static readonly Vector4[] s_Layers = new Vector4[maxProbeCount];
        static readonly Vector4[] s_SortKeys = new Vector4[maxProbeCount];
        static bool s_Rendering;
        static Camera s_RenderingCamera;
        static Capture s_RenderingCapture;
        static Vector4 s_ReflectionPlane;

        static readonly int s_CountId = Shader.PropertyToID("_PlanarReflectionCount");
        static readonly int s_ProjectionWorldToLocalId = Shader.PropertyToID("_PlanarReflectionProjectionWorldToLocal");
        static readonly int s_WorldToClipId = Shader.PropertyToID("_PlanarReflectionWorldToClip");
        static readonly int s_PlanesId = Shader.PropertyToID("_PlanarReflectionPlanes");
        static readonly int s_CentersId = Shader.PropertyToID("_PlanarReflectionCenters");
        static readonly int s_RightsId = Shader.PropertyToID("_PlanarReflectionRights");
        static readonly int s_ForwardsId = Shader.PropertyToID("_PlanarReflectionForwards");
        static readonly int s_ParametersId = Shader.PropertyToID("_PlanarReflectionParameters");
        static readonly int s_LayersId = Shader.PropertyToID("_PlanarReflectionLayers");
        static readonly int s_SortKeysId = Shader.PropertyToID("_PlanarReflectionSortKeys");
        static readonly int s_UVScaleBiasId = Shader.PropertyToID("_PlanarReflectionUVScaleBias");
        static readonly int[] s_TextureIds = {
            Shader.PropertyToID("_PlanarReflectionTexture0"), Shader.PropertyToID("_PlanarReflectionTexture1"),
            Shader.PropertyToID("_PlanarReflectionTexture2"), Shader.PropertyToID("_PlanarReflectionTexture3")
        };

        internal sealed class Capture : IDisposable
        {
            internal Camera camera;
            internal UniversalAdditionalCameraData additionalData;
            internal RenderTexture texture, filteredTexture;
            internal RenderTexture sampleTexture => filtered ? filteredTexture : texture;
            internal Matrix4x4 regularProjection;
            internal Vector4 plane;
            internal bool filtered, roughReflections;
            internal RTHandle textureHandle, fallbackHandle;
            internal Matrix4x4 worldToClip, sourceView, sourceProjection, probeTransform;
            internal int lastFrame = -1;
            internal uint lastRequest;
            internal bool valid;

            internal Capture(Camera source, PlanarReflectionProbe probe)
            {
                var gameObject = new GameObject($"Planar Reflection ({source.name}, {probe.name})", typeof(Camera), typeof(UniversalAdditionalCameraData));
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                camera = gameObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.cameraType = CameraType.Reflection;
                additionalData = gameObject.GetComponent<UniversalAdditionalCameraData>();
                additionalData.hasPersistentHistory = true;
            }

            public void Dispose()
            {
                if (camera != null)
                {
                    HistoryFrameRTSystem.Release(camera);
                    camera.targetTexture = null;
                    camera.DestroyVolumeStack(additionalData);
                    CoreUtils.Destroy(camera.gameObject);
                }
                ReleaseTexture();
            }

            internal void ReleaseTexture()
            {
                fallbackHandle?.Release();
                fallbackHandle = null;
                textureHandle?.Release();
                textureHandle = null;
                if (filteredTexture != null) { filteredTexture.Release(); CoreUtils.Destroy(filteredTexture); filteredTexture = null; }
                filtered = false;
                if (texture != null)
                {
                    texture.Release();
                    CoreUtils.Destroy(texture);
                    texture = null;
                }
                valid = false;
            }
        }

        sealed class CameraState : IDisposable
        {
            internal readonly Dictionary<PlanarReflectionProbe, Capture> captures = new Dictionary<PlanarReflectionProbe, Capture>();
            internal readonly List<PlanarReflectionProbe> selected = new List<PlanarReflectionProbe>(maxProbeCount);
            internal float lastUsed;

            public void Dispose()
            {
                foreach (Capture capture in captures.Values)
                    capture.Dispose();
                captures.Clear();
                selected.Clear();
            }
        }

        static PlanarReflectionSystem()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
#endif
            Application.quitting += Cleanup;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Cleanup();

        internal static void Register(PlanarReflectionProbe probe)
        {
            if (!s_Probes.Contains(probe))
                s_Probes.Add(probe);
        }

        internal static void Unregister(PlanarReflectionProbe probe)
        {
            s_Probes.Remove(probe);
            foreach (CameraState state in s_Cameras.Values)
            {
                if (state.captures.TryGetValue(probe, out Capture capture))
                {
                    capture.Dispose();
                    state.captures.Remove(probe);
                }
                state.selected.Remove(probe);
            }
            if (s_Probes.Count == 0)
                Cleanup();
        }

        internal static void Invalidate(PlanarReflectionProbe probe)
        {
            foreach (CameraState state in s_Cameras.Values)
                if (state.captures.TryGetValue(probe, out Capture capture))
                    capture.valid = false;
        }

        internal static RenderTexture GetTexture(PlanarReflectionProbe probe, Camera source)
        {
            if (source != null && s_Cameras.TryGetValue(source, out CameraState state)
                && state.captures.TryGetValue(probe, out Capture capture) && capture.valid)
                return capture.sampleTexture;
            return null;
        }

        // The source camera applies atmosphere on its path to the mirror. A capture starts at the mirror.
        internal static bool TryGetReflectionPlane(Camera camera, out Vector4 plane)
        {
            bool active = s_Rendering && camera != null && camera == s_RenderingCamera;
            plane = active ? s_ReflectionPlane : Vector4.zero;
            return active;
        }

        internal static bool TryGetCapture(Camera camera, out Capture capture)
        {
            capture = s_RenderingCapture;
            return s_Rendering && camera == s_RenderingCamera && capture != null && capture.filteredTexture != null;
        }
        internal static void Cleanup()
        {
            foreach (CameraState state in s_Cameras.Values)
                state.Dispose();
            s_Cameras.Clear();
            s_Candidates.Clear();
            s_Probes.RemoveAll(probe => probe == null);
            Shader.SetGlobalInt(s_CountId, 0);
            for (int i = 0; i < maxProbeCount; ++i)
                Shader.SetGlobalTexture(s_TextureIds[i], Texture2D.blackTexture);
            s_Rendering = false;
            s_RenderingCamera = null;
            s_RenderingCapture = null;
            s_ReflectionPlane = Vector4.zero;
        }

        static void PruneUnusedCameras()
        {
            s_DeadCameras.Clear();
            foreach (var pair in s_Cameras)
                if (pair.Key == null || Time.realtimeSinceStartup - pair.Value.lastUsed > 5f)
                    s_DeadCameras.Add(pair.Key);
            foreach (Camera camera in s_DeadCameras)
            {
                s_Cameras[camera].Dispose();
                s_Cameras.Remove(camera);
            }
        }

#if UNITY_EDITOR
        internal static ulong GetSceneCullingMask(Camera camera)
        {
            if (camera.overrideSceneCullingMask != 0)
                return camera.overrideSceneCullingMask;
            if (camera.scene.IsValid())
                return UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.scene);
            return camera.cameraType == CameraType.SceneView
                ? UnityEditor.SceneManagement.SceneCullingMasks.MainStageSceneViewObjects
                : UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects;
        }
#endif

        internal static void RenderForCamera(ScriptableRenderContext context, Camera source)
        {
            if (s_Rendering || source == null || source.cameraType == CameraType.Reflection || source.cameraType == CameraType.Preview)
                return;
            PruneUnusedCameras();
            if (s_Probes.Count == 0 || source.stereoEnabled)
                return;
            source.TryGetComponent(out UniversalAdditionalCameraData sourceData);
            if (sourceData != null && sourceData.renderType == CameraRenderType.Overlay)
                return;

            if (!s_Cameras.TryGetValue(source, out CameraState state))
            {
                state = new CameraState();
                s_Cameras.Add(source, state);
            }
            state.lastUsed = Time.realtimeSinceStartup;
            state.selected.Clear();
            GeometryUtility.CalculateFrustumPlanes(source, s_FrustumPlanes);
            s_Candidates.Clear();
#if UNITY_EDITOR
            ulong sceneCullingMask = GetSceneCullingMask(source);
#endif
            foreach (PlanarReflectionProbe probe in s_Probes)
            {
                if (probe == null || !probe.isActiveAndEnabled || probe.intensity <= 0f || probe.weight <= 0f)
                    continue;
#if UNITY_EDITOR
                if ((probe.gameObject.sceneCullingMask & sceneCullingMask) == 0)
                    continue;
#endif
                if (source.cameraType == CameraType.SceneView && !probe.renderInSceneView)
                    continue;
                if (!probe.twoSided && Vector3.Dot(source.transform.position - probe.planePosition, probe.planeNormal) <= 0.001f)
                    continue;
                Bounds bounds = PlanarReflectionMath.InfluenceBounds(probe);
                if (!GeometryUtility.TestPlanesAABB(s_FrustumPlanes, bounds))
                    continue;
                if (probe.maxCaptureDistance > 0f && bounds.SqrDistance(source.transform.position) > probe.maxCaptureDistance * probe.maxCaptureDistance)
                    continue;
                s_Candidates.Add(probe);
            }
            s_Candidates.Sort((a, b) => {
                int priority = b.priority.CompareTo(a.priority);
                if (priority != 0) return priority;
                int volume = InfluenceVolume(a).CompareTo(InfluenceVolume(b));
                return volume != 0 ? volume : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });

            // Release captures that no longer contribute instead of keeping one texture per visited probe.
            s_DeadProbes.Clear();
            foreach (PlanarReflectionProbe probe in state.captures.Keys)
                if (s_Candidates.IndexOf(probe) < 0 || s_Candidates.IndexOf(probe) >= maxProbeCount)
                    s_DeadProbes.Add(probe);
            foreach (PlanarReflectionProbe probe in s_DeadProbes)
            {
                state.captures[probe].Dispose();
                state.captures.Remove(probe);
            }

            bool previousInvertCulling = GL.invertCulling;
            s_Rendering = true;
            try
            {
                int count = Mathf.Min(s_Candidates.Count, maxProbeCount);
                for (int i = 0; i < count; ++i)
                {
                    PlanarReflectionProbe probe = s_Candidates[i];
                    if (!state.captures.TryGetValue(probe, out Capture capture))
                    {
                        capture = new Capture(source, probe);
                        state.captures.Add(probe, capture);
                    }
                    RenderCapture(context, source, sourceData, probe, capture, previousInvertCulling);
                    if (capture.valid)
                        state.selected.Add(probe);
                }
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
                s_Rendering = false;
            }
        }

        static void RenderCapture(ScriptableRenderContext context, Camera source, UniversalAdditionalCameraData sourceData,
            PlanarReflectionProbe probe, Capture capture, bool previousInvertCulling)
        {
            Vector2Int size = PlanarReflectionMath.TargetSize(source.pixelWidth, source.pixelHeight, probe.resolutionScale, probe.maxResolution);
            Matrix4x4 projection = source.nonJitteredProjectionMatrix;
            bool moved = capture.sourceView != source.worldToCameraMatrix || capture.sourceProjection != projection
                || capture.probeTransform != probe.transform.localToWorldMatrix || capture.texture == null || capture.texture.width != size.x || capture.texture.height != size.y;
            if (!PlanarReflectionMath.ShouldCapture(probe.refreshMode, capture.valid, moved, probe.renderRequest, capture.lastRequest,
                Time.frameCount - capture.lastFrame, probe.updateInterval, Application.isPlaying)) return;
            if (capture.texture == null || capture.texture.width != size.x || capture.texture.height != size.y || capture.roughReflections != probe.roughReflections)
            {
                capture.ReleaseTexture();
                GraphicsFormat format = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R16G16B16A16_SFloat : SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
                var descriptor = new RenderTextureDescriptor(size.x, size.y, format, 24) {
                    msaaSamples = 1, useMipMap = probe.roughReflections, autoGenerateMips = false, mipCount = probe.roughReflections ? 0 : 1,
                    useDynamicScale = false
                };
                capture.texture = new RenderTexture(descriptor) {
                    name = $"Planar Reflection ({source.name}, {probe.name})", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                capture.texture.Create();
                descriptor.depthStencilFormat = GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.enableRandomWrite = true;
                descriptor.mipCount = probe.roughReflections ? Mathf.Min(7, 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Max(size.x, size.y), 2))) : 1;
                capture.filteredTexture = new RenderTexture(descriptor) { name = "Filtered " + capture.texture.name, hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                capture.filteredTexture.Create();
                capture.textureHandle = RTHandles.Alloc(capture.filteredTexture);
            }

            capture.valid = false;
            Camera camera = capture.camera;
            camera.CopyFrom(source);
            camera.scene = source.scene;
#if UNITY_EDITOR
            camera.overrideSceneCullingMask = GetSceneCullingMask(source);
#endif
            camera.enabled = false;
            camera.cameraType = CameraType.Reflection;
            camera.targetTexture = capture.texture;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            camera.cullingMask = source.cullingMask & probe.cullingMask;
            camera.farClipPlane = Mathf.Max(source.nearClipPlane + 0.01f, Mathf.Min(source.farClipPlane, probe.farClipPlane));
            camera.clearFlags = source.clearFlags == CameraClearFlags.Nothing || source.clearFlags == CameraClearFlags.Depth
                ? CameraClearFlags.Skybox : source.clearFlags;

            Matrix4x4 reflection = PlanarReflectionMath.ReflectionMatrix(probe.planePosition, probe.planeNormal);
            Vector3 reflectedPosition = reflection.MultiplyPoint(source.transform.position);
            Vector3 reflectedForward = reflection.MultiplyVector(source.transform.forward);
            Vector3 reflectedUp = reflection.MultiplyVector(source.transform.up);
            camera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));
            camera.worldToCameraMatrix = source.worldToCameraMatrix * reflection;
            // Preserve asymmetric and physical-camera projection while limiting the capture's far plane.
            if (Mathf.Abs(projection.m32) > 0.5f)
            {
                float n = source.nearClipPlane;
                float f = camera.farClipPlane;
                projection.m22 = -(f + n) / (f - n);
                projection.m23 = -(2f * f * n) / (f - n);
            }
            else
            {
                float n = source.nearClipPlane;
                float f = camera.farClipPlane;
                projection.m22 = -2f / (f - n);
                projection.m23 = -(f + n) / (f - n);
            }
            capture.regularProjection = projection;
            capture.roughReflections = probe.roughReflections;
            if (!camera.orthographic && (probe.fieldOfViewMode != PlanarReflectionFieldOfViewMode.Viewer || !Mathf.Approximately(probe.fieldOfViewScale, 1)))
            {
                camera.fieldOfView = PlanarReflectionMath.CaptureFieldOfView(probe, source);
                projection = Matrix4x4.Perspective(camera.fieldOfView, source.aspect, camera.nearClipPlane, camera.farClipPlane);
                capture.regularProjection = projection;
            }
            camera.projectionMatrix = projection;
            Vector3 clippingNormal = probe.planeNormal;
            if (Vector3.Dot(source.transform.position - probe.planePosition, clippingNormal) < 0f)
                clippingNormal = -clippingNormal;
            Vector4 clipPlane = PlanarReflectionMath.CameraSpacePlane(camera.worldToCameraMatrix,
                probe.planePosition, clippingNormal, probe.clipPlaneOffset);
            camera.projectionMatrix = camera.CalculateObliqueMatrix(clipPlane);
            camera.nonJitteredProjectionMatrix = camera.projectionMatrix;
            camera.cullingMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;

            UniversalAdditionalCameraData data = capture.additionalData;
            data.renderType = CameraRenderType.Base;
            data.SetRenderer(probe.rendererIndex);
            data.renderPostProcessing = false;
            data.renderShadows = probe.renderShadows;
            data.antialiasing = AntialiasingMode.None;
            data.allowXRRendering = false;
            data.allowHDROutput = false;
            data.dithering = false;
            data.stopNaN = false;
            data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            data.requiresDepthOption = CameraOverrideOption.On;
            data.volumeLayerMask = sourceData != null ? sourceData.volumeLayerMask : (LayerMask)1;
            data.volumeTrigger = sourceData != null && sourceData.volumeTrigger != null ? sourceData.volumeTrigger : source.transform;
            data.volumeFrameworkUpdateMode = VolumeFrameworkUpdateMode.EveryFrame;

            s_RenderingCamera = camera;
            s_ReflectionPlane = new Vector4(clippingNormal.x, clippingNormal.y, clippingNormal.z,
                -Vector3.Dot(clippingNormal, probe.planePosition));
            capture.plane = s_ReflectionPlane;
            capture.filtered = false;
            s_RenderingCapture = capture;
            GL.invertCulling = !previousInvertCulling;
            try
            {
                UniversalRenderPipeline.RenderSingleCameraInternal(context, camera, ref data);
                if (!capture.filtered && capture.texture.useMipMap) capture.texture.GenerateMips();
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
                s_RenderingCamera = null;
                s_RenderingCapture = null;
                s_ReflectionPlane = Vector4.zero;
            }
            capture.worldToClip = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            capture.sourceView = source.worldToCameraMatrix;
            capture.sourceProjection = source.nonJitteredProjectionMatrix;
            capture.probeTransform = probe.transform.localToWorldMatrix;
            capture.lastFrame = Time.frameCount;
            capture.lastRequest = probe.renderRequest;
            capture.valid = true;
        }

        static RTHandle GetFallbackHandle(Capture capture) => capture.fallbackHandle ?? (capture.fallbackHandle = RTHandles.Alloc(capture.texture));

        internal static void ImportTextures(RenderGraph graph, Camera source, TextureHandle[] textures)
        {
            int count = 0;
            if (!s_Rendering && source != null && source.cameraType != CameraType.Reflection && !source.stereoEnabled
                && s_Cameras.TryGetValue(source, out CameraState state))
            {
                foreach (PlanarReflectionProbe probe in state.selected)
                {
                    if (probe == null || !probe.isActiveAndEnabled || !state.captures.TryGetValue(probe, out Capture capture) || !capture.valid)
                        continue;
                    var info = new RenderTargetInfo {
                        width = capture.texture.width,
                        height = capture.texture.height,
                        volumeDepth = 1,
                        msaaSamples = 1,
                        format = capture.texture.graphicsFormat
                    };
                    textures[count++] = capture.filtered ? graph.ImportTexture(capture.textureHandle) : graph.ImportTexture(GetFallbackHandle(capture), info);
                }
            }
            for (int i = count; i < maxProbeCount; ++i)
                textures[i] = graph.defaultResources.blackTexture;
        }

        internal static void BindComputeTextures(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, TextureHandle[] textures)
        {
            for (int i = 0; i < maxProbeCount; ++i)
                cmd.SetComputeTextureParam(shader, kernel, s_TextureIds[i], textures[i]);
        }

        static float InfluenceVolume(PlanarReflectionProbe probe)
        {
            Vector3 size = PlanarReflectionMath.InfluenceBounds(probe).size;
            return size.x * size.y * size.z;
        }

        internal static void BindGlobals(CommandBuffer cmd, Camera source)
        {
            int count = 0;
            if (!s_Rendering && source != null && source.cameraType != CameraType.Reflection && !source.stereoEnabled
                && s_Cameras.TryGetValue(source, out CameraState state))
            {
                foreach (PlanarReflectionProbe probe in state.selected)
                {
                    if (probe == null || !probe.isActiveAndEnabled || !state.captures.TryGetValue(probe, out Capture capture) || !capture.valid)
                        continue;
                    Vector3 normal = probe.planeNormal;
                    Vector3 center = probe.planePosition;
                    Vector3 right = probe.transform.right;
                    Vector3 forward = probe.transform.forward;
                    Vector2 size = probe.halfSize;
                    s_WorldToClip[count] = capture.worldToClip;
                    Vector3 scale = probe.projection == PlanarReflectionProjection.Sphere ? Vector3.one * probe.projectionSphereRadius : probe.projectionVolumeSize * .5f;
                    scale = Vector3.Scale(Vector3.Max(scale, Vector3.one * .001f), new Vector3(Mathf.Abs(probe.transform.lossyScale.x), Mathf.Abs(probe.transform.lossyScale.y), Mathf.Abs(probe.transform.lossyScale.z)));
                    s_ProjectionWorldToLocal[count] = Matrix4x4.TRS(probe.transform.TransformPoint(probe.projectionVolumeCenter), probe.transform.rotation, Vector3.Max(scale, Vector3.one * .001f)).inverse;
                    s_Planes[count] = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, center));
                    s_Centers[count] = new Vector4(center.x, center.y, center.z, Mathf.Max(0.001f, probe.surfaceThickness));
                    s_Rights[count] = new Vector4(right.x, right.y, right.z, size.x);
                    s_Forwards[count] = new Vector4(forward.x, forward.y, forward.z, size.y);
                    s_Parameters[count] = new Vector4(probe.intensity, probe.blendDistance, probe.normalThreshold, probe.roughReflections ? Mathf.Min(6, capture.sampleTexture.mipmapCount - 1) : 0);
                    uint layers = probe.renderingLayerMask;
                    float fade = probe.fadeDistance > 0 ? Mathf.Clamp01((probe.fadeDistance - Vector3.Distance(source.transform.position, center)) / Mathf.Max(.001f, .1f * probe.fadeDistance)) : 1f;
                    s_Layers[count] = new Vector4(layers & 0xffff, layers >> 16, probe.weight * fade, (int)probe.projection);
                    s_SortKeys[count] = new Vector4(probe.priority, InfluenceVolume(probe), 0, 0);
                    cmd.SetGlobalTexture(s_TextureIds[count], capture.sampleTexture);
                    ++count;
                }
            }
            cmd.SetGlobalInt(s_CountId, count);
            for (int i = count; i < maxProbeCount; ++i)
                cmd.SetGlobalTexture(s_TextureIds[i], Texture2D.blackTexture);
            if (count == 0)
                return;
            cmd.SetGlobalMatrixArray(s_WorldToClipId, s_WorldToClip);
            cmd.SetGlobalMatrixArray(s_ProjectionWorldToLocalId, s_ProjectionWorldToLocal);
            cmd.SetGlobalVectorArray(s_PlanesId, s_Planes);
            cmd.SetGlobalVectorArray(s_CentersId, s_Centers);
            cmd.SetGlobalVectorArray(s_RightsId, s_Rights);
            cmd.SetGlobalVectorArray(s_ForwardsId, s_Forwards);
            cmd.SetGlobalVectorArray(s_ParametersId, s_Parameters);
            cmd.SetGlobalVectorArray(s_LayersId, s_Layers);
            cmd.SetGlobalVectorArray(s_SortKeysId, s_SortKeys);
            cmd.SetGlobalVector(s_UVScaleBiasId, SystemInfo.graphicsUVStartsAtTop
                ? new Vector4(0.5f, -0.5f, 0.5f, 0.5f) : new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}
