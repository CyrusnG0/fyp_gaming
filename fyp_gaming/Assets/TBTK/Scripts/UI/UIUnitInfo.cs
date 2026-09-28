using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIUnitInfo : UIScreen {
		
		public Text labelName;
		
		public Slider sliderHP;
		public Slider sliderAP;
		
		public Text labelHP;
		public Text labelAP;
		
		public Text labelStats;
		
		private int abilityItemLimit=5;
		public List<UIObject> abilityItemList=new List<UIObject>();
		
		private int effectItemLimit=8;
		public List<UIObject> effectItemList=new List<UIObject>();
		
		public RectTransform windowRect;
		
		[Space(5)]
		public UIButton buttonClose;
		public UIButton buttonCloseBG;
		
		private static UIUnitInfo instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			for(int i=0; i<abilityItemLimit; i++){
				if(i>0) abilityItemList.Add(UIButton.Clone(abilityItemList[0].rootObj, "AbilityItem"+(i)));
				abilityItemList[i].Init();
				abilityItemList[i].SetCallback(this.OnHoverABItem, this.OnExitABItem);
				abilityItemList[i].SetActive(false);
			}
			
			for(int i=0; i<effectItemLimit; i++){
				if(i>0) effectItemList.Add(UIButton.Clone(effectItemList[0].rootObj, "EffectItem"+(i)));
				effectItemList[i].Init();
				effectItemList[i].SetCallback(this.OnHoverEffItem, this.OnExitEffItem);
				effectItemList[i].SetActive(false);
			}
			
			buttonClose.Init();
			buttonClose.button.onClick.AddListener(delegate { OnCloseButton(); });
			
			buttonCloseBG.Init();
			buttonCloseBG.button.onClick.AddListener(delegate { OnCloseButton(); });
			
			thisObj.SetActive(false);
		}
		
		
		public void OnCloseButton(){
			Hide();
		}
		
		void Update(){
			if(Input.GetKeyDown(KeyCode.Escape)) OnCloseButton();
		}
		
		
		private Unit currentUnit;
		
		public void UpdateDisplay(Unit unit){
			currentUnit=unit;
			
			labelName.text=unit.itemName;
			
			sliderHP.value=unit.GetHPRatio();
			sliderAP.value=unit.GetAPRatio();
			
			labelHP.text=unit.hp+"/"+unit.GetFullHP();
			labelAP.text=unit.ap+"/"+unit.GetFullAP();
			
			labelStats.text=unit.GetDmgHPMin().ToString("f0")+" - "+unit.GetDmgHPMax().ToString("f0")+"\n";
			labelStats.text+=unit.GetAttack().ToString("f0")+"\n";
			labelStats.text+=unit.GetHit().ToString("f0")+"\n\n";
			labelStats.text+=unit.GetDefense().ToString("f0")+"\n";
			labelStats.text+=unit.GetDodge().ToString("f0")+"\n";
			
			for(int i=0; i<abilityItemList.Count; i++){
				if(i<unit.abilityList.Count){
					abilityItemList[i].image.sprite=unit.abilityList[i].icon;
					abilityItemList[i].SetActive(true);
				}
				else abilityItemList[i].SetActive(false);
			}
			
			for(int i=0; i<effectItemList.Count; i++){
				if(i<unit.effectList.Count){
					effectItemList[i].image.sprite=unit.effectList[i].icon;
					effectItemList[i].SetActive(true);
				}
				else effectItemList[i].SetActive(false);
			}
		}
		
		
		public void OnHoverABItem(GameObject butObj){
			int idx=UI.GetIdxFromList(abilityItemList, butObj);
			
			//Vector3 sPos=UI.GetCorner(abilityItemList[idx].rectT, 1)+new Vector3(0, 10*abilityItemList[idx].rectT.lossyScale.y, 0);
			Vector3 sPos=UI.GetCorner(windowRect, 0);//+new Vector3(0, 10*abilityItemList[idx].rectT.lossyScale.y, 0);
			UITooltip.Show(currentUnit.GetAbility(idx), sPos, new Vector3(-10, 115), 3);
		}
		public void OnExitABItem(GameObject butObj){
			UITooltip.HideTooltip();
		}
		
		
		public void OnHoverEffItem(GameObject butObj){
			int idx=UI.GetIdxFromList(effectItemList, butObj);
			
			Vector3 sPos=UI.GetCorner(windowRect, 0);//+new Vector3(0, 10*abilityItemList[idx].rectT.lossyScale.y, 0);
			UITooltip.Show(currentUnit.GetEffect(idx), sPos, new Vector3(-10, 115), 3);
		}
		public void OnExitEffItem(GameObject butObj){
			UITooltip.HideTooltip();
		}
		
		
		public static void Show(Unit unit){ instance._Show(unit); }
		public void _Show(Unit unit){
			UpdateDisplay(unit);
			if(thisObj.activeInHierarchy) return;
			base.Show(.1f);
		}
		public static void Hide(){ instance._Hide(); }
		public void _Hide(){
			base.Hide(.1f);
		}
		
	}

}