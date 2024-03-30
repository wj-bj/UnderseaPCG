using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class BoidMobileSettings : ScriptableObject {
    // Settings
    public float rotationSpeed = 3f;
    public float speed = 6f;
    public float neighbourDistance = 3f;
    public float boidSpeedVariation = 1f;
    [Header ("Collisions")]
    public LayerMask obstacleMask;
    public float boundsRadius = .27f;
    public float avoidCollisionWeight = 10;
    public float collisionAvoidDst = 5;

}