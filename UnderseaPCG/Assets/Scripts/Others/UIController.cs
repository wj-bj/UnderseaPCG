using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public Button AutoButton;
    public Button MapButton;
    public Image MapImage;

    public Button GenerateButton;
    public Slider DensityScrollbar;
    public Image pos2DImage;
    bool isManual = false;
    bool isMap = false;
    Texture2D texture;
    
    // Start is called before the first frame update
    void Start()
    {
        AutoButton.onClick.AddListener(onClickAutoButton);
        MapButton.onClick.AddListener(onClickMapButton);
        DensityScrollbar.onValueChanged.AddListener(UpdateDensity);
        GenerateButton.onClick.AddListener(OnClickGenerateButton);

        MapImage.gameObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if(isMap)
            UpdateLocationOnMap();
    }

    void onClickAutoButton()
    {
        isManual = !isManual;
        if(isManual)
        {
            AutoButton.GetComponentInChildren<TMP_Text>().text = "Manual";
            GameObject.FindAnyObjectByType<DiverMoveUnit>().steerType = DiverMoveUnit.SteerType.Manul;
        }
        else
        {
            AutoButton.GetComponentInChildren<TMP_Text>().text = "Auto";
            GameObject.FindAnyObjectByType<DiverMoveUnit>().steerType = DiverMoveUnit.SteerType.Default;
        }
    }

    void onClickMapButton()
    {
        isMap = !isMap;
        if(isMap)
        {
            var dmap =SceneManager.Instance.mapGenerator.densityMap;
            var rmap = SceneManager.Instance.mapGenerator.reefMap;
            int width = SceneManager.Instance.mapGenerator.width;
            int height = SceneManager.Instance.mapGenerator.height;
            texture = new Texture2D(width, height);
            UpdateMapTexture(dmap, rmap, texture);
            MapImage.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.zero);
            MapImage.gameObject.SetActive(true);
            var densityFill = SceneManager.Instance.mapGenerator.densityFill;
            DensityScrollbar.value = densityFill/100f;

		}
        else{
            MapImage.gameObject.SetActive(false);
        }
            
       
    }

    void UpdateMapTexture(int[,] dmap, int[,] rmap, Texture2D texture)
    {
        
        int width = dmap.GetLength(0);
        int height = dmap.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Color color = Color.white;
                if(dmap[x,y] == 0 && rmap[x,y] == 1)
						color = new Color(0.5f,0.6f,0,1);
				else if(dmap[x,y] == 1)
						color = Color.black;
				else
						color = Color.white;
            
                 texture.SetPixel(x, y, color);
              
            }
        }
        texture.Apply();

    }

    void UpdateDensity(float value)
    {
        SceneManager.Instance.mapGenerator.densityFill = (int)(DensityScrollbar.value*100);
        SceneManager.Instance.UpdateScene();
        var dmap =SceneManager.Instance.mapGenerator.densityMap;
        var rmap = SceneManager.Instance.mapGenerator.reefMap;
        int width = SceneManager.Instance.mapGenerator.width;
        int height = SceneManager.Instance.mapGenerator.height;
        UpdateMapTexture(dmap, rmap, texture);
        MapImage.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.zero);

        
    }

    void OnClickGenerateButton()
    {
        
        SceneManager.Instance.GenerateScene();
    }

    void UpdateLocationOnMap()
    {
        var cam = Camera.main.gameObject;
        var pos = cam.transform.position;
        int width = SceneManager.Instance.mapGenerator.width;
        int height = SceneManager.Instance.mapGenerator.height;
        Vector3 rangeXY = new Vector2(width/2, height/2);
        // Vector3 pos = new Vector3(-width/2 + x + .5f,0, -height/2 + y+.5f);
        Vector2 pos2D = new Vector2(pos.x/rangeXY.x, pos.z/rangeXY.y);
        pos2DImage.rectTransform.anchoredPosition = pos2D*100;

    }
}
