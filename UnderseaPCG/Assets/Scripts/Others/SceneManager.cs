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
   

    private static SceneManager _instance;

    // Singleton
    public static SceneManager Instance
    {
        get
        {
            if (_instance == null)
            {
                //Find Scene Manager
                _instance = FindObjectOfType<SceneManager>();

                if (_instance == null)
                {
                    // Create Singleton Object
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
        //Generate Scene Asynchronously
        StartCoroutine(AsycGenerateScene());
       
    }

    public IEnumerator AsycGenerateScene(){
        WaitForSeconds wait = new WaitForSeconds(0.1f);
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
        base.OnInspectorGUI(); 

        SceneManager script = (SceneManager)target;

        if (GUILayout.Button("Generate Scene"))
        {
            script.GenerateScene();
        }

        if (GUILayout.Button("Generate Vegetaion"))
        {
            script.decorationGenerator.GenerateDecorations(script.mapGenerator.getDecorationMap());
        }
    }


}