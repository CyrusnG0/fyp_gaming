using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIControl : MonoBehaviour {
		
		//~ [Tooltip("Check to indicate if this is an actual game scene, false if otherwise (for setting up perk menu only scene)")]
		//~ public bool isGameScene=true;
		//~ public static bool IsGameScene(){ return instance!=null && instance.isGameScene; }
		
		
		[Space(5)][Tooltip("Check to enable touch mode intended for touch input where hover over build or ability button to bring up tooltip is not an available\n\nIn touch-mode, button with tooltip will need two click. first to bring up the tooltip, second to confirm click.\nOnly works for build button when using not using PointNBuild build mode\nOnly works for ability button that doesnt require target select")]
		public bool touchMode=false;
		public static bool InTouchMode(){ return instance!=null && instance.touchMode; }
		
		/*
		[Space(10)]
		[Tooltip("Check to have the unit HP overlay always visible")]
		public bool alwaysShowHPOverlay=false;
		public static bool AlwaysShowHPOverlay(){ return instance.alwaysShowHPOverlay; }
		[Tooltip("Check to show text overlay on attack hit")]
		public bool showTextOverlay=false;
		public static bool ShowTextOverlay(){ return instance.showTextOverlay; }
		*/
		
		
		[Space(10)]
		[Tooltip("Check to enable Perk Menu")]
		public bool enablePerkMenu=true;
		public static bool EnablePerkMenu(){ return instance.enablePerkMenu; }
		public static void DisablePerkMenu(){ 
			instance.enablePerkMenu=false;
			UIHUD.DisablePerkButton();
		}
		
		[Tooltip("Check to have unit info window pop up when right-click on unit")]
		public bool enableUnitInfo=true;
		public static bool EnableUnitInfo(){ return instance.enableUnitInfo; }
		
		
		
		[Space(10)][Tooltip("The reference width used in the canvas scaler\nThis value is used in calculation to get the overlays shows up in the right position")]
		public float scaleReferenceWidth=1600;
		public static float GetScaleReferenceWidth(){ return instance!=null ? instance.scaleReferenceWidth : 1600 ; }
		
		
		private static UIControl instance;
		
		public void Awake(){
			instance=this;
		}
		
		
		
		void OnEnable(){
			TBTK.onGameStartE += OnGameStart ;
			TBTK.onGameOverE += OnGameOver ;
			
			//TBTK.onSelectUnitE += OnSelectUnit ;
			//TBTK.onSelectFactionE += OnSelectFaction ;
			
			//TBTK.onAbilityTargetingE += OnAbilityTargeting ;
		}
		void OnDisable(){
			TBTK.onGameStartE -= OnGameStart ;
			TBTK.onGameOverE -= OnGameOver ;
			
			//TBTK.onSelectUnitE -= OnSelectUnit ;
			//TBTK.onSelectFactionE -= OnSelectFaction ;
			
			//TBTK.onAbilityTargetingE -= OnAbilityTargeting ;
		}
		
		
		void OnGameStart(){
			UIOverlayUnit.StartGame();
		}
		
		void OnGameOver(bool playerWon){
			StartCoroutine(ShowGameOver(playerWon));
		}
		IEnumerator ShowGameOver(bool playerWon){
			yield return new WaitForSeconds(1.25f);
			UIGameOver.Show(playerWon);
		}
		
		
		
		public static IEnumerator WaitForRealSeconds(float time){
			float start = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup < start + time) yield return null;
		}
		
		
		[Header("Level Management")]
		public string nextSceneName="";
		public string menuSceneName="";
		
		public static void RestartLevel(){
			Debug.Log("Restart level");
			string lvlName=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
			UnityEngine.SceneManagement.SceneManager.LoadScene(lvlName);
			Time.timeScale=1;
		}
		public static void NextLevel(){
			Debug.Log("load next level");
			UnityEngine.SceneManagement.SceneManager.LoadScene(instance.nextSceneName);
			Time.timeScale=1;
		}
		public static void MainMenu(){
			Debug.Log("load main menu");
			UnityEngine.SceneManagement.SceneManager.LoadScene(instance.menuSceneName);
			Time.timeScale=1;
		}
		
	}

}