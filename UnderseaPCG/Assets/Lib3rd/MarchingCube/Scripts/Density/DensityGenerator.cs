using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class DensityGenerator : MonoBehaviour {

    const int threadGroupSize = 8;
    public ComputeShader densityShader;
    public ComputeShader cellularShader;

    protected List<ComputeBuffer> buffersToRelease;

    void OnValidate() {
        if (FindObjectOfType<MeshGenerator>()) {
            FindObjectOfType<MeshGenerator>().RequestMeshUpdate();
        }
    }

    public virtual ComputeBuffer Generate (ComputeBuffer pointsBuffer, int numPointsPerAxis, float boundsSize, Vector3 worldBounds, Vector3 centre, Vector3 offset, float spacing) {
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
        int numThreadsPerAxis = Mathf.CeilToInt (numPointsPerAxis / (float) threadGroupSize);
        // Points buffer is populated inside shader with pos (xyz) + density (w).
        // Set paramaters
        densityShader.SetBuffer (0, "points", pointsBuffer);
        densityShader.SetInt ("numPointsPerAxis", numPointsPerAxis);
        densityShader.SetFloat ("boundsSize", boundsSize);
        densityShader.SetVector ("centre", new Vector4 (centre.x, centre.y, centre.z));
        densityShader.SetVector ("offset", new Vector4 (offset.x, offset.y, offset.z));
        densityShader.SetFloat ("spacing", spacing);
        densityShader.SetVector("worldSize", worldBounds);

        // Dispatch shader
        densityShader.Dispatch (0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        if (buffersToRelease != null) {
            foreach (var b in buffersToRelease) {
                b.Release();
            }
        }

        // Return voxel data buffer so it can be used to generate mesh
        return pointsBuffer;
    }

    public virtual ComputeBuffer ModifyWithMask (ComputeBuffer pointsBuffer, ComputeBuffer maskBuffer, int numPointsPerAxis) {
        int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
        int numThreadsPerAxis = Mathf.CeilToInt (numPointsPerAxis / (float) threadGroupSize);
        // Points buffer is populated inside shader with pos (xyz) + density (w).
        // Set paramaters
        cellularShader.SetBuffer (1, "points", pointsBuffer);
        cellularShader.SetBuffer (1, "maskPoints", maskBuffer);
        cellularShader.SetInt ("numPointsPerAxis", numPointsPerAxis);

        // Dispatch shader
        cellularShader.Dispatch (1, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        if (buffersToRelease != null) {
            foreach (var b in buffersToRelease) {
                b.Release();
            }
        }

        // Return voxel data buffer so it can be used to generate mesh
        return pointsBuffer;
    }

        public virtual ComputeBuffer CellularAutma (ComputeBuffer pointsBuffer, int numPointsPerAxis) {
            if(cellularShader == null) {
                return null;
            }
            int numPoints = numPointsPerAxis * numPointsPerAxis * numPointsPerAxis;
            int numThreadsPerAxis = Mathf.CeilToInt (numPointsPerAxis / (float) threadGroupSize);
            // Points buffer is populated inside shader with pos (xyz) + density (w).
            // Set paramaters
            cellularShader.SetBuffer (0, "points", pointsBuffer);
            cellularShader.SetInt ("numPointsPerAxis", numPointsPerAxis);
   

        // Dispatch shader
        cellularShader.Dispatch (0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        if (buffersToRelease != null) {
            foreach (var b in buffersToRelease) {
                b.Release();
            }
        }

        // Return voxel data buffer so it can be used to generate mesh
        return pointsBuffer;
    }
}