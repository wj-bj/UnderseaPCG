// URP port of the original Built-in water surface: two scrolling wave normals,
// cubemap reflection/refraction blended by fresnel, plus a light glint.
Shader "Custome/water2"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_RefractionTex ("Refraction Texture", 2D) = "white" {}
		_Color ("Main Color", Color) = (0,0.15,0.115,1)
		_WaveMap("Wave map", 2D) = "bump"{}
		_CubeMap ("Environment Cubemap", Cube) = "_Skybox"{}
		_WaveXSpeed("Wave Horizointal Speed", Range(-1,1)) = 0.01
		_WaveYSpeed("Wave Vertical Speed", Range(-1,1)) = 0.01
		_Distortion ("Distortion", Range(0,100)) = 10
		_FresnelPower ("FresnelPower", Range(0,30)) = 1
		_RefractionStrength ("RefractionStrength", Range(0,2)) = 1
		_SpecRotate ("SpecRotate", Range(0,360)) = 0
	}
	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode"="UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
			TEXTURE2D(_WaveMap);  SAMPLER(sampler_WaveMap);
			TEXTURECUBE(_CubeMap); SAMPLER(sampler_CubeMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float4 _RefractionTex_ST;
				float4 _WaveMap_ST;
				half4 _Color;
				float _WaveXSpeed;
				float _WaveYSpeed;
				float _Distortion;
				float _FresnelPower;
				float _RefractionStrength;
				float _SpecRotate;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				float3 normalOS : NORMAL;
				float4 tangentOS : TANGENT;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				float3 normalWS : TEXCOORD2;
				float3 tangentWS : TEXCOORD3;
				float3 bitangentWS : TEXCOORD4;
				half fogFactor : TEXCOORD5;
			};

			Varyings vert (Attributes v)
			{
				Varyings o;
				VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
				VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
				o.positionCS = p.positionCS;
				o.positionWS = p.positionWS;
				o.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
				o.uv.zw = TRANSFORM_TEX(v.uv, _WaveMap);
				o.normalWS = n.normalWS;
				o.tangentWS = n.tangentWS;
				o.bitangentWS = n.bitangentWS;
				o.fogFactor = ComputeFogFactor(p.positionCS.z);
				return o;
			}

			half4 frag (Varyings i) : SV_Target
			{
				Light mainLight = GetMainLight();
				float3 lightDir = mainLight.direction;
				float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
				float2 speed = _Time.x * float2(_WaveXSpeed, _WaveYSpeed);

				// Two wave layers scrolling in opposite directions
				half3 bump1 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, i.uv.zw + speed));
				half3 bump2 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, i.uv.zw - speed));
				half3 bumpTS = normalize(half3(bump1.xy + bump2.xy, bump1.z * bump2.z));

				float3 bump = normalize(bumpTS.x * i.tangentWS + bumpTS.y * i.bitangentWS + bumpTS.z * i.normalWS);

				float3 refractDir = normalize(refract(-viewDir, bump, 1 / 1.33));
				half3 refrCol = SAMPLE_TEXTURECUBE(_CubeMap, sampler_CubeMap, refractDir).rgb * _Color.rgb;

				float angle = radians(_SpecRotate);
				float3 nLightDir = float3(lightDir.x * cos(angle) - lightDir.z * sin(angle), lightDir.y, lightDir.x * sin(angle) + lightDir.z * cos(angle));
				float m = pow(abs(dot(nLightDir, refractDir)), _FresnelPower * 4);
				refrCol += mainLight.color * m * _RefractionStrength;

				half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy + speed);
				float3 reflDir = reflect(-viewDir, bump);
				half3 reflCol = SAMPLE_TEXTURECUBE(_CubeMap, sampler_CubeMap, reflDir).rgb * texColor.rgb * _Color.rgb;

				float fresnel = saturate(pow(1 - saturate(dot(viewDir, bump)), _FresnelPower));
				half3 finalColor = reflCol * fresnel + refrCol * (1 - fresnel);
				finalColor = MixFog(finalColor, i.fogFactor);
				return half4(finalColor, 1);
			}
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }
			Cull Off
			ZWrite On
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex vertShadow
			#pragma fragment fragShadow
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			float4 vertShadow (float4 positionOS : POSITION) : SV_POSITION
			{
				return TransformObjectToHClip(positionOS.xyz);
			}

			half4 fragShadow () : SV_Target { return 0; }
			ENDHLSL
		}
	}
}
