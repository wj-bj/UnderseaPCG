using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// One-off helpers for the Built-in -> URP migration. Each entry point returns a text report.
public static class UrpMigration
{
    const string SettingsDir = "Assets/Settings";
    const string RendererPath = SettingsDir + "/URP_Renderer.asset";
    const string AssetPath = SettingsDir + "/URP_Asset.asset";
    const string SnapshotPath = "Assets/Settings/MaterialSnapshot_BuiltIn.txt";

    public static string SetupPipeline()
    {
        var sb = new StringBuilder();
        if (!AssetDatabase.IsValidFolder(SettingsDir))
            AssetDatabase.CreateFolder("Assets", "Settings");

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            AssetDatabase.CreateAsset(rendererData, RendererPath);
            sb.AppendLine("created renderer, postProcessData=" + (rendererData.postProcessData != null));
        }

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        if (urp == null)
        {
            urp = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(urp, AssetPath);
            sb.AppendLine("created URP asset");
        }

        urp.supportsCameraDepthTexture = true;
        urp.supportsCameraOpaqueTexture = true;
        urp.supportsHDR = true;
        urp.msaaSampleCount = 4;
        urp.shadowDistance = 150f;
        urp.shadowCascadeCount = 4;
        EditorUtility.SetDirty(urp);

        GraphicsSettings.defaultRenderPipeline = urp;

        int original = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(original, false);

