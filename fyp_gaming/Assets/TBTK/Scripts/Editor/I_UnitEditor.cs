using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(Unit))] [CanEditMultipleObjects]
	public class I_UnitEditor : TBEditorInspector {
		
		private Unit instance;
		
		public override void Awake(){
			base.Awake();
			instance = (Unit)target;
		}
		
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			
			bool isInDB=UnitDB.GetPrefabIDList().Contains(instance.prefabID);
			if(!isInDB){ instance.prefabID=-1; EditorUtility.SetDirty(instance); }
			
			#if UNITY_2018_3_OR_NEWER
				bool isPrefab=PrefabUtility.GetPrefabAssetType(instance)==PrefabAssetType.Regular;
				if(isPrefab){
					if(isInDB){
						if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init(instance.prefabID);
					}
					else NotInDB();
				}
				else NotAPrefab();
			
			#else
				PrefabType type=PrefabUtility.GetPrefabType(instance);
				if(type==PrefabType.Prefab || type==PrefabType.PrefabInstance){
					if(isInDB){
						if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init(instance.prefabID);
					}
					else NotInDB();
				}
				else NotAPrefab();
				
			#endif
			
			//DrawDefaultInspector();
			Unit.inspector=DefaultInspector(Unit.inspector);
		}
		
		
		private void NotInDB(){
			string text="Unit won't be available to be deployed to game, or accessible in UnitEditor until it has been added to TBTK database.";
			text+="\n\nYou can still edit the unit using default inspector. However it's not recommended";
			EditorGUILayout.HelpBox(text, MessageType.Warning);
			
			if(GUILayout.Button("Add Prefab to Database")){
				#if UNITY_2018_3_OR_NEWER
					string assetPath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
					GameObject rootObj = (GameObject)AssetDatabase.LoadAssetAtPath(assetPath, typeof(GameObject));
					Unit rootInstance=rootObj.GetComponent<Unit>();
					
					UnitEditorWindow.Init();
					UnitEditorWindow.NewItem(rootInstance);
					UnitEditorWindow.Init(rootInstance.prefabID);		//call again to select the instance in editor window
					
					instance.prefabID=rootInstance.prefabID;
				#else
					UnitEditorWindow.Init();
					UnitEditorWindow.NewItem(instance);
					UnitEditorWindow.Init(instance.prefabID);		//call again to select the instance in editor window
				#endif
			}
			
			EditorGUILayout.Space();
			
			if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init();
		}
		private void NotAPrefab(){
			string text="Unit won't be available to be deployed to game, or accessible in UnitEditor until it's made a prefab and added to TBTK database.";
			text+="\n\nYou can still edit the unit using default inspector. However it's not recommended";
			EditorGUILayout.HelpBox(text, MessageType.Warning);
			
			EditorGUILayout.Space();
			
			if(GUILayout.Button("Unit Editor Window")) UnitEditorWindow.Init();
		}
		
	}
	
}
