using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Random = UnityEngine.Random;

namespace BoidMobile
{
    public class FishFlockingMobile : MonoBehaviour
    {
        private const int BOID_SIZE = 8 * sizeof(float);
        public struct Boid
        {
            
            public Vector3 position;
            public float noise_offset;
            public Vector3 direction;
            public float theta;
   
            public Boid(Vector3 pos, Vector3 dir, float offset)
            {
                position.x = pos.x;
                position.y = pos.y;
                position.z = pos.z;
                direction.x = dir.x;
                direction.y = dir.y;
                direction.z = dir.z;
                noise_offset = offset;
                theta = Random.value * Mathf.PI * 2;
            }
        }

        //public ComputeShader shader;

        private float rotationSpeed = 1f;
        private float boidSpeed = 1f;
        private float neighbourDistance = 1f;
        private float boidSpeedVariation = 1f;
        public Mesh boidMesh;
        public Material boidMaterial;
        public int boidsCount;
        public float spawnRadius;
        public Transform target;

        int kernelHandle;
        ComputeBuffer boidsBuffer;
        ComputeBuffer argsBuffer;
        uint[] args = new uint[5] {0, 0, 0, 0, 0};
        Boid[] boidsArray;
        GameObject[] boids;
        int groupSizeX;
        int numOfBoids;
        Bounds bounds;
        MaterialPropertyBlock props;

        public BoidMobileSettings settings;

        //Debug
        struct GizmoFishData
        {
            public Vector3 pos;
            public Vector3 dir;
            public float collideDist;
            public bool isHit;
        }


        public bool debugFishCollideRay = false;
        private List<GizmoFishData> gizmoFishList = new List<GizmoFishData>();

        #region JobRayCast

        public bool useJobForRaycast = false;
        NativeArray<RaycastHit> resultsNative;
        NativeArray<RaycastCommand> commandsNative;
        RaycastHit[] results;
        RaycastCommand[] commandsLocal;

        #endregion

        public SpatialCube SpatialDatas;
        private bool UseCPUUpdateBoids = true;
        public bool Usecollision = true;


        private NativeArray<Boid> nativeBoidsArray;
        public bool useJobForBoidsUpdate = false;
        private JobHandle sheduleBoidsJobHandle;

        void Start()
        {
            //kernelHandle = shader.FindKernel("CSMain");

            uint x = 128;
            //shader.GetKernelThreadGroupSizes(kernelHandle, out x, out _, out _);
            groupSizeX = Mathf.CeilToInt((float) boidsCount / (float) x);
            numOfBoids = groupSizeX * (int) x;

            bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            props = new MaterialPropertyBlock();
            props.SetFloat("_UniqueID", UnityEngine.Random.value);

            InitBoids();
            InitShader();
            if (useJobForRaycast)
                InitJob();

            StartCoroutine(AsyncUpdate());
        }

        private void InitBoids()
        {
            boids = new GameObject[numOfBoids];
            boidsArray = new Boid[numOfBoids];

            for (int i = 0; i < numOfBoids; i++)
            {
                Vector3 pos = transform.position + Random.insideUnitSphere * spawnRadius;
                Quaternion rot = Quaternion.Slerp(transform.rotation, Random.rotation, 0.3f);
                float offset = Random.value * 1000.0f;
                boidsArray[i] = new Boid(pos, rot.eulerAngles, offset);
            }
            
            nativeBoidsArray = new NativeArray<Boid>(boidsArray.Length, Allocator.Persistent); 
        }

        void InitShader()
        {
            boidsBuffer = new ComputeBuffer(numOfBoids, 8 * sizeof(float),ComputeBufferType.Constant);
            boidsBuffer.SetData(boidsArray);

            argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            if (boidMesh != null)
            {
                args[0] = (uint) boidMesh.GetIndexCount(0);
                args[1] = (uint) numOfBoids;
            }

            argsBuffer.SetData(args);
            boidMaterial.SetConstantBuffer("UnityInstancing_Props", boidsBuffer,0,boids.Length*BOID_SIZE);
            
        }