        AssetDatabase.SaveAssets();
        sb.AppendLine(Report());
        return sb.ToString();
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        var rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        sb.AppendLine("graphics=" + (rp != null ? rp.name : "NULL"));
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var a = QualitySettings.GetRenderPipelineAssetAt(i);
            sb.AppendLine("quality " + QualitySettings.names[i] + "=" + (a != null ? a.name : "inherit"));
        }
        if (rp != null)
        {
            var list = rp.rendererDataList;
            for (int i = 0; i < list.Length; i++)
            {
                var d = list[i] as UniversalRendererData;
                sb.AppendLine("renderer[" + i + "]=" + (list[i] != null ? list[i].name : "NULL")
                    + " postProcessData=" + (d != null && d.postProcessData != null));
            }
            sb.AppendLine("depthTex=" + rp.supportsCameraDepthTexture + " opaqueTex=" + rp.supportsCameraOpaqueTexture
                + " hdr=" + rp.supportsHDR + " msaa=" + rp.msaaSampleCount);
        }
        sb.AppendLine("colorSpace=" + PlayerSettings.colorSpace);
        return sb.ToString();
    }

    static IEnumerable<string> ProjectMaterialPaths()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".mat")) yield return path;
        }
    }

    static string TexPath(Material m, string prop)
    {
        if (!m.HasProperty(prop)) return "-";
        var t = m.GetTexture(prop);
        return t == null ? "null" : AssetDatabase.GetAssetPath(t);
    }

    // Writes source material values to disk while they are still on Built-in shaders.
    public static string SnapshotMaterials()
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (var path in ProjectMaterialPaths())
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            n++;
            sb.AppendLine(path + " | " + m.shader.name
                + " | main=" + TexPath(m, "_MainTex")
                + " | color=" + (m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")
                + " | bump=" + TexPath(m, "_BumpMap")
                + " | metalMap=" + TexPath(m, "_MetallicGlossMap")
                + " | metallic=" + (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString() : "-")
                + " | gloss=" + (m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness").ToString() : "-")
                + " | emis=" + TexPath(m, "_EmissionMap")
                + " | mode=" + (m.HasProperty("_Mode") ? m.GetFloat("_Mode").ToString() : "-")
                + " | cutoff=" + (m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff").ToString() : "-"));
        }
        if (!AssetDatabase.IsValidFolder(SettingsDir))
            AssetDatabase.CreateFolder("Assets", "Settings");
        File.WriteAllText(SnapshotPath, sb.ToString());
        AssetDatabase.ImportAsset(SnapshotPath);
        return "snapshot of " + n + " materials -> " + SnapshotPath;
    }

    // Converts Standard -> URP/Lit and Unlit/Color -> URP/Unlit, naming the target explicitly.
    public static string ConvertMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (lit == null || unlit == null) return "URP shaders not found";

        var sb = new StringBuilder();
        foreach (var path in ProjectMaterialPaths())
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            string src = m.shader.name;
            if (src != "Standard" && src != "Unlit/Color") continue;

            var mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            var scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            var offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

            if (src == "Unlit/Color")
            {
                m.shader = unlit;
                m.SetColor("_BaseColor", color);
            }
            else
            {
                var bump = m.GetTexture("_BumpMap");
                float bumpScale = m.GetFloat("_BumpScale");
                var metalMap = m.GetTexture("_MetallicGlossMap");
                float metallic = m.GetFloat("_Metallic");
                float gloss = m.GetFloat("_Glossiness");
                float glossMapScale = m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f;
                var occlusion = m.GetTexture("_OcclusionMap");
                float occlusionStrength = m.GetFloat("_OcclusionStrength");
                var emisMap = m.GetTexture("_EmissionMap");
                var emisColor = m.GetColor("_EmissionColor");
                bool emissive = m.IsKeywordEnabled("_EMISSION");
                int mode = (int)m.GetFloat("_Mode");
                float cutoff = m.GetFloat("_Cutoff");

                m.shader = lit;
                m.SetTexture("_BaseMap", mainTex);
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureOffset("_BaseMap", offset);
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", metalMap != null ? glossMapScale : gloss);
                m.SetFloat("_Cutoff", cutoff);
                if (bump != null)
                {
                    m.SetTexture("_BumpMap", bump);
                    m.SetFloat("_BumpScale", bumpScale);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (metalMap != null)
                {
                    m.SetTexture("_MetallicGlossMap", metalMap);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (occlusion != null)
                {
                    m.SetTexture("_OcclusionMap", occlusion);
                    m.SetFloat("_OcclusionStrength", occlusionStrength);
                    m.EnableKeyword("_OCCLUSIONMAP");
                }
                if (emissive)
                {
                    m.SetTexture("_EmissionMap", emisMap);
                    m.SetColor("_EmissionColor", emisColor);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }

                // Standard _Mode: 0 opaque, 1 cutout, 2 fade, 3 transparent
                bool transparent = mode >= 2;
                m.SetFloat("_Surface", transparent ? 1f : 0f);
                m.SetFloat("_AlphaClip", mode == 1 ? 1f : 0f);
                if (mode == 1) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                if (transparent)
                {
                    m.SetFloat("_Blend", mode == 3 ? 1f : 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_SrcBlend", mode == 3 ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    m.SetOverrideTag("RenderType", mode == 1 ? "TransparentCutout" : "Opaque");
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1f);
                    m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = mode == 1 ? (int)RenderQueue.AlphaTest : -1;
                }
            }

            EditorUtility.SetDirty(m);
            bool texOk = mainTex == null || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == mainTex;
            sb.AppendLine(path + ": " + src + " -> " + m.shader.name + " baseMapKept=" + texOk
                + " baseColor=" + m.GetColor("_BaseColor"));
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // Amplify coral/seaweed materials -> URP/Lit. Smoothness maps and transmission have no
    // direct URP/Lit slot, so smoothness becomes a scalar and transmission is dropped.
    public static string ConvertCoralMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var sb = new StringBuilder();
        foreach (var path in ProjectMaterialPaths())
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            string src = m.shader.name;
            if (src != "ASE/ASE_Standart" && src != "ASE/ASE_StandartCutout2Sided") continue;
            bool cutout = src == "ASE/ASE_StandartCutout2Sided";

            var albedo = m.GetTexture("_Albedo");
            float tiling = m.HasProperty("_Tiling") ? m.GetFloat("_Tiling") : 1f;
            var scale = m.GetTextureScale("_Albedo") * tiling;
            var offset = m.GetTextureOffset("_Albedo");
            var color = m.GetColor("_AlbedoColor");
            var normal = m.GetTexture("_NormalMap");
            float normalDepth = m.GetFloat("_NormalMapDepth");
            var metalMap = m.GetTexture("_MetallicMap");
            float metallic = m.GetFloat("_Metallic");
            float smooth = m.GetFloat("_Snoothness");
            var smoothMap = m.GetTexture("_SmoothnessMap");
            var emisMap = m.GetTexture("_EmissionMap");
            var emisColor = m.GetColor("_EmissionColor") * m.GetFloat("_EmissionMultiplayer");
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;

            sb.AppendLine(path + ": " + src + " albedo=" + (albedo ? albedo.name : "null") + " color=" + color
                + " normal=" + (normal ? normal.name : "null") + " metalMap=" + (metalMap ? metalMap.name : "null")
                + " metallic=" + metallic + " smooth=" + smooth + " smoothMap=" + (smoothMap ? smoothMap.name : "null")
                + " emisMap=" + (emisMap ? emisMap.name : "null") + " emis=" + emisColor + " cutoff=" + cutoff);

            m.shader = lit;
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalDepth);
                m.EnableKeyword("_NORMALMAP");
            }
            if (metalMap != null)
            {
                m.SetTexture("_MetallicGlossMap", metalMap);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            }
            else
            {
                m.SetFloat("_Metallic", 0f);
            }
            // Corals read as wet, slightly glossy surfaces; the old map-driven smoothness is approximated.
            m.SetFloat("_Smoothness", Mathf.Clamp01(smooth) * 0.35f);
            if (emisMap != null && emisColor.maxColorComponent > 0.001f)
            {
                m.SetTexture("_EmissionMap", emisMap);
                m.SetColor("_EmissionColor", emisColor);
                m.EnableKeyword("_EMISSION");
            }
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cutoff", cutoff);
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.doubleSidedGI = true;
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                m.SetFloat("_AlphaClip", 0f);
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
            EditorUtility.SetDirty(m);
            sb.AppendLine("   -> " + m.shader.name + " baseMap=" + (m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "null")
                + " cull=" + m.GetFloat("_Cull") + " alphaClip=" + m.GetFloat("_AlphaClip"));
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // Lists every material referenced by prefabs/models that is not on a URP-ready shader.
    public static string PrefabShaderScan()
    {
        var d = new SortedDictionary<string, int>();
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    string k = Path.GetFileName(path) + " | " + (m == null ? "<null>" : m.shader.name + " | " + m.name + " | " + AssetDatabase.GetAssetPath(m));
                    d[k] = d.ContainsKey(k) ? d[k] + 1 : 1;
                }
        }
        var sb = new StringBuilder();
        foreach (var kv in d) sb.AppendLine(kv.Value + "x " + kv.Key);
        return sb.ToString();
    }

    // Prefabs that still point at Built-in default materials get the URP package defaults.
    public static string FixBuiltinDefaultMaterials()
    {
        const string pkg = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/";
        var lit = AssetDatabase.LoadAssetAtPath<Material>(pkg + "Lit.mat");
        var particle = AssetDatabase.LoadAssetAtPath<Material>(pkg + "ParticlesUnlit.mat");
        if (lit == null || particle == null) return "URP default materials not found";

        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || AssetDatabase.GetAssetPath(m) != "Resources/unity_builtin_extra") continue;
                    bool isParticle = r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer;
                    mats[i] = isParticle ? particle : lit;
                    sb.AppendLine(path + " / " + r.name + ": " + m.name + " -> " + mats[i].name);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        return sb.Length == 0 ? "nothing to fix" : sb.ToString();
    }

    public static string ShaderInventory()
    {
        var d = new SortedDictionary<string, List<string>>();
        foreach (var path in ProjectMaterialPaths())
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            string n = m.shader == null ? "<null>" : m.shader.name;
            if (!d.ContainsKey(n)) d[n] = new List<string>();
            d[n].Add(Path.GetFileNameWithoutExtension(path));
        }
        var sb = new StringBuilder();
        foreach (var kv in d) sb.AppendLine(kv.Key + " : " + string.Join(", ", kv.Value));
        return sb.ToString();
    }
}
