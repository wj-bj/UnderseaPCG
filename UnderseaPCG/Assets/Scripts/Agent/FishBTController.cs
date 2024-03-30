using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;
using NPBehave;

public class FishBTController : AgentController
{
    private const int FISH_IDLE = 0;
    private const int FISH_SEEK = 1;
    private float WAIT_TIME = 4f;
    private Root BTree;
    public float SEEK_DISTANCE = 5f;

    private List<int> utilityScores;
    private int currentAction = -1;

    private float timeInterval = 0f;
    // Start is called before the first frame update
    void Start()
    {
        // InitBT();
        utilityScores = new List<int>();
        utilityScores.Add(0);
        utilityScores.Add(0);
    }

    // Update is called once per frame
    void Update()
    {
        UpdateScores();
  
        timeInterval += Time.deltaTime;
        if(timeInterval>1f){
            timeInterval = 0f;
             var action = ShouldSeek()?FISH_SEEK:FISH_IDLE;
            if(currentAction!= action){
                currentAction = action;
                var tree = SelectBT(action);
                SwitchTree(tree);
                
            }
        }
    }

    void OnDestroy()
    {
        BTree?.Stop();
    }

    void InitBT()
    {
        
        UpdateScores();
    }

    void UpdateBTInfo()
    {
        var obj = GameObject.FindGameObjectWithTag("Player");
        if(obj!=null){
           
            Vector3 objLocalPos = this.transform.InverseTransformPoint(obj.transform.position);
            BTree.Blackboard["targetLocalPos"] = objLocalPos;
            BTree.Blackboard["targetDistance"] = objLocalPos.magnitude;
            BTree.Blackboard["target"] = obj;
           
        }
        else{
            BTree.Blackboard["targetDistance"] = 100f;
            BTree.Blackboard["target"] = null;
        }
    }

    void ChangeFishState(int state)
    {
        Debug.Log("ChangeFishState"+state);
        if(state==0){
           this.GetComponentInChildren<FishMoveUnit>().steerType = FishMoveUnit.SteerType.Default;
        }
        else if(state ==1){
          var obj = BTree.Blackboard["target"] as GameObject;
          var script = this.GetComponentInChildren<FishMoveUnit>();
          script.steerType = FishMoveUnit.SteerType.Seek;
          script.SetTarget(obj);
          
        }
    }



    private void UpdateScores()
    {
        var obj = GameObject.FindGameObjectWithTag("Player");
        float distance = 100f;
        if(obj!=null){
            var fishObj = this.GetComponentInChildren<FishMoveUnit>();
            Vector3 objLocalPos = fishObj.transform.InverseTransformPoint(obj.transform.position);
            distance = objLocalPos.magnitude;
           
        }
 
        if(distance<SEEK_DISTANCE){
            utilityScores[FISH_IDLE] = 10;
            utilityScores[FISH_SEEK] = 30;
        }
        else if(distance<SEEK_DISTANCE*1.5){
            utilityScores[FISH_IDLE] = 20;
            utilityScores[FISH_SEEK] = 20;
        }
        else{
            utilityScores[FISH_IDLE] = 40;
            utilityScores[FISH_SEEK] = 0;
        }
    }

    private bool ShouldSeek()
    {
        bool ret = false;
        var probability = UnityEngine.Random.Range(0f, 1f);
        float threshold = (float)utilityScores[FISH_IDLE] / (utilityScores[FISH_SEEK] + utilityScores[FISH_IDLE]);
        if(probability>threshold){
            ret = true;
        }
        return ret;
    }

    private Root SelectBT(int action)
    {
        Root root = null;
        if(action == FISH_SEEK){
        
                root =FishSeek();
        }
        else{
                root = FishIdle();
        }
        return root;
          
    }

    private Root FishSeek()
    {
        Node sel = new Sequence( new Action(() =>
                            {
                                ChangeFishState(1);
        
                            }) { Label = "Follow" },
                            new Wait(WAIT_TIME));
            // Wrap behaviour in blackboard update service
        Node service = new Service(1f, UpdateBTInfo, sel);
        Root root = new Root(service);
        return root;
        
    }
    private Root FishIdle()
    {
       Node sel = new Sequence( new Action(() =>
                            {
                                ChangeFishState(0);
        
                            }) { Label = "Path" },
                            new Wait(WAIT_TIME));
            // Wrap behaviour in blackboard update service
        Node service = new Service(1f, UpdateBTInfo, sel);
        Root root = new Root(service);
        return root;
    }

    private void SwitchTree(Root t)
    {
            if (BTree != null) {
              
                BTree.Stop();
            }

            BTree = t;
            #if UNITY_EDITOR
            Debugger debugger = (Debugger)this.gameObject.AddComponent(typeof(Debugger));
            debugger.BehaviorTree = BTree;
            #endif
            
            BTree.Start();
    }
}
