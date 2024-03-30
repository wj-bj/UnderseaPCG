using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class SceneManager : MonoBehaviour
{
    public MapGenerator mapGenerator;
    public MeshGenerator meshGenerator;
    public DecorationGenerator decorationGenerator;
    public AgentGenerator agentGenerator;
    // Start is called before the first frame update

    private static SceneManager _instance;

    public static SceneManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // 查找现有的实例
                _instance = FindObjectOfType<SceneManager>();

                if (_instance == null)
                {
                    // 创建新的GameObject，并添加Singleton组件
                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<SceneManager>();
                    singletonObject.name = " SceneManager";
                }
            }

            return _instance;
        }
    }

    void Start()
    {
        GenerateScene();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void UpdateScene(){
        if(mapGenerator != null){
            mapGenerator.GenerateMap();
        }

        int[,] map = mapGenerator.densityMap;
        if(meshGenerator != null){
            meshGenerator.generateMaskPoints(map);
            meshGenerator.Run();
            meshGenerator.UpdateCollider();
        }
        if(decorationGenerator != null){
            var decmap= mapGenerator.getDecorationMap();
            
            decorationGenerator.GenerateDecorations(decmap);
        }

    }

    public void GenerateScene(){
        
        StartCoroutine(AsycGenerateScene());
       
    }

    public IEnumerator AsycGenerateScene(){
        WaitForSeconds wait = new WaitForSeconds(0.2f);
        if(mapGenerator != null){
            mapGenerator.GenerateMap();
            yield return null;
        }

        int[,] map = mapGenerator.densityMap;
        if(meshGenerator != null){
            meshGenerator.generateMaskPoints(map);
            meshGenerator.Run();
            yield return wait;
            meshGenerator.UpdateCollider();
        }

        if(decorationGenerator != null){
            var decmap= mapGenerator.getDecorationMap();
            
            decorationGenerator.GenerateDecorations(decmap);
        }

        if(agentGenerator != null){
            agentGenerator.GenerateAgents(map);
        }

        yield return null;
    }
}




[CustomEditor(typeof(SceneManager))]
public class SceneManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI(); // 显示默认的属性

        SceneManager script = (SceneManager)target;

        if (GUILayout.Button("点击我"))
        {
            script.GenerateScene();
        }

        if (GUILayout.Button("植被"))
        {
            script.decorationGenerator.GenerateDecorations(script.mapGenerator.getDecorationMap());
        }
    }


}