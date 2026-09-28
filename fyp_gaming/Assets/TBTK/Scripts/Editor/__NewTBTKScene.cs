using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace TBTK {

	public class _NewTBTKScene : EditorWindow {

		[MenuItem ("Tools/TBTK/New Scene (Square-Grid)", false, -100)]
		private static void NewTBTKScene_Square () {
			CreateEmptyScene();
			
			GameObject obj=(GameObject)Instantiate(Resources.Load("NewScenePrefab/TBTK_SqGrid", typeof(GameObject)));
			GameObject uiObj=(GameObject)Instantiate(Resources.Load("NewScenePrefab/UI_TBTK", typeof(GameObject)));
			
			obj.name="TBTK";	uiObj.name="UI_TBTK";		uiObj.transform.parent=obj.transform;
		}
		[MenuItem ("Tools/TBTK/New Scene (Hex-Grid)", false, -100)]
		private static void NewTBTKScene_Hex () {
			CreateEmptyScene();
			
			GameObject obj=(GameObject)Instantiate(Resources.Load("NewScenePrefab/TBTK_HexGrid", typeof(GameObject)));
			GameObject uiObj=(GameObject)Instantiate(Resources.Load("NewScenePrefab/UI_TBTK", typeof(GameObject)));
			
			obj.name="TBTK";	uiObj.name="UI_TBTK";		uiObj.transform.parent=obj.transform;
		}

		static void CreateEmptyScene(){
			//EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
			
			//RenderSettings.skybox=null;
			//RenderSettings.skybox=(Material)Resources.Load("NewScenePrefab/Skybox", typeof(Material));
			
			//RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Skybox;
			//RenderSettings.ambientLight=new Color(.5f, .5f, .5f, .5f);
			
			EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
			DestroyImmediate(Camera.main.gameObject);
		}
		
	}

}