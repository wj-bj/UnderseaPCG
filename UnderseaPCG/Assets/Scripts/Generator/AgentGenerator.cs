using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class AgentGenerator : MonoBehaviour
{
    public GameObject[] agents;

   

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnApplicationQuit()
    {
        // ClearAgents();
        Debug.Log("Quit");
    }


    public void ClearAgents()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

    }

    public void GenerateAgents(int[,] map)
    {
        ClearAgents();

        if (map != null) {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
            int countAngent = agents!=null?agents.Length:0;
            
            while(countAngent>0){
                int randomIndex = Random.Range(0, width * height);
                int gridX = randomIndex % width;
                int gridY = randomIndex / height;
                int count = 0;
                if (map[gridX, gridY] == 0) {
                    Vector3 pos = new Vector3(-width/2 + gridX + .5f,0, -height/2 + gridY+.5f);
                    GameObject agentObj = Instantiate(agents[countAngent-1], transform);
                    agentObj.transform.position = pos;
                    agentObj.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                    float scale = 1f;
                    agentObj.transform.localScale = new Vector3(scale, scale, scale);
                     //raycast to find the ground
                    RaycastHit hit;
                    //add noise to the position
                    pos += new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                    agentObj.GetComponent<AgentController>().InitializeAgent(map);   
                    // if (Physics.Raycast(new Vector3(pos.x, 20, pos.z), Vector3.down, out hit, 200))
                    // {
                    //         pos = hit.point;
                                    
                    // }
                  
                    countAngent--;
                   
                }
                else if(count>20){
                    continue;
                }
                
            }

		}
    }
}
