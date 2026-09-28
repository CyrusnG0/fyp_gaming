using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(PerkManager))]
	public class I_PerkManagerEditor : TBEditorInspector {
		
		private PerkManager instance;
		
		public override void Awake(){
			base.Awake();
			instance = (PerkManager)target;
		}
		
		
		
		public override void OnInspectorGUI(){
			Undo.RecordObject(instance, "GameControl");
			
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			EditorGUIUtility.labelWidth=170;
			
			cont=new GUIContent("In Game Scene:", "Check to indicate the PerkManager is intended for in game scene\nIf not it will be treated as a stand alone or pre-game perk screen where the effect won't actually get applied");
			instance.inGameScene=EditorGUILayout.Toggle(cont, instance.inGameScene);
			
			cont=new GUIContent("Load From Cache On Start:", "Check to load the progress from previous scene\nIf the PerkManager in any scene prior to this has 'Save To Cache On End' checked, the progress made will be carried over");
			instance.loadProgressFromCache=EditorGUILayout.Toggle(cont, instance.loadProgressFromCache);
			
			cont=new GUIContent("Save To Cache On End:", "Check to cache the progress for coming scene\nIf the PerkManager in the next (or any scene) after this has 'Load From Cache On Start' checked, any progress made in this scene will be carried over");
			instance.saveProgressToCache=EditorGUILayout.Toggle(cont, instance.saveProgressToCache);
			
			EditorGUILayout.Space();
			
			cont=new GUIContent("Currency:", "The starting value of the currency used to unlock perk");
			instance.currency=EditorGUILayout.IntField(cont, instance.currency);
			
			EditorGUIUtility.labelWidth=0;
			EditorGUILayout.Space();
			
			DrawPerkList();
			
			EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			GameControl.inspector=DefaultInspector(GameControl.inspector);
		}
		
		
		private bool showPerkList=true;
		void DrawPerkList(){
			//EditorGUILayout.BeginHorizontal();
			//EditorGUILayout.LabelField("", GUILayout.MaxWidth(10));
			showPerkList=EditorGUILayout.Foldout(showPerkList, "Show Perk List");
			//EditorGUILayout.EndHorizontal();
			if(showPerkList){
				
				EditorGUILayout.BeginHorizontal();
				if(GUILayout.Button("EnableAll") && !Application.isPlaying){
					instance.unavailableIDList.Clear();
				}
				if(GUILayout.Button("DisableAll") && !Application.isPlaying){
					instance.unlockedIDList.Clear();
					instance.unavailableIDList=PerkDB.GetPrefabIDList();
				}
				EditorGUILayout.EndHorizontal();
				
				for(int i=0; i<TBE.perkDB.perkList.Count; i++) DrawPerkItem(TBE.perkDB.perkList[i]);
			}
		}
		
		void DrawPerkItem(Perk perk){
			EditorGUILayout.BeginHorizontal();
			
				GUILayout.Box("", GUILayout.Width(40),  GUILayout.Height(40));
				TBE.DrawSprite(GUILayoutUtility.GetLastRect(), perk.icon, perk.desp, false);
			
				GUILayout.BeginVertical();
					EditorGUILayout.Space();
					GUILayout.Label(perk.name, GUILayout.ExpandWidth(false));
			
					GUILayout.BeginHorizontal();
						bool flag=!instance.unavailableIDList.Contains(perk.prefabID) ? true : false;
						//if(Application.isPlaying) flag=!flag;	//switch it around in runtime
						EditorGUILayout.LabelField(new GUIContent(" - enabled: ", "check to enable the perk in this scene"), GUILayout.Width(70));
						flag=EditorGUILayout.Toggle(flag);
			
						if(!Application.isPlaying){
							if(flag) instance.unavailableIDList.Remove(perk.prefabID);
							else{
								if(!instance.unavailableIDList.Contains(perk.prefabID)){
									instance.unavailableIDList.Add(perk.prefabID);
									instance.unlockedIDList.Remove(perk.prefabID);
								}
							}
						}
						
						if(!instance.unavailableIDList.Contains(perk.prefabID)){
							flag=instance.unlockedIDList.Contains(perk.prefabID);
							EditorGUILayout.LabelField(new GUIContent("- unlocked:", "Check to set the perk as unlocked right from the start"), GUILayout.Width(75));
							flag=EditorGUILayout.Toggle(flag);
							if(!flag) instance.unlockedIDList.Remove(perk.prefabID);
							else if(!instance.unlockedIDList.Contains(perk.prefabID)) instance.unlockedIDList.Add(perk.prefabID);
						}
						
					GUILayout.EndHorizontal();
					
				GUILayout.EndVertical();
			
			EditorGUILayout.EndHorizontal();
		}
		
	}
	
}
