using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the UnderseaV2 scene and the URP renderer it uses. Safe to run again.
public static class UnderseaV2Setup
{
    const string Root = "Assets/UnderseaV2";
    const string ScenePath = Root + "/Scenes/UnderseaV2.unity";
    const string RendererPath = Root + "/Settings/URP_Renderer_Underwater.asset";
    const string FogMaterialPath = Root + "/Materials/UnderwaterFog.mat";
    const string VolumeProfilePath = Root + "/Settings/UnderseaV2_Volume.asset";
    const string BaseRendererPath = "Assets/Settings/URP_Renderer.asset";
    const string OldScenePath = "Assets/Scenes/undersea.unity";
    const string OldSceneBackupPath = "Assets/Scenes/undersea_unsaved_backup.unity";

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    // Renderer with the full-screen water pass. Returns its index in the URP asset.
    public static string SetupRenderer()
    {
        var sb = new StringBuilder();
        EnsureFolder("Assets", "UnderseaV2");
        EnsureFolder(Root, "Settings");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Scenes");

        var shader = Shader.Find("Hidden/UnderseaV2/UnderwaterFog");
        if (shader == null) return "fog shader not found";

        var fogMat = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
        if (fogMat == null)
        {
            fogMat = new Material(shader);
            AssetDatabase.CreateAsset(fogMat, FogMaterialPath);
            sb.AppendLine("created fog material");
        }

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            if (!AssetDatabase.CopyAsset(BaseRendererPath, RendererPath)) return "could not copy base renderer";
            rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            sb.AppendLine("created underwater renderer");
        }

        // Earlier versions used URP's Full Screen Pass feature; replace it with the custom one.
        for (int i = rendererData.rendererFeatures.Count - 1; i >= 0; i--)
        {
            var old = rendererData.rendererFeatures[i];
            if (old is FullScreenPassRendererFeature && old.name == "UnderwaterFog")
            {
                rendererData.rendererFeatures.RemoveAt(i);
                AssetDatabase.RemoveObjectFromAsset(old);
                Object.DestroyImmediate(old, true);
                sb.AppendLine("removed old Full Screen Pass feature");
            }
        }