        void InitJob()
        {
            results = new RaycastHit[boidsArray.Length];
            commandsLocal = new RaycastCommand[boidsArray.Length];
            resultsNative = new NativeArray<RaycastHit>(boidsArray.Length, Allocator.Persistent);
            commandsNative = new NativeArray<RaycastCommand>(boidsArray.Length, Allocator.Persistent);
            for (int i = 0; i < boidsArray.Length; i++)
            {
                var dir = boidsArray[i].direction.normalized;
                var pos = boidsArray[i].position;
                commandsNative[i] = new RaycastCommand(pos, dir, settings.collisionAvoidDst, settings.obstacleMask);
            }
        }

        void Update()
        {
            UpdateSetting();
            Graphics.DrawMeshInstancedIndirect(boidMesh, 0, boidMaterial, bounds, argsBuffer, 0, props);
        }

        void UpdateSetting()
        {
            boidSpeed = settings.speed;
            neighbourDistance = settings.neighbourDistance;
            rotationSpeed = settings.rotationSpeed;
            boidSpeedVariation = settings.boidSpeedVariation;
        }

        IEnumerator AsyncUpdate()
        {
            while (true)
            {
                    if (IsUseSpatialDivide())
                    {
                        UpdateSpatialInfo();
                        if(!useJobForBoidsUpdate)
                            UpdateBoidsBySpaticalCube();
                        else
                        {
                            UpdateBoidsJobBySpaticalCube();
                        }
                    }
                    else
                    {
                        UpdateBoids();
                    }
                    if (Usecollision)
                    {
                        if (!useJobForRaycast)
                            PostUpdateBoids();
                        else
                            PostUpdateBoidsByJob();
                    }

                    boidsBuffer.SetData(boidsArray);
                    //shader.SetBuffer(this.kernelHandle, "boidsBuffer", boidsBuffer);
                    //boidMaterial.SetConstantBuffer("boidsBuffer", boidsBuffer,0,boids.Length);
                    boidMaterial.SetConstantBuffer("InstanceInfos", boidsBuffer,0,boids.Length*BOID_SIZE);
                    yield return null;
                
            }
        }

        void UpdateSpatialInfo()
        {
            if (IsUseSpatialDivide())
            {
                SpatialDatas.UpdateSpatialInfo(boidsArray);
                if (useJobForBoidsUpdate)
                    SpatialDatas.UpdateNativeFishSpaticalData(boidsArray);
            }
        }

        void UpdateBoids()
        {
            var flockPosition = target.transform.position;
            int count = 0;
            // for (int i = 0; i < 1; i++)
            // {
                var indices = boidsArray;

                for (int j = 0; j < indices.Length; j++)
                {
                    count++;
                    var index = j;
                    var boid = boidsArray[index];

                    float delta = (boid.theta + Time.deltaTime * 4) / 2 / Mathf.PI;
                    boid.theta = delta - Mathf.Floor(delta);
                    float velocity = boidSpeed * 1.0f;
                    Vector3 separation = Vector3.zero;
                    Vector3 alignment = Vector3.zero;
                    Vector3 cohesion = flockPosition;
                    uint nearbyCount = 1;
                    for (int k = 0; k < indices.Length; k++)
                    {
                        if (k == j)
                            continue;
                        var indexN = k;
                        var nboid = boidsArray[indexN];
                        var diffLen = Vector3.Distance(boid.position, nboid.position);
                        if (diffLen < neighbourDistance)
                        {
                            Vector3 tempBoid_position = nboid.position;
                            Vector3 diff = boid.position - tempBoid_position;
                            float scaler = Mathf.Clamp01(1.0f - diffLen / neighbourDistance);
                            separation += diff * (scaler / diffLen);
                            alignment += nboid.direction;
                            cohesion += tempBoid_position;
                            nearbyCount += 1;
                        }
                    }

                    float avg = 1.0f / (float) nearbyCount;
                    alignment *= avg;
                    cohesion *= avg;
                    cohesion = Vector3.Normalize(cohesion - boid.position);
                    Vector3 direction = alignment + separation + cohesion;

                    float ip = Mathf.Exp(-rotationSpeed * Time.deltaTime);
                    boid.direction = Vector3.Lerp(Vector3.Normalize(direction), Vector3.Normalize(boid.direction), ip);
                    boid.direction = Vector3.Normalize(boid.direction);

                    boid.position += (boid.direction) * (velocity * Time.deltaTime);
                   boidsArray[index] = boid;
                }
            //}
            
        }

