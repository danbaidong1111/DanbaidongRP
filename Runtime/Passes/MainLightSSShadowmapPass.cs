using System;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class MainLightSSShadowmapPass : ScriptableRenderPass
    {
        static readonly int _SSShadowmapPid = Shader.PropertyToID("_CustomMainLightSSShadowmapTex");
        RenderTargetIdentifier _SSShadowmmapRti = new RenderTargetIdentifier(_SSShadowmapPid);

        int m_SSShadowmapWidth;
        int m_SSShadowmapHeight;

        ComputeShader m_SSShadowmapCompute;
        ComputeShader m_SSShadowmapBlur;

        MainLight8CascadeShadowCasterPass m_ShadowRenderPass = null;

        const int SHADER_NUMTHREAD_X = 8; //must match compute shader's [numthread(x)]
        const int SHADER_NUMTHREAD_Y = 8; //must match compute shader's [numthread(y)]

        const int BLUR_GROUPSIZE = 128;//must match blur compute shader's N

        RenderTargetHandle depthHandle;
        RenderTargetHandle normalHandle;
        public MainLightSSShadowmapPass(RenderPassEvent evt)
        {
            base.profilingSampler = new ProfilingSampler(nameof(MainLightSSShadowmapPass));
            renderPassEvent = evt;

            if (m_SSShadowmapCompute == null)
            {
                m_SSShadowmapCompute = (ComputeShader)(Resources.Load("CustomSSShadowmap"));
            }

            if (m_SSShadowmapBlur == null)
            {
                m_SSShadowmapBlur = (ComputeShader)(Resources.Load("SSShadowmapBlur"));
            }
        }

        float[] CalcGaussWeights(float sigma)
        {
            float twoSigma2 = 2.0f * sigma * sigma;

            // Estimate the blur radius based on sigma since sigma controls the "width" of the bell curve.
            // For example, for sigma = 3, the width of the bell curve is 
            int blurRadius = (int)Mathf.Ceil(2.0f * sigma);

            float[] weights = new float[2 * blurRadius + 1];

            float weightSum = 0.0f;

            for (int i = -blurRadius; i <= blurRadius; ++i)
            {
                float x = (float)i;

                weights[i + blurRadius] = Mathf.Exp(-x * x / twoSigma2);

                weightSum += weights[i + blurRadius];
            }

            // Divide by the sum so all the weights add up to 1.0.
            for (int i = 0; i < weights.Length; ++i)
            {
                weights[i] /= weightSum;
            }

            return weights;
        }

        public bool Setup(ref RenderingData renderingData, ref MainLight8CascadeShadowCasterPass shadowPass, RenderTargetHandle depthHandle, RenderTargetHandle normalHandle)
        {
            if (!m_SSShadowmapCompute)
                return false;
            if (!shadowPass.IsShadowmapReady())
                return false;


            m_SSShadowmapWidth = renderingData.cameraData.cameraTargetDescriptor.width;
            m_SSShadowmapHeight = renderingData.cameraData.cameraTargetDescriptor.height;

            m_ShadowRenderPass = shadowPass;

            this.depthHandle = depthHandle;
            this.normalHandle = normalHandle;

            return true;
        }

        public static RenderTexture GetTemporarySSShadowmapTexture(int width, int height)
        {
            var shadowTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.RFloat);
            shadowTexture.filterMode = FilterMode.Bilinear;
            shadowTexture.wrapMode = TextureWrapMode.Clamp;
            shadowTexture.enableRandomWrite = true;

            return shadowTexture;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {


            RenderTextureDescriptor rtd = new RenderTextureDescriptor(m_SSShadowmapWidth, m_SSShadowmapHeight, RenderTextureFormat.RFloat, 0, 0);
            rtd.enableRandomWrite = true;

            cmd.GetTemporaryRT(_SSShadowmapPid, rtd);

            //No need to set rendertarget
            //ConfigureTarget
            //ConfigureClear
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            cmd.ReleaseTemporaryRT(_SSShadowmapPid);

        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            if (m_SSShadowmapCompute == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("CustomSSShadowmap")))
            {
                int dispatchThreadGroupXCount = Mathf.CeilToInt((float)m_SSShadowmapWidth / SHADER_NUMTHREAD_X); //divide by shader's numthreads.x
                int dispatchThreadGroupYCount = Mathf.CeilToInt((float)m_SSShadowmapHeight / SHADER_NUMTHREAD_Y); //divide by shader's numthreads.y
                int dispatchThreadGroupZCount = 1; //divide by shader's numthreads.z

                int kernelIndex = m_SSShadowmapCompute.FindKernel("GenerateSSShadowmap");
                cmd.SetComputeVectorParam(m_SSShadowmapCompute, "_SSShadowmapSize", new Vector2(m_SSShadowmapWidth, m_SSShadowmapHeight));
                cmd.SetComputeVectorParam(m_SSShadowmapCompute, "_ShadowmapCacheSize", m_ShadowRenderPass.GetShadowmapSize());
                cmd.SetComputeIntParam(m_SSShadowmapCompute, "_ShadowCascadeResolution", m_ShadowRenderPass.GetCascadeResolution());

                cmd.SetComputeTextureParam(m_SSShadowmapCompute, kernelIndex, _SSShadowmapPid, _SSShadowmmapRti);
                cmd.SetComputeTextureParam(m_SSShadowmapCompute, kernelIndex, m_ShadowRenderPass.m_MainLightShadowmapCacahe.id, m_ShadowRenderPass.m_MainLightShadowmapCacaheTexture);
                cmd.SetComputeTextureParam(m_SSShadowmapCompute, kernelIndex, "_CameraDepthTexture", depthHandle.Identifier());

                cmd.DispatchCompute(m_SSShadowmapCompute, kernelIndex, dispatchThreadGroupXCount, dispatchThreadGroupYCount, dispatchThreadGroupZCount);
            }

            using (new ProfilingScope(cmd, new ProfilingSampler("CustomSSShadowmapBlur")))
            {
                //Blur
                int blurGroupsXCount = Mathf.CeilToInt((float)m_SSShadowmapWidth / BLUR_GROUPSIZE);
                int blurGroupsYCount = Mathf.CeilToInt((float)m_SSShadowmapHeight / BLUR_GROUPSIZE);

                int kernelIndex = m_SSShadowmapBlur.FindKernel("HorzBlurCS");
                //float[] weights = CalcGaussWeights(1.5f);
                //for(int i = 0;i< weights.Length;i++)
                //{
                //    Debug.Log(i + ": " + weights[i]);
                //}
                //cmd.SetComputeIntParam(m_SSShadowmapBlur, "gBlurRadius", weights.Length / 2);

                cmd.SetComputeVectorParam(m_SSShadowmapBlur, "_SSShadowmapSize", new Vector2(m_SSShadowmapWidth, m_SSShadowmapHeight));
                cmd.SetComputeTextureParam(m_SSShadowmapCompute, kernelIndex, "_CameraDepthTexture", depthHandle.Identifier());
                cmd.SetComputeTextureParam(m_SSShadowmapCompute, kernelIndex, "_CameraNormalsTexture", normalHandle.Identifier());

                cmd.SetComputeTextureParam(m_SSShadowmapBlur, kernelIndex, _SSShadowmapPid, _SSShadowmmapRti);
                cmd.DispatchCompute(m_SSShadowmapBlur, kernelIndex, blurGroupsXCount, m_SSShadowmapHeight, 1);

                kernelIndex = m_SSShadowmapBlur.FindKernel("VertBlurCS");
                cmd.SetComputeTextureParam(m_SSShadowmapBlur, kernelIndex, _SSShadowmapPid, _SSShadowmmapRti);
                cmd.DispatchCompute(m_SSShadowmapBlur, kernelIndex, m_SSShadowmapWidth, blurGroupsYCount, 1);

                cmd.SetGlobalTexture(_SSShadowmapPid, _SSShadowmmapRti);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

    }
}
