using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIAbilityUnit : UIScreen {
		
		private float spacing;
		private int activeAbCount;
		
		private RectTransform buttonParentRectT;
		private RectTransform buttonParentRectTAura;
		public static void SetStartingOffset(float value){
			float space=instance.activeAbCount*80 + instance.spacing ;
			
			instance.buttonParentRectT.localPosition=new Vector2(value, instance.buttonParentRectT.localPosition.y);
			instance.buttonParentRectTAura.localPosition=new Vector2(value+space, instance.buttonParentRectTAura.localPosition.y);
		}
		
		public int buttonLimit=8;
		public List<UIButton> buttonList=new List<UIButton>();
		
		[Space(8)]
		public int buttonLimit_Aura=2;
		public List<UIButton> buttonList_Aura=new List<UIButton>();
		
		private static UIAbilityUnit instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			for(int i=0; i<buttonLimit; i++){
				if(i>0) buttonList.Add(UIButton.Clone(buttonList[0].rootObj, "UAbilityButton"+(i)));
				buttonList[i].Init();
				
				int idx=i;	buttonList[i].button.onClick.AddListener(delegate { OnButton(idx); });
				buttonList[i].SetCallback(this.OnHoverButton, this.OnExitButton);
				
				//cooldownSlider.Add(abilityButtons[idx].rootT.GetChild(0).gameObject.GetComponent<Slider>());
				
				buttonList[i].SetActive(false);
			}
			
			for(int i=0; i<buttonLimit_Aura; i++){
				if(i>0) buttonList_Aura.Add(UIButton.Clone(buttonList_Aura[0].rootObj, "UAbilityButton"+(i)));
				buttonList_Aura[i].Init();
				
				//int idx=i;	buttonList_Aura[i].button.onClick.AddListener(delegate { OnButtonAura(idx); });
				buttonList_Aura[i].SetCallback(this.OnHoverButtonAura, this.OnExitButton);
				
				buttonList_Aura[i].SetActive(false);
			}
			
			
			buttonParentRectT=buttonList[0].rootT.parent.GetComponent<RectTransform>();
			buttonParentRectT.gameObject.SetActive(false);
			
			buttonParentRectTAura=buttonList_Aura[0].rootT.parent.GetComponent<RectTransform>();
			buttonParentRectTAura.gameObject.SetActive(false);
			
			spacing=buttonParentRectTAura.localPosition.x-buttonParentRectT.localPosition.x;
			
			canvasGroup.alpha=1;
		}
		
		
		void OnEnable(){
			TBTK.onSelectUnitE += OnSelectUnit ;
			TBTK.onActionInProgressE += OnActionInProgressE ;
			TBTK.onAbilityTargetingE += OnAbilityTargeting ;
		}
		void OnDisable(){
			TBTK.onSelectUnitE -= OnSelectUnit ;
			TBTK.onActionInProgressE -= OnActionInProgressE ;
			TBTK.onAbilityTargetingE -= OnAbilityTargeting ;
		}
		
		void OnSelectUnit(Unit unit){ UpdateDisplay(unit); }
		
		void OnActionInProgressE(bool flag){
			for(int i=0; i<buttonLimit; i++){
				if(!buttonList[i].rootObj.activeInHierarchy) continue;
				buttonList[i].button.interactable=!flag;
			}
		}
		
		
		public void OnButton(int idx){
			if(idx>=buttonList.Count) return;
			if(!buttonList[idx].button.interactable || !buttonList[idx].rootObj.activeInHierarchy) return;
			
			if(AbilityManager.IsWaitingForTargetU() && AbilityManager.GetSelectedIdx()==idx){
				//buttonList[AbilityManager.GetSelectedIdx()].imgHighlight.gameObject.SetActive(false);
				AbilityManager.ExitAbilityTargetMode();
				ClearSelection();
				return;
			}
			
			//ClearSelection();
			buttonList[idx].SetHighlight(true);
			UnitManager.GetSelectedUnit().SelectAbility(idx);
		}
		
		public void OnHoverButton(GameObject butObj){
			int idx=0;
			for(int i=0; i<buttonList.Count; i++){
				if(buttonList[i].rootObj==butObj){ idx=i; break; }
			}
			
			Vector3 sPos=UI.GetCorner(buttonList[idx].rectT, 1)+new Vector3(0, 10*buttonList[idx].rectT.lossyScale.y, 0);
			UITooltip.Show(UnitManager.GetSelectedUnit().GetAbility(idx), sPos, new Vector2(0, 20));
		}
		
		public void OnHoverButtonAura(GameObject butObj){
			int idx=0;
			for(int i=0; i<buttonList_Aura.Count; i++){
				if(buttonList_Aura[i].rootObj==butObj){ idx=i; break; }
			}
			
			Vector3 sPos=UI.GetCorner(buttonList_Aura[idx].rectT, 1)+new Vector3(0, 10*buttonList_Aura[idx].rectT.lossyScale.y, 0);
			UITooltip.ShowAura(UnitManager.GetSelectedUnit().GetAura(idx), sPos, new Vector2(0, 20));
		}
		
		public void OnExitButton(GameObject butObj){
			UITooltip.HideTooltip();
		}
		
		
		void Update(){
			if(Input.GetKeyDown(KeyCode.Alpha1)) OnButton(0);
			if(Input.GetKeyDown(KeyCode.Alpha2)) OnButton(1);
			if(Input.GetKeyDown(KeyCode.Alpha3)) OnButton(2);
			if(Input.GetKeyDown(KeyCode.Alpha4)) OnButton(3);
			if(Input.GetKeyDown(KeyCode.Alpha5)) OnButton(4);
			if(Input.GetKeyDown(KeyCode.Alpha6)) OnButton(5);
			if(Input.GetKeyDown(KeyCode.Alpha7)) OnButton(6);
			if(Input.GetKeyDown(KeyCode.Alpha8)) OnButton(7);
			if(Input.GetKeyDown(KeyCode.Alpha9)) OnButton(8);
			if(Input.GetKeyDown(KeyCode.Alpha0)) OnButton(9);
			
			if(Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)){
				if(AbilityManager.IsWaitingForTargetU()){
					UIInput.ClearTouchModeCursor();
					AbilityManager.ExitAbilityTargetMode();
					ClearSelection();
				}
			}
		}
		
		
		private int curHighlightIdx=-1;
		public static void OnAbilityTargeting(Ability ab){ instance._OnAbilityTargeting(ab); }
		public void _OnAbilityTargeting(Ability ab){
			if(curHighlightIdx>=0){
				if(ab==null || ab.isFacAbility || ab.index!=curHighlightIdx)
					buttonList[curHighlightIdx].SetHighlight(false);
			}
			
			if(ab!=null && ab.isUnitAbility){
				curHighlightIdx=ab.index;
				buttonList[curHighlightIdx].SetHighlight(true);
			}
		}
		
		
		public static void UpdateDisplay(Unit unit){ instance._UpdateDisplay(unit); }
		public void _UpdateDisplay(Unit unit){
			if(unit==null || !unit.playableUnit){
				buttonParentRectT.gameObject.SetActive(false);
				//for(int i=0; i<buttonLimit; i++) buttonList[i].SetActive(false);
				return;
			}
			
			int activeCount=0;
			
			for(int i=0; i<buttonLimit; i++){
				if(i<unit.abilityList.Count){
					Ability ab=unit.abilityList[i];
					
					buttonList[i].SetImage(ab.icon);
					buttonList[i].SetImage2(ab.icon);
					
					//int isAvailable=ab.IsAvailable();
					Ability._AbilityStatus abilityStatus=ab.IsAvailable();
					buttonList[i].SetImage2(false);//isAvailable!=0;
					buttonList[i].SetLabel(ab.HasUseLimit() ? ab.GetUseRemain().ToString() : "");
					//buttonList[i].label.text=ab.currentCD>0 ? ab.currentCD.ToString() : "" ;
					
					buttonList[i].SetHighlight(false);
					
					buttonList[i].button.interactable=(abilityStatus==Ability._AbilityStatus.Ready);
					
					activeCount+=1;
				}
				
				buttonList[i].SetActive(i<unit.abilityList.Count);
			}
			
			buttonParentRectT.gameObject.SetActive(activeCount>0);
			
			activeAbCount=activeCount;
			
			
			activeCount=0;
			
			for(int i=0; i<buttonLimit_Aura; i++){
				if(i<unit.auraIDList.Count){
					Effect eff=EffectDB.GetPrefab(unit.auraIDList[i]);
					buttonList_Aura[i].SetImage(eff.icon);
					activeCount+=1;
				}
				buttonList_Aura[i].SetActive(i<unit.auraIDList.Count);
			}
			
			buttonParentRectTAura.gameObject.SetActive(activeCount>0);
			
			if(activeCount>0){
				float x=buttonParentRectT.localPosition.x+activeAbCount*80+spacing ;
				instance.buttonParentRectTAura.localPosition=new Vector2(x, instance.buttonParentRectTAura.localPosition.y);
			}
		}
		
		
		
		
		
		public static void ClearSelection(){
			for(int i=0; i<instance.buttonLimit; i++) instance.buttonList[i].SetHighlight(false);
		}
		
	}

}