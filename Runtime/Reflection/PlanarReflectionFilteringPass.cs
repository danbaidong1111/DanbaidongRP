using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal sealed class PlanarReflectionFilteringPass
    {
        readonly ComputeShader m_Shader;
        readonly int m_Prepare, m_Copy, m_Downsample, m_Filter;
        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("Planar Reflection Filtering");
        internal PlanarReflectionFilteringPass(ComputeShader shader)
        {
            m_Shader = shader;
            if (shader == null)
                return;
            m_Prepare = shader.FindKernel("Prepare");
            m_Copy = shader.FindKernel("CopyReflection");
            m_Downsample = shader.FindKernel("DownScale");
            m_Filter = shader.FindKernel("FilterPlanarReflection");
        }

        struct Settings
        {
            internal Vector4 size, plane, position, capture;
            internal Matrix4x4 inverseOblique, inverseVP, viewProjection;
            internal int mipCount, filteredMipCount;
        }

        sealed class PassData
        {
            internal ComputeShader shader;
            internal int kernel, mip;
            internal TextureHandle sourceColor, sourceDepth, color, depth, output;
            internal Settings settings;
            internal PlanarReflectionSystem.Capture capture;
        }

        static Vector4 Size(int width, int height) => new Vector4(width, height, 1f / width, 1f / height);
        static void SetParameters(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var s = data.settings;
            cmd.SetComputeVectorParam(data.shader, "_BaseSize", s.size);
            cmd.SetComputeVectorParam(data.shader, "_CurrentSize", Size(Mathf.Max(1, (int)s.size.x >> data.mip), Mathf.Max(1, (int)s.size.y >> data.mip)));
            cmd.SetComputeVectorParam(data.shader, "_Plane", s.plane);
            cmd.SetComputeVectorParam(data.shader, "_CameraPosition", s.position);
            cmd.SetComputeVectorParam(data.shader, "_CaptureParams", s.capture);
            cmd.SetComputeMatrixParam(data.shader, "_ObliqueInverseVP", s.inverseOblique);
            cmd.SetComputeMatrixParam(data.shader, "_InverseVP", s.inverseVP);
            cmd.SetComputeMatrixParam(data.shader, "_ViewProjection", s.viewProjection);
            cmd.SetComputeIntParam(data.shader, "_MaxMip", Mathf.Max(1, Mathf.FloorToInt(Mathf.Log(s.size.x, 2) - 1)));
        }

        static void Dispatch(PassData data, ComputeGraphContext context)
        {
            int width = Mathf.Max(1, (int)data.settings.size.x >> data.mip), height = Mathf.Max(1, (int)data.settings.size.y >> data.mip);
            context.cmd.DispatchCompute(data.shader, data.kernel, (width + 7) / 8, (height + 7) / 8, 1);
        }

        struct Resources
        {
            internal Settings settings;
            internal PlanarReflectionSystem.Capture capture;
            internal TextureHandle sourceColor, sourceDepth, color, depth, output;
        }

        static readonly string[] s_MipNames = CreatePassNames("Planar Color and Depth Mip ", 32);
        static readonly string[] s_FilterNames = CreatePassNames("Planar Roughness Filter ", 7);
        static string[] CreatePassNames(string prefix, int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; ++i)
                names[i] = prefix + i;
            return names;
        }

        Resources InitResource(RenderGraph graph, ContextContainer frameData, PlanarReflectionSystem.Capture capture)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var camera = cameraData.camera;
            int width = capture.filteredTexture.width, height = capture.filteredTexture.height;
            var vp = GL.GetGPUProjectionMatrix(capture.regularProjection, true) * camera.worldToCameraMatrix;
            var settings = new Settings
            {
                size = Size(width, height),
                plane = capture.plane,
                position = camera.transform.position,
                capture = new Vector4(2f * Mathf.Atan(1f / Mathf.Abs(capture.regularProjection.m11)), camera.orthographic ? 2f * camera.orthographicSize * camera.aspect : 0, camera.farClipPlane, 0),
                inverseOblique = (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse,
                inverseVP = vp.inverse,
                viewProjection = vp,
                mipCount = 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2)),
                filteredMipCount = capture.roughReflections ? Mathf.Min(7, capture.filteredTexture.mipmapCount) : 1
            };
            var resources = new Resources
            {
                settings = settings,
                capture = capture,
                sourceColor = resourceData.activeColorTexture,
                sourceDepth = resourceData.cameraDepthTexture,
                output = graph.ImportTexture(capture.textureHandle)
            };
            if (!capture.roughReflections)
                return resources;
            var descriptor = new TextureDesc(width, height)
            {
                name = "Planar Color Pyramid",
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear
            };
            resources.color = graph.CreateTexture(descriptor);
            descriptor.name = "Planar Depth Pyramid";
            descriptor.colorFormat = GraphicsFormat.R32_SFloat;
            resources.depth = graph.CreateTexture(descriptor);
            return resources;
        }

        void RenderCopy(RenderGraph graph, in Resources r)
        {
            using var builder = graph.AddComputePass<PassData>("Planar Reflection Copy", out var data, s_Sampler);
            data.shader = m_Shader;
            data.kernel = m_Copy;
            data.settings = r.settings;
            data.capture = r.capture;
            data.sourceColor = r.sourceColor;
            data.output = r.output;
            builder.UseTexture(r.sourceColor, AccessFlags.Read);
            builder.UseTexture(r.output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) => ExecuteCopy(pass, context));
        }

        void RenderPrepare(RenderGraph graph, in Resources r)
        {
            using (var builder = graph.AddComputePass<PassData>("Planar Depth Conversion", out var data, s_Sampler))
            {
                data.shader = m_Shader;
                data.kernel = m_Prepare;
                data.settings = r.settings;
                data.capture = r.capture;
                data.sourceColor = r.sourceColor;
                data.sourceDepth = r.sourceDepth;
                data.color = r.color;
                data.depth = r.depth;
                data.output = r.output;
                builder.UseTexture(data.sourceColor, AccessFlags.Read);
                builder.UseTexture(data.sourceDepth, AccessFlags.Read);
                builder.UseTexture(r.color, AccessFlags.Write);
                builder.UseTexture(r.depth, AccessFlags.Write);
                builder.UseTexture(r.output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) => ExecutePrepare(pass, context));
            }
        }

        void RenderPyramid(RenderGraph graph, in Resources r)
        {
            for (int mip = 1; mip < r.settings.mipCount; ++mip)
            {
                using var builder = graph.AddComputePass<PassData>(s_MipNames[mip], out var data);
                data.shader = m_Shader;
                data.kernel = m_Downsample;
                data.settings = r.settings;
                data.mip = mip;
                data.color = r.color;
                data.depth = r.depth;
                builder.UseTexture(r.color, AccessFlags.ReadWrite);
                builder.UseTexture(r.depth, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) => ExecutePyramid(pass, context));
            }
        }

        void RenderFiltering(RenderGraph graph, in Resources r)
        {
            for (int mip = 1; mip < r.settings.filteredMipCount; ++mip)
            {
                using var builder = graph.AddComputePass<PassData>(s_FilterNames[mip], out var data, s_Sampler);
                data.shader = m_Shader;
                data.kernel = m_Filter;
                data.settings = r.settings;
                data.mip = mip;
                data.color = r.color;
                data.depth = r.depth;
                data.output = r.output;
                builder.UseTexture(r.color, AccessFlags.Read);
                builder.UseTexture(r.depth, AccessFlags.Read);
                builder.UseTexture(r.output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData pass, ComputeGraphContext context) => ExecuteFiltering(pass, context));
            }
        }

        static void ExecuteCopy(PassData pass, ComputeGraphContext context)
        {
            SetParameters(pass, context);
            RTHandle source = pass.sourceColor;
            context.cmd.SetComputeVectorParam(pass.shader, "_SourceScale", source.useScaling ? source.rtHandleProperties.rtHandleScale : Vector4.one);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_SourceColor", pass.sourceColor);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_FilteredReflection", pass.output);
            Dispatch(pass, context);
            pass.capture.filtered = true;
        }

        static void ExecutePrepare(PassData pass, ComputeGraphContext context)
        {
            SetParameters(pass, context);
            RTHandle source = pass.sourceColor, sourceDepth = pass.sourceDepth;
            Vector4 colorScale = source.useScaling ? source.rtHandleProperties.rtHandleScale : Vector4.one;
            Vector4 depthScale = sourceDepth.useScaling ? sourceDepth.rtHandleProperties.rtHandleScale : Vector4.one;
            context.cmd.SetComputeVectorParam(pass.shader, "_SourceScale", new Vector4(colorScale.x, colorScale.y, depthScale.x, depthScale.y));
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_SourceColor", pass.sourceColor);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_SourceDepth", pass.sourceDepth);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_OutputColor", pass.color, 0);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_OutputDepth", pass.depth, 0);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_FilteredReflection", pass.output, 0);
            Dispatch(pass, context);
            pass.capture.filtered = true;
        }

        static void ExecutePyramid(PassData pass, ComputeGraphContext context)
        {
            SetParameters(pass, context);
            context.cmd.SetComputeIntParam(pass.shader, "_SourceMip", pass.mip - 1);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_ColorPyramid", pass.color);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_DepthPyramid", pass.depth);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_OutputColor", pass.color, pass.mip);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_OutputDepth", pass.depth, pass.mip);
            Dispatch(pass, context);
        }

        static void ExecuteFiltering(PassData pass, ComputeGraphContext context)
        {
            SetParameters(pass, context);
            context.cmd.SetComputeIntParam(pass.shader, "_SourceMip", pass.mip);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_ColorPyramid", pass.color);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_DepthPyramid", pass.depth);
            context.cmd.SetComputeTextureParam(pass.shader, pass.kernel, "_FilteredReflection", pass.output, pass.mip);
            Dispatch(pass, context);
        }

        internal void Render(RenderGraph graph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            if (m_Shader == null || !SystemInfo.supportsComputeShaders || !PlanarReflectionSystem.TryGetCapture(cameraData.camera, out var capture))
                return;
            var resources = InitResource(graph, frameData, capture);
            if (!capture.roughReflections)
            {
                RenderCopy(graph, resources);
                return;
            }

            RenderPrepare(graph, resources);
            RenderPyramid(graph, resources);
            RenderFiltering(graph, resources);
        }
    }
}
