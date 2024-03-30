using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode,ImageEffectAllowedInSceneView]
public class FogEffectCommandBuffer : MonoBehaviour
{
    public Material _mat;
    public Color _fogColor;
    // public float _depthStart;
    // [Range(0f,1f)]
    // public float _depthDistance;
    [MinMaxSlider(0, 1f)]
    public Vector2 _depthStartEnd;
    

    private CommandBuffer commandBuffer = null;

    
    
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

    
    void OnDisable()

    {

        Camera.main.RemoveCommandBuffer(CameraEvent.AfterSkybox, commandBuffer);
        commandBuffer.Clear();

    }
    
    void OnEnable()
    {
        
        //申请RT
      
        commandBuffer = new CommandBuffer();
        commandBuffer.name = "FogEffectCommand";
        //设置Command Buffer渲染目标为申请的RT
        // commandBuffer.SetRenderTarget(renderTexture);
        // //初始颜色设置为灰色
        // commandBuffer.ClearRenderTarget(true, true, Color.gray);
        int grabScreenID = Shader.PropertyToID("_ScreenCopyTexture");
        int fogTextureID = Shader.PropertyToID("_FogTexture");
        
        if (_mat)
        {
            
            commandBuffer.GetTemporaryRT(fogTextureID,-1,-1,0,FilterMode.Bilinear);
            commandBuffer.GetTemporaryRT(grabScreenID,-1,-1,0,FilterMode.Bilinear);
            commandBuffer.Blit(BuiltinRenderTextureType.CurrentActive,grabScreenID);
            commandBuffer.Blit(grabScreenID,fogTextureID,_mat);
            commandBuffer.Blit(fogTextureID,BuiltinRenderTextureType.CameraTarget);
           // commandBuffer.SetRenderTarget(fogTextureID);
         
            commandBuffer.ReleaseTemporaryRT(grabScreenID);
            commandBuffer.ReleaseTemporaryRT(fogTextureID);
        }
        //直接加入相机的CommandBuffer事件队列中
        Camera.main.AddCommandBuffer(CameraEvent.AfterSkybox, commandBuffer);
    }
}
