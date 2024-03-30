using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityMovementAI;
public class MermaidMoveUnit : MonoBehaviour
{
        public enum SteerType
        {
            Seek,
            Default
        }
        public SteerType steerType = SteerType.Default;
        public MovementAIRigidbody target;



        SteeringBasics steeringBasics;
        Wander2 wander;
        WallAvoidance wallAvoidance; 

        public Animator animator;


        public void SetTarget(GameObject des)
        {
            target = null;
            if(des!=null)
            {
                var script = des.GetComponent<MovementAIRigidbody>();
                target = script;
            }
        }

        void Start()
        {
            wander = GetComponent<Wander2>();

            steeringBasics = GetComponent<SteeringBasics>();
            wallAvoidance = GetComponent<WallAvoidance>();
 
        }

        void FixedUpdate()
        {
          
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
            Vector3 accel = wallAvoidance.GetSteering();

            if (accel.magnitude < 0.005f)
            {
               
                accel = wander.GetSteering();
            }

            if(animator!=null)
                    animator.SetBool("IsSeek", false);
            steeringBasics.Steer(accel);
            steeringBasics.LookWhereYoureGoing();
          

        }

        void UpdateSeekBehaviour(){
           
            Vector3 accel = Vector3.zero;
            if(target != null)
            {
               
                accel = steeringBasics.Seek(target.transform.position);
                                
            }

            if(animator!=null)
            {
                var isSeek = false;
                if(target!=null)
                    isSeek = true;
                animator.SetBool("IsSeek", isSeek);
            }
            
  
            
            steeringBasics.Steer(accel);
            steeringBasics.LookWhereYoureGoing();
            
        }

        
}