        UnderwaterVolumeFeature feature = null;
        foreach (var f in rendererData.rendererFeatures)
            if (f is UnderwaterVolumeFeature uv) feature = uv;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<UnderwaterVolumeFeature>();
            feature.name = "UnderwaterVolume";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            sb.AppendLine("added UnderwaterVolume feature");
        }
        feature.material = fogMat;
        EditorUtility.SetDirty(feature);
        rendererData.SetDirty();
        EditorUtility.SetDirty(rendererData);

        var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return "URP asset not assigned";
        var so = new SerializedObject(urp);
        var list = so.FindProperty("m_RendererDataList");
        if (list == null) return "m_RendererDataList not found on URP asset; add the renderer by hand";
        int index = -1;
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == rendererData) index = i;
        if (index < 0)
        {
            index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = rendererData;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            sb.AppendLine("registered renderer in URP asset");
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine("rendererIndex=" + index + " features=" + rendererData.rendererFeatures.Count);
        return sb.ToString();
    }

    static int UnderwaterRendererIndex()
    {
        var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var list = urp.rendererDataList;
        for (int i = 0; i < list.Length; i++)
            if (list[i] == rendererData) return i;
        return -1;
    }

    static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing) && existing != null) return existing;
        var component = profile.Add<T>(true);
        component.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
        return component;
    }

    static VolumeProfile SetupVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }
        var tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        var bloom = AddOverride<Bloom>(profile);
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.25f);
        bloom.scatter.Override(0.7f);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
    }

    const string TerrainMaterialPath = Root + "/Materials/TerrainV2.mat";
    const string GroundTexDir = "Assets/Mesh/sea-rock-free/textures/Ground/";

    // Creates the seabed material and switches the open UnderseaV2 scene's terrain to it.
    public static string SetupTerrain(int pointsPerAxis)
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "open " + ScenePath + " first";

        var shader = Shader.Find("UnderseaV2/Terrain");
        if (shader == null) return "terrain shader not found";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, TerrainMaterialPath);
            sb.AppendLine("created terrain material");
        }
        mat.shader = shader;
        var sand = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexDir + "battle_012_sand_01_basecolor.tga");
        var sandN = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexDir + "battle_012_sand_01_nxyr.tga");
        var rock = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexDir + "Tex_undead_ground_01_d.TGA");
        var rockN = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexDir + "Tex_undead_ground_01_n.TGA");
        mat.SetTexture("_SandTex", sand);
        mat.SetTexture("_SandNormal", sandN);
        mat.SetTexture("_RockTex", rock);
        mat.SetTexture("_RockNormal", rockN);
        EditorUtility.SetDirty(mat);
        sb.AppendLine("textures: sand=" + (sand != null) + " sandN=" + (sandN != null) + " rock=" + (rock != null) + " rockN=" + (rockN != null));

        var gen = Object.FindAnyObjectByType<MeshGenerator>();
        if (gen == null) return sb + "no MeshGenerator in scene";
        gen.mat = mat;
        gen.gradientNormals = true;
        gen.smoothMask = true;
        if (pointsPerAxis > 1) gen.numPointsPerAxis = pointsPerAxis;
        EditorUtility.SetDirty(gen);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("terrain: points=" + gen.numPointsPerAxis + " iso=" + gen.isoLevel + " cellular=" + gen.cellularSteps);
        return sb.ToString();
    }

    const string WaterMaterialPath = Root + "/Materials/WaterSurfaceV2.mat";

    // Replaces the old water plane's rendering with the new surface (the old object and
    // its collider stay, only its renderer is switched off).
    public static string SetupWater()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "open " + ScenePath + " first";

        var shader = Shader.Find("UnderseaV2/WaterSurface");
        if (shader == null) return "water shader not found";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            sb.AppendLine("created water material");
        }

        float level = 11f;
        var old = GameObject.Find("WaterSurface");
        if (old != null)
        {
            level = old.transform.position.y;
            var oldRenderer = old.GetComponent<MeshRenderer>();
            if (oldRenderer != null && oldRenderer.enabled)
            {
                oldRenderer.enabled = false;
                EditorUtility.SetDirty(oldRenderer);
                sb.AppendLine("disabled old WaterSurface renderer");
            }
        }

        var go = GameObject.Find("Water Surface V2");
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Water Surface V2";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<WaterSurfaceFollow>();
            sb.AppendLine("created Water Surface V2");
        }
        go.transform.position = new Vector3(0f, level, 0f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = new Vector3(60f, 1f, 60f); // built-in plane is 10 m, so 600 m across
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        EditorUtility.SetDirty(renderer);

        var env = Object.FindAnyObjectByType<UnderwaterEnvironment>();
        if (env != null)
        {
            env.waterSurface = go.transform;
            EditorUtility.SetDirty(env);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("water level=" + level);
        return sb.ToString();
    }

    static Material VegetationMaterial(string name, Texture tex, Color baseColor, Color tipColor, bool alphaClip,
        float swayAmplitude, float swayFrequency, float swayWaves, float stiffness, float translucency, float variation)
    {
        string path = Root + "/Materials/Vegetation_" + name + ".mat";
        var shader = Shader.Find("UnderseaV2/Vegetation");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.enableInstancing = true;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_TipColor", tipColor);
        mat.SetFloat("_ColorVariation", variation);
        mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        if (alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_SwayAmplitude", swayAmplitude);
        mat.SetFloat("_SwayFrequency", swayFrequency);
        mat.SetFloat("_SwayWaves", swayWaves);
        mat.SetFloat("_SwayStiffness", stiffness);
        mat.SetFloat("_Translucency", translucency);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh FindMesh(string assetPath, string meshName)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            if (obj is Mesh m && m.name == meshName) return m;
        return null;
    }

    // Creates the GPU vegetation system with its layers and switches off the old
    // GameObject-based decoration spawning in the UnderseaV2 scene.
    public static string SetupVegetation()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "open " + ScenePath + " first";

        const string coralFbx = "Assets/Lib3rd/_Corals/Corals.FBX";
        const string weedFbx = "Assets/Lib3rd/_Corals/Seaweeds.FBX";
        const string texDir = "Assets/Lib3rd/_Corals/Textures/";
        var texA = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "Corals_1_5_8_9_diff.png");
        var texB = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "Corals_2_7_10_12_diff.png");
        var texC = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "Corals_13_14_15_16_diff.png");
        var texD = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "Corals_3and6_diff.png");

        var grassMat = VegetationMaterial("Seagrass", null, new Color(0.10f, 0.30f, 0.12f), new Color(1.9f, 2.1f, 1.3f), false, 0.28f, 1.6f, 1.2f, 1.4f, 0.6f, 0.35f);
        var kelpMat = VegetationMaterial("Kelp", null, new Color(0.22f, 0.27f, 0.08f), new Color(1.5f, 1.6f, 1.0f), false, 0.2f, 0.8f, 2.2f, 1.3f, 0.7f, 0.3f);
        var weedMat = VegetationMaterial("Seaweed", texD, Color.white, Color.white, true, 0.22f, 1.2f, 1.6f, 1.5f, 0.6f, 0.25f);
        var fanMat = VegetationMaterial("FanCoral", texD, Color.white, Color.white, true, 0.04f, 0.9f, 1.0f, 2.0f, 0.5f, 0.2f);
        var coralMatA = VegetationMaterial("CoralA", texA, Color.white, Color.white, false, 0f, 1f, 1f, 2f, 0.1f, 0.2f);
        var coralMatB = VegetationMaterial("CoralB", texB, Color.white, Color.white, false, 0f, 1f, 1f, 2f, 0.1f, 0.2f);
        var coralMatC = VegetationMaterial("CoralC", texC, Color.white, Color.white, false, 0f, 1f, 1f, 2f, 0.1f, 0.2f);

        var system = Object.FindAnyObjectByType<VegetationSystem>();
        if (system == null)
        {
            system = new GameObject("Vegetation System").AddComponent<VegetationSystem>();
            sb.AppendLine("created Vegetation System");
        }
        system.scatterShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/VegetationScatter.compute");
        system.meshGenerator = Object.FindAnyObjectByType<MeshGenerator>();
        system.mapGenerator = Object.FindAnyObjectByType<MapGenerator>();
        system.environment = Object.FindAnyObjectByType<UnderwaterEnvironment>();

        var layers = new System.Collections.Generic.List<VegetationLayer>();
        int seed = 1;

        layers.Add(new VegetationLayer {
            name = "Seagrass", source = VegetationLayer.MeshSource.GrassClump, material = grassMat,
            density = 5f, slopeRange = new Vector2(0.8f, 1f), scaleRange = new Vector2(0.35f, 0.9f), surfaceClearance = 1.5f,
            alignToNormal = 0.3f, patchFrequency = 0.12f, patchThreshold = 0.45f, patchSeed = 11,
            castShadows = false, maxInstances = 60000, seed = seed++ });

        layers.Add(new VegetationLayer {
            name = "Kelp", source = VegetationLayer.MeshSource.Kelp, material = kelpMat,
            density = 0.4f, slopeRange = new Vector2(0.55f, 1f), scaleRange = new Vector2(2.5f, 6f), surfaceClearance = 6.5f,
            alignToNormal = 0f, patchFrequency = 0.08f, patchThreshold = 0.55f, patchSeed = 23,
            castShadows = true, maxInstances = 4000, seed = seed++ });

        foreach (var weed in new[] { "Seaweed", "Seaweed_G" })
            layers.Add(new VegetationLayer {
                name = weed, mesh = FindMesh(weedFbx, weed), material = weedMat,
                density = weed == "Seaweed" ? 1.6f : 0.5f, slopeRange = new Vector2(0.6f, 1f), scaleRange = new Vector2(0.8f, 2.2f), surfaceClearance = 2.5f,
                alignToNormal = 0.1f, patchFrequency = 0.15f, patchThreshold = 0.5f, patchSeed = 37,
                castShadows = false, maxInstances = 12000, seed = seed++ });

        foreach (var fan in new[] { "Coral_3_1", "Coral_3_2" })
            layers.Add(new VegetationLayer {
                name = fan, mesh = FindMesh(coralFbx, fan), material = fanMat,
                density = 0.25f, slopeRange = new Vector2(0.3f, 1f), scaleRange = new Vector2(1.5f, 3f), surfaceClearance = 2f,
                alignToNormal = 0.5f, patchFrequency = 0.1f, patchThreshold = 0.5f, patchSeed = 51,
                castShadows = false, maxInstances = 3000, seed = seed++ });

        // Solid corals: all share one patch seed so they grow together as reefs.
        var corals = new (string mesh, Material mat)[] {
            // The lower-polygon meshes of the pack (roughly 400-1800 triangles each).
            ("Coral3_1", coralMatA), ("Coral3_2", coralMatA), ("Coral1_2", coralMatA), ("Coral4_2", coralMatA),
            ("Coral5_2", coralMatB), ("Coral6_1", coralMatB), ("Coral7_1", coralMatB), ("Coral9_2", coralMatB),
            ("Coral_13_2", coralMatC), ("Coral_13_3", coralMatC), ("Coral_15_3", coralMatC), ("Coral_16_1", coralMatC) };
        foreach (var c in corals)
            layers.Add(new VegetationLayer {
                name = c.mesh, mesh = FindMesh(coralFbx, c.mesh), material = c.mat,
                density = 0.09f, slopeRange = new Vector2(0.35f, 1f), scaleRange = new Vector2(1.5f, 3.5f), surfaceClearance = 2f,
                alignToNormal = 0.6f, patchFrequency = 0.1f, patchThreshold = 0.52f, patchSeed = 51,
                castShadows = false, maxInstances = 3000, seed = seed++ });

        int missing = 0;
        foreach (var l in layers)
            if (l.source == VegetationLayer.MeshSource.Asset && l.mesh == null) { missing++; sb.AppendLine("missing mesh: " + l.name); }
        system.layers = layers;
        EditorUtility.SetDirty(system);

        // The old generator spawned one GameObject per plant; the GPU system replaces it here.
        var manager = Object.FindAnyObjectByType<SceneManager>();
        if (manager != null && manager.decorationGenerator != null)
        {
            manager.decorationGenerator.ClearDecorations();
            manager.decorationGenerator = null;
            EditorUtility.SetDirty(manager);
            sb.AppendLine("disconnected old DecorationGenerator");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        system.Rebuild();
        sb.AppendLine("layers=" + layers.Count + " missing=" + missing);
        sb.AppendLine(system.Report());
        return sb.ToString();
    }

    // Creates the multi-species GPU fish system and removes the old single-species
    // flock prefab from the list of agents this scene spawns.
    public static string SetupFish()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "open " + ScenePath + " first";

        const string fishFbx = "Assets/Lib3rd/Fish/Models/fish_modified.fbx";
        Mesh fishMesh = null;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fishFbx))
            if (obj is Mesh m) fishMesh = m;
        if (fishMesh == null) return "fish mesh not found";

        string matPath = Root + "/Materials/FishV2.mat";
        var shader = Shader.Find("UnderseaV2/Fish");
        if (shader == null) return "fish shader not found";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        mat.enableInstancing = true;
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Lib3rd/Fish/Models/Textures/fish_texture.png"));
        EditorUtility.SetDirty(mat);

        var system = Object.FindAnyObjectByType<FishSchoolSystem>();
        if (system == null)
        {
            system = new GameObject("Fish School System").AddComponent<FishSchoolSystem>();
            sb.AppendLine("created Fish School System");
        }
        system.simulation = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/FishSchool.compute");
        system.fishMesh = fishMesh;
        system.fishMaterial = mat;
        system.meshGenerator = Object.FindAnyObjectByType<MeshGenerator>();
        system.environment = Object.FindAnyObjectByType<UnderwaterEnvironment>();

        // The source mesh is a thin fish about 0.65 m long; bodyScale is (thickness, height, length).
        system.species = new System.Collections.Generic.List<FishSpecies>
        {
            new FishSpecies {
                name = "Sardine", count = 1400, schools = 4, sizeRange = new Vector2(0.28f, 0.4f),
                speedRange = new Vector2(1.6f, 2.6f), neighbourRadius = 1.6f, separationRadius = 0.35f,
                cohesion = 1.2f, alignment = 1.6f, separation = 2.2f, turnRate = 4f, depthBand = new Vector2(2f, 9f),
                fleeRadius = 3f, wander = 0.25f,
                bodyScale = new Vector3(2.2f, 0.75f, 1.05f), backColor = new Color(0.16f, 0.30f, 0.42f), bellyColor = new Color(0.86f, 0.90f, 0.92f),
                shine = 1.2f, tailAmplitude = 0.13f, tailFrequency = 14f },
            new FishSpecies {
                name = "YellowTang", count = 260, schools = 4, sizeRange = new Vector2(0.4f, 0.55f),
                speedRange = new Vector2(0.9f, 1.6f), neighbourRadius = 2f, separationRadius = 0.6f,
                cohesion = 0.9f, alignment = 0.9f, separation = 2f, turnRate = 3f, depthBand = new Vector2(7f, 15f),
                fleeRadius = 2.5f, wander = 0.45f,
                bodyScale = new Vector3(2.4f, 1.55f, 0.8f), backColor = new Color(0.95f, 0.72f, 0.08f), bellyColor = new Color(1f, 0.86f, 0.25f),
                shine = 0.35f, tailAmplitude = 0.1f, tailFrequency = 10f },
            new FishSpecies {
                name = "StripedReef", count = 220, schools = 6, sizeRange = new Vector2(0.25f, 0.36f),
                speedRange = new Vector2(0.6f, 1.2f), neighbourRadius = 1.5f, separationRadius = 0.45f,
                cohesion = 0.7f, alignment = 0.6f, separation = 2f, turnRate = 4.5f, depthBand = new Vector2(9f, 16f),
                fleeRadius = 2.5f, wander = 0.6f,
                bodyScale = new Vector3(3f, 1.2f, 0.85f), backColor = new Color(0.95f, 0.38f, 0.08f), bellyColor = new Color(1f, 0.55f, 0.18f),
                stripeColor = new Color(0.96f, 0.96f, 0.92f), stripeCount = 3f, stripeStrength = 0.95f,
                shine = 0.25f, tailAmplitude = 0.16f, tailFrequency = 12f },
            new FishSpecies {
                name = "BlueFusilier", count = 320, schools = 3, sizeRange = new Vector2(0.45f, 0.6f),
                speedRange = new Vector2(1.4f, 2.4f), neighbourRadius = 2.2f, separationRadius = 0.55f,
                cohesion = 1f, alignment = 1.4f, separation = 2f, turnRate = 3.2f, depthBand = new Vector2(3f, 12f),
                fleeRadius = 3f, wander = 0.3f,
                bodyScale = new Vector3(2.4f, 0.85f, 1.15f), backColor = new Color(0.08f, 0.32f, 0.85f), bellyColor = new Color(0.95f, 0.85f, 0.25f),
                shine = 0.6f, tailAmplitude = 0.12f, tailFrequency = 12f },
            new FishSpecies {
                name = "Barracuda", count = 40, schools = 2, sizeRange = new Vector2(1.3f, 1.7f),
                speedRange = new Vector2(1.0f, 1.8f), neighbourRadius = 3f, separationRadius = 1.4f,
                cohesion = 0.6f, alignment = 1.2f, separation = 2f, turnRate = 2f, depthBand = new Vector2(2f, 10f),
                fleeRadius = 3f, wander = 0.25f,
                bodyScale = new Vector3(2.2f, 0.5f, 1.5f), backColor = new Color(0.22f, 0.28f, 0.30f), bellyColor = new Color(0.80f, 0.84f, 0.86f),
                stripeColor = new Color(0.08f, 0.10f, 0.12f), stripeCount = 9f, stripeStrength = 0.35f,
                shine = 1.0f, tailAmplitude = 0.09f, tailFrequency = 7f },
            new FishSpecies {
                name = "ReefShark", count = 4, schools = 4, sizeRange = new Vector2(3.6f, 4.6f),
                speedRange = new Vector2(1.3f, 2.4f), neighbourRadius = 3f, separationRadius = 3f,
                cohesion = 0f, alignment = 0f, separation = 2f, turnRate = 1.1f, depthBand = new Vector2(3f, 14f),
                fleeRadius = 0f, isPredator = true, wander = 0.5f,
                bodyScale = new Vector3(4.2f, 0.62f, 1.25f), backColor = new Color(0.20f, 0.23f, 0.26f), bellyColor = new Color(0.78f, 0.80f, 0.80f),
                shine = 0.3f, tailAmplitude = 0.11f, tailFrequency = 4.5f },
        };
        EditorUtility.SetDirty(system);

        var agents = Object.FindAnyObjectByType<AgentGenerator>();
        if (agents != null && agents.agents != null)
        {
            var kept = new System.Collections.Generic.List<GameObject>();
            foreach (var prefab in agents.agents)
            {
                if (prefab != null && prefab.GetComponentInChildren<FishFlocking>(true) != null)
                    sb.AppendLine("removed old flock prefab from spawn list: " + prefab.name);
                else
                    kept.Add(prefab);
            }
            agents.agents = kept.ToArray();
            EditorUtility.SetDirty(agents);
        }

        int total = 0;
        foreach (var sp in system.species) total += sp.count;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("species=" + system.species.Count + " fish=" + total);
        return sb.ToString();
    }

    // Rebuilds map, terrain and decorations in edit mode.
    public static string Regenerate()
    {
        var manager = Object.FindAnyObjectByType<SceneManager>();
        if (manager == null) return "no SceneManager in scene";
        manager.UpdateScene();
        int verts = 0;
        foreach (var chunk in Object.FindObjectsByType<Chunk>(FindObjectsInactive.Exclude))
        {
            var mf = chunk.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) verts += mf.sharedMesh.vertexCount;
        }
        return "regenerated, terrain vertices=" + verts;
    }

    // Copies the open undersea scene (including unsaved edits) into a new scene, then
    // replaces the Built-in era camera effects with the URP water volume.
    public static string CreateScene()
    {
        var sb = new StringBuilder();
        int rendererIndex = UnderwaterRendererIndex();
        if (rendererIndex < 0) return "run SetupRenderer first";

        var active = EditorSceneManager.GetActiveScene();
        if (active.path == OldScenePath)
        {
            if (active.isDirty && !System.IO.File.Exists(OldSceneBackupPath))
            {
                EditorSceneManager.SaveScene(active, OldSceneBackupPath, true);
                sb.AppendLine("backed up unsaved old scene to " + OldSceneBackupPath);
            }
            if (!System.IO.File.Exists(ScenePath))
            {
                EditorSceneManager.SaveScene(active, ScenePath, true);
                sb.AppendLine("created " + ScenePath);
            }
        }
        if (!System.IO.File.Exists(ScenePath)) return sb + "open " + OldScenePath + " first";

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var cam = Camera.main;
        if (cam == null) return sb + "no main camera in scene";
        foreach (var legacy in cam.GetComponents<MonoBehaviour>())
        {
            if (legacy is FogEffectCommandBuffer || legacy is UnderwaterEffect || legacy is FogEffect)
            {
                sb.AppendLine("removed " + legacy.GetType().Name);
                Object.DestroyImmediate(legacy);
            }
        }
        cam.farClipPlane = 150f;
        cam.allowHDR = true;
        var camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camData.SetRenderer(rendererIndex);
        camData.renderPostProcessing = true;
        EditorUtility.SetDirty(camData);

        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.type == LightType.Directional) sun = l;
        var water = GameObject.Find("WaterSurface");

        var env = Object.FindFirstObjectByType<UnderwaterEnvironment>();
        if (env == null)
        {
            env = new GameObject("Underwater Environment").AddComponent<UnderwaterEnvironment>();
            sb.AppendLine("added Underwater Environment");
        }
        env.sun = sun;
        env.waterSurface = water != null ? water.transform : null;
        EditorUtility.SetDirty(env);

        var profile = SetupVolumeProfile();
        var volume = Object.FindFirstObjectByType<Volume>();
        if (volume == null) volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);

        RenderSettings.fog = false;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("saved " + scene.path + " rendererIndex=" + rendererIndex + " sun=" + (sun ? sun.name : "none")
            + " water=" + (water ? water.transform.position.y.ToString() : "none")
            + " volumeOverrides=" + profile.components.Count);
        return sb.ToString();
    }
}
