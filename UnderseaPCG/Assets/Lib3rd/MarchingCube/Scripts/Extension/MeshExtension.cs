using UnityEngine;

public static class MeshExtensions
{
    public static void RecalculateSmoothNormals(this Mesh mesh, float angleThreshold = 0.0f)
    {
        var vertices = mesh.vertices;
        var normals = new Vector3[vertices.Length];
        
        var triangleCount = mesh.triangles.Length / 3;
        for (int i = 0; i < triangleCount; i++)
        {
            // 获取三角形的顶点索引
            int index0 = mesh.triangles[i * 3];
            int index1 = mesh.triangles[i * 3 + 1];
            int index2 = mesh.triangles[i * 3 + 2];

            // 计算面法线
            Vector3 faceNormal = Vector3.Cross(vertices[index1] - vertices[index0], vertices[index2] - vertices[index0]).normalized;

            // 将面法线加到顶点法线上
            normals[index0] += faceNormal;
            normals[index1] += faceNormal;
            normals[index2] += faceNormal;
        }

        // 归一化顶点法线
        for (int i = 0; i < normals.Length; i++)
            normals[i].Normalize();

        // 应用计算后的法线
        mesh.normals = normals;
    }
}