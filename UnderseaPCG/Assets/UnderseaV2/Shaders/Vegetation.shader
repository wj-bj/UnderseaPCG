// GPU-instanced underwater plants and corals (drawn with DrawMeshInstancedIndirect).
// Instance transforms come from the buffer filled by VegetationScatter.compute.
Shader "UnderseaV2/Vegetation"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _TipColor ("Tip Colour (multiplied towards the top)", Color) = (1, 1, 1, 1)
        _ColorVariation ("Per-plant Colour Variation", Range(0, 1)) = 0.2
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Sway)]
        _SwayAmplitude ("Sway Amplitude (m at the tip)", Float) = 0.3
        _SwayFrequency ("Sway Frequency", Float) = 1.0
        _SwayWaves ("Waves Along The Stem", Float) = 1.5
        _SwayStiffness ("Stiffness (higher = only the tip moves)", Range(0.5, 4)) = 1.6

        [Header(Lighting)]
        _Translucency ("Translucency", Range(0, 1)) = 0.4
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        #pragma shader_feature_local _ALPHATEST_ON

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _TipColor;
            half _ColorVariation;
            half _Cutoff;
            float _SwayAmplitude;
            float _SwayFrequency;
            float _SwayWaves;
            float _SwayStiffness;
            half _Translucency;
            half _AmbientBoost;
        CBUFFER_END

        // Per-draw values set through a MaterialPropertyBlock.
        float4x4 _MeshMatrix;   // moves the source mesh so its base sits at the origin
        float _MeshHeight;      // height of the mesh after _MeshMatrix, for the sway falloff

        struct PlantInstance
        {
            float3 position;
            float scale;
            float3 up;
            float rotation;
            float4 random;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<PlantInstance> _Instances;
        #endif

        static float3 _InstPos = float3(0, 0, 0);
        static float _InstScale = 1;
        static float3x3 _InstRot = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
        static float4 _InstRandom = float4(0.5, 0.5, 0.5, 0.5);

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                PlantInstance inst = _Instances[unity_InstanceID];
                _InstPos = inst.position;
                _InstScale = inst.scale;
                _InstRandom = inst.random;

                // Basis with Y along the growth direction, spun by the instance rotation.
                float3 up = inst.up;
                float3 refAxis = abs(up.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 side = normalize(cross(refAxis, up));
                float3 fwd = cross(up, side);
                float s, c;
                sincos(inst.rotation, s, c);
                float3 x = side * c + fwd * s;
                float3 z = fwd * c - side * s;
                _InstRot = float3x3(x.x, up.x, z.x,
                                    x.y, up.y, z.y,
                                    x.z, up.z, z.z);
            #endif
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // Places a vertex in the world and bends it with the current.
        void PlantTransform(float3 positionOS, float3 normalOS, out float3 positionWS, out float3 normalWS, out float height01)
        {
            float3 local = mul(_MeshMatrix, float4(positionOS, 1)).xyz;
            float3 localN = normalize(mul((float3x3)_MeshMatrix, normalOS));
            height01 = saturate(local.y / max(_MeshHeight, 1e-3));

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                positionWS = _InstPos + mul(_InstRot, local * _InstScale);
                normalWS = normalize(mul(_InstRot, localN));
            #else
                positionWS = TransformObjectToWorld(local);
                normalWS = TransformObjectToWorldNormal(localN);
            #endif

            // Sway: a travelling wave up the stem, out of phase between neighbours,
            // plus a slower drift so whole patches lean together.
            float bend = pow(height01, _SwayStiffness);
            float phase = _InstRandom.x * 6.2831853 + dot(_InstPos.xz, float2(0.35, 0.27));
            float t = _Time.y * _SwayFrequency;
            float wave = sin(t + phase - height01 * _SwayWaves * 3.14159);
            float wave2 = sin(t * 0.63 + phase * 1.7 - height01 * _SwayWaves * 2.1 + 1.3);
            float drift = sin(_Time.y * 0.21 + dot(_InstPos.xz, float2(0.05, 0.04)));
            float amplitude = _SwayAmplitude * _InstScale * bend;
            float2 dir = normalize(float2(0.8, 0.6));
            float2 offset = dir * (wave * 0.7 + drift * 0.6) + float2(-dir.y, dir.x) * wave2 * 0.45;
            positionWS.xz += offset * amplitude;
            positionWS.y -= dot(offset, offset) * amplitude * 0.25;
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
                float3 tint : TEXCOORD3;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float height01;
                PlantTransform(v.positionOS.xyz, v.normalOS, o.positionWS, o.normalWS, height01);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);

                float3 tint = lerp(_BaseColor.rgb, _BaseColor.rgb * _TipColor.rgb, height01);
                float3 variation = lerp(float3(0.75, 0.9, 0.7), float3(1.15, 1.05, 1.0), _InstRandom.y);
                o.tint = tint * lerp(1.0, variation, _ColorVariation);
                return o;
            }

            half4 frag (Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                #ifdef _ALPHATEST_ON
                    clip(tex.a - _Cutoff);
                #endif

                float3 n = normalize(i.normalWS);
                n = isFrontFace ? n : -n;
                half3 albedo = tex.rgb * i.tint;

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = dot(n, sun.direction);
                float direct = saturate(ndl * 0.6 + 0.4);
                // Thin leaves let some light through from behind.
                float through = saturate(-ndl) * _Translucency;
                half3 lighting = sun.color * sun.shadowAttenuation * (direct + through);
                lighting += SampleSH(n) * _AmbientBoost;

                return half4(albedo * lighting, 1);
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
            #pragma fragment fragClip
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct VaryingsClip
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            VaryingsClip vertShadow (Attributes v)
            {
                VaryingsClip o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS, normalWS;
                float height01;
                PlantTransform(v.positionOS.xyz, v.normalOS, positionWS, normalWS, height01);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragClip (VaryingsClip i) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                return 0;
            }
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
            #pragma fragment fragClip
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup

            struct VaryingsClip
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            VaryingsClip vertDepth (Attributes v)
            {
                VaryingsClip o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS, normalWS;
                float height01;
                PlantTransform(v.positionOS.xyz, v.normalOS, positionWS, normalWS, height01);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragClip (VaryingsClip i) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                return 0;
            }
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
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            VaryingsDN vertDN (Attributes v)
            {
                VaryingsDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS;
                float height01;
                PlantTransform(v.positionOS.xyz, v.normalOS, positionWS, o.normalWS, height01);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragDN (VaryingsDN i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                float3 n = normalize(i.normalWS);
                return half4(isFrontFace ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
