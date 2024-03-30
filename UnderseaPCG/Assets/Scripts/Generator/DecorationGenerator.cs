using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class DecorationGenerator : MonoBehaviour
{
    public GameObject[] decorations;
    public Vector2 scaleRange = new Vector2(2, 5);

    [Range(1, 3)]
    public int denstiy =1;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void ClearDecorations()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    public void GenerateDecorations(int[,] map)
    {
        ClearDecorations();

        if (map != null) {
            int width = map.GetLength(0);
            int height = map.GetLength(1);
			for (int x = 0; x < width; x ++) {
				for (int y = 0; y < height; y ++) {
				    if(map[x,y] == 1){
                        Vector3 pos = new Vector3(-width/2 + x + .5f,0, -height/2 + y+.5f);
					    for(int i = 0; i < denstiy; i++){
                            if(decorations.Length > 0){
                                GameObject newDecoration = Instantiate(decorations[Random.Range(0,decorations.Length)], transform);
                                // newDecoration.transform.position = pos;
                                newDecoration.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                                float scale = Random.Range(scaleRange.x, scaleRange.y);
                                newDecoration.transform.localScale = new Vector3(scale, scale, scale);

                                //raycast to find the ground
                                RaycastHit hit;
                                //add noise to the position
                                pos += new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                                
                                if (Physics.Raycast(new Vector3(pos.x, 20, pos.z), Vector3.down, out hit, 200))
                                {
                                    pos = hit.point;
                                    newDecoration.SetActive(true);
                                }
                                else
                                {
                                    newDecoration.SetActive(false);
                                }
                            
                                newDecoration.transform.position = pos;
                            }
                        }
                    }
						
					
				}
			}
		}
    }
}
