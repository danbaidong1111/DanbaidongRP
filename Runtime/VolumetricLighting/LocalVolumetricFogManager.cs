using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    internal static class LocalVolumetricFogManager
    {
        internal readonly struct LocalFogView
        {
            internal readonly Plane[] planes;
            internal readonly Vector3 position, forward;
            internal readonly float far;
            internal readonly bool orthographic;

            internal LocalFogView(Matrix4x4 projection, Matrix4x4 view, float far, bool orthographic)
            {
                // The grid ends on a radial cap, so the camera's planar far clip is not a culling plane.
                Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(projection * view);
                planes = new Plane[5];
                Array.Copy(frustum, planes, planes.Length);
                Matrix4x4 inverseView = view.inverse;
                position = inverseView.GetColumn(3);
                forward = -inverseView.GetColumn(2);
                this.far = far;
                this.orthographic = orthographic;
            }
        }

        internal static bool IsLocalFogVisible(Bounds bounds, LocalFogView[] views, float distanceFadeEnd)
        {
            foreach (var view in views)
            {
                if (!GeometryUtility.TestPlanesAABB(view.planes, bounds)) continue;
                float cap = Mathf.Min(view.far, distanceFadeEnd);
                if (view.orthographic)
                {
                    Vector3 n = view.forward;
                    float extent = Mathf.Abs(n.x) * bounds.extents.x + Mathf.Abs(n.y) * bounds.extents.y + Mathf.Abs(n.z) * bounds.extents.z;
                    if (Vector3.Dot(bounds.center - view.position, n) - extent <= cap + 0.0001f) return true;
                }
                else if (bounds.SqrDistance(view.position) <= (cap + 0.0001f) * (cap + 0.0001f)) return true;
            }
            return false;
        }

        internal static void ApplyLocalFogBudget(List<LocalVolumetricFogData> volumes, int budget)
        {
            budget = Mathf.Max(0, budget);
            if (volumes.Count > budget)
            {
                volumes.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.instanceId.CompareTo(b.instanceId));
                volumes.RemoveRange(budget, volumes.Count - budget);
            }
            volumes.Sort(LocalVolumetricFog.ComparePriority);
        }

        static bool IsInCameraScene(Camera camera, GameObject volume)
        {
            if (camera.scene.IsValid() && camera.scene != volume.scene)
                return false;
#if UNITY_EDITOR
            ulong sceneMask = camera.overrideSceneCullingMask;
            if (sceneMask == 0)
            {
                sceneMask = camera.scene.IsValid()
                    ? UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(camera.scene)
                    : camera.cameraType == CameraType.SceneView
                        ? UnityEditor.SceneManagement.SceneCullingMasks.MainStageSceneViewObjects
                        : UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects;
            }
            return (sceneMask & volume.sceneCullingMask) != 0;
#else
            return true;
#endif
        }

        internal static LocalVolumetricFogData[] Capture(AtmosphericFog fog, VBufferParameters buffer, Camera camera, ref int hash)
        {
            var views = new LocalFogView[buffer.views];
            for (int eye = 0; eye < views.Length; ++eye)
                views[eye] = new LocalFogView(buffer.cullingProjections[eye], buffer.viewMatrices[eye], buffer.far, camera.orthographic);
            var localVolumes = new List<LocalVolumetricFogData>();
            var localHashes = new Dictionary<int, int>();
            foreach (var local in LocalVolumetricFog.instances)
            {
                if (local == null || !local.isActiveAndEnabled || (camera.cullingMask & (1 << local.gameObject.layer)) == 0
                    || !IsInCameraScene(camera, local.gameObject)) continue;
#if UNITY_EDITOR
                if (UnityEditor.SceneVisibilityManager.instance.IsHidden(local.gameObject)) continue;
#endif
                var captured = local.CaptureData(Time.realtimeSinceStartup);
                if (!IsLocalFogVisible(captured.bounds, views, captured.distanceFade.w)) continue;
                localVolumes.Add(captured);
                localHashes[captured.instanceId] = local.GetSettingsHash();
            }
            ApplyLocalFogBudget(localVolumes, fog.maxLocalFogVolumes.value);
            foreach (var local in localVolumes) hash = unchecked(hash * 23 + localHashes[local.instanceId]);
            return localVolumes.ToArray();
        }
    }
}
