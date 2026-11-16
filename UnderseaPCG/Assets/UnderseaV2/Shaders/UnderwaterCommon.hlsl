#ifndef UNDERWATER_COMMON_INCLUDED
#define UNDERWATER_COMMON_INCLUDED

// Global underwater parameters, set every frame by UnderwaterEnvironment.cs.
// Shared by the full-screen water pass and by any surface shader that wants
// to fog itself (transparents, water surface).

float  _UW_Enabled;        // 1 while an UnderwaterEnvironment is active
float  _UW_WaterLevel;     // world-space height of the water surface
float3 _UW_Extinction;     // beam attenuation per metre (absorption + out-scatter), RGB
float3 _UW_DiffuseAtten;   // attenuation of downwelling sunlight per metre of depth, RGB
float3 _UW_ScatterColor;   // light scattered toward the eye, before depth attenuation
float3 _UW_SunDir;         // unit vector pointing toward the sun
float  _UW_SunPhase;       // strength of the forward-scatter glow around the sun direction
float  _UW_DepthTint;      // how strongly surfaces lose colour with their own depth

float  _UW_CausticIntensity;  // brightness of caustics on lit surfaces
float  _UW_CausticScale;      // pattern repeats per metre
float  _UW_CausticSpeed;      // animation speed
float  _UW_CausticFadeDepth;  // depth (m) at which caustics have faded to ~37%
float  _UW_CausticDispersion; // colour fringing amount
float  _UW_ShaftStrength;     // 0 = uniform water glow, 1 = fully broken into light shafts
float  _UW_ShaftScale;        // size of the shaft pattern relative to the caustics

// Fraction of sunlight left after travelling down to a point at worldY.
float3 UW_DownwellingLight(float worldY)
{
    float depth = max(0.0, _UW_WaterLevel - worldY);
    return exp(-_UW_DiffuseAtten * depth);
}

// Where the sun ray through positionWS crosses the water surface (xz).
// Everything along one sun ray shares the same caustic pattern coordinate.
float2 UW_SunProjectedXZ(float3 positionWS)
{
    float3 s = _UW_SunDir;
    return positionWS.xz + s.xz * ((_UW_WaterLevel - positionWS.y) / max(s.y, 0.2));
}

// Tileable caustic network: the minimum of three rotated cell distances forms bright lines.
float UW_CausticPattern(float2 uv, float t)
{
    static const float3x3 m = float3x3(-2, -1, 2,  3, -2, 1,  1, 2, 2);
    float3 k = float3(uv, t);
    k = mul(k, m) * 0.5; float d1 = length(0.5 - frac(k));
    k = mul(k, m) * 0.4; float d2 = length(0.5 - frac(k));
    k = mul(k, m) * 0.3; float d3 = length(0.5 - frac(k));
    return pow(min(min(d1, d2), d3), 7.0) * 25.0;
}

// RGB caustic light at a point, before masking by surface orientation and shadow.
float3 UW_Caustics(float3 positionWS)
{
    float2 uv = UW_SunProjectedXZ(positionWS) * _UW_CausticScale;
    float t = _Time.y * _UW_CausticSpeed;
    float2 fringe = float2(_UW_CausticDispersion, -_UW_CausticDispersion);

    float3 c;
    c.r = UW_CausticPattern(uv + fringe, t);
    c.g = UW_CausticPattern(uv, t);
    c.b = UW_CausticPattern(uv - fringe, t);

    // A second, larger and slower layer breaks up the regular repeat.
    float wide = UW_CausticPattern(uv * 0.37 + 4.1, t * 0.6 + 7.0);
    c *= 0.55 + wide;

    float depth = max(0.0, _UW_WaterLevel - positionWS.y);
    return c * exp(-depth / max(_UW_CausticFadeDepth, 0.01));
}

// Low-frequency version of the same pattern, used to break the water glow into shafts.
float UW_ShaftPattern(float3 positionWS)
{
    float2 uv = UW_SunProjectedXZ(positionWS) * _UW_CausticScale * _UW_ShaftScale;
    float t = _Time.y * _UW_CausticSpeed * 0.5;
    // The pattern averages about 0.074; dividing by the resulting mean (0.31) keeps the
    // overall water brightness unchanged when shafts are switched on.
    return (0.05 + 3.5 * saturate(UW_CausticPattern(uv, t))) / 0.31;
}

// Water volume along the view ray from cameraWS to positionWS.
// transmittance: how much of the surface colour survives the trip.
// inscatter:     light scattered into the ray by the water.
// surfaceLight:  colour multiplier for the surface itself (sunlight lost on the way down).
// Returns false when the camera is above water or no environment is active.
bool UW_WaterTerms(float3 positionWS, float3 cameraWS, bool isSky,
                   out float3 transmittance, out float3 inscatter, out float3 surfaceLight,
                   out float3 rayDir, out float rayLength)
{
    transmittance = 1.0;
    inscatter = 0.0;
    surfaceLight = 1.0;
    rayDir = float3(0, 0, 1);
    rayLength = 0.0;

    float camDepth = _UW_WaterLevel - cameraWS.y;
    if (_UW_Enabled < 0.5 || camDepth <= 0.0)
        return false;

    float3 ray = positionWS - cameraWS;
    float dist = length(ray);
    float3 dir = ray / max(dist, 1e-4);
    if (isSky)
        dist = 1e5;

    // The water column ends at the surface.
    float distToSurface = dir.y > 1e-4 ? camDepth / dir.y : 1e6;
    bool leavesWater = dist > distToSurface;
    dist = min(dist, distToSurface);

    float3 c = _UW_Extinction;
    float3 k = _UW_DiffuseAtten;

    if (!isSky && !leavesWater)
        surfaceLight = exp(-k * max(0.0, _UW_WaterLevel - positionWS.y) * _UW_DepthTint);

    transmittance = exp(-c * dist);

    // Closed-form integral of light scattered into the ray: the source term at a point
    // t metres along the ray is S * exp(-k * depth(t)), attenuated by exp(-c * t).
    float3 a = max(c - k * dir.y, 1e-3);
    inscatter = _UW_ScatterColor * exp(-k * camDepth) * (c / a) * (1.0 - exp(-a * dist));

    float sunAmount = saturate(dot(dir, _UW_SunDir));
    inscatter *= 1.0 + _UW_SunPhase * pow(sunAmount, 6.0);

    rayDir = dir;
    rayLength = dist;
    return true;
}

// Convenience wrapper for shaders that only need uniform water (no shafts or caustics).
float3 UW_ApplyWater(float3 color, float3 positionWS, float3 cameraWS, bool isSky)
{
    float3 transmittance, inscatter, surfaceLight, rayDir;
    float rayLength;
    if (!UW_WaterTerms(positionWS, cameraWS, isSky, transmittance, inscatter, surfaceLight, rayDir, rayLength))
        return color;
    return color * surfaceLight * transmittance + inscatter;
}

#endif
