using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(TurnControl))]
	public class I_TurnControlEditor : TBEditorInspector {
		
		//private TurnControl instance;
		
		public override void Awake(){
			base.Awake();
			//instance = (TurnControl)target;
		}
		
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			//EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			TurnControl.inspector=DefaultInspector(TurnControl.inspector, 0);
		}
		
	}
	
}
