using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Copy the given depth buffer into the given destination depth buffer.
    ///
    /// You can use this pass to copy a depth buffer to a destination,
    /// so you can use it later in rendering. If the source texture has MSAA
    /// enabled, the pass uses a custom MSAA resolve. If the source texture
    /// does not have MSAA enabled, the pass uses a Blit or a Copy Texture
    /// operation, depending on what the current platform supports.
    /// </summary>
    public class GPUCopyPass : ScriptableRenderPass
    {
        private RTHandle m_Source { get; set; }
        private RTHandle m_Destination { get; set; }

        internal bool m_ShouldClear;

        private ComputeShader m_Shader;
        private int k_SampleKernel_xyzw2x_8;
        private int k_SampleKernel_xyzw2x_1;

        static readonly int s_RectOffset = Shader.PropertyToID("_RectOffset");
        static readonly int s_Result = Shader.PropertyToID("_Result");
        static readonly int s_Source = Shader.PropertyToID("_Source");
        static int[] s_IntParams = new int[2];

        /// <summary>
        /// Creates a new <c>CopyDepthPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="shouldClear">Controls whether it should do a clear before copying the depth.</param>
        /// <seealso cref="RenderPassEvent"/>
        public GPUCopyPass(RenderPassEvent evt, ComputeShader computeShader, bool shouldClear = false)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CopyDepthPass));
            renderPassEvent = evt;
            m_ShouldClear = shouldClear;

            m_Shader = computeShader;
            k_SampleKernel_xyzw2x_8 = m_Shader.FindKernel("KSampleCopy4_1_x_8");
            k_SampleKernel_xyzw2x_1 = m_Shader.FindKernel("KSampleCopy4_1_x_1");
        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Target</param>
        public void Setup(RTHandle source, RTHandle destination)
        {
            this.m_Source = source;
            this.m_Destination = destination;
        }

        void SampleCopyChannel(
            CommandBuffer cmd,
            RectInt rect,
            int sourceID,
            RenderTargetIdentifier source,
            int targetID,
            RenderTargetIdentifier target,
            int slices,
            int kernel8,
            int kernel1)
        {
            RectInt main, topRow, rightCol, topRight;
            unsafe
            {
                RectInt* dispatch1Rects = stackalloc RectInt[3];
                int dispatch1RectCount = 0;
                RectInt dispatch8Rect = new RectInt(0, 0, 0, 0);

                if (TileLayoutUtils.TryLayoutByTiles(
                    rect,
                    8,
                    out main,
                    out topRow,
                    out rightCol,
                    out topRight))
                {
                    if (topRow.width > 0 && topRow.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRow;
                        ++dispatch1RectCount;
                    }
                    if (rightCol.width > 0 && rightCol.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = rightCol;
                        ++dispatch1RectCount;
                    }
                    if (topRight.width > 0 && topRight.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRight;
                        ++dispatch1RectCount;
                    }
                    dispatch8Rect = main;
                }
                else if (rect.width > 0 && rect.height > 0)
                {
                    dispatch1Rects[dispatch1RectCount] = rect;
                    ++dispatch1RectCount;
                }

                cmd.SetComputeTextureParam(m_Shader, kernel8, sourceID, source);
                cmd.SetComputeTextureParam(m_Shader, kernel1, sourceID, source);
                cmd.SetComputeTextureParam(m_Shader, kernel8, targetID, target);
                cmd.SetComputeTextureParam(m_Shader, kernel1, targetID, target);

                if (dispatch8Rect.width > 0 && dispatch8Rect.height > 0)
                {
                    var r = dispatch8Rect;
                    // Use intermediate array to avoid garbage
                    s_IntParams[0] = r.x;
                    s_IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_Shader, s_RectOffset, s_IntParams);
                    cmd.DispatchCompute(m_Shader, kernel8, (int)Mathf.Max(r.width / 8, 1), (int)Mathf.Max(r.height / 8, 1), slices);
                }

                for (int i = 0, c = dispatch1RectCount; i < c; ++i)
                {
                    var r = dispatch1Rects[i];
                    // Use intermediate array to avoid garbage
                    s_IntParams[0] = r.x;
                    s_IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_Shader, s_RectOffset, s_IntParams);
                    cmd.DispatchCompute(m_Shader, kernel1, (int)Mathf.Max(r.width, 1), (int)Mathf.Max(r.height, 1), slices);
                }
            }
        }

        public void SampleCopyChannel_xyzw2x(CommandBuffer cmd, RTHandle source, RTHandle target, RectInt rect)
        {
            Debug.Assert(source.rt.volumeDepth == target.rt.volumeDepth);
            SampleCopyChannel(cmd, rect, s_Source, source, s_Result, target, source.rt.volumeDepth, k_SampleKernel_xyzw2x_8, k_SampleKernel_xyzw2x_1);
        }

        /// <inheritdoc />
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var isDepth = (m_Destination.rt && m_Destination.rt.graphicsFormat == GraphicsFormat.None);
            descriptor.graphicsFormat = isDepth ? GraphicsFormat.D32_SFloat_S8_UInt : GraphicsFormat.R32_SFloat;
            descriptor.msaaSamples = 1;
            // This is a temporary workaround for Editor as not setting any depth here
            // would lead to overwriting depth in certain scenarios (reproducable while running DX11 tests)
