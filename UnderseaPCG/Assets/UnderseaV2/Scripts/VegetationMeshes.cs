using System.Collections.Generic;
using UnityEngine;

// Procedural plant meshes. All are 1 unit tall with the base at the origin, and store the
// height fraction in uv.y so the shader can tint and bend them from root to tip.
public static class VegetationMeshes
{
    // A tuft of narrow blades leaning outward from a common root.
    public static Mesh GrassClump(int blades = 7, int segments = 4, float bladeWidth = 0.07f, int seed = 1)
    {
        var rng = new System.Random(seed);
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        for (int b = 0; b < blades; b++)
        {
            float angle = (b + (float)rng.NextDouble() * 0.8f) / blades * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 side = new Vector3(-outward.z, 0f, outward.x);
            float lean = 0.15f + (float)rng.NextDouble() * 0.35f;
            float height = 0.6f + (float)rng.NextDouble() * 0.4f;
            Vector3 root = outward * (0.03f + (float)rng.NextDouble() * 0.08f);
            AddRibbon(verts, normals, uvs, tris, root, outward, side, height, lean, bladeWidth, segments, 0f, true);
        }
        return Build("GrassClump", verts, normals, uvs, tris);
    }

    // A tall stalk made of two crossed ribbons with leaf blades growing off it.
    public static Mesh Kelp(int segments = 12, float stalkWidth = 0.035f, int leaves = 9, int seed = 3)
    {
        var rng = new System.Random(seed);
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        AddRibbon(verts, normals, uvs, tris, Vector3.zero, Vector3.forward, Vector3.right, 1f, 0f, stalkWidth, segments, 0f, false);
        AddRibbon(verts, normals, uvs, tris, Vector3.zero, Vector3.right, Vector3.forward, 1f, 0f, stalkWidth, segments, 0f, false);

        for (int l = 0; l < leaves; l++)
        {
            float t = 0.12f + 0.85f * (l + (float)rng.NextDouble() * 0.6f) / leaves;
            float angle = l * 2.4f + (float)rng.NextDouble();
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 side = new Vector3(-outward.z, 0f, outward.x);
            float length = 0.16f + (float)rng.NextDouble() * 0.1f;
            AddLeaf(verts, normals, uvs, tris, new Vector3(0f, t, 0f), outward, side, length, 0.05f, t);
        }
        return Build("Kelp", verts, normals, uvs, tris);
    }

    // Vertical strip from 'root', curving toward 'outward' by 'lean' at the tip.
    static void AddRibbon(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
        Vector3 root, Vector3 outward, Vector3 side, float height, float lean, float width, int segments, float uvOffset, bool taper)
    {
        int start = verts.Count;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 centre = root + Vector3.up * (t * height) + outward * (lean * t * t);
            float w = width * 0.5f * (taper ? Mathf.Lerp(1f, 0.15f, t * t) : 1f);
            Vector3 tangent = (Vector3.up * height + outward * (2f * lean * t)).normalized;
            Vector3 normal = Vector3.Cross(side, tangent).normalized;
            verts.Add(centre - side * w);
            verts.Add(centre + side * w);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(0f, t * height + uvOffset));
            uvs.Add(new Vector2(1f, t * height + uvOffset));
        }
        for (int i = 0; i < segments; i++)
        {
            int a = start + i * 2;
            tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
            tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
        }
    }

    // Pointed leaf growing sideways and slightly upward from a point on the stalk.
    static void AddLeaf(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
        Vector3 origin, Vector3 outward, Vector3 side, float length, float width, float heightUv)
    {
        int start = verts.Count;
        Vector3 dir = (outward + Vector3.up * 0.6f).normalized;
        Vector3 normal = Vector3.Cross(side, dir).normalized;
        Vector3 mid = origin + dir * (length * 0.45f);
        Vector3 tip = origin + dir * length;
        verts.Add(origin); verts.Add(mid - side * width); verts.Add(mid + side * width); verts.Add(tip);
        for (int i = 0; i < 4; i++) normals.Add(normal);
        // All leaf vertices share the stalk height so the whole leaf sways with its attachment point.
        uvs.Add(new Vector2(0.5f, heightUv)); uvs.Add(new Vector2(0f, heightUv));
        uvs.Add(new Vector2(1f, heightUv)); uvs.Add(new Vector2(0.5f, heightUv));
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
        tris.Add(start + 1); tris.Add(start + 3); tris.Add(start + 2);
    }

    static Mesh Build(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
