/*
	Original author: Alberto Mellado Cruz (09/11/2017)
	Vertex-animated fish drawn with GPU instancing (DrawMeshInstancedIndirect).
	Ported from the Built-in pipeline to URP.
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
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #define FISH_HALF_PI 1.57079632679

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _SpeedX, _FrequencyX, _AmplitudeX;
            float _SpeedY, _FrequencyY, _AmplitudeY;
            float _SpeedZ, _FrequencyZ, _AmplitudeZ;
            float _HeadLimit;
            float _FinPos;
            float _UVScale;
            float _TimeScale;
            float4 _CausticColor;
        CBUFFER_END

        // Per-instance values filled in by setup()
        static float4x4 _FishMatrix = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
        static float _FinOffset = 0;

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
                Boid b = boidsBuffer[unity_InstanceID];
                _FinOffset = sin(b.theta);
                _FishMatrix = create_matrix(b.position, b.direction, float3(0.0, 1.0, 0.0));
            #endif
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // Swim animation in object space, then placement in world space.
        void FishTransform(inout float3 pos, inout float3 normal)
        {
            pos.z += sin((pos.z + _Time.y * _SpeedX) * _FrequencyX) * _AmplitudeX;
            pos.y += sin((pos.z + _Time.y * _SpeedY) * _FrequencyY) * _AmplitudeY;
            if (pos.z > _HeadLimit)
                pos.x += sin((0.05 + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * _HeadLimit;
            else
                pos.x += sin((pos.z + _Time.y * _SpeedZ) * _FrequencyZ) * _AmplitudeZ * pos.z;

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                if (pos.z < _FinPos)
                    pos.x += (sin(abs(pos.z + 0.2) * 5 * FISH_HALF_PI + 3 * FISH_HALF_PI) + 1) * 0.3 * _FinOffset;
                pos = mul(_FishMatrix, float4(pos, 1)).xyz;
                normal = normalize(mul((float3x3)_FishMatrix, normal));
            #else
                pos = TransformObjectToWorld(pos);
                normal = TransformObjectToWorldNormal(normal);
            #endif
        }

        float3 CalculateCaustic(float2 uv, float UVScale, float Time)
        {
            #define F length(.5-frac(k.xyw =mul(float3x3(-2,-1,2, 3,-2,1, 1,2,2),k.xyw)*
            float4 k = float4(1,0,0,0);
            k.w = Time;
            float2 p = (uv)*UVScale*float2(900,800);
            k.xy = p*(sin(0.3))/2e2;
            k = pow(min(min(F .5)),F .4))),F .3))), 6)*20+float4(0,0,0,1);
            #undef F
            return k.xyz;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 pos = v.positionOS.xyz;
                float3 normal = v.normalOS;
                FishTransform(pos, normal);

                o.positionWS = pos;
                o.normalWS = normal;
                o.positionCS = TransformWorldToHClip(pos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 normalWS = normalize(i.normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float atten = mainLight.shadowAttenuation;
                float3 light = mainLight.color * (saturate(dot(mainLight.direction, normalWS)) * 0.4 + 0.6);
                col.rgb = col.rgb * saturate(atten + 0.2) * light;

                float mask = smoothstep(-0.5, 1, normalWS.y);
                float3 caustic = _CausticColor.rgb * CalculateCaustic(i.positionWS.xz, _UVScale, _Time.x * _TimeScale) * mask;
                col.rgb += caustic;

                col.rgb = MixFog(col.rgb, i.fogFactor);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 vertShadow(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz;
                float3 normal = v.normalOS;
                FishTransform(pos, normal);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(pos, normal, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            float4 vertDepth(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz;
                float3 normal = v.normalOS;
                FishTransform(pos, normal);
                return TransformWorldToHClip(pos);
            }

            half4 fragDepth() : SV_Target { return 0; }
            ENDHLSL
        }

        // Needed when a renderer feature asks for the camera normals texture:
        // URP then builds the depth texture from this pass instead of DepthOnly.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            VaryingsDN vertDN(Attributes v)
            {
                VaryingsDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz;
                float3 normal = v.normalOS;
                FishTransform(pos, normal);
                o.positionCS = TransformWorldToHClip(pos);
                o.normalWS = normal;
                return o;
            }

            half4 fragDN(VaryingsDN i) : SV_Target
            {
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