        void UpdateBoidsBySpaticalCube()
        {
            var spaceData = SpatialDatas.Datas;
            var flockPosition = target.transform.position;
            int count = 0;
            for (int i = 0; i < spaceData.Length; i++)
            {
                var indices = spaceData[i].indices;
                //var indices = spaceData[i].surrondIndices;
                for (int j = 0; j < indices.Count; j++)
                {
                    count++;
                    var index = indices[j];
                    var boid = boidsArray[index];

                    float delta = (boid.theta + Time.deltaTime * 4) / 2 / Mathf.PI;
                    boid.theta = delta - Mathf.Floor(delta);
                    float velocity = boidSpeed * 1.0f;
                    Vector3 separation = Vector3.zero;
                    Vector3 alignment = Vector3.zero;
                    Vector3 cohesion = flockPosition;
                    uint nearbyCount = 1;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        if (k == j)
                            continue;
                        var indexN = indices[k];
                        var nboid = boidsArray[indexN];
                        var diffLen = Vector3.Distance(boid.position, nboid.position);
                        if (diffLen < neighbourDistance)
                        {
                            Vector3 tempBoid_position = nboid.position;
                            Vector3 diff = boid.position - tempBoid_position;
                            float scaler = Mathf.Clamp01(1.0f - diffLen / neighbourDistance);
                            separation += diff * (scaler / diffLen);
                            alignment += nboid.direction;
                            cohesion += tempBoid_position;
                            nearbyCount += 1;
                        }
                    }

                    float avg = 1.0f / (float) nearbyCount;
                    alignment *= avg;
                    cohesion *= avg;
                    cohesion = Vector3.Normalize(cohesion - boid.position);
                    Vector3 direction = alignment + separation + cohesion;

                    float ip = Mathf.Exp(-rotationSpeed * Time.deltaTime);
                    boid.direction = Vector3.Lerp(Vector3.Normalize(direction), Vector3.Normalize(boid.direction), ip);
                    boid.direction = Vector3.Normalize(boid.direction);

                    boid.position += (boid.direction) * (velocity * Time.deltaTime);
                    boidsArray[index] = boid;
                }
            }
        }

        void OnDestroy()
        {
            if (boidsBuffer != null)
            {
                boidsBuffer.Dispose();
            }

            if (argsBuffer != null)
            {
                argsBuffer.Dispose();
            }

            if (useJobForRaycast)
            {
                if (commandsNative != null)
                {
                    commandsNative.Dispose();
                }

                if (resultsNative != null)
                {
                    resultsNative.Dispose();
                }
            }

            if (useJobForBoidsUpdate)
            {
                nativeBoidsArray.Dispose();
            }
        }

        void PostUpdateBoids()
        {
            if (debugFishCollideRay)
                gizmoFishList.Clear();
            for (int i = 0; i < boidsArray.Length; i++)
            {
                var dir = boidsArray[i].direction;
                var pos = boidsArray[i].position;
                float hitDistance = settings.collisionAvoidDst;
                if (IsHeadingForCollision(pos, dir, out hitDistance))
                {
                    float weight = hitDistance / settings.collisionAvoidDst;
                    weight = Mathf.Max(Mathf.Min(weight, 1f), 0.01f);
                    Vector3 collisionAvoidDir = ObstacleRays(pos, dir);
                    Vector3 collisionAvoidForce = collisionAvoidDir.normalized * settings.avoidCollisionWeight / weight;
                    // dir = dir.normalized;
                    dir = Vector3.Lerp(dir, collisionAvoidForce, Time.deltaTime * rotationSpeed);
                    boidsArray[i].direction = dir;
                }

                //update info for fish collision gizmos
                if (debugFishCollideRay)
                {
                    GizmoFishData data = new GizmoFishData();
                    data.pos = pos;
                    data.dir = dir;
                    if (hitDistance < settings.collisionAvoidDst)
                        data.isHit = true;
                    data.collideDist = hitDistance;
                    gizmoFishList.Add(data);
                }
            }
        }

