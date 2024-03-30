/*
	Author: Alberto Mellado Cruz
	Date: 09/11/2017

	Comments: 
	This is just a test that would depend on the 3D Model used.
	Vertex animations would allow the use of GPU Instancing, 
	enabling the use of a dense amount of animated fish.
  	The code may not be optimized but it was just a test
*/

Shader "Custom/FishAnimation"
{
    Properties
    {
        _MainTex("Albedo (RGB)", 2D) = "white" {}
        _SpeedX("SpeedX", Range(0, 10)) = 1
        _FrequencyX("FrequencyX", Range(0, 10)) = 1
        _AmplitudeX("AmplitudeX", Range(0, 0.2)) = 1
        _SpeedY("SpeedY", Range(0, 10)) = 1
        _FrequencyY("FrequencyY", Range(0, 10)) = 1
        _AmplitudeY("AmplitudeY", Range(0, 0.2)) = 1
        _SpeedZ("SpeedZ", Range(0, 10)) = 1
        _FrequencyZ("FrequencyZ", Range(0, 10)) = 1
        _AmplitudeZ("AmplitudeZ", Range(0, 2)) = 1
        _HeadLimit("HeadLimit", Range(-2, 2)) = 0.05
        _FinPos("FinPos", Range(-1, 1)) = -0.2
        [Header(Caustic)]
        _UVScale("UVScale", Range( 0 , 5)) = 1
		_TimeScale("TimeScale", Range( 0 , 10)) = 0
		_CausticColor("CausticColor", Color) = (0.5471698,0.5471698,0.5471698,0)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }
        Cull off

        Pass
        {

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWorld : TEXCOORD1;
                SHADOW_COORDS(2)
                float3 worldPos : TEXCOORD3;
                uint inst : SV_InstanceID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // X AXIS

            float _SpeedX;
            float _FrequencyX;
            float _AmplitudeX;

            // Y AXIS

            float _SpeedY;
            float _FrequencyY;
            float _AmplitudeY;

            // Z AXIS

            float _SpeedZ;
            float _FrequencyZ;
            float _AmplitudeZ;

            // Head Limit (Head wont shake so much)

            float _HeadLimit;


            float3 _BoidPosition;
            float _FinOffset;
            float _FinPos;
            float4x4 _Matrix;

            float _UVScale;
		    float _TimeScale;
		    float4 _CausticColor;

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        struct Boid
        {
            float3 position;
            float3 direction;
            float noise_offset;
            float theta;
        };
        StructuredBuffer<Boid> boidsBuffer; 
            #endif

            float4x4 create_matrix(float3 pos, float3 dir, float3 up)
            {
                float3 zaxis = normalize(dir);
                float3 xaxis = normalize(cross(up, zaxis));
                float3 yaxis = cross(zaxis, xaxis);
                return float4x4(
                    xaxis.x, yaxis.x, zaxis.x, pos.x,
                    xaxis.y, yaxis.y, zaxis.y, pos.y,
                    xaxis.z, yaxis.z, zaxis.z, pos.z,
                    0, 0, 0, 1
                );
            }

            float3 CalculateCaustic( float2 uv, float UVScale, float Time )
		    {
				#define F length(.5-frac(k.xyw =mul(float3x3(-2,-1,2, 3,-2,1, 1,2,2),k.xyw)*
				           float4 k = float4(1,0,0,0);
				           k.w = Time;
				           float2 p = (uv)*UVScale*float2(900,800);
				           k.xy = p*(sin(0.3))/2e2;
				           k = pow(min(min(F .5)),F .4))),F .3))), 6)*20+float4(0,0,0,1);
				return k.xyz;
		    }

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                _FinOffset = sin(boidsBuffer[unity_InstanceID].theta);
                _Matrix = create_matrix(boidsBuffer[unity_InstanceID].position, boidsBuffer[unity_InstanceID].direction, float3(0.0, 1.0, 0.0));
                #endif
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                


                //Z AXIS
                v.vertex.z += sin((v.vertex.z + _Time.y * _SpeedX) * _FrequencyX) * _AmplitudeX;

                //Y AXIS
                v.vertex.y += sin((v.vertex.z + _Time.y * _SpeedY) * _FrequencyY) * _AmplitudeY;

                //X AXIS
                if (v.vertex.z > _HeadLimit)
                {
                    v.vertex.x += sin((0.05 + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * _HeadLimit;
                }
                else
                {
                    v.vertex.x += sin((v.vertex.z + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * v.vertex.z;
                }

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
             if (v.vertex.z<_FinPos){
                    v.vertex.x += (sin(abs(v.vertex.z+0.2)*5*UNITY_HALF_PI + 3*UNITY_HALF_PI) + 1) * 0.3 * _FinOffset;
             }
            v.vertex = mul(_Matrix, v.vertex);
                #endif
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                v.normal=mul(_Matrix, v.normal);
                
                o.normalWorld = UnityObjectToWorldNormal(normalize(v.normal));
                TRANSFER_SHADOW(o);
                o.worldPos = mul(unity_ObjectToWorld,v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
			#ifdef USING_DIRECTIONAL_LIGHT
				float3 lightDir = normalize(_WorldSpaceLightPos0.xyz); //_WorldSpaceLightPos0;//normalize(_LitDir);//
			#else
				float3 lightDir = _WorldSpaceLightPos0; //- i.vertex;
			#endif
                fixed4 col = tex2D(_MainTex, i.uv);
                float atten = SHADOW_ATTENUATION(i);
               // atten = atten>0.5?1:0.5;
                float3 normalWorld = normalize(i.normalWorld);
                float3 light = _LightColor0*(saturate(dot(lightDir,normalWorld))*0.4+0.6);
                col.xyz = col.xyz*saturate((atten+0.2))*light;

                float mask = smoothstep(-0.5,1,normalWorld.y);
                
                float3 caustic = _CausticColor*CalculateCaustic(i.worldPos.xz,_UVScale,_Time.x*_TimeScale)*mask;
                col.xyz +=caustic;
                
              
                return col;
            }
            ENDCG

        }
        
           Pass
        {
            Tags{ "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex VSMain
            #pragma fragment PSMain
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                //		uint id : SV_VertexID;
                uint inst : SV_InstanceID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // X AXIS

            float _SpeedX;
            float _FrequencyX;
            float _AmplitudeX;

            // Y AXIS

            float _SpeedY;
            float _FrequencyY;
            float _AmplitudeY;

            // Z AXIS

            float _SpeedZ;
            float _FrequencyZ;
            float _AmplitudeZ;

            // Head Limit (Head wont shake so much)

            float _HeadLimit;


            float3 _BoidPosition;
            float _FinOffset;
            float _FinPos;
            float4x4 _Matrix;

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        struct Boid
        {
            float3 position;
            float3 direction;
            float noise_offset;
            float theta;
        };
        StructuredBuffer<Boid> boidsBuffer; 
            #endif
            float4x4 create_matrix(float3 pos, float3 dir, float3 up)
            {
                float3 zaxis = normalize(dir);
                float3 xaxis = normalize(cross(up, zaxis));
                float3 yaxis = cross(zaxis, xaxis);
                return float4x4(
                    xaxis.x, yaxis.x, zaxis.x, pos.x,
                    xaxis.y, yaxis.y, zaxis.y, pos.y,
                    xaxis.z, yaxis.z, zaxis.z, pos.z,
                    0, 0, 0, 1
                );
            }

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                _FinOffset = sin(boidsBuffer[unity_InstanceID].theta);
                _Matrix = create_matrix(boidsBuffer[unity_InstanceID].position, boidsBuffer[unity_InstanceID].direction, float3(0.0, 1.0, 0.0));
                #endif
            }

            v2f VSMain(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);


                //Z AXIS
                v.vertex.z += sin((v.vertex.z + _Time.y * _SpeedX) * _FrequencyX) * _AmplitudeX;

                //Y AXIS
                v.vertex.y += sin((v.vertex.z + _Time.y * _SpeedY) * _FrequencyY) * _AmplitudeY;

                //X AXIS
                if (v.vertex.z > _HeadLimit)
                {
                    v.vertex.x += sin((0.05 + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * _HeadLimit;
                }
                else
                {
                    v.vertex.x += sin((v.vertex.z + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * v.vertex.z;
                }

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
             if (v.vertex.z<_FinPos){
                    v.vertex.x += (sin(abs(v.vertex.z+0.2)*5*UNITY_HALF_PI + 3*UNITY_HALF_PI) + 1) * 0.3 * _FinOffset;
             }
            v.vertex = mul(_Matrix, v.vertex);
                #endif
                o.pos = UnityObjectToClipPos(v.vertex);

                return o;
            }

            float4 PSMain(float4 vertex:SV_POSITION) : SV_TARGET
            {
                return 0;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}