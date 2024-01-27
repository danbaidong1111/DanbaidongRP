#ifndef DANBAIDONG_TOON_INCLUDED
#define DANBAIDONG_TOON_INCLUDED

//-----------------------------------------------------------------------------
// Danbaidong Toon shading functions:
// InDirect diffuse is different from URP
//-----------------------------------------------------------------------------


///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

struct MY_PBR_DATA
{
    float metallic;
    float occlusion;
    float smoothness;
    float3 positionWS;
    float3 normalWS;
    float4 albedo;
};


///////////////////////////////////////////////////////////////////////////////
//                          Toon PBR functions                              //
///////////////////////////////////////////////////////////////////////////////
/*
 * 
 * Cook-Torrance BRDF:
 * fr = kd * flambert + ks * fcook-torrance
 * 
 * fcook-torrance = DFG/4(w0*n)(w1*n)
 * D法线分布函数(Normal Distribution Function)
 * F菲涅尔方程(Fresnel Rquation)
 * G几何函数(Geometry Function)
 */

/*
 * D: Trowbridge-Reitz GGX
 * in:  NdotHin = max(dot(N, H), 0.0);
 */

float DistributionGGX(float NdotHin,float roughnessSquare)
{
    float a2    = roughnessSquare;
    float NdotH = NdotHin;
    float NdotH2 = NdotH * NdotH;
    
    float nom   = a2;
    float denom = NdotH2 * (a2 - 1.0) + 1.0;
    denom       = PI * denom * denom;
    return nom/denom;
}

//Gsub Schlick-GGX
float GeometrySchlickGGX(float NdotV, float k)
{
    float nom = NdotV;
    float denom = NdotV * (1.0 - k) + k;
   
    return nom/denom;
}

/*
 * G: GeometrySmith
 * in:  NdotL, NdotV is max(dot, 0)
 *      kdirect = pow(roughness + 1, 2)/8;
 *      kIBL = pow(roughness, 2)/2
 */
float GeometrySmith(float NdotL,float NdotV,float k)
{
    float Gnv = GeometrySchlickGGX(NdotV, k);
    float Gnl = GeometrySchlickGGX(NdotL, k);
    return Gnv * Gnl;
}

/* 
 * F: fresnelSchlick
 * F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0)
 * 这里使用UE中把5次方的简化计算
 * 
 * direct use hv，微观
 * indirect use nv, 宏观
 * 
 * Unity使用hl，是一种对GGX shader渲染效果优化方法，http://filmicworlds.com/blog/optimizing-ggx-shaders-with-dotlh/
 */
real3 fresnelSchlick(float HdotV,float3 F0)
{
    float3 F = F0 + (1 - F0) * exp2((-5.55473 * HdotV - 6.98316 ) * HdotV);
    return F;
}

/* 
 * F: fresnelSchlick indirect
 */
real3 fresnelSchlickIndirect(float NdotV, float3 F0,float roughness)
{
    float3 F = exp2((-5.55473 * NdotV - 6.98316 ) * NdotV);
    return F0 + F * saturate(1 - roughness - F0);
}




// 间接光漫反射 球谐函数 光照探针
// GlobalIllumination.hlsl
half3 SH_IndirectionDiff(float3 normalWS)
{
    return SampleSH(normalWS);
    // real4 SHCoefficients[7];
    // SHCoefficients[0]=unity_SHAr;
    // SHCoefficients[1]=unity_SHAg;
    // SHCoefficients[2]=unity_SHAb;
    // SHCoefficients[3]=unity_SHBr;
    // SHCoefficients[4]=unity_SHBg;
    // SHCoefficients[5]=unity_SHBb;
    // SHCoefficients[6]=unity_SHC;
    // float3 Color=SampleSH9(SHCoefficients,normalWS);
    // return max(0, Color);
}

// 间接光高光 反射探针
real3 IndirSpeCube(float3 normalWS,float3 viewWS,float perceptualRoughness,float occlusion)
{
    float3 reflectDirWS=reflect(-viewWS,normalWS);
    return GlossyEnvironmentReflection(reflectDirWS, perceptualRoughness, occlusion);
    
    // roughness=roughness*(1.7-0.7*roughness);//Unity内部不是线性 调整下拟合曲线求近似
    // float MidLevel=roughness*6;//把粗糙度remap到0-6 7个阶级 然后进行lod采样
    // float4 speColor=SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0,reflectDirWS,MidLevel);//根据不同的等级进行采样
    // #if !defined(UNITY_USE_NATIVE_HDR)
    // return DecodeHDREnvironment(speColor,unity_SpecCube0_HDR)*occlusion;//用DecodeHDREnvironment将颜色从HDR编码下解码。可以看到采样出的rgbm是一个4通道的值，最后一个m存的是一个参数，解码时将前三个通道表示的颜色乘上xM^y，x和y都是由环境贴图定义的系数，存储在unity_SpecCube0_HDR这个结构中。
    // #else
    // return speColor.xyz*occlusion;
    // #endif
}

