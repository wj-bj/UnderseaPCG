using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Rebuilds the UnderseaV2 scene's look stage by stage, for before/after screenshots.
// Play mode only; nothing here is saved to the scene.
//   0 URP migration only (no water volume)   1 + water fog   2 + new terrain
//   3 + caustics and light shafts
//   4 + new water surface   5 + GPU vegetation   6 + multi-species fish
public static class ShowcaseStages
{
    static bool captured;
    static float causticIntensity, shaftStrength;
    static Material newTerrainMaterial;
    static int newPoints;
    static int terrainState = -1;

    static T Find<T>() where T : Object
    {
        return Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    }

    public static string SetStage(int stage)
    {
        if (!Application.isPlaying) return "enter Play mode first";
        var env = Find<UnderwaterEnvironment>();
        var gen = Find<MeshGenerator>();
        var map = Find<MapGenerator>();
        var veg = Find<VegetationSystem>();
        var fish = Find<FishSchoolSystem>();
        var colour = Find<ColourGenerator>();
        if (env == null || gen == null || map == null) return "scene is missing systems";

        if (!captured)
        {
            causticIntensity = env.causticIntensity;
            shaftStrength = env.shaftStrength;
            newTerrainMaterial = gen.mat;
            newPoints = gen.numPointsPerAxis;
            captured = true;
        }

        bool newTerrain = stage >= 2;
        bool lightEffects = stage >= 3;
        bool newWater = stage >= 4;

        env.enabled = stage >= 1;
        env.causticIntensity = lightEffects ? causticIntensity : 0f;
        env.shaftStrength = lightEffects ? shaftStrength : 0f;

        if (veg != null) veg.enabled = stage >= 5;
        if (fish != null) fish.enabled = stage >= 6;

        // Terrain: the original setup (40 samples per axis, hard mask, per-face normals,
        // height-ramp material) versus the new one.
        int wanted = newTerrain ? 1 : 0;
        if (terrainState != wanted)
        {
            terrainState = wanted;
            gen.gradientNormals = newTerrain;
            gen.smoothMask = newTerrain;
            gen.numPointsPerAxis = newTerrain ? newPoints : 40;
            gen.mat = newTerrain || colour == null ? newTerrainMaterial : colour.mat;
            gen.generateMaskPoints(map.densityMap);
            gen.Run();
            gen.UpdateCollider();
        }

        // Water surface. Until the caustics stage the old plane also cast a shadow over
        // the whole seabed, which is how the scene actually looked at that point.
        var oldWater = GameObject.Find("WaterSurface");
        if (oldWater != null)
        {
            var r = oldWater.GetComponent<MeshRenderer>();
            r.enabled = !newWater;
            r.shadowCastingMode = lightEffects ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
        var water = GameObject.Find("Water Surface V2");
        if (water != null) water.GetComponent<MeshRenderer>().enabled = newWater;

        return "stage " + stage + " terrainPoints=" + gen.numPointsPerAxis + " vegetation=" + (veg != null && veg.enabled) + " fish=" + (fish != null && fish.enabled);
    }

    // Fixed viewpoints shared by every stage.
    public static string MakeCameras()
    {
        Make("ShotReef", new Vector3(6f, 8f, -8f), new Vector3(0f, -4f, 2f));
        Make("ShotWide", new Vector3(14f, 1.5f, -14f), new Vector3(0f, 3f, 4f));
        Make("ShotUp", new Vector3(10f, 2f, -10f), new Vector3(8f, 11f, -4f));
        return "cameras ready";
    }

    static void Make(string name, Vector3 position, Vector3 lookAt)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.farClipPlane = 150f;
            cam.fieldOfView = 55f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(1);
            data.renderPostProcessing = true;
        }
        go.transform.position = position;
        go.transform.LookAt(lookAt);
    }
}
