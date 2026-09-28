using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TBTK{
	
	[System.Serializable]
	public class Wall{
		public Transform objT;
		
		public int nIdx1_x;	public int nIdx2_x;
		public int nIdx1_z;	public int nIdx2_z;
		
		[System.NonSerialized] public Node node1;
		[System.NonSerialized] public Node node2;
		
		public float angle1;	//from n1 to n2
		public float angle2;	//from n2 to n1
		
		public Wall(Transform prefab, Node n1, Node n2){
			node1=n1; node2=n2;	
			nIdx1_x=node1.idxX;	nIdx2_x=node2.idxX;
			nIdx1_z=node1.idxZ;	nIdx2_z=node2.idxZ;
			angle1=GridManager.GetAngle(n1.GetPos(), n2.GetPos(), true);
			angle2=GridManager.GetAngle(n1.GetPos(), n2.GetPos(), true);
			
			Vector3 pos=(n1.GetPos()+n2.GetPos())/2;
			Quaternion rot=Quaternion.LookRotation(n1.GetPos()-n2.GetPos());
			
			#if UNITY_EDITOR
				objT=(Transform)PrefabUtility.InstantiatePrefab(prefab);
				objT.position=pos;	objT.rotation=rot;
			#else
				objT=(Transform)MonoBehaviour.Instantiate(prefab, pos, rot);
			#endif
			
			objT.transform.localScale*=GridManager.GetNodeSize();
			//objT.transform.parent=node1.objT;
			objT.transform.parent=GridGenerator.GetGridObjParent();
			objT.transform.name=node1.GetObjT().name+"_"+objT.transform.name;
			
			if(GridManager.IsHexGrid()){
				objT.transform.localScale=new Vector3(objT.transform.localScale.x*0.5f, objT.transform.localScale.y, objT.transform.localScale.x*0.5f);
			}
		}
		
		public void SetupNode(){
			if(node1==null) node1=GridManager.GetNode(nIdx1_x, nIdx1_z);
			if(node2==null) node2=GridManager.GetNode(nIdx2_x, nIdx2_z);
		}
		
		public float GetAngle1(Node node){
			if(node==node1) return angle1;
			else if(node==node2) return angle2;
			return 0;
		}
		
		public bool CheckContainNode(Node node){
			return node1==node | node2==node;
		}
		
		public void Remove(){
			node1.wallList.Remove(this);
			node2.wallList.Remove(this);
			if(objT!=null) MonoBehaviour.DestroyImmediate(objT.gameObject);
			else Debug.Log("wall has no obj? error?");
		}
	}
	
	[System.Serializable]
	public class Node{
		public int idx;		public int idxX;	public int idxZ;
		public int x;		public int y;		public int z;		//for hex node, to calculate distance
		public Transform objT;
		public Renderer rend;
		
		public Transform GetObjT(){ return objT; }
		
		public bool walkable=true;
		
		public float cost=1;
		
		public Vector3 pos;
		public Vector3 GetPos(){ return objT!=null ? objT.position : pos ; }
		
		public Transform obstacleT;
		
		public Unit unit;
		
		public Collectible collectible;
		
		[HideInInspector] public Node abLineParent;		//for line type ability, refering to the final node in the line
		
		public bool IsEmpty(){	//for checking before placing any item 
			return unit==null && collectible==null && !HasObstacle();
		}
		
		//~ public int floorID=-1;
		//~ public int wallID=-1;
		
		//~ public FloorPrefab floorPrefab;
		//~ public void InitiateFloorPrefab(){ if(walkable && objT!=null) floorPrefab=objT.GetComponent<FloorPrefab>(); }
		//~ public bool CanPlaceHostile(){ return floorPrefab!=null ? floorPrefab.enableHostile : false ; }
		//~ public bool CanPlaceCollectible(){ return floorPrefab!=null ? floorPrefab.enableCollectible : false ; }
		//~ public bool CanPlaceDTerrain(){ return floorPrefab!=null ? floorPrefab.enableDTerrain : false ; }
		
		
		public Node(int i, int iX, int iZ, Vector3 p){
			idx=i; idxX=iX; idxZ=iZ; 
			pos=p;
		}
		
		public void DeleteObject(){
			if(objT==null) return;
			MonoBehaviour.DestroyImmediate(objT.gameObject);
		}
		
		//public bool IsBlocked(Node node, bool ignoreUnit=false, bool ignoreObs=false){ 
		public bool IsBlocked(Node node, int ignoreUnit=-1, bool ignoreObs=false){ 
			if(!walkable) return true;
			
			if(!ignoreObs && obstacleT!=null) return true;
			//if(!ignoreUnit && unit!=null) return true;
			
			if(AStar.CantBypassUnit(ignoreUnit) && unit!=null) return true;
			if(AStar.BypassFriendlyUnit(ignoreUnit) && unit!=null && unit.facID!=ignoreUnit) return true;
			
			//check for wall 
			if(!ignoreObs){
				for (int i=0; i<wallList.Count; i++){
					if(wallList[i].CheckContainNode(node)) return true; 
				}
			}
			
			if(GridManager.IsSquareGrid() && GridManager.EnableDiagonalNeighbour()){
				if(GetPos().x!=node.GetPos().x && GetPos().z!=node.GetPos().z){
					List<Node> tgtNeighbours=node.GetNeighbourList();
					List<Node> adjNeighbours=new List<Node>();
					for(int i=0; i<tgtNeighbours.Count; i++){
						if(neighbours.Contains(tgtNeighbours[i])) adjNeighbours.Add(tgtNeighbours[i]);
					}
					
					//Debug.Log("IsBlocked  "+adjNeighbours.Count);
					
					int blockedCount=0;
					for(int i=0; i<adjNeighbours.Count; i++){
						if(adjNeighbours[i].IsBlocked(this, ignoreUnit, ignoreObs)) blockedCount+=1;
					}
					return blockedCount==adjNeighbours.Count;
				}
			}
			
			return false;
		}
		
		
		
		
		
		public bool HasObstacle(){ return obstacleT!=null; }
		public void RemoveObstacle(){
			MonoBehaviour.DestroyImmediate(obstacleT.gameObject);
		}
		public void AddObstacle(Transform prefab){
			if(obstacleT!=null) RemoveObstacle();
			
			#if UNITY_EDITOR
				obstacleT=(Transform)PrefabUtility.InstantiatePrefab(prefab);
				obstacleT.position=GetPos();
			#else
				obstacleT=(Transform)MonoBehaviour.Instantiate(prefab, GetPos(), Quaternion.identity);
			#endif
			
			obstacleT.transform.localScale*=GridManager.GetNodeSize();
			//obstacleT.transform.parent=objT;
			obstacleT.transform.parent=GridGenerator.GetGridObjParent();
			obstacleT.transform.name=objT.name+"_"+obstacleT.transform.name;
		}
		
		
		#region wall
		public List<Wall> wallList=new List<Wall>();
		
		public void SetupWall(){
			for(int i=0; i<wallList.Count; i++) wallList[i].SetupNode();
		}
		
		public int CheckWall(Vector3 point){
			Node neighbour=GetNeighbourFromPos(point);
			if(neighbour==null) return -100;
			if(neighbour.HasObstacle()) return -100;
			
			for (int i=0; i<wallList.Count; i++){
				if(wallList[i].CheckContainNode(neighbour)) return i; 
				//if(wallList[i].GetAngle(this)==angle) return false; 
			}
			
			return -1;
		}
		public void AddWall(Vector3 point, Transform prefab){
			if(CheckWall(point)==-1){
				Node neighbour=GetNeighbourFromPos(point);
				Wall wall=new Wall(prefab, this, neighbour);
				wallList.Add(wall);	neighbour.wallList.Add(wall);
			}
		}
		public void RemoveWall(Vector3 point){
			int idx=CheckWall(point);
			if(idx>=0) wallList[idx].Remove();
		}
		
		public bool HasWall(){ return wallList.Count>0; }
		#endregion
		
		
		#region LOS/Fog-of-War
		private bool visible=true;
		private int scanned=0;
		
		private MeshRenderer objRend;
		
		public bool IsVisible(bool debug=false){ 
			if(debug) Debug.Log(" ----------------------  "+visible+"   "+scanned+"   "+objT.gameObject.activeInHierarchy);
			return !GameControl.EnableFogOfWar() | visible | scanned>0 ; }
		
		public void SetVisible(bool flag){
			visible=flag;
			
			//if(rend==null) rend=objT.gameObject.GetComponent<Renderer>();
			//rend.enabled=visible;
			
			UpdateVisibility();
		}
		public void UpdateVisibility(){
			if(objT==null) Debug.Log("objT is null   "+idxX+"   "+idxZ);
			
			if(GridManager.UseIndividualCollider()){
				if(objRend==null) objRend=objT.GetComponent<MeshRenderer>();
				objRend.enabled=IsVisible();
			}
			else{
				if(objT!=null) objT.gameObject.SetActive(IsVisible());
			}
			
			if(unit!=null){
				Utility.SetLayerRecursively(unit.transform, IsVisible() ? TBTK.GetLayerUnit() : TBTK.GetLayerInvisible());
			}
			if(collectible!=null){
				Utility.SetLayerRecursively(collectible.transform, IsVisible() ? 0 : TBTK.GetLayerInvisible());
			}
		}
		
		
		public void RevealFogOfWar(int duration){
			GridManager.AddScannedNode(this);
			scanned=Mathf.Max(scanned, duration);
			if(!visible) UpdateVisibility();
		}
		public bool IterateFogOfWarCD(){
			scanned-=1;
			if(scanned<=0) UpdateVisibility();
			return scanned<=0;
		}
		#endregion
		
		
		#region cover
		public List<int> coverList=new List<int>();
		
		public void InitCover(){
			coverList=new List<int>{ 0, 0, 0, 0 };
			if(GridManager.IsHexGrid()){ coverList.Add(0);	coverList.Add(0); }
			
			Vector3 offset=new Vector3(0, 0.1f, 0);
			
			int count=GridManager.IsHexGrid() ? 6 : 4 ;
			int angleStart=GridManager.IsHexGrid() ? 30 : 0 ;
			int angleStep=GridManager.IsHexGrid() ? 60 : 90 ;
			
			for(int i=0; i<count; i++){
				int angle=angleStart+(i*angleStep);
				Node neighbour=GetNeighbourOfAngle(angle);
				
				if(neighbour==null) continue;
				if(!neighbour.IsBlocked(this)) continue;
				
				Vector3 dir=(neighbour.GetPos()-GetPos()).normalized;
				//float dist=GridManager.GetNodeSize();//*GridManager.GetGridToTileSizeRatio()*.75f;
				LayerMask mask=1<<TBTK.GetLayerObsFullCover() | 1<<TBTK.GetLayerObsHalfCover();	RaycastHit hit;
				
				bool flag=Physics.Raycast(GetPos()+offset, dir, out hit, GridManager.GetNodeSize(), mask);
				
				if(flag) coverList[i]=hit.transform.gameObject.layer==TBTK.GetLayerObsFullCover() ? 2 : 1 ;
			}
		}
		
		public int HasCover(int count=0){
			for(int i=0; i<coverList.Count; i++){ if(coverList[i]>0) count+=1; }
			return count;
		}
		
		public const float effectiveCoverAngle=90;
		public int GetCover(float angle){
			int cover=0;
			if(GridManager.IsHexGrid()){
				if(angle>0 && angle<60)				cover=coverList[0];
				else if(angle==60)						cover=Mathf.Max(coverList[0], coverList[1]);
				else if(angle>60 && angle<120)	cover=coverList[1];
				else if(angle==120)					cover=Mathf.Max(coverList[1], coverList[2]);
				else if(angle>120 && angle<180)	cover=coverList[2];
				else if(angle==180)					cover=Mathf.Max(coverList[2], coverList[3]);
				else if(angle>180 && angle<240)	cover=coverList[3];
				else if(angle==240)					cover=Mathf.Max(coverList[3], coverList[4]);
				else if(angle>240 && angle<300)	cover=coverList[4];
				else if(angle==300)					cover=Mathf.Max(coverList[4], coverList[5]);
				else if(angle>300 && angle<360)	cover=coverList[5];
				else if(angle==360 || angle==0)	cover=Mathf.Max(coverList[5], coverList[0]);
				
				//~ if(angle<30) 									cover=Mathf.Max(coverList[5], coverList[0]);
				//~ else if(angle==30)							cover=coverList[0];
				//~ else if(angle>30 && angle<90)			cover=Mathf.Max(coverList[0], coverList[1]);
				//~ else if(angle==90)							cover=coverList[1];
				//~ else if(angle>90 && angle<150)		cover=Mathf.Max(coverList[1], coverList[2]);
				//~ else if(angle==150)						cover=coverList[2];
				//~ else if(angle>150 && angle<210)		cover=Mathf.Max(coverList[2], coverList[3]);
				//~ else if(angle==210)						cover=coverList[3];
				//~ else if(angle>210 && angle<270)		cover=Mathf.Max(coverList[3], coverList[4]);
				//~ else if(angle==270)						cover=coverList[4];
				//~ else if(angle>270 && angle<330)		cover=Mathf.Max(coverList[4], coverList[5]);
				//~ else if(angle==330)						cover=coverList[5];
				//~ else if(angle>330)							cover=Mathf.Max(coverList[5], coverList[0]);
			}
			else if(GridManager.IsSquareGrid()){
				if(angle>0 && angle<90) 					cover=Mathf.Max(coverList[0], coverList[1]);
				else if(angle==90)							cover=coverList[1];
				else if(angle>90 && angle<180)		cover=Mathf.Max(coverList[1], coverList[2]);
				else if(angle==180)						cover=coverList[2];
				else if(angle>180 && angle<270)		cover=Mathf.Max(coverList[2], coverList[3]);
				else if(angle==270)						cover=coverList[3];
				else if(angle>270 && angle<360)		cover=Mathf.Max(coverList[3], coverList[0]);
				else if(angle==0 || angle==360)		cover=coverList[0];
			}
			return cover;
		}
		#endregion
		
		
		#region deploy and spawn-group
		public int deployFacID=-1;
		public int spawnGroupFacID=-1;
		public int spawnGroupID=-1;
		
		public void SetDeploymentFac(int facID){ deployFacID=facID; }
		public void ClearDeploymentFac(){ SetDeploymentFac(-1); }
		
		public void SetSpawnGroup(int facID, int groupID){
			spawnGroupFacID=facID;
			spawnGroupID=groupID;
		}
		public void ClearSpawnGroup(){ SetSpawnGroup(-1, -1); }
		#endregion
		
		
		#region A* and neighbour
		public float scoreG;
		public float scoreH;
		public float scoreF;
		public float tempScoreG=0;
		public Node parent;
		
		[System.NonSerialized] private List<Node> neighbours=new List<Node>();
		private List<float> neighboursAngle=new List<float>();
		
		public enum _ListState{Unassigned, Open, Close};
		public _ListState listState=_ListState.Unassigned;
		
		public void ResetListState(){
			listState=_ListState.Unassigned;
			scoreG=0;	scoreH=0;	scoreF=0;
			parent=null;
		}
		
		//public List<Node> GetNeighbourList(bool walkableOnly=false, bool allowUnit=false, bool allowObj=false){ 
		public List<Node> GetNeighbourList(bool walkableOnly=false, int allowUnit=-1, bool allowObs=false){ 
			if(neighbours==null) SetupNeighbour();
			List<Node> newList=new List<Node>();
			if(walkableOnly){
				for(int i=0; i<neighbours.Count; i++){
					if(!neighbours[i].IsBlocked(this, allowUnit, allowObs)) newList.Add(neighbours[i]);
				}
			}
			else{
				for(int i=0; i<neighbours.Count; i++) newList.Add(neighbours[i]);
				//for(int i=0; i<disconnectedNeighbourList.Count; i++) newList.Add(disconnectedNeighbourList[i]);
			}
			return newList;
		}
		
		public Node GetNeighbourFromPos(Vector3 pos){
			return GetNeighbourOfAngle((int)GridManager.GetAngle(GetPos(), pos, true));
		}
		public Node GetNeighbourOfAngle(int angle){
			for(int i=0; i<neighboursAngle.Count; i++){
				if(neighboursAngle[i]==angle) return neighbours[i];
			}
			return null;
		}
		
		public void SetupNeighbour(){
			neighboursAngle=new List<float>();
			neighbours=GetVerticalNeighbour(GetHorizontalNeighbour(new List<Node>()));
			for(int i=0; i<neighbours.Count; i++){
				if(neighbours[i]!=null){
					neighboursAngle.Add(GridManager.GetAngle(this, neighbours[i], true));
					continue;
				}
				neighbours.RemoveAt(i);	i-=1;
			}
			
			if(GridManager.IsSquareGrid() && GridManager.EnableDiagonalNeighbour()){
				AddDiagonalNeighbour(1, 1);
				AddDiagonalNeighbour(1, -1);
				AddDiagonalNeighbour(-1, -1);
				AddDiagonalNeighbour(-1, 1);
			}
		}
		public void AddDiagonalNeighbour(int offsetX, int offsetZ){
			Node nn=GridManager.GetNode(idxX+offsetX, idxZ+offsetZ);
			if(nn!=null){
				neighbours.Add(nn);
				neighboursAngle.Add(GridManager.GetAngle(this, nn, true));
			}
		}
		public List<Node> GetHorizontalNeighbour(List<Node> list){
			if(GridManager.GetGridType()==_GridType.HexGrid){
				if(idxX>0){
					list.Add(GridManager.GetNode(idxX-1, idxZ));
					if(idxX%2==0) 	list.Add(GridManager.GetNode(idxX-1, idxZ+1));
					else					list.Add(GridManager.GetNode(idxX-1, idxZ-1));
				}
				if(idxX<GridManager.DimensionX()-1){
					list.Add(GridManager.GetNode(idxX+1, idxZ));
					if(idxX%2==0) 	list.Add(GridManager.GetNode(idxX+1, idxZ+1));
					else					list.Add(GridManager.GetNode(idxX+1, idxZ-1));
				}
			}
			else{
				if(idxX>0) 										list.Add(GridManager.GetNode(idxX-1, idxZ));
				if(idxX<GridManager.DimensionX()-1) 	list.Add(GridManager.GetNode(idxX+1, idxZ));
			}
			return list;
		}
		public List<Node> GetVerticalNeighbour(List<Node> list){
			if(idxZ>0) 										list.Add(GridManager.GetNode(idxX, idxZ-1));
			if(idxZ<GridManager.DimensionZ()-1) 	list.Add(GridManager.GetNode(idxX, idxZ+1));
			return list;
		}
		
		
		public List<Node> ProcessNeighbour(int ignoreUnit, bool ignoreObs, bool calculateF=false, Vector3 pos=default(Vector3), bool debug=false){
			for(int i=0; i<neighbours.Count; i++){
				if(neighbours[i].IsBlocked(this, ignoreUnit, ignoreObs)) continue;
				
				float dirMultiplier=1;
				if(GridManager.IsHexGrid()){
					if(parent!=null && (GetPos()-parent.GetPos()).normalized==(neighbours[i].GetPos()-GetPos()).normalized) dirMultiplier=1.05f;
				}
				else{
					if(parent!=null && (GetPos()-parent.GetPos()).normalized==(neighbours[i].GetPos()-GetPos()).normalized) dirMultiplier=0.95f;
				}
				
				tempScoreG=scoreG+neighbours[i].cost*dirMultiplier;
				
				//if the neightbour state is clean (never evaluated so far in the search)
				if(neighbours[i].listState==_ListState.Unassigned){
					//check the score of G and H and update F, also assign the parent to currentNode
					neighbours[i].scoreG=tempScoreG;
					neighbours[i].parent=this;
					
					if(calculateF){
						neighbours[i].scoreH=Vector3.Distance(neighbours[i].pos, pos);
						neighbours[i].scoreF=neighbours[i].scoreG+neighbours[i].scoreH;
					}
				}
				//if the neighbour state is open (it has been evaluated and added to the open list)
				else if(neighbours[i].listState==_ListState.Open){
					//calculate if the path if using this neighbour node through current node would be shorter compare to previous assigned parent node
					if(neighbours[i].scoreG>tempScoreG){
						//if so, update the corresponding score and and reassigned parent
						neighbours[i].scoreG=tempScoreG;
						neighbours[i].parent=this;
						if(calculateF) neighbours[i].scoreF=neighbours[i].scoreG+neighbours[i].scoreH;
					}
				}
			}
			
			return neighbours;
		}
		
		public int GetAStarAPCost(){ return (int)scoreG; }
		#endregion
	}
	
}
