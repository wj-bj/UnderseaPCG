using System;
using System.Collections;
using System.Collections.Generic;
using BoidMobile;
using Unity.Collections;
using UnityEngine;

[ExecuteAlways]
public class SpatialCube : MonoBehaviour
{
    public class SpatialCubeData{
        public Vector3 pos;
        public int count;
        public List<int> indices;
        public List<int> surrondIndices;
        public SpatialCubeData(){
            indices = new List<int>(512);
            surrondIndices = new List<int>(512);
        }
        
    }

    public struct NativeSpatialData
    {
        public int fishCount;
        public int gridCount;
        public NativeArray<unitLookup> lookup;
        public NativeArray<int> fishArray;
    }

    public struct unitLookup
    {
        public int startIndex, count;
    }
    
    public Vector3 unitSize = Vector3.one;
    //Bounds bound;
    Bounds[] boundsArray;
    public Vector3Int Size = Vector3Int.one;

    int totalGrids = 0;

    public SpatialCubeData[] Datas;

    private Vector3 startPos = Vector3.zero;

    public bool debugGrid = true;

    #if UNITY_EDITOR
    private Vector3 lastPostion;
    #endif

    public NativeSpatialData NativeFishSpaticalData;
    
    // Start is called before the first frame update
    void Start()
    {
        var center =gameObject.transform.position;
        
       // bound.SetMinMax(center-Size,center+Size);

    }

    void Awake(){
        #if UNITY_EDITOR
            lastPostion = transform.position;
        #endif
        var total = Size.x*Size.y*Size.z;
        boundsArray = new Bounds[total];
        Datas = new SpatialCubeData[total];
        for(int i=0; i<Datas.Length;i++){
           Datas[i] = new SpatialCubeData();
        }
        updateInfo();
    }

    // Update is called once per frame
    void Update()
    {
        #if UNITY_EDITOR
        if(!lastPostion.Equals(transform.position)){
            lastPostion = transform.position;
            updateInfo();
        }
        #endif
       
    }

    void OnDrawGizmos(){
        if(debugGrid == true)
            Draw();
    }

    public void Draw(){
        if(boundsArray==null || Datas == null){
            return;
        }
        Gizmos.color = Color.green;
        for(int i = 0; i<boundsArray.Length;i++){
            if(Datas[i].count>0){
                Gizmos.color = new Color(1.0f,0,0,0.2f);   
                var bound = boundsArray[i];           
                Gizmos.DrawCube(bound.center,bound.size);
               
            }
                
            else{
                Gizmos.color = Color.green;
                var bound = boundsArray[i];           
                Gizmos.DrawWireCube(bound.center,bound.size);
                
            }
            
        }
        
    }
    public void OnValidate(){
        var center =gameObject.transform.position;
        updateInfo();
  
    }

    int getGridIndex(int x, int y, int z){
        return x + Size.x*y+ Size.x*Size.y*z;
    }

    public void updateInfo(){
        var center =gameObject.transform.position;
         var total = Size.x*Size.y*Size.z;
         if(totalGrids!= total){
                boundsArray = new Bounds[total];
                Datas = new SpatialCubeData[total];
                totalGrids = total;
                for(int i=0; i<Datas.Length;i++){
                    Datas[i] = new SpatialCubeData();
                }
         }
         float startX = 0f;
         float startY = 0f;
         float startZ = 0f;

         if(Size.x%2 != 0)
             startX = center.x - Size.x/2*unitSize.x;
         else
             startX = center.x - Size.x/2*unitSize.x + unitSize.x/2;

        if(Size.y%2 != 0)
             startY = center.y - Size.y/2*unitSize.y;
         else
             startY = center.y - Size.y/2*unitSize.y + unitSize.y/2;

        if(Size.z%2 != 0)
             startZ = center.z - Size.z/2*unitSize.z;
         else
             startZ = center.z - Size.z/2*unitSize.z+ unitSize.z/2;

        for(int z =0; z< Size.z; z++){
            for(int y =0; y<Size.y; y++){
                for(int x =0; x< Size.x; x++){
                    var index = getGridIndex(x,y,z);
                    var bound = boundsArray[index];
                    var pos = new Vector3(startX+x*unitSize.x,startY+y*unitSize.y, startZ+z*unitSize.z);
                    bound.center = pos;
                    bound.size = unitSize;
                    boundsArray[index] = bound;
                }
            }
        }

        startPos = new Vector3(startX,startY,startZ);

    }

    //private FishFlocking.Boid[] test;
    public void UpdateSpatialInfo(FishFlocking.Boid[] array){
        for(int i = 0; i<Datas.Length;i++){
            //Debug.Log($"{i}:{Datas[i].surrondIndices.Count}");
            Datas[i].count = 0;
            Datas[i].indices.Clear();
            Datas[i].surrondIndices.Clear();
        }
        for(int i =0; i<array.Length;i++){
            var boid = array[i];
            Vector3Int grid = GetParticleGrid(boid.position);
            var index = getGridIndex(grid.x,grid.y,grid.z); 
            //var index = GetParticleGridIndex(boid.position);  
            index =Mathf.Max(index,0);
            index =Mathf.Min(index,Datas.Length-1);
            Datas[index].count++;
            Datas[index].indices.Add(i);
            // var x = grid.x;
            // var y = grid.y;
            // var z = grid.z;
            // for(int ii= x-1; ii<=x+1; ii++)
            // {
            //     for(int jj = y-1; jj<=y+1; jj++)
            //     {
            //         for(int kk =z-1; kk<=z+1; kk++)
            //         {
            //             if(ii<0||ii>=Size.x||jj<0||jj>=Size.y||kk<0||kk>=Size.z)
            //             {
                            
            //             }
            //             else
            //             {
            //                 var index1 = getGridIndex(ii,jj,kk);
            //                 Datas[index1].surrondIndices.Add(i);
            //             }
            //         }
            //     }
            
            // } 

        }

    }
    
    public void UpdateSpatialInfo(FishFlockingMobile.Boid[] array){
        for(int i = 0; i<Datas.Length;i++){
            //Debug.Log($"{i}:{Datas[i].surrondIndices.Count}");
            Datas[i].count = 0;
            Datas[i].indices.Clear();
            Datas[i].surrondIndices.Clear();
        }
        for(int i =0; i<array.Length;i++){
            var boid = array[i];
            Vector3Int grid = GetParticleGrid(boid.position);
            var index = getGridIndex(grid.x,grid.y,grid.z); 
            //var index = GetParticleGridIndex(boid.position);  
            index =Mathf.Max(index,0);
            index =Mathf.Min(index,Datas.Length-1);
            Datas[index].count++;
            Datas[index].indices.Add(i);

        }

    }

    int GetParticleGridIndex(Vector3 pos, float diameter = 1f) 
    {
    
        var start = startPos - new Vector3(unitSize.x,unitSize.y,unitSize.z)/2;
	    Vector3 gridLocation = (pos - start);
        gridLocation.x = gridLocation.x/unitSize.x;
        gridLocation.y = gridLocation.y/unitSize.y;
        gridLocation.z = gridLocation.z/unitSize.z;

        int x = Mathf.FloorToInt(gridLocation.x);
        int y = Mathf.FloorToInt(gridLocation.y);
        int z = Mathf.FloorToInt(gridLocation.z);
	    return getGridIndex(x,y,z);
    }

    Vector3Int GetParticleGrid(Vector3 pos, float diameter = 1f) 
    {
    
        var start = startPos - new Vector3(unitSize.x,unitSize.y,unitSize.z)/2;
	    Vector3 gridLocation = (pos - start);
        gridLocation.x = gridLocation.x/unitSize.x;
        gridLocation.y = gridLocation.y/unitSize.y;
        gridLocation.z = gridLocation.z/unitSize.z;

        int x = Mathf.FloorToInt(gridLocation.x);
        int y = Mathf.FloorToInt(gridLocation.y);
        int z = Mathf.FloorToInt(gridLocation.z);
	    return new Vector3Int(x,y,z);
    }

    Vector3Int GetGridByIndex(int index){
        Vector3Int pos = Vector3Int.zero;
        var area = Size.x*Size.y;
        pos.z = index/area;
        var temp = index%area;
        pos.y =  temp/Size.x;
        pos.x = temp%Size.x;
        return pos;
    }

    public void UpdateNativeFishSpaticalData(FishFlockingMobile.Boid[] array)
    {
        if (NativeFishSpaticalData.fishCount < 1||NativeFishSpaticalData.gridCount!= Size.x*Size.y*Size.z)
        {
            var gridCount = Size.x * Size.y * Size.z;
            NativeFishSpaticalData.fishArray = new NativeArray<int>(array.Length, Allocator.Persistent); 
            NativeFishSpaticalData.lookup = new NativeArray<unitLookup>(gridCount, Allocator.Persistent);
            NativeFishSpaticalData.fishCount = array.Length;
            NativeFishSpaticalData.gridCount = gridCount;
        }

        var fishArray = NativeFishSpaticalData.fishArray;
        var lookup = NativeFishSpaticalData.lookup;
        var currentIndex = 0;
        for(int i = 0; i<Datas.Length;i++)
        {
            unitLookup data = new unitLookup {startIndex = currentIndex, count = Datas[i].count};
            var indice = Datas[i].indices;
            for (int j = 0; j < indice.Count; j++)
            {
                fishArray[currentIndex] = indice[j];
                currentIndex++;
            }
            lookup[i] = data;
        }
        
    }

    void OnDestroy()
    {
        if (NativeFishSpaticalData.fishCount>0 )
        {
            NativeFishSpaticalData.fishArray.Dispose();
            NativeFishSpaticalData.lookup.Dispose();
    
        }
    }
}
