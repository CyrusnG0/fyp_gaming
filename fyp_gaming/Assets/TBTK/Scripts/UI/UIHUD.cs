using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIHUD : UIScreen {
		
		public int maxUnitOrderItem;
		public List<UIButton> unitOrderList=new List<UIButton>();
		
		[Space(8)]
		public UIButton buttonPerk;
		public UIButton buttonPause;
		public UIButton buttonEndTurn;
		
		private static UIHUD instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		//public override void Start(){
		public new IEnumerator Start(){
			if(TurnControl.IsUnitPerTurn()){
				for(int i=0; i<maxUnitOrderItem; i++){
					if(i>0) unitOrderList.Add(UIButton.Clone(unitOrderList[0].rootObj, "UnitOrderItem"+(i)));
					unitOrderList[i].Init();
					
					//int idx=i;	unitOrderList[i].button.onClick.AddListener(delegate { OnUnitOrderItem(idx); });
					//unitOrderList[i].SetCallback(this.OnHoverUnitOrderItem, this.OnExitUnitOrderItem);
					
					unitOrderList[i].rootObj.SetActive(false);
				}
			}
			else unitOrderList[0].rootObj.SetActive(false);
			
			
			if(UIControl.EnablePerkMenu()){
				buttonPerk.Init();
				buttonPerk.button.onClick.AddListener(delegate { OnPerkButton(); });
			}
			else buttonPerk.rootObj.SetActive(false);
			
			buttonPause.Init();
			buttonPause.button.onClick.AddListener(delegate { OnPauseButton(); });
			
			buttonEndTurn.Init();
			buttonEndTurn.button.onClick.AddListener(delegate { OnEndTurnButton(); });
			buttonEndTurn.SetActive(false);
			
			
			if(UnitManager.RequireManualDeployment()){
				canvasGroup.interactable=false;
				canvasGroup.blocksRaycasts=false;
				
				canvasGroup.alpha=0;
				while(UnitManager.DeployingUnit()) yield return null;
				
				canvasGroup.interactable=true;
				canvasGroup.blocksRaycasts=true;
			}
			
			canvasGroup.alpha=1;
		}
		
		
		void OnEnable(){
			TBTK.onSelectUnitE += OnSelectUnit ;
			TBTK.onActionInProgressE += OnActionInProgress ;
			
			TBTK.onNewTurnE += OnNewTurn ;
			TBTK.onUnitDestroyedE += OnUnitDestroyed ;
			
			TBTK.onUnitOrderChangedE += UpdateUnitOrderList ;
		}
		void OnDisable(){
			TBTK.onSelectUnitE -= OnSelectUnit ;
			TBTK.onActionInProgressE -= OnActionInProgress ;
			
			TBTK.onNewTurnE -= OnNewTurn ;
			TBTK.onUnitDestroyedE -= OnUnitDestroyed ;
			
			TBTK.onUnitOrderChangedE -= UpdateUnitOrderList ;
		}
		
		
		void OnSelectUnit(Unit unit){
			if(unit==null || !unit.playableUnit) return;
			buttonEndTurn.SetActive(true);
			if(UIControl.EnablePerkMenu()) buttonPerk.SetActive(true);
		}
		void OnActionInProgress(bool flag){
			buttonEndTurn.button.interactable=!flag;
			if(UIControl.EnablePerkMenu()) buttonPerk.button.interactable=!flag;
		}
		
		
		void OnNewTurn(){
			UpdateUnitOrderList();
		}
		void OnUnitDestroyed(Unit unit){
			UpdateUnitOrderList();
		}
		void UpdateUnitOrderList(){
			if(!TurnControl.IsUnitPerTurn()) return;
			
			List<Unit> unitList=UnitManager.GetAllUnitList();
			for(int i=0; i<unitOrderList.Count; i++){
				if(i<unitList.Count){
					unitOrderList[i].SetImage(unitList[i].icon);
					unitOrderList[i].SetHighlight(i==TurnControl.GetTurn());
					unitOrderList[i].SetActive(true);
				}
				else unitOrderList[i].SetActive(false);
			}
		}
		
		
		//void OnUnitOrderItem(int idx){
		//	//CameraControl.SetPosition(UnitManager.GetAllUnitList()[idx].GetPos());
		//	CameraControl.FocusOnUnit(UnitManager.GetAllUnitList()[idx]);
		//}
		
		
		public void OnPerkButton(){
			UIPerkScreen.Show();
		}
		public void OnPauseButton(){
			UIPauseMenu.Show();
		}
		public void OnEndTurnButton(){
			if(!GameControl.EndTurn()) return;
			
			if(UIControl.EnablePerkMenu()) buttonPerk.SetActive(false);
			
			buttonEndTurn.SetActive(false);
			UIAbilityUnit.UpdateDisplay(null);
			UIAbilityFaction.UpdateDisplay(null);
			
			UIOverlayUnit.EndTurn();
		}
		
		
		public static void DisablePerkButton(){ instance.buttonPerk.rootObj.SetActive(false); }
		
		
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