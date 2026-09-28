using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIPerkScreen : UIScreen {
		
		[Tooltip("Check to use custom layout\n\nYou can use arrange your own layout in the scroll view\nYou will need to manually assign the item to itemList and assign the perk associate to the item")]
		public bool customLayout=false;
		public static bool UseCustomLayout(){ return instance.customLayout; }
		
		[Space(10)]
		[Tooltip("A list of all the item in the scroll view\nNeed to be manually assign when using custom layout\nFill up automatically when not using custom layout")]
		public List<UIPerkItem> itemList=new List<UIPerkItem>();
		
		//[Space(5)]
		//public RectTransform selectHighlightT;
		
		[Space(5)]
		public Text lbPerkCurrency;
		public Text lbPerkPoint;
		
		[Space(10)]
		public Text lbPerkName;
		public Text lbPerkDesp;
		public Text lbPerkUnavailable;
		public UIObject perkCostObj;
		
		public UIObject perkCurrencyObj;
		//public List<UIObject> costItemList=new List<UIObject>();
		
		[Space(10)]
		public UIButton buttonUnlock;
		public UIButton buttonClose;
		
		[Space(8)]
		[HideInInspector] public Transform perkListParent;
		[HideInInspector] public GameObject perkItemPrefab;
		
		
		private static UIPerkScreen instance;
		public void SetInstance(){ instance=this; }	//used by custom Inspector
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){ 
			
			if(!PerkManager.PerkSystemEnabled()){
				thisObj.SetActive(false);
				return;
			}
			
			//perkCostObj.Init();			perkCostObj.image.sprite=PerkDB.GetRscIcon();
			//perkCurrencyObj.Init();		perkCurrencyObj.image.sprite=PerkDB.GetRscIcon();
			
			if(customLayout){
				List<Perk> perkList=PerkManager.GetPerkList();
				for(int i=0; i<itemList.Count; i++){
					itemList[i].Init();
					int idx=i;	itemList[i].button.onClick.AddListener(delegate { OnItem(idx); });
					//itemList[i].SetCallback(OnHoverButton, OnExitButton);
					//itemList[i].SetCallback(null, null, this.OnItem, null);
					
					bool matched=false;
					for(int n=0; n<perkList.Count; n++){
						if(itemList[i].linkedPerkPID==perkList[n].prefabID){
							itemList[i].linkedPerkIdx=n;	matched=true;
							itemList[i].image.sprite=perkList[n].icon;
						}
					}
					
					if(!matched){
						Debug.LogWarning("No perk with matching prefab found");
						itemList[i].rootObj.SetActive(false);
						continue;
					}
					
					itemList[i].UnparentConnector();
					itemList[i].UnparentConnectorBase();
				}
			}
			else{
				List<Perk> perkList=PerkManager.GetPerkList();
				
				if(perkList.Count==0){
					UIControl.DisablePerkMenu();
					thisObj.SetActive(false);
					return;
				}
				
				if(perkItemPrefab==null) perkItemPrefab=itemList[0].rootObj;
				
				for(int i=0; i<perkList.Count; i++){
					//if(i>0) itemList.Add(UIPerkItem.Clone(itemList[0].rootObj, "PerkItem"+(i)));
					if(i>0) itemList.Add(UIPerkItem.Clone(perkItemPrefab, "PerkItem"+(i)));
					itemList[i].Init();
					int idx=i;	itemList[i].button.onClick.AddListener(delegate { OnItem(idx); });
					//itemList[i].SetCallback(null, null, this.OnItem, null);
					itemList[i].linkedPerkPID=perkList[i].prefabID;
					itemList[i].linkedPerkIdx=i;
					itemList[i].image.sprite=perkList[i].icon;
					
					itemList[i].rootObj.SetActive(true);
				}
			}
			
			for(int i=0; i<itemList.Count; i++) itemList[i].SetHighlight(false);
			
			buttonUnlock.Init();
			buttonUnlock.button.onClick.AddListener(delegate { OnUnlockButton(); });
			//buttonPurchase.SetCallback(null, null, this.OnPurchaseButton, null);
			
			buttonClose.Init();
			buttonClose.button.onClick.AddListener(delegate { OnCloseButton(); });
			
			//~ if(PerkManager.InGameScene()){
				//~ buttonClose.Init();
				//~ buttonClose.button.onClick.AddListener(delegate { OnCloseButton(); });
				//~ //buttonClose.SetCallback(null, null, this.OnCloseButton, null);
			//~ }
			
			//~ if(!PerkManager.InGameScene()){
				//~ canvasGroup.alpha=1;
				//~ thisObj.SetActive(true);
				//~ StartCoroutine(DelayUpdateList());
			//~ }
			//~ else{
				//~ thisObj.SetActive(false);
			//~ }
			
			thisObj.SetActive(false);
			
			OnItem(0);
		}
		
		
		//~ public IEnumerator DelayUpdateList(){
			//~ yield return null;
			//~ UpdateList();
		//~ }
		public void UpdateList(){
			for(int i=0; i<itemList.Count; i++){
				bool unlocked=PerkManager.GetPerkFromIndex(itemList[i].linkedPerkIdx).IsUnlocked();
				itemList[i].SetImage2(unlocked);
				if(itemList[i].connector!=null) itemList[i].connector.SetActive(unlocked);
				
				if(PerkManager.GetPerkFromIndex(itemList[i].linkedPerkIdx).IsAvailable()=="") itemList[i].image.material=null;
				
				//~ if(purchased) itemList[i].button.interactable=unlocked;
				//~ else itemList[i].button.interactable=PerkManager.GetPerkFromIndex(itemList[i].linkedPerkIdx).IsAvailable()=="";
			}
			
			//lbPerkCount.text="Purchased Perk: "+PerkManager.GetPurchasedPerkCount();
			
			lbPerkCurrency.text="currency: <size="+(lbPerkCurrency.fontSize+7)+">"+PerkManager.GetPerkCurrency()+"</size>";
			lbPerkPoint.text="point: <size="+(lbPerkPoint.fontSize+7)+">"+PerkManager.GetPerkPoint()+"</size>";
		}
		
		
		private int selectedIdx=0;
		public void OnItem(int idx){
			//int idx=GetItemIndex(butObj);
			
			itemList[selectedIdx].SetHighlight(false);
			itemList[idx].SetHighlight(true);
			selectedIdx=idx;
			
			//selectHighlightT.localPosition=itemList[idx].rectT.localPosition-new Vector3(35, -35, 0);
			//Debug.Log(selectHighlightT.localPosition+"  "+itemList[idx].rectT.localPosition);
			
			Perk perk=PerkManager.GetPerkOfIndex(itemList[idx].linkedPerkIdx);
			
			lbPerkName.text=perk.name;
			lbPerkDesp.text=perk.desp;
			lbPerkUnavailable.text=perk.IsAvailable();//perk.GetDespUnavailable();
			
			buttonUnlock.label.text="Unlock cost - <b><i><size="+(buttonUnlock.label.fontSize+7)+">"+perk.GetCost()+"</size></i></b>";
			
			//~ if(!PerkManager.UseRscManagerForCost()){
				//perkCostObj.label.text=perk.GetCost().ToString("f0");
			//~ }
			//~ else{
				//~ List<float> cost=perk.GetPurchaseCostRsc();
				//~ for(int i=0; i<cost.Count; i++) costItemList[i].lbMain.text=cost[i].ToString("f0");
			//~ }
			
			buttonUnlock.SetActive(!perk.IsUnlocked() & perk.IsAvailable()=="");
		}
		private int GetItemIndex(GameObject butObj){
			for(int i=0; i<itemList.Count; i++){ if(itemList[i].rootObj==butObj) return i;}
			return 0;
		}
		
		
		public void OnUnlockButton(){
			string output=PerkManager.UnlockPerk(itemList[selectedIdx].linkedPerkPID);
			if(output==""){
				UpdateList();
				buttonUnlock.SetActive(false);
				UIMessage.DisplayMessage(lbPerkName.text+" unlocked!");
			}
			else UIMessage.DisplayMessage(output);
		}
		
		public void OnCloseButton(){ Hide(); }
		
		
		
		private bool showing=true;
		public static bool IsShowing(){
			if(instance==null) return false;
			return instance.showing && instance.gameObject.activeInHierarchy;
		}
		
		
		public static void Show(){ instance._Show(); }
		public void _Show(){
			showing=true;
			UpdateList();
			base.Show();
		}
		public static void Hide(){ instance._Hide(); }
		public void _Hide(){
			showing=false;
			base.Hide();
		}
		
		
		
		
		[System.Serializable]
		public class UIPerkItem : UIButton{
			
			[HideInInspector] 
			public int linkedPerkIdx=-1;
			[Tooltip("The prefabID of the perk associated to this item")]
			public int linkedPerkPID=-1;
			
			[HideInInspector] public GameObject connector;
			[HideInInspector] public GameObject connectorB;
			
			
			public UIPerkItem(){}
			public UIPerkItem(GameObject obj){ rootObj=obj; Init(); }
			
			public override void Init(){
				base.Init();
				
				if(!UIPerkScreen.UseCustomLayout()) return;
				
				foreach(Transform child in rectT){
					if(child.name=="ConnectorBase")	connectorB=child.gameObject;
					if(child.name=="Connector")			connector=child.gameObject;
				}
			}
			
			public void UnparentConnectorBase(){
				if(connectorB==null) return;
				connectorB.transform.SetParent(rootT.parent);
				connectorB.transform.SetAsFirstSibling();
			}
			public void UnparentConnector(){
				if(connector==null) return;
				connector.transform.SetParent(rootT.parent);
				connector.transform.SetAsFirstSibling();
			}
			
			public static new UIPerkItem Clone(GameObject srcObj, string name="", Vector3 posOffset=default(Vector3)){
				GameObject newObj=UI.Clone(srcObj, name, posOffset);
				return new UIPerkItem(newObj);
			}
			
		}
	}

}
