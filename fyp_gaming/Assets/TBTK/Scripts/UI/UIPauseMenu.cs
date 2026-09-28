using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIPauseMenu : UIScreen {
		
		public UIButton buttonResume;
		public UIButton buttonRestart;
		public UIButton buttonMainMenu;
		
		private static UIPauseMenu instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			buttonResume.Init();
			buttonResume.button.onClick.AddListener(delegate { OnResumeButton(); });
			
			buttonRestart.Init();
			buttonRestart.button.onClick.AddListener(delegate { OnRestartButton(); });
			
			buttonMainMenu.Init();
			buttonMainMenu.button.onClick.AddListener(delegate { OnMenuButton(); });
			
			thisObj.SetActive(false);
		}
		
		
		public void OnResumeButton(){
			Hide();
		}
		public void OnRestartButton(){
			UIControl.RestartLevel();
		}
		public void OnMenuButton(){
			UIControl.MainMenu();
		}
		
		
		void Update(){
			if(Input.GetKeyDown(KeyCode.Escape)) OnResumeButton();
		}
		
		
		public static void Show(){ instance._Show(); }
		public void _Show(){
			if(thisObj.activeInHierarchy) return;
			base.Show();
		}
		public static void Hide(){ instance._Hide(); }
		public void _Hide(){
			base.Hide();
		}
		
	}

}