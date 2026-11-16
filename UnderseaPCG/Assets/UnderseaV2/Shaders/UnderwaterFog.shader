// Full-screen water volume for opaque geometry and sky: caustics on lit surfaces,
// absorption, in-scattering and light shafts.
// Used by a URP Full Screen Pass Renderer Feature (Before Rendering Transparents,
// needs depth and normals).
Shader "Hidden/UnderseaV2/UnderwaterFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "UnderwaterFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
            #include "UnderwaterCommon.hlsl"

            #define SHAFT_STEPS 20
            #define SHAFT_MAX_DISTANCE 45.0

            // 0 = fully in the sun's shadow, 1 = lit. Undoes the light's Shadow Strength,
            // which is an artistic setting for surfaces and should not thin out the shafts.
            float SunShadow(float3 positionWS)
            {
                float s = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
                float strength = GetMainLightShadowParams().x;
                return strength > 1e-3 ? saturate((s - (1.0 - strength)) / strength) : 1.0;
            }

            // Ratio of sunlight actually reaching the view ray (terrain shadows and the wave
            // pattern included) to the light a uniform, unshadowed water column would get.
            // Weighted the same way as the analytic in-scatter integral, so 1 means "no change".
            float LightShafts(float3 cameraWS, float3 dir, float rayLength, float2 pixel)
            {
                float marchLength = min(rayLength, SHAFT_MAX_DISTANCE);
                float stepSize = marchLength / SHAFT_STEPS;
                float jitter = InterleavedGradientNoise(pixel, 0);

                float c = dot(_UW_Extinction, float3(0.2, 0.5, 0.3));
                float k = dot(_UW_DiffuseAtten, float3(0.2, 0.5, 0.3));

                float lit = 0.0;
                float total = 0.0;
                for (int i = 0; i < SHAFT_STEPS; i++)
                {
                    float t = (i + jitter) * stepSize;
                    float3 p = cameraWS + dir * t;
                    float depth = max(0.0, _UW_WaterLevel - p.y);
                    float w = exp(-c * t - k * depth);
                    lit += w * SunShadow(p) * UW_ShaftPattern(p);
                    total += w;
                }
                float marched = lit / max(total, 1e-4);

                // Beyond the marched segment the water is treated as uniform.
                float covered = (1.0 - exp(-c * marchLength)) / max(1.0 - exp(-c * rayLength), 1e-4);
                return lerp(1.0, marched, saturate(covered));
            }

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0;
                    float deviceDepth = rawDepth;
                #else
                    bool isSky = rawDepth >= 1.0;
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 cameraWS = _WorldSpaceCameraPos;

                float3 transmittance, inscatter, surfaceLight, rayDir;
                float rayLength;
                if (!UW_WaterTerms(positionWS, cameraWS, isSky, transmittance, inscatter, surfaceLight, rayDir, rayLength))
                    return color;

                float3 sceneColor = color.rgb;

                // Caustics: extra sunlight focused by the waves, only where the sun reaches.
                if (!isSky && positionWS.y < _UW_WaterLevel && _UW_CausticIntensity > 0.0)
                {
                    float3 normalWS = SampleSceneNormals(uv);
                    float facing = smoothstep(0.0, 0.5, dot(normalWS, _UW_SunDir));
                    float shadow = SunShadow(positionWS);
                    float fade = saturate(1.0 - rayLength / 60.0);
                    float3 caustics = UW_Caustics(positionWS) * (_UW_CausticIntensity * facing * shadow * fade);
                    sceneColor *= 1.0 + caustics;
                }

                if (_UW_ShaftStrength > 0.0)
                {
                    float shafts = LightShafts(cameraWS, rayDir, rayLength, uv * _ScreenParams.xy);
                    // The water surface is drawn later without shafts. Open-water rays just
                    // below the horizon ease back to the same uniform glow so the two meet.
                    if (isSky)
                        shafts = lerp(shafts, 1.0, smoothstep(-0.3, 0.0, rayDir.y));
                    inscatter *= lerp(1.0, shafts, _UW_ShaftStrength);
                }

                color.rgb = sceneColor * surfaceLight * transmittance + inscatter;
                return color;
            }
            ENDHLSL
        }
    }
}
