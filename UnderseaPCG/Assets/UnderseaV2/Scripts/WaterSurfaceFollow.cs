using UnityEngine;
using UnityEngine.Rendering;

// Keeps a large water plane centred under/over whichever camera is rendering, so the
// surface reaches the horizon without needing an enormous mesh. The shader uses
// world-space coordinates, so the waves do not slide when the plane moves.
[ExecuteAlways]
public class WaterSurfaceFollow : MonoBehaviour
{
    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        Vector3 c = cam.transform.position;
        transform.position = new Vector3(c.x, transform.position.y, c.z);
    }
}
