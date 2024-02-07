namespace UnityEngine.Rendering.Universal
{
    public abstract class UniversalRenderPipelineResources : RenderPipelineResources
    {
        protected override string packagePath => URPUtils.GetURPRenderPipelinePath();
    }
}
