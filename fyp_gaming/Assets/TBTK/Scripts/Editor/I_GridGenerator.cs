using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(GridGenerator))]
	public class I_GridGeneratorEditor : TBEditorInspector {

		private GridGenerator instance;
		
		public override void Awake(){
			base.Awake();
			
			instance = (GridGenerator)target;
			instance.Init();
		}
		
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			//if(GUILayout.Button("Generate")) instance.Generate();
			//if(GUILayout.Button("Clear")) instance.Clear();
			
			//EditorGUILayout.Space();
			
			
			//DefaultInspector(0);		//DrawDefaultInspector();
			GridGenerator.inspector=DefaultInspector(GridGenerator.inspector);
		}
		
	}
	
}
