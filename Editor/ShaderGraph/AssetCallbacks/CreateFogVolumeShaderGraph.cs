using UnityEditor.ShaderGraph;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    static class CreateFogVolumeShaderGraph
    {
        [MenuItem("Assets/Create/Shader Graph/Danbaidong/Fog Volume Shader Graph", priority = CoreUtils.Priorities.assetsCreateShaderMenuPriority + 6)]
        public static void CreateFogVolumeGraph()
        {
            var target = new UniversalTarget();
            target.TrySetActiveSubTarget(typeof(UniversalFogVolumeSubTarget));
            GraphUtil.CreateNewGraphWithOutputs(new[] { target }, new[] { FogVolumeBlocks.Albedo, FogVolumeBlocks.Density });
        }
    }
}
