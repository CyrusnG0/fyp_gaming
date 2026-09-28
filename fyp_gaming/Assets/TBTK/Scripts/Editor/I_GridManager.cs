using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(GridManager))]
	public class I_GridManagerEditor : TBEditorInspector {

		private GridManager instance;
		
		public override void Awake(){
			base.Awake();
			instance = (GridManager)target;
			
			InitLabel();
		}
		
		
		private string[] gridTypeLabel;
		private string[] gridTypeTooltip;
		
		private string[] colliderTypeLabel;
		private string[] colliderTypeTooltip;
		
		private string[] rangeCalTypeLabel;
		private string[] rangeCalTypeTooltip;
		
		void InitLabel(){
			int enumLength = Enum.GetValues(typeof(_GridType)).Length;
			gridTypeLabel=new string[enumLength];		gridTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				gridTypeLabel[i]=((_GridType)i).ToString();
				if((_GridType)i==_GridType.HexGrid) 		gridTypeTooltip[i]="using Hex grid";
				if((_GridType)i==_GridType.SquareGrid) 	gridTypeTooltip[i]="using square grid";
			}
			
			enumLength = Enum.GetValues(typeof(_GridColliderType)).Length;
			colliderTypeLabel=new string[enumLength];	colliderTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				colliderTypeLabel[i]=((_GridColliderType)i).ToString();
				if((_GridColliderType)i==_GridColliderType.Master) 
					colliderTypeTooltip[i]="using a single master collider for all the node on the grid. Allow bigger grid but the nodes on the grid cannot be adjusted";
				if((_GridColliderType)i==_GridColliderType.Individual) 
					colliderTypeTooltip[i]="using individual collider for each node on the grid. This allow positional adjustment of individual node but severely limited the grid size. Not recommend for any grid beyond 100x100.";
			}
			
			enumLength = Enum.GetValues(typeof(GridManager._RangeCalculation)).Length;
			rangeCalTypeLabel=new string[enumLength];		rangeCalTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				rangeCalTypeLabel[i]=((GridManager._RangeCalculation)i).ToString();
				if((GridManager._RangeCalculation)i==GridManager._RangeCalculation.ByNode) 		
					rangeCalTypeTooltip[i]="Connection of node is measure as a unit of distance";
				if((GridManager._RangeCalculation)i==GridManager._RangeCalculation.ByDistance) 	
					rangeCalTypeTooltip[i]="The actual distance between the node is used\nwith node size being used as the unit of distance";
			}
		}
		
		
		
		private bool showBaseProperties=true;
		private int cacheLabelLength=80;
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			
			
			if(GUILayout.Button("Generate Grid")) instance.gameObject.GetComponent<GridGenerator>().Generate();
			
			EditorGUILayout.BeginHorizontal();
				if(GUILayout.Button("Generate Unit")) instance.gameObject.GetComponent<GridGenerator>().GenerateUnit();
				if(GUILayout.Button("Generate Collectible")) instance.gameObject.GetComponent<GridGenerator>().GenerateCollectible();
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.Space();
			
			EditorGUILayout.BeginHorizontal();
				GUI.color=new Color(1f, .75f, .5f, 1);
				if(GUILayout.Button("Clear")) instance.gameObject.GetComponent<GridGenerator>().Clear();
				GUI.color=Color.white;
				if(GUILayout.Button("Clear Invalid Spawn/Deploy Point")){
					instance.gameObject.GetComponent<GridGenerator>().ClearInvalidDeploymentAndSpawnArea();
					SceneView.RepaintAll();
				}
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.Space();
			EditorGUILayout.Space();
			
			EditorGUIUtility.labelWidth=180;
			
			cont=new GUIContent("Generate Grid On Start:", "Check regenerate the grid when the scene start playing");
			instance.generateGridOnStart=EditorGUILayout.Toggle(cont, instance.generateGridOnStart);
			
			cont=new GUIContent("Generate Unit On Start:", "Check regenerate the grid when the scene start playing");
			instance.generateUnitOnStart=EditorGUILayout.Toggle(cont, instance.generateUnitOnStart);
			
			cont=new GUIContent("Generate Collectible On Start:", "Check regenerate the grid when the scene start playing");
			instance.generateCollectibleOnStart=EditorGUILayout.Toggle(cont, instance.generateCollectibleOnStart);
			
			EditorGUIUtility.labelWidth=0;
			
			EditorGUILayout.Space();
				
				int rangeCalculation=(int)instance.rangeCalculation;
				cont=new GUIContent("Range Calculation:", "The type calculation used when determined range\nOnly applicable when using square-grid with diagonal neighbour");
				if(instance.cacheGridType==_GridType.SquareGrid && instance.cacheDiagonalNeighbour){
					contL=TBE.SetupContL(rangeCalTypeLabel, rangeCalTypeTooltip);
					rangeCalculation = EditorGUILayout.Popup(cont, rangeCalculation, contL);
					instance.rangeCalculation=(GridManager._RangeCalculation)rangeCalculation;
				}
				else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
				
			EditorGUILayout.Space();
			
			EditorGUILayout.BeginHorizontal();
				showBaseProperties=EditorGUILayout.Foldout(showBaseProperties, "Grid Properties", TBE.foldoutS);
				if(showBaseProperties) EditorGUILayout.LabelField("Active-Grid", GUILayout.MaxWidth(cacheLabelLength));
			EditorGUILayout.EndHorizontal();
			
			if(showBaseProperties){
				EditorGUILayout.BeginHorizontal();
					int gridType=(int)instance.gridType;
					cont=new GUIContent("Grid Type:", "The type of grid to use (Hex or Square)");
					contL=TBE.SetupContL(gridTypeLabel, gridTypeTooltip);
					gridType = EditorGUILayout.Popup(cont, gridType, contL);
					instance.gridType=(_GridType)gridType;
					
					EditorGUILayout.LabelField(instance.cacheGridType==_GridType.HexGrid ? "Hex" : "Square", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();
					int colliderType=(int)instance.colliderType;
					cont=new GUIContent("Collider Type:", "The type of collider to use (The collider are used for cursor detection)");
					contL=TBE.SetupContL(colliderTypeLabel, colliderTypeTooltip);
					colliderType = EditorGUILayout.Popup(cont, colliderType, contL);
					instance.colliderType=(_GridColliderType)colliderType;
				
					EditorGUILayout.LabelField(instance.cacheColliderType==_GridColliderType.Master ? "Master" : "Individual", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Diagonal Neighbor:", "Check to enable diagonal neighbor");
					if(instance.gridType==_GridType.HexGrid) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else instance.enableDiagonalNeighbour=EditorGUILayout.Toggle(cont, instance.enableDiagonalNeighbour);
					
					if(instance.gridType!=_GridType.HexGrid) EditorGUILayout.LabelField(instance.cacheDiagonalNeighbour.ToString(), GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Grid Width:", "The number of tile in the grid in x-axis");
					instance.dimensionX=EditorGUILayout.IntField(cont, instance.dimensionX);
				
					EditorGUILayout.LabelField(instance.cacheDimensionX.ToString(), GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Grid Length:", "The number of tile in the grid in z-axis");
					instance.dimensionZ=EditorGUILayout.IntField(cont, instance.dimensionZ);
				
					EditorGUILayout.LabelField(instance.cacheDimensionZ.ToString(), GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Node Size:", "The size of individual node in the grid");
					instance.nodeSize=EditorGUILayout.FloatField(cont, instance.nodeSize);
				
					EditorGUILayout.LabelField(instance.cacheNodeSize.ToString("f2"), GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Node Spacing:", "The spacing between nodes in the grid");
					instance.nodeSpacing=EditorGUILayout.FloatField(cont, instance.nodeSpacing);
					
					EditorGUILayout.LabelField(instance.cacheNodeSpacing.ToString("f2"), GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Unwalkable Rate:", "The percentage of the unwalkable tile on the grid.\nTakes value from 0-1 with 0.25 means 25% of the grid will not be walkabe");
					instance.unwalkableRate=EditorGUILayout.FloatField(cont, instance.unwalkableRate);
					EditorGUILayout.LabelField("", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Obstacle Rate:", "The percentage of the tile with obstacle on the grid.\nTakes value from 0-1 with 0.25 means 25% of the grid will have obstacle");
					instance.obstacleRate=EditorGUILayout.FloatField(cont, instance.obstacleRate);
					EditorGUILayout.LabelField("", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Wall/Fence Rate:", "The percentage of the tile with wall(s) on the grid.\nTakes value from 0-1 with 0.25 means 25% of the grid will have wall");
					instance.wallRate=EditorGUILayout.FloatField(cont, instance.wallRate);
					EditorGUILayout.LabelField("", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();	
					cont=new GUIContent("Move Cost:", "The default move cost assigned to individual node");
					instance.defaultMoveCostPerNode=EditorGUILayout.IntField(cont, instance.defaultMoveCostPerNode);
					EditorGUILayout.LabelField("", GUILayout.MaxWidth(cacheLabelLength));
				EditorGUILayout.EndHorizontal();
			}
			
			EditorGUILayout.Space();
			
			I_GridEditor.DrawGridEditor(instance);
			
			EditorGUILayout.Space();
			EditorGUILayout.Space();
			
			//DefaultInspector(0);	//DrawDefaultInspector();
			GridManager.inspector=DefaultInspector(GridManager.inspector);
		}
		
		public void OnSceneGUI(){
			I_GridEditor.OnSceneGUI();
		}
		
	}
	
}
