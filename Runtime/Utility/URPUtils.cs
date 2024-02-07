using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Experimental.Rendering;
using System.Text.RegularExpressions;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Various utility functions for URP.
    /// </summary>
    public class URPUtils
    {

        // We need these at runtime for RenderPipelineResources upgrade
        internal static string GetURPRenderPipelinePath()
            => "Packages/com.unity.render-pipelines.danbaidong/";

        internal static string GetCorePath()
            => "Packages/com.unity.render-pipelines.core/";


    }
}
