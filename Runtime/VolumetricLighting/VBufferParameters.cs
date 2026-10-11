namespace UnityEngine.Rendering.Universal
{
    internal struct VBufferParameters
    {
        internal int width, height, slices, views;
        internal int allocationSlices;
        internal float near, far;
        internal Matrix4x4 inverseViewProjection, viewProjection;
        internal Matrix4x4[] viewProjections, inverseViewProjections, depthInverseViewProjections, cullingProjections, viewMatrices;
        internal Vector4[] positions, forwards;
        internal Vector4 depthEncoding, depthDecoding;
        internal VBufferParameters(UniversalCameraData cameraData, AtmosphericFog settings)
        {
            var camera = cameraData.camera;
            views = cameraData.xr.enabled && cameraData.xr.singlePassEnabled ? cameraData.xr.viewCount : 1;
            settings.GetGridSize(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height,
                out width, out height, out slices);
            // Both fog models share capacity, including the boundary used by Toon integration.
            allocationSlices = slices + (settings.isPhysical ? 1 : 0);
            near = Mathf.Max(0.1f, camera.nearClipPlane);
            float farPlaneRadius = camera.farClipPlane;
            var rayProjection = camera.nonJitteredProjectionMatrix;
            var standardProjection = camera.orthographic
                ? Matrix4x4.Ortho(-1, 1, -1, 1, camera.nearClipPlane, camera.farClipPlane)
                : Matrix4x4.Perspective(camera.fieldOfView, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            // Keep lens shift while excluding temporal jitter and reflection-plane clipping from the ray grid.
            rayProjection.SetRow(2, standardProjection.GetRow(2));
            viewProjection = GL.GetGPUProjectionMatrix(rayProjection, true) * cameraData.GetViewMatrix();
            inverseViewProjection = viewProjection.inverse;
            viewProjections = new Matrix4x4[2];
            inverseViewProjections = new Matrix4x4[2];
            depthInverseViewProjections = new Matrix4x4[2];
            positions = new Vector4[2];
            forwards = new Vector4[2];
            viewMatrices = new Matrix4x4[views];
            cullingProjections = new Matrix4x4[views];
            for (int eye = 0; eye < views; ++eye)
            {
                var projection = cameraData.xr.enabled ? cameraData.GetProjectionMatrixNoJitter(eye) : rayProjection;
                projection.SetRow(2, standardProjection.GetRow(2));
                if (!camera.orthographic)
                    farPlaneRadius = Mathf.Max(farPlaneRadius, GetFarPlaneRadius(projection));
                var view = cameraData.GetViewMatrix(eye);
                viewMatrices[eye] = view;
                cullingProjections[eye] = projection;
                viewProjections[eye] = GL.GetGPUProjectionMatrix(projection, true) * view;
                inverseViewProjections[eye] = viewProjections[eye].inverse;
                depthInverseViewProjections[eye] = (GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(eye), true) * view).inverse;
                positions[eye] = view.inverse.GetColumn(3);
                positions[eye].w = camera.orthographic ? 1 : 0;
                forwards[eye] = -view.inverse.GetColumn(2);
                forwards[eye].w = 0;
            }
            far = Mathf.Max(near + 0.001f, Mathf.Min(near + settings.volumetricDistance.value,
                Mathf.Min(farPlaneRadius, settings.maxDistance.value)));
            GetDepthParameters(near, far, settings.sliceDistributionUniformity.value, out depthEncoding, out depthDecoding);
        }

        internal static void GetDepthParameters(float near, float far, float uniformity, out Vector4 encoding, out Vector4 decoding)
        {
            float curvature = Mathf.Max(0.001f, 2 - 2 * uniformity);
            float logarithm = Mathf.Log(curvature * Mathf.Max(0.001f, far - near) + 1, 2);
            encoding = new Vector4(Mathf.Log(curvature, 2) / logarithm, 1 / logarithm, near - 1 / curvature, 1);
            decoding = new Vector4(1 / curvature, logarithm, near - 1 / curvature, 1);
        }

        internal static float GetFarPlaneRadius(Matrix4x4 projection)
        {
            var inverse = projection.inverse;
            float radius = 0;
            for (int y = -1; y <= 1; y += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                float distance = inverse.MultiplyPoint(new Vector3(x, y, 1)).magnitude;
                if (!float.IsNaN(distance) && !float.IsInfinity(distance)) radius = Mathf.Max(radius, distance);
            }
            return radius;
        }
    }
}
