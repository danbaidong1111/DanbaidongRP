using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    //-----------------------------------------------------------------------------
    // light extension
    //-----------------------------------------------------------------------------
    static class VisibleLightExtensionMethods
    {
        public struct VisibleLightAxisAndPosition
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
        }

        public static Vector3 GetPosition(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(3);
        }

        public static Vector3 GetForward(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(2);
        }

        public static Vector3 GetUp(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(1);
        }

        public static Vector3 GetRight(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(0);
        }

        public static VisibleLightAxisAndPosition GetAxisAndPosition(this VisibleLight value)
        {
            var matrix = value.localToWorldMatrix;
            VisibleLightAxisAndPosition output;
            output.Position = matrix.GetColumn(3);
            output.Forward = matrix.GetColumn(2);
            output.Up = matrix.GetColumn(1);
            output.Right = matrix.GetColumn(0);
            return output;
        }
    }

    //-----------------------------------------------------------------------------
    // structure definition
    //-----------------------------------------------------------------------------

    [GenerateHLSL]
    internal enum LightVolumeType
    {
        Cone,
        Sphere,
        Box,
        Count
    }

    [GenerateHLSL]
    internal enum LightCategory
    {
        Punctual,
        Area,
        Env,
        Decal,
        Count
    }

    [GenerateHLSL]
    internal enum LightFeatureFlags
    {
        // Light bit mask must match LightDefinitions.s_LightFeatureMaskFlags value
        Punctual = 1 << 12,
        Area = 1 << 13,
        Directional = 1 << 14,
        Env = 1 << 15,
        Sky = 1 << 16,
        SSRefraction = 1 << 17,
        SSReflection = 1 << 18,
        // If adding more light be sure to not overflow LightDefinitions.s_LightFeatureMaskFlags
    }

    // Caution: Order is important and is use for optimization in light loop
    [GenerateHLSL]
    internal enum GPULightType
    {
        Directional,
        Point,
        Spot,
        ProjectorPyramid,
        ProjectorBox,

        // AreaLight
        //Tube, // Keep Line lights before Rectangle. This is needed because of a compiler bug (see LightLoop.hlsl)
        //Rectangle,
        // Currently not supported in real time (just use for reference)
        //Disc,
        // Sphere,
    };

    //Do not change these numbers!!
    //Its not a full power of 2 because the last light slot is reserved.
    internal enum FPTLMaxLightSizes
    {
        Low = 31,
        High = 63
    }

    [GenerateHLSL]
    class LightDefinitions
    {
        public static int s_MaxNrBigTileLightsPlusOne = 512;      // may be overkill but the footprint is 2 bits per pixel using uint16.
        public static float s_ViewportScaleZ = 1.0f;
        public static int s_UseLeftHandCameraSpace = 1;

        public static int s_TileSizeFptl = 16;
        public static int s_TileSizeClustered = 32;
        public static int s_TileSizeBigTile = 64;

        // Tile indexing constants for indirect dispatch deferred pass : [2 bits for eye index | 15 bits for tileX | 15 bits for tileY]
        public static int s_TileIndexMask = 0x7FFF;
        public static int s_TileIndexShiftX = 0;
        public static int s_TileIndexShiftY = 15;
        public static int s_TileIndexShiftEye = 30;

        // feature variants
        public static int s_NumFeatureVariants = 29;

        // light list limits
        public static int s_LightListMaxCoarseEntries = 64;
        public static int s_LightClusterMaxCoarseEntries = 128;

        // We have room for ShaderConfig.FPTLMaxLightCount lights, plus 1 implicit value for length.
        // We allocate only 16 bits per light index & length, thus we divide by 2, and store in a word buffer.
        /// <summary>
        /// Maximum number of lights for a fine pruned light tile. This number can only be the prespecified possibilities in FPTLMaxLightSizes
        /// Lower count will mean some memory savings.
        /// Note: For any rendering bigger than 4k (in native) it is recommended to use Low count per tile, to avoid possible artifacts.
        /// </summary>
        public static int s_LightDwordPerFptlTile = (((int)FPTLMaxLightSizes.High + 1)) / 2;
        public static int s_LightClusterPackingCountBits = (int)Mathf.Ceil(Mathf.Log(Mathf.NextPowerOfTwo((int)FPTLMaxLightSizes.High), 2));
        public static int s_LightClusterPackingCountMask = (1 << s_LightClusterPackingCountBits) - 1;
        public static int s_LightClusterPackingOffsetBits = 32 - s_LightClusterPackingCountBits;
        public static int s_LightClusterPackingOffsetMask = (1 << s_LightClusterPackingOffsetBits) - 1;

        // Following define the maximum number of bits use in each feature category.
        public static uint s_LightFeatureMaskFlags = 0xFFF000;
        public static uint s_LightFeatureMaskFlagsOpaque = 0xFFF000 & ~((uint)LightFeatureFlags.SSRefraction); // Opaque don't support screen space refraction
        public static uint s_LightFeatureMaskFlagsTransparent = 0xFFF000 & ~((uint)LightFeatureFlags.SSReflection); // Transparent don't support screen space reflection
        public static uint s_MaterialFeatureMaskFlags = 0x000FFF;   // don't use all bits just to be safe from signed and/or float conversions :/

        // Screen space shadow flags
        public static uint s_RayTracedScreenSpaceShadowFlag = 0x1000;
        public static uint s_ScreenSpaceColorShadowFlag = 0x100;
        public static uint s_InvalidScreenSpaceShadow = 0xff;
        public static uint s_ScreenSpaceShadowIndexMask = 0xff;

        //Contact shadow bit definitions
        public static int s_ContactShadowFadeBits = 8;
        public static int s_ContactShadowMaskBits = 32 - s_ContactShadowFadeBits;
        public static int s_ContactShadowFadeMask = (1 << s_ContactShadowFadeBits) - 1;
        public static int s_ContactShadowMaskMask = (1 << s_ContactShadowMaskBits) - 1;

    }

    [GenerateHLSL]
    struct SFiniteLightBound
    {
        public Vector3 boxAxisX; // Scaled by the extents (half-size)
        public Vector3 boxAxisY; // Scaled by the extents (half-size)
        public Vector3 boxAxisZ; // Scaled by the extents (half-size)
        public Vector3 center;   // Center of the bounds (box) in camera space
        public float scaleXY;  // Scale applied to the top of the box to turn it into a truncated pyramid (X = Y)
        public float radius;     // Circumscribed sphere for the bounds (box)
    };

    [GenerateHLSL]
    struct LightVolumeData
    {
        public Vector3 lightPos;     // Of light's "origin"
        public uint lightVolume;     // Type index

        public Vector3 lightAxisX;   // Normalized
        public uint lightCategory;   // Category index

        public Vector3 lightAxisY;   // Normalized
        public float radiusSq;       // Cone and sphere: light range squared

        public Vector3 lightAxisZ;   // Normalized
        public float cotan;          // Cone: cotan of the aperture (half-angle)

        public Vector3 boxInnerDist; // Box: extents (half-size) of the inner box
        public uint featureFlags;

        public Vector3 boxInvRange;  // Box: 1 / (OuterBoxExtents - InnerBoxExtents)
        public float unused2;
    };

    /// <summary>
    /// unsafe for array
    /// </summary>
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    //unsafe struct ShaderVariablesLightList
    struct ShaderVariablesLightList
    {
        public Matrix4x4 g_mInvScrProjectionArr;
        public Matrix4x4 g_mScrProjectionArr;
        public Matrix4x4 g_mInvProjectionArr;
        public Matrix4x4 g_mProjectionArr;

        public Vector4 g_screenSize;

        public Vector2Int g_viDimensions;
        public int g_iNrVisibLights;
        public uint g_isOrthographic;

        public uint g_BaseFeatureFlags;
        public int g_iNumSamplesMSAA;
        public uint _EnvLightIndexShift;
        public uint _DecalIndexShift;

        // From HDRP ShaderVariablesGlobal
        // Tile/Cluster
        public uint _NumTileFtplX;
        public uint _NumTileFtplY;
        public float g_fClustScale;
        public float g_fClustBase;

        public float g_fNearPlane;
        public float g_fFarPlane;
        public int g_iLog2NumClusters; // We need to always define these to keep constant buffer layouts compatible
        public uint g_isLogBaseBufferEnabled;

        public uint _NumTileClusteredX;
        public uint _NumTileClusteredY;
        public uint _DirectionalLightCount;
        public int _EnvSliceSize; // Unused

        //public uint _EnableDecalLayers;
    }


    [GenerateHLSL(PackingRules.Exact, false)]
    struct GPULightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 lightPosWS;
        public uint lightLayerMask;

        public Vector3 lightColor;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 lightDirection;
        public int shadowLightIndex;

        public Vector4 lightOcclusionProbInfo;
        
        public int cookieLightIndex;
        public int shadowType;
        public float minRoughness;
        public float __unused1__; 
        
        public float baseContribution;
        public float rimContribution;
        public float outlineContribution;
        public float __unused2__;

    };

    [GenerateHLSL(PackingRules.Exact, false)]
    struct DirectionalLightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 lightPosWS;
        public uint lightLayerMask;

        public Vector3 lightColor;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 lightDirection;
        public int shadowlightIndex;

        public float minRoughness;
        public float lightDimmer;       //TODO: make it used
        public float diffuseDimmer;     //TODO: make it used
        public float specularDimmer;    //TODO: make it used
    };

    [GenerateHLSL(PackingRules.Exact, false)]
    struct EnvLightData
    {
        // EnvLightData is in ReflectionProbeManager, we just need this struct for index.
    }

    //-----------------------------------------------------------------------------
    // render pass
    //-----------------------------------------------------------------------------

    /// <summary>
    /// Main Class
    /// </summary>
    public class GPULights : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_ScreenSpaceAABBTag = "ScreenSpaceAABB";
        private static string m_CoarseCullingTag = "CoarseCulling";
        private static string m_ClusterCullingTag = "ClusterCulling";
        private static string m_ClearLightListsTag = "ClearLightLists";
        private static ProfilingSampler m_ScreenSpaceAABBSampler = new ProfilingSampler(m_ScreenSpaceAABBTag);
        private static ProfilingSampler m_CoarseCullingSampler = new ProfilingSampler(m_CoarseCullingTag);
        private static ProfilingSampler m_ClusterCullingSampler = new ProfilingSampler(m_ClusterCullingTag);
        private static ProfilingSampler m_ClearLightListsSampler = new ProfilingSampler(m_ClearLightListsTag);

        // Public Variables
        internal ShaderVariablesLightList lightCBuffer;

        private int m_MaxDirectionalLightsOnScreen = 16;
        private int m_MaxPunctualLightsOnScreen = 512;
        // TODO: change to m_MaxDirectionalLightsOnScreen + m_MaxPunctualLightsOnScreen(512) + m_MaxAreaLightsOnScreen + m_MaxEnvLightsOnScreen
        private int m_MaxLightOnScreen = 16 + 512;

        // Private Variables
        private ComputeShader m_gpulightsCS_ClearLists;
        private ComputeShader m_gpuLightsCS_CoarseCulling;
        private ComputeShader m_gpuLightsCS_FPTL;
        private ComputeShader m_gpuLightsCS_Cluster;
        private int m_ClearKernel;
        private int m_ScreenSpaceAABBKernel;
        private int m_CoarseCullingLightsKernel;
        private int m_ClusterCullingLightsKernel;

        private GPULightsDataBuildSystem m_GPULightsDataBuildSystem;

        // Constants
        private const int k_Log2NumClusters = 6; // accepted range is from 0 to 6 (NR_THREADS is set to 64). NumClusters is 1<<g_iLog2NumClusters
        private const float k_ClustLogBase = 1.02f;     // each slice 2% bigger than the previous

        // Statics
        // Left-handed to right-handed
        static readonly Matrix4x4 s_FlipMatrixLHSRHS = Matrix4x4.Scale(new Vector3(1, 1, -1));

        public GPULights(UniversalRenderPipelineRuntimeShaders shaderResources, RenderPassEvent passEvent)
        {
            m_gpulightsCS_ClearLists        = shaderResources.gpuLightsClearLists;
            m_gpuLightsCS_CoarseCulling     = shaderResources.gpuLightsCoarseCullingCS;
            m_gpuLightsCS_FPTL              = shaderResources.gpuLightsFPTL;
            m_gpuLightsCS_Cluster           = shaderResources.gpuLightsCluster;

            m_ClearKernel                   = m_gpulightsCS_ClearLists.FindKernel("ClearList");
            m_ScreenSpaceAABBKernel         = m_gpuLightsCS_CoarseCulling.FindKernel("ScreenSpaceAABB");
            m_CoarseCullingLightsKernel     = m_gpuLightsCS_CoarseCulling.FindKernel("CoarseCullingLights");
            m_ClusterCullingLightsKernel    = m_gpuLightsCS_Cluster.FindKernel("ClusterCullingLights");

            renderPassEvent = passEvent;
            lightCBuffer = new ShaderVariablesLightList();
        }

        /// <summary>
        /// Compute and upload light CBUFFER
        /// </summary>
        /// <param name="context"></param>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal bool Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            return true;
        }

        internal void PreSetup(UniversalLightData lightData, UniversalCameraData cameraData, GPULightsDataBuildSystem gpuLightsDataBuildSystem)
        {
            m_GPULightsDataBuildSystem = gpuLightsDataBuildSystem;

            var desc = cameraData.cameraTargetDescriptor;
            int width = desc.width;
            int height = desc.height;

            var temp = new Matrix4x4();
            temp.SetRow(0, new Vector4(0.5f * width, 0.0f, 0.0f, 0.5f * width));
            temp.SetRow(1, new Vector4(0.0f, 0.5f * height, 0.0f, 0.5f * height));
            temp.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            var temp2 = new Matrix4x4();
            temp2.SetRow(0, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            temp2.SetRow(1, new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
            temp2.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            temp2.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            // camera to screen matrix (and it's inverse)
            {
                //Matrix4x4 gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix(cameraData.IsCameraProjectionMatrixFlipped());
                Matrix4x4 projMatrix = cameraData.GetProjectionMatrix();

                projMatrix *= s_FlipMatrixLHSRHS;
                
                lightCBuffer.g_mScrProjectionArr = temp * projMatrix;
                lightCBuffer.g_mInvScrProjectionArr = lightCBuffer.g_mScrProjectionArr.inverse;
                
                lightCBuffer.g_mProjectionArr = temp2 * projMatrix;
                lightCBuffer.g_mInvProjectionArr = lightCBuffer.g_mProjectionArr.inverse;

            }

            var scaledCameraWidth = (float)cameraData.cameraTargetDescriptor.width;
            var scaledCameraHeight = (float)cameraData.cameraTargetDescriptor.height;

            if (cameraData.camera.allowDynamicResolution)
            {
                scaledCameraWidth *= ScalableBufferManager.widthScaleFactor;
                scaledCameraHeight *= ScalableBufferManager.heightScaleFactor;
            }

            int envLightsCount = gpuLightsDataBuildSystem.envLightsCount;
            int additionalLightsCount = lightData.additionalLightsCount;
            lightCBuffer.g_iNrVisibLights = additionalLightsCount + envLightsCount;
            lightCBuffer._DirectionalLightCount = (uint)lightData.directionalLightsCount;

            /// <see cref="ScriptableRenderer"/> cmd.SetGlobalVector(ShaderPropertyId.screenSize...
            lightCBuffer.g_screenSize = new Vector4(scaledCameraWidth, scaledCameraHeight, 1.0f / scaledCameraWidth, 1.0f / scaledCameraHeight);
            lightCBuffer.g_viDimensions = new Vector2Int((int)scaledCameraWidth, (int)scaledCameraHeight);
            lightCBuffer.g_isOrthographic = cameraData.camera.orthographic ? 1u : 0u;
            //lightCBuffer.g_BaseFeatureFlags = 0; // Filled for each individual pass.
            //lightCBuffer.g_iNumSamplesMSAA = msaaSamples;
            lightCBuffer._EnvLightIndexShift = (uint)additionalLightsCount;
            lightCBuffer._DecalIndexShift = (uint)(additionalLightsCount + envLightsCount);

            const float C = (float)(1 << k_Log2NumClusters);
            var geomSeries = (1.0 - Mathf.Pow(k_ClustLogBase, C)) / (1 - k_ClustLogBase); // geometric series: sum_k=0^{C-1} base^k

            // Tile/Cluster
            lightCBuffer._NumTileFtplX = (uint)RenderingUtils.DivRoundUp(width, LightDefinitions.s_TileSizeFptl);
            lightCBuffer._NumTileFtplY = (uint)RenderingUtils.DivRoundUp(height, LightDefinitions.s_TileSizeFptl);
            lightCBuffer.g_fClustScale = (float)(geomSeries / (cameraData.camera.farClipPlane - cameraData.camera.nearClipPlane)); ;
            lightCBuffer.g_fClustBase = k_ClustLogBase;
            lightCBuffer.g_fNearPlane = cameraData.camera.nearClipPlane;
            lightCBuffer.g_fFarPlane = cameraData.camera.farClipPlane;
            lightCBuffer.g_iLog2NumClusters = k_Log2NumClusters;
            lightCBuffer.g_isLogBaseBufferEnabled = 1;// Need depth
            lightCBuffer._NumTileClusteredX = (uint)RenderingUtils.DivRoundUp(width, LightDefinitions.s_TileSizeClustered);
            lightCBuffer._NumTileClusteredY = (uint)RenderingUtils.DivRoundUp(height, LightDefinitions.s_TileSizeClustered);
        }

        private class GPULightsPassData
        {
            internal UniversalLightData lightData;
            internal GPULightsDataBuildSystem gpuLightsDataBuildSystem;

            // Compute Shaders
            internal ComputeShader gpuLightsClearLists;
            internal ComputeShader gpuLightsCoarseCullingCS;
            internal ComputeShader gpuLightsFPTL;
            internal ComputeShader gpuLightsCluster;
            internal int clearKernel;
            internal int screenSpaceAABBKernel;
            internal int coarseCullingLightsKernel;
            internal int clusterCullingLightsKernel;

            internal int nrBigTilesX;
            internal int nrBigTilesY;

            internal int nrClustersX;
            internal int nrClustersY;

            // LightsData
            internal ShaderVariablesLightList lightListCB;
            internal BufferHandle lightBoundsBuffer;
            internal BufferHandle lightVolumeDataBuffer;

            // AABB, CoarseCull Buffer
            internal BufferHandle AABBBoundsBuffer;
            internal BufferHandle coarseLightList;

            // ClusterBuffer
            internal BufferHandle globalLightListAtomic;
            internal BufferHandle perVoxelOffset;
            internal BufferHandle perVoxelLightLists;
            internal BufferHandle perTileLogBaseTweak;
        }

        public class GPULightsOutPassData : ContextItem
        {
            internal ShaderVariablesLightList lightListCB;
            // LightsDataBuffer
            internal BufferHandle GPULightsData;
            internal BufferHandle directionalLightsData;
            //internal BufferHandle envLightsData;

            // CoarseBuffer
            internal BufferHandle coarseLightList;
            // ClusterBuffer
            internal BufferHandle perVoxelOffset;
            internal BufferHandle perVoxelLightLists;
            internal BufferHandle perTileLogBaseTweak;
            public override void Reset()
            {
                // We should always reset texture handles since they are only vaild for the current frame.
                lightListCB = new ShaderVariablesLightList();
                GPULightsData = BufferHandle.nullHandle;
                directionalLightsData = BufferHandle.nullHandle;
                perVoxelOffset = BufferHandle.nullHandle;
                perVoxelLightLists = BufferHandle.nullHandle;
                perTileLogBaseTweak = BufferHandle.nullHandle;
            }
        }

        internal static GPULightsOutPassData UseRayTracingLights(IComputeRenderGraphBuilder builder, ContextContainer frameData)
        {
            var lights = frameData.Get<GPULightsOutPassData>();
            // Closest-hit shaders read these buffers even when the ray-generation shader does not.
            builder.UseBuffer(lights.GPULightsData, AccessFlags.Read);
            builder.UseBuffer(lights.directionalLightsData, AccessFlags.Read);

            var resources = frameData.Get<UniversalResourceData>();
            if (resources.directionalShadowsTexture.IsValid())
                builder.UseTexture(resources.directionalShadowsTexture, AccessFlags.Read);
            if (resources.additionalShadowsTexture.IsValid())
                builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
            return lights;
        }

        internal static void BindRayTracingLights(ComputeCommandBuffer cmd, RayTracingShader shader, GPULightsOutPassData lights)
        {
            // Bind the current camera's data explicitly; raster globals do not declare graph lifetimes.
            ConstantBuffer.PushGlobal(cmd, lights.lightListCB, ShaderConstants.ShaderVariablesLightList);
            cmd.SetRayTracingBufferParam(shader, ShaderConstants.g_GPULightDatas, lights.GPULightsData);
            cmd.SetRayTracingBufferParam(shader, ShaderConstants.g_DirectionalLightDatas, lights.directionalLightsData);
        }

        private void InitResources(RenderGraph renderGraph, GPULightsPassData passData, UniversalLightData lightData, GPULightsOutPassData outData, UniversalCameraData cameraData)
        {
            // Copy the constant buffer into the parameter struct.
            passData.lightListCB = lightCBuffer;

            passData.lightData = lightData;
            passData.gpuLightsDataBuildSystem = m_GPULightsDataBuildSystem;

            // Compute Shaders
            passData.gpuLightsClearLists = m_gpulightsCS_ClearLists;
            passData.gpuLightsCoarseCullingCS = m_gpuLightsCS_CoarseCulling;
            passData.gpuLightsFPTL = m_gpuLightsCS_FPTL;
            passData.gpuLightsCluster = m_gpuLightsCS_Cluster;
            passData.clearKernel = m_ClearKernel;
            passData.screenSpaceAABBKernel = m_ScreenSpaceAABBKernel;
            passData.coarseCullingLightsKernel = m_CoarseCullingLightsKernel;
            passData.clusterCullingLightsKernel = m_ClusterCullingLightsKernel;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.nrBigTilesX = RenderingUtils.DivRoundUp(width, 64);
            passData.nrBigTilesY = RenderingUtils.DivRoundUp(height, 64);


            var bufferSystem = GraphicsBufferSystem.instance;
            int allLightsBufferSize = m_MaxLightOnScreen;

            GraphicsBuffer lightBoundsBuf = bufferSystem.GetGraphicsBuffer<SFiniteLightBound>(GraphicsBufferSystemBufferID.GPULightsLightBoundsBuffer, m_MaxLightOnScreen, "lightBoundsBuffer");
            GraphicsBuffer lightVolumeDataBuf = bufferSystem.GetGraphicsBuffer<LightVolumeData>(GraphicsBufferSystemBufferID.GPULightsLightVolumeDataBuffer, m_MaxLightOnScreen, "lightVolumeDataBuffer");

            passData.lightBoundsBuffer = renderGraph.ImportBuffer(lightBoundsBuf, bufferName: "lightBoundsBuffer");
            passData.lightVolumeDataBuffer = renderGraph.ImportBuffer(lightVolumeDataBuf, bufferName: "lightVolumeDataBuffer");
            //passData.lightBoundsBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize, Marshal.SizeOf(typeof(SFiniteLightBound)), "lightBoundsBuffer"));
            //passData.lightVolumeDataBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize, Marshal.SizeOf(typeof(LightVolumeData)), "lightVolumeDataBuffer"));

            passData.AABBBoundsBuffer = renderGraph.CreateBuffer(new BufferDesc(allLightsBufferSize, Marshal.SizeOf(typeof(float4)), "AABBBoundsBuffer"));
            passData.coarseLightList = renderGraph.CreateBuffer(new BufferDesc(LightDefinitions.s_MaxNrBigTileLightsPlusOne * passData.nrBigTilesX * passData.nrBigTilesY, sizeof(uint), "coarseLightList"));


            // Cluster buffers
            passData.nrClustersX = (width + LightDefinitions.s_TileSizeClustered - 1) / LightDefinitions.s_TileSizeClustered;
            passData.nrClustersY = (height + LightDefinitions.s_TileSizeClustered - 1) / LightDefinitions.s_TileSizeClustered;
            var nrClusterTiles = passData.nrClustersX * passData.nrClustersY;

            passData.globalLightListAtomic = renderGraph.CreateBuffer(new BufferDesc(1, sizeof(uint), "globalLightListAtomic")); 
            passData.perVoxelLightLists = renderGraph.CreateBuffer(new BufferDesc(32 * (1 << k_Log2NumClusters) * nrClusterTiles, sizeof(uint), "perVoxelLightLists")); ;
            passData.perVoxelOffset = renderGraph.CreateBuffer(new BufferDesc((int)LightCategory.Count * (1 << k_Log2NumClusters) * nrClusterTiles, sizeof(uint), "perVoxelOffset")); 
            passData.perTileLogBaseTweak = renderGraph.CreateBuffer(new BufferDesc(nrClusterTiles, sizeof(float), "perTileLogBaseTweak"));

            // Outdata
            outData.lightListCB = passData.lightListCB;
            outData.GPULightsData = renderGraph.CreateBuffer(new BufferDesc(m_MaxPunctualLightsOnScreen, Marshal.SizeOf(typeof(GPULightData)), "GPULightsData"));
            outData.directionalLightsData = renderGraph.CreateBuffer(new BufferDesc(m_MaxDirectionalLightsOnScreen, Marshal.SizeOf(typeof(DirectionalLightData)), "directionalLightsData"));

            outData.coarseLightList = passData.coarseLightList;

            outData.perVoxelOffset = passData.perVoxelOffset;
            outData.perVoxelLightLists = passData.perVoxelLightLists;
            outData.perTileLogBaseTweak = passData.perTileLogBaseTweak;
        }

        /// <summary>
        /// Clear one compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="bufferToClear"></param>
        static void ClearLightList(ComputeCommandBuffer cmd, GPULightsPassData data, GraphicsBuffer bufferToClear)
        {
            Vector2 countAndOffset = new Vector2Int(bufferToClear.count, 0);
            int totalNumberOfGroupsNeeded = RenderingUtils.DivRoundUp(bufferToClear.count, 64);
            const int maxAllowedGroups = 65535;// On higher resolutions we might end up with more than 65535 group which is not allowed, so we need to to have multiple dispatches.

            cmd.SetComputeBufferParam(data.gpuLightsClearLists, data.clearKernel, "_LightListToClear", bufferToClear);

            int i = 0;
            while (totalNumberOfGroupsNeeded > 0)
            {
                countAndOffset.y = maxAllowedGroups * i;
                cmd.SetComputeVectorParam(data.gpuLightsClearLists, "_LightListEntriesAndOffset", countAndOffset);

                int currGroupCount = Math.Min(maxAllowedGroups, totalNumberOfGroupsNeeded);

                cmd.DispatchCompute(data.gpuLightsClearLists, data.clearKernel, currGroupCount, 1, 1);

                totalNumberOfGroupsNeeded -= currGroupCount;
                i++;
            }
        }

        /// <summary>
        /// Clear all light lists compute buffer.
        /// </summary>
        /// <param name="cmd"></param>
        static void ClearAllLightLists(ComputeCommandBuffer cmd, GPULightsPassData data)
        {
            using (new ProfilingScope(cmd, m_ClearLightListsSampler))
            {
                if (data.coarseLightList.IsValid())
                {
                    ClearLightList(cmd, data, data.coarseLightList);
                    ClearLightList(cmd, data, data.perVoxelOffset);
                }
            }
        }



        static void ExecutePass(GPULightsPassData data, ComputeGraphContext context)
        {
            // TODO: We should add envLights(probe) and decals as HDRP.
            int totalLightCount = data.lightListCB.g_iNrVisibLights;
            if (totalLightCount == 0)
            {
                ClearAllLightLists(context.cmd, data);
                return;
            }

            var cmd = context.cmd;
            // Set lightsData here.
            cmd.SetBufferData(data.lightBoundsBuffer, data.gpuLightsDataBuildSystem.lightBounds, 0, 0, data.gpuLightsDataBuildSystem.boundsCount);
            cmd.SetBufferData(data.lightVolumeDataBuffer, data.gpuLightsDataBuildSystem.lightVolumes, 0, 0, data.gpuLightsDataBuildSystem.boundsCount);
            // Push Constant buffer
            ConstantBuffer.Push(cmd, data.lightListCB, data.gpuLightsCoarseCullingCS, ShaderConstants.ShaderVariablesLightList);

            // GenerateLightsScreenSpaceAABBs
            using (new ProfilingScope(cmd, m_ScreenSpaceAABBSampler))
            {
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel, ShaderConstants.g_LightBounds, data.lightBoundsBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel, ShaderConstants.g_vBoundsBuffer, data.AABBBoundsBuffer);// out

                const int threadsPerLight = 4;  // Shader: THREADS_PER_LIGHT (4)
                const int threadsPerGroup = 64; // Shader: THREADS_PER_GROUP (64)

                int groupCount = RenderingUtils.DivRoundUp(totalLightCount * threadsPerLight, threadsPerGroup);
                cmd.DispatchCompute(data.gpuLightsCoarseCullingCS, data.screenSpaceAABBKernel, groupCount, 1, 1);
            }

            // CoarseCullingLights
            using (new ProfilingScope(cmd, m_CoarseCullingSampler))
            {
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, ShaderConstants.g_LightVolumeData, data.lightVolumeDataBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, ShaderConstants.g_LightBounds, data.lightBoundsBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, ShaderConstants.g_vBoundsBuffer, data.AABBBoundsBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, ShaderConstants.g_vLightList, data.coarseLightList);// out

                cmd.DispatchCompute(data.gpuLightsCoarseCullingCS, data.coarseCullingLightsKernel, data.nrBigTilesX, data.nrBigTilesY, 1);
            }

            // FPTLLights
            {
                // no implementation
            }

            // ClusterLights
            using (new ProfilingScope(cmd, m_ClusterCullingSampler))
            {
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_LightVolumeData, data.lightVolumeDataBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_LightBounds, data.lightBoundsBuffer);// in

                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_vBoundsBuffer, data.AABBBoundsBuffer);// in
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_CoarseLightList, data.coarseLightList);// in

                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_vLayeredLightList, data.perVoxelLightLists);// out
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_LayeredOffset, data.perVoxelOffset);// out
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_LayeredSingleIdxBuffer, data.globalLightListAtomic);// used
                cmd.SetComputeBufferParam(data.gpuLightsCluster, data.clusterCullingLightsKernel, ShaderConstants.g_logBaseBuffer, data.perTileLogBaseTweak);// out
                ConstantBuffer.Push(cmd, data.lightListCB, data.gpuLightsCluster, ShaderConstants.ShaderVariablesLightList);

                cmd.DispatchCompute(data.gpuLightsCluster, data.clusterCullingLightsKernel, data.nrClustersX, data.nrClustersY, 1);
            }

            // Use RenderSetGlobalAsync to set global
            // Resolve
            {
                //ResolveGPULightsData(cmd, data);
            }


        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            // We need reBuild GPULightsData at main thread.
            m_GPULightsDataBuildSystem.ReBuildGPULightsDataBuffer(frameData.Get<UniversalLightData>());

            using (var builder = renderGraph.AddComputePass<GPULightsPassData>("GPU Lights", out var passData, ProfilingSampler.Get(URPProfileId.GPULights)))
            {
                // Access resources.
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Set passData
                GPULightsOutPassData outPassData = frameData.GetOrCreate<GPULightsOutPassData>();
                InitResources(renderGraph, passData, lightData, outPassData, cameraData);

                // Declare input/output
                builder.UseBuffer(passData.lightBoundsBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.lightVolumeDataBuffer, AccessFlags.Write);

                builder.UseBuffer(passData.AABBBoundsBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.coarseLightList, AccessFlags.Write);

                builder.UseBuffer(passData.globalLightListAtomic, AccessFlags.Write);
                builder.UseBuffer(passData.perVoxelOffset, AccessFlags.Write);
                builder.UseBuffer(passData.perVoxelLightLists, AccessFlags.Write);
                builder.UseBuffer(passData.perTileLogBaseTweak, AccessFlags.Write);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                // Setup builder state
#if DANBAIDONGRP_ASYNC_COMPUTE
                builder.EnableAsyncCompute(true);
#endif
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((GPULightsPassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });
            }
        }

        private class GlobalSyncPassData
        {
            internal GPULightsDataBuildSystem gpuLightsDataBuildSystem;
            internal GPULightsOutPassData outData;
        }

        static void ResolveGPULightsData(ComputeCommandBuffer cmd, GlobalSyncPassData data)
        {
            var outData = data.outData;
            cmd.SetBufferData(outData.GPULightsData, data.gpuLightsDataBuildSystem.gpuLightsData, 0, 0, data.gpuLightsDataBuildSystem.lightsCount);
            cmd.SetBufferData(outData.directionalLightsData, data.gpuLightsDataBuildSystem.directionalLightsData, 0, 0, data.gpuLightsDataBuildSystem.directionalLightCount);

            // We inited lightCBuffer at preSetup.
            ConstantBuffer.PushGlobal(cmd, outData.lightListCB, ShaderConstants.ShaderVariablesLightList);
            // Lights data ref
            cmd.SetGlobalBuffer(ShaderConstants.g_GPULightDatas, outData.GPULightsData);
            cmd.SetGlobalBuffer(ShaderConstants.g_DirectionalLightDatas, outData.directionalLightsData);
            // Coarse cull result
            cmd.SetGlobalBuffer(ShaderConstants.g_CoarseLightList, outData.coarseLightList);
            // Cluster cull result
            cmd.SetGlobalBuffer(ShaderConstants.g_vLightListCluster, outData.perVoxelLightLists);
            cmd.SetGlobalBuffer(ShaderConstants.g_vLayeredOffsetsBuffer, outData.perVoxelOffset);
            cmd.SetGlobalBuffer(ShaderConstants.g_logBaseBuffer, outData.perTileLogBaseTweak);

            cmd.SetKeyword(ShaderGlobalKeywords.GPULightsCluster, true);
        }

        internal void RenderSetGlobalSync(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<GlobalSyncPassData>("GPU Lights Global Sync", out var passData, ProfilingSampler.Get(URPProfileId.GPULightsGlobalAsync)))
            {
                if (!frameData.Contains<GPULightsOutPassData>())
                {
                    return;
                }
                var outData = frameData.Get<GPULightsOutPassData>();
                passData.gpuLightsDataBuildSystem = m_GPULightsDataBuildSystem;
                passData.outData = outData;

                builder.UseBuffer(outData.GPULightsData, AccessFlags.Write);
                builder.UseBuffer(outData.directionalLightsData, AccessFlags.Write);

                builder.UseBuffer(outData.coarseLightList);

                builder.UseBuffer(outData.perVoxelOffset);
                builder.UseBuffer(outData.perVoxelLightLists);
                builder.UseBuffer(outData.perTileLogBaseTweak);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((GlobalSyncPassData data, ComputeGraphContext context) =>
                {
                    ResolveGPULightsData(context.cmd, data);
                });
            }

        }

        /// <inheritdoc/>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            // Clean Keyword if need
            cmd.SetKeyword(ShaderGlobalKeywords.GPULightsCluster, false);
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
            //m_RenderTarget?.Release();
        }

        static class ShaderConstants
        {
            public static readonly int g_LightBounds            = Shader.PropertyToID("g_LightBounds");
            public static readonly int g_vBoundsBuffer          = Shader.PropertyToID("g_vBoundsBuffer");
            public static readonly int g_LightVolumeData        = Shader.PropertyToID("g_LightVolumeData");
            public static readonly int g_vLightList             = Shader.PropertyToID("g_vLightList");
            public static readonly int ShaderVariablesLightList = Shader.PropertyToID("ShaderVariablesLightList");
            public static readonly int g_CoarseLightList        = Shader.PropertyToID("g_CoarseLightList");
            public static readonly int g_vLayeredLightList      = Shader.PropertyToID("g_vLayeredLightList");
            public static readonly int g_LayeredOffset          = Shader.PropertyToID("g_LayeredOffset");
            public static readonly int g_LayeredSingleIdxBuffer = Shader.PropertyToID("g_LayeredSingleIdxBuffer");
            public static readonly int g_vLightListCluster      = Shader.PropertyToID("g_vLightListCluster");
            public static readonly int g_vLayeredOffsetsBuffer  = Shader.PropertyToID("g_vLayeredOffsetsBuffer");
            public static readonly int g_logBaseBuffer          = Shader.PropertyToID("g_logBaseBuffer");
            public static readonly int g_GPULightDatas          = Shader.PropertyToID("g_GPULightDatas");
            public static readonly int g_DirectionalLightDatas  = Shader.PropertyToID("g_DirectionalLightDatas");
        }
    }

}
