using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;
using NPBehave;

public class AgentController : MonoBehaviour
{

    public enum AgentType
    {
        fish,
        mermaid,
        driver,
        loot,
    }

    public AgentType agentType;
  

    int[,] map;
    // Start is called before the first frame update

    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

  

    public void InitializeAgent(int[,] map)
    {
        this.map = map;
        switch (agentType)
        {
            case AgentType.fish:
                InitializeFish();
                break;
            case AgentType.mermaid:
                InitializeMermaid();
                break;
            case AgentType.driver:
                InitializeDriver();
                break;
            case AgentType.loot:
                InitializeLoot();
                break;
            default:
                break;
        }
    }

    void InitializeFish()
    {
        var fishScript = GetComponentInChildren<FishMoveUnit>();
        if(fishScript != null)
        {
             int width = map.GetLength(0);
            int height = map.GetLength(1);
            int count = 3;
            Vector3 [] vecPaths= new Vector3[count];
           
            for (int i = 0; i < count; i++)
            {
                 int randomIndex = UnityEngine.Random.Range(0, width * height);
                int gridX = randomIndex % width;
                int gridY = randomIndex / height;
                    
                Vector3 pos = new Vector3(-width/2 + gridX + .5f,0, -height/2 + gridY+.5f);

                vecPaths[i] = pos;
            }
            
            LinePath path = new LinePath(vecPaths);
            fishScript.path = path;
        }
    }

    void InitializeDriver()
    {
         var script = GetComponentInChildren<DiverMoveUnit>();
        if(script != null)
        {
             int width = map.GetLength(0);
            int height = map.GetLength(1);
            int count = 3;
            Vector3 [] vecPaths= new Vector3[count];
           
            for (int i = 0; i < count; i++)
            {
                 int randomIndex = UnityEngine.Random.Range(0, width * height);
                int gridX = randomIndex % width;
                int gridY = randomIndex / height;
                    
                Vector3 pos = new Vector3(-width/2 + gridX + .5f,5f, -height/2 + gridY+.5f);

                vecPaths[i] = pos;
            }
            
            LinePath path = new LinePath(vecPaths);
            script.path = path;
            script.steerType = DiverMoveUnit.SteerType.Default;
        }

        Camera.main.GetComponent<CameraController>().target = this.transform;
    }

    void InitializeMermaid()
    {

    }

    void InitializeLoot()
    {
        var script = GetComponentInChildren<LootMoveUnit>();
        if(script != null)
        {
             int width = map.GetLength(0);
            int height = map.GetLength(1);
            int count = 5;
            Vector3 [] vecPaths= new Vector3[count];
            List<int> availableIndex = new List<int>();
            for (int i = 0; i < width * height; i++)
            {
                if(map[i % width, i / height] == 0)
                    availableIndex.Add(i);
            }
           
            for (int i = 0; i < count; i++)
            {
                
                int randomIndex = UnityEngine.Random.Range(0, availableIndex.Count);
                randomIndex = availableIndex[randomIndex];
                int gridX = randomIndex % width;
                int gridY = randomIndex / height;
                    
                Vector3 pos = new Vector3(-width/2 + gridX + .5f,0, -height/2 + gridY+.5f);

                vecPaths[i] = pos;
            }
            
            script.desPoints = vecPaths;
        }
    }

  
}
