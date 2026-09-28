using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TBTK{

	
	public class GridGenerator : MonoBehaviour {
		
		#if UNITY_EDITOR
		public static bool inspector=false;
		#endif
		
		
		public const float spaceXHex=0.75f;
		public const float spaceZHex=0.865f;
		
		[HideInInspector] [Space(8)] public Transform gridParent;
		
		[Space(8)]
		public Transform nodePrefabSq;
		public Transform nodePrefabHex;
		
		//~ [Space(8)]
		//~ [HideInInspector] public Transform obstaclePrefabSq;
		//~ [HideInInspector] public Transform obstaclePrefabHex;
		//~ [HideInInspector] public Transform wallPrefab;
		
		[Space(8)]
		public List<Transform> obstaclePrefabSqF;
		public List<Transform> obstaclePrefabSqH;
		public List<Transform> obstaclePrefabHexF;
		public List<Transform> obstaclePrefabHexH;
		public List<Transform> wallPrefabF;
		public List<Transform> wallPrefabH;
		
		[Space(8)]
		public Material matHexFog;
		public Material matSqFog;
		
		
		[HideInInspector] [Space(8)] public Transform gridObjParent;
		public static Transform GetGridObjParent(){
			if(instance.gridObjParent==null){
				instance.gridObjParent=new GameObject("GridItems").transform;
				instance.gridObjParent.parent=instance.transform.parent;
			}
			return instance.gridObjParent;
		}
		
		
		public static GridGenerator instance;
		
		public void Init(){ 
			instance=this;
		}
		
		
		[ContextMenu ("Clear")]
		public void Clear(){ DeleteGrid(); }

		//public int seed=10;
		
		[ContextMenu ("Generate")]
		public void Generate(){ Generate(null); }
		public void Generate(GridManager gm){
			if(gm==null) gm=gameObject.GetComponent<GridManager>();
			if(gm==null) return;
			
			Debug.Log("Generate Grid");
			instance=this;	gm.SetupInstance();	GridManager.InitNodeDirList();
			
			DeleteGrid();
			
			gm.CacheGridSetting();
			
			//Random.InitState(seed);
			
			if(gm.gridType==_GridType.SquareGrid) GenerateSquare(gm);
			if(gm.gridType==_GridType.HexGrid) GenerateHex(gm);
		}
		public void GenerateHex(GridManager gm){
			float offsetX=gm.GetOffsetX();//dimensionX*gm.gridSize/2f-gm.gridSize*.5f;
			float offsetZ=gm.GetOffsetZ();//dimensionZ*gm.gridSize/2f-gm.gridSize*.5f;
			
			float spaceX=spaceXHex*gm._GetNodeSize();//*GridToTileRatio;
			float spaceZ=spaceZHex*gm._GetNodeSize();//*GridToTileRatio;
			
			List<Node> allNodeList=new List<Node>();
			
			for(int x=0; x<gm.dimensionX; x++){
				float offsetHexZ=(x%2==1) ? 0 : (spaceZ/2);
				
				int limit=x%2==1 ? gm.dimensionZ : gm.dimensionZ-1;
				
				List<Node> list=new List<Node>();
				
				int count=0;
				for(int z=0; z<limit; z++){
					list.Add(new Node(count, x, z, new Vector3(x*spaceX, 0, z*spaceZ+offsetHexZ)-new Vector3(offsetX, 0, offsetZ)));
					count+=1;
					allNodeList.Add(list[z]);
					
					list[z].x=x;
					list[z].y=-((x+1)/2)+z;
					list[z].z=-(x/2)-z;
					
					list[z].cost=gm.defaultMoveCostPerNode;
				}
				gm.grid.Add(list);
				gm.gridT.Add(new ListWrapper(list));
			}
			
			GenerateCommon(gm, allNodeList);
		}
		public void GenerateSquare(GridManager gm){
			float offsetX=gm.GetOffsetX();//dimensionX*gm.gridSize/2f-gm.gridSize*.5f;
			float offsetZ=gm.GetOffsetZ();//dimensionZ*gm.gridSize/2f-gm.gridSize*.5f;
			
			//~ for(int i=0; i<deadSectionList.Count; i++){
				//~ if(deadSectionList[i].maxX==Mathf.Infinity) deadSectionList[i].maxX=deadSectionList[i].minX;
				//~ if(deadSectionList[i].maxZ==Mathf.Infinity) deadSectionList[i].maxZ=deadSectionList[i].minZ;
			//~ }
			
			List<Node> allNodeList=new List<Node>();
			
			int count=0;
			for(int x=0; x<gm.dimensionX; x++){
				List<Node> list=new List<Node>();
				for(int z=0; z<gm.dimensionZ; z++){
					list.Add(new Node(count, x, z, new Vector3(x, 0, z)*gm._GetNodeSize()-new Vector3(offsetX, 0, offsetZ)));
					count+=1;
					
					//~ for(int i=0; i<deadSectionList.Count; i++){
						//~ list[z].walkable=!deadSectionList[i].CheckBound(list[z]);
					//~ }
					allNodeList.Add(list[z]);
					
					list[z].cost=gm.defaultMoveCostPerNode;
				}
				gm.grid.Add(list);
				gm.gridT.Add(new ListWrapper(list));
			}
			
			GenerateCommon(gm, allNodeList);
		}
		private void GenerateCommon(GridManager gm, List<Node> allNodeList){
			for(int x=0; x<gm.grid.Count; x++){
				for(int z=0; z<gm.grid[x].Count; z++){
					gm.grid[x][z].SetupNeighbour();
				}
			}
			
			int unwalkableCount=(int)Mathf.Min(allNodeList.Count, gm.unwalkableRate*(gm.dimensionX*gm.dimensionZ));
			int loopCount=unwalkableCount*3;
			for(int i=0; i<loopCount; i++){
				if(allNodeList.Count<=0) break;
				int rand=Random.Range(0, allNodeList.Count);
				
				if(CheckAccessObstacle(allNodeList[rand])){
					allNodeList[rand].walkable=false;
					allNodeList.RemoveAt(rand);
					unwalkableCount-=1;
					if(unwalkableCount<=0) break;
				}
			}
			
			
			if(gridParent==null){
				gridParent=new GameObject("Grid").transform;
				gridParent.parent=transform.parent;
			}
			
			CombineMeshes comMesh=gridParent.gameObject.AddComponent<CombineMeshes>();
			comMesh.matHexFog=matHexFog;		comMesh.matSqFog=matSqFog;
			
			
			Transform prefab=(gm.gridType==_GridType.SquareGrid ? nodePrefabSq : nodePrefabHex);
			
			//Debug.Log("Generate common doing something to nodes' objT");
			
			for(int x=0; x<gm.grid.Count; x++){
				for(int z=0; z<gm.grid[x].Count; z++){
					Node node=gm.grid[x][z];
					//if(!node.walkable) continue;
					#if UNITY_EDITOR
						node.objT=(Transform)PrefabUtility.InstantiatePrefab(prefab);
						node.objT.position=node.pos;
					#else
						node.objT=(Transform)Instantiate(prefab, node.GetPos(), Quaternion.identity);
					#endif
					node.objT.transform.parent=gridParent;
					node.objT.transform.localScale*=gm.nodeSize*(gm.gridType==_GridType.HexGrid ? 1.16f : 1);
					#if UNITY_EDITOR
					node.objT.name=node.idxX+"x"+node.idxZ;//+"   "+Random.Range(0, 9999);
					#endif
					
					if(GridManager.UseMasterCollider()){
						DestroyImmediate(node.objT.GetComponent<Collider>());
					}
					
					node.objT.gameObject.GetComponent<Renderer>().enabled=node.walkable;
					//node.objT.gameObject.SetActive(node.walkable);
				}
			}
			
			//generate obstacle
			int obsCount=(int)(allNodeList.Count*Mathf.Min(gm.obstacleRate, 0.5f));
			loopCount=obsCount*3;
			for(int i=0; i<loopCount; i++){
				int rand=Rand.Range(0, allNodeList.Count);
				while(allNodeList[rand].HasObstacle()) rand=Rand.Range(0, allNodeList.Count);
				if(CheckAccessObstacle(allNodeList[rand])){
					Transform prefabO=null;		bool fullCover=Rand.value()<0.5f;
					
					if(GridManager.IsHexGrid()){
						if(fullCover) prefabO=obstaclePrefabHexF[Rand.Range(0, obstaclePrefabHexF.Count)];
						else prefabO=obstaclePrefabHexH[Rand.Range(0, obstaclePrefabHexH.Count)];
					}
					else{
						if(fullCover) prefabO=obstaclePrefabSqF[Rand.Range(0, obstaclePrefabSqF.Count)];
						else prefabO=obstaclePrefabSqH[Rand.Range(0, obstaclePrefabSqH.Count)];
					}
					
					allNodeList[rand].AddObstacle(prefabO);//GridManager.IsHexGrid() ? obstaclePrefabHex : obstaclePrefabSq);
					obsCount-=1;
					if(obsCount<=0) break;
				}
			}
			
			//generate wall
			int wallCount=(int)(allNodeList.Count*Mathf.Min(gm.wallRate, 0.75f));	//Debug.Log("wallcount: "+wallCount);
			loopCount=wallCount*3;
			for(int i=0; i<loopCount; i++){
				int rand=Rand.Range(0, allNodeList.Count);
				while(allNodeList[rand].HasObstacle()) rand=Rand.Range(0, allNodeList.Count);
					
				Transform prefabW=null;		bool fullCover=Rand.value()<0.5f;
				
				if(fullCover) prefabW=wallPrefabF[Rand.Range(0, wallPrefabF.Count)];
				else prefabW=wallPrefabH[Rand.Range(0, wallPrefabH.Count)];
				
				int dirIdx=Random.Range(0, GridManager.nodeDirList.Count);
				if(AddWall(allNodeList[rand], prefabW)){
					//~ if(Rand.value()<0.75f-(GridManager.IsSquareGrid() ? 0.6f : 0)){
					if(true){//Rand.value()<0.75f-(GridManager.IsSquareGrid() ? 0.6f : 0)){
						dirIdx=(dirIdx+1)%GridManager.nodeDirList.Count;
						bool flag=AddWall(allNodeList[rand], prefabW);
						
						if(flag && Rand.value()<0.5f-(GridManager.IsSquareGrid() ? 0.6f : 0.2f)){
							dirIdx=(dirIdx+1)%GridManager.nodeDirList.Count;
							AddWall(allNodeList[rand], prefabW);
						}
					}
					
					wallCount-=1;
					if(wallCount<=0) break;
				}
				//else Debug.Log("add wall failed  "+wallCount+"   "+i);
				
				//~ int dirIdx=Random.Range(0, GridManager.nodeDirList.Count);	//Debug.Log(i+"   "+GridManager.nodeDirList[dirIdx]);
				//~ allNodeList[rand].AddWall(allNodeList[rand].GetPos()+GridManager.nodeDirList[dirIdx], wallPrefab);
				
				//~ if(Rand.value()<0.75f){
					//~ dirIdx=(dirIdx+1)%GridManager.nodeDirList.Count;	//Debug.Log(i+"   "+GridManager.nodeDirList[dirIdx]);
					//~ allNodeList[rand].AddWall(allNodeList[rand].GetPos()+GridManager.nodeDirList[dirIdx], wallPrefab);
					
					//~ if(Rand.value()<0.5f){
						//~ dirIdx=(dirIdx+1)%GridManager.nodeDirList.Count;	//Debug.Log(i+"   "+GridManager.nodeDirList[dirIdx]);
						//~ allNodeList[rand].AddWall(allNodeList[rand].GetPos()+GridManager.nodeDirList[dirIdx], wallPrefab);
					//~ }
				//~ }
			}
			
			
			//PlaceDeployAndSpawnPoint();
			
			UnitManager um=(UnitManager)FindObjectOfType(typeof(UnitManager));
			
			for(int i=0; i<um.factionList.Count; i++){
				Faction fac=um.factionList[i];
				for(int n=0; n<fac.deploymentPointList.Count; n++){
					Node node=GridManager.GetNode(fac.deploymentPointList[n], null);
					if(node!=null) node.deployFacID=fac.factionID;
					//if(node!=null && node.walkable && !node.HasObstacle()) node.deployFacID=fac.factionID;
				}
				
				for(int n=0; n<fac.spawnGroupList.Count; n++){
					for(int j=0; j<fac.spawnGroupList[n].spawnPointList.Count; j++){
						Node node=GridManager.GetNode(fac.spawnGroupList[n].spawnPointList[j], null);
						if(node!=null) node.SetSpawnGroup(fac.factionID, fac.spawnGroupList[n].ID);
						//if(node!=null && node.walkable && !node.HasObstacle()) node.SetSpawnGroup(fac.factionID, fac.spawnGroupList[n].ID);
					}
				}
			}
			
			gm.SetupMasterCollider();
		}
		
		
		private bool AddWall(Node node, Transform wallPrefab, bool flag=true){
			List<Node> neighbourList=node.GetNeighbourList(true);
			if(neighbourList.Count<=1){
				//Debug.Log("node has no neighbour  "+node.objT.name+"    "+node.GetNeighbourList(false).Count);
				return false;
			}
			
			Node neighbour=neighbourList[Rand.Range(0, neighbourList.Count)];
			if(neighbour==null) return false;
			
			node.AddWall(node.GetPos()+(neighbour.GetPos()-node.GetPos()), wallPrefab);
			
			//~ for(int i=0; i<neighbourList.Count; i++){
				//~ if(neighbourList[i]==node) continue;
				//~ if(!neighbourList[i].walkable || neighbourList[i].HasObstacle()) continue;
				//~ List<Node> path=AStar.SearchWalkableNode(node, neighbourList[i], false);
				//~ if(path.Count==0){ flag=false; break; }
			//~ }
			
			//~ if(!flag) node.RemoveWall(neighbour.GetPos()-node.GetPos());
			
			List<Node> path=AStar.SearchWalkableNode(node, neighbour, AStar.BypassUnitCode(false));
			if(path.Count<=0) node.RemoveWall(node.GetPos()+(neighbour.GetPos()-node.GetPos())); 
			
			return path.Count>0;
						
						//~ if(!flag){
							//~ node.RemoveWall(node.GetPos()+(neighbour.GetPos()-node.GetPos()));
							//~ Debug.Log("no cant do");
						//~ }
			
			//~ return flag;
		}
		private bool CheckAccessObstacle(Node node, bool flag=true, int sIdx=0){
			List<Node> neighbours=node.GetNeighbourList();	
			
			bool cachedWalkable=node.walkable;	node.walkable=false;
			
			for(int i=0; i<neighbours.Count; i++){
				if(neighbours[i].walkable && !neighbours[i].HasObstacle()){ sIdx=i; break; }
			}
			
			for(int i=0; i<neighbours.Count; i++){
				if(i==sIdx) continue;
				if(!neighbours[i].walkable || neighbours[i].HasObstacle()) continue;
				List<Node> path=AStar.SearchWalkableNode(neighbours[sIdx], neighbours[i], AStar.BypassUnitCode(false));
				if(path.Count==0){ flag=false; break; }
			}
			
			node.walkable=cachedWalkable;
			
			return flag;
		}
		
		
		
		public void DeleteGrid(){
			ClearUnit();
			ClearCollectible();
			
			GridManager gm=gameObject.GetComponent<GridManager>();
			if(gm==null) return;
			
			CacheDeploymentAndSpawnArea(gm);
			
			for(int x=0; x<gm.grid.Count; x++){
				if(gm.grid[x]==null) continue;
				gm.grid[x].Clear();
			}
			gm.grid.Clear();
			gm.gridT.Clear();
			
			if(gridParent!=null) DestroyImmediate(gridParent.gameObject);
			if(gridObjParent!=null) DestroyImmediate(gridObjParent.gameObject);
		}
		
		
		
		public void CacheDeploymentAndSpawnArea(GridManager gm){
			UnitManager um=(UnitManager)FindObjectOfType(typeof(UnitManager));
			
			for(int i=0; i<um.factionList.Count; i++){
				um.factionList[i].deploymentPointList.Clear();
				for(int n=0; n<um.factionList[i].spawnGroupList.Count; n++) um.factionList[i].spawnGroupList[n].spawnPointList.Clear();
			}
			
			
			for(int x=0; x<gm.grid.Count; x++){
				for(int z=0; z<gm.grid[x].Count; z++){
					if(gm.grid[x][z].deployFacID>=0){
						for(int i=0; i<um.factionList.Count; i++){
							if(um.factionList[i].factionID==gm.grid[x][z].deployFacID){
								um.factionList[i].deploymentPointList.Add(gm.grid[x][z].GetPos());
								break;
							}
						}
					}
					
					
					if(gm.grid[x][z].spawnGroupFacID>=0){
						for(int i=0; i<um.factionList.Count; i++){
							if(um.factionList[i].factionID==gm.grid[x][z].spawnGroupFacID){
								for(int n=0; n<um.factionList[i].spawnGroupList.Count; n++){
									if(um.factionList[i].spawnGroupList[n].ID==gm.grid[x][z].spawnGroupID){
										um.factionList[i].spawnGroupList[n].spawnPointList.Add(gm.grid[x][z].GetPos());
										break;
									}
								}
								break;
							}
						}
					}
				}
			}
		}
		public void ClearInvalidDeploymentAndSpawnArea(){
			GridManager gm=gameObject.GetComponent<GridManager>();
			if(gm==null) return;
			
			for(int x=0; x<gm.grid.Count; x++){
				for(int z=0; z<gm.grid[x].Count; z++){
					if(gm.grid[x][z].deployFacID>=0){
						if(!gm.grid[x][z].walkable || gm.grid[x][z].HasObstacle()) gm.grid[x][z].ClearDeploymentFac();
					}
					if(gm.grid[x][z].spawnGroupFacID>=0){
						if(!gm.grid[x][z].walkable || gm.grid[x][z].HasObstacle()) gm.grid[x][z].ClearSpawnGroup();
					}
				}
			}
		}
		
		
		//~ public static Node GetNode(int x, int z){ 
			//~ //Debug.Log(x+","+z+"    "+instance.grid.Count+"  "+instance.nList.Count);
			//~ //return instance.nList[x].myList[z];
			//~ return instance.grid[x][z];
		//~ }
		
		//~ public static Node GetNode(Vector3 point){
			//~ Vector2 coor=new Vector2(point.x, point.z)/GridGenerator.GetNodeSize();
			//~ return GetNode((int)Mathf.Round(coor.x), (int)Mathf.Round(coor.y));
		//~ }
		
		
		public void GenerateUnit(){
			UnitManager um=ClearUnit();
			if(um==null) return; 
			
			for(int i=0; i<um.factionList.Count; i++){
				for(int n=0; n<um.factionList[i].spawnGroupList.Count; n++){
					um.factionList[i].spawnGroupList[n].Spawn(um.factionList[i]);
				}
			}
		}
		public UnitManager ClearUnit(){
			GridManager gm=gameObject.GetComponent<GridManager>();
			UnitManager um=(UnitManager)FindObjectOfType(typeof(UnitManager));
			
			if(um==null){ Debug.LogWarning("No UnitManager in the scene"); return null; }
			
			for(int x=0; x<gm.grid.Count; x++){
				for(int z=0; z<gm.grid[x].Count; z++){
					if(gm.grid[x][z].unit==null) continue;
					DestroyImmediate(gm.grid[x][z].unit.gameObject);
				}
			}
			
			for(int i=0; i<um.factionList.Count; i++) um.factionList[i].unitList.Clear();
			
			return um;
		}
		
		
		public void GenerateCollectible(){
			CollectibleManager cm=ClearCollectible();
			if(cm==null) return; 
			
			cm.Generate();
		}
		public CollectibleManager ClearCollectible(){
			CollectibleManager cm=(CollectibleManager)FindObjectOfType(typeof(CollectibleManager));
			if(cm==null){ Debug.LogWarning("No CollectibleManager in the scene"); return null; }
			
			for(int i=0; i<cm.activeItemList.Count; i++) DestroyImmediate(cm.activeItemList[i].gameObject);
			cm.activeItemList.Clear();
			
			return cm;
		}
		
		
	}
	
	
	
	[System.Serializable]
	public class ListWrapper{
		public List<Node> list;
		public ListWrapper(List<Node> l){ list=l; }
	}
	
	
	
	
	
	
	//~ [System.Serializable]
	//~ public class Boundary{
		//~ public int minX=0;	public int maxX=0;
		//~ public int minZ=0;	public int maxZ=0;
		
		//~ public bool CheckBound(Node node){
			//~ Verify();
			//~ if(node.idxX<=minX || node.idxX>=maxX) return false;
			//~ if(node.idxZ<=minZ || node.idxZ>=maxZ) return false;
			//~ return true;
		//~ }
		//~ public void Verify(){
			//~ if(maxX<minX) maxX=minX;
			//~ if(maxZ<minZ) maxZ=minZ;
		//~ }
	//~ }

}