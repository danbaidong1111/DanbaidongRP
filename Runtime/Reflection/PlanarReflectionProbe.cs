namespace UnityEngine.Rendering.Universal
{
    public enum PlanarReflectionProjection { Infinite, Box, Sphere }
    public enum PlanarReflectionRefreshMode { EveryFrame, OnEnable, OnDemand }
    public enum PlanarReflectionFieldOfViewMode { Viewer, Fixed, Automatic }

    /// <summary>A real-time reflection of the scene about this transform's local XZ plane.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Danbaidong Planar Reflection Probe")]
    public sealed class PlanarReflectionProbe : MonoBehaviour
    {
        [Header("Influence")]
        [Tooltip("Width and length of the reflecting region in local XZ units. Transform scale also applies.")]
        public Vector2 size = new Vector2(10f, 10f);
        [Tooltip("Offset of the reflection plane along the transform's local up axis, in world units.")]
        public float planeOffset;
        [Min(0.001f), Tooltip("Maximum world-space distance of a receiving surface from the reflection plane.")]
        public float surfaceThickness = 0.15f;
        [Min(0f), Tooltip("World-space distance over which the reflection fades at the region's edges.")]
        public float blendDistance = 0.5f;
        [Range(0f, 1f), Tooltip("Minimum alignment between the receiving surface normal and the reflection plane normal.")]
        public float normalThreshold = 0.85f;
        [Min(0f)] public float intensity = 1f;
        [Range(0f, 1f), Tooltip("Blend weight within the reflection hierarchy.")]
        public float weight = 1f;
        [Min(0f), Tooltip("Fade out near this distance from the camera. Zero disables the fade.")]
        public float fadeDistance = 10000f;
        [Tooltip("Rendering layers allowed to receive this reflection.")]
        public RenderingLayerMask renderingLayerMask = uint.MaxValue;
        [Tooltip("Higher priorities are rendered first when more than four probes overlap a camera.")]
        public int priority;
        [Tooltip("Also capture a reflection when the camera is below the plane.")]
        public bool twoSided;

        [Header("Projection")]
        [Tooltip("Use a proxy of the reflected scene to project normal-mapped reflections.")]
        public PlanarReflectionProjection projection = PlanarReflectionProjection.Infinite;
        public Vector3 projectionVolumeCenter = new Vector3(0, 10, 0);
        public Vector3 projectionVolumeSize = new Vector3(30, 20, 30);
        [Min(.001f)] public float projectionSphereRadius = 15f;

        [Header("Capture")]
        public PlanarReflectionRefreshMode refreshMode = PlanarReflectionRefreshMode.EveryFrame;
        public PlanarReflectionFieldOfViewMode fieldOfViewMode = PlanarReflectionFieldOfViewMode.Viewer;
        [Range(1f, 170f)] public float fieldOfView = 90f;
        [Min(.01f)] public float fieldOfViewScale = 1f;
        public LayerMask cullingMask = -1;
        [Range(0.125f, 1f)] public float resolutionScale = 0.5f;
        [Range(64, 4096)] public int maxResolution = 1024;
        [Min(0.1f)] public float farClipPlane = 100f;
        [Min(0.001f), Tooltip("Offsets the clipping plane to prevent the reflecting surface from reflecting itself.")]
        public float clipPlaneOffset = 0.025f;
        public bool renderShadows = true;
        [Tooltip("Filter rough reflections using scene depth and distance to the reflection plane.")]
        public bool roughReflections = true;
        [Tooltip("Use the pipeline's default renderer when -1, otherwise use this renderer index.")]
        public int rendererIndex = -1;
        [Min(1), Tooltip("Capture once every N frames. Moving cameras, planes and resized targets always refresh immediately.")]
        public int updateInterval = 1;
        [Min(0f), Tooltip("Skip captures farther from the plane region than this distance. Zero disables distance culling.")]
        public float maxCaptureDistance = 200f;
        public bool renderInSceneView = true;

        /// <summary>World-space point on the reflection plane.</summary>
        public Vector3 planePosition => transform.position + transform.up * planeOffset;
        /// <summary>World-space normal of the reflection plane.</summary>
        public Vector3 planeNormal => transform.up;
        internal Vector2 halfSize => new Vector2(Mathf.Max(0.001f, size.x * Mathf.Abs(transform.lossyScale.x) * 0.5f),
            Mathf.Max(0.001f, size.y * Mathf.Abs(transform.lossyScale.z) * 0.5f));

        /// <summary>The most recently rendered texture for this source camera, or null before a capture.</summary>
        public RenderTexture GetTexture(Camera sourceCamera) => PlanarReflectionSystem.GetTexture(this, sourceCamera);

        /// <summary>Refresh this probe on the next render of each source camera.</summary>
        internal uint renderRequest { get; private set; }
        public void RequestRender() { ++renderRequest; PlanarReflectionSystem.Invalidate(this); }

        void OnEnable() => PlanarReflectionSystem.Register(this);
        void OnDisable() => PlanarReflectionSystem.Unregister(this);
        void OnDestroy() => PlanarReflectionSystem.Unregister(this);

        void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.001f, size.x), Mathf.Max(0.001f, size.y));
            surfaceThickness = Mathf.Max(0.001f, surfaceThickness);
            blendDistance = Mathf.Max(0f, blendDistance);
            intensity = Mathf.Max(0f, intensity);
            resolutionScale = Mathf.Clamp(resolutionScale, 0.125f, 1f);
            maxResolution = Mathf.Clamp(maxResolution, 64, 4096);
            farClipPlane = Mathf.Max(0.1f, farClipPlane);
            clipPlaneOffset = Mathf.Max(0.001f, clipPlaneOffset);
            updateInterval = Mathf.Max(1, updateInterval);
            normalThreshold = Mathf.Clamp01(normalThreshold);
            maxCaptureDistance = Mathf.Max(0f, maxCaptureDistance);
            projectionVolumeSize = Vector3.Max(Vector3.one * .001f, projectionVolumeSize);
            projectionSphereRadius = Mathf.Max(.001f, projectionSphereRadius);
            weight = Mathf.Clamp01(weight);
            fadeDistance = Mathf.Max(0, fadeDistance);
            fieldOfView = Mathf.Clamp(fieldOfView, 1, 170);
            fieldOfViewScale = Mathf.Max(.01f, fieldOfViewScale);
            RequestRender();
        }

        void OnDrawGizmosSelected()
        {
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(planePosition, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Vector2 extent = halfSize;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(extent.x * 2f, surfaceThickness * 2f, extent.y * 2f));
            Gizmos.DrawLine(Vector3.zero, Vector3.up * Mathf.Min(extent.x, extent.y));
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