// 间接高光 曲线拟合 放弃LUT采样而使用曲线拟合
real3 IndirSpeFactor(float roughnessSquare, float smoothness,float metallic,float3 F0,float NdotV)
{
    float surfaceReduction = 1.0 / (roughnessSquare + 1.0);

    half oneMinusReflectivity = OneMinusReflectivityMetallic(metallic);
    half reflectivity = half(1.0) - oneMinusReflectivity;
    // #if defined(SHADER_API_GLES)//Lighting.hlsl 261行
    // float reflectivity = BRDFSpec.x;
    // #else
    // float reflectivity = max(max(BRDFSpec.x,BRDFSpec.y),BRDFSpec.z);
    // #endif
    
    half grazingTerm = saturate(smoothness + reflectivity);
    float fresnelTerm = Pow4(1.0 - NdotV);

    return  surfaceReduction * lerp(F0, grazingTerm, fresnelTerm);
}

// sigmoid sss feature
float sigmoid(float x, float center, float sharp) {
    float s;
    s = 1 / (1 + pow(100000, (-3 * sharp * (x - center))));
    return s;
};

float3 IndirectDiffuse(float3 normalWS, half upDirLerp, half4 selfEnvColor, half3 albedo, float3 F0, float NdotV, float roughness, float metallic, float occlusion)
{
    float3 SHNormal = lerp(normalWS, float3(0,1,0), upDirLerp);
	float3 SHColor = SampleSH(SHNormal);
    float3 envColor = lerp(SHColor, selfEnvColor.rgb, selfEnvColor.a);
	
	float3 indirKs = fresnelSchlickIndirect(NdotV, F0, roughness);
	float3 indirKd = (1 - indirKs) * (1 - metallic);
	float3 indirDiffColor = envColor * indirKd * albedo * occlusion;

    return indirDiffColor;
}

half4 SampleDirectShadowRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float lightRange)
{
    float2 shadowRampUV = float2(lightRange, 0.125);
    half4 shadowRampCol = SAMPLE_TEXTURE2D(RampTex, RampSampler, shadowRampUV);
    return shadowRampCol;
}

