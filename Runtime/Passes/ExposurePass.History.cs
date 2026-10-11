using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal.Internal
{
    internal sealed partial class ExposurePass
    {
        internal sealed class CameraState
        {
            bool m_Valid;
            int m_LastFrame = -1, m_Mode = -1;
            Texture2D m_Curve;
            RTHandle m_CurveHandle, m_MaskHandle;
            Texture m_Mask;
            readonly Color[] m_CurvePixels = new Color[128];
            readonly CurveState m_CurveState = new CurveState();
            readonly CurveState m_MinCurveState = new CurveState();
            readonly CurveState m_MaxCurveState = new CurveState();

            sealed class CurveState
            {
                Keyframe[] m_Keys = System.Array.Empty<Keyframe>();
                WrapMode m_PreWrap, m_PostWrap;
                bool m_Initialized, m_IsNull;

                internal bool Update(AnimationCurve curve)
                {
                    int count = curve?.length ?? 0;
                    bool changed = !m_Initialized || m_IsNull != (curve == null) || m_Keys.Length != count
                        || (curve != null && (m_PreWrap != curve.preWrapMode || m_PostWrap != curve.postWrapMode));
                    if (m_Keys.Length != count) m_Keys = new Keyframe[count];
                    for (int i = 0; i < count; ++i)
                    {
                        Keyframe key = curve[i];
                        var previous = m_Keys[i];
                        changed |= previous.time != key.time || previous.value != key.value
                            || previous.inTangent != key.inTangent || previous.outTangent != key.outTangent
                            || previous.inWeight != key.inWeight || previous.outWeight != key.outWeight
                            || previous.weightedMode != key.weightedMode;
                        m_Keys[i] = key;
                    }
                    m_Initialized = true;
                    m_IsNull = curve == null;
                    if (curve != null) { m_PreWrap = curve.preWrapMode; m_PostWrap = curve.postWrapMode; }
                    return changed;
                }
            }

            internal void Invalidate() => m_Valid = false;

            internal bool Prepare(UniversalCameraData data, int frame, int mode)
            {
                bool valid = m_Valid && m_LastFrame == frame - 1 && m_Mode == mode && !data.resetHistory;
                m_Mode = mode;
                m_Valid = false;
                return valid;
            }
            internal void Commit(int frame)
            {
                m_LastFrame = frame;
                m_Valid = true;
            }
            internal RTHandle GetCurve(Exposure settings, out Vector2 range)
            {
                var curve = settings.curveMap.value;
                range = curve != null && curve.length > 0 ? new Vector2(curve[0].time, curve[curve.length - 1].time) : new Vector2(-10, 20);
                range.y = Mathf.Max(range.y, range.x + .001f);
                bool changed = m_CurveState.Update(curve) | m_MinCurveState.Update(settings.limitMinCurveMap.value)
                    | m_MaxCurveState.Update(settings.limitMaxCurveMap.value);
                if (m_Curve == null)
                {
                    m_Curve = new Texture2D(128, 1, TextureFormat.RGBAFloat, false, true)
                    { name = "Exposure Curves", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    m_CurveHandle = RTHandles.Alloc(m_Curve);
                    changed = true;
                }
                if (!changed) return m_CurveHandle;
                for (int i = 0; i < m_CurvePixels.Length; ++i)
                {
                    float ev = Mathf.Lerp(range.x, range.y, i / 127f);
                    var min = settings.limitMinCurveMap.value;
                    var max = settings.limitMaxCurveMap.value;
                    m_CurvePixels[i] = new Color(curve != null && curve.length > 0 ? curve.Evaluate(ev) : ev,
                        min != null && min.length > 0 ? min.Evaluate(ev) : -100f,
                        max != null && max.length > 0 ? max.Evaluate(ev) : 100f, 0);
                }
                m_Curve.SetPixels(m_CurvePixels);
                m_Curve.Apply(false, false);
                return m_CurveHandle;
            }
            internal RTHandle GetMask(Texture texture)
            {
                texture = texture != null ? texture : Texture2D.whiteTexture;
                if (m_Mask != texture)
                {
                    m_MaskHandle?.Release();
                    m_Mask = texture;
                    m_MaskHandle = RTHandles.Alloc(texture);
                }
                return m_MaskHandle;
            }
            internal void Dispose()
            {
                m_MaskHandle?.Release();
                m_CurveHandle?.Release();
                CoreUtils.Destroy(m_Curve);
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
            if (m_CameraStates.TryGetValue(historyRTSystem, out var state))
            {
                state.Dispose();
                m_CameraStates.Remove(historyRTSystem);
            }
        }

        void InvalidateHistory(UniversalCameraData cameraData)
        {
            var system = HistoryFrameRTSystem.GetOrCreate(cameraData.camera, cameraData.xr.multipassId);
            if (m_CameraStates.TryGetValue(system, out var state))
                state.Invalidate();
        }

        void DisposeCameraStates()
        {
            foreach (var entry in m_CameraStates)
            {
                entry.Key.disposed -= RemoveCameraState;
                entry.Value.Dispose();
                entry.Key.ReleaseHistoryFrameRT(HistoryFrameType.Exposure);
            }
            m_CameraStates.Clear();
        }

        static bool ReAllocateHistoryTextureIfNeeded(HistoryFrameRTSystem historyRTSystem)
        {
            if (historyRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure) != null)
                return false;

            var descriptor = new RenderTextureDescriptor(1, 1, GraphicsFormat.R32G32_SFloat, 0)
            { enableRandomWrite = true, dimension = TextureDimension.Tex2D, volumeDepth = 1, msaaSamples = 1 };
            historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.Exposure, historyRTSystem.name, AllocateHistoryTexture, descriptor, 2);
            return true;
        }

        static RTHandle AllocateHistoryTexture(RenderTextureDescriptor descriptor, string cameraName, int frame, RTHandleSystem system)
        {
            return system.Alloc(1, 1, colorFormat: descriptor.graphicsFormat, enableRandomWrite: true,
                filterMode: FilterMode.Point, name: cameraName + " Exposure " + frame);
        }
    }
}
