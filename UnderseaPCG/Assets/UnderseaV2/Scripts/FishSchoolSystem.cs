using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[System.Serializable]
public class FishSpecies
{
    public string name = "Fish";
    public int count = 200;
    [Tooltip("Number of separate groups the fish start in.")]
    public int schools = 2;
    public Vector2 sizeRange = new Vector2(0.8f, 1.2f);

    [Header("Behaviour")]
    public Vector2 speedRange = new Vector2(1f, 2f);
    public float neighbourRadius = 2f;
    public float separationRadius = 0.6f;
    public float cohesion = 1f;
    public float alignment = 1f;
    public float separation = 2f;
    public float turnRate = 3f;
    [Tooltip("How far below the water surface this species lives (shallowest, deepest), in metres.")]
    public Vector2 depthBand = new Vector2(2f, 14f);
    [Tooltip("Distance at which it bolts from predators. Limited by the neighbour grid cell size.")]
    public float fleeRadius = 2.5f;
    public bool isPredator = false;
    public float wander = 0.3f;

    [Header("Look")]
    [Tooltip("Body shape multipliers: thickness, height, length.")]
    public Vector3 bodyScale = Vector3.one;
    [ColorUsage(false)] public Color backColor = new Color(0.3f, 0.4f, 0.5f);
    [ColorUsage(false)] public Color bellyColor = new Color(0.85f, 0.88f, 0.9f);
    [ColorUsage(false)] public Color stripeColor = Color.black;
    public float stripeCount = 0f;
    [Range(0f, 1f)] public float stripeStrength = 0f;
    [Range(0f, 2f)] public float shine = 0.4f;
    [Range(0f, 0.5f)] public float tailAmplitude = 0.14f;
    public float tailFrequency = 9f;
}