half4 SampleDirectSpecularRamp(TEXTURE2D_PARAM(RampTex, RampSampler), float specRange)
{
    float2 specRampUV = float2(specRange, 0.375);
    half4 specRampCol = SAMPLE_TEXTURE2D(RampTex, RampSampler, specRampUV);
    return specRampCol;
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

/*
 * PBR函数反射率方程见https://learnopengl-cn.github.io/07%20PBR/02%20Lighting/
 */
real3 FragLigthingPBR(Light mainLight, MY_PBR_DATA pbrData, out half3 dirResult, out half3 indirResult)
{
    float3 lightDirWS = normalize(mainLight.direction);
    float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - pbrData.positionWS);
    float3 normalWS = normalize(pbrData.normalWS);
    float metallic = pbrData.metallic;
    float occlusion = pbrData.occlusion;
    float smoothness = pbrData.smoothness;
    float4 albedo = pbrData.albedo;

    // float roughness = 1 - smoothness;
    // //Unity lerp roughtness
    // roughness = lerp(0.002,1,roughness);
    // float roughnessSquare = roughness * roughness;
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    float roughness           = max(PerceptualRoughnessToRoughness(perceptualRoughness), HALF_MIN_SQRT);
    float roughnessSquare     = max(roughness * roughness, HALF_MIN);

    float3 F0 = lerp(0.04, albedo, metallic);
    
    float3 halfDir = normalize(lightDirWS + viewDirWS);
    float NdotL = saturate(dot(normalWS, lightDirWS));
    float NdotV = saturate(dot(normalWS, viewDirWS));
    float NdotH = saturate(dot(normalWS, halfDir));
    float HdotV = saturate(dot(halfDir,  viewDirWS));

    float NDF = DistributionGGX(NdotH, roughnessSquare);
    float G = GeometrySmith(NdotL, NdotV,pow(roughness + 1, 2)/8);
    float3 F = fresnelSchlick(HdotV, F0);

    float3 kSpec = F;
    float3 kDiff = (1.0 - F) * (1.0 - metallic);

    float3 nom = NDF * G * F;
    float3 denom = 4.0 * NdotV * NdotL + 0.0001;
    float3 BRDFSpec = nom / denom;

    //direct
    float3 directLightResult = (kDiff * albedo + BRDFSpec * PI) * mainLight.color * mainLight.shadowAttenuation * NdotL;

    //indirect
    float3 SHColor = SH_IndirectionDiff(normalWS);
    float3 indirKs = fresnelSchlickIndirect(NdotV, F0, roughness);//环境光没有确切的H向量来计算fresnel，用NdotV模拟，粗糙度的影响。
    float3 indirKd = (1 - indirKs) * (1 - metallic);// Unity用的是OneMinusReflectivityMetallic(metallic)
    float3 indirDiffColor = indirKd * SHColor * albedo * occlusion;// irrandianceMap => SHColor

    // Unity未使用IBL贴图的计算
    float3 indirSpeCubeColor = IndirSpeCube(normalWS, viewDirWS, perceptualRoughness, occlusion);
    float3 indirSpeCubeFactor = IndirSpeFactor(roughnessSquare, smoothness, metallic, F0, NdotV);
    float3 indirSpecColor = indirSpeCubeColor * indirSpeCubeFactor;

    // //使用IBL贴图计算环境光镜面反射部分
    // float3 indirSpeCubeColor=IndirSpeCube(normalWS,viewDirWS,roughness,occlusion);// prefilterMap => specCube_0
    // float2 envBRDF = SAMPLE_TEXTURE2D(_IblBrdfLut, sampler_IblBrdfLut, float2(NdotV, roughness)).xy;
    // float3 indirSpecColor = indirSpeCubeColor * (indirKs * envBRDF.x + envBRDF.y);

    float3 indirColor = indirSpecColor + indirDiffColor;

    // Additional light
    float3 addDiffColor = 0;
    float3 addSpecColor = 0;
    #ifdef _ADDITIONAL_LIGHTS
        uint pixelLightCount = GetAdditionalLightsCount();
        for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
        {
            Light light = GetAdditionalLight(lightIndex, pbrData.positionWS);
            half3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
            
            //diff
            addDiffColor += LightingLambert(attenuatedLightColor, light.direction, normalWS);
            //addSpecColor += LightingSpecular(attenuatedLightColor, light.direction, normalWS, viewDirWS, 1.0, smoothness*10);
            
            //spec
            float3 halfAdd = normalize(light.direction+viewDirWS);
            float NdotAddH = saturate(dot(normalWS,halfAdd));
            float NdotAddL = saturate(dot(normalWS,light.direction));
            addSpecColor += DistributionGGX(NdotAddH,roughnessSquare) * attenuatedLightColor * NdotAddL;
        }
    #endif
    float3 addLightResult = addDiffColor * albedo + addSpecColor;
    dirResult = directLightResult;
    indirResult = indirColor + 0.3 * addLightResult;

    return directLightResult + indirColor + 0.3 * addLightResult;
}



struct Toon_a2v
{
	float4 vertex 	:POSITION;
	float3 normal 	:NORMAL;
	float4 tangent 	:TANGENT;
	float4 color  	:COLOR;
	float2 uv0 		:TEXCOORD0;
	float2 uv1 		:TEXCOORD1;
	UNITY_VERTEX_INPUT_INSTANCE_ID 
};
struct Toon_v2f 
{
	float4 positionHCS		:SV_POSITION;
    float3 positionWS   	:TEXCOORD0;
    float3 normalWS     	:TEXCOORD1;
    float3 tangentWS    	:TEXCOORD2;
    float3 biTangentWS  	:TEXCOORD3;
	float4 color 			:TEXCOORD4;
	float4 uv				:TEXCOORD5;// xy:uv0 zw:uv1
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Toon_v2f DanbaidongToonVert(Toon_a2v v)
{
	Toon_v2f o;
    
    UNITY_SETUP_INSTANCE_ID(v); 
    UNITY_TRANSFER_INSTANCE_ID(v,o); 
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	o.positionHCS = TransformObjectToHClip(v.vertex);
    o.positionWS = TransformObjectToWorld(v.vertex);
	o.normalWS = TransformObjectToWorldNormal(v.normal);
    o.tangentWS = TransformObjectToWorldDir(v.tangent.xyz);
    o.biTangentWS = cross(o.normalWS,o.tangentWS) * v.tangent.w * GetOddNegativeScale();
	o.color = v.color;
	o.uv.xy = v.uv0.xy;
    o.uv.zw = v.uv1.xy;

	return o;
}

// Frag not defined

#endif /* DANBAIDONG_TOON_INCLUDED */
