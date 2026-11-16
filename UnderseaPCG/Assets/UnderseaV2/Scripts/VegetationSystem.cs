using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
public class VegetationLayer
{
    public enum MeshSource { Asset, GrassClump, Kelp }

    public string name = "Layer";
    public bool enabled = true;
    public MeshSource source = MeshSource.Asset;
    public Mesh mesh;
    public Material material;

    [Header("Placement")]
    [Tooltip("Plants per square metre of suitable ground.")]
    public float density = 1f;
    [Tooltip("Allowed ground steepness as normal.y: 1 = flat, 0 = vertical wall.")]
    public Vector2 slopeRange = new Vector2(0.6f, 1f);
    [Tooltip("Allowed world height. The top is also limited by the water level.")]
    public Vector2 heightRange = new Vector2(-1000f, 1000f);
    public Vector2 scaleRange = new Vector2(1f, 2f);
    [Tooltip("Distance the plant must stay below the water surface (use its full height for tall plants).")]
    public float surfaceClearance = 1f;
    [Range(0f, 1f)] public float alignToNormal = 0.3f;

    [Header("Patches")]
    [Tooltip("Larger value = smaller, more frequent patches.")]
    public float patchFrequency = 0.12f;
    [Tooltip("0 = everywhere, towards 1 = only in the middle of patches.")]
    [Range(0f, 1f)] public float patchThreshold = 0.45f;
    [Tooltip("Layers with the same patch seed grow in the same patches.")]
    public int patchSeed = 1;
    [Tooltip("0 = ignore the map, 1 = only in reef zones, -1 = only outside reef zones.")]
    [Range(-1, 1)] public int reefMode = 0;

    [Header("Rendering")]
    public bool castShadows = true;
    public int maxInstances = 20000;
    public int seed = 1;

    [System.NonSerialized] public ComputeBuffer instances;
    [System.NonSerialized] public ComputeBuffer args;
    [System.NonSerialized] public Mesh runtimeMesh;
    [System.NonSerialized] public MaterialPropertyBlock props;
    [System.NonSerialized] public int instanceCount;
}

// Scatters plants and corals over the generated terrain on the GPU and draws them with
// instancing. Rebuilds whenever the terrain is regenerated.
[ExecuteAlways]
public class VegetationSystem : MonoBehaviour
{
    public ComputeShader scatterShader;
    public MeshGenerator meshGenerator;
    public MapGenerator mapGenerator;
    public UnderwaterEnvironment environment;
    public List<VegetationLayer> layers = new List<VegetationLayer>();

    ComputeBuffer vertexBuffer;
    ComputeBuffer normalBuffer;
    ComputeBuffer reefBuffer;
    Bounds drawBounds = new Bounds(Vector3.zero, Vector3.one * 500f);
    bool dirty = true;
    MeshGenerator subscribedTo;

