Shader "Hidden"
{
    Properties
	{
		[Header(Header)]
		[NoScaleOffset]_MainTex ("MainTex", 2D) = "white" { }
		[HDR] _Color ("Color", Color) = (1, 1, 1, 1)

        [FoldoutBegin(_FoldoutEnd1)]_Foldout("Foldout1", float) = 0
        _Float1 ("float1", float) = 2
        _MainTex2 ("MainTex2", 2D) = "white" { }
        [RangeSlider(_SliderMin0, _SliderMax0)]_RangeSlider0("RangeSlider0", Range(-5, 5)) = 0
        _SliderMin0("SliderMin0", float) = 0
        _SliderMax0("SliderMax0", float) = 1
        [HDR] _Color2 ("Color2", Color) = (1, 1, 1.1, 1)
        [Ramp]_RampTex0 ("RampTex0", 2D) = "white" { }
        [FoldoutEnd]_FoldoutEnd1("FoldoutEnd1", float) = 0

        [FoldoutBegin(_FoldoutEnd2, _FOLDOUT_KEY1)]_FoldoutWithKey("FoldoutWithKey", float) = 0
        [HideInInspector]_FOLDOUT_KEY1("_FOLDOUT_KEY1", float) = 0
        _Float2 ("float2", float) = 1
        [HDR] _Color3 ("Color3", Color) = (1, 1, 1.5, 1)
        [KeysEnum(_KEY1, _KEY2, _KEY3)]_KeywordsEnum("KeywordsEnum", float) = 0

        [FoldoutEnd]_FoldoutEnd2("FoldoutEnd2", float) = 0


        [RangeSlider(_SliderMin, _SliderMax)]_RangeSlider1("RangeSlider", Range(0, 5)) = 0
        _SliderMin("SliderMin", float) = 0
        _SliderMax("SliderMax", float) = 1

        [Title(RangeSlider ShowChild)]
        [RangeSlider(_SliderMin2, _SliderMax2, flase)]_RangeSlider2("RangeSlider2ShowChild", Range(1, 10)) = 0
        _SliderMin2("SliderMin", float) = 0
        _SliderMax2("SliderMax", float) = 1
        [Ramp]_RampTex ("RampTex", 2D) = "white" { }
        [Ramp]_RampTex3 ("RampTex3", 2D) = "white" { }
	}
	
	HLSLINCLUDE
	
	
	
	ENDHLSL
	
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
            #pragma shader_feature_local _FOLDOUT_KEY1
            #pragma shader_feature_local _ _KEY1 _KEY2 _KEY3
 
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv     : TEXCOORD0;
                float3 normal : NORMAL;
			};

			struct v2f
			{

				float4 vertex   : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv       : TEXCOORD1;

			};
			float4 _Color;
            sampler2D _RampTex;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
                o.normalWS = normalize(UnityObjectToWorldNormal(v.normal));
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// sample the texture
                
				fixed4 col = _Color;


                #if _FOLDOUT_KEY1
                col = 0.5;
                #endif

                #if _KEY1
                col = 0.1;
                #elif _KEY2
                col = 0.2;
                #elif _KEY3
                col = 0.3;
                #endif

                i.normalWS = normalize(i.normalWS);
                float NdotL = dot(i.normalWS, _WorldSpaceLightPos0);
                NdotL = NdotL * 0.5 + 0.5;
    
                half4 ramp = tex2D(_RampTex, float2(NdotL, 0.4));
                half3 rampCol = lerp(0.0, ramp.rgb, ramp.a);
				return fixed4(col.rgb * rampCol, 1);
			}
			ENDCG
		}
	}
	CustomEditor "UnityEditor.DanbaidongGUI.DanbaidongGUI"
}