        void PostUpdateBoidsByJob()
        {
            if (debugFishCollideRay)
                gizmoFishList.Clear();

            //init
            float hitDistance = settings.collisionAvoidDst;
            int count = boidsArray.Length;

            commandsNative.CopyTo(commandsLocal);
            for (int i = 0; i < boidsArray.Length; i++)
            {
                var dir = boidsArray[i].direction;
                var pos = boidsArray[i].position;
                commandsLocal[i].from = pos;
                commandsLocal[i].direction = dir;
            }

            commandsNative.CopyFrom(commandsLocal);
            JobHandle handle = RaycastCommand.ScheduleBatch(commandsNative, resultsNative, 16, default(JobHandle));
            handle.Complete();

            resultsNative.CopyTo(results);

            for (int i = 0; i < boidsArray.Length; i++)
            {
                var dir = boidsArray[i].direction;
                var pos = boidsArray[i].position;
                hitDistance = settings.collisionAvoidDst;
                if (results[i].collider != null)
                {
                    hitDistance = results[i].distance;
                    float weight = hitDistance / settings.collisionAvoidDst;
                    weight = Mathf.Max(Mathf.Min(weight, 1f), 0.01f);
                    Vector3 collisionAvoidDir = ObstacleRays(pos, dir, false);
                    Vector3 collisionAvoidForce = collisionAvoidDir.normalized * settings.avoidCollisionWeight / weight;
                    //dir = dir.normalized;
                    dir = Vector3.Lerp(dir, collisionAvoidForce, Time.deltaTime * rotationSpeed);
                    boidsArray[i].direction = dir;
                }

                //update info for fish collision gizmos
                if (debugFishCollideRay)
                {
                    GizmoFishData data = new GizmoFishData();
                    data.pos = pos;
                    data.dir = dir;
                    data.isHit = false;
                    if (hitDistance < settings.collisionAvoidDst)
                        data.isHit = true;
                    data.collideDist = hitDistance;
                    gizmoFishList.Add(data);
                }
            }
        }

        bool IsHeadingForCollision(Vector3 pos, Vector3 dir, out float hitDistance)
        {
            RaycastHit hit;
            hitDistance = 100f;
            if (Physics.SphereCast(pos, settings.boundsRadius, dir, out hit, settings.collisionAvoidDst,
                settings.obstacleMask))
            {
                hitDistance = hit.distance;
                return true;
            }
            else
            {
            }

            return false;
        }


        Vector3 ObstacleRays(Vector3 pos, Vector3 direction, bool accurate = true)
        {
            Vector3[] rayDirections = BoidHelper.flockDirections;
            Transform cachedTransform = transform;
            transform.forward = direction;
            if (accurate == false)
            {
                float random = Random.Range(0f, 1f);
                return random > 0.75f ? transform.right : -transform.right;
            }

            for (int i = 0; i < rayDirections.Length; i++)
            {
                Vector3 dir = cachedTransform.TransformDirection(rayDirections[i]);
                Ray ray = new Ray(pos, dir);
                if (!Physics.SphereCast(ray, settings.boundsRadius, settings.collisionAvoidDst, settings.obstacleMask))
                {
                    return dir;
                }
            }

            return direction;
        }


