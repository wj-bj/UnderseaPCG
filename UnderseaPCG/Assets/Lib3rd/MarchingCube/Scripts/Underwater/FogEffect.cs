using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode,ImageEffectAllowedInSceneView]
public class FogEffect : MonoBehaviour
{
    public Material _mat;
    public Color _fogColor;
    // public float _depthStart;
    // [Range(0f,1f)]
    // public float _depthDistance;
    [MinMaxSlider(0, 1f)]
    public Vector2 _depthStartEnd;
    
    
    // Start is called before the first frame update
    void Start()
    {
        GetComponent<Camera>().depthTextureMode = DepthTextureMode.Depth;
    }

    // Update is called once per frame
    void Update()
    {
        _mat.SetColor("_FogColor", _fogColor);
        _mat.SetFloat("_DepthStart", _depthStartEnd.x);
        _mat.SetFloat("_DepthDistance", _depthStartEnd.y);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination){
        Graphics.Blit(source,destination,_mat);
    }

 
}
