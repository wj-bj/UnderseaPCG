using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using Unity.Jobs;
using Unity.Collections;

public class FishFlocking : MonoBehaviour
{
    public struct Boid
    {
        public Vector3 position;
        public Vector3 direction;
        public float noise_offset;
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

    public ComputeShader shader;

    public float rotationSpeed = 1f;
    public float boidSpeed = 1f;
    public float neighbourDistance = 1f;
    public float boidSpeedVariation = 1f;
    public Mesh boidMesh;
    public Material boidMaterial;
    public int boidsCount;
    public float spawnRadius;
    public Transform target;

    int kernelHandle;
    ComputeBuffer boidsBuffer;
    ComputeBuffer argsBuffer;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    Boid[] boidsArray;
    GameObject[] boids;
    int groupSizeX;
    int numOfBoids;
    Bounds bounds;
    MaterialPropertyBlock props;

    public BoidSettings settings;

    //Debug
    struct GizmoFishData{
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
    
    public SpatialCube SpatialDatas = null;
    public bool UseCPUUpdateBoids;
    public bool Usecollision = true;

    private const int MAX_INSTANCE_SIZE = 1024;
    void Start()
    {
        kernelHandle = shader.FindKernel("CSMain");

        uint x;
        shader.GetKernelThreadGroupSizes(kernelHandle, out x, out _, out _);
        groupSizeX = Mathf.CeilToInt((float)boidsCount / (float)x);
        numOfBoids = groupSizeX * (int)x;

        bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
        props = new MaterialPropertyBlock();
        props.SetFloat("_UniqueID", Random.value);

        InitBoids();
        InitShader();
        if(useJobForRaycast)
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
    }

    void InitShader()
    {
        boidsBuffer = new ComputeBuffer(numOfBoids, 8 * sizeof(float));
        boidsBuffer.SetData(boidsArray);

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        if (boidMesh != null)
        {
            args[0] = (uint)boidMesh.GetIndexCount(0);
            args[1] = (uint)numOfBoids;
        }
        argsBuffer.SetData(args);

        shader.SetBuffer(this.kernelHandle, "boidsBuffer", boidsBuffer);
        shader.SetFloat("rotationSpeed", rotationSpeed);
        shader.SetFloat("boidSpeed", boidSpeed);
        shader.SetFloat("boidSpeedVariation", boidSpeedVariation);
        shader.SetVector("flockPosition", target.transform.position);
        shader.SetFloat("neighbourDistance", neighbourDistance);
        shader.SetInt("boidsCount", numOfBoids);

        boidMaterial.SetBuffer("boidsBuffer", boidsBuffer);
        
    }

    void InitJob(){
        
        results = new RaycastHit[boidsArray.Length];
        commandsLocal = new RaycastCommand[boidsArray.Length];
        resultsNative = new NativeArray<RaycastHit>(boidsArray.Length, Allocator.Persistent);
        commandsNative = new NativeArray<RaycastCommand>(boidsArray.Length,Allocator.Persistent);
        for(int i=0; i< boidsArray.Length;i++){
            var dir = boidsArray[i].direction.normalized;
            var pos = boidsArray[i].position;
            commandsNative[i] = new RaycastCommand(pos,dir,settings.collisionAvoidDst, settings.obstacleMask);
        }
    }

    void Update()
    {
        shader.SetFloat("time", Time.time);
        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetVector("flockPosition",target.position);

        shader.SetFloat("neighbourDistance", neighbourDistance);
        shader.SetFloat("rotationSpeed", rotationSpeed);
        shader.SetFloat("boidSpeed", boidSpeed);
        shader.SetFloat("boidSpeedVariation", boidSpeedVariation);
        shader.Dispatch(this.kernelHandle, groupSizeX, 1, 1);

        Graphics.DrawMeshInstancedIndirect(boidMesh, 0, boidMaterial, bounds, argsBuffer, 0, props);
    }

    IEnumerator AsyncUpdate(){
        while(true){   
            if(!UseCPUUpdateBoids){
                var request = AsyncGPUReadback.Request(boidsBuffer);
                yield return new WaitUntil(()=>request.done);
                request.GetData<Boid>().CopyTo(boidsArray);
                if(Usecollision){
                    if(!useJobForRaycast)
                        PostUpdateBoids();
                    else
                        PostUpdateBoidsByJob();
                }

                UpdateSpatialInfo();
                boidsBuffer.SetData(boidsArray);
                shader.SetBuffer(this.kernelHandle, "boidsBuffer", boidsBuffer);
                boidMaterial.SetBuffer("boidsBuffer", boidsBuffer);  
                yield return new WaitForFixedUpdate();
               
            }
            else
            {
                UpdateSpatialInfo();
                UpdateBoidsBySpaticalCube();
                if(Usecollision){
                    if(!useJobForRaycast)
                        PostUpdateBoids();
                    else
                        PostUpdateBoidsByJob();
                }
                boidsBuffer.SetData(boidsArray);
                shader.SetBuffer(this.kernelHandle, "boidsBuffer", boidsBuffer);
                boidMaterial.SetBuffer("boidsBuffer", boidsBuffer);
               
                yield return null;
            }
            
        }
        
    }