// Simulates and draws every fish on the GPU. Neighbours are found with a uniform grid,
// so the cost grows with the number of fish instead of its square, and terrain is
// avoided by reading the density field that the terrain mesh was built from.
public class FishSchoolSystem : MonoBehaviour
{
    public ComputeShader simulation;
    public Mesh fishMesh;
    public Material fishMaterial;
    public MeshGenerator meshGenerator;
    public UnderwaterEnvironment environment;
    public List<FishSpecies> species = new List<FishSpecies>();
    [Tooltip("Objects with this tag scare nearby fish.")]
    public string threatTag = "Player";
    public float threatRadius = 3.5f;
    public bool castShadows = false;
    public int seed = 7;
    [Tooltip("Index of the species whose first fish the Follow Target tracks (for cameras).")]
    public int followSpecies = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct Fish
    {
        public Vector3 position;
        public float speed;
        public Vector3 direction;
        public float phase;
        public uint species;
        public float size;
        public Vector2 random;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SpeciesData
    {
        public float speedMin, speedMax, neighbourRadius, separationRadius;
        public float cohesion, alignment, separation, turnRate;
        public float yMin, yMax, fleeRadius, isPredator;
        public float tailFrequency, wander, pad0, pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LookData
    {
        public Vector3 bodyScale; public float tailAmplitude;
        public Vector3 backColor; public float stripeCount;
        public Vector3 bellyColor; public float stripeStrength;
        public Vector3 stripeColor; public float shine;
    }

    const int CellCapacity = 24;   // must match CELL_CAPACITY in FishSchool.compute
    const int MaxThreats = 4;
    const float CellSize = 3f;

    ComputeBuffer fishA, fishB, speciesBuffer, lookBuffer, gridCount, gridSlots, densityBuffer, args;
    MaterialPropertyBlock props;
    Bounds bounds;
    Vector3Int gridDim;
    int fishCount;
    bool hasDensity;
    bool initialised;
    int kClear, kBuild, kSimulate, kCopy;
    Transform[] threats = new Transform[0];
    float nextThreatScan;
    readonly Vector4[] threatData = new Vector4[MaxThreats];

    Transform followTarget;
    int followIndex;
    bool followPending;

    // A transform that tracks one fish, so a camera can follow the school.
    public Transform FollowTarget
    {
        get
        {
            if (followTarget == null)
            {
                followTarget = new GameObject("Fish Follow Target").transform;
                followTarget.SetParent(transform, false);
            }
            return followTarget;
        }
    }

    // Low-rate asynchronous copy of all fish, for gameplay code that needs to know where
    // schools and predators are (the mermaid). Nothing is read back until EnableSnapshot is called.
    public float snapshotInterval = 0.4f;
    Fish[] snapshot;
    bool snapshotWanted, snapshotPending;
    float nextSnapshot;

    public void EnableSnapshot() { snapshotWanted = true; }
    public bool HasSnapshot => snapshot != null;

    bool IsPredator(uint speciesIndex) => speciesIndex < species.Count && species[(int)speciesIndex].isPredator;

    public bool NearestPredator(Vector3 from, out Vector3 position, out float distance)
    {
        position = Vector3.zero; distance = float.MaxValue;
        if (snapshot == null) return false;
        float best = float.MaxValue;
        foreach (var f in snapshot)
        {
            if (!IsPredator(f.species)) continue;
            float d = (f.position - from).sqrMagnitude;
            if (d < best) { best = d; position = f.position; }
        }
        if (best == float.MaxValue) return false;
        distance = Mathf.Sqrt(best);
        return true;
    }

    // Centre of the densest group of prey fish within maxRange of a point.
    public bool FindSchool(Vector3 from, float maxRange, float radius, out Vector3 centre)
    {
        centre = Vector3.zero;
        if (snapshot == null) return false;
        float bestScore = 0f; Vector3 bestSeed = Vector3.zero;
        float r2 = radius * radius;
        for (int i = 0; i < snapshot.Length; i += 16)
        {
            var seed = snapshot[i];
            if (IsPredator(seed.species)) continue;
            float dist = Vector3.Distance(seed.position, from);
            if (dist > maxRange) continue;
            int count = 0;
            foreach (var f in snapshot)
                if (!IsPredator(f.species) && (f.position - seed.position).sqrMagnitude < r2) count++;
            float score = count / (1f + dist / maxRange);
            if (score > bestScore) { bestScore = score; bestSeed = seed.position; }
        }
        if (bestScore < 8f) return false;
        Vector3 heading;
        return TrackSchool(bestSeed, radius, out centre, out heading);
    }

    // Mean position and heading of the prey fish near a previously found centre.
    public bool TrackSchool(Vector3 lastCentre, float radius, out Vector3 centre, out Vector3 heading)
    {
        centre = lastCentre; heading = Vector3.forward;
        if (snapshot == null) return false;
        float r2 = radius * radius;
        Vector3 sum = Vector3.zero, dir = Vector3.zero; int n = 0;
        foreach (var f in snapshot)
        {
            if (IsPredator(f.species) || (f.position - lastCentre).sqrMagnitude > r2) continue;
            sum += f.position; dir += f.direction; n++;
        }
        if (n < 4) return false;
        centre = sum / n;
        if (dir.sqrMagnitude > 1e-6f) heading = dir.normalized;
        return true;
    }

    public int FishCount => fishCount;
    public bool HasTerrainField => hasDensity;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        if (meshGenerator != null) meshGenerator.MeshesUpdated += OnTerrainUpdated;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (meshGenerator != null) meshGenerator.MeshesUpdated -= OnTerrainUpdated;
        Release();
    }

    void Release()
    {
        foreach (var b in new[] { fishA, fishB, speciesBuffer, lookBuffer, gridCount, gridSlots, densityBuffer, args })
            b?.Release();
        fishA = fishB = speciesBuffer = lookBuffer = gridCount = gridSlots = densityBuffer = args = null;
        initialised = false;
        hasDensity = false;
        snapshot = null;
    }

    float WaterLevel => environment != null ? environment.WaterLevel : 11f;

    void Initialise()
    {
        if (simulation == null || fishMesh == null || fishMaterial == null || species.Count == 0) return;

        // Play area: the terrain volume, capped at the water surface.
        float size = meshGenerator != null ? meshGenerator.boundsSize : 64f;
        Vector3 chunks = meshGenerator != null ? (Vector3)meshGenerator.numChunks : Vector3.one;
        Vector3 extent = chunks * size;
        Vector3 min = -extent / 2f;
        Vector3 max = extent / 2f;
        max.y = WaterLevel;
        bounds = new Bounds((min + max) / 2f, max - min);
        gridDim = new Vector3Int(
            Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / CellSize)),
            Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / CellSize)),
            Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / CellSize)));

        var rng = new System.Random(seed);
        var fish = new List<Fish>();
        var speciesData = new SpeciesData[species.Count];
        var lookData = new LookData[species.Count];

        for (int s = 0; s < species.Count; s++)
        {
            var sp = species[s];
            float yMax = WaterLevel - sp.depthBand.x;
            float yMin = Mathf.Max(min.y + 2f, WaterLevel - sp.depthBand.y);
            speciesData[s] = new SpeciesData
            {
                speedMin = sp.speedRange.x, speedMax = sp.speedRange.y,
                neighbourRadius = Mathf.Min(sp.neighbourRadius, CellSize), separationRadius = sp.separationRadius,
                cohesion = sp.cohesion, alignment = sp.alignment, separation = sp.separation, turnRate = sp.turnRate,
                yMin = yMin, yMax = yMax, fleeRadius = Mathf.Min(sp.fleeRadius, CellSize), isPredator = sp.isPredator ? 1f : 0f,
                tailFrequency = sp.tailFrequency, wander = sp.wander
            };
            lookData[s] = new LookData
            {
                bodyScale = sp.bodyScale, tailAmplitude = sp.tailAmplitude,
                backColor = ToVector(sp.backColor), stripeCount = sp.stripeCount,
                bellyColor = ToVector(sp.bellyColor), stripeStrength = sp.stripeStrength,
                stripeColor = ToVector(sp.stripeColor), shine = sp.shine
            };

            int schools = Mathf.Max(1, sp.schools);
            for (int g = 0; g < schools; g++)
            {
                Vector3 centre = new Vector3(
                    Mathf.Lerp(min.x + 8f, max.x - 8f, (float)rng.NextDouble()),
                    Mathf.Lerp(yMin, yMax, 0.3f + 0.6f * (float)rng.NextDouble()),
                    Mathf.Lerp(min.z + 8f, max.z - 8f, (float)rng.NextDouble()));
                float heading = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
                int n = sp.count / schools + (g < sp.count % schools ? 1 : 0);
                float radius = Mathf.Max(1.5f, Mathf.Pow(n, 1f / 3f) * sp.separationRadius * 1.2f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 offset = new Vector3((float)rng.NextDouble() - 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.6f, (float)rng.NextDouble() - 0.5f) * 2f * radius;
                    fish.Add(new Fish
                    {
                        position = centre + offset,
                        speed = Mathf.Lerp(sp.speedRange.x, sp.speedRange.y, (float)rng.NextDouble()),
                        direction = dir,
                        phase = (float)rng.NextDouble() * 6.28f,
                        species = (uint)s,
                        size = Mathf.Lerp(sp.sizeRange.x, sp.sizeRange.y, (float)rng.NextDouble()),
                        random = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble())
                    });
                }
            }
        }

        fishCount = fish.Count;
        if (fishCount == 0) return;
        followIndex = 0;
        for (int i = 0; i < fish.Count; i++)
            if (fish[i].species == (uint)Mathf.Clamp(followSpecies, 0, species.Count - 1)) { followIndex = i; break; }

        int stride = Marshal.SizeOf(typeof(Fish));
        fishA = new ComputeBuffer(fishCount, stride);
        fishB = new ComputeBuffer(fishCount, stride);
        fishA.SetData(fish);
        fishB.SetData(fish);
        speciesBuffer = new ComputeBuffer(species.Count, Marshal.SizeOf(typeof(SpeciesData)));
        speciesBuffer.SetData(speciesData);
        lookBuffer = new ComputeBuffer(species.Count, Marshal.SizeOf(typeof(LookData)));
        lookBuffer.SetData(lookData);

        int cells = gridDim.x * gridDim.y * gridDim.z;
        gridCount = new ComputeBuffer(cells, sizeof(uint));
        gridSlots = new ComputeBuffer(cells * CellCapacity, sizeof(uint));

        args = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { fishMesh.GetIndexCount(0), (uint)fishCount, fishMesh.GetIndexStart(0), fishMesh.GetBaseVertex(0), 0 });

        props = new MaterialPropertyBlock();
        props.SetBuffer("_Looks", lookBuffer);
        props.SetVector("_MeshCenter", fishMesh.bounds.center);
        props.SetVector("_MeshHalfSize", fishMesh.bounds.extents);

        kClear = simulation.FindKernel("ClearGrid");
        kBuild = simulation.FindKernel("BuildGrid");
        kSimulate = simulation.FindKernel("Simulate");
        kCopy = simulation.FindKernel("CopyDensity");
        initialised = true;

        CopyTerrainField();
    }

    static Vector3 ToVector(Color c)
    {
        c = c.linear;
        return new Vector3(c.r, c.g, c.b);
    }

    void OnTerrainUpdated()
    {
        if (initialised) CopyTerrainField();
    }

    // Takes a private copy of the terrain density values so fish can steer around rock.
    void CopyTerrainField()
    {
        hasDensity = false;
        if (!Application.isPlaying || meshGenerator == null) return;
        var points = meshGenerator.PointsBuffer;
        // The generator reuses one buffer per chunk, so this only describes the whole
        // terrain when the map is a single chunk.
        if (points == null || !points.IsValid() || meshGenerator.numChunks != Vector3Int.one) return;

        int n = meshGenerator.numPointsPerAxis;
        if (points.count != n * n * n) return;
        if (densityBuffer == null || densityBuffer.count != points.count)
        {
            densityBuffer?.Release();
            densityBuffer = new ComputeBuffer(points.count, sizeof(float));
        }
        simulation.SetInt("_DensitySize", n);
        simulation.SetBuffer(kCopy, "_TerrainPoints", points);
        simulation.SetBuffer(kCopy, "_DensityOut", densityBuffer);
        simulation.Dispatch(kCopy, Mathf.CeilToInt(points.count / 64f), 1, 1);

        float size = meshGenerator.boundsSize;
        simulation.SetVector("_DensityOrigin", -Vector3.one * size / 2f);
        simulation.SetFloat("_DensitySpacing", size / (n - 1));
        simulation.SetFloat("_IsoLevel", meshGenerator.isoLevel);
        hasDensity = true;
    }

    void ScanThreats()
    {
        nextThreatScan = Time.time + 1f;
        if (string.IsNullOrEmpty(threatTag)) { threats = new Transform[0]; return; }
        GameObject[] found;
        try { found = GameObject.FindGameObjectsWithTag(threatTag); }
        catch (UnityException) { found = new GameObject[0]; }
        threats = new Transform[Mathf.Min(found.Length, MaxThreats)];
        for (int i = 0; i < threats.Length; i++) threats[i] = found[i].transform;
    }

    void Update()
    {
        if (!initialised)
        {
            Initialise();
            if (!initialised) return;
        }
        if (Time.time >= nextThreatScan) ScanThreats();
        for (int i = 0; i < MaxThreats; i++)
        {
            bool live = i < threats.Length && threats[i] != null;
            threatData[i] = live ? new Vector4(threats[i].position.x, threats[i].position.y, threats[i].position.z, threatRadius) : Vector4.zero;
        }

        simulation.SetInt("_FishCount", fishCount);
        simulation.SetFloat("_DeltaTime", Mathf.Min(Time.deltaTime, 0.05f));
        simulation.SetFloat("_SimTime", Time.time);
        simulation.SetVector("_BoundsMin", bounds.min);
        simulation.SetVector("_BoundsMax", bounds.max);
        simulation.SetFloat("_CellSize", CellSize);
        simulation.SetInts("_GridDim", gridDim.x, gridDim.y, gridDim.z);
        simulation.SetFloat("_WaterLevel", WaterLevel);
        simulation.SetVectorArray("_Threats", threatData);
        simulation.SetFloat("_HasDensity", hasDensity ? 1f : 0f);

        int cells = gridDim.x * gridDim.y * gridDim.z;
        simulation.SetBuffer(kClear, "_GridCount", gridCount);
        simulation.Dispatch(kClear, Mathf.CeilToInt(cells / 64f), 1, 1);

        int fishGroups = Mathf.CeilToInt(fishCount / 64f);
        simulation.SetBuffer(kBuild, "_FishIn", fishA);
        simulation.SetBuffer(kBuild, "_GridCount", gridCount);
        simulation.SetBuffer(kBuild, "_GridSlots", gridSlots);
        simulation.Dispatch(kBuild, fishGroups, 1, 1);

        simulation.SetBuffer(kSimulate, "_FishIn", fishA);
        simulation.SetBuffer(kSimulate, "_FishOut", fishB);
        simulation.SetBuffer(kSimulate, "_Species", speciesBuffer);
        simulation.SetBuffer(kSimulate, "_GridCount", gridCount);
        simulation.SetBuffer(kSimulate, "_GridSlots", gridSlots);
        if (hasDensity) simulation.SetBuffer(kSimulate, "_Density", densityBuffer);
        else simulation.SetBuffer(kSimulate, "_Density", gridCount); // unused when _HasDensity is 0, but must be bound
        simulation.Dispatch(kSimulate, fishGroups, 1, 1);

        // The freshly written buffer becomes the one that is read and drawn.
        var tmp = fishA; fishA = fishB; fishB = tmp;
        props.SetBuffer("_Fish", fishA);

        // Track one fish without stalling the GPU: the position arrives a frame or two late.
        if (followTarget != null && !followPending)
        {
            followPending = true;
            int stride = Marshal.SizeOf(typeof(Fish));
            AsyncGPUReadback.Request(fishA, stride, followIndex * stride, request =>
            {
                followPending = false;
                if (request.hasError || followTarget == null) return;
                var f = request.GetData<Fish>()[0];
                followTarget.position = f.position;
                if (f.direction.sqrMagnitude > 1e-6f) followTarget.rotation = Quaternion.LookRotation(f.direction, Vector3.up);
            });
        }

        if (snapshotWanted && !snapshotPending && Time.time >= nextSnapshot)
        {
            snapshotPending = true;
            nextSnapshot = Time.time + snapshotInterval;
            AsyncGPUReadback.Request(fishA, request =>
            {
                snapshotPending = false;
                if (request.hasError || !initialised) return;
                var data = request.GetData<Fish>();
                if (snapshot == null || snapshot.Length != data.Length) snapshot = new Fish[data.Length];
                data.CopyTo(snapshot);
            });
        }
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (!initialised || cam.cameraType == CameraType.Preview) return;
        Graphics.DrawMeshInstancedIndirect(fishMesh, 0, fishMaterial, bounds, args, 0, props,
            castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, gameObject.layer, cam);
    }

    // Reads all fish back from the GPU. Slow; for diagnostics only.
    public string Diagnose()
    {
        if (!initialised) return "not initialised";
        var data = new Fish[fishCount];
        fishA.GetData(data);
        var per = new int[species.Count];
        var speed = new float[species.Count];
        var meanY = new float[species.Count];
        var aboveBand = new int[species.Count];
        var pitch = new float[species.Count];
        Vector3 lo = data[0].position, hi = data[0].position;
        int invalid = 0;
        foreach (var f in data)
        {
            if (float.IsNaN(f.position.x) || float.IsNaN(f.direction.x)) { invalid++; continue; }
            lo = Vector3.Min(lo, f.position);
            hi = Vector3.Max(hi, f.position);
            per[f.species]++;
            speed[f.species] += f.speed;
            meanY[f.species] += f.position.y;
            pitch[f.species] += f.direction.y;
            if (f.position.y > WaterLevel - species[(int)f.species].depthBand.x + 0.5f) aboveBand[f.species]++;
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("fish=").Append(fishCount).Append(" invalid=").Append(invalid).Append(" terrainField=").Append(hasDensity)
          .Append(" min=").Append(lo.ToString("F1")).Append(" max=").Append(hi.ToString("F1")).Append(" |");
        for (int s = 0; s < species.Count; s++)
            sb.Append(' ').Append(species[s].name).Append('=').Append(per[s]).Append("@").Append((speed[s] / Mathf.Max(1, per[s])).ToString("F1")).Append("m/s")
              .Append(" y=").Append((meanY[s] / Mathf.Max(1, per[s])).ToString("F1"))
              .Append(" pitch=").Append((pitch[s] / Mathf.Max(1, per[s])).ToString("F2"))
              .Append(" above=").Append(aboveBand[s]).Append(';');
        return sb.ToString();
    }

    // Position and heading of the n-th fish of a species. Slow; for diagnostics only.
    public bool SampleFish(int speciesIndex, int n, out Vector3 position, out Vector3 direction, out float size)
    {
        position = Vector3.zero; direction = Vector3.forward; size = 1f;
        if (!initialised) return false;
        var data = new Fish[fishCount];
        fishA.GetData(data);
        int seen = 0;
        foreach (var f in data)
        {
            if (f.species != speciesIndex) continue;
            if (seen++ < n) continue;
            position = f.position; direction = f.direction; size = f.size;
            return true;
        }
        return false;
    }

    public Vector3 SpeciesCentre(int index)
    {
        if (!initialised) return Vector3.zero;
        var data = new Fish[fishCount];
        fishA.GetData(data);
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var f in data) if (f.species == index) { sum += f.position; n++; }
        return n > 0 ? sum / n : Vector3.zero;
    }
}
