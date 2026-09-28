using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(Collectible))]
	public class I_CollectibleEditor : TBEditorInspector {
		
		private Collectible instance;
		
		public override void Awake(){
			base.Awake();
			instance = (Collectible)target;
		}
		
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			
			bool isInDB=CollectibleDB.GetPrefabIDList().Contains(instance.prefabID);
			if(!isInDB){ instance.prefabID=-1; EditorUtility.SetDirty(instance); }
			
			#if UNITY_2018_3_OR_NEWER
				bool isPrefab=PrefabUtility.GetPrefabAssetType(instance)==PrefabAssetType.Regular;
				if(isPrefab){
					if(isInDB){
						if(GUILayout.Button("Collectible Editor Window")) CollectibleEditorWindow.Init(instance.prefabID);
					}
					else NotInDB();
				}
				else NotAPrefab();
			
			#else
				PrefabType type=PrefabUtility.GetPrefabType(instance);
				if(type==PrefabType.Prefab || type==PrefabType.PrefabInstance){
					if(isInDB){
						if(GUILayout.Button("Collectible Editor Window")) CollectibleEditorWindow.Init(instance.prefabID);
					}
					else NotInDB();
				}
				else NotAPrefab();
				
			#endif
			
			//DrawDefaultInspector();
			Collectible.inspector=DefaultInspector(Collectible.inspector);
		}
		
		
		private void NotInDB(){
			string text="Item won't be available to be deployed to game, or accessible in CollectibleEditor until it's it has been added to TBTK database.";
			text+="\n\nYou can still edit the item using default inspector. However it's not recommended";
			EditorGUILayout.HelpBox(text, MessageType.Warning);
			
			if(GUILayout.Button("Add Prefab to Database")){
				#if UNITY_2018_3_OR_NEWER
					string assetPath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
					GameObject rootObj = (GameObject)AssetDatabase.LoadAssetAtPath(assetPath, typeof(GameObject));
					Collectible rootInstance=rootObj.GetComponent<Collectible>();
					
					CollectibleEditorWindow.Init();
					CollectibleEditorWindow.NewItem(rootInstance);
					CollectibleEditorWindow.Init(rootInstance.prefabID);		//call again to select the instance in editor window
					
					instance.prefabID=rootInstance.prefabID;
				#else
					CollectibleEditorWindow.Init();
					CollectibleEditorWindow.NewItem(instance);
					CollectibleEditorWindow.Init(instance.prefabID);		//call again to select the instance in editor window
				#endif
			}
			
			EditorGUILayout.Space();
			
			if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init();
		}
		private void NotAPrefab(){
			string text="Item won't be available to be deployed to game, or accessible in CollectibleEditor until it's made a prefab and added to TBTK database.";
			text+="\n\nYou can still edit the item using default inspector. However it's not recommended";
			EditorGUILayout.HelpBox(text, MessageType.Warning);
			
			EditorGUILayout.Space();
			
			if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init();
		}
		
	}
	
}
