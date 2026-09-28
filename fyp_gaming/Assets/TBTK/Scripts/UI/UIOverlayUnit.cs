using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIOverlayUnit : MonoBehaviour {
		
		public Vector2 offset;
		public static Vector3 GetPosOffset(){ return instance.offset; }
		
		[Space(10)] public List<Color> colorList=new List<Color>();
		public static Color GetColor(int idx){
			if(instance.colorList.Count>0 && idx<instance.colorList.Count) return instance.colorList[idx];
			return new Color(.2f, 1f, .2f, 1f);
		}
		
		
		[Space(10)]
		public GameObject rootOverlayItem;
		public List<UIHPOverlayItem> overlayItemList=new List<UIHPOverlayItem>();
		
		[Space(10)]
		public Sprite spriteCoverFull;
		public Sprite spriteCoverHalf;
		public static Sprite GetCoverSpriteFull(){ return instance.spriteCoverFull; }
		public static Sprite GetCoverSpriteHalf(){ return instance.spriteCoverHalf; }
		public static Sprite GetCoverSprite(int type){
			if(type==1) return GetCoverSpriteHalf();
			if(type==2) return GetCoverSpriteFull();
			return null;
		}
		
		
		private static UIOverlayUnit instance;
		
		void Awake() {
			instance=this;
			
			//~ for(int i=0; i<30; i++){
				//~ if(i==0) overlayItemList.Add(rootOverlayItem.AddComponent<UIHPOverlayItem>());
				//~ else overlayItemList.Add(UI.Clone(rootOverlayItem).GetComponent<UIHPOverlayItem>());
				
				//~ overlayItemList[i].Init();
			//~ }
			
			overlayItemList.Add(rootOverlayItem.AddComponent<UIHPOverlayItem>());
			rootOverlayItem.SetActive(false);
		}
		
		
		
		public static void StartGame(){
			List<Faction> allFacList=UnitManager.GetFactionList();
			for(int i=0; i<allFacList.Count; i++){
				for(int n=0; n<allFacList[i].unitList.Count; n++){
					AddUnit(allFacList[i].unitList[n], i);
				}
			}
		}
		
		public static void EndTurn(){
			if(!GameControl.EnableCoverSystem()) return;
			
			for(int i=0; i<instance.overlayItemList.Count; i++){
				if(instance.overlayItemList[i].unit==null) continue;
				instance.overlayItemList[i].ClearCoverDisplay();
			}
		}
		
		
		void OnEnable(){ TBTK.onNewUnitE += AddUnit; }
		void OnDisable(){ TBTK.onNewUnitE -= AddUnit; }
		
		
		public static void AddUnit(Unit unit, int colorIdx){ instance._AddUnit(unit, colorIdx); }
		public void _AddUnit(Unit unit, int colorIdx){
			int index=GetUnusedItemIndex();
			overlayItemList[index].SetUnit(unit, colorIdx);
		}
		
		//public static void RemoveUnit(Unit){ instance._RemoveUnit(unit); }
		//public void _RemoveUnit(Unit){  }
		
		
		private int GetUnusedItemIndex(){
			for(int i=0; i<overlayItemList.Count; i++){
				if(overlayItemList[i].unit!=null) continue;
				return i;
			}
			
			overlayItemList.Add(UI.Clone(rootOverlayItem).GetComponent<UIHPOverlayItem>());
			overlayItemList[overlayItemList.Count-1].Init();
			return overlayItemList.Count-1;
		}
		
	}


	public class UIHPOverlayItem : MonoBehaviour {
		
		[HideInInspector] public Unit unit;
		
		private GameObject thisObj;
		private RectTransform rectT;
		//private CanvasGroup canvasG;
		
		private Slider sliderHP;
		//private Slider sliderSH;
		
		private Image imgCover;
		
		public void Init(){
			thisObj=gameObject;
			rectT=thisObj.GetComponent<RectTransform>();
			//canvasG=thisObj.GetComponent<CanvasGroup>();
			
			foreach(Transform child in thisObj.transform){
				if(child.name=="SliderHP") sliderHP=child.GetComponent<Slider>();
				//else if(child.name=="SliderHP") sliderHP=child.GetComponent<Slider>();
				else if(child.name=="Cover") imgCover=child.GetComponent<Image>();
			}
			
			if(imgCover!=null) imgCover.enabled=false;
		}
		
		void OnDisable(){
			TBTK.onSelectUnitE -= OnSelectUnit ;
			TBTK.onActionInProgressE -= ClearCoverDisplay ;
		}
		
		public void ClearCoverDisplay(){ imgCover.enabled=false; }
		void ClearCoverDisplay(bool flag){ ClearCoverDisplay(); }
		void OnSelectUnit(Unit sUnit){
			if(sUnit!=null && !sUnit.playableUnit) return;
			if(sUnit!=null && sUnit.facID==unit.facID) return;
			if(!GameControl.EnableCoverSystem()) return;
			
			if(sUnit==null) imgCover.enabled=false;
			else{
				imgCover.sprite=UIOverlayUnit.GetCoverSprite(Attack.GetCover(sUnit.node, unit.node));
				imgCover.enabled=imgCover.sprite!=null;
			}
		}
		
		void Update(){
			if(unit==null || unit.hp<=0 || !unit.GetObj().activeInHierarchy){
				unit=null;
				thisObj.SetActive(false);
				return;
			}
			
			if(!unit.node.IsVisible()){
				rectT.localPosition=new Vector3(1, 1, 1) * -99;
				return;
			}
			
			UpdateScreenPos();
			
			sliderHP.value=unit.GetHPRatio();
			
			//~ if(!UIControl.AlwaysShowHPOverlay()){
				//~ canvasG.alpha = (slider.value>=1 && (sliderSH.value<=0 || sliderSH.value>=1)) ? 0 : 1 ;
			//~ }
		}
		
		public void SetUnit(Unit tgtUnit, int colorIdx){ 
			unit=tgtUnit;
			
			if(thisObj==null) Init();
			
			if(GameControl.EnableCoverSystem()){
				TBTK.onSelectUnitE += OnSelectUnit ;
				TBTK.onActionInProgressE += ClearCoverDisplay ;
				OnSelectUnit(UnitManager.GetSelectedUnit());
			}
			
			Update();
			
			SetColor(colorIdx);
			
			thisObj.SetActive(true);
		}
		
		public void SetColor(int colorIdx){
			Image img=sliderHP.fillRect.gameObject.GetComponent<Image>();
			img.color=UIOverlayUnit.GetColor(colorIdx);
		}
		
		void UpdateScreenPos(){
			Vector3 screenPos=Camera.main.WorldToScreenPoint(unit.GetPos());	screenPos.z=0;
			rectT.localPosition=(screenPos+UIOverlayUnit.GetPosOffset())*UI.GetScaleFactor();
		}
		
	}

}