#if UNITY_EDITOR
            // This is a temporary workaround for Editor as not setting any depth here
            // would lead to overwriting depth in certain scenarios (reproducable while running DX11 tests)
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
                ConfigureTarget(m_Destination, m_Destination);
            else
#endif
                ConfigureTarget(m_Destination);
            if (m_ShouldClear)
                ConfigureClear(ClearFlag.All, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            renderingData.commandBuffer.SetGlobalTexture("_CameraDepthAttachment", m_Source.nameID);
            var computeShader = m_Shader;


            if (computeShader == null)
            {
                Debug.LogErrorFormat("Missing {0}. GPU Copy Depth render pass will not execute. Check for missing reference in the renderer resources.", computeShader);
                return;
            }

            var cmd = renderingData.commandBuffer;
            var cameraData = renderingData.cameraData;

            // we must use actual size instead of cameraData.pixelWidth/pixelHeight
            int actualWidth = cameraData.cameraTargetDescriptor.width;
            int actualHeight = cameraData.cameraTargetDescriptor.height;

            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.GPUCopy)))
            {
                SampleCopyChannel_xyzw2x(cmd, this.m_Source, this.m_Destination, new RectInt(0, 0, actualWidth, actualHeight));
            }
        }


        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            m_Destination = k_CameraTarget;
        }

        // RenderGraph Not Supported
        //internal void Render(RenderGraph renderGraph, out TextureHandle destination, in TextureHandle source, ref RenderingData renderingData)
        //{

        //    // TODO RENDERGRAPH: should refactor this as utility method for other passes to set Global textures
        //    using (var builder = renderGraph.AddRenderPass<PassData>("Setup Global Depth", out var passData, base.profilingSampler))
        //    {
        //        passData.source = builder.ReadTexture(source);
        //        builder.AllowPassCulling(false);

        //        builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
        //        {
        //            context.cmd.SetGlobalTexture("_CameraDepthAttachment", data.source);
        //        });
        //    }

        //    using (var builder = renderGraph.AddRenderPass<PassData>("Copy Depth", out var passData, base.profilingSampler))
        //    {
        //        var depthDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        //        depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
        //        depthDescriptor.depthStencilFormat = GraphicsFormat.None;
        //        depthDescriptor.depthBufferBits = 0;
        //        depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
        //        destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);

        //        passData.computeShader = m_Shader;
        //        passData.cameraData = renderingData.cameraData;
        //        passData.cmd = renderingData.commandBuffer;
        //        passData.source = builder.ReadTexture(source);
        //        passData.destination = builder.UseColorBuffer(destination, 0);

        //        // TODO RENDERGRAPH: culling? force culling off for testing
        //        builder.AllowPassCulling(false);

        //        builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
        //        {
        //            ExecutePass(context.renderContext, data, ref data.cmd, ref data.cameraData, data.source, data.destination);
        //        });
        //    }

        //    using (var builder = renderGraph.AddRenderPass<PassData>("Setup Global Copy Depth", out var passData, base.profilingSampler))
        //    {
        //        passData.cmd = renderingData.commandBuffer;
        //        passData.destination = builder.UseColorBuffer(destination, 0);

        //        builder.AllowPassCulling(false);

        //        builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
        //        {
        //            data.cmd.SetGlobalTexture("_CameraDepthBufferMipChain", data.destination);
        //        });
        //    }
        //}
    }

}