    void UpdateSpatialInfo(){
        if(IsUseSpatialDivide()){
            SpatialDatas.UpdateSpatialInfo(boidsArray);

        }
    }

    void UpdateBoidsBySpaticalCube(){
        if(SpatialDatas == null)
            return;
        var spaceData = SpatialDatas.Datas;
        var flockPosition =  target.transform.position;
        int count = 0;
        for(int i =0; i< spaceData.Length; i++){
            var indices = spaceData[i].indices;
            //var indices = spaceData[i].surrondIndices;
            for(int j =0; j< indices.Count;j++){
                count++;
                var index = indices[j];
                var boid = boidsArray[index];
                
                float delta = (boid.theta + Time.deltaTime * 4)/2/Mathf.PI;
                boid.theta = delta - Mathf.Floor(delta);
                float velocity = boidSpeed *1.0f ;
                Vector3 separation = Vector3.zero;
                Vector3 alignment = Vector3.zero;
                Vector3 cohesion = flockPosition;
                uint nearbyCount = 1;
                for(int k =0; k< indices.Count;k++){
                    if(k==j)
                        continue;
                    var indexN = indices[k];
                    var nboid = boidsArray[indexN];
                    var diffLen = Vector3.Distance(boid.position,nboid.position);
                    if(diffLen<neighbourDistance){
                        
                        Vector3 tempBoid_position = nboid.position;
			            Vector3 diff = boid.position - tempBoid_position;
			            float scaler = Mathf.Clamp01(1.0f - diffLen / neighbourDistance);
			            separation += diff * (scaler / diffLen);
			            alignment += nboid.direction;
			            cohesion += tempBoid_position;
                        nearbyCount += 1;
                    }
                }
                float avg = 1.0f / (float)nearbyCount;
	            alignment *= avg;
	            cohesion *= avg;
	            cohesion = Vector3.Normalize(cohesion - boid.position);
	            Vector3 direction = alignment + separation + cohesion;

	            float ip = Mathf.Exp(-rotationSpeed * Time.deltaTime);
	            boid.direction = Vector3.Lerp(Vector3.Normalize(direction), Vector3.Normalize(boid.direction), ip);
	            boid.direction = Vector3.Normalize(boid.direction);

	            boid.position += (boid.direction) * (velocity * Time.deltaTime);
               // boid.position += (new Vector3(1,0,1)) * (1 * Time.deltaTime);
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

        if(useJobForRaycast){
            if(commandsNative!=null)
            {
                commandsNative.Dispose();
            }

            if(resultsNative!=null)
            {
                resultsNative.Dispose();
            }
        }

    }

    void PostUpdateBoids(){
        if(debugFishCollideRay)
            gizmoFishList.Clear();
        for(int i=0; i< boidsArray.Length;i++){

            var dir = boidsArray[i].direction;
            var pos = boidsArray[i].position;
            float hitDistance = settings.collisionAvoidDst;
            if (IsHeadingForCollision (pos,dir,out hitDistance)) {
                float weight = hitDistance/settings.collisionAvoidDst;
                weight = Mathf.Max(Mathf.Min(weight,1f),0.01f);
                Vector3 collisionAvoidDir = ObstacleRays (pos,dir);
                Vector3 collisionAvoidForce = collisionAvoidDir.normalized * settings.avoidCollisionWeight/weight;
               // dir = dir.normalized;
                dir = Vector3.Lerp(dir,collisionAvoidForce,Time.deltaTime*rotationSpeed);
                boidsArray[i].direction = dir;
                
            }

            //update info for fish collision gizmos
            if(debugFishCollideRay){
                GizmoFishData data = new GizmoFishData();
                data.pos = pos;
                data.dir = dir;
                if(hitDistance < settings.collisionAvoidDst)
                        data.isHit = true;
                data.collideDist = hitDistance;
                gizmoFishList.Add(data);
            }
        }

    }

    void PostUpdateBoidsByJob(){
       
        if(debugFishCollideRay)
            gizmoFishList.Clear();

        //init
        float hitDistance = settings.collisionAvoidDst;
        int count = boidsArray.Length;
  
        commandsNative.CopyTo(commandsLocal);
        for(int i=0; i< boidsArray.Length;i++){   
            var dir = boidsArray[i].direction;
            var pos = boidsArray[i].position;
            commandsLocal[i].from = pos;
            commandsLocal[i].direction = dir; 
   
        }
        commandsNative.CopyFrom(commandsLocal);
        JobHandle handle = RaycastCommand.ScheduleBatch(commandsNative,resultsNative,16,default(JobHandle));
        handle.Complete();
      
       resultsNative.CopyTo(results);
        
        for(int i=0; i< boidsArray.Length;i++){
            var dir = boidsArray[i].direction;
            var pos = boidsArray[i].position;
            hitDistance = settings.collisionAvoidDst;
            if (results[i].collider!=null) {
            
                hitDistance = results[i].distance;
                float weight = hitDistance/settings.collisionAvoidDst;
                weight = Mathf.Max(Mathf.Min(weight,1f),0.01f);
                Vector3 collisionAvoidDir = ObstacleRays (pos,dir,false);
                Vector3 collisionAvoidForce = collisionAvoidDir.normalized * settings.avoidCollisionWeight/weight;
                //dir = dir.normalized;
                dir = Vector3.Lerp(dir,collisionAvoidForce,Time.deltaTime*rotationSpeed);
                boidsArray[i].direction = dir;
                
            }

            //update info for fish collision gizmos
            if(debugFishCollideRay){
                GizmoFishData data = new GizmoFishData();
                data.pos = pos;
                data.dir = dir;
                data.isHit = false;
                if(hitDistance < settings.collisionAvoidDst)
                        data.isHit = true;
                data.collideDist = hitDistance;
                gizmoFishList.Add(data);
            }
        }


    }

    bool IsHeadingForCollision (Vector3 pos, Vector3 dir, out float  hitDistance) {
        RaycastHit hit;
        hitDistance = 100f;
        if (Physics.SphereCast (pos, settings.boundsRadius, dir, out hit, settings.collisionAvoidDst, settings.obstacleMask)) {
            hitDistance = hit.distance;
            return true;
        } else { }
        return false;
    }


    Vector3 ObstacleRays (Vector3 pos,Vector3 direction, bool accurate = true) {
        Vector3[] rayDirections = BoidHelper.flockDirections;
        Transform cachedTransform = transform;
        transform.forward = direction;
        if(accurate == false){
            float random = Random.Range(0f,1f);
            return random>0.75f?transform.right:-transform.right;
        }
        
        for (int i = 0; i < rayDirections.Length; i++) {
           
            Vector3 dir = cachedTransform.TransformDirection (rayDirections[i]);
            Ray ray = new Ray (pos, dir);
            if (!Physics.SphereCast (ray, settings.boundsRadius, settings.collisionAvoidDst, settings.obstacleMask)) {
                return dir;
            }
        }
        return direction;
    }


    void DrawGizmosSphereCast(){

        foreach(var ray in gizmoFishList){
            var des = ray.dir*settings.collisionAvoidDst;
            if(!ray.isHit){
              Gizmos.color = Color.green;
              Gizmos.DrawRay(ray.pos, des);
            }
            else{
                var pos = ray.pos + ray.dir*ray.collideDist;
                des = ray.dir*ray.collideDist;
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(pos,settings.boundsRadius);
                Gizmos.DrawRay(ray.pos,des);
            }
        }
        
      
    }

    void OnDrawGizmos(){
        if(debugFishCollideRay){
            DrawGizmosSphereCast();
            

            
        }
    }

    void UpdateBoidWihtJob()
    {
        if(boidsArray!=null&&boidsArray.Length>0){
            var fishDataArray = new NativeArray<Boid>(boidsArray.Length, Allocator.TempJob);
            for(var i = 0; i<boidsArray.Length;i++){
                fishDataArray[i] = boidsArray[i];

            }
            var job = new BoidUpdateJob{FishDataArray = fishDataArray};
            var jobHandle = job.Schedule(boidsArray.Length,32);
            jobHandle.Complete();
            fishDataArray.Dispose();
        }
    }

    bool IsUseSpatialDivide(){
        return SpatialDatas!=null?true:false;

    }
}

public struct BoidUpdateJob : IJobParallelFor{
    public NativeArray<FishFlocking.Boid> FishDataArray;
    public void Execute(int index){
        var data = FishDataArray[index];
        for(int i=0; i<10;i++){
            data.position *= 0.1f;
            FishDataArray[index] = data;
        }
    }

}

