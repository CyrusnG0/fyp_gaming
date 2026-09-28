using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(UnitManager))]
	public class I_UnitManagerEditor : TBEditorInspector {
		
		//private UnitManager instance;
		
		public override void Awake(){
			base.Awake();
			//instance = (UnitManager)target;
		}
		
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			EditorGUILayout.Space();
			
			//EditorGUIUtility.labelWidth=190;
			
			//cont=new GUIContent("Enable Manual Unit Deployment:", "Check to let the player to place starting faction manually before the game starts");
			//instance.enableManualDeployment=EditorGUILayout.Toggle(cont, instance.enableManualDeployment);
			
			//EditorGUIUtility.labelWidth=0;
			
			
			EditorGUILayout.Space();
			
			if(GUILayout.Button("Open Window-Editor")) umEditorWindow.Init();
			
			//EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			DefaultInspector();
			//UnitManager.inspector=DefaultInspector(UnitManager.inspector);
		}
		
	}
	
}
