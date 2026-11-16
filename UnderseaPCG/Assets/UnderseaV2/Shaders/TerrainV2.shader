// Seabed terrain for marching-cubes meshes (no UVs needed).
// Sand on flat ground, rock on slopes, both projected in world space.
Shader "UnderseaV2/Terrain"
{
    Properties
    {
        [Header(Sand)]
        _SandTex ("Sand Albedo", 2D) = "white" {}
        [Normal] _SandNormal ("Sand Normal", 2D) = "bump" {}
        _SandTint ("Sand Tint", Color) = (1, 0.96, 0.86, 1)
        _SandScale ("Sand Tiling (per metre)", Float) = 0.25
        _SandNormalStrength ("Sand Normal Strength", Range(0, 2)) = 0.8

        [Header(Rock)]
        _RockTex ("Rock Albedo", 2D) = "gray" {}
        [Normal] _RockNormal ("Rock Normal", 2D) = "bump" {}
        _RockTint ("Rock Tint", Color) = (0.62, 0.64, 0.66, 1)
        _RockScale ("Rock Tiling (per metre)", Float) = 0.18
        _RockNormalStrength ("Rock Normal Strength", Range(0, 2)) = 1.0

        [Header(Blending)]
        _SlopeStart ("Sand Slope Start (normal.y)", Range(0, 1)) = 0.62
        _SlopeEnd ("Sand Slope End (normal.y)", Range(0, 1)) = 0.82
        _BlendNoise ("Blend Noise Amount", Range(0, 0.5)) = 0.18
        _MacroVariation ("Large-scale Colour Variation", Range(0, 1)) = 0.35

        [Header(Algae)]
        _AlgaeColor ("Algae Colour", Color) = (0.28, 0.42, 0.2, 1)
        _AlgaeAmount ("Algae Amount", Range(0, 1)) = 0.45

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _CreviceDarkening ("Underside Darkening", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SandTex_ST;
            float4 _RockTex_ST;
            half4 _SandTint;
            half4 _RockTint;
            half4 _AlgaeColor;
            float _SandScale;
            float _RockScale;
            half _SandNormalStrength;
            half _RockNormalStrength;
            half _SlopeStart;
            half _SlopeEnd;
            half _BlendNoise;
            half _MacroVariation;
            half _AlgaeAmount;
            half _Smoothness;
            half _CreviceDarkening;
        CBUFFER_END

        TEXTURE2D(_SandTex);    SAMPLER(sampler_SandTex);
        TEXTURE2D(_SandNormal); SAMPLER(sampler_SandNormal);
        TEXTURE2D(_RockTex);    SAMPLER(sampler_RockTex);
        TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        // Smooth 3D value noise in 0..1
        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i);
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        float Fbm(float3 p)
        {
            return ValueNoise(p) * 0.6 + ValueNoise(p * 2.3 + 11.7) * 0.3 + ValueNoise(p * 5.1 + 3.1) * 0.1;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Triplanar normal mapping ("whiteout" blend): each projection's tangent-space
            // normal is re-oriented onto the surface normal, then the three are blended.
            float3 TriplanarNormal(TEXTURE2D_PARAM(tex, smp), float3 p, float3 n, float3 w, float strength)
            {
                float3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.zy), strength);
                float3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.xz), strength);
                float3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, p.xy), strength);
                tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
                return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
            }

            half3 TriplanarColor(TEXTURE2D_PARAM(tex, smp), float3 p, float3 w)
            {
                return SAMPLE_TEXTURE2D(tex, smp, p.zy).rgb * w.x
                     + SAMPLE_TEXTURE2D(tex, smp, p.xz).rgb * w.y
                     + SAMPLE_TEXTURE2D(tex, smp, p.xy).rgb * w.z;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 p = i.positionWS;

                float3 w = pow(abs(n), 4.0);
                w /= (w.x + w.y + w.z);

                float noise = Fbm(p * 0.35);
                float macro = Fbm(p * 0.07 + 5.3);

                // Sand settles on flat ground; noise breaks up the boundary.
                float sandMask = smoothstep(_SlopeStart, _SlopeEnd, n.y + (noise - 0.5) * 2.0 * _BlendNoise);

                // Rock: full triplanar. A second, larger sample hides the tiling.
                float3 rp = p * _RockScale;
                half3 rock = TriplanarColor(TEXTURE2D_ARGS(_RockTex, sampler_RockTex), rp, w);
                half3 rockFar = TriplanarColor(TEXTURE2D_ARGS(_RockTex, sampler_RockTex), rp * 0.23, w);
                rock = lerp(rock, rockFar, 0.4) * _RockTint.rgb;
                float3 rockN = TriplanarNormal(TEXTURE2D_ARGS(_RockNormal, sampler_RockNormal), rp, n, w, _RockNormalStrength);

                // Sand: only ever seen from above, so a single top-down projection is enough.
                float2 sp = p.xz * _SandScale;
                half3 sand = SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, sp).rgb;
                half3 sandFar = SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, sp * 0.19 + 0.37).rgb;
                sand = lerp(sand, sandFar, 0.5) * _SandTint.rgb;
                float3 st = UnpackNormalScale(SAMPLE_TEXTURE2D(_SandNormal, sampler_SandNormal, sp), _SandNormalStrength);
                float3 sandN = normalize(float3(st.x + n.x, abs(st.z) * n.y, st.y + n.z));

                // Algae grows on rock that faces up enough to catch light.
                float algae = saturate((n.y - 0.1) * 2.0) * smoothstep(0.45, 0.75, Fbm(p * 0.6 + 17.0)) * _AlgaeAmount;
                rock = lerp(rock, rock * 0.5 + _AlgaeColor.rgb * 0.5, algae);

                half3 albedo = lerp(rock, sand, sandMask);
                albedo *= lerp(1.0, 0.65 + macro * 0.7, _MacroVariation);
                float3 normalWS = normalize(lerp(rockN, sandN, sandMask));

                // Undersides and overhangs receive less light from the water above.
                half occlusion = lerp(1.0, saturate(n.y * 0.5 + 0.75), _CreviceDarkening);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.occlusion = occlusion;
                surface.alpha = 1;

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vertShadow (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
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

            float4 vertDepth (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
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

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            VaryingsDN vertDN (Attributes v)
            {
                VaryingsDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN (VaryingsDN i) : SV_Target
            {
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
