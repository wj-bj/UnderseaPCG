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
		Tags { "Queue"="Transparent"  "RenderType"="Opaque"}
		//GrabPass { "_RefractionTex" }
		  //通过GrabPass定义了一个抓取屏幕图像的pass，这个pass 定义了一个字符串
		  //字符串内部的名称决定了抓取到的屏幕图像存储于哪个纹理之中-- _RefractionTex
		  //实际上可以不声明字符串，声明可以获取更高的性能

		Pass
		{
			Tags{"LightMode"="ForwardBase"}
			//Cull Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"
			#include "Lighting.cginc"

			#pragma multi_compile_fwdbase

			struct a2v
			{
				float4 vertex : POSITION;
				float4 uv : TEXCOORD0;
				float3 normal :NORMAL;
				float4 tangent : TANGENT;
			};

			struct v2f
			{
				float4 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
				float4 scrPos :TEXCOORD1  ;
				float4 TtoW0: TEXCOORD2 ;
				float4 TtoW1: TEXCOORD3 ;
				float4 TtoW2: TEXCOORD4 ; 
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			sampler2D _WaveMap;
			float4 _WaveMap_ST;
			samplerCUBE _CubeMap;
			fixed _WaveYSpeed;
			fixed _WaveXSpeed;
			float _Distortion;
			sampler2D _RefractionTex;
			float4 _RefractionTex_TexelSize;
			float _FresnelPower;
			float _RefractionStrength;
			float _SpecRotate;
			
			v2f vert (a2v v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.scrPos = ComputeGrabScreenPos(o.pos);
				//通过此函数来得到对应的被抓取的屏幕图像的坐标

				o.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
				o.uv.zw = TRANSFORM_TEX(v.uv, _WaveMap);

				float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);
				fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
				fixed3 worldBinormal = cross(worldNormal,worldTangent) * v.tangent.w;

				o.TtoW0= float4(worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x);
				o.TtoW1= float4(worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y);
				o.TtoW2= float4(worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z);

				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				#ifdef USING_DIRECTIONAL_LIGHT
					float3 lightDir = normalize(_WorldSpaceLightPos0.xyz); //_WorldSpaceLightPos0;//normalize(_LitDir);//
				#else
					float3 lightDir = _WorldSpaceLightPos0; //- i.vertex;
				#endif
				float3 worldPos =float3 (i.TtoW0.w, i.TtoW1.w, i.TtoW2.w);
				fixed3 viewDir =  normalize(UnityWorldSpaceViewDir(worldPos));
				float2 speed = _Time.x * float2(_WaveXSpeed,_WaveYSpeed);
				  //计算法线纹理的当前偏移量

				fixed3 bump1 = UnpackNormal(tex2D(_WaveMap, i.uv.zw +speed)).rgb;
				fixed3 bump2 = UnpackNormal(tex2D(_WaveMap, i.uv.zw -speed)).rgb;
				
				//fixed3 bump = normalize(bump1 + bump2);
				fixed3 bump = BlendNormals(bump1,bump2);
				
				   // 通过偏移量对法线纹理进行采样，两次采样是为了模拟两层交叉的水面波动效果
				   // 对两次结果相加并归一化得到切线空间下的法线方向
				float2 offset = bump.xy *_Distortion * _RefractionTex_TexelSize.xy;
				   //对屏幕图像进行偏移
				i.scrPos.xy = offset * i.scrPos.z + i.scrPos.xy;

				fixed3 refrCol = tex2D(_RefractionTex, i.scrPos.xy/i.scrPos.w).rgb;
				
				  //简言之ComputeGrabScreenPos得到的屏幕空间坐标并不是真正的空间坐标
				  //它是包含了插值之后的结果，必须除以w分量，还原出真正的视口坐标
				  //原理是通过透视除法，得出真正的屏幕坐标
				  //通过这样来得到反射的颜色也就是空间中所有已经渲染的不透明物体，都已经在一张贴图中
				bump = normalize (half3(dot(i.TtoW0.xyz,  bump),   dot(i.TtoW1.xyz,bump),  dot(i.TtoW2.xyz, bump)));
				//bump = -fixed3(0,1,0);
				half3 refractDir = normalize(refract(-viewDir,bump, 1/1.33));
				 refrCol  = texCUBE(_CubeMap, refractDir).rgb * _Color.rgb;
				half angle = radians(_SpecRotate);
				half3 nLightDir = half3(lightDir.x*cos(angle)-lightDir.z*sin(angle),lightDir.y, lightDir.x*sin(angle)+lightDir.z*cos(angle));
				half m = pow(abs(dot(nLightDir,refractDir)),_FresnelPower*4);
				refrCol += _LightColor0*m*_RefractionStrength;

				fixed4 texColor = tex2D(_MainTex,i.uv.xy + speed);
			
				fixed3 reflDir = reflect(-viewDir,bump);

				fixed3 reflCol  = texCUBE(_CubeMap, reflDir).rgb * texColor.rgb * _Color.rgb;
				
				fixed fresnel  = pow (1- saturate(dot(viewDir,bump)) ,_FresnelPower);
				fresnel = saturate(fresnel);
				 //计算菲涅尔系数
				
				fixed3 finalcolor = reflCol * fresnel + refrCol*(1-fresnel);
				
				return fixed4(finalcolor,1);

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
	//FallBack Off
}
