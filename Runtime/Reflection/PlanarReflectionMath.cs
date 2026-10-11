namespace UnityEngine.Rendering.Universal
{
    internal static class PlanarReflectionMath
    {
        internal static Matrix4x4 ReflectionMatrix(Vector3 position, Vector3 normal)
        {
            normal.Normalize();
            float d = -Vector3.Dot(normal, position);
            Vector4 plane = new Vector4(normal.x, normal.y, normal.z, d);
            Matrix4x4 reflection = Matrix4x4.identity;
            for (int row = 0; row < 3; ++row)
                for (int column = 0; column < 4; ++column)
                    reflection[row, column] -= 2f * plane[row] * plane[column];
            return reflection;
        }

        internal static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 position, Vector3 normal, float offset)
        {
            Vector3 point = worldToCamera.MultiplyPoint(position + normal * offset);
            Vector3 direction = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(direction.x, direction.y, direction.z, -Vector3.Dot(point, direction));
        }

        internal static Vector2Int TargetSize(int width, int height, float scale, int maximum)
        {
            width = Mathf.Max(width, 1);
            height = Mathf.Max(height, 1);
            float factor = Mathf.Min(Mathf.Clamp(scale, 0.125f, 1f), Mathf.Max(64, maximum) / (float)Mathf.Max(width, height));
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * factor)), Mathf.Max(1, Mathf.RoundToInt(height * factor)));
        }

        internal static bool ShouldCapture(PlanarReflectionRefreshMode mode, bool valid, bool moved, uint request, uint lastRequest, int elapsedFrames, int interval, bool playing)
        {
            if (mode == PlanarReflectionRefreshMode.OnDemand) return request != 0 && (!valid || request != lastRequest);
            if (mode == PlanarReflectionRefreshMode.OnEnable) return !valid || request != lastRequest;
            return !valid || moved || request != lastRequest || !playing || elapsedFrames >= interval;
        }
        internal static float CaptureFieldOfView(PlanarReflectionProbe probe, Camera viewer)
        {
            if (probe.fieldOfViewMode == PlanarReflectionFieldOfViewMode.Fixed) return Mathf.Clamp(probe.fieldOfView, 1, 170);
            float fov = viewer.fieldOfView;
            if (probe.fieldOfViewMode == PlanarReflectionFieldOfViewMode.Automatic)
            {
                fov = 1;
                Vector3 direction = probe.planePosition - viewer.transform.position;
                var extent = probe.halfSize;
                for (int i = 0; i < 8; ++i)
                {
                    Vector3 corner = probe.planePosition + probe.transform.right * ((i & 1) == 0 ? -extent.x : extent.x)
                        + probe.transform.up * ((i & 2) == 0 ? -probe.surfaceThickness : probe.surfaceThickness)
                        + probe.transform.forward * ((i & 4) == 0 ? -extent.y : extent.y);
                    fov = Mathf.Max(fov, 2 * Vector3.Angle(direction, corner - viewer.transform.position));
                }
            }
            return Mathf.Clamp(fov * probe.fieldOfViewScale, 1, 170);
        }

        internal static Bounds InfluenceBounds(PlanarReflectionProbe probe)
        {
            Vector2 size = probe.halfSize;
            Vector3 x = probe.transform.right * size.x;
            Vector3 y = probe.planeNormal * probe.surfaceThickness;
            Vector3 z = probe.transform.forward * size.y;
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(probe.planePosition, extents * 2f);
        }
    }
}
