using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMovementAI;

public class LootMoveUnit : MonoBehaviour
{
    // Start is called before the first frame update
    public Vector3[] desPoints;
    public int nextPoint = 0;

    public float timeInterval=6f;

    public SteeringBasics steeringBasics;
    public MovementAIRigidbody rb;
    private float time = 0f;
    void Start()
    {
        steeringBasics = GetComponent<SteeringBasics>();
        rb = GetComponent<MovementAIRigidbody>();
        Go2NextPoint();
    }

    // Update is called once per frame
    void Update()
    {
        time+= Time.deltaTime;
        if(time>timeInterval)
            Go2NextPoint();

    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.GetComponent<MermaidMoveUnit>() != null)
        {
            Debug.Log("Mermaid is near");
            Go2NextPoint();
        }
    }

    void FixedUpdate()
    {
            Vector3 targetPos = desPoints[nextPoint];
            Vector3 dis = targetPos - transform.position;
            Vector3 accel = Vector3.zero;
            if (dis.magnitude > 0.1f)
            {
                accel = steeringBasics.Seek(targetPos);
            }
            else
            {
                rb.Velocity = Vector3.zero; 
               
            }
        
     
            steeringBasics.Steer(accel);  
            steeringBasics.LookWhereYoureGoing();
    }
    void Go2NextPoint()
    {
        time = 0;
        nextPoint = (nextPoint + 1) % desPoints.Length;
    }
}
