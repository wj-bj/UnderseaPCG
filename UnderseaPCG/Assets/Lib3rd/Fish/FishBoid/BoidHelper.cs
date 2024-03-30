using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class BoidHelper {

    const int numViewDirections = 300;
    const int numFlockViewDirections = 6;
    public static readonly Vector3[] directions;
    public static readonly Vector3[] flockDirections;

    static BoidHelper () {
        directions = new Vector3[BoidHelper.numViewDirections];

        float goldenRatio = (1 + Mathf.Sqrt (5)) / 2;
        float angleIncrement = Mathf.PI * 2 * goldenRatio;

        for (int i = 0; i < numViewDirections; i++) {
            float t = (float) i / numViewDirections;
            float inclination = Mathf.Acos (1 - 2 * t);
            float azimuth = angleIncrement * i;

            float x = Mathf.Sin (inclination) * Mathf.Cos (azimuth);
            float y = Mathf.Sin (inclination) * Mathf.Sin (azimuth);
            float z = Mathf.Cos (inclination);
            directions[i] = new Vector3 (x, y, z);
        }

        flockDirections = new Vector3[BoidHelper.numFlockViewDirections];

        for (int i = 0; i < numFlockViewDirections; i++) {
            float t = (float) i / numFlockViewDirections;
            float inclination = Mathf.Acos (1 - 2 * t);
            float azimuth = angleIncrement * i;

            float x = Mathf.Sin (inclination) * Mathf.Cos (azimuth);
            float y = Mathf.Sin (inclination) * Mathf.Sin (azimuth);
            float z = Mathf.Cos (inclination);
            flockDirections[i] = new Vector3 (x, y, z);
        }
    }


}