using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;
public class DiverMoveUnit : MonoBehaviour
{

    public enum SteerType
    {
            Manul,
            Default
    }

    public SteerType steerType = SteerType.Default;
    SteeringBasics steeringBasics;
    public bool pathLoop = false;

    public bool reversePath = false;

    public LinePath path;
    FollowPath followPath;
    MovementAIRigidbody rigidbody;
    // Start is called before the first frame update
    void Start()
    {
        path.CalcDistances();

        steeringBasics = GetComponent<SteeringBasics>();
        followPath = GetComponent<FollowPath>();

        steeringBasics = GetComponent<SteeringBasics>();

        rigidbody = GetComponent<MovementAIRigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void FixedUpdate()
    {
        
        switch (steerType)
        {
            case SteerType.Manul:
                UpdateManulBehaviour();
                break;
    
            case SteerType.Default:
                UpdateDefaultBehaviour();
                break;
        }
    }

    void UpdateDefaultBehaviour()
    {
            path.Draw();
            var script = gameObject.GetComponent<Submarine>();
            script.enabled = false;

            if (reversePath && followPath.IsAtEndOfPath(path))
            {
                path.ReversePath();
            }

            Vector3 accel = followPath.GetSteering(path, pathLoop);
        
            steeringBasics.Steer(accel);
            steeringBasics.LookWhereYoureGoing();
            
    }

    void UpdateManulBehaviour()
    {
        rigidbody.Velocity = new Vector3(0, 0, 0);
        rigidbody.AngularVelocity = 0;
        var script = gameObject.GetComponent<Submarine>();
        script.enabled = true;
        
    }
}
