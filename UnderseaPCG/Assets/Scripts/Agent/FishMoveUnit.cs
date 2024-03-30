using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;
public class FishMoveUnit : MonoBehaviour
{
        public enum SteerType
        {
            Seek,
            Flee,
            FollowPath,
            Default
        }
        public SteerType steerType = SteerType.Default;
        public MovementAIRigidbody target;
        public bool pathLoop = false;

        public bool reversePath = false;

        public LinePath path;


        SteeringBasics steeringBasics;
        FollowPath followPath;

        Flee flee;




        void Start()
        {
            path.CalcDistances();

            steeringBasics = GetComponent<SteeringBasics>();
            followPath = GetComponent<FollowPath>();
            flee = GetComponent<Flee>();
        }

        void FixedUpdate()
        {
            path.Draw();

            switch (steerType)
            {
                case SteerType.Seek:
                    UpdateSeekBehaviour();
                    break;
    
                case SteerType.Default:
                    UpdateDefaultBehaviour();
                    break;
            }


        }

        void UpdateDefaultBehaviour()
        {
            if (reversePath && followPath.IsAtEndOfPath(path))
            {
                path.ReversePath();
            }

            Vector3 accel = followPath.GetSteering(path, pathLoop);
             if(target != null)
            {
                Vector3 dist = transform.position - target.transform.position;
                var panicDist = flee.panicDist*1.5f;

                /* If the target is far way then don't flee */
                if (dist.magnitude < panicDist){
                    var evadeAccel = flee.GetSteering(target.transform.position);
                    
                    if(evadeAccel.magnitude > 0.2f)
                    {
                        
                        accel = evadeAccel;
                    }
                }
                
            }
  

            steeringBasics.Steer(accel);
            steeringBasics.LookWhereYoureGoing();
        }

        void UpdateSeekBehaviour(){
           
            Vector3 accel = Vector3.zero;
            if(target != null)
            {
                //Vector3 dist = transform.position - target.transform.position;
                accel = steeringBasics.Seek(target.transform.position);
                
            }
  
            steeringBasics.Steer(accel);
            steeringBasics.LookWhereYoureGoing();
            
        }

        public void SetTarget(GameObject des)
        {
            target = null;
            if(des!=null)
            {
                var script = des.GetComponent<MovementAIRigidbody>();
                target = script;
            }
        }
}
