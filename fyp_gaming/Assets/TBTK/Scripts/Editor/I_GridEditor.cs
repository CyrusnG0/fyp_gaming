using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	//[CustomEditor(typeof(GridManager))]
	public class I_GridEditor : TBEditorInspector {
		
		private static GridManager gm;
		private static GridGenerator gGen;
		
		private static UnitManager unitManager;
		private static CollectibleManager collectibleManager;
		
		private enum _EditMode{ Node, Unit, Collectible, }
		private static _EditMode editMode;
		
		private static Color colorOn=new Color(.25f, 1f, 1f, 1f);
		
		private static bool showGridEditor=false;
		private static bool enableEditing=false;
		public static void DrawGridEditor(GridManager gridM){
			gm=gridM;	gGen=gm.gameObject.GetComponent<GridGenerator>();
			
			Init();
			
			GetUnitAndCollectibleManager();
			
			EditorGUILayout.Space();
			
			EditorGUILayout.BeginHorizontal();
				EditorGUI.BeginChangeCheck();
				showGridEditor=EditorGUILayout.Foldout(showGridEditor, "Show Grid editor", TBE.foldoutS);
				if(EditorGUI.EndChangeCheck()) enableEditing=showGridEditor;
				
				GUI.color=enableEditing ? colorOn : new Color(0.5f, 0.5f, 0.5f, 1);
				if(GUILayout.Button("Edit Grid", GUILayout.MaxWidth(80))){
					enableEditing=!enableEditing;
					if(enableEditing) showGridEditor=true;
					else showGridEditor=false;
				}
				GUI.color=Color.white;
			EditorGUILayout.EndHorizontal();
			
			if(!showGridEditor) return;
			
			if(Application.isPlaying){
				EditorGUILayout.HelpBox("Grid editing is not allowed during runtime", MessageType.Warning);
				return;
			}
			
			
			GUILayout.Label("Edit Type:", GUILayout.MaxWidth(60));
			
			EditorGUILayout.BeginHorizontal();
			
				GUI.color=editMode==_EditMode.Node ? colorOn : Color.white ;
				if(GUILayout.Button("Node")) editMode=_EditMode.Node;
				
				GUI.color=editMode==_EditMode.Unit ? colorOn : Color.white ;
				if(GUILayout.Button("Unit")) editMode=_EditMode.Unit;
			
				GUI.color=editMode==_EditMode.Collectible ? colorOn : Color.white ;
				if(GUILayout.Button("Collectible")) editMode=_EditMode.Collectible;
				
				GUI.color=Color.white;
			
			EditorGUILayout.EndHorizontal();
			
			editorWidth=Screen.width;
			if(editMode==_EditMode.Node) 			DrawEditModeUINode();
			if(editMode==_EditMode.Unit) 			DrawEditModeUIUnit();
			if(editMode==_EditMode.Collectible) 	DrawEditModeUICollectible();
			
			GUI.color=new Color(1, 1, 1, 1);
		}
		
		
		private static void GetUnitAndCollectibleManager(){
			if(unitManager==null) unitManager=(UnitManager)FindObjectOfType(typeof(UnitManager));
			if(collectibleManager==null) collectibleManager=(CollectibleManager)FindObjectOfType(typeof(CollectibleManager));
		}
		
		
		private static bool init=false;
		private static void Init(){
			if(gGen==null) return;
			
			if(init) return;
			init=true;
			
			prefabObsHexH=gGen.obstaclePrefabHexH.Count>0 ? gGen.obstaclePrefabHexH[0] : null;
			prefabObsHexF=gGen.obstaclePrefabHexF.Count>0 ? gGen.obstaclePrefabHexF[0] : null;
			prefabObsSqH=gGen.obstaclePrefabSqH.Count>0 ? gGen.obstaclePrefabSqH[0] : null;
			prefabObsSqF=gGen.obstaclePrefabSqF.Count>0 ? gGen.obstaclePrefabSqF[0] : null;
			prefabWallH=gGen.wallPrefabH.Count>0 ? gGen.wallPrefabH[0] : null;
			prefabWallF=gGen.wallPrefabF.Count>0 ? gGen.wallPrefabF[0] : null;
		}
		
		
		private static float editorWidth=1;		//used to determined the width of the inspector
		
		
		
		private enum _NodeStateE {Unwalkable, Default, WallH, WallF, ObstacleH, ObstacleF, SpawnArea, Deployment, APCost}
		private static _NodeStateE nodeState=_NodeStateE.Default;
		
		//~ private int spawnAreaFactionID=0;	//factionID of the spawnArea
		//~ private int spawnAreaInfoID=0;			//spawnInfoID of the spawnArea (each faction could have multiple spawnInfo)
		//~ private int deployAreaFactionID=0;
		
		private static int deployFacID;
		private static int spawnGroupFacID;
		private static int spawnGroupID;
		
		private static void DrawStateButton(_NodeStateE state, string text){
			GUI.color=nodeState==state ? colorOn : Color.white; 
			if(GUILayout.Button(text, GUILayout.MaxWidth(editorWidth/2))) nodeState=state;
			GUI.color=Color.white;
			
			gm.drawAPCost=nodeState==_NodeStateE.APCost;
		}
		
		
			
		private static void DrawEditModeUINode(){
			GUILayout.Label("Node State:");
			
			EditorGUILayout.BeginHorizontal();
				DrawStateButton(_NodeStateE.Unwalkable, "Unwalkable");
				DrawStateButton(_NodeStateE.Default, "Default");
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				DrawStateButton(_NodeStateE.WallH, "Wall (Half)");
				DrawStateButton(_NodeStateE.WallF, "Wall (Full)");
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				DrawStateButton(_NodeStateE.ObstacleH, "Obstacle (Half)");
				DrawStateButton(_NodeStateE.ObstacleF, "Obstacle (Full)");
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				DrawStateButton(_NodeStateE.Deployment, "Deployment");
				DrawStateButton(_NodeStateE.SpawnArea, "SpawnArea");
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				DrawStateButton(_NodeStateE.APCost, "AP Cost");
				//DrawStateButton(_NodeStateE.SpawnArea, "SpawnArea");
			EditorGUILayout.EndHorizontal();
			
			if(nodeState==_NodeStateE.SpawnArea){
				GUILayout.Label("________________________________________________________________________________________________________", GUILayout.Width(editorWidth-30));
				
				GetUnitAndCollectibleManager();
				
				for(int i=0; i<unitManager.factionList.Count; i++){
					EditorGUILayout.Space();
					
					GUI.color=spawnGroupFacID==i ? colorOn : Color.white ;
					if(GUILayout.Button(unitManager.factionList[i].name.ToString(), GUILayout.MaxWidth(editorWidth))) spawnGroupFacID=i;
					GUI.color=Color.white;
					
					if(spawnGroupFacID==i){
						Faction fac=unitManager.factionList[i];
						
						if(fac.spawnGroupList.Count==0) spawnGroupID=-1; 
						else{
							spawnGroupID=Mathf.Clamp(spawnGroupID, 0, fac.spawnGroupList.Count);
						
							for(int n=0; n<fac.spawnGroupList.Count; n++){
								EditorGUILayout.BeginHorizontal();
									GUILayout.Label("   - ", GUILayout.MaxWidth(25));
									GUI.color=spawnGroupID==n ? colorOn : Color.white ;
									if(GUILayout.Button("SpawnArea "+(n+1), GUILayout.MaxWidth((editorWidth-25)*0.7f-10))) spawnGroupID=n;
								
									GUI.color=Color.white;
									if(GUILayout.Button("Clear All ", GUILayout.MaxWidth((editorWidth-25)*0.3f))){
										List<List<Node>> grid=GridManager.GetGrid();
										for(int x=0; x<grid.Count; x++){
											for(int z=0; z<grid[x].Count; z++){
												if(grid[x][z].spawnGroupFacID==fac.factionID && grid[x][z].spawnGroupID==fac.spawnGroupList[n].ID) 
													grid[x][z].ClearSpawnGroup();
											}
										}
										SceneView.RepaintAll();
									}
								EditorGUILayout.EndHorizontal();
							}
						}
					}
				}
			}
			if(nodeState==_NodeStateE.Deployment){
				GUILayout.Label("________________________________________________________________________________________________________", GUILayout.Width(editorWidth-30));
				
				GetUnitAndCollectibleManager();
				
				for(int i=0; i<unitManager.factionList.Count; i++){
					EditorGUILayout.Space();
					EditorGUILayout.BeginHorizontal();
						
						GUI.color=deployFacID==i ? colorOn : Color.white ;
						if(GUILayout.Button(unitManager.factionList[i].name.ToString(), GUILayout.MaxWidth(editorWidth*0.7f))) deployFacID=i;
						GUI.color=Color.white;
					
						if(GUILayout.Button("Clear All", GUILayout.MaxWidth(editorWidth*0.3f))){
							List<List<Node>> grid=GridManager.GetGrid();
							for(int x=0; x<grid.Count; x++){
								for(int z=0; z<grid[x].Count; z++){
									if(grid[x][z].deployFacID==unitManager.factionList[i].factionID) grid[x][z].ClearDeploymentFac();
								}
							}
							SceneView.RepaintAll();
						}
						
					EditorGUILayout.EndHorizontal();
				}
			}
			if(nodeState==_NodeStateE.WallH){
				prefabWallH=DrawPrefabList(prefabWallH, gGen.wallPrefabH);
			}
			if(nodeState==_NodeStateE.WallF){
				prefabWallF=DrawPrefabList(prefabWallF, gGen.wallPrefabF);
			}
			if(nodeState==_NodeStateE.ObstacleH){
				if(GridManager.IsHexGrid()) prefabObsHexH=DrawPrefabList(prefabObsHexH, gGen.obstaclePrefabHexH);
				else prefabObsSqH=DrawPrefabList(prefabObsSqH, gGen.obstaclePrefabSqH);
			}
			if(nodeState==_NodeStateE.ObstacleF){
				if(GridManager.IsHexGrid()) prefabObsHexF=DrawPrefabList(prefabObsHexF, gGen.obstaclePrefabHexF);
				else prefabObsSqF=DrawPrefabList(prefabObsSqF, gGen.obstaclePrefabSqF);
			}
			if(nodeState==_NodeStateE.APCost){
				GUILayout.Label("________________________________________________________________________________________________________", GUILayout.Width(editorWidth-30));
				
				EditorGUILayout.Space();
				
				cont=new GUIContent("AP Cost To Set:", "");
				apCost=EditorGUILayout.DelayedIntField(cont, apCost, GUILayout.MaxWidth(180));
				
				SceneView.RepaintAll();
			}
		}
		
		private static int apCost=1;
		
		
		private static Transform DrawPrefabList(Transform prefab, List<Transform> prefabList){
			GUILayout.Label("________________________________________________________________________________________________________", GUILayout.Width(editorWidth-30));
			GUILayout.Label("Prefabs:");
			
			itemInRow=(int)Mathf.Max(1, Mathf.Floor((editorWidth)/50));
			Rect rect=new Rect(0, 0, 0, 0);
			
			EditorGUILayout.BeginHorizontal();
				for(int i=0; i<prefabList.Count; i++){
					if(i%itemInRow==0){
						EditorGUILayout.EndHorizontal();
						EditorGUILayout.BeginHorizontal();
					}
					
					GUI.color=prefab==prefabList[i] ? colorOn : Color.white ;
					cont=new GUIContent((Texture)null, "");
					
					if(GUILayout.Button(cont, GUILayout.Width(45), GUILayout.Height(45))) prefab=prefabList[i];
					
					if(prefab==prefabList[i]) rect=GUILayoutUtility.GetLastRect();
					else{
						Rect rrect=GUILayoutUtility.GetLastRect();
						rrect.x+=3; rrect.y+=3; rrect.width-=6; rrect.height-=6;
						TBE.DrawSprite(rrect, null, "", false);
					}
				}
				
				if(prefab!=null){
					rect.x+=3; rect.y+=3; rect.width-=6; rect.height-=6;
					TBE.DrawSprite(rect, null, "", false);
					//TBE.DrawSprite(rect, selectedUnit.icon, selectedUnit.name, false);
				}
			EditorGUILayout.EndHorizontal();
				
			return prefabList.Count>0 ? prefabList[0] : prefab;
		}
		
		
		
		private static void DrawEditModeUIUnit(){
			GUILayout.Label("Unit's Faction:");
			
				if(unitManager==null) unitManager=(UnitManager)FindObjectOfType(typeof(UnitManager));
			
				for(int i=0; i<unitManager.factionList.Count; i++){
					GUI.color=unitFactionID==i ? colorOn : Color.white ;
					if(GUILayout.Button(unitManager.factionList[i].name.ToString(), GUILayout.MaxWidth(editorWidth))) unitFactionID=i;
				}
			
			GUILayout.Label("______________________________________________________________________________________________________________", GUILayout.Width(editorWidth-30));
			GUILayout.Label("Unit To Deploy:");
			
				GetUnitAndCollectibleManager();
				
				List<Unit> unitList=TBE.unitDB.unitList;
				itemInRow=(int)Mathf.Max(1, Mathf.Floor((editorWidth)/50));
				Rect rect=new Rect(0, 0, 0, 0);
				
				EditorGUILayout.BeginHorizontal();
					for(int i=0; i<unitList.Count; i++){
						if(i%itemInRow==0){
							EditorGUILayout.EndHorizontal();
							EditorGUILayout.BeginHorizontal();
						}
						
						GUI.color=selectedUnit==unitList[i] ? colorOn : Color.white ;
						cont=new GUIContent((Texture)null, "");
						
						if(GUILayout.Button(cont, GUILayout.Width(45), GUILayout.Height(45))) selectedUnit=unitList[i];
						
						if(selectedUnit==unitList[i]) rect=GUILayoutUtility.GetLastRect();
						else{
							Rect rrect=GUILayoutUtility.GetLastRect();
							rrect.x+=3; rrect.y+=3; rrect.width-=6; rrect.height-=6;
							TBE.DrawSprite(rrect, unitList[i].icon, "", false);
						}
					}
					
					if(selectedUnit!=null){
						rect.x+=3; rect.y+=3; rect.width-=6; rect.height-=6;
						TBE.DrawSprite(rect, selectedUnit.icon, "", false);
					}
				EditorGUILayout.EndHorizontal();
				
				GUI.color=Color.white;
				
			EditorGUILayout.Space();
		}
		
		private static void DrawEditModeUICollectible(){
			GUILayout.Label("Collectible To Deploy:");
				
				GetUnitAndCollectibleManager();
			
				List<Collectible> collectibleList=TBE.collectibleDB.collectibleList;
				itemInRow=(int)Mathf.Max(1, Mathf.Floor((editorWidth)/50));
				Rect rect=new Rect(0, 0, 0, 0);
				
				EditorGUILayout.BeginHorizontal();
					for(int i=0; i<collectibleList.Count; i++){
						if(i%itemInRow==0){
							EditorGUILayout.EndHorizontal();
							EditorGUILayout.BeginHorizontal();
						}
						
						GUI.color=selectedCollectible==collectibleList[i] ? colorOn : Color.white ;
						cont=new GUIContent((Texture)null, "");
						
						if(GUILayout.Button(cont, GUILayout.Width(45), GUILayout.Height(45))) selectedCollectible=collectibleList[i];
						
						if(selectedCollectible==collectibleList[i]) rect=GUILayoutUtility.GetLastRect();
						else{
							Rect rrect=GUILayoutUtility.GetLastRect();
							rrect.x+=3; rrect.y+=3; rrect.width-=6; rrect.height-=6;
							TBE.DrawSprite(rrect, collectibleList[i].icon, "", false);
						}
					}
					
					if(selectedCollectible!=null){
						rect.x+=3; rect.y+=3; rect.width-=6; rect.height-=6;
						TBE.DrawSprite(rect, selectedCollectible.icon, selectedCollectible.itemName, false);
					}
				EditorGUILayout.EndHorizontal();
				
				GUI.color=Color.white;
				
			EditorGUILayout.Space();
		}
		
		
		
		
		private static Transform prefabObsHexH;
		private static Transform prefabObsHexF;
		private static Transform prefabObsSqH;
		private static Transform prefabObsSqF;
		private static Transform prefabWallH;
		private static Transform prefabWallF;
		
		private static int unitFactionID=0;	//factionID of the unit to be plop on the grid
		private static Unit selectedUnit;		//currently select unit to be plop on the grid
		
		private static Collectible selectedCollectible;		//currently select collectible to be plop on the grid=1;		//used to determined the width of the inspector
		private static int itemInRow;
		
		
		
		
		#region SceneView interaction
		public static void OnSceneGUI(){
			if(Application.isPlaying) return;
			if(!enableEditing) return;
			
			Event current = Event.current;
			int controlID = GUIUtility.GetControlID(FocusType.Passive);
			
			switch (current.type)
			{
				case EventType.MouseDown:
					Edit(current);
					camRot=SceneView.currentDrawingSceneView.rotation;
					break;
				
				case EventType.MouseDrag:
					//if(current.button==0) 
						Edit(current);
				
					if(!rotatingView) SceneView.currentDrawingSceneView.rotation=camRot;
					else		camRot=SceneView.currentDrawingSceneView.rotation;
				
					break;
				
				//case EventType.MouseUp:
				//	break;
				
				case EventType.KeyDown:
					if(Event.current.keyCode==(KeyCode.RightAlt) || Event.current.keyCode==(KeyCode.LeftAlt)) rotatingView=true;
					break;
					
				case EventType.KeyUp:
					if(Event.current.keyCode==(KeyCode.RightAlt) || Event.current.keyCode==(KeyCode.LeftAlt)) rotatingView=false;
					break;
		 
				case EventType.Layout:
					HandleUtility.AddDefaultControl(controlID);
					break;
			}
			
			
			//int controlId = HandleUtility.GetControlId(FocusType.Passive);
			//HandleUtility.AddDefaultControl(controlId);
			
			//Debug.Log(Camera.current.transform.name);
			//Debug.Log("  "+SceneView.currentDrawingSceneView.camera.transform.rotation);
			//Debug.Log("Available id: " + GUIUtility.GetControlID(FocusType.Passive));
		}
		
		private static Quaternion camRot;
		private static bool rotatingView=false;
		
		private static void Edit(Event current){
			if(!enableEditing) return;
			if(rotatingView) return;
			
			LayerMask mask=1<<TBTK.GetLayerNode();
			Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
			RaycastHit hit;
			if(Physics.Raycast(ray, out hit, Mathf.Infinity, mask)){
				Node node=GridManager.GetNode(hit.point, hit.collider.gameObject);
				
				//if(gm.gridColliderType==GridManager._GridColliderType.Individual)
				//	node=hit.transform.gameObject.GetComponent<Node>();
				//else if(gm.gridColliderType==GridManager._GridColliderType.Master)
				//	node=gm._GetNodeOnPos(hit.point);
				
				if(node==null) return;
				
				//Undo.RecordObject(node, "Node");
				Undo.RecordObject(GridManager.GetInstance(), "gm");

				if(editMode==_EditMode.Node) EditNodeState(node, current.button, hit.point);
				else if(editMode==_EditMode.Unit) EditNodeUnit(node, current.button, hit.point);
				else if(editMode==_EditMode.Collectible) EditNodeCollectible(node, current.button, hit.point);
				
				//EditorUtility.SetDirty(node);
			}
			//else Debug.Log("hit nothing");
		}
		
		
		private static void EditNodeState(Node node, int mouseClick=0, Vector3 hitPos=default(Vector3)){
			Undo.RecordObject(GridManager.GetInstance(), "gm");
			
			if(nodeState==_NodeStateE.Unwalkable){
				if(node.HasObstacle()){ Debug.LogWarning("Cannot set node to unwalkable. Clear obstacle on node first", gm); return; }
				//Undo.RecordObject(node.gameObject, "Node");
				node.walkable=false;
				node.objT.GetComponent<Renderer>().enabled=node.walkable;//.SetActive(node.walkable);
				//node.gameObject.SetActive(false);
			}
			else if(nodeState==_NodeStateE.Default){
				if(node.HasObstacle()){ Debug.LogWarning("Cannot set node to walkable. Clear obstacle on node first", gm); return; }
				//Undo.RecordObject(node.gameObject, "Node");
				node.walkable=true;
				node.objT.GetComponent<Renderer>().enabled=node.walkable;//.SetActive(node.walkable);
				//node.gameObject.SetActive(true);
			}
			else if(nodeState==_NodeStateE.WallH || nodeState==_NodeStateE.WallF){
				if(node.HasObstacle()){ Debug.LogWarning("Cannot add/remove wall. Clear obstacle on node first", gm); return; }
				
				//~ Grid grid=instance.GetGrid();
				//~ Vector3 dir=hitPos-node.GetPos();
				//~ float angle=0;
				//~ if(instance.nodeType==_NodeType.Square) angle=Utilities.VectorToAngle90(new Vector2(dir.x, dir.z));
				//~ else if(instance.nodeType==_NodeType.Hex) angle=Utilities.VectorToAngle60(new Vector2(dir.x, dir.z));
				
				//~ Node neighbourNode=grid.GetNeighbourInDir(node, angle);
				//~ if(neighbourNode==null){ Debug.LogWarning("Cannot add/remove wall. There's no adjacent node", this); return; }
				//~ if(neighbourNode.HasObstacle()){ Debug.LogWarning("Cannot add/remove wall. Clear obstacle on neighbour node first", this); return; }
				
				//~ if(mouseClick==0) node.AddWall(angle, neighbourNode, nodeState==_NodeStateE.WallH ? 1 : 2);
				//~ else node.RemoveWall(angle, neighbourNode);
				
				//~ Debug.Log(node.wallList.Count);
				
				if(mouseClick==0){
					
					Node neighbour=node.GetNeighbourFromPos(hitPos);
					if(neighbour==null){ Debug.Log("neighbour is null"); return; }
					
					if(nodeState==_NodeStateE.WallH)
						node.AddWall(node.GetPos()+(neighbour.GetPos()-node.GetPos()), prefabWallH);
					if(nodeState==_NodeStateE.WallF)
						node.AddWall(node.GetPos()+(neighbour.GetPos()-node.GetPos()), prefabWallF);
					
					Unit cachedUnit1=neighbour.unit;		neighbour.unit=null;
					Unit cachedUnit2=node.unit;				node.unit=null;
					
					bool flag=true;
					List<Node> path=AStar.SearchWalkableNode(node, neighbour, AStar.BypassUnitCode(false), false, false);
					if(path.Count==0){ flag=false; }
					
					neighbour.unit=cachedUnit1;		node.unit=cachedUnit2;
					
					if(!flag){
						node.RemoveWall(node.GetPos()+(neighbour.GetPos()-node.GetPos()));
						Debug.Log("Cannot create wall here, would block off the node completely");
					}
				
				}
				else node.RemoveWall(hitPos);
			}
			else if(nodeState==_NodeStateE.ObstacleH || nodeState==_NodeStateE.ObstacleF){
				if(node.HasWall()){ Debug.LogWarning("Cannot add/remove obstacle. Clear wall on node first", gm); return; }
				
				if(mouseClick==1){
					if(node.HasObstacle()) node.RemoveObstacle();
				}
				else{
					if(nodeState==_NodeStateE.ObstacleH)
						node.AddObstacle(GridManager.IsHexGrid() ? prefabObsHexH : prefabObsSqH);
					else
						node.AddObstacle(GridManager.IsHexGrid() ? prefabObsHexF : prefabObsSqF);
					
					//~ ClearSpawnNode(node);
					//~ ClearDeployableNode(node);
				}
			}
			else if(nodeState==_NodeStateE.SpawnArea){
				//if(node.HasObstacle()){ Debug.LogWarning("Cannot add/remove obstacle. Clear wall on node first", gm); return; }
				
				//~ if(spawnAreaFactionID>=unitManager.factionList.Count) return;
				//~ if(spawnAreaInfoID>=unitManager.factionList[spawnAreaFactionID].spawnInfoList.Count) return;
				
				//~ //Undo.RecordObject(unitManager, "FactionManager");
				
				//~ Faction fac=null;
				
				if(mouseClick==1){
					//ClearSpawnNode(node);
					node.ClearSpawnGroup();
				}
				else{
					if(spawnGroupFacID>=0 && spawnGroupID>=0){
						int facID=unitManager.factionList[spawnGroupFacID].factionID;
						int groupID=unitManager.factionList[spawnGroupFacID].spawnGroupList[spawnGroupID].ID;
						//if(grid[x][z].spawnGroupFacID==fac.factionID && grid[x][z].spawnGroupID==fac.spawnGroupList[n].factionID) 
						node.SetSpawnGroup(facID, groupID);
					}
					//~ fac=unitManager.factionList[spawnAreaFactionID];
					//~ if(node.spawnAreaID!=fac.ID) ClearSpawnNode(node);
					
					//~ if(!fac.spawnInfoList[spawnAreaInfoID].startingNodeList.Contains(node)){
						//~ fac.spawnInfoList[spawnAreaInfoID].startingNodeList.Add(node);
						//~ node.spawnAreaID=fac.ID;
					//~ }
				}
				
				SceneView.RepaintAll();
			}
			else if(nodeState==_NodeStateE.Deployment){
				//if(node.HasObstacle()){ Debug.LogWarning("Cannot add/remove obstacle. Clear wall on node first", gm); return; }
				
				//~ if(deployAreaFactionID>=unitManager.factionList.Count) return;
				
				//~ //Undo.RecordObject(unitManager, "FactionManager");
				
				//~ Faction fac=null;
				
				if(mouseClick==1){
					//ClearDeployableNode(node);
					node.ClearDeploymentFac();
				}
				else{
					//Debug.Log(deployFacID+"   "+unitManager.factionList[deployFacID].factionID);
					node.SetDeploymentFac(unitManager.factionList[deployFacID].factionID);
					//~ fac=unitManager.factionList[deployAreaFactionID];
					//~ if(node.deployAreaID!=fac.ID) ClearDeployableNode(node);
					
					//~ if(!fac.deployableNodeList.Contains(node)){
						//~ fac.deployableNodeList.Add(node);
						//~ node.deployAreaID=fac.ID;
					//~ }
				}
				
				SceneView.RepaintAll();
			}
			else if(nodeState==_NodeStateE.APCost){
				node.cost=apCost;
			}
		}
		
		
		
		private static void EditNodeUnit(Node node, int mouseClick=0, Vector3 hitPos=default(Vector3)){
			if(mouseClick==0){
				if(!node.walkable){ Debug.LogWarning("Cannot place unit on unwalkable node", gm); return; }
				if(node.obstacleT!=null){ Debug.LogWarning("Cannot place unit on node with obstacle", gm); return; }
				if(selectedUnit==null){ Debug.LogWarning("No unit selected. Select a unit from the editor first", gm); return; }
				
				if(node.unit!=null) RemoveUnit(node.unit);
				if(node.collectible!=null) RemoveCollectible(node.collectible);
				
				//~ Vector3 dir=hitPos-node.GetPos();
				//~ float angle=0;
				//~ if(instance.nodeType==_NodeType.Square) angle=360-(Utilities.VectorToAngle90(new Vector2(dir.x, dir.z))-90);
				//~ else if(instance.nodeType==_NodeType.Hex) angle=360-(Utilities.VectorToAngle60(new Vector2(dir.x, dir.z))-90);
				
				float angle=GridManager.GetAngle(node.GetPos(), hitPos, true);
				
				GameObject unitObj=(GameObject)PrefabUtility.InstantiatePrefab(selectedUnit.gameObject);
				
				Undo.RegisterCreatedObjectUndo(unitObj, "CreatedUnit");
				
				unitObj.transform.position=node.GetPos();
				unitObj.transform.rotation=Quaternion.Euler(0, 360-angle+90, 0);
				if(UnitManager.GetInstance()!=null) unitObj.transform.parent=UnitManager.GetInstance().transform;
				
				Unit unit=unitObj.GetComponent<Unit>();
				node.unit=unit;	unit.node=node;
				
				unitManager.factionList[unitFactionID].unitList.Add(unit);
				unit.SetFacID(unitManager.factionList[unitFactionID].factionID);
			}
			else if(mouseClick==1) RemoveUnit(node.unit);
		}
		private static void RemoveUnit(Unit unit){
			if(unit==null) return;
			
			for(int i=0; i<unitManager.factionList.Count; i++){
				if(unitManager.factionList[i].factionID==unit.GetFacID()){
					unitManager.factionList[i].unitList.Remove(unit);
					break;
				}
			}
			
			unit.node.unit=null;
			Undo.DestroyObjectImmediate(unit.gameObject);
		}
		
		
		
		private static void EditNodeCollectible(Node node, int mouseClick=0, Vector3 hitPos=default(Vector3)){
			if(mouseClick==0){
				if(!node.walkable){ Debug.LogWarning("Cannot place item on unwalkable node", gm); return; }
				if(node.obstacleT!=null){ Debug.LogWarning("Cannot place item on node with obstacle", gm); return; }
				if(selectedCollectible==null){ Debug.LogWarning("No item selected. Select a unit from the editor first", gm); return; }
				
				if(node.unit!=null) RemoveUnit(node.unit);
				if(node.collectible!=null) RemoveCollectible(node.collectible);
				
				float angle=GridManager.GetAngle(node.GetPos(), hitPos, true);
				
				GameObject itemObj=(GameObject)PrefabUtility.InstantiatePrefab(selectedCollectible.gameObject);
				//if(CollectibleManager.GetInstance()!=null) itemObj.transform.parent=CollectibleManager.GetInstance().transform;
				
				Undo.RegisterCreatedObjectUndo(itemObj, "CreatedItem");
				
				itemObj.transform.position=node.GetPos();
				itemObj.transform.rotation=Quaternion.Euler(0, 360-angle+90, 0);
				itemObj.transform.parent=node.objT;
				
				Collectible item=itemObj.GetComponent<Collectible>();
				node.collectible=item;	
				
				collectibleManager.PlaceItemAtNode(item, node);
			}
			else if(mouseClick==1) RemoveCollectible(node.collectible);
		}
		private static void RemoveCollectible(Collectible item){
			if(item==null) return;
			//item.node.collectible=null;
			collectibleManager.RemoveItem(item);
			Undo.DestroyObjectImmediate(item.gameObject);
		}
		
		#endregion
		
	}
	
}
