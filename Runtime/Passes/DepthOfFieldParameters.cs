namespace UnityEngine.Rendering.Universal
{
    internal static class DepthOfFieldParameters
    {
        internal struct Settings
        {
            internal Vector4 lens, ranges, parameters, counts, projection, optics, adaptive, zBuffer, temporal;
            internal Vector3 apertureShape;
            internal Vector2 depthJitter;
            internal int downsample, width, height, blurWidth, blurHeight, tileWidth, tileHeight;
            internal bool debug, physicalAlgorithm, highQuality, stabilize, limitNear, debugTiles, alpha;
        }
        struct HistoryKey
        {
            internal Vector4 lens, ranges, optics, counts, exposure, projection, adaptive, temporal;
            internal Vector3 shape;
            internal int mode, resolution, quality;
        }
        internal static Hash128 SettingsKey(Settings settings, Camera camera)
        {
            var exposure = VolumeManager.instance.stack.GetComponent<Exposure>();
            var key = new HistoryKey { lens = settings.lens, ranges = settings.ranges, optics = settings.optics,
                counts = settings.counts, shape = settings.apertureShape,
                projection = settings.projection, adaptive = settings.adaptive, temporal = settings.temporal,
                mode = (settings.physicalAlgorithm ? 1 : 0) | (settings.parameters.x > .5f ? 2 : 0)
                    | (settings.limitNear ? 4 : 0) | (settings.debug ? 8 : 0) | (settings.debugTiles ? 16 : 0)
                    | (settings.alpha ? 32 : 0) | (settings.stabilize ? 64 : 0),
                resolution = settings.downsample, quality = settings.highQuality ? 1 : 0,
                exposure = exposure == null ? Vector4.zero : new Vector4((int)exposure.mode.value,
                    exposure.isAutomatic || exposure.mode.value == ExposureMode.Off ? 0 :
                        (exposure.mode.value == ExposureMode.UsePhysicalCamera ? Internal.ExposurePass.CameraEV100(camera) : exposure.fixedExposure.value) - exposure.compensation.value,
                    Internal.ExposurePass.LensExposureScale, 0) };
            return Hash128.Compute(ref key);
        }
        internal static float CalculateCoCScale(float focalLength, float aperture, float focusDistance, float sensorHeight, int height)
        {
            float f = Mathf.Max(focalLength, 1f) * .001f;
            return f * f / (Mathf.Max(aperture, .1f) * (Mathf.Max(focusDistance, f + .001f) - f))
                * Mathf.Max(height, 1) / (2f * Mathf.Max(sensorHeight * .001f, .001f));
        }
        internal static Settings PrepareSettings(DepthOfField volume, Camera camera, int width, int height)
        {
            bool physicalLens = volume.focusMode.value == DepthOfFieldFocusMode.PhysicalCamera;
            float focal = physicalLens ? camera.focalLength : volume.focalLength.value;
            float aperture = physicalLens ? camera.aperture : volume.aperture.value;
            float focus = physicalLens && volume.useCameraFocusDistance.value ? camera.focusDistance : volume.focusDistance.value;
            focus = Mathf.Max(focus, focal * .001f + .001f);
            float sensor = physicalLens ? 2f * focal * Mathf.Tan(camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad * .5f) : volume.sensorHeight.value;
            float curvature = volume.bladeCurvature.value;
            float anamorphism = physicalLens ? camera.anamorphism : volume.anamorphism.value;
            float barrel = physicalLens ? camera.barrelClipping : volume.barrelClipping.value;
            int blades = physicalLens ? camera.bladeCount : volume.bladeCount.value;
            float rotation = volume.bladeRotation.value;
            if (physicalLens)
            {
                var range = camera.curvature;
                curvature = range.y > range.x ? 1f - Mathf.Clamp01((aperture - range.x) / (range.y - range.x)) : 0f;
                rotation += (aperture - Camera.kMinAperture) / (Camera.kMaxAperture - Camera.kMinAperture) * (360f / blades);
            }
            curvature = Mathf.Lerp(curvature, 1f, Mathf.Abs(anamorphism));
            int reduction = Mathf.Clamp((int)volume.resolution.value, 1, 4);
            float nearRadius = volume.nearMaxBlur.value, farRadius = volume.farMaxBlur.value;
            // Serialized sample budgets retain their meaning; the aperture uses a square grid or rings.
            float nearAxis = Mathf.Sqrt(volume.nearSampleCount.value), farAxis = Mathf.Sqrt(volume.farSampleCount.value);
            bool highQuality = volume.highQualityFiltering.value;
            float adaptiveWeight = volume.adaptiveSamplingWeight.value;
            float radiusScale = height / 1080f;
            if (volume.quality.value != DepthOfFieldQuality.Custom)
            {
                int quality = (int)volume.quality.value - 1;
                nearAxis = quality == 0 ? 3 : quality == 1 ? 5 : 8;
                farAxis = quality == 0 ? 4 : quality == 1 ? 7 : 14;
                nearRadius = quality == 0 ? 4 : quality == 1 ? 8 : 14;
                farRadius = quality == 0 ? 10 : quality == 1 ? 16 : 26;
                reduction = quality == 0 ? 4 : quality == 1 ? 2 : 1;
                highQuality = quality != 0;
                adaptiveWeight = quality == 0 ? .5f : quality == 1 ? .75f : 1f;
                if (volume.physicallyBased.value)
                {
                    radiusScale = Mathf.Min(width / 1920f, height / 1080f);
                    if (volume.focusMode.value != DepthOfFieldFocusMode.ManualRanges) radiusScale *= 4f;
                }
            }
            float sampleScale = height / 1080f * (volume.physicallyBased.value ? 2f : 2f / reduction);
            if (volume.focusMode.value == DepthOfFieldFocusMode.ManualRanges && volume.nearFocusEnd.value <= 0) nearRadius = 0;
            float nearEnd = Mathf.Max(volume.nearFocusEnd.value, volume.nearFocusStart.value + .001f);
            float farStart = Mathf.Max(volume.farFocusStart.value, nearEnd);
            float farEnd = Mathf.Max(volume.farFocusEnd.value, farStart + .001f);
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            float z0 = 1 - far / near, z1 = far / near;
            if (SystemInfo.usesReversedZBuffer) { z1 += z0; z0 = -z0; }
            return new Settings {
                lens = new Vector4(focus, CalculateCoCScale(focal, aperture, focus, sensor, height), nearRadius * radiusScale, farRadius * radiusScale),
                ranges = new Vector4(volume.nearFocusStart.value, nearEnd, farStart, farEnd),
                parameters = new Vector4(volume.focusMode.value == DepthOfFieldFocusMode.ManualRanges ? 1 : 0, reduction, 8, 0),
                counts = new Vector4(Mathf.Max(3, Mathf.CeilToInt(nearAxis * sampleScale)), Mathf.Max(3, Mathf.CeilToInt(farAxis * sampleScale)), 0, 0),
                projection = new Vector4(near, far, camera.orthographic ? 1 : 0, 0),
                zBuffer = new Vector4(z0, z1, z0 / far, z1 / far),
                apertureShape = new Vector3(blades, curvature, rotation),
                optics = new Vector4(anamorphism / 4f, volume.physicallyBased.value ? 0 : barrel / 3f, focal * (focal * .001f) / (aperture * (focus - focal * .001f)), 0),
                adaptive = new Vector4(Mathf.Min(adaptiveWeight, 1f), Mathf.Max(adaptiveWeight, 1f), 0, 0),
                temporal = new Vector4(volume.temporalAntiFlicker.value, volume.temporalMotionRejection.value, volume.temporalSharpening.value, .9f),
                downsample = reduction, width = width, height = height,
                blurWidth = Mathf.Max(1, (width + reduction - 1) / reduction), blurHeight = Mathf.Max(1, (height + reduction - 1) / reduction),
                tileWidth = (width + 7) / 8, tileHeight = (height + 7) / 8,
                debug = volume.debugCircleOfConfusion.value, debugTiles = volume.debugTileClassification.value,
                physicalAlgorithm = volume.physicallyBased.value, highQuality = highQuality,
                stabilize = volume.coCStabilization.value, limitNear = volume.limitManualRangeNearBlur.value, alpha = true
            };
        }
        internal static Vector2 CalculateDepthJitter(Matrix4x4 projection, Matrix4x4 nonJitteredProjection)
        {
            var jitter = projection * nonJitteredProjection.inverse;
            return new Vector2(jitter.m03, jitter.m13) * .5f;
        }
        internal static Vector2 CalculateTextureDepthJitter(UniversalCameraData cameraData)
        {
            return CalculateDepthJitter(GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true),
                GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrixNoJitter(), true));
        }
    }
}
