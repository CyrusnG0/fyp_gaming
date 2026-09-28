using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using TBTK;

public class Demo_Persistent_PreGame : UIScreen {
	
	[Tooltip("The factionID of the faction in the game scene in which the unit assigned should be used")]
	public int factionID;
	
	[Space(8)]
	[Tooltip("The resource use to buy/add unit to starting unit\nLoad from cache when it's available")]
	public int resource=0;
	public static int cachedRsc=-1;
	
	[Tooltip("The value added to currency in PerkManager when the player won the last game")]
	public int winPerkCurrencyGain=1;
	[Tooltip("The value added to resource (to buy/add unit) when the player won the last game")]
	public int winResourceGain=200;
	
	[Space(8)]
	[Tooltip("Next scene to load to (the game scene)")]
	public string nextSceneName="";
	[Tooltip("Previous scene to load to (game menu)")]
	public string prevSceneName="";
	
	
	
	
	[Header("UI Element Assignment")]
	public int poolButtonLimit=20;
	public List<UIButton> poolButtonList=new List<UIButton>();
	
	public int squadLimit=6;
	public List<UIButton> squadButtonList=new List<UIButton>();
	
	public UIButton buttonAddUnit;
	public UIButton buttonPerkMenu;
	
	public UIButton buttonStartGame;
	public UIButton buttonBack;
	
	public Text lbResource;
	
	[Header("Unit Stats Display")]
	public Text lbName;
	public Text lbLabel;
	public Text lbValue;
	
	
	
	[Header("Unit Pool")]	//[HideInInspector] 
	public List<Unit> unitPoolList=new List<Unit>();
	//[HideInInspector] 
	public List<Unit> squadList=new List<Unit>();
	
	private int selectIdx=0;
	
	private Transform unitParent;
	private Vector3 spawnPos;
	
	public Transform GetUnitParent(){
		if(unitParent==null) unitParent=new GameObject("UnitParent_DontDestroy").transform;
		return unitParent;
	}
	public Unit SpawnUnit(Unit unit){
		GameObject unitObj=(GameObject)Instantiate(unit.gameObject, spawnPos, Quaternion.identity);
		unitObj.transform.parent=GetUnitParent();
		return unitObj.GetComponent<Unit>();		
	}
	
	
	private static Demo_Persistent_PreGame instance;
	
	public override void Awake(){
		base.Awake();
		instance=this;
	}
	
	public override void Start(){
		
		spawnPos=new Vector3(0, 99999, 0);
		
		//unitPoolList=new List<Unit>( UnitDB.GetList() );
		for(int i=0; i<unitPoolList.Count; i++){
			unitPoolList[i]=SpawnUnit(unitPoolList[i]);
			unitPoolList[i].ResetEffect(true);
		}
		
		//load squadList from cache
		squadList=UnitManager.GetCachedUnitList(factionID);
		List<CachedUnitInfo> squadInfoList=UnitManager.GetCachedUnitInfoList(factionID);
		
		for(int i=0; i<squadList.Count; i++){
			squadList[i]=SpawnUnit(squadList[i]);
		}
		
		
		if(cachedRsc>=0) resource=cachedRsc;			//check if there's previous resource value in the cache
		
		if(GameControl.factionWon_ID==factionID){	//check if player won the last match, if yes add the resources won
			resource+=winResourceGain;
			PerkManager.GainCurrency(winPerkCurrencyGain);
		}
		
		
		//Initiate the UI-elements
		for(int i=0; i<poolButtonLimit; i++){
			if(i>0) poolButtonList.Add(UIButton.Clone(poolButtonList[0].rootObj, "PoolButton"+(i)));
			poolButtonList[i].Init();
			
			int idx=i;	poolButtonList[i].button.onClick.AddListener(delegate { OnPoolButton(idx); });
			
			poolButtonList[i].SetHighlight(false);
			poolButtonList[i].SetActive(false);
		}
		
		for(int i=0; i<squadLimit; i++){
			if(i>0) squadButtonList.Add(UIButton.Clone(squadButtonList[0].rootObj, "SquadButton"+(i)));
			squadButtonList[i].Init();
			
			int idx=i;	squadButtonList[i].button.onClick.AddListener(delegate { OnSquadButton(idx); });
			
			squadButtonList[i].SetActive(false);
		}
		
		UpdatePoolButtonList();
		UpdateSquadButtonList();
		
		buttonAddUnit.Init();
		buttonAddUnit.button.onClick.AddListener(delegate { OnAddUnit(); });
		
		buttonPerkMenu.Init();
		buttonPerkMenu.button.onClick.AddListener(delegate { OnPerkMenu(); });
		
		buttonStartGame.Init();
		buttonStartGame.button.onClick.AddListener(delegate { OnStartGame(); });
		
		buttonBack.Init();
		buttonBack.button.onClick.AddListener(delegate { OnBackButton(); });
		
		lbResource.text="resource: <i>"+resource+"</i>";
		
		OnPoolButton(0);
		
		canvasGroup.alpha=1;
	}
	
	
	//add a unit from the unit pool to player squad
	public void OnAddUnit(){
		if(resource<unitPoolList[selectIdx].value){
			UIMessage.DisplayMessage("Insufficient Resource");
			return;
		}
		
		resource-=unitPoolList[selectIdx].value;
		lbResource.text="resource: <i>"+resource+"</i>";
		
		squadList.Add(SpawnUnit(unitPoolList[selectIdx]));
		
		UpdateSquadButtonList();
	}
	
	
	//show perk screen
	public void OnPerkMenu(){
		UIPerkScreen.Show();
		waitingForPerkScreen=true;
		UI.FadeOut(canvasGroup, 0f);
	}
	
