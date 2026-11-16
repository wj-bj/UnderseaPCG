// GPU-instanced fish for FishSchoolSystem. One mesh serves every species: each species
// reshapes the body, recolours it and adds stripes, and the tail beat follows the
// swimming speed computed by the simulation.
Shader "UnderseaV2/Fish"
{
    Properties
    {
        _MainTex ("Detail Texture (brightness only)", 2D) = "white" {}
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.6
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half _DetailStrength;
            half _AmbientBoost;
        CBUFFER_END

        // Per-draw values (MaterialPropertyBlock): where the mesh sits in its own space.
        float3 _MeshCenter;
        float3 _MeshHalfSize;

        struct FishData
        {
            float3 position;
            float speed;
            float3 direction;
            float phase;
            uint species;
            float size;
            float2 random;
        };

        struct FishLook
        {
            float3 bodyScale;       // thickness, height, length multipliers
            float tailAmplitude;
            float3 backColor;
            float stripeCount;
            float3 bellyColor;
            float stripeStrength;
            float3 stripeColor;
            float shine;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<FishData> _Fish;
            StructuredBuffer<FishLook> _Looks;
        #endif

        static float3 _FPos = float3(0, 0, 0);
        static float3 _FDir = float3(0, 0, 1);
        static float _FPhase = 0;
        static float _FSize = 1;
        static float2 _FRandom = float2(0.5, 0.5);
        static float3 _LBodyScale = float3(1, 1, 1);
        static float _LTail = 0.1;
        static float3 _LBack = float3(0.4, 0.5, 0.6);
        static float3 _LBelly = float3(0.9, 0.9, 0.9);
        static float3 _LStripe = float3(0, 0, 0);
        static float _LStripeCount = 0;
        static float _LStripeStrength = 0;
        static float _LShine = 0.3;

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                FishData f = _Fish[unity_InstanceID];
                FishLook l = _Looks[f.species];
                _FPos = f.position;
                _FDir = f.direction;
                _FPhase = f.phase;
                _FSize = f.size;
                _FRandom = f.random;
                _LBodyScale = l.bodyScale;
                _LTail = l.tailAmplitude;
                _LBack = l.backColor;
                _LBelly = l.bellyColor;
                _LStripe = l.stripeColor;
                _LStripeCount = l.stripeCount;
                _LStripeStrength = l.stripeStrength;
                _LShine = l.shine;
            #endif
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // body.x: -1 tail .. +1 head, body.y: -1 belly .. +1 back
        void FishTransform(float3 positionOS, float3 normalOS, out float3 positionWS, out float3 normalWS, out float2 body)
        {
            float3 local = positionOS - _MeshCenter;
            body = float2(local.z / max(_MeshHalfSize.z, 1e-4), local.y / max(_MeshHalfSize.y, 1e-4));

            local *= _LBodyScale * _FSize;
            float3 n = normalize(normalOS / _LBodyScale);

            // Swimming: a wave travelling from head to tail, growing toward the tail.
            float lengthWS = _MeshHalfSize.z * 2.0 * _LBodyScale.z * _FSize;
            float tail = saturate(0.35 - body.x * 0.65);
            float wave = sin(_FPhase - body.x * 2.4);
            float sway = wave * tail * tail * _LTail * lengthWS;
            // The head counter-swings slightly, as real fish do.
            sway += sin(_FPhase + 1.6) * 0.03 * lengthWS * saturate(body.x);
            local.x += sway;

            float3 z = normalize(_FDir);
            float3 x = normalize(cross(float3(0, 1, 0), z));
            float3 y = cross(z, x);
            positionWS = _FPos + x * local.x + y * local.y + z * local.z;
            normalWS = normalize(x * n.x + y * n.y + z * n.z);
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 body : TEXCOORD3;
                float3 backColor : TEXCOORD4;
                float3 bellyColor : TEXCOORD5;
                float4 stripe : TEXCOORD6;     // rgb colour, a strength
                float2 stripeShine : TEXCOORD7; // x stripe count, y shine
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                FishTransform(v.positionOS.xyz, v.normalOS, o.positionWS, o.normalWS, o.body);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                // Small per-fish brightness difference so a school is not one flat colour.
                float variation = 0.85 + 0.3 * _FRandom.y;
                o.backColor = _LBack * variation;
                o.bellyColor = _LBelly * variation;
                o.stripe = float4(_LStripe, _LStripeStrength);
                o.stripeShine = float2(_LStripeCount, _LShine);
                return o;
            }

            half4 frag (Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                n = isFrontFace ? n : -n;

                // Dark back, pale belly (countershading), with the texture adding scale detail.
                float backAmount = smoothstep(-0.65, 0.3, i.body.y);
                float3 albedo = lerp(i.bellyColor, i.backColor, backAmount);
                float detail = dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb, float3(0.3, 0.59, 0.11));
                albedo *= lerp(1.0, 0.55 + detail * 0.9, _DetailStrength);

                // Vertical bars along the body.
                if (i.stripeShine.x > 0.5)
                {
                    float bars = sin(i.body.x * i.stripeShine.x * 3.14159);
                    float mask = smoothstep(0.25, 0.6, bars) * saturate(1.0 - abs(i.body.x) * 0.2);
                    albedo = lerp(albedo, i.stripe.rgb, mask * i.stripe.a);
                }

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndl = dot(n, sun.direction);
                float3 lighting = sun.color * sun.shadowAttenuation * saturate(ndl * 0.6 + 0.4);
                lighting += SampleSH(n) * _AmbientBoost;

                // Silvery flash when the flank catches the sun.
                float3 h = normalize(sun.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), 24.0) * i.stripeShine.y * sun.shadowAttenuation;

                return half4(albedo * lighting + sun.color * spec, 1);
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

            float4 vertShadow (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS, normalWS;
                float2 body;
                FishTransform(v.positionOS.xyz, v.normalOS, positionWS, normalWS, body);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 fragDepth () : SV_Target { return 0; }
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

            float4 vertDepth (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS, normalWS;
                float2 body;
                FishTransform(v.positionOS.xyz, v.normalOS, positionWS, normalWS, body);
                return TransformWorldToHClip(positionWS);
            }

            half4 fragDepth () : SV_Target { return 0; }
            ENDHLSL
        }

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

            VaryingsDN vertDN (Attributes v)
            {
                VaryingsDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS;
                float2 body;
                FishTransform(v.positionOS.xyz, v.normalOS, positionWS, o.normalWS, body);
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 fragDN (VaryingsDN i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                return half4(isFrontFace ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
