using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;
using NPBehave;

public class MermaidBTController : AgentController
{
    private float MERMAID_WAIT_TIME = 3f;
    private Root behaviorTree;
    // Start is called before the first frame update
    void Start()
    {
        InitBT();
        Debug.Log("MermaidBTController Start");
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnDestroy()
    {
        behaviorTree?.Stop();
    }

    void InitBT()
    {

        behaviorTree = CreateBT();
 
        
 
        behaviorTree?.Start();
    }

    void UpdateBTInfo()
    {
        var obj = GameObject.FindGameObjectWithTag("Respawn");
        if(obj!=null){
           
            Vector3 lootLocalPos = this.transform.InverseTransformPoint(GameObject.FindGameObjectWithTag("Respawn").transform.position);
            behaviorTree.Blackboard["lootLocalPos"] = lootLocalPos;
            behaviorTree.Blackboard["lootDistance"] = lootLocalPos.magnitude;
             behaviorTree.Blackboard["target"] = obj;
           
        }
    }

    void ChangeMermaidState(int state)
    {
        //   var obj =GameObject.FindGameObjectWithTag("Respawn");
        if(state==0){
           this.GetComponent<MermaidMoveUnit>().steerType = MermaidMoveUnit.SteerType.Default;
        }
        else if(state ==1){
          var obj = behaviorTree.Blackboard["target"] as GameObject;
          var script = this.GetComponent<MermaidMoveUnit>();
          script.steerType = MermaidMoveUnit.SteerType.Seek;
          script.SetTarget(obj);
          Debug.Log("Start Seek");
        }
    }

    Root CreateBT(){
        Root root = new Root(
            new Service(1f, UpdateBTInfo,
            new Selector(

                    // check the 'Distance' blackboard value.
                    // When the condition changes, we want to immediately jump in or out of this path, thus we use IMMEDIATE_RESTART
                    new BlackboardCondition("lootDistance", Operator.IS_SMALLER, 8f, Stops.IMMEDIATE_RESTART,

                        // the player is in our range of 7.5f
                        new Sequence(

                            // go towards player until playerDistance is greater than 8 
                            new Action(() =>
                            {
                                ChangeMermaidState(1);
        
                            }) { Label = "Follow" },
                            new WaitUntilStopped(),
                            new Wait(MERMAID_WAIT_TIME),
                            new Action(() =>
                            {
                                    Debug.Log("End Seek");
                                 
                            }) { Label = "Follow1" }
                        )
                    ),

                    // wander
                    new Sequence(
                        new Action(() => {
                            ChangeMermaidState(0);
                              Debug.Log("Wander 1");
                            }
                            ) { Label = "Wander 1" },
                        
                        new Wait(MERMAID_WAIT_TIME),
                        new Action(() =>
                            {
                                ChangeMermaidState(1);
                                Debug.Log("Seek 2");
                                 
                            }) { Label = "Wander 2" },
                        new Wait(MERMAID_WAIT_TIME)
                    )
                )
            )

        );

        return root;
    }
}
