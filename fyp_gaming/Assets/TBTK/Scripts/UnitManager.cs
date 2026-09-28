using System.Collections;
using System.Collections.Generic;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TBTK{

	[ExecuteInEditMode]
	public class UnitManager : MonoBehaviour {
		
		public List<Unit> allUnitList=new List<Unit>();
		public static List<Unit> GetAllUnitList(){ 
			if(TurnControl.IsUnitPerTurn()) return instance.allUnitList;
			else{
				instance.allUnitList=new List<Unit>();
				for(int i=0; i<instance.factionList.Count; i++) instance.allUnitList.AddRange(instance.factionList[i].unitList);
				return instance.allUnitList;
			}
		}
		
		
		public List<Faction> factionList=new List<Faction>();
		public static List<Faction> GetFactionList(){ return instance.factionList; }
		public static Faction GetFaction(int idx){ return instance.factionList[idx]; }
		public static Faction GetFactionWithID(int fID){ 
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID==fID) return instance.factionList[i];
			}
			return null;
		}
		
		
		private bool hasAIInGame=false;
		
		public Unit selectedUnit;
		public static Unit GetSelectedUnit(){ return instance.selectedUnit; }
		
		public static Faction GetSelectedFaction(){ 
			if(GetSelectedUnit()==null) return null; 
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID==GetSelectedUnit().GetFacID()) return instance.factionList[i];
			}
			return null;
		}
		
		
		private static UnitManager instance;
		public static UnitManager GetInstance(){ 
			if(instance==null) instance=(UnitManager)FindObjectOfType(typeof(UnitManager));
			return instance;
		}
		
		public static void Init(){
			if(instance==null) instance=(UnitManager)FindObjectOfType(typeof(UnitManager));
			
			for(int i=0; i<instance.factionList.Count; i++){
				instance.factionList[i].Init(i);
				instance.hasAIInGame|=instance.factionList[i].playableFaction;
			}
			
			instance.DeployAllUnit();	//only applies for AI faction unless enableManualDeployment is left unchecked
			
			instance.deployingFacIdx=instance._RequireManualDeployment();
			if(instance.deployingFacIdx>=0) instance.NewFactionDeployment();
			
			instance._Init();
		}
		public void _Init(){
			//~ selectIndicator=GridManager.IsHexGrid() ? selectIndicatorHex : selectIndicatorSq ;
			//~ selectIndicator.localScale*=GridManager.GetNodeSize();
			
			//~ if(GridManager.IsHexGrid()) selectIndicatorSq.gameObject.SetActive(false);
			//~ if(GridManager.IsSquareGrid()) selectIndicatorHex.gameObject.SetActive(false);
		}
		
		public void Start(){
			if(!Application.isPlaying && instance==null) instance=this;
		}
		
		
		public void NewFactionDeployment(){
			deployingUnitIdx=0;
			int deployFacID=instance.factionList[deployingFacIdx].factionID;
			
			List<Node> deploymentNodeList=GridManager.GetDeploymentNode(deployFacID);
			GridIndicator.ShowDeployment(deploymentNodeList);
			
			//~ List<Node> addOnList=new List<Node>();
			//~ for(int i=0; i<deploymentNodeList.Count; i++){
				//~ List<Node> neighbours=deploymentNodeList[i].GetNeighbourList(true);
				//~ for(int n=0; n<neighbours.Count; n++){
					//~ if(!deploymentNodeList.Contains(neighbours[n])) addOnList.Add(neighbours[n]);
				//~ }
			//~ }
			//~ deploymentNodeList.AddRange(addOnList);
			GridManager.SetupFogOfWarForDeployment(deploymentNodeList, deployFacID);
		}
		
		public static bool IsDeploymentDone(){
			if(instance.factionList[instance.deployingFacIdx].deployingList.Count==0) return true;
			
			bool hasEmptyNode=false;
			List<Node> deploymentNodeList=GridManager.GetDeploymentNode(instance.factionList[instance.deployingFacIdx].factionID);
			for(int i=0; i<deploymentNodeList.Count; i++){
				if(deploymentNodeList[i].unit==null){ hasEmptyNode|=true; break; }
			}
			
			return !hasEmptyNode;
		}
		
		public static bool CheckDeploymentHasUnitOnGrid(){
			return instance.factionList[instance.deployingFacIdx].unitList.Count>0;
		}
		
		public static bool DeployUnit(Node node, int idx=0){ return instance._DeployUnit(node, idx); }
		public bool _DeployUnit(Node node, int idx=0){
			if(node.deployFacID!=factionList[deployingFacIdx].factionID) return false;
			
			if(node.unit!=null){
				if(node.unit.GetFacID()!=deployingFacIdx) return false;
				factionList[deployingFacIdx].unitList.Remove(node.unit);
				factionList[deployingFacIdx].deployingList.Add(node.unit);
				node.unit.transform.position=new Vector3(0, 99999, 0);
				node.unit=null;
				
				//PrevUnitToDeploy();
			}
			else{
				if(!node.walkable || node.obstacleT!=null) return false;
				if(factionList[deployingFacIdx].deployingList.Count==0) return false;
				
				node.unit=factionList[deployingFacIdx].deployingList[idx];
				node.unit.transform.position=node.GetPos();
				
				if(!Unit.enableRotation) node.unit.transform.rotation=Quaternion.identity;
				else node.unit.transform.rotation=Quaternion.Euler(0, factionList[deployingFacIdx].direction, 0);
				
				node.unit.node=node;
				factionList[deployingFacIdx].unitList.Add(node.unit);
				factionList[deployingFacIdx].deployingList.RemoveAt(idx);
				
				if(factionList[deployingFacIdx].unitList.Count<=1) deployingUnitIdx=0;
			}
			return true;
		}
		public static int EndDeployment(){
			GridIndicator.HideDeployment();
			instance.deployingFacIdx=instance._RequireManualDeployment(instance.deployingFacIdx+1);
			if(instance.deployingFacIdx>=0) instance.NewFactionDeployment();
			return instance.deployingFacIdx;
		}
		
		
		public static bool RequireManualDeployment(){ return instance._RequireManualDeployment()>=0; }
		public int _RequireManualDeployment(int offset=0){
			if(!GameControl.EnableUnitDeployment()) return -1;
			//if(unitDeployed) return -1;
			
			for(int i=0+offset; i<factionList.Count; i++){
				if(!factionList[i].playableFaction) continue;
				if(factionList[i].deployingList.Count>0) return i;
			}
			
			return -1;
		}
		
		public int deployingFacIdx=-1;
		public int deployingUnitIdx=-1;
		public static bool DeployingUnit(){ return instance.deployingFacIdx>=0; }
		
		public static int GetDeployingFacIdx(){ return instance.deployingFacIdx; }
		public static int GetDeployingUnitIdx(){ return instance.deployingUnitIdx; }
		public static void SetDeployingUnitIdx(int idx){ instance.deployingUnitIdx=idx; }
		//~ public static void NextUnitToDeploy(){
			//~ instance.deployingUnitIdx+=1;
			//~ if(instance.deployingUnitIdx>=instance.factionList[deployingFacIdx].deployingList.Count) instance.deployingUnitIdx=0;
		//~ }
		//~ public static void PrevUnitToDeploy(){
			//~ instance.deployingUnitIdx-=1;
			//~ if(instance.deployingUnitIdx<0) instance.deployingUnitIdx=instance.factionList[deployingFacIdx].deployingList.Count-1;
		//~ }
		
		//private bool unitDeployed=false;
		//public static bool UnitDeployed(){ return instance.unitDeployed; }
		
		public void DeployAllUnit(){
			//unitDeployed=true;
			for(int i=0; i<factionList.Count; i++){
				if(GameControl.EnableUnitDeployment() && factionList[i].playableFaction) continue;
				factionList[i].factionID=i;
				factionList[i].DeployUnit();
			}
		}
		
		
		public void InitAITrigger(){
			if(!hasAIInGame) return;
			for(int i=0; i<factionList.Count; i++){
				if(factionList[i].playableFaction) continue;
				for(int n=0; n<factionList[i].unitList.Count; n++) CheckAITrigger(factionList[i].unitList[n]);
			}
		}
		public static void CheckAITrigger(Unit unit){ instance._CheckAITrigger(unit); }
		public void _CheckAITrigger(Unit unit){
			if(!hasAIInGame) return;
			
			for(int i=0; i<factionList.Count; i++){
				if(unit.triggered && factionList[i].playableFaction) continue;
				if(factionList[i].factionID==unit.GetFacID()) continue;
				
				for(int n=0; n<factionList[i].unitList.Count; n++){
					if(unit.triggered && factionList[i].unitList[n].triggered) continue;
					
					float dist=GridManager.GetDistance(unit.node, factionList[i].unitList[n].node);
					if(dist<=factionList[i].unitList[n].GetSight()) factionList[i].unitList[n].triggered=true;
					
					if(dist<=unit.GetSight()){
						unit.triggered=true;
						if(factionList[i].playableFaction) break;
					}
				}
			}
		}
		
		
		public static void StartGame(){ instance._StartGame(); }
		public void _StartGame(){
			for(int i=0; i<factionList.Count; i++) factionList[i].StartGame(i);
			
			if(TurnControl.IsUnitPerTurn()){
				for(int i=0; i<factionList.Count; i++){
					for(int n=0; n<factionList[i].unitList.Count; n++){
						allUnitList.Add(factionList[i].unitList[n]);
						//factionList[i].unitList[n].instanceID=Random.Range(0, 9999);
					}
				}
				allUnitList=SortUnitListByPriority(allUnitList);
			}
			
			if(TurnControl.IsFactionPerTurn()){
				for(int i=0; i<factionList.Count; i++){
					factionList[i].unitList=SortUnitListByPriority(factionList[i].unitList);
				}
			}
			
			InitAITrigger();
		}
		
		
		
		public static bool CheckIfNewTurnEndRound(int turnIdx){
			if(TurnControl.IsUnitPerTurn()){
				return turnIdx>=instance.allUnitList.Count;
			}
			else if(TurnControl.IsFactionPerTurn()){
				return turnIdx>=instance.factionList.Count;
			}
			return false;
		}
		
		public static void EndTurn_UnitPerTurn(int turnIdx){
			//if(turnIdx>=instance.allUnitList.Count) turnIdx=0;
			if(instance.allUnitList[turnIdx]==null || instance.allUnitList[turnIdx].hp<=0){	//in case the unit is destroy by dot or other effect
				TurnControl.EndTurn();
				return;
			}
			
			instance.allUnitList[turnIdx].NewTurn();
			
			if(instance.allUnitList[turnIdx].playableUnit){
				TBSelectUnit(instance.allUnitList[turnIdx]);
				for(int i=0; i<instance.factionList.Count; i++){
					if(instance.factionList[i].factionID==instance.allUnitList[turnIdx].GetFacID()){ 
						TBTK.OnSelectFaction(instance.factionList[i]); break; 
					}
				}
			}
			else{
				AI.MoveUnit(instance.allUnitList[turnIdx]);
			}
			
			//return turnIdx;
		}
		public static void EndTurn_FactionPerTurn(int turnIdx){
			//if(turnIdx>=instance.factionList.Count) turnIdx=0;
			instance.factionList[turnIdx].NewTurn();
			
			if(instance.factionList[turnIdx].playableFaction){
				TBSelectUnit(instance.factionList[turnIdx].unitList[0]);
				TBTK.OnSelectFaction(instance.factionList[turnIdx]);
			}
			else{
				AI.MoveFaction(instance.factionList[turnIdx]);
			}
			
			//return turnIdx;
		}
		
		//~ private bool aiToMoveNext=false;
		//~ private unit unitToMoveNext=false;
		//~ public static Unit NextUnitToMove;
		//~ public static void MoveAI(int turnIdx){
			//~ aiToMoveNext=false;
			
			//~ if(TurnControl.IsUnitPerTurn()){
				//~ AI.MoveUnit(instance.allUnitList[turnIdx]);
			//~ }
			//~ else if(TurnControl.IsFactionPerTurn()){
				//~ AI.MoveFaction(instance.factionList[turnIdx]);
			//~ }
		//~ }
		
		
		
		//to iterate unit ability and  effect CD
		public static IEnumerator EndTurn_IterateCD(){
			for(int i=0; i<instance.factionList.Count; i++){
				for(int n=0; n<instance.factionList[i].unitList.Count; n++){
					instance.factionList[i].unitList[n].IterateCD();
					
					if(TurnControl.WaitForUnitDestroy() && instance.factionList[i].unitList[n].hp<=0) 
						yield return instance.StartCoroutine(instance.factionList[i].unitList[n].DestroyRoutine());
				}
			}
		}
		
		//~ public static bool CanSwitchUnit(){
			//~ if(!TurnControl.AllowUnitSwitching) return false;
			//~ if(TurnControl.IsUnitPerTurn()) return false;
			//~ return true;
		//~ }
		
		
		[Space(8)] public int currentFacID=-1;
		public static int GetCurrentFactionID(){ return instance.currentFacID; }
		
		public static void SelectNextUnit(){ 
			//if(TurnControl.IsUnitPerTurn()){
			//	GameControl.EndTurn();
			//}
			if(TurnControl.IsFactionPerTurn()){
				if(GetSelectedFaction().GetMoveCount()>=TurnControl.GetUnitLimit()) TBSelectUnit(instance.selectedUnit);
				else TBSelectUnit(instance.factionList[instance.currentFacID].GetNextUnitInTurn());
			}
		}
		
		
		public static void TBSelectUnit(Unit unit){ instance._SelectUnit(unit); }	//by non UI-user event
		public static void SelectUnit(Unit unit){ 
			if(!TurnControl.CanSwitchUnit(unit)) return;
			instance._SelectUnit(unit);
		}
		public void _SelectUnit(Unit unit){
			selectedUnit=unit;
			
			if(unit!=null && unit.playableUnit){
				//selectIndicator.position=selectedUnit.GetPos();
				
				currentFacID=instance.selectedUnit.GetFacID();
				factionList[currentFacID].UpdateTurnIdx(selectedUnit);
				
				GridManager.SelectUnit(unit);
				
				GridIndicator.SetSelect(unit.node);
				GridIndicator.ShowMovable(GridManager.GetWalkableList());
				GridIndicator.ShowHostile(GridManager.GetAttackableList());
				
				selectedUnit.AudioPlaySelect();
			}
			else{
				GridIndicator.HideAll();
				GridIndicator.SetSelect(null);
				//else selectIndicator.position=new Vector3(0, 9999, 0);
			}
			
			TBTK.OnSelectUnit(unit);
		}
		
		public static List<Unit> SortUnitListByPriority(List<Unit> list){
			List<Unit> newList=new List<Unit>();
			while(list.Count>0){
				int highestIdx=0;
				float highestVal=0;
				for(int i=0; i<list.Count; i++){
					float turnPriority=list[i].GetTurnPriority();
					if(turnPriority>highestVal || turnPriority==highestVal && Random.value<0.5f){
						highestVal=turnPriority;
						highestIdx=i;
					}
				}
				newList.Add(list[highestIdx]);
				list.RemoveAt(highestIdx);
			}
			return newList;
		}
		
		
		private IEnumerator SelectedUnitDestroyed(){
			while(GameControl.ActionInProgress() || AI.ActionInProgress()) yield return null;
			if(TurnControl.IsFactionPerTurn()) SelectNextUnit();
			else if(TurnControl.IsUnitPerTurn()) GameControl.EndTurn();
		}
		
		public static void UnitDestroyed(Unit unit){ instance._UnitDestroyed(unit); }
		public void _UnitDestroyed(Unit unit){
			if(selectedUnit==unit){
				StartCoroutine(SelectedUnitDestroyed());
				//~ Debug.Log("selected unit is destroyed");
				//~ if(TurnControl.IsFactionPerTurn()) SelectNextUnit();
				//~ else if(TurnControl.IsUnitPerTurn()) GameControl.EndTurn();
				//if(TurnControl.IsUnitPerTurn()) 
			}
			
			if(TurnControl.IsUnitPerTurn()){
				if(allUnitList.IndexOf(unit)<=TurnControl.GetTurn()) TurnControl.RevertTurn();
				allUnitList.Remove(unit);
			}
			
			TBTK.OnUnitDestroyed(unit);
			
			bool factionCleared=factionList[unit.GetBaseFacID()].RemoveUnit(unit);
			if(unit.tempFacID>=0) factionCleared|=factionList[unit.tempFacID].RemoveUnit(unit);
			if(!factionCleared) return;
			
			int facWithUnitCount=0;
			int facIdxWithUnit=-1;
			for(int i=0; i<factionList.Count; i++){
				if(factionList[i].unitList.Count>0){
					facIdxWithUnit=i;
					facWithUnitCount+=1;
				}
			}
			
			if(facWithUnitCount<=1) GameControl.GameOver(factionList[facIdxWithUnit]);
		}
		
		public static void GameOver(){
			for(int i=0; i<instance.factionList.Count; i++) instance.factionList[i].CacheUnit_PostGame();
		}
		
		
		
		public static List<Unit> GetAllFriendlyUnits(int facID){
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID!=facID) continue;
				return instance.factionList[i].unitList;
			}
			return new List<Unit>();
		}
		public static List<Unit> GetAllHostileUnits(int facID){
			List<Unit> list=new List<Unit>();
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID==facID) continue;
				list.AddRange(instance.factionList[i].unitList);
			}
			return list;
		}
		
		public static List<Unit> GetAllUnits(){
			if(TurnControl.IsUnitPerTurn()) return instance.allUnitList;
			
			List<Unit> list=new List<Unit>();
			for(int i=0; i<instance.factionList.Count; i++) list.AddRange(instance.factionList[i].unitList);
			return list;
		}
		
		
		public static void AddUnit(Unit unit, int facID){
			if(TurnControl.IsUnitPerTurn()){
				if(instance.allUnitList[instance.allUnitList.Count-1].GetTurnPriority()>unit.GetTurnPriority()){
					instance.allUnitList.Add(unit);
				}
				else{
					for(int i=0; i<instance.allUnitList.Count; i++){
						if(instance.allUnitList[i].GetTurnPriority()>unit.GetTurnPriority()) continue;
						instance.allUnitList.Insert(i, unit);
						break;
					}
				}
				
				if(GetSelectedUnit().GetTurnPriority()<unit.GetTurnPriority()) TurnControl.AddTurn();
				
				TBTK.OnUnitOrderChanged();
			}
			
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID!=facID) continue;
				instance.factionList[i].AddUnit(unit);
				break;
			}
		}
		
		
		public static void AddAbilityToPlayerFaction(int abilityPID){
			foreach(Faction fac in instance.factionList){
				if(!fac.playableFaction) continue;
				fac.AddAbility(abilityPID);
			}
		}
		public static void AddAbilityToPlayerUnit(int abilityPID, List<int> unitPIDList){
			foreach(Faction fac in instance.factionList){
				if(!fac.playableFaction) continue;
				foreach(Unit unit in fac.unitList){
					if(unitPIDList!=null && !unitPIDList.Contains(unit.prefabID)) continue;
					unit.AddAbility(abilityPID);
				}
			}
		}
		
		
		public static void AddFacSwitchUnit(Unit unit){
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID!=unit.tempFacID) continue;
				instance.factionList[i].unitList.Add(unit);
				//factionList.tempUnitList.Add(unit);
				break;
			}
		}
		public static void RemoveFacSwitchUnit(Unit unit){
			for(int i=0; i<instance.factionList.Count; i++){
				if(instance.factionList[i].factionID!=unit.tempFacID) continue;
				instance.factionList[i].unitList.Remove(unit);
				//factionList.tempUnitList.Remove(unit);
				break;
			}
		}
		
		
		//only used in editor 
		public static Unit PlaceUnit(GameObject unitPrefab, Node node, float dir, bool spawn=true){
			//GameObject obj=(GameObject)MonoBehaviour.Instantiate(unitPrefab, nodeList[rand].GetPos(), Quaternion.identity);
			
			GameObject obj;
			Quaternion rot=Unit.enableRotation ? Quaternion.Euler(0, dir, 0) : Quaternion.identity ;
			
			if(spawn){
				//~ #if UNITY_EDITOR
					//~ obj=(GameObject)PrefabUtility.InstantiatePrefab(unitPrefab);
					//~ obj.transform.position=node.GetPos();
				//~ #else
					
					obj=(GameObject)MonoBehaviour.Instantiate(unitPrefab, node.GetPos(), rot);
				//~ #endif
			}
			else{
				obj=unitPrefab;
				obj.transform.position=node.GetPos();
				obj.transform.rotation=rot;
			}
			
			if(GetInstance()!=null) obj.transform.parent=instance.transform;
			
			Unit unit=obj.GetComponent<Unit>();
			unit.node=node;		node.unit=unit;
			return unit;
		}
		
		
		
		
		public static float GetAuraAttackMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackMul(unit, node); }
		public static float GetAuraDefenseMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDefenseMul(unit, node); }
		public static float GetAuraHitMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraHitMul(unit, node); }
		public static float GetAuraDodgeMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDodgeMul(unit, node); }
		
		public static float GetAuraDmgHPMinMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgHPMinMul(unit, node); }
		public static float GetAuraDmgHPMaxMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgHPMaxMul(unit, node); }
		public static float GetAuraDmgAPMinMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgAPMinMul(unit, node); }
		public static float GetAuraDmgAPMaxMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgAPMaxMul(unit, node); }
		public static float GetAuraCritChanceMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritChanceMul(unit, node); }
		public static float GetAuraCritReducMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritReducMul(unit, node); }
		public static float GetAuraCritMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritMul(unit, node); }
		
		public static float GetAuraCDmgMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCDmgMul(unit, node); }
		public static float GetAuraCHitPenaltyMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCHitPenaltyMul(unit, node); }
		public static float GetAuraCCritPenaltyMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCCritPenaltyMul(unit, node); }
		
		public static float GetAuraODmgMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraODmgMul(unit, node); }
		public static float GetAuraOHitPenaltyMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraOHitPenaltyMul(unit, node); }
		public static float GetAuraOCritPenaltyMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraOCritPenaltyMul(unit, node); }
		
		public static float GetAuraAttackRangeMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackRangeMul(unit, node); }
		public static float GetAuraAttackRangeMinMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackRangeMinMul(unit, node); }
		public static float GetAuraMoveRangeMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraMoveRangeMul(unit, node); }
		public static float GetAuraTurnPriorityMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraTurnPriorityMul(unit, node); }
		public static float GetAuraSightMul(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraSightMul(unit, node); }
		
		
		public static float GetAuraAttackMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackMod(unit, node); }
		public static float GetAuraDefenseMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDefenseMod(unit, node); }
		public static float GetAuraHitMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraHitMod(unit, node); }
		public static float GetAuraDodgeMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDodgeMod(unit, node); }
		
		public static float GetAuraDmgHPMinMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgHPMinMod(unit, node); }
		public static float GetAuraDmgHPMaxMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgHPMaxMod(unit, node); }
		public static float GetAuraDmgAPMinMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgAPMinMod(unit, node); }
		public static float GetAuraDmgAPMaxMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraDmgAPMaxMod(unit, node); }
		public static float GetAuraCritChanceMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritChanceMod(unit, node); }
		public static float GetAuraCritReducMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritReducMod(unit, node); }
		public static float GetAuraCritMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCritMod(unit, node); }
		
		public static float GetAuraCDmgMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCDmgMod(unit, node); }
		public static float GetAuraCHitPenaltyMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCHitPenaltyMod(unit, node); }
		public static float GetAuraCCritPenaltyMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraCCritPenaltyMod(unit, node); }
		
		public static float GetAuraODmgMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraODmgMod(unit, node); }
		public static float GetAuraOHitPenaltyMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraOHitPenaltyMod(unit, node); }
		public static float GetAuraOCritPenaltyMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraOCritPenaltyMod(unit, node); }
		
		public static float GetAuraAttackRangeMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackRangeMod(unit, node); }
		public static float GetAuraAttackRangeMinMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraAttackRangeMinMod(unit, node); }
		public static float GetAuraMoveRangeMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraMoveRangeMod(unit, node); }
		public static float GetAuraTurnPriorityMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraTurnPriorityMod(unit, node); }
		public static float GetAuraSightMod(Unit unit, Node node){ return GetFactionWithID(unit.facID).GetAuraSightMod(unit, node); }
		
		
		
		
		//for gizmo
		public static Color GetFacColor(int idx){	
			if(instance==null) instance=(UnitManager)FindObjectOfType(typeof(UnitManager));
			return idx<instance.factionList.Count ? instance.factionList[idx].color : new Color(0, 0, 0, 0);
		}
		
		
		
		public static List<Faction> cachedFactionList=new List<Faction>();
		
		public static void ClearCache(){ cachedFactionList.Clear(); }
		public static void CacheFaction(int factionID, List<Unit> unitList, bool postBattle=false){
			List<CachedUnitInfo> unitInfoList=new List<CachedUnitInfo>();
			for(int i=0; i<unitList.Count; i++){
				unitInfoList.Add(new CachedUnitInfo(unitList[i]));
				unitList[i]=UnitDB.GetPrefab(unitList[i].prefabID);
			}
			
			int index=CheckCachedFaction(factionID);
			
			if(index>=0){
				cachedFactionList[index].unitList=unitList;
				cachedFactionList[index].cachedUnitInfoList=unitInfoList;
			}
			else{
				Faction fac=new Faction();
				fac.factionID=factionID;
				fac.unitList=unitList;
				fac.cachedUnitInfoList=unitInfoList;
				cachedFactionList.Add(fac);
			}
		}
		
		public static int CheckCachedFaction(int factionID){
			for(int i=0; i<cachedFactionList.Count; i++){
				if(cachedFactionList[i].factionID==factionID) return i;
			}
			return -1;
		}
		public static List<Unit> GetCachedUnitList(int index){
			return ((index>=0 && cachedFactionList.Count>index) ? cachedFactionList[index].unitList : new List<Unit>() );
		}
		
		public static List<CachedUnitInfo> GetCachedUnitInfoList(int index){
			return ((index>=0 && cachedFactionList.Count>index) ? cachedFactionList[index].cachedUnitInfoList : new List<CachedUnitInfo>() );
		}
	}
	
	
	public class CachedUnitInfo{
		//public float hp;
		public CachedUnitInfo(Unit unit){ 
			//hp=unit.hp;
		}
	}
	

	[System.Serializable]
	public class Faction{
		//[HideInInspector] 
		public int factionID=0;
		[HideInInspector] public int factionIdx=-1;
		
		public string name="Faction";
		public Color color;	//not used in runtime
		public float direction;
		
		public int turnIdx;		//indicate which unit in the faction is being selected right now, used in FactionPerTurn only
		public void UpdateTurnIdx(Unit unit){ turnIdx=unitList.IndexOf(unit); }
		
		[HideInInspector]
		public int movedUnitCount=0;		//not in used
		
		public bool playableFaction=true;
		
		public List<Unit> unitList=new List<Unit>();
		public List<CachedUnitInfo> cachedUnitInfoList=new List<CachedUnitInfo>();
		
		public List<Unit> startingUnitList=new List<Unit>();
		public List<Unit> deployingList=new List<Unit>();
		
		public List<SpawnGroup> spawnGroupList=new List<SpawnGroup>();
		
		
		public bool loadFromData;
		public bool saveToData;
		public bool saveLoadedUnitOnly;
		//public int cachedFacID=0;	//the factionID as assigned in editor
		
		
		[HideInInspector] //for grid regeneration
		public List<Vector3> deploymentPointList=new List<Vector3>();
		
		
		public void CacheUnit_PostGame(){		//called by the faction instance in a game when the game is over
			if(!saveToData) return;
			
			if(!saveLoadedUnitOnly){
				UnitManager.CacheFaction(factionID, unitList, false);
				return;
			}
			
			List<Unit> newList=new List<Unit>();
			for(int i=0; i<unitList.Count; i++){
				if(unitList[i].loadedFromCache) newList.Add(unitList[i]);
			}
			UnitManager.CacheFaction(factionID, newList, true);
		}
		
		
		public void Init(int facID){
			factionIdx=facID;
			
			bool loadedFromCache=false;
			
			if(loadFromData){
				int index=UnitManager.CheckCachedFaction(factionID);
				if(index>=0){
					startingUnitList=UnitManager.GetCachedUnitList(index);
					cachedUnitInfoList=UnitManager.GetCachedUnitInfoList(index);
					loadedFromCache=true;
				}
			}
			
			for(int i=0; i<startingUnitList.Count; i++){
				if(startingUnitList[i]==null) continue;
				
				GameObject unitObj=(GameObject)MonoBehaviour.Instantiate(startingUnitList[i].gameObject, new Vector3(0, 99999, 0), Quaternion.identity);
				deployingList.Add(unitObj.GetComponent<Unit>());
				
				if(loadedFromCache){
					deployingList[deployingList.Count-1].loadedFromCache=true;
					
					//deployingList[deployingList.Count-1].hp=cachedUnitInfoList[i].hp;
				}
				
				unitObj.transform.parent=UnitManager.GetInstance().transform;
			}
			
			for(int i=0; i<deployingList.Count; i++){
				deployingList[i].SetFacID(factionID);
				deployingList[i].playableUnit=playableFaction;
			}
			
			for(int i=0; i<unitList.Count; i++){
				unitList[i].SetFacID(factionID);
				unitList[i].playableUnit=playableFaction;
			}
			
			//abilityIDList=new List<int>{ 0, 1 };
			for(int i=0; i<abilityIDList.Count; i++) AddAbility(abilityIDList[i]);
		}
		public void AddAbility(int abPrefabID){
			abilityList.Add(AbilityFDB.GetPrefab(abPrefabID).Clone());
			abilityList[abilityList.Count-1].Init(factionID, abilityList.Count-1);
		}
		
		public void AddUnit(Unit unit){	//add unit during runtime (mid-game)
			unit.SetFacID(factionID);
			unit.playableUnit=playableFaction;
			unit.NewTurn();
			unitList.Add(unit);
			
			TBTK.OnNewUnit(unit, factionIdx);
		}
		
		public void DeployUnit(){
			List<Node> nodeList=GridManager.GetDeploymentNode(factionID);
			
			int count=deployingList.Count;
			for(int i=0; i<count; i++){
				if(nodeList.Count==0) break;
				
				int rand=Random.Range(0, nodeList.Count);
				
				Unit unit=UnitManager.PlaceUnit(deployingList[0].gameObject, nodeList[rand], direction, false);
				unitList.Add(unit);
				
				nodeList.RemoveAt(rand);		deployingList.RemoveAt(0);
			}
		}
		
		public void StartGame(int facID){
			for(int i=0; i<unitList.Count; i++){
				unitList[i].NewTurn(true);
				//unitList[i].UpdateAuraTarget();
			}
		}
		
		public bool RemoveUnit(Unit unit){
			unitList.Remove(unit);
			return unitList.Count==0;
		}
		
		public void NewTurn(){
			turnIdx=0;
			for(int i=0; i<unitList.Count; i++) unitList[i].NewTurn();
			
			movedUnitCount=0;
		}
		
		public void SelectUnit(Unit unit){
			UpdateTurnIdx(unit);
		}
		
		public Unit GetNextUnitInTurn(){
			if(unitList.Count==0) return null;
			int newIdx=(turnIdx+1)%unitList.Count;
			return unitList[newIdx];
		}
		
		
		public int GetMoveCount(){	//only used in factionPerTurn
			int count=0;
			for(int i=0; i<unitList.Count; i++) count+=unitList[i].HasTakenAction() ? 1 : 0 ;
			return count;
		}
		
		
		
		public List<int> abilityIDList=new List<int>();
		public List<Ability> abilityList=new List<Ability>();	//runtime attribute
		public Ability GetAbility(int idx){ return abilityList[idx]; }
		
		public Ability._AbilityStatus SelectAbility(int idx){
			Ability._AbilityStatus abilityStatus=abilityList[idx].IsAvailable();
			if(abilityStatus!=Ability._AbilityStatus.Ready) return abilityStatus;
			
			//~ int usable=abilityList[idx].IsAvailable();
			//~ if(usable!=0) return usable;
			
			if(!abilityList[idx].requireTarget){
				UseAbility(idx, null);
			}
			else{
				AbilityManager.AbilityTargetModeFac(this, abilityList[idx]);
			}
			
			return 0;
		}
		
		public void UseAbility(int idx, Node target){ 
			GameControl.FactionUseAbility(this, abilityList[idx], target);
			//StartCoroutine(_UseAbility(abilityList[idx], target)); 
		}
		public IEnumerator UseAbilityRoutine(Ability ability, Node target){
			Debug.Log("UseAbilityRoutine   "+target);
			ability.Activate();
			
			//ap-=ability.apCost;
			
			//~ yield return CRoutine.Get().StartCoroutine(AbilityHit(ability, target));
			yield return CRoutine.Get().StartCoroutine(ability.HitTarget(target));
		}
		
		//~ public IEnumerator AbilityHit(Ability ability, Node target){
			//~ ability.HitTarget(target);
		//~ }
		
		
		
		public static bool UseAura(){ return Unit.enableAura; }
		
		public float GetAuraAttackMul(Unit unit, Node node, float value=1){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraAttackMul(node); } return value;}
		public float GetAuraDefenseMul(Unit unit, Node node, float value=1){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDefenseMul(node); } return value; }
		public float GetAuraHitMul(Unit unit, Node node, float value=1){				if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraHitMul(node); } return value; }
		public float GetAuraDodgeMul(Unit unit, Node node, float value=1){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDodgeMul(node); } return value; }
		
		public float GetAuraDmgHPMinMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDmgHPMinMul(node); } return value; }
		public float GetAuraDmgHPMaxMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDmgHPMaxMul(node); } return value; }
		public float GetAuraDmgAPMinMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDmgAPMinMul(node); } return value; }
		public float GetAuraDmgAPMaxMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraDmgAPMaxMul(node); } return value; }
		public float GetAuraCritChanceMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCritChanceMul(node); } return value; }
		public float GetAuraCritReducMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCritReducMul(node); } return value; }
		public float GetAuraCritMul(Unit unit, Node node, float value=1){				if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCritMul(node); } return value; }
		
		public float GetAuraCDmgMul(Unit unit, Node node, float value=1){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCDmgMul(node); } return value; }
		public float GetAuraCHitPenaltyMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCHitPenaltyMul(node); } return value; }
		public float GetAuraCCritPenaltyMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraCCritPenaltyMul(node); } return value; }
		
		public float GetAuraODmgMul(Unit unit, Node node, float value=1){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraODmgMul(node); } return value; }
		public float GetAuraOHitPenaltyMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraOHitPenaltyMul(node); } return value; }
		public float GetAuraOCritPenaltyMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraOCritPenaltyMul(node); } return value; }
		
		public float GetAuraAttackRangeMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraAttackRangeMul(node); } return value; }
		public float GetAuraAttackRangeMinMul(Unit unit, Node node, float value=1){if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraAttackRangeMinMul(node); } return value; }
		public float GetAuraMoveRangeMul(Unit unit, Node node, float value=1){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraMoveRangeMul(node); } return value; }
		public float GetAuraTurnPriorityMul(Unit unit, Node node, float value=1){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraTurnPriorityMul(node); } return value; }
		public float GetAuraSightMul(Unit unit, Node node, float value=1){				if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value*=unitList[i].GetAuraSightMul(node); } return value; }
		
		
		public float GetAuraAttackMod(Unit unit, Node node, float value=0){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraAttackMod(node); } return value; }
		public float GetAuraDefenseMod(Unit unit, Node node, float value=0){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDefenseMod(node); } return value; }
		public float GetAuraHitMod(Unit unit, Node node, float value=0){				if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraHitMod(node); } return value; 		}
		public float GetAuraDodgeMod(Unit unit, Node node, float value=0){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDodgeMod(node); } return value; }
		
		public float GetAuraDmgHPMinMod(Unit unit, Node node, float value=0){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDmgHPMinMod(node); } return value; }
		public float GetAuraDmgHPMaxMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDmgHPMaxMod(node); } return value; }
		public float GetAuraDmgAPMinMod(Unit unit, Node node, float value=0){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDmgAPMinMod(node); } return value; }
		public float GetAuraDmgAPMaxMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraDmgAPMaxMod(node); } return value; }
		public float GetAuraCritChanceMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCritChanceMod(node); } return value; }
		public float GetAuraCritReducMod(Unit unit, Node node, float value=0){		if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCritReducMod(node); } return value; }
		public float GetAuraCritMod(Unit unit, Node node, float value=0){				if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCritMod(node); } return value; }
		
		public float GetAuraCDmgMod(Unit unit, Node node, float value=0){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCDmgMod(node); } return value; }
		public float GetAuraCHitPenaltyMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCHitPenaltyMod(node); } return value; }
		public float GetAuraCCritPenaltyMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraCCritPenaltyMod(node); } return value; }
		
		public float GetAuraODmgMod(Unit unit, Node node, float value=0){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraODmgMod(node); } return value; }
		public float GetAuraOHitPenaltyMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraOHitPenaltyMod(node); } return value; }
		public float GetAuraOCritPenaltyMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraOCritPenaltyMod(node); } return value; }
		
		public float GetAuraAttackRangeMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraAttackRangeMod(node); } return value; }
		public float GetAuraAttackRangeMinMod(Unit unit, Node node, float value=0){if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraAttackRangeMinMod(node); } return value; }
		public float GetAuraMoveRangeMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraMoveRangeMod(node); } return value; }
		public float GetAuraTurnPriorityMod(Unit unit, Node node, float value=0){	if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraTurnPriorityMod(node); } return value; }
		public float GetAuraSightMod(Unit unit, Node node, float value=0){			if(!UseAura()) return value;	for(int i=0; i<unitList.Count; i++){ if(unitList[i]!=unit) value+=unitList[i].GetAuraSightMul(node); } return value; }
		
	}
	
	
	[System.Serializable]
	public class SpawnGroup{
		public int ID;
		
		public int countMin=5;
		public int countMax=5;
		
		public List<Unit> unitList=new List<Unit>();
		public List<int> unitCountMinList=new List<int>();
		public List<int> unitCountMaxList=new List<int>();
		
		[HideInInspector] //for grid regeneration
		public List<Vector3> spawnPointList=new List<Vector3>();
		
		public void Spawn(Faction fac){
			//use a dummy list so the original list wont get altered if this is run in EditMode
			List<Unit> cloneUnitList=new List<Unit>( unitList );	
			List<int> cloneMinList=new List<int>( unitCountMinList );
			List<int> cloneMaxList=new List<int>( unitCountMaxList );
			List<Unit> spawnedUnitList=new List<Unit>();	
			
			List<Node> nodeList=GridManager.GetSpawnGroup(fac.factionID, ID);
			int limit=(int)Mathf.Min(nodeList.Count, Rand.Range(countMin, countMax));		int currentCount=0;
			
			//loop through the unitlist, place the limited amount of them
			while(nodeList.Count>0 && currentCount<limit){
				bool hasMinimum=false;
				for(int i=0; i<cloneUnitList.Count; i++){
					if(nodeList.Count==0) break;
					if(unitCountMinList[i]<=0) continue;
					
					int nodeIdx=Rand.Range(0, nodeList.Count);
					spawnedUnitList.Add(UnitManager.PlaceUnit(cloneUnitList[i].gameObject, nodeList[nodeIdx], fac.direction));
					nodeList.RemoveAt(nodeIdx);		currentCount+=1;
					
					cloneMinList[i]-=1;	cloneMaxList[i]-=1;
					
					if(cloneMinList[i]<=0 || cloneMaxList[i]<=0){
						cloneUnitList.RemoveAt(i); 	cloneMinList.RemoveAt(i); 	cloneMaxList.RemoveAt(i); 	i-=1;
						continue;
					}
				}
				if(!hasMinimum) break;
			}
			
			while(currentCount<limit && nodeList.Count>0 && cloneUnitList.Count>0){
				int unitIdx=Rand.Range(0, cloneUnitList.Count);
				int nodeIdx=Rand.Range(0, nodeList.Count);
				
				spawnedUnitList.Add(UnitManager.PlaceUnit(cloneUnitList[unitIdx].gameObject, nodeList[nodeIdx], fac.direction));
				nodeList.RemoveAt(nodeIdx);		currentCount+=1;
				
				cloneMaxList[unitIdx]-=1;
				if(cloneMaxList[unitIdx]<=0){
					cloneUnitList.RemoveAt(unitIdx);	cloneMaxList.RemoveAt(unitIdx);
					continue;
				}
			}
			
			for(int i=0; i<spawnedUnitList.Count; i++) fac.unitList.Add(spawnedUnitList[i]);
		}
		
	}
	
	
}