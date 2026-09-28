using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UITooltip : UIScreen {
		
		//~ public int buttonLimit=8;
		//~ public List<UIButton> buttonList=new List<UIButton>();
		
		public GameObject tooltipObj;
		public Text tooltipLabel;
		
		private RectTransform tooltipRectT;
		private RectTransform labelRectT;
		
		private static UITooltip instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			canvasGroup.interactable=false;
			canvasGroup.blocksRaycasts=false;
			
			tooltipRectT=tooltipObj.GetComponent<RectTransform>();
			labelRectT=tooltipLabel.gameObject.GetComponent<RectTransform>();
			
			tooltipObj.SetActive(false);
			
			canvasGroup.alpha=1;
		}
		
		
		void OnEnable(){
			TBTK.onActionInProgressE += OnActionInProgress ;
		}
		void OnDisable(){
			TBTK.onActionInProgressE -= OnActionInProgress ;
		}
		
		
		void OnActionInProgress(bool flag){
			if(flag && tooltipObj.activeInHierarchy) HideTooltip();
		}
		
		
		public static void Show(Effect effect, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){ 
			if(effect==null) return;
			//~ instance.StartCoroutine(instance._ShowEffect(ability, screenPos, useWorldSpace));
			instance.StartCoroutine(instance._ShowEffect(effect, screenPos, offset, pivot));
		}
		public IEnumerator _ShowEffect(Effect effect, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){
			string text="<b><size="+(tooltipLabel.fontSize+5)+">"+effect.name+"</size></b>\n\n";
			text+="<i><b>"+effect.desp+"</b></i>";
			text+="<i>\n\n"+effect.durationRemain+" "+(effect.durationRemain>1 ? "turns" : "turn")+" left</i>";
			
			tooltipLabel.text=text;
			tooltipLabel.lineSpacing=1.0f;
			
			if(pivot==0) tooltipRectT.pivot=new Vector2(0, 0);
			if(pivot==1) tooltipRectT.pivot=new Vector2(0, 1);
			if(pivot==2) tooltipRectT.pivot=new Vector2(1, 1);
			if(pivot==3) tooltipRectT.pivot=new Vector2(1, 0);
			
			tooltipObj.SetActive(true);	canvasGroup.alpha=0;
			yield return null;
			
			//tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//if(useWorldSpace) tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//else tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.localPosition+=new Vector3(offset.x, offset.y, 0);//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.sizeDelta=labelRectT.sizeDelta+new Vector2(40, 30);
			
			canvasGroup.alpha=1;
		}
		
		
		
		public static void ShowAura(Effect effect, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){ 
			if(effect==null) return;
			//~ instance.StartCoroutine(instance._ShowEffect(ability, screenPos, useWorldSpace));
			instance.StartCoroutine(instance._ShowAura(effect, screenPos, offset, pivot));
		}
		public IEnumerator _ShowAura(Effect effect, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){
			string text="<b><size="+(tooltipLabel.fontSize+5)+">"+effect.name+"</size></b>\n\n";
			text+="<i><b>"+effect.desp+"</b></i>";
			
			tooltipLabel.text=text;
			tooltipLabel.lineSpacing=1.0f;
			
			if(pivot==0) tooltipRectT.pivot=new Vector2(0, 0);
			if(pivot==1) tooltipRectT.pivot=new Vector2(0, 1);
			if(pivot==2) tooltipRectT.pivot=new Vector2(1, 1);
			if(pivot==3) tooltipRectT.pivot=new Vector2(1, 0);
			
			tooltipObj.SetActive(true);	canvasGroup.alpha=0;
			yield return null;
			
			//tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//if(useWorldSpace) tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//else tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.localPosition+=new Vector3(offset.x, offset.y, 0);//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.sizeDelta=labelRectT.sizeDelta+new Vector2(40, 30);
			
			canvasGroup.alpha=1;
		}
		
		
		
		public static void Show(Ability ability, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){ 
			if(ability==null) return;
			//~ instance.StartCoroutine(instance._ShowAbility(ability, screenPos, useWorldSpace));
			instance.StartCoroutine(instance._ShowAbility(ability, screenPos, offset, pivot));
		}
		public IEnumerator _ShowAbility(Ability ability, Vector3 screenPos=default(Vector3), Vector2 offset=default(Vector2), int pivot=0){//, bool useWorldSpace=true){
			string text="<b><size="+(tooltipLabel.fontSize+5)+">"+ability.name+"</size></b>\n\n";
			text+="<i><b>"+ability.desp+"</b></i>";
			if(ability.GetCurrentCD()>0) text+="<i>\n\nCooldown: "+ability.GetCurrentCD()+"-"+(ability.GetCurrentCD()>1 ? "turns" : "turn")+"</i>";
			
			tooltipLabel.text=text;
			tooltipLabel.lineSpacing=1.0f;
			
			if(pivot==0) tooltipRectT.pivot=new Vector2(0, 0);
			if(pivot==1) tooltipRectT.pivot=new Vector2(0, 1);
			if(pivot==2) tooltipRectT.pivot=new Vector2(1, 1);
			if(pivot==3) tooltipRectT.pivot=new Vector2(1, 0);
			
			tooltipObj.SetActive(true);	canvasGroup.alpha=0;
			yield return null;
			
			//tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//if(useWorldSpace) tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			//else tooltipRectT.localPosition=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.position=screenPos;//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.localPosition+=new Vector3(offset.x, offset.y, 0);//GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.sizeDelta=labelRectT.sizeDelta+new Vector2(40, 30);
			
			canvasGroup.alpha=1;
		}
		
		
		public static void ShowAttackInfo(Attack attack){ 
			instance.StartCoroutine(instance._ShowAttackInfo(attack));
		}
		public IEnumerator _ShowAttackInfo(Attack attack){
			Vector3 screenPos1 = Camera.main.WorldToScreenPoint(attack.srcUnit.GetPos());
			Vector3 screenPos2 = Camera.main.WorldToScreenPoint(attack.tgtUnit.GetPos());
			
			float offsetX=0;		float fontSize=tooltipLabel.fontSize;
			
			if(screenPos1.x>screenPos2.x){ tooltipRectT.pivot=new Vector2(1, 0); offsetX=-20; }
			if(screenPos1.x<screenPos2.x){ tooltipRectT.pivot=new Vector2(0, 0); offsetX=20; }
			
			//float textSizeH=(tooltipLabel.fontSize+6);///UI.GetScaleFactor();
			
			string text="";
			text+="<i>damage:	<b><size="+(fontSize+10)+">"+attack.damageHPMin.ToString("f0")+" - "+attack.damageHPMax.ToString("f0")+"</size></b></i>\n";
			text+="<i>hit: 			<b><size="+(fontSize+0)+">"+(attack.hitChance*100).ToString("f0")+"%</size></b></i>\n";
			text+="<i>critical:		<b><size="+(fontSize+0)+">"+(attack.critChance*100).ToString("f0")+"%</size></b></i>";
			
			if(attack.cover==1) text+="\n\ntarget in <i><b>Half-Cover</b></i>";
			if(attack.cover==2) text+="\n\ntarget in <i><b>Full-Cover</b></i>";
			
			tooltipLabel.text=text;
			tooltipLabel.lineSpacing=1.2f;
			
			tooltipObj.SetActive(true);	canvasGroup.alpha=0;
			yield return null;
			
			tooltipRectT.localPosition=GetScreenPos(attack.tgtUnit.GetPos(), new Vector3(offsetX, 20));
			tooltipRectT.sizeDelta=labelRectT.sizeDelta+new Vector2(40, 30);
			
			canvasGroup.alpha=1;
		}
		
		public static void HideTooltip(){
			instance.tooltipObj.SetActive(false);
		}
		
		
		public static Vector3 GetScreenPos(Vector3 point, Vector3 offset=default(Vector3)){
			Vector3 screenPos=Camera.main.WorldToScreenPoint(point);	screenPos.z=0;
			return (screenPos+offset)*UI.GetScaleFactor();	
		}
		
	}

}