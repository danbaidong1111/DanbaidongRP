using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Generates an in-place depth pyramid 
    /// TODO: Mip-mapping depth is problematic for precision at lower mips, generate a packed atlas instead
    /// </summary>
    public class DepthPyramidPass : ScriptableRenderPass
    {
        private RTHandle m_DepthMipChainTexture { get; set; }
        private RenderingUtils.PackedMipChainInfo m_PackedMipChainInfo;
        private bool m_Mip1AlreadyComputed;

        private ComputeShader m_Shader;
        private int m_DepthDownsampleKernel;

        private int[] m_SrcOffset;
        private int[] m_DstOffset;

        static readonly int s_SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        static readonly int s_DstOffset = Shader.PropertyToID("_DstOffset");
        static readonly int s_DepthMipChain = Shader.PropertyToID("_DepthMipChain");

        /// <summary>
        /// 
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="computeShader"></param>
        public DepthPyramidPass(RenderPassEvent evt, ComputeShader computeShader)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CopyDepthPass));
            renderPassEvent = evt;

            m_Shader = computeShader;
            m_DepthDownsampleKernel = m_Shader.FindKernel("KDepthDownsample8DualUav");

            m_SrcOffset = new int[4];
            m_DstOffset = new int[4];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="depthMipChainTexture"></param>
        internal void Setup(RTHandle depthMipChainTexture, RenderingUtils.PackedMipChainInfo info, bool mip1AlreadyComputed = false)
        {
            this.m_DepthMipChainTexture = depthMipChainTexture;
            this.m_PackedMipChainInfo = info;
            this.m_Mip1AlreadyComputed = mip1AlreadyComputed;
        }

        /// <inheritdoc />
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        void RenderMinDepthPyramid(CommandBuffer cmd, RTHandle texture, RenderingUtils.PackedMipChainInfo info, bool mip1AlreadyComputed)
        {
            var cs = m_Shader;
            int kernel = m_DepthDownsampleKernel;

            // TODO: Do it 1x MIP at a time for now. In the future, do 4x MIPs per pass, or even use a single pass.
            // Note: Gather() doesn't take a LOD parameter and we cannot bind an SRV of a MIP level,
            // and we don't support Min samplers either. So we are forced to perform 4x loads.
            for (int i = 1; i < info.mipLevelCount; i++)
            {
                if (mip1AlreadyComputed && i == 1) continue;

                Vector2Int dstSize = info.mipLevelSizes[i];
                Vector2Int dstOffset = info.mipLevelOffsets[i];
                Vector2Int srcSize = info.mipLevelSizes[i - 1];
                Vector2Int srcOffset = info.mipLevelOffsets[i - 1];
                Vector2Int srcLimit = srcOffset + srcSize - Vector2Int.one;

                m_SrcOffset[0] = srcOffset.x;
                m_SrcOffset[1] = srcOffset.y;
                m_SrcOffset[2] = srcLimit.x;
                m_SrcOffset[3] = srcLimit.y;

                m_DstOffset[0] = dstOffset.x;
                m_DstOffset[1] = dstOffset.y;
                m_DstOffset[2] = 0;
                m_DstOffset[3] = 0;

                cmd.SetComputeIntParams(cs, s_SrcOffsetAndLimit, m_SrcOffset);
                cmd.SetComputeIntParams(cs, s_DstOffset, m_DstOffset);
                cmd.SetComputeTextureParam(cs, kernel, s_DepthMipChain, texture);
                cmd.DispatchCompute(cs, kernel, RenderingUtils.DivRoundUp(dstSize.x, 8), RenderingUtils.DivRoundUp(dstSize.y, 8), texture.rt.volumeDepth);
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Shader == null)
            {
                Debug.LogErrorFormat("Missing {0}. DepthPyramid render pass will not execute. Check for missing reference in the renderer resources.", m_Shader);
                return;
            }
            if (m_DepthMipChainTexture == null || !m_DepthMipChainTexture.rt.IsCreated())
            {
                Debug.LogError("No valid DepthMipChainTexture for depth pyramid pass.");
                return;
            }

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.DepthPyramid)))
            {
                RenderMinDepthPyramid(cmd, m_DepthMipChainTexture, m_PackedMipChainInfo, m_Mip1AlreadyComputed);
                cmd.SetGlobalTexture(m_DepthMipChainTexture.name, m_DepthMipChainTexture);
            }
        }


        /// <inheritdoc/>
        //public override void OnCameraCleanup(CommandBuffer cmd)
        //{
        //    if (cmd == null)
        //        throw new ArgumentNullException("cmd");

        //}

        // RenderGraph Not Supported
    }

}
