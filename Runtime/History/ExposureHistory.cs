using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>Exposure feedback owned by the camera's history RT system.</summary>
    internal sealed class ExposureHistory
    {
        readonly HistoryFrameRTSystem m_System;
        bool m_Valid, m_Playing;
        int m_LastFrame = -1, m_Mode = -1;
        double m_LastTime;
        Vector2Int m_Size;
        Vector3 m_Position;
        Quaternion m_Rotation;
        Matrix4x4 m_Projection;
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

        internal ExposureHistory(HistoryFrameRTSystem system) { m_System = system; }
        internal static ExposureHistory Get(Camera camera, int eye = 0) => HistoryFrameRTSystem.GetOrCreate(camera, eye).exposureHistory;
        internal void Invalidate() => m_Valid = false;
        internal RTHandle Current(int eye = 0) => m_System.GetCurrentFrameRT(HistoryFrameType.Exposure);
        internal RTHandle Previous(int eye = 0) => m_System.GetPreviousFrameRT(HistoryFrameType.Exposure);

        internal bool Prepare(UniversalCameraData data, Vector2Int size, int mode)
        {
            if (Current() == null)
            {
                var descriptor = new RenderTextureDescriptor(1, 1, GraphicsFormat.R32G32_SFloat, 0)
                { enableRandomWrite = true, dimension = TextureDimension.Tex2D, volumeDepth = 1, msaaSamples = 1 };
                m_System.AllocHistoryFrameRT((int)HistoryFrameType.Exposure, m_System.name, Allocate, descriptor, 2);
                m_Valid = false;
            }
            var camera = data.camera;
            var projection = camera.nonJitteredProjectionMatrix;
            double time = Time.realtimeSinceStartupAsDouble;
            bool continuous = VolumetricHistory.IsHistoryContinuous(Application.isPlaying, m_Playing, Time.frameCount, m_LastFrame, time, m_LastTime);
            bool valid = m_Valid && continuous && m_Size == size && m_Mode == mode
                && VolumetricHistory.IsProjectionStable(m_Projection, projection)
                && (camera.transform.position - m_Position).sqrMagnitude <= 25f
                && Quaternion.Angle(camera.transform.rotation, m_Rotation) <= 45f && !data.resetHistory;
            m_LastFrame = Time.frameCount;
            m_LastTime = time;
            m_Playing = Application.isPlaying;
            m_Size = size;
            m_Mode = mode;
            m_Position = camera.transform.position;
            m_Rotation = camera.transform.rotation;
            m_Projection = projection;
            return valid;
        }
        internal void Commit() => m_Valid = true;
        static RTHandle Allocate(RenderTextureDescriptor descriptor, string cameraName, int frame, RTHandleSystem system)
        {
            return system.Alloc(1, 1, colorFormat: descriptor.graphicsFormat, enableRandomWrite: true,
                filterMode: FilterMode.Point, name: cameraName + " Exposure " + frame);
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
            m_System.ReleaseHistoryFrameRT(HistoryFrameType.Exposure);
        }
    }
}
