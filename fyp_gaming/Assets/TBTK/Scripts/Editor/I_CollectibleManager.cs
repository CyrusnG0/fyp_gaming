using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(CollectibleManager))]
	public class I_CollectibleManagerEditor : TBEditorInspector {
		
		private CollectibleManager instance;
		
		public override void Awake(){
			base.Awake();
			instance = (CollectibleManager)target;
		}
		
		
		public override void OnInspectorGUI(){
			Undo.RecordObject(instance, "CollectibleManager");
			
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			EditorGUIUtility.labelWidth=140;
			
				//~ cont=new GUIContent("Generate On Start:", "Check to ");
				//~ instance.generateItemOnStart=EditorGUILayout.Toggle(cont, instance.generateItemOnStart);
				
				cont=new GUIContent("Active Item Limit:", "The maximum amount of collectible available on the grid at any given time");
				instance.activeItemLimit=EditorGUILayout.IntField(cont, instance.activeItemLimit);
			
			EditorGUILayout.Space();
			
				cont=new GUIContent("Generate In Game:", "Check to enable spawning of collectible item during runtime at the end of each turn");
				instance.generateInGame=EditorGUILayout.Toggle(cont, instance.generateInGame);
				
				cont=new GUIContent(" - Max Spawn Per Turn:", "The maximum amount of collectible to be spawned at each turn");
				if(!instance.generateInGame) EditorGUILayout.LabelField(" - Max Spawn Per Turn:", "n/a");
				else instance.maxSpawnPerTurn=EditorGUILayout.IntField(cont, instance.maxSpawnPerTurn);
				
				cont=new GUIContent(" - Spawn Chance:", "The success rate of a collectible to be spawned at each spawning attempt during runtime");
				if(!instance.generateInGame) EditorGUILayout.LabelField(" - Spawn Chance:", "n/a");
				else instance.spawnChance=EditorGUILayout.FloatField(cont, instance.spawnChance);
			
			EditorGUIUtility.labelWidth=0;
			EditorGUILayout.Space();
			
				cont=new GUIContent("Effect On Spawn:", "The effect object to be spawned when a new collectible item is spawned during runtime");
				DrawVisualObject(instance.effectOnSpawn, cont, 140);
			
			EditorGUILayout.Space();
			
			DrawItemList();
			
			EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			CollectibleManager.inspector=DefaultInspector(CollectibleManager.inspector, 0);
		}
		
		
		
		private bool showItemList=true;
		void DrawItemList(){
			//EditorGUILayout.BeginHorizontal();
			//EditorGUILayout.LabelField("", GUILayout.MaxWidth(10));
			showItemList=EditorGUILayout.Foldout(showItemList, "Show Perk List");
			//EditorGUILayout.EndHorizontal();
			if(showItemList){
				
				EditorGUILayout.BeginHorizontal();
				if(GUILayout.Button("EnableAll") && !Application.isPlaying){
					instance.unavailableIDList.Clear();
				}
				if(GUILayout.Button("DisableAll") && !Application.isPlaying){
					instance.unavailableIDList=CollectibleDB.GetPrefabIDList();
				}
				EditorGUILayout.EndHorizontal();
				
				for(int i=0; i<TBE.collectibleDB.collectibleList.Count; i++) DrawItem(TBE.collectibleDB.collectibleList[i]);
			}
		}
		
		void DrawItem(Collectible item){
			EditorGUILayout.BeginHorizontal();
			
				GUILayout.Box("", GUILayout.Width(40),  GUILayout.Height(40));
				TBE.DrawSprite(GUILayoutUtility.GetLastRect(), item.icon, item.desp, false);
			
				GUILayout.BeginVertical();
					EditorGUILayout.Space();
					GUILayout.Label(item.name, GUILayout.ExpandWidth(false));
			
					GUILayout.BeginHorizontal();
						bool flag=!instance.unavailableIDList.Contains(item.prefabID) ? true : false;
						//if(Application.isPlaying) flag=!flag;	//switch it around in runtime
						EditorGUILayout.LabelField(new GUIContent(" - enabled: ", "check to enable the item in this level"), GUILayout.Width(70));
						flag=EditorGUILayout.Toggle(flag);
			
						if(!Application.isPlaying){
							if(flag) instance.unavailableIDList.Remove(item.prefabID);
							else{
								if(!instance.unavailableIDList.Contains(item.prefabID)){
									instance.unavailableIDList.Add(item.prefabID);
								}
							}
						}
						
						//~ if(!instance.unavailableIDList.Contains(item.prefabID)){
							//~ flag=instance.unlockedIDList.Contains(item.prefabID);
							//~ EditorGUILayout.LabelField(new GUIContent("- unlocked:", "Check to set the item as unlocked right from the start"), GUILayout.Width(75));
							//~ flag=EditorGUILayout.Toggle(flag);
							//~ if(!flag) instance.unlockedIDList.Remove(item.prefabID);
							//~ else if(!instance.unlockedIDList.Contains(item.prefabID)) instance.unlockedIDList.Add(item.prefabID);
						//~ }
						
					GUILayout.EndHorizontal();
					
				GUILayout.EndVertical();
			
			EditorGUILayout.EndHorizontal();
		}
		
		
	}
	
}
