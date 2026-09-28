using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIDeployment : UIScreen {
		
		public int buttonLimit=8;
		public List<UIButton> buttonList=new List<UIButton>();
		
		public UIButton buttonScrollLeft;
		public UIButton buttonScrollRight;
		
		public UIButton buttonAutoDeploy;
		public UIButton buttonEndDeployment;
		
		private static UIDeployment instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			for(int i=0; i<buttonLimit; i++){
				if(i>0) buttonList.Add(UIButton.Clone(buttonList[0].rootObj, "DeployButton"+(i)));
				buttonList[i].Init();
				
				int idx=i;	buttonList[i].button.onClick.AddListener(delegate { OnButton(idx); });
				buttonList[i].SetCallback(this.OnHoverButton, this.OnExitButton);
				
				buttonList[i].SetHighlight(i==deployIdx);
				
				buttonList[i].SetActive(false);
			}
			
			
			
			buttonScrollLeft.Init();
			buttonScrollLeft.button.onClick.AddListener(delegate { OnScrollLeft(); });
			
			buttonScrollRight.Init();
			buttonScrollRight.button.onClick.AddListener(delegate { OnScrollRight(); });
			
			buttonAutoDeploy.Init();
			buttonAutoDeploy.button.onClick.AddListener(delegate { OnAutoDeployButton(); });
			
			buttonEndDeployment.Init();
			buttonEndDeployment.button.onClick.AddListener(delegate { OnEndDeploymentButton(); });
			
			
			if(UnitManager.RequireManualDeployment()){
				UI.FadeIn(canvasGroup, 0.25f);
				UpdateDisplay(UnitManager.GetDeployingFacIdx());
			}
			else thisObj.SetActive(false);
		}
		
		[Space(8)] private int deployIdx=0;
		public void OnButton(int idx){
			buttonList[idx].SetHighlight(true);
			buttonList[deployIdx].SetHighlight(false);
			
			deployIdx=idx;
		}
		
		public void OnHoverButton(GameObject butObj){
			
		}
		public void OnExitButton(GameObject butObj){
			
		}
		
		
		public void OnScrollLeft(){
			if(curFaction.deployingList.Count<=1) return;
			curFaction.deployingList.Add(curFaction.deployingList[0]);
			curFaction.deployingList.RemoveAt(0);
			UpdateDisplay();
		}
		public void OnScrollRight(){
			if(curFaction.deployingList.Count<=1) return;
			curFaction.deployingList.Insert(0, curFaction.deployingList[curFaction.deployingList.Count-1]);
			curFaction.deployingList.RemoveAt(curFaction.deployingList.Count-1);
			UpdateDisplay();
		}
		
		
		public void OnAutoDeployButton(){
			curFaction.DeployUnit();
			UpdateDisplay();
		}
		
		
		public void OnEndDeploymentButton(){
			//confirmation prompt
			if(!UnitManager.CheckDeploymentHasUnitOnGrid()){
				string text="Must at least have one unit deployed!";
				UIPrompt.Show1(text, null);
				return;
			}
			
			if(!UnitManager.IsDeploymentDone()){
				string text="Not all unit has been deployed\nContinue anyway?";
				UIPrompt.Show2(text, EndDeployment, null);
				return;
			}
			
			EndDeployment();
		}
		public void EndDeployment(){
			int nextFacIdx=UnitManager.EndDeployment();
			
			if(nextFacIdx>=0) StartCoroutine(NextFaction(nextFacIdx));
			else UI.FadeOut(canvasGroup, 0.25f, thisObj);	//base.Hide();
		}
		
		public IEnumerator NextFaction(int nextFacIdx){
			UI.FadeOut(canvasGroup, 0.25f);
			yield return new WaitForSeconds(0.5f);
			UpdateDisplay(nextFacIdx);
			UI.FadeIn(canvasGroup, 0.25f);
		}
		
		
		
		void Update(){
			if(Input.GetKeyDown(KeyCode.Return)) OnEndDeploymentButton();
			
			if(UI.IsCursorOnUI()) return;
			
			if(Input.GetMouseButtonDown(0)){
				Node node=UIInput.GetNodeFromCursor();
				if(node!=null){
					UnitManager.DeployUnit(node, deployIdx);
					UpdateDisplay();
				}
			}
		}
		
		
		
		private Faction curFaction;
		
		public static void UpdateDisplay(int facIdx=-1){ instance._UpdateDisplay(facIdx); }
		public void _UpdateDisplay(int facIdx=-1){
			//Debug.Log("_UpdateDisplay   "+fac.factionID);
			
			if(facIdx>=0) curFaction=UnitManager.GetFaction(facIdx);
			
			List<Unit> unitList=new List<Unit>( curFaction.deployingList );
			//~ int idx=UnitManager.GetCurrentDeployingUnitIdx();
			//~ for(int i=idx; i<curFac.deployingList.Count; i++) unitList.Add(curFac.deployingList[i]);
			//~ for(int i=0; i<idx; i++) unitList.Add(curFac.deployingList[i]);
			
			for(int i=0; i<buttonList.Count; i++){
				if(i<unitList.Count){
					//Debug.Log(i+"   "+buttonList[i].image+"   "+unitList[i]);
					buttonList[i].image.sprite=unitList[i].icon;
					//buttonList[i].label.text=unitList[i];
				}
				
				buttonList[i].SetActive(i<curFaction.deployingList.Count);
			}
			
			if(unitList.Count>0){
				int newIdx=Mathf.Clamp(deployIdx, 0, Mathf.Min(buttonList.Count-1, unitList.Count-1));
				if(newIdx!=deployIdx) OnButton(newIdx);
			}
			
			//~ buttonScrollLeft.button.interactable=(unitList.Count>buttonList.Count);
			//~ buttonScrollRight.button.interactable=(unitList.Count>buttonList.Count);
			buttonScrollLeft.SetActive(unitList.Count>0);
			buttonScrollRight.SetActive(unitList.Count>0);
			buttonAutoDeploy.SetActive(unitList.Count>0);
		}
		
		
		//public static void Show(Faction fac){}// instance._Show(fac); }
		//~ public void _Show(Faction fac){
			//~ Debug.Log("_Show()   !!!!!");
			//~ //_UpdateDisplay(fac);
			//~ base.Show();
		//~ }
		
	}

}