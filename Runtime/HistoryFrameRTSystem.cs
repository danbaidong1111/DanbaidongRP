using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Copy from HDCameraFrameHistoryType. Uncomment your HistoryFrameType if needed.
    /// <summary>
    public enum HistoryFrameType
    {
        /// <summary>Color buffer mip chain.</summary>
        ColorBufferMipChain,
        ///// <summary>Exposure buffer.</summary>
        //Exposure,
        ///// <summary>Temporal antialiasing history.</summary>
        //TemporalAntialiasing,
        ///// <summary>Velocity magnitude history used for TAA velocity weighting.</summary>
        //TAAMotionVectorMagnitude,
        ///// <summary>Depth of field CoC.</summary>
        //DepthOfFieldCoC,
        ///// <summary>Normal buffer.</summary>
        //Normal,
        ///// <summary>Depth buffer.</summary>
        //Depth,
        ///// <summary>Mip one of the depth buffer .</summary>
        //Depth1,
        ///// <summary>Ambient Occlusion buffer.</summary>
        //AmbientOcclusion,
        ///// <summary>Ray traced ambient occlusion buffer.</summary>
        //RaytracedAmbientOcclusion,
        ///// <summary>Ray traced shadow history buffer.</summary>
        //RaytracedShadowHistory,
        ///// <summary>Ray traced shadow history validity buffer.</summary>
        //RaytracedShadowHistoryValidity,
        ///// <summary>Ray traced shadow history distance buffer.</summary>
        //RaytracedShadowDistanceValidity,
        ///// <summary>Ray traced reflections buffer.</summary>
        //RaytracedReflection,
        ///// <summary>Ray traced indirect diffuse HF buffer.</summary>
        //RaytracedIndirectDiffuseHF,
        ///// <summary>Ray traced indirect diffuse LF buffer.</summary>
        //RaytracedIndirectDiffuseLF,
        ///// <summary>Ray traced subsurface buffer.</summary>
        //RayTracedSubSurface,
        ///// <summary>Path tracing buffer.</summary>
        //PathTracing,
        ///// <summary>Temporal antialiasing history after DoF.</summary>
        //TemporalAntialiasingPostDoF,
        ///// <summary>Volumetric clouds buffer 0.</summary>
        //VolumetricClouds0,
        ///// <summary>Volumetric clouds buffer 1.</summary>
        //VolumetricClouds1,
        ///// <summary>Screen Space Reflection Accumulation.</summary>
        //ScreenSpaceReflectionAccumulation,
        ///// <summary>Path-traced Albedo AOV.</summary>
        //AlbedoAOV,
        ///// <summary>Path-traced Normal AOV.</summary>
        //NormalAOV,
        ///// <summary>Path-traced motion vector AOV.</summary>
        //MotionVectorAOV,
        ///// <summary>Denoised path-traced frame history.</summary>
        //DenoiseHistory
    }

    sealed internal class HistoryFrameRTSystem
    {
        static Dictionary<(Camera, int), HistoryFrameRTSystem> s_Cameras = new Dictionary<(Camera, int), HistoryFrameRTSystem>();
        static List<(Camera, int)> s_Cleanup = new List<(Camera, int)>(); // Recycled to reduce GC pressure

        public Camera camera;

        /// <summary>Camera name.</summary>
        public string name { get; private set; } // Needs to be cached because camera.name generates GCAllocs

        private BufferedRTHandleSystem m_BufferedRTHandleSystem;

        internal HistoryFrameRTSystem(Camera camera)
        {
            this.camera = camera;
            this.name = camera.name;
            m_BufferedRTHandleSystem = new BufferedRTHandleSystem();
        }

        /// <summary>
        /// Get the existing HistoryFrameRTSystem for the provided camera or create a new if it does not exist yet.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="xrMultipassId"></param>
        /// <returns></returns>
        public static HistoryFrameRTSystem GetOrCreate(Camera camera, int xrMultipassId = 0)
        {
            HistoryFrameRTSystem historyFrameRTSystem;

            if (!s_Cameras.TryGetValue((camera, xrMultipassId), out historyFrameRTSystem))
            {
                historyFrameRTSystem = new HistoryFrameRTSystem(camera);
                s_Cameras.Add((camera, xrMultipassId), historyFrameRTSystem);
            }

            return historyFrameRTSystem;
        }

        /// <summary>
        /// Set the RTHandle scale to the actual camera size (can be scaled)
        /// </summary>
        /// <param name="actualWidth"></param>
        /// <param name="actualHeight"></param>
        public void SetReferenceSize(int actualWidth, int actualHeight)
        {
            m_BufferedRTHandleSystem.SwapAndSetReferenceSize(actualWidth, actualHeight);
        }

        /// <summary>
        /// Returns the id RTHandle from the previous frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle from previous frame.</returns>
        public RTHandle GetPreviousFrameRT(HistoryFrameType id)
        {
            return m_BufferedRTHandleSystem.GetFrameRT((int)id, 1);
        }

        /// <summary>
        /// Returns the id RTHandle of the current frame.
        /// </summary>
        /// <param name="id">Id of the history RTHandle.</param>
        /// <returns>The RTHandle of the current frame.</returns>
        public RTHandle GetCurrentFrameRT(HistoryFrameType id)
        {
            return m_BufferedRTHandleSystem.GetFrameRT((int)id, 0);
        }

        /// <summary>
        /// Queries the number of RT handle buffers allocated for a buffer ID.
        /// </summary>
        /// <param name="bufferId">The buffer ID to query.</param>
        /// <returns>The num of frames allocated</returns>
        public int GetNumFramesAllocated(HistoryFrameType bufferId)
        {
            return m_BufferedRTHandleSystem.GetNumFramesAllocated((int)bufferId);
        }

        /// <summary>
        /// Release a buffer
        /// </summary>
        /// <param name="bufferId">Id of the buffer that needs to be released.</param>
        public void ReleaseBuffer(HistoryFrameType bufferId)
        {
            m_BufferedRTHandleSystem.ReleaseBuffer((int)bufferId);
        }

        void ReleaseAllHistoryFrameRT()
        {
            m_BufferedRTHandleSystem.ReleaseAll();
        }

        public void ReleaseHistoryFrameRT(HistoryFrameType id)
        {
            m_BufferedRTHandleSystem.ReleaseBuffer((int)id);
        }

        internal static void ClearAll()
        {
            foreach (var cameraKey in s_Cameras)
            {
                cameraKey.Value.ReleaseAllHistoryFrameRT();
                cameraKey.Value.Dispose();
            }

            s_Cameras.Clear();
            s_Cleanup.Clear();
        }

        /// <summary>
        /// Look for any camera that hasn't been used in the last frame and remove them from the pool.
        /// </summary>
        internal static void CleanUnused()
        {
            foreach (var key in s_Cameras.Keys)
            {
                var historyFrameRTSystem = s_Cameras[key];
                Camera camera = historyFrameRTSystem.camera;

                // Unfortunately, the scene view camera is always isActiveAndEnabled==false so we can't rely on this. For this reason we never release it (which should be fine in the editor)
                if (camera != null && camera.cameraType == CameraType.SceneView)
                    continue;

                if (camera == null)
                {
                    s_Cleanup.Add(key);
                    continue;
                }

                UniversalAdditionalCameraData additionalCameraData = null;
                if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR)
                    camera.gameObject.TryGetComponent(out additionalCameraData);

                bool hasPersistentHistory = additionalCameraData != null && additionalCameraData.hasPersistentHistory;
                // We keep preview camera around as they are generally disabled/enabled every frame. They will be destroyed later when camera.camera is null
                // TODO: Add "isPersistent", it will Mark the Camera as persistant so it won't be destroyed if the camera is disabled.
                if (!camera.isActiveAndEnabled && camera.cameraType != CameraType.Preview && !hasPersistentHistory)
                    s_Cleanup.Add(key);
            }

            foreach (var cam in s_Cleanup)
            {
                Debug.Log("Clean cameras: " + cam);
                s_Cameras[cam].Dispose();
                s_Cameras.Remove(cam);
            }

            s_Cleanup.Clear();
        }

        internal void Dispose()
        {
            m_BufferedRTHandleSystem?.Dispose();
            m_BufferedRTHandleSystem = null;
        }

        /// <summary>
        /// Allocates a history RTHandle with the unique identifier id.
        /// </summary>
        /// <param name="id">Unique id for this history buffer.</param>
        /// <param name="cameraName">Prefix of RT name</param>
        /// <param name="allocator">Allocator function for the history RTHandle.</param>
        /// <param name="graphicsFormat">graphicsFormat</param>
        /// <param name="bufferCount">umber of buffer that should be allocated.</param>
        /// <returns>A new RTHandle.</returns>
        public RTHandle AllocHistoryFrameRT(int id, string cameraName, Func<GraphicsFormat, string, int, RTHandleSystem, RTHandle> allocator, GraphicsFormat graphicsFormat, int bufferCount)
        {
            m_BufferedRTHandleSystem.AllocBuffer(id, (rts, i) => allocator(graphicsFormat, cameraName, i, rts), bufferCount);
            return m_BufferedRTHandleSystem.GetFrameRT(id, 0);
        }
    }

}
