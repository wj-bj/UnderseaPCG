// Water surface, designed to be seen from below.
// Inside Snell's window the sky and sun show through the rippling surface; outside it
// the surface is a mirror of the water below (total internal reflection).
Shader "UnderseaV2/WaterSurface"
{
    Properties
    {
        [Header(Waves)]
        _WaveScale ("Ripple Size (per metre)", Float) = 0.55
        _WaveSpeed ("Ripple Speed", Float) = 0.6
        _WaveStrength ("Ripple Strength", Range(0, 2)) = 0.55
        _SwellStrength ("Swell Strength", Range(0, 1)) = 0.25
        _WaveFadeDistance ("Ripple Fade Distance", Float) = 90

        [Header(Seen From Below)]
        _WindowBrightness ("Sky Brightness Through Window", Range(0, 5)) = 1.6
        _SunDiscIntensity ("Sun Disc Intensity", Range(0, 60)) = 18
        _SunGlowIntensity ("Sun Glow Intensity", Range(0, 5)) = 0.9
        _ReflectionTint ("Underside Reflection Tint", Color) = (0.85, 0.95, 1, 1)

        [Header(Seen From Above)]
        _DeepColor ("Deep Water Colour", Color) = (0.02, 0.16, 0.24, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "UnderwaterCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _WaveScale;
                float _WaveSpeed;
                float _WaveStrength;
                float _SwellStrength;
                float _WaveFadeDistance;
                float _WindowBrightness;
                float _SunDiscIntensity;
                float _SunGlowIntensity;
                half4 _ReflectionTint;
                half4 _DeepColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // Height of the surface: four ripple layers drifting in different directions
            // plus two long swells.
            float WaveHeight(float2 xz, float t, float ripple)
            {
                float2 p = xz * _WaveScale;
                float h = 0.0;
                h += Noise2(p * 1.0 + t * float2(0.9, 0.4)) * 0.45;
                h += Noise2(p * 2.1 + t * float2(-0.7, 0.8) + 13.1) * 0.28;
                h += Noise2(p * 4.3 + t * float2(0.5, -1.1) + 41.7) * 0.17;
                h += Noise2(p * 9.1 + t * float2(-1.3, -0.6) + 7.9) * 0.10;
                h *= ripple;
                h += sin(dot(xz, float2(0.11, 0.07)) + t * 0.8) * _SwellStrength;
                h += sin(dot(xz, float2(-0.05, 0.13)) + t * 0.6 + 1.7) * _SwellStrength * 0.7;
                return h;
            }

            float3 SkyColor(float3 dir)
            {
                half4 env = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, dir, 0);
                return DecodeHDREnvironment(env, unity_SpecCube0_HDR);
            }

            half4 frag (Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 p = i.positionWS;
                float3 cam = _WorldSpaceCameraPos;
                float3 toCam = cam - p;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 1e-4);

                // Ripples fade with distance so the far surface does not shimmer.
                float ripple = _WaveStrength * saturate(1.0 - dist / max(_WaveFadeDistance, 1.0));
                float t = _Time.y * _WaveSpeed;
                const float e = 0.04;
                float h0 = WaveHeight(p.xz, t, ripple);
                float hx = WaveHeight(p.xz + float2(e, 0), t, ripple);
                float hz = WaveHeight(p.xz + float2(0, e), t, ripple);
                float3 N = normalize(float3(-(hx - h0) / e, 1.0, -(hz - h0) / e));

                Light sun = GetMainLight();
                float3 color;

                if (cam.y < p.y)
                {
                    // Looking up from the water. The surface normal on our side points down.
                    float3 I = -V;
                    float3 Nd = -N;
                    float3 T = refract(I, Nd, 1.33);
                    bool totalReflection = dot(T, T) < 1e-5;

                    // Mirror image of the water below.
                    float3 R = reflect(I, Nd);
                    R.y = -abs(R.y);
                    float3 below = p - float3(0, 0.05, 0);
                    float3 reflected = UW_ApplyWater(0.0, below + R * 1000.0, below, true) * _ReflectionTint.rgb;

                    if (totalReflection)
                    {
                        color = reflected;
                    }
                    else
                    {
                        T = normalize(T);
                        float cosT = saturate(dot(T, N));
                        float fresnel = 0.02 + 0.98 * pow(1.0 - cosT, 5.0);

                        float3 sky = SkyColor(T) * _WindowBrightness;
                        float sunAmount = saturate(dot(T, sun.direction));
                        sky += sun.color * (pow(sunAmount, 600.0) * _SunDiscIntensity + pow(sunAmount, 24.0) * _SunGlowIntensity);

                        color = lerp(sky, reflected, fresnel);
                    }

                    color = UW_ApplyWater(color, p, cam, false);
                }
                else
                {
                    // Looking down from the air: sky reflection over deep water colour.
                    float cosV = saturate(dot(V, N));
                    float fresnel = 0.02 + 0.98 * pow(1.0 - cosV, 5.0);
                    float3 R = reflect(-V, N);
                    R.y = abs(R.y);
                    float3 sky = SkyColor(R);
                    float sunAmount = saturate(dot(R, sun.direction));
                    sky += sun.color * pow(sunAmount, 300.0) * _SunDiscIntensity;
                    color = lerp(_DeepColor.rgb, sky, fresnel);
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
