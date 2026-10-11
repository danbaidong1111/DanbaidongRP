using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public enum LocalVolumetricFogMaskMode { Texture, Material }
    public enum LocalVolumetricFogBlendingMode { Additive, Overwrite, Multiply, Min, Max }
    public enum LocalVolumetricFogScaleMode { InheritFromHierarchy, Invariant }
    public enum LocalVolumetricFogFalloffMode { Linear, Exponential }

    internal struct LocalVolumetricFogData
    {
        public Matrix4x4 worldToVolume, volumeToWorld;
        public Vector4 albedoDensity, shapeNoise, distanceFade;
        public Vector3 textureTiling, textureOffset, positiveFade, negativeFade, noiseVelocity;
        public int invertFade, falloffMode, blendingMode, maskMode, priority, instanceId, maskResolution;
        public Texture texture;
        public Material material;
        public Bounds bounds;
        public bool textureIsAlphaOnly;
    }

    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/Local Volumetric Fog")]
    public sealed class LocalVolumetricFog : MonoBehaviour
    {
        internal static readonly List<LocalVolumetricFog> instances = new List<LocalVolumetricFog>();

        [Tooltip("Dimensions along the volume's local axes, before optional hierarchy scaling.")]
        public Vector3 size = new Vector3(5, 5, 5);
        [Tooltip("Inherit From Hierarchy multiplies Size by the Transform scale. Invariant uses Size directly.")]
        public LocalVolumetricFogScaleMode scaleMode = LocalVolumetricFogScaleMode.InheritFromHierarchy;
        [Tooltip("Extinction in inverse meters. Zero contributes no medium in Additive mode, and can remove fog with Overwrite or Min.")]
        [Min(0)] public float density = 0.1f;
        [Tooltip("Single-scattering reflectance. RGB is clamped to [0,1]; alpha is not used.")]
        [ColorUsage(false)]
        public Color albedo = Color.white;
        [Tooltip("How the local scattering and extinction combine with the medium already present.")]
        public LocalVolumetricFogBlendingMode blendingMode = LocalVolumetricFogBlendingMode.Additive;
        [Tooltip("Lower priorities are evaluated first. At equal priority, the instance ID provides a stable order for the lifetime of the objects.")]
        public int priority;

        [Tooltip("Choose a 3D texture, or a material that writes a color multiplier in RGB and a density multiplier in alpha.")]
        public LocalVolumetricFogMaskMode maskMode = LocalVolumetricFogMaskMode.Texture;
        [Tooltip("Optional 3D texture or 3D RenderTexture. RGB multiplies Albedo and alpha multiplies Density. Alpha8 textures leave Albedo unchanged.")]
        public Texture volumeMask;
        [Tooltip("Material with a DanbaidongFogVolume pass. The Fog Volume Mask template provides the required slice coordinates and output contract.")]
        public Material materialMask;
        [Tooltip("Resolution along each axis when rendering a material into a volume mask. Higher values increase memory and the number of slice draws.")]
        [Range(8, 128)] public int materialMaskResolution = 32;
        [Tooltip("Repetitions of the mask along the volume's local X, Y and Z axes.")]
        public Vector3 textureTiling = Vector3.one;
        [Tooltip("Static offset of the mask in texture coordinates.")]
        public Vector3 textureOffset;
        [Tooltip("Mask motion in texture-coordinate units per second. Positive motion scrolls the pattern toward the positive local axis.")]
        public Vector3 textureScrollingSpeed;

        [Tooltip("Use independent blend distances for all six faces. Stored values are fractions of the corresponding axis size.")]
        public bool perAxisFade;
        [Tooltip("Uniform blend width as a fraction of the shortest scaled axis. Zero gives a hard boundary.")]
        [Range(0, 0.5f)] public float edgeFade = 0.15f;
        [Tooltip("Blend widths at the positive X, Y and Z faces, as fractions of each axis size.")]
        public Vector3 positiveFade = new Vector3(0.15f, 0.15f, 0.15f);
        [Tooltip("Blend widths at the negative X, Y and Z faces, as fractions of each axis size.")]
        public Vector3 negativeFade = new Vector3(0.15f, 0.15f, 0.15f);
        [Tooltip("Keep the boundary region and fade the interior instead.")]
        public bool invertFade;
        [Tooltip("Shape of the boundary blend. Distance Fade always remains linear.")]
        public LocalVolumetricFogFalloffMode falloffMode = LocalVolumetricFogFalloffMode.Linear;
        [Tooltip("Distance from the camera where the local medium starts fading out, in meters.")]
        [Min(0)] public float distanceFadeStart = 10000;
        [Tooltip("Distance from the camera where the local medium is fully faded out, in meters.")]
        [Min(0)] public float distanceFadeEnd = 10000;

        [Tooltip("Additional procedural density modulation, applied after the texture or material mask. Set to zero for an unmodified mask.")]
        [Range(0, 1)] public float noiseAmount = 0.5f;
        [Min(0.001f)] public float noiseScale = 0.5f;
        public Vector3 noiseVelocity = new Vector3(0.1f, 0, 0);
        [Tooltip("Use an ellipsoid inside the volume bounds instead of a box. The six-face fade remains available inside this shape.")]
        public bool spherical;

        /// <summary>Distance at which a homogeneous medium transmits 1/e of the incident light.</summary>
        public float meanFreePath
        {
            get => density > 0 ? 1f / density : float.PositiveInfinity;
            set => density = float.IsPositiveInfinity(value) ? 0 : 1f / Mathf.Max(0.05f, Finite(value, 10f));
        }

        void OnEnable() { if (!instances.Contains(this)) instances.Add(this); }
        void OnDisable() => instances.Remove(this);
        void OnDestroy() => instances.Remove(this);

        internal Matrix4x4 worldToVolume => volumeToWorld.inverse;
        internal Matrix4x4 volumeToWorld => Matrix4x4.TRS(transform.position, transform.rotation, scaledSize);
        internal Vector3 scaledSize
        {
            get
            {
                Vector3 dimensions = new Vector3(Mathf.Max(0.001f, Finite(size.x, 5f)), Mathf.Max(0.001f, Finite(size.y, 5f)), Mathf.Max(0.001f, Finite(size.z, 5f)));
                if (scaleMode == LocalVolumetricFogScaleMode.InheritFromHierarchy)
                    dimensions = Vector3.Scale(dimensions, transform.lossyScale);
                return new Vector3(NonZeroScale(dimensions.x), NonZeroScale(dimensions.y), NonZeroScale(dimensions.z));
            }
        }

        internal bool hasValidTextureMask => volumeMask == null || volumeMask.dimension == TextureDimension.Tex3D;

        internal LocalVolumetricFogData CaptureData(float time)
        {
            Matrix4x4 toWorld = volumeToWorld;
            Vector3 extentX = toWorld.GetColumn(0) * 0.5f;
            Vector3 extentY = toWorld.GetColumn(1) * 0.5f;
            Vector3 extentZ = toWorld.GetColumn(2) * 0.5f;
            Vector3 boundsSize = 2f * (Abs(extentX) + Abs(extentY) + Abs(extentZ));
            float start = Mathf.Max(0f, Finite(distanceFadeStart, 10000f));
            float end = Mathf.Max(start, Finite(distanceFadeEnd, start));
            float inverseDistance = 1f / Mathf.Max(end - start, 0.00001526f);
            float uniformFade = Mathf.Clamp(Finite(edgeFade, 0.15f), 0f, 0.5f);
            Vector3 axisSize = Abs(scaledSize);
            float uniformDistance = uniformFade * Mathf.Min(axisSize.x, Mathf.Min(axisSize.y, axisSize.z));
            Vector3 uniformFaceFade = new Vector3(uniformDistance / axisSize.x, uniformDistance / axisSize.y, uniformDistance / axisSize.z);
            Texture mask = hasValidTextureMask ? volumeMask : null;
            return new LocalVolumetricFogData
            {
                worldToVolume = toWorld.inverse,
                volumeToWorld = toWorld,
                albedoDensity = new Vector4(Mathf.Clamp01(Finite(albedo.r, 1)), Mathf.Clamp01(Finite(albedo.g, 1)), Mathf.Clamp01(Finite(albedo.b, 1)), Mathf.Max(0, Finite(density, 0))),
                textureTiling = Finite(textureTiling, Vector3.one),
                textureOffset = Finite(textureOffset, Vector3.zero) - Finite(textureScrollingSpeed, Vector3.zero) * Finite(time, 0),
                positiveFade = perAxisFade ? Clamp01(positiveFade) : uniformFaceFade,
                negativeFade = perAxisFade ? Clamp01(negativeFade) : uniformFaceFade,
                distanceFade = new Vector4(inverseDistance, end * inverseDistance, start, end),
                shapeNoise = new Vector4(spherical ? 1 : 0, Mathf.Clamp01(Finite(noiseAmount, 0)), Mathf.Max(0.001f, Finite(noiseScale, 0.5f)), 0),
                noiseVelocity = Finite(noiseVelocity, Vector3.zero),
                invertFade = invertFade ? 1 : 0,
                falloffMode = (int)falloffMode,
                blendingMode = (int)blendingMode,
                maskMode = (int)maskMode,
                priority = priority,
                instanceId = GetInstanceID(),
                maskResolution = Mathf.Clamp(materialMaskResolution, 8, 128),
                texture = mask,
                material = materialMask,
                textureIsAlphaOnly = mask is Texture3D texture3D && texture3D.format == TextureFormat.Alpha8,
                bounds = new Bounds(transform.position, boundsSize)
            };
        }

        internal static int ComparePriority(LocalVolumetricFogData a, LocalVolumetricFogData b)
        {
            int priorityOrder = a.priority.CompareTo(b.priority);
            return priorityOrder != 0 ? priorityOrder : a.instanceId.CompareTo(b.instanceId);
        }

        internal int GetSettingsHash()
        {
            LocalVolumetricFogData data = CaptureData(0);
            unchecked
            {
                int hash = data.instanceId;
                hash = hash * 23 + data.volumeToWorld.GetHashCode();
                hash = hash * 23 + data.albedoDensity.GetHashCode();
                hash = hash * 23 + data.textureTiling.GetHashCode();
                hash = hash * 23 + data.textureOffset.GetHashCode();
                hash = hash * 23 + textureScrollingSpeed.GetHashCode();
                hash = hash * 23 + data.positiveFade.GetHashCode();
                hash = hash * 23 + data.negativeFade.GetHashCode();
                hash = hash * 23 + data.distanceFade.GetHashCode();
                hash = hash * 23 + data.shapeNoise.GetHashCode();
                hash = hash * 23 + data.noiseVelocity.GetHashCode();
                hash = hash * 23 + data.invertFade;
                hash = hash * 23 + data.falloffMode;
                hash = hash * 23 + data.blendingMode;
                hash = hash * 23 + data.maskMode;
                hash = hash * 23 + data.priority;
                hash = hash * 23 + data.maskResolution;
                hash = hash * 23 + gameObject.layer;
                if (data.texture != null)
                {
                    hash = hash * 23 + data.texture.GetInstanceID();
                    hash = hash * 23 + data.texture.updateCount.GetHashCode();
                    hash = hash * 23 + data.texture.imageContentsHash.GetHashCode();
                }
                if (data.material != null)
                {
                    hash = hash * 23 + data.material.GetInstanceID();
                    hash = hash * 23 + data.material.ComputeCRC().GetHashCode();
                }
                return hash;
            }
        }

        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        static Vector3 Finite(Vector3 value, Vector3 fallback) => new Vector3(Finite(value.x, fallback.x), Finite(value.y, fallback.y), Finite(value.z, fallback.z));
        static Vector3 Clamp01(Vector3 value) => new Vector3(Mathf.Clamp01(Finite(value.x, 0)), Mathf.Clamp01(Finite(value.y, 0)), Mathf.Clamp01(Finite(value.z, 0)));
        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        static float NonZeroScale(float value) => value < 0 ? Mathf.Min(-0.001f, Finite(value, -1)) : Mathf.Max(0.001f, Finite(value, 1));

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(albedo.r, albedo.g, albedo.b, 0.6f);
            Gizmos.matrix = volumeToWorld;
            if (spherical) Gizmos.DrawWireSphere(Vector3.zero, 0.5f); else Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