    const int InstanceStride = sizeof(float) * 12;
    static readonly int InstancesId = Shader.PropertyToID("_Instances");
    static readonly int MeshMatrixId = Shader.PropertyToID("_MeshMatrix");
    static readonly int MeshHeightId = Shader.PropertyToID("_MeshHeight");

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        Subscribe();
        dirty = true;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (subscribedTo != null) subscribedTo.MeshesUpdated -= MarkDirty;
        subscribedTo = null;
        ReleaseAll();
    }

    void OnValidate() { dirty = true; }

    void Subscribe()
    {
        if (subscribedTo == meshGenerator) return;
        if (subscribedTo != null) subscribedTo.MeshesUpdated -= MarkDirty;
        subscribedTo = meshGenerator;
        if (subscribedTo != null) subscribedTo.MeshesUpdated += MarkDirty;
    }

    public void MarkDirty() { dirty = true; }

    void Update()
    {
        Subscribe();
        if (dirty) Rebuild();
    }

    void ReleaseAll()
    {
        vertexBuffer?.Release(); vertexBuffer = null;
        normalBuffer?.Release(); normalBuffer = null;
        reefBuffer?.Release(); reefBuffer = null;
        foreach (var layer in layers)
        {
            layer.instances?.Release(); layer.instances = null;
            layer.args?.Release(); layer.args = null;
            layer.instanceCount = 0;
            if (layer.runtimeMesh != null && layer.source != VegetationLayer.MeshSource.Asset)
                DestroyImmediate(layer.runtimeMesh);
            layer.runtimeMesh = null;
        }
    }

    // Collects the terrain triangles from all chunk meshes. Returns the triangle count.
    int UploadTerrain()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tmpV = new List<Vector3>();
        var tmpN = new List<Vector3>();
        bool hasBounds = false;

        foreach (var chunk in FindObjectsByType<Chunk>(FindObjectsInactive.Exclude))
        {
            var mf = chunk.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
            var mesh = mf.sharedMesh;
            mesh.GetVertices(tmpV);
            mesh.GetNormals(tmpN);
            if (tmpN.Count != tmpV.Count) continue;

            // The generator writes unindexed triangles (three consecutive vertices each).
            var toWorld = chunk.transform.localToWorldMatrix;
            for (int i = 0; i < tmpV.Count; i++)
            {
                verts.Add(toWorld.MultiplyPoint3x4(tmpV[i]));
                normals.Add(toWorld.MultiplyVector(tmpN[i]).normalized);
            }

            var renderer = chunk.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (!hasBounds) { drawBounds = renderer.bounds; hasBounds = true; }
                else drawBounds.Encapsulate(renderer.bounds);
            }
        }

        int count = verts.Count - verts.Count % 3;
        if (count == 0) return 0;
        drawBounds.Expand(20f);

        if (vertexBuffer == null || vertexBuffer.count != count)
        {
            vertexBuffer?.Release();
            normalBuffer?.Release();
            vertexBuffer = new ComputeBuffer(count, sizeof(float) * 3);
            normalBuffer = new ComputeBuffer(count, sizeof(float) * 3);
        }
        vertexBuffer.SetData(verts, 0, 0, count);
        normalBuffer.SetData(normals, 0, 0, count);
        return count / 3;
    }

    // Reef zones from the 2D map: cells marked as reef that are not solid terrain.
    Vector2Int UploadReefMask()
    {
        int w = 1, h = 1;
        float[] data = { 0f };
        if (mapGenerator != null && mapGenerator.reefMap != null && mapGenerator.densityMap != null)
        {
            w = mapGenerator.reefMap.GetLength(0);
            h = mapGenerator.reefMap.GetLength(1);
            data = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    data[y * w + x] = (mapGenerator.reefMap[x, y] == 1 && mapGenerator.densityMap[x, y] == 0) ? 1f : 0f;
        }
        if (reefBuffer == null || reefBuffer.count != data.Length)
        {
            reefBuffer?.Release();
            reefBuffer = new ComputeBuffer(data.Length, sizeof(float));
        }
        reefBuffer.SetData(data);
        return new Vector2Int(w, h);
    }

    static Mesh ResolveMesh(VegetationLayer layer)
    {
        switch (layer.source)
        {
            case VegetationLayer.MeshSource.GrassClump: return VegetationMeshes.GrassClump(seed: layer.seed);
            case VegetationLayer.MeshSource.Kelp: return VegetationMeshes.Kelp(seed: layer.seed);
            default: return layer.mesh;
        }
    }

    public void Rebuild()
    {
        dirty = false;
        if (scatterShader == null) return;

        int triangleCount = UploadTerrain();
        if (triangleCount == 0)
        {
            foreach (var layer in layers) layer.instanceCount = 0;
            return;
        }
        Vector2Int mapSize = UploadReefMask();
        float waterLevel = environment != null ? environment.WaterLevel : 1000f;

        int kernel = scatterShader.FindKernel("Scatter");
        scatterShader.SetBuffer(kernel, "_Vertices", vertexBuffer);
        scatterShader.SetBuffer(kernel, "_Normals", normalBuffer);
        scatterShader.SetBuffer(kernel, "_ReefMask", reefBuffer);
        scatterShader.SetInt("_TriangleCount", triangleCount);
        scatterShader.SetInts("_MapSize", mapSize.x, mapSize.y);
        // Map cell (0,0) sits at world -size/2, the same convention MapGenerator uses for its gizmos.
        scatterShader.SetVector("_MapOrigin", new Vector2(-mapSize.x / 2, -mapSize.y / 2));

        uint groupSize;
        scatterShader.GetKernelThreadGroupSizes(kernel, out groupSize, out _, out _);
        int groups = Mathf.CeilToInt(triangleCount / (float)groupSize);

        foreach (var layer in layers)
        {
            layer.instanceCount = 0;
            if (!layer.enabled || layer.material == null || layer.density <= 0f) continue;

            if (layer.runtimeMesh == null) layer.runtimeMesh = ResolveMesh(layer);
            var mesh = layer.runtimeMesh;
            if (mesh == null) continue;

            int capacity = Mathf.Max(1, layer.maxInstances);
            if (layer.instances == null || layer.instances.count != capacity)
            {
                layer.instances?.Release();
                layer.instances = new ComputeBuffer(capacity, InstanceStride, ComputeBufferType.Append);
            }
            if (layer.args == null)
                layer.args = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);

            layer.instances.SetCounterValue(0);
            scatterShader.SetBuffer(kernel, "_Output", layer.instances);
            scatterShader.SetFloat("_Density", layer.density);
            scatterShader.SetVector("_SlopeRange", layer.slopeRange);
            scatterShader.SetVector("_HeightRange", new Vector2(layer.heightRange.x, Mathf.Min(layer.heightRange.y, waterLevel - layer.surfaceClearance)));
            scatterShader.SetVector("_ScaleRange", layer.scaleRange);
            scatterShader.SetFloat("_NoiseScale", layer.patchFrequency);
            scatterShader.SetFloat("_NoiseThreshold", layer.patchThreshold);
            scatterShader.SetFloat("_PatchSeed", layer.patchSeed);
            scatterShader.SetFloat("_ReefMode", layer.reefMode);
            scatterShader.SetFloat("_AlignToNormal", layer.alignToNormal);
            scatterShader.SetFloat("_Seed", layer.seed);
            scatterShader.Dispatch(kernel, groups, 1, 1);

            var argData = new uint[] { mesh.GetIndexCount(0), 0, mesh.GetIndexStart(0), mesh.GetBaseVertex(0), 0 };
            layer.args.SetData(argData);
            ComputeBuffer.CopyCount(layer.instances, layer.args, sizeof(uint));

            // Move the mesh so its base centre is at the origin: pack meshes are laid out side by side.
            var b = mesh.bounds;
            var meshMatrix = Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            if (layer.props == null) layer.props = new MaterialPropertyBlock();
            layer.props.SetBuffer(InstancesId, layer.instances);
            layer.props.SetMatrix(MeshMatrixId, meshMatrix);
            layer.props.SetFloat(MeshHeightId, Mathf.Max(b.size.y, 0.001f));

            // Reading the count back stalls the GPU briefly; this only happens on a rebuild.
            layer.args.GetData(argData);
            layer.instanceCount = (int)Mathf.Min(argData[1], capacity);
        }
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview) return;
        foreach (var layer in layers)
        {
            if (!layer.enabled || layer.instanceCount <= 0 || layer.runtimeMesh == null || layer.material == null) continue;
            if (layer.args == null || layer.instances == null) continue;
            Graphics.DrawMeshInstancedIndirect(layer.runtimeMesh, 0, layer.material, drawBounds, layer.args, 0, layer.props,
                layer.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, gameObject.layer, cam);
        }
    }

    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        int total = 0;
        long tris = 0;
        foreach (var layer in layers)
        {
            total += layer.instanceCount;
            if (layer.runtimeMesh != null) tris += (long)layer.instanceCount * (layer.runtimeMesh.GetIndexCount(0) / 3);
            sb.Append(layer.name).Append('=').Append(layer.instanceCount).Append(layer.instanceCount >= layer.maxInstances ? "(full) " : " ");
        }
        return "instances=" + total + " triangles=" + tris + " | " + sb;
    }
}
