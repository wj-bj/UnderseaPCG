Shader "Custom/WaterSurface"
{
	Properties{
	_Color("Color",Color) = (1,1,1,1)
	_TimeScale("TimeScale",range(0,10)) =1
	_DepthFactor("DepthFactor",range(0,1)) =0.5
	_WaveStrength("WaveStrength",range(0,10)) =1
	_Distortion("_Distortion",float) = 1.0
	_NoiseTex("noiseSample",2D) = "white"{}
	_RampTex("RampColor",2D) = "white"{}
	_WaveTex("_WaveTex",2D) = "white"{}
	_NormalTex("_NormalTex",2D)="white"{}
	_BumpFactor("_BumpFactor",range(0,1)) =1.0
	_FresnelFactor("_FresnelFactor",range(0,10)) = 4.0
	_FresnelDistanceFactor("_FresnelDistanceFactor",range(10,100)) = 30
	_SwormFlowFactor("_SwormFlowFactor",range(0,100)) = 4.0
	_CubeMap("_CubeMap",Cube) = "_Skybox"{}
	}
		SubShader
	{
		Tags{"RenderType" = "Opaque" "Queue"="Geometry"}
	GrabPass{"_GrabPass"}
	Pass
	{
		Cull Off
		//Blend One Zero
	CGPROGRAM
	// required to use ComputeScreenPos()
	#include "UnityCG.cginc"
	

	#pragma vertex vert
	#pragma fragment frag

	 sampler2D _CameraDepthTexture;
	fixed4 _Color;
	float _DepthFactor;
	float _WaveStrength;
	float _TimeScale;
	fixed _Distortion;
	sampler2D _NoiseTex;
	sampler2D _RampTex;
	sampler2D _WaveTex;
	sampler2D _GrabPass;
	sampler2D _NormalTex;
	samplerCUBE _CubeMap;
	float4 _NoiseTex_ST;
	float4 _WaveTex_ST;
	float _BumpFactor;
	float _FresnelFactor;
	float _FresnelDistanceFactor;
	float _SwormFlowFactor;
	float4 _NormalTex_ST;
	float4 _GrabPass_TexelSize;

	struct vertexInput
	 {
	   float4 vertex : POSITION;
	   float4 texcoord:TEXCOORD;
	   float3 normal:NORMAL;
	   float4 tangent:TANGENT;
	 };

	struct vertexOutput
	 {
	   float4 pos : SV_POSITION;
	   float4 grabScreenPos:texcoord2;
	   float4 UV:TEXCOORD3;
	   float4 originalPos:TEXCOORD4;
	   float4 T2W0:TEXCOORD5;
	   float4 T2W1:TEXCOORD6;
	   float4 T2W2 : TEXCOORD7;
	 };

	vertexOutput vert(vertexInput input)
	  {
		vertexOutput output;
		output.originalPos= UnityObjectToClipPos(input.vertex);
		output.UV.xy = TRANSFORM_TEX(input.texcoord.xy, _NoiseTex);
		output.UV.zw = TRANSFORM_TEX(input.texcoord.xy, _NormalTex);
		//Animating
		//tex2D(_NoiseTex,input.texcoord)is not available in vertex shader 
		//for there are no UV derivatives in Vertex Shader
		float noise =tex2Dlod(_NoiseTex, float4(output.UV.xy, 0, 0));
		input.vertex.y += cos(_Time.x * noise)*0.2*noise*_WaveStrength;

		// convert obj-space position to camera clip space
		output.pos = UnityObjectToClipPos(input.vertex);

		// compute depth (screenPos is a float4)
		output.grabScreenPos = ComputeGrabScreenPos(output.originalPos);
		//compute tangent2world matrix,needs world space tangent coordinates
		fixed3 worldPos = mul(unity_ObjectToWorld, input.vertex);
		fixed3 worldNormal = UnityObjectToWorldNormal(input.normal);
		fixed3 worldTangent = UnityObjectToWorldDir(input.tangent.xyz);
		fixed3 worldBitangent = cross(worldNormal, worldTangent)*input.tangent.w;

		output.T2W0 = float4(worldTangent.x, worldBitangent.x, worldNormal.x, worldPos.x);
		output.T2W1 = float4(worldTangent.y, worldBitangent.y, worldNormal.y, worldPos.y);
		output.T2W2 = float4(worldTangent.z, worldBitangent.z, worldNormal.z, worldPos.z);


		return output;
	  }
	  float4 frag(vertexOutput input) : COLOR
	  {

		  //完成折射形变部分
			  //获取对GrabPass的采样坐标，坐标包含形变,形变从噪声获取
			  float noise = tex2D(_NoiseTex, input.UV.xy + _TimeScale*float2(_Time.x,_Time.x));
			  float2 bump = float2(noise * 2 - 1,noise * 2 - 1)*_GrabPass_TexelSize;
			  bump *= _Distortion;
			  input.grabScreenPos.xy += bump;
			  input.grabScreenPos.xy /= input.grabScreenPos.w;//手动进行齐次除法，当然也可以使用宏
			//对grabpass采样
			  float4 ditortionColor = tex2D(_GrabPass, input.grabScreenPos.xy);
			
		//完成物体交互部分
		  // sample camera depth texture
			float depthSample = tex2D(_CameraDepthTexture, input.grabScreenPos.xy);//采样得到非线性深度
	  	depthSample =0;
			float depth = LinearEyeDepth(depthSample);//线性深度
	  		
			float foamLine = depth- input.grabScreenPos.w;//遮罩计算
	  	
			foamLine=(1-smoothstep(0,2,foamLine));//反转一下好用一点
			float4 foamRamp = float4(tex2D(_RampTex, smoothstep(0, 1+ _DepthFactor, float2(1 - foamLine, 0.5))).rgb, 1);//对材质进行采样
			float4 foamcolor = _Color*foamRamp;//输出颜色
			
			//水波
			float4 wave = tex2D(_WaveTex,TRANSFORM_TEX(float2(foamLine,input.grabScreenPos.x),_WaveTex))*foamLine;

		//完成水面法线与反射部分
			//首先获取法线纹理中的数据，并且解包。然后通过法线方向来求反射方向。
			//法线数据获取
			float2 uvForRefl = input.UV.zw + noise +(1+ noise * _SwormFlowFactor/100)*_Time.x;
			float4 packednormal =tex2D(_NormalTex, uvForRefl);
			float3 tangentNormal =UnpackNormal(packednormal);
			tangentNormal.xy *= _BumpFactor;
			tangentNormal.z = sqrt(1 - dot(tangentNormal.xy, tangentNormal.xy));
			
			//法线变换到世界坐标
			float3 worldNormal = mul(float3x3(input.T2W0.xyz, input.T2W1.xyz, input.T2W2.xyz), tangentNormal);
			
			//利用法线坐标求出反射角对cubemap采样，这里需要一个cubemap，麻烦出去Unity那边做一个哦
			float3 worldPos = float3(input.T2W0.w, input.T2W1.w, input.T2W2.w);
			float3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
			float3 worldReflDir = normalize(reflect(-worldViewDir, worldNormal));
			float4 reflColor = texCUBE(_CubeMap, worldReflDir);


			float fresnel = pow(1 - max(0, abs(dot(worldViewDir, worldNormal))), _FresnelFactor)*smoothstep(0, _FresnelDistanceFactor, depth);

			float4 color = foamcolor * foamcolor.a + (1 - foamcolor.a)*ditortionColor;
			color = color * (1 - fresnel) + fresnel * reflColor+wave.rrrr*0.5;
		  return float4(color);
		}

		ENDCG
	  } 
		
	       Pass
        {
            Tags{ "LightMode" = "ShadowCaster" }
        	Cull Off
            CGPROGRAM
            #pragma vertex VSMain
            #pragma fragment PSMain
            #include "UnityCG.cginc"
 
            float4 VSMain (float4 vertex:POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }
 
            float4 PSMain (float4 vertex:SV_POSITION) : SV_TARGET
            {
                return 0;
            }
         
            ENDCG
        }
	}
}
