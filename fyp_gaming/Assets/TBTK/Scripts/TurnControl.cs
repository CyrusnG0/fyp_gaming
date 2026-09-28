using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{
	
	public enum _TurnMode{
		FactionPerTurn,
		UnitPerTurn,
	}
	public enum _CDTracking{
		EveryTurn,
		EveryRound,
	}
	
	public class TurnControl : MonoBehaviour {
		
		#if UNITY_EDITOR
		public static bool inspector=false;
		#endif
		
		
		public _TurnMode turnMode;
		public static _TurnMode GetTurnMode(){ return instance.turnMode; }
		public static bool IsUnitPerTurn(){ return instance.turnMode==_TurnMode.UnitPerTurn; }
		public static bool IsFactionPerTurn(){ return instance.turnMode==_TurnMode.FactionPerTurn; }
		
		public bool enableUnitLimit=false;
		public int facUnitLimit=1;
		public static bool EnableUnitLimit(){ return instance.enableUnitLimit; }
		//public static float GetUnitLimit(){ return instance.enableUnitLimit ? 1 : Mathf.Infinity ; }
		public static float GetUnitLimit(){ return instance.enableUnitLimit ? Mathf.Max(1, instance.facUnitLimit) : Mathf.Infinity ; }
		
		public bool allowUnitSwitching=true;
		public static bool AllowUnitSwitching(){ return instance.allowUnitSwitching; }
		
		public bool waitForUnitDestroy=true;
		public static bool WaitForUnitDestroy(){ return instance.waitForUnitDestroy; }
		
		public _CDTracking cdTracking;	//use in UnitPerTurn only
		public static bool IterateCDEveryTurn(){ return instance.cdTracking==_CDTracking.EveryTurn; }
		public static bool IterateCDEveryRound(){ return instance.cdTracking==_CDTracking.EveryRound; }
		
		
		[Space(10)]
		public int roundCounter=0;	public static int GetRound(){ return instance.roundCounter; }
		public int currentTurn=-1;	public static int GetTurn(){ return instance.currentTurn; }
		
		//only called in UnitPerTurn mode, when current unit or a unit prior to current unit in move order is removed from game
		public static void RevertTurn(){ instance.currentTurn=Mathf.Max(-1, instance.currentTurn-1); }
		
		//only called in UnitPerTurn mode, when current unit or a unit prior to current unit in move order is removed from game
		public static void AddTurn(){ instance.currentTurn+=1; }
		
		
		private static TurnControl instance;
		
		public static void Init(bool useGlobalSetting){
			if(instance==null) instance=(TurnControl)FindObjectOfType(typeof(TurnControl));
			
			if(useGlobalSetting){
				GlobalSettingDB db=GlobalSettingDB.Init();
				instance.turnMode=db.turnMode;
				instance.allowUnitSwitching=db.allowUnitSwitching;
				instance.waitForUnitDestroy=db.waitForUnitDestroy;
				instance.cdTracking=db.cdTracking;
			}
		}
		
		
		public static void StartGame(){
			instance.currentTurn=-1;
			EndTurn();
		}
		
		
		public static void EndTurn(){ instance.StartCoroutine(instance._EndTurn()); }
		public IEnumerator _EndTurn(bool endRound=false){
			GridManager.EndTurn();	//to iterate node scanned by reveal fog-of-war
			//yield return StartCoroutine(UnitManager.EndTurn());			//just to iterate unit ability and  effect CD
			
			//display new turn?
			
			CollectibleManager.NewTurn();
			
			currentTurn+=1;
			endRound=UnitManager.CheckIfNewTurnEndRound(currentTurn);
			if(endRound) currentTurn=0;
			
			if(turnMode==_TurnMode.FactionPerTurn){
				yield return StartCoroutine(UnitManager.EndTurn_IterateCD());
				
				yield return new WaitForSeconds(0.25f);
				UnitManager.EndTurn_FactionPerTurn(currentTurn);
			}
			else if(turnMode==_TurnMode.UnitPerTurn){
				if(IterateCDEveryTurn() || (endRound && IterateCDEveryRound())) 
					yield return StartCoroutine(UnitManager.EndTurn_IterateCD());
				
				yield return new WaitForSeconds(0.25f);
				UnitManager.EndTurn_UnitPerTurn(currentTurn);
			}
			
			//~ if(turnMode==_TurnMode.FactionPerTurn){
				//~ currentTurn=UnitManager.EndTurn_FactionPerTurn(currentTurn);
				//~ if(currentTurn==0) endRound=true;
				//~ yield return StartCoroutine(UnitManager.EndTurn_IterateCD());
			//~ }
			//~ else if(turnMode==_TurnMode.UnitPerTurn){
				//~ currentTurn=UnitManager.EndTurn_UnitPerTurn(currentTurn);
				//~ if(currentTurn==0) endRound=true;
				
				//~ if(IterateCDEveryTurn() || (endRound && IterateCDEveryRound())) 
					//~ yield return StartCoroutine(UnitManager.EndTurn_IterateCD());
			//~ }
			
			if(endRound) roundCounter+=1;
			
			//~ Debug.Log("Check if selected unit is destroyed by dot effect");
			
			TBTK.OnNewTurn();
		}
		
		
		public static bool CanSwitchUnit(Unit unit){
			if(TurnControl.IsUnitPerTurn()) return false;
			if(!AllowUnitSwitching()) return false;
			
			if(EnableUnitLimit() && !unit.HasTakenAction()){//UnitManager.GetSelectedFaction().GetMoveCount()>=GetUnitLimit()){
				if(UnitManager.GetSelectedFaction().GetMoveCount()>=GetUnitLimit()) return false;
			}
			
			return true;
		}
		
	}

}