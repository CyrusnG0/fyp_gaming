using UnityEngine;
using UnityEditor;

using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK{
	
	public class TBEditorInspector : Editor {

		protected static GUIContent cont;
		protected static GUIContent contN=GUIContent.none;
		protected static GUIContent[] contL;
		
		
		public virtual void Awake(){ TBE.Init(); }
		
		
		public override void OnInspectorGUI(){
			TBE.InitGUIStyle();
			
			
		}
		protected void CheckChange(){
			if(GUI.changed && !Application.isPlaying){
				EditorUtility.SetDirty(target);
				EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
			}
		}
		
		
		protected static bool showDefaultEditor=false;
		protected void DefaultInspector(float spacing=0){
			showDefaultEditor=DefaultInspector(showDefaultEditor, spacing);
		}
		protected bool DefaultInspector(bool flag, float spacing=0){
			EditorGUILayout.Space();
			EditorGUILayout.BeginHorizontal();
				if(spacing>0) EditorGUILayout.LabelField("", GUILayout.MaxWidth(spacing));
				flag=EditorGUILayout.Foldout(flag, "Show default editor", TBE.foldoutS);
			EditorGUILayout.EndHorizontal();
			
			if(flag) DrawDefaultInspector();
			EditorGUILayout.Space();
			
			CheckChange();
			
			return flag;
		}
		
		
		protected void DrawVisualObject(VisualObject vo, GUIContent gContent, float width=0){
			EditorGUIUtility.labelWidth=width;
			
			vo.obj=(GameObject)EditorGUILayout.ObjectField(gContent, vo.obj, typeof(GameObject), true);
			
			cont=new GUIContent(" - Auto Destroy:", "Check if the spawned effect should be destroyed automatically");
			if(vo.obj!=null) vo.autoDestroy=EditorGUILayout.Toggle(cont, vo.autoDestroy);
			else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
			
			cont=new GUIContent(" - Effect Duration:", "How long before the spawned effect object is destroyed");
			if(vo.obj!=null && vo.autoDestroy) vo.duration=EditorGUILayout.FloatField(cont, vo.duration);
			else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
		}
		
		
	}
	
}