        void DrawGizmosSphereCast()
        {
            foreach (var ray in gizmoFishList)
            {
                var des = ray.dir * settings.collisionAvoidDst;
                if (!ray.isHit)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawRay(ray.pos, des);
                }
                else
                {
                    var pos = ray.pos + ray.dir * ray.collideDist;
                    des = ray.dir * ray.collideDist;
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(pos, settings.boundsRadius);
                    Gizmos.DrawRay(ray.pos, des);
                }
            }
        }

        void OnDrawGizmos()
        {
            if (debugFishCollideRay)
            {
                DrawGizmosSphereCast();
            }
        }

        bool IsUseSpatialDivide()
        {
            return SpatialDatas != null ? true : false;
        }

        void UpdateBoidsJobBySpaticalCube()
        {
            // Debug.Log("asdfadasdfa"+sheduleBoidsJobHandle.IsCompleted.ToString());
            // if (sheduleBoidsJobHandle.IsCompleted)
            // {
            //     sheduleBoidsJobHandle.Complete();
            //     nativeBoidsArray.CopyTo(boidsArray);
            //     nativeBoidsArray.CopyFrom(boidsArray);
            //     var job = new UpdateSpaticalBoidsJob()
            //     {
            //         deltaTime = Time.deltaTime,
            //         rotationSpeed = rotationSpeed,
            //         boidSpeed = boidSpeed,
            //         neighbourDistance = neighbourDistance,
            //         flockPosition = target.transform.position,
            //         lookup = SpatialDatas.NativeFishSpaticalData.lookup,
            //         fishArray = SpatialDatas.NativeFishSpaticalData.fishArray,
            //         boidsArray = nativeBoidsArray
            //     };
            //     JobHandle sheduleJobDependency = new JobHandle();
            //     sheduleBoidsJobHandle = job.ScheduleParallel(SpatialDatas.NativeFishSpaticalData.lookup.Length, 16,sheduleJobDependency);
            //     
            // }
            nativeBoidsArray.CopyFrom(boidsArray);
            var job = new UpdateSpaticalBoidsJob()
            {
                deltaTime = Time.deltaTime,
                rotationSpeed = rotationSpeed,
                boidSpeed = boidSpeed,
                neighbourDistance = neighbourDistance,
                flockPosition = target.transform.position,
                lookup = SpatialDatas.NativeFishSpaticalData.lookup,
                fishArray = SpatialDatas.NativeFishSpaticalData.fishArray,
                boidsArray = nativeBoidsArray
            };
            JobHandle sheduleJobDependency = new JobHandle();
            sheduleBoidsJobHandle = job.ScheduleParallel(SpatialDatas.NativeFishSpaticalData.lookup.Length, 16,sheduleJobDependency);
            
            sheduleBoidsJobHandle.Complete();
            nativeBoidsArray.CopyTo(boidsArray);
        }

        public struct UpdateSpaticalBoidsJob : IJobFor
        {
            public float boidSpeed;
            public float rotationSpeed;
            public float deltaTime;
            public float neighbourDistance;
            public Unity.Mathematics.float3 flockPosition;
            [ReadOnly]public NativeArray<SpatialCube.unitLookup> lookup;
            [ReadOnly]public NativeArray<int> fishArray;
            
            [NativeDisableParallelForRestriction]public NativeArray<Boid> boidsArray;
            
            public void Execute(int i)
            {
                var data = lookup[i];
                int startIndex = data.startIndex;
                for (int j = 0; j < data.count; j++)
                {
                    var index = fishArray[startIndex+j];
                    var boid = boidsArray[index];
                    float delta = (boid.theta + deltaTime * 4) / 2 / Mathf.PI;
                    boid.theta = delta - math.floor(delta);
                    float velocity = boidSpeed * 1.0f;
                    float3 separation = float3.zero;
                    float3 alignment = float3.zero;
                    float3 cohesion = flockPosition;
                    int nearbyCount = 1;
                    for (int k = 0; k < data.count; k++)
                    {
                        if (k == j)
                            continue;
                        var indexN = fishArray[startIndex+k];
                        var nboid = boidsArray[indexN];
                        var diffLen = math.distance(boid.position, nboid.position);
                        if (diffLen < neighbourDistance)
                        {
                            float3 tempBoid_position = nboid.position;
                            float3 diff = (float3)boid.position -tempBoid_position;
                            float scaler = Mathf.Clamp01(1.0f - diffLen / neighbourDistance);
                            separation += diff * (scaler / diffLen);
                            alignment += (float3)nboid.direction;
                            cohesion += tempBoid_position;
                            nearbyCount += 1;
                        }
                    }
                    float avg = 1.0f / (float) nearbyCount;
                    alignment *= avg;
                    cohesion *= avg;
                    cohesion = math.normalize(cohesion - (float3)boid.position);
                    Vector3 direction = alignment + separation + cohesion;
                    
                    float ip = math.exp(-rotationSpeed * deltaTime);
                    direction = math.lerp(math.normalize(direction), math.normalize(boid.direction), ip);
                    boid.direction = math.normalize(direction);
                    
                    boid.position += (direction) * (velocity * deltaTime);
                    boidsArray[index] = boid;
                }
                
            }
        }
    }
}