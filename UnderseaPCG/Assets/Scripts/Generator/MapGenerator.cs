using UnityEngine;
using System.Collections;
using System;

[ExecuteInEditMode]
public class MapGenerator : MonoBehaviour {

	public int width;
	public int height;

	public int seed;
	public bool useRandomSeed;

	public int smoothSteps = 5;

	public bool drawGizmos = true;

	[Range(0,100)]
	public int densityFill;
	[Range(0,100)]
	public int decorationFill;

	[HideInInspector]
	public int[,] densityMap;
	[HideInInspector]
	public int[,] reefMap;

	bool settingsUpdated = false;


	void Start() {
		
	}



	void Update() {

		if(settingsUpdated) {
			GenerateDensityMap();
			GenerateReefMap();
			settingsUpdated = false;
		}
	}
	void OnValidate() {
        settingsUpdated = true;
    }

	// map for density, like hills, mountains, etc. higher density means higher elevation
	void GenerateDensityMap() {
		densityMap = new int[width,height];
		RandomFillMap(ref densityMap,seed,densityFill);

		for (int i = 0; i < smoothSteps; i ++) {
			SmoothMap(ref densityMap);
		}
	}

	// map for decoration, like rocks, corals, etc.
	void GenerateReefMap() {
		reefMap = new int[width,height];
		RandomFillMap(ref reefMap,seed+1,decorationFill);

		for (int i = 0; i < smoothSteps; i ++) {
			SmoothMap(ref reefMap);
		}
	}

	// fill the map with random values, 1 for occupied, 0 for empty
	void RandomFillMap(ref int[,] map, int seed = 0, int fillthreshold = 50) {
		if (useRandomSeed) {
			seed = (int)Time.time;
		}

		System.Random pseudoRandom = new System.Random(seed);

		for (int x = 0; x < width; x ++) {
			for (int y = 0; y < height; y ++) {
				if (x == 0 || x == width-1 || y == 0 || y == height -1) {
					map[x,y] = 1;
				}
				else {
					map[x,y] = (pseudoRandom.Next(0,100) < fillthreshold)? 1: 0;
				}
			}
		}
	}

	// cellular automata
	void SmoothMap(ref int[,] map) {
		int[,] t_map = map;
		for (int x = 0; x < width; x ++) {
			for (int y = 0; y < height; y ++) {
				int neighbourWallTiles = GetSurroundingWallCount(map ,x,y);
					
				if (neighbourWallTiles > 4)
					t_map[x,y] = 1;
				else if (neighbourWallTiles < 4)
					t_map[x,y] = 0;

			}
		}
		map = t_map;
	}

	// count the number of occupied grids around the current grid
	int GetSurroundingWallCount(int[,] map, int gridX, int gridY) {
		int wallCount = 0;
		for (int neighbourX = gridX - 1; neighbourX <= gridX + 1; neighbourX ++) {
			for (int neighbourY = gridY - 1; neighbourY <= gridY + 1; neighbourY ++) {
				if (neighbourX >= 0 && neighbourX < width && neighbourY >= 0 && neighbourY < height) {
					if (neighbourX != gridX || neighbourY != gridY) {
						wallCount += map[neighbourX,neighbourY];
					}
				}
				else {
					wallCount ++;
				}
			}
		}

		return wallCount;
	}

	public int[,] getDecorationMap(){
		int[,] dmap=densityMap;
		int[,] rmap=reefMap;
		int[,] map = new int[width,height];
		for(int x = 0; x < width; x++){
			for(int y = 0; y < height; y++){
				if(rmap[x,y] == 1 && dmap[x,y] == 0){
					map[x,y] = UnityEngine.Random.Range(0,5)>3?1:0;
				}
			}
		}
		return map;
	}

	public void GenerateMap(){
		GenerateDensityMap();
		GenerateReefMap();
	
	}


	void OnDrawGizmos() {
		if (!drawGizmos) {
			return;
		}
		int[,] dmap=densityMap;
		int[,] rmap=reefMap;
		if (dmap != null) {
			for (int x = 0; x < width; x ++) {
				for (int y = 0; y < height; y ++) {
					if(dmap[x,y] == 0 && rmap[x,y] == 1)
						Gizmos.color = new Color(0.5f,0.6f,0,1);
					else if(dmap[x,y] == 1)
						Gizmos.color = Color.black;
					else
						Gizmos.color = Color.white;
					
					Vector3 pos = new Vector3(-width/2 + x + .5f,0, -height/2 + y+.5f);
					Gizmos.DrawCube(pos,Vector3.one);
				}
			}
		}
	}

}
