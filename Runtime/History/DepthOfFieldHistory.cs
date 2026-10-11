using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    internal sealed class DepthOfFieldHistory
    {
        internal enum Surface { CoC, Color, Velocity, Depth, Count }
        readonly HistoryFrameRTSystem m_System;
        static HistoryFrameType Id(Surface surface) => (HistoryFrameType)((int)HistoryFrameType.DepthOfFieldCoC + (int)surface);
        readonly Hash128[] m_Descriptors = new Hash128[2];
        readonly bool[] m_ColorHistory = new bool[2];
        readonly Hash128[] m_Settings = new Hash128[2];
        readonly int[] m_LastFrame = { -1, -1 };
        readonly Vector3[] m_Position = new Vector3[2];
        readonly Quaternion[] m_Rotation = new Quaternion[2];
        readonly Matrix4x4[] m_Projection = new Matrix4x4[2];
        readonly bool[] m_Valid = new bool[2];
        readonly bool[] m_PreviousWasValid = new bool[2];
        readonly int[] m_AccumulatedFrames = new int[2];

        internal DepthOfFieldHistory(HistoryFrameRTSystem system) { m_System = system; }
        internal static DepthOfFieldHistory Get(Camera camera, int eye = 0) => HistoryFrameRTSystem.GetOrCreate(camera, eye).depthOfFieldHistory;
        internal RTHandle Current(int eye, Surface surface) => m_System.GetCurrentFrameRT(Id(surface));
        internal RTHandle Previous(int eye, Surface surface) => m_System.GetPreviousFrameRT(Id(surface));
        internal bool IsValid(int eye) => m_Valid[eye];
        internal int LastFrame(int eye) => m_LastFrame[eye];
        internal bool PreviousWasValid(int eye) => m_PreviousWasValid[eye];
        internal int AccumulatedFrames(int eye) => m_AccumulatedFrames[eye];
        internal void Invalidate() { m_Valid[0] = m_Valid[1] = false; }
        public void Reset()
        {
            for (int eye = 0; eye < 2; ++eye)
            {
                Release(eye);
                m_LastFrame[eye] = -1;
            }
        }
        void Release(int eye)
        {
            for (int surface = 0; surface < (int)Surface.Count; ++surface)
                m_System.ReleaseHistoryFrameRT(Id((Surface)surface));
            m_Valid[eye] = false;
            m_PreviousWasValid[eye] = false;
            m_AccumulatedFrames[eye] = 0;
        }
        internal bool Prepare(UniversalCameraData cameraData, int eye, RenderTextureDescriptor descriptor, Hash128 settings, bool colorHistory = true)
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
            if (Current(eye, Surface.CoC) == null || m_Descriptors[eye] != descriptorKey || m_ColorHistory[eye] != colorHistory)
            {
                Release(eye);
                for (int surface = 0; surface < (int)Surface.Count; ++surface)
                {
                    if (!colorHistory && (surface == (int)Surface.Color || surface == (int)Surface.Velocity)) continue;
                    descriptor.graphicsFormat = surface == (int)Surface.Color ? GraphicsFormat.R16G16B16A16_SFloat
                        : surface == (int)Surface.Depth ? GraphicsFormat.R32_SFloat : GraphicsFormat.R16_SFloat;
                    m_System.AllocHistoryFrameRT((int)Id((Surface)surface), "Depth of Field " + (Surface)surface, Allocate, descriptor, 2);
                }
                m_Descriptors[eye] = descriptorKey;
                m_ColorHistory[eye] = colorHistory;
            }
            var camera = cameraData.camera;
            var projection = cameraData.GetProjectionMatrixNoJitter();
            bool projectionChanged = Mathf.Abs(projection.m00 - m_Projection[eye].m00) > 0.01f
                || Mathf.Abs(projection.m11 - m_Projection[eye].m11) > 0.01f
                || Mathf.Abs(projection.m02 - m_Projection[eye].m02) > 0.01f
                || Mathf.Abs(projection.m12 - m_Projection[eye].m12) > 0.01f;
            bool cut = (camera.transform.position - m_Position[eye]).sqrMagnitude > 25f
                || Quaternion.Angle(camera.transform.rotation, m_Rotation[eye]) > 45f;
            bool valid = m_Valid[eye] && m_LastFrame[eye] == Time.frameCount - 1
                && m_Settings[eye] == settings && !cameraData.resetHistory && !projectionChanged && !cut;
            m_Settings[eye] = settings;
            m_Position[eye] = camera.transform.position;
            m_Rotation[eye] = camera.transform.rotation;
            m_Projection[eye] = projection;
            m_PreviousWasValid[eye] = valid;
            return valid;
        }
        static RTHandle Allocate(RenderTextureDescriptor descriptor, string name, int frame, RTHandleSystem system)
        {
            return system.Alloc(descriptor.width, descriptor.height, slices: descriptor.volumeDepth, dimension: descriptor.dimension,
                colorFormat: descriptor.graphicsFormat, enableRandomWrite: true, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: name + " " + frame);
        }
        internal void Commit(int eye, int frame)
        {
            m_LastFrame[eye] = frame;
            m_Valid[eye] = true;
            m_AccumulatedFrames[eye] = m_PreviousWasValid[eye] ? m_AccumulatedFrames[eye] + 1 : 1;
        }
    }
}
