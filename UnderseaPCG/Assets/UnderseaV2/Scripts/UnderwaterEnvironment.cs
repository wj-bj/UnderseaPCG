using UnityEngine;

// Single source of truth for the water volume. Pushes its settings to global shader
// properties (see UnderwaterCommon.hlsl) so every underwater shader agrees on them.
[ExecuteAlways]
public class UnderwaterEnvironment : MonoBehaviour
{
    [Header("Water")]
    [Tooltip("Optional. When set, the water level follows this transform's height.")]
    public Transform waterSurface;
    public float waterLevel = 11f;

    [Header("Absorption")]
    [Tooltip("How fast each colour channel is lost per metre along the view ray (R, G, B). Red goes first in real water.")]
    public Vector3 extinction = new Vector3(0.08f, 0.025f, 0.016f);
    [Min(0f)] public float density = 1f;
    [Tooltip("Sunlight loss per metre of depth, as a fraction of extinction. Must stay below 1.")]
    [Range(0.05f, 0.95f)] public float downwellingRatio = 0.4f;
    [Tooltip("How strongly surfaces lose colour with their own depth.")]
    [Range(0f, 2f)] public float depthTint = 0.5f;

    [Header("Scattering")]
    [ColorUsage(false)] public Color scatterColor = new Color(0.14f, 0.58f, 0.85f);
    [Min(0f)] public float scatterIntensity = 0.9f;
    [Tooltip("Extra glow when looking toward the sun.")]
    [Range(0f, 4f)] public float sunPhase = 1.8f;

    [Header("Caustics")]
    [Tooltip("Brightness of the rippling light on sunlit surfaces.")]
    [Range(0f, 8f)] public float causticIntensity = 4f;
    [Tooltip("Pattern repeats per metre.")]
    [Min(0.01f)] public float causticScale = 1.3f;
    [Min(0f)] public float causticSpeed = 0.35f;
    [Tooltip("Depth in metres at which caustics have mostly faded.")]
    [Min(0.1f)] public float causticFadeDepth = 30f;
    [Tooltip("Colour fringing at the edges of the caustic lines.")]
    [Range(0f, 0.05f)] public float causticDispersion = 0.012f;

    [Header("Light Shafts")]
    [Tooltip("0 = uniform water glow, 1 = glow fully broken into shafts by shadows and waves.")]
    [Range(0f, 1f)] public float shaftStrength = 1f;
    [Tooltip("Shaft width relative to the caustic pattern. Smaller values give wider shafts.")]
    [Range(0.05f, 1f)] public float shaftScale = 0.12f;

    [Header("Sun")]
    public Light sun;

    static readonly int EnabledId = Shader.PropertyToID("_UW_Enabled");
    static readonly int WaterLevelId = Shader.PropertyToID("_UW_WaterLevel");
    static readonly int ExtinctionId = Shader.PropertyToID("_UW_Extinction");
    static readonly int DiffuseAttenId = Shader.PropertyToID("_UW_DiffuseAtten");
    static readonly int ScatterColorId = Shader.PropertyToID("_UW_ScatterColor");
    static readonly int SunDirId = Shader.PropertyToID("_UW_SunDir");
    static readonly int SunPhaseId = Shader.PropertyToID("_UW_SunPhase");
    static readonly int DepthTintId = Shader.PropertyToID("_UW_DepthTint");
    static readonly int CausticIntensityId = Shader.PropertyToID("_UW_CausticIntensity");
    static readonly int CausticScaleId = Shader.PropertyToID("_UW_CausticScale");
    static readonly int CausticSpeedId = Shader.PropertyToID("_UW_CausticSpeed");
    static readonly int CausticFadeDepthId = Shader.PropertyToID("_UW_CausticFadeDepth");
    static readonly int CausticDispersionId = Shader.PropertyToID("_UW_CausticDispersion");
    static readonly int ShaftStrengthId = Shader.PropertyToID("_UW_ShaftStrength");
    static readonly int ShaftScaleId = Shader.PropertyToID("_UW_ShaftScale");

    public float WaterLevel => waterSurface != null ? waterSurface.position.y : waterLevel;

    void OnEnable() { Apply(); }
    void Update() { Apply(); }
    void OnValidate() { Apply(); }

    void OnDisable()
    {
        Shader.SetGlobalFloat(EnabledId, 0f);
    }

    void Apply()
    {
        if (!isActiveAndEnabled) return;

        Vector3 c = Vector3.Max(extinction, Vector3.one * 0.001f) * density;
        Vector3 k = c * downwellingRatio;

        Color sunColor = Color.white;
        Vector3 sunDir = Vector3.up;
        if (sun != null)
        {
            sunColor = sun.color.linear * sun.intensity;
            sunDir = -sun.transform.forward;
        }
        Color scatter = scatterColor.linear * scatterIntensity;
        scatter = new Color(scatter.r * sunColor.r, scatter.g * sunColor.g, scatter.b * sunColor.b);

        Shader.SetGlobalFloat(EnabledId, 1f);
        Shader.SetGlobalFloat(WaterLevelId, WaterLevel);
        Shader.SetGlobalVector(ExtinctionId, c);
        Shader.SetGlobalVector(DiffuseAttenId, k);
        Shader.SetGlobalVector(ScatterColorId, new Vector4(scatter.r, scatter.g, scatter.b, 0f));
        Shader.SetGlobalVector(SunDirId, sunDir.normalized);
        Shader.SetGlobalFloat(SunPhaseId, sunPhase);
        Shader.SetGlobalFloat(DepthTintId, depthTint);
        Shader.SetGlobalFloat(CausticIntensityId, causticIntensity);
        Shader.SetGlobalFloat(CausticScaleId, causticScale);
        Shader.SetGlobalFloat(CausticSpeedId, causticSpeed);
        Shader.SetGlobalFloat(CausticFadeDepthId, causticFadeDepth);
        Shader.SetGlobalFloat(CausticDispersionId, causticDispersion);
        Shader.SetGlobalFloat(ShaftStrengthId, shaftStrength);
        Shader.SetGlobalFloat(ShaftScaleId, shaftScale);
    }
}