	//when perk screen is closed, fade back in
	private bool waitingForPerkScreen=false;
	void Update(){
		if(waitingForPerkScreen && !UIPerkScreen.IsShowing()){
			UI.FadeIn(canvasGroup, 0f);
			waitingForPerkScreen=false;
		}
	}
	
	
	public void OnStartGame(){
		if(squadList.Count==0){
			UIPrompt.Show1("Cannot start game.\nNo unit has been selected!", null);
			return;
		}
		
		cachedRsc=resource;	//cached resource value so the value can be retrieved when we load back into this scene after a game
		
		//set the squadlist to the cache so UnitManager can load it in the next scene
		UnitManager.CacheFaction(factionID, squadList);
		UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
	}
	public void OnBackButton(){
		UnityEngine.SceneManagement.SceneManager.LoadScene(prevSceneName);
	}
	
	
	//when unit in squad are pressed, select that unit and update the stats display
	public void OnPoolButton(int idx){
		poolButtonList[selectIdx].SetHighlight(false);
		poolButtonList[idx].SetHighlight(true);
		selectIdx=idx;
		
		UpdateUnitDisplay();
	}
	
	//when unit in squad are pressed, remove the unit from squad
	public void OnSquadButton(int idx){
		resource+=squadList[idx].value;
		lbResource.text="resource: <i>"+resource+"</i>";
		
		Destroy(squadList[idx].gameObject);
		
		squadList.RemoveAt(idx);
		UpdateSquadButtonList();
	}
	
	
	//update the unit information display for the selected unit
	public void UpdateUnitDisplay(){
		Unit unit=unitPoolList[selectIdx];
		
		string text="Damage:\n"+"Attack:\n"+"Hit:\n\n"+"Defense:\n"+"Dodge:\n\n";
		text+=unit.desp;
		
		string textStats="";//"<size="+(lbValue.fontSize+5)+">  </size>\n\n";
		textStats+="<i>"+unit.GetDmgHPMin().ToString("f0")+" - "+unit.GetDmgHPMax().ToString("f0")+"</i>\n";
		textStats+="<i>"+unit.GetAttack().ToString("f0")+"</i>\n";
		textStats+="<i>"+unit.GetHit().ToString("f0")+"</i>\n\n";
		textStats+="<i>"+unit.GetDefense().ToString("f0")+"</i>\n";
		textStats+="<i>"+unit.GetDodge().ToString("f0")+"</i>\n\n";
		
		lbName.text=unit.itemName;
		lbLabel.text=text;
		lbValue.text=textStats;
	}
	
	
	//update the display of the units available for player to select
	public void UpdatePoolButtonList(){
		for(int i=0; i<poolButtonList.Count; i++){
			if(i<unitPoolList.Count){
				poolButtonList[i].SetLabel(unitPoolList[i].itemName);
				poolButtonList[i].SetLabel2("$"+unitPoolList[i].value);
				poolButtonList[i].image.sprite=unitPoolList[i].icon;
				poolButtonList[i].SetActive(true);
			}
			else poolButtonList[i].SetActive(false);
		}
	}
	//update the display of the units currently in player squad
	public void UpdateSquadButtonList(){
		for(int i=0; i<squadButtonList.Count; i++){
			if(i<squadList.Count){
				squadButtonList[i].image.sprite=squadList[i].icon;
				squadButtonList[i].SetActive(true);
			}
			else squadButtonList[i].SetActive(false);
		}
	}
	
	
	
	
	
	
}
