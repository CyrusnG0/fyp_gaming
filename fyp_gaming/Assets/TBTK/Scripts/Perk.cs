using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace TBTK{
	
	public enum _PerkType{
		NewUnitAbility,
		NewFactionAbility,
		
		ModifyUnit,
		ModifyUnitAbility,
		ModifyFactionAbility,
		ModifyEffect,
		//~ ModifyPerkCost,
		
		//~ Unit, 						//modify a particular set of unit
		//~ Unit_All, 				
		//~ UnitAbility, 				//modify a particular set of unit ability
		//~ UnitAbility_All, 		
		//~ FactionAbility, 		//modify a particular set of faction ability
		//~ FactionAbility_All, 	
		
		//~ NewUnitAbility, 		//unlock new unit ability
		//~ NewFactionAbility,	//unlock new faction ability
	}
	
	[System.Serializable]
	public class Perk : TBTKItem {
		
		public _PerkType type;
		
		public bool repeatable=false;	//not in use
		public bool unlocked=false;
		
		
		public int cost=1;
		//public int minLevel=1;									//min level to reach before becoming available (check GameControl.levelID)
		public int minPerkPoint=0;						//min amount of perk unlocked
		public List<int> prereq=new List<int>();	//prerequisite perk before becoming available, element is removed as the perk is unlocked in runtime
		
		public enum _StatsType{ Modifier, Multiplier }
		public _StatsType statsType=_StatsType.Multiplier;
		public bool IsMultiplier(){ return statsType==_StatsType.Multiplier; }
		
		public int abilityPID;								//for new ability to be add (both faction and unit)
		public Stats stats=new Stats();
		
		public bool applyToAll=true;
		public List<int> itemPIDList=new List<int>();	//for item being modified
		
		public enum _EffModType{ Append, Replace }
		public bool AppendEffect(){ return effModType==_EffModType.Append; }
		
		public _EffModType effModType=_EffModType.Append;
		public List<int> effIDList=new List<int>();		//for ability and unit hit effects
		
		public List<int> unitImmuneEffIDList=new List<int>();	//effect that unit is immuned to
		
		
		public bool IsUnlocked(){ return unlocked; }
		public int GetCost(){ return cost; }
		
		public void VerifyItemPIDList(){
			for(int i=0; i<itemPIDList.Count; i++){
				if(itemPIDList[i]<0){ itemPIDList.RemoveAt(i);	i-=1; }
			}
		}
		
		public bool UseStats(){
			if(type==_PerkType.ModifyUnit) return true;
			else if(type==_PerkType.ModifyUnitAbility) return true;
			else if(type==_PerkType.ModifyFactionAbility) return true;
			else if(type==_PerkType.ModifyEffect) return true;
			//else if(type==_PerkType.ModifyPerkCost) return true;
			return false;
		}
		public void Reset(){
			if(IsMultiplier())	 stats.ResetAsMultiplier(); 
			else					 stats.ResetAsModifier(); 
		}
		
		
		public string IsAvailable(){
			if(unlocked) return "";
			//if(GameControl.GetLevelID()<minLevel) return "Available at level "+minLevel;
			//if(PerkManager.GetPerkCurrency()<cost) return "Insufficient perk currency";
			if(PerkManager.GetPerkPoint()<minPerkPoint) return "Insufficient perk point. Require "+minPerkPoint;
			if(prereq.Count>0){
				string text=GetMissingPrereq();
				if(text!="") return "Require: "+text;
				
				//~ string text="Require: ";	bool req=false;
				//~ for(int i=0; i<prereq.Count; i++){
					//~ Perk perk=PerkManager.GetPerkFromID(prereq[i]);
					//~ if(perk==null){ prereq.RemoveAt(i); i-=1; continue; }
					//~ if(perk.unlocked) continue;
					//~ text+=((req) ? ", " : "")+perk.name;		req=true;
				//~ }
				//~ if(req) return text;
				//return "Not all prerequisite perk has been unlocked";
			}
			return "";
		}
		
		public string GetMissingPrereq(){
			string text="";	bool req=false;
			for(int i=0; i<prereq.Count; i++){
				Perk perk=PerkManager.GetPerkFromID(prereq[i]);
				if(perk==null){ prereq.RemoveAt(i); i-=1; continue; }
				if(perk.unlocked) continue;
				text+=((req) ? ", " : "")+perk.name;		req=true;
			}
			return text;
		}
		
		public string Unlock(bool useCurrency=true){
			if(useCurrency){
				if(PerkManager.GetPerkCurrency()<cost) return "Insufficient perk currency";
				PerkManager.SpendCurrency(cost);
			}
			
			//if(!repeatable){
				if(!unlocked) unlocked=true;
				else return "perk has been unlocked";
			//}
			
			Activate();
			
			return "";
		}
		
		public void Activate(){
			if(!PerkManager.InGameScene()) return;
			
			Debug.Log("Activate  "+type);
			
			if(type==_PerkType.NewUnitAbility){
				PerkManager.NewUnitAbilityPerkUnlocked(this);		SetupCallback();
			}
			else if(type==_PerkType.NewFactionAbility){
				PerkManager.NewFactionAbilityPerkUnlocked(abilityPID);
			}
			
			else if(type==_PerkType.ModifyUnit){
				PerkManager.AddUnitPerkID(prefabID);					SetupCallback();
			}
			else if(type==_PerkType.ModifyUnitAbility){
				PerkManager.AddUnitAbilityPerkID(prefabID);			SetupCallback();
			}
			else if(type==_PerkType.ModifyFactionAbility){
				PerkManager.AddFactionAbilityPerkID(prefabID);	SetupCallback();
			}
			else if(type==_PerkType.ModifyEffect){
				PerkManager.AddEffectPerkID(prefabID);				SetupCallback();
			}
		}
		
		
		public Perk Clone(){
			Perk clone=new Perk();
			
			base.Clone(this, clone);
			
			clone.type=type;
			
			clone.repeatable=repeatable;
			clone.unlocked=unlocked;
			
			clone.cost=cost;
			clone.minPerkPoint=minPerkPoint;
			clone.prereq=new List<int>( prereq );
			
			clone.statsType=statsType;
			
			clone.abilityPID=abilityPID;
			clone.stats=stats.Clone();

			clone.applyToAll=applyToAll;
			clone.itemPIDList=new List<int>( itemPIDList );
			
			clone.effModType=effModType;
			clone.effIDList=new List<int>( effIDList );
			
			clone.unitImmuneEffIDList=new List<int>( unitImmuneEffIDList );
			
			return clone;
		}
		
		
		
		
		public bool CheckID(int prefabID){
			return applyToAll ? true : itemPIDList.Contains(prefabID);
			
			//if(applyToAll) return true;
			//~ else if(type==_PerkType.ModifyUnit) 			return unitPIDList.Contains(prefabID);
			//~ else if(type==_PerkType.ModifyUnitAbility)		return uAbilityPIDList.Contains(prefabID);
			//~ else if(type==_PerkType.ModifyFactionAbility)	return fAffectPIDList.Contains(prefabID);
			//~ else if(type==_PerkType.ModifyEffect)			return effectPIDList.Contains(prefabID);
			//~ return false;
		}
		
		
		
		public List<int> ModifyEffectList(int prefabID, List<int> list){
			if(effIDList.Count==0 || !CheckID(prefabID)) return list;
			
			if(AppendEffect()){
				for(int i=0; i<effIDList.Count; i++) list.Add(effIDList[i]);
			}
			else list=effIDList;
			
			return list;
		}
		
		public List<int> ModifyImmuneEffectList(int prefabID, List<int> list){
			if(unitImmuneEffIDList.Count==0 || !CheckID(prefabID)) return list;
			for(int i=0; i<unitImmuneEffIDList.Count; i++) list.Add(unitImmuneEffIDList[i]);
			return list;
		}
		
		
		private void SetupCallback(){
			if(type==_PerkType.NewUnitAbility){
				GetAbilityPID = (prefabID) => { return CheckID(prefabID) ? abilityPID : -1 ; };
				return;
			}
			
			
			if(!IsMultiplier()){
				GetModHP = (prefabID) => 				{ return CheckID(prefabID) ? stats.hp : 0 ; };
				GetModAP = (prefabID) => 				{ return CheckID(prefabID) ? stats.ap : 0 ; };
				GetModHPRegen = (prefabID) => 		{ return CheckID(prefabID) ? stats.hpRegen : 0 ; };
				GetModAPRegen = (prefabID) => 		{ return CheckID(prefabID) ? stats.apRegen : 0 ; };
				
				GetModAttack = (prefabID) => 			{ return CheckID(prefabID) ? stats.attack : 0 ; };
				GetModDefense = (prefabID) => 		{ return CheckID(prefabID) ? stats.defense : 0 ; };
				GetModHit = (prefabID) => 				{ return CheckID(prefabID) ? stats.hit : 0 ; };
				GetModDodge = (prefabID) => 			{ return CheckID(prefabID) ? stats.dodge : 0 ; };
				GetModDmgHPMin = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgHPMin : 0 ; };
				GetModDmgHPMax = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgHPMax : 0 ; };
				GetModDmgAPMin = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgAPMin : 0 ; };
				GetModDmgAPMax = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgAPMax : 0 ; };
				GetModCritC = (prefabID) => 			{ return CheckID(prefabID) ? stats.critChance : 0 ; };
				GetModCritR = (prefabID) => 			{ return CheckID(prefabID) ? stats.critReduc : 0 ; };
				GetModCritM = (prefabID) => 			{ return CheckID(prefabID) ? stats.critMultiplier : 0 ; };
				
				GetModCDmgMul = (prefabID) => 		{ return CheckID(prefabID) ? stats.cDmgMultip : 0 ; };
				GetModCHitPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.cHitPenalty : 0 ; };
				GetModCCritPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.cCritPenalty : 0 ; };
				
				GetModODmgMul = (prefabID) => 		{ return CheckID(prefabID) ? stats.oDmgMultip : 0 ; };
				GetModOHitPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.oHitPenalty : 0 ; };
				GetModOCritPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.oCritPenalty : 0 ; };
				
				GetModARange = (prefabID) => 		{ return CheckID(prefabID) ? stats.attackRange : 0 ; };
				GetModARangeMin = (prefabID) => 	{ return CheckID(prefabID) ? stats.attackRangeMin : 0 ; };
				GetModMRange = (prefabID) => 		{ return CheckID(prefabID) ? stats.moveRange : 0 ; };
				GetModTPrioity = (prefabID) => 		{ return CheckID(prefabID) ? stats.turnPriority : 0 ; };
				GetModSight = (prefabID) => 			{ return CheckID(prefabID) ? stats.sight : 0 ; };
				GetModMoveLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.moveLimit : 0 ; };
				GetModAttackLim = (prefabID) =>		{ return CheckID(prefabID) ? stats.attackLimit : 0 ; };
				GetModCounterLim = (prefabID) =>	{ return CheckID(prefabID) ? stats.counterLimit : 0 ; };
				GetModAbilityLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.abilityLimit : 0 ; };
				
				GetModAbDur = (prefabID) => 			{ return CheckID(prefabID) ? stats.abDuration : 0 ; };
				GetModAbCD = (prefabID) => 			{ return CheckID(prefabID) ? stats.abCooldown : 0 ; };
				GetModAbUseLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.abUseLimit : 0 ; };
				GetModAbAPCost = (prefabID) => 		{ return CheckID(prefabID) ? stats.abApCost : 0 ; };
				GetModAbAOE = (prefabID) => 			{ return CheckID(prefabID) ? stats.abAoeRange : 0 ; };
				GetModAbEffHitC = (prefabID) => 		{ return CheckID(prefabID) ? stats.abEffHitChance : 0 ; };
				
				GetModEffDuration = (prefabID) => 	{ return CheckID(prefabID) ? stats.effduration : 0 ; };
				GetModEffHP = (prefabID) => 			{ return CheckID(prefabID) ? stats.effHP : 0 ; };
				GetModEffAP = (prefabID) => 			{ return CheckID(prefabID) ? stats.effAP : 0 ; };
			}
			if(IsMultiplier()){
				GetMulHP = (prefabID) => 				{ return CheckID(prefabID) ? stats.hp : 1 ; };
				GetMulAP = (prefabID) => 				{ return CheckID(prefabID) ? stats.ap : 1 ; };
				GetMulHPRegen = (prefabID) => 		{ return CheckID(prefabID) ? stats.hpRegen : 1 ; };
				GetMulAPRegen = (prefabID) => 		{ return CheckID(prefabID) ? stats.apRegen : 1 ; };
				
				GetMulAttack = (prefabID) => 			{ return CheckID(prefabID) ? stats.attack : 1 ; };
				GetMulDefense = (prefabID) => 		{ return CheckID(prefabID) ? stats.defense : 1 ; };
				GetMulHit = (prefabID) => 				{ return CheckID(prefabID) ? stats.hit : 1 ; };
				GetMulDodge = (prefabID) => 			{ return CheckID(prefabID) ? stats.dodge : 1 ; };
				GetMulDmgHPMin = (prefabID) => 		{ return CheckID(prefabID) ? stats.dmgHPMin : 1 ; };
				GetMulDmgHPMax = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgHPMax : 1 ; };
				GetMulDmgAPMin = (prefabID) => 		{ return CheckID(prefabID) ? stats.dmgAPMin : 1 ; };
				GetMulDmgAPMax = (prefabID) => 	{ return CheckID(prefabID) ? stats.dmgAPMax : 1 ; };
				GetMulCritC = (prefabID) => 			{ return CheckID(prefabID) ? stats.critChance : 1 ; };
				GetMulCritR = (prefabID) => 			{ return CheckID(prefabID) ? stats.critReduc : 1 ; };
				GetMulCritM = (prefabID) => 			{ return CheckID(prefabID) ? stats.critMultiplier : 1 ; };
				
				GetMulCDmgMul = (prefabID) => 		{ return CheckID(prefabID) ? stats.cDmgMultip : 1 ; };
				GetMulCHitPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.cHitPenalty : 1 ; };
				GetMulCCritPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.cCritPenalty : 1 ; };
				
				GetMulODmgMul = (prefabID) => 		{ return CheckID(prefabID) ? stats.oDmgMultip : 1 ; };
				GetMulOHitPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.oHitPenalty : 1 ; };
				GetMulOCritPen = (prefabID) => 		{ return CheckID(prefabID) ? stats.oCritPenalty : 1 ; };
				
				GetMulARange = (prefabID) => 			{ return CheckID(prefabID) ? stats.attackRange : 1 ; };
				GetMulARangeMin = (prefabID) => 	{ return CheckID(prefabID) ? stats.attackRangeMin : 1 ; };
				GetMulMRange = (prefabID) => 		{ return CheckID(prefabID) ? stats.moveRange : 1 ; };
				GetMulTPrioity = (prefabID) => 		{ return CheckID(prefabID) ? stats.turnPriority : 1 ; };
				GetMulSight = (prefabID) => 			{ return CheckID(prefabID) ? stats.sight : 1 ; };
				GetMulMoveLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.moveLimit : 1 ; };
				GetMulAttackLim = (prefabID) =>		{ return CheckID(prefabID) ? stats.attackLimit : 1 ; };
				GetMulCounterLim = (prefabID) =>		{ return CheckID(prefabID) ? stats.counterLimit : 1 ; };
				GetMulAbilityLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.abilityLimit : 1 ; };
				
				GetMulAbDur = (prefabID) => 			{ return CheckID(prefabID) ? stats.abDuration : 1 ; };
				GetMulAbCD = (prefabID) => 			{ return CheckID(prefabID) ? stats.abCooldown : 1 ; };
				GetMulAbUseLim = (prefabID) => 		{ return CheckID(prefabID) ? stats.abUseLimit : 1 ; };
				GetMulAbAPCost = (prefabID) => 		{ return CheckID(prefabID) ? stats.abApCost : 1 ; };
				GetMulAbAOE = (prefabID) => 			{ return CheckID(prefabID) ? stats.abAoeRange : 1 ; };
				GetMulAbEffHitC = (prefabID) => 		{ return CheckID(prefabID) ? stats.abEffHitChance : 1 ; };
				
				GetMulEffDuration = (prefabID) => 	{ return CheckID(prefabID) ? stats.effduration : 1 ; };
				GetMulEffHP = (prefabID) => 			{ return CheckID(prefabID) ? stats.effHP : 1 ; };
				GetMulEffAP = (prefabID) => 			{ return CheckID(prefabID) ? stats.effAP : 1 ; };
			}
		}
		
		
		
		public Func<int, int> GetAbilityPID = (prefabID) => { return -1 ; };
		
		
		public Func<int, float> GetModHP = (prefabID) => { return 0; };
		public Func<int, float> GetModAP = (prefabID) => { return 0; };
		public Func<int, float> GetModHPRegen = (prefabID) => { return 0; };
		public Func<int, float> GetModAPRegen = (prefabID) => { return 0; };
		
		public Func<int, float> GetModAttack = (prefabID) => { return 0; };
		public Func<int, float> GetModDefense = (prefabID) => { return 0; };
		public Func<int, float> GetModHit = (prefabID) => { return 0; };
		public Func<int, float> GetModDodge = (prefabID) => { return 0; };
		public Func<int, float> GetModDmgHPMin = (prefabID) => { return 0; };
		public Func<int, float> GetModDmgHPMax = (prefabID) => { return 0; };
		public Func<int, float> GetModDmgAPMin = (prefabID) => { return 0; };
		public Func<int, float> GetModDmgAPMax = (prefabID) => { return 0; };
		public Func<int, float> GetModCritC = (prefabID) => { return 0; };
		public Func<int, float> GetModCritR = (prefabID) => { return 0; };
		public Func<int, float> GetModCritM = (prefabID) => { return 0; };
		
		public Func<int, float> GetModCDmgMul = (prefabID) => { return 0; };
		public Func<int, float> GetModCHitPen = (prefabID) => { return 0; };
		public Func<int, float> GetModCCritPen = (prefabID) => { return 0; };
		
		public Func<int, float> GetModODmgMul = (prefabID) => { return 0; };
		public Func<int, float> GetModOHitPen = (prefabID) => { return 0; };
		public Func<int, float> GetModOCritPen = (prefabID) => { return 0; };
		
		public Func<int, float> GetModARange = (prefabID) => { return 0; };
		public Func<int, float> GetModARangeMin = (prefabID) => { return 0; };
		public Func<int, float> GetModMRange = (prefabID) => { return 0; };
		public Func<int, float> GetModTPrioity = (prefabID) => { return 0; };
		public Func<int, float> GetModSight = (prefabID) => { return 0; };
		public Func<int, float> GetModMoveLim = (prefabID) => { return 0; };
		public Func<int, float> GetModAttackLim = (prefabID) => { return 0; };
		public Func<int, float> GetModCounterLim = (prefabID) => { return 0; };
		public Func<int, float> GetModAbilityLim = (prefabID) => { return 0; };
		
		public Func<int, float> GetModAbDur = (prefabID) => { return 0; };
		public Func<int, float> GetModAbCD = (prefabID) => { return 0; };
		public Func<int, float> GetModAbUseLim = (prefabID) => { return 0; };
		public Func<int, float> GetModAbAPCost = (prefabID) => { return 0; };
		public Func<int, float> GetModAbAOE = (prefabID) => { return 0; };
		public Func<int, float> GetModAbEffHitC = (prefabID) => { return 0; };
		public Func<int, float> GetModAbSwFacDur = (prefabID) => { return 0; };
		
		public Func<int, float> GetModEffDuration = (prefabID) => { return 0; };
		public Func<int, float> GetModEffHP = (prefabID) => { return 0; };
		public Func<int, float> GetModEffAP = (prefabID) => { return 0; };

		
		
		public Func<int, float> GetMulHP = (prefabID) => { return 1; };
		public Func<int, float> GetMulAP = (prefabID) => { return 1; };
		public Func<int, float> GetMulHPRegen = (prefabID) => { return 1; };
		public Func<int, float> GetMulAPRegen = (prefabID) => { return 1; };
		public Func<int, float> GetMulAttack = (prefabID) => { return 1; };
		public Func<int, float> GetMulDefense = (prefabID) => { return 1; };
		public Func<int, float> GetMulHit = (prefabID) => { return 1; };
		public Func<int, float> GetMulDodge = (prefabID) => { return 1; };
		public Func<int, float> GetMulDmgHPMin = (prefabID) => { return 1; };
		public Func<int, float> GetMulDmgHPMax = (prefabID) => { return 1; };
		public Func<int, float> GetMulDmgAPMin = (prefabID) => { return 1; };
		public Func<int, float> GetMulDmgAPMax = (prefabID) => { return 1; };
		public Func<int, float> GetMulCritC = (prefabID) => { return 1; };
		public Func<int, float> GetMulCritR = (prefabID) => { return 1; };
		public Func<int, float> GetMulCritM = (prefabID) => { return 1; };
		
		public Func<int, float> GetMulCDmgMul = (prefabID) => { return 1; };
		public Func<int, float> GetMulCHitPen = (prefabID) => { return 1; };
		public Func<int, float> GetMulCCritPen = (prefabID) => { return 1; };
		
		public Func<int, float> GetMulODmgMul = (prefabID) => { return 1; };
		public Func<int, float> GetMulOHitPen = (prefabID) => { return 1; };
		public Func<int, float> GetMulOCritPen = (prefabID) => { return 1; };
		
		public Func<int, float> GetMulARange = (prefabID) => { return 1; };
		public Func<int, float> GetMulARangeMin = (prefabID) => { return 1; };
		public Func<int, float> GetMulMRange = (prefabID) => { return 1; };
		public Func<int, float> GetMulTPrioity = (prefabID) => { return 1; };
		public Func<int, float> GetMulSight = (prefabID) => { return 1; };
		public Func<int, float> GetMulMoveLim = (prefabID) => { return 1; };
		public Func<int, float> GetMulAttackLim = (prefabID) => { return 1; };
		public Func<int, float> GetMulCounterLim = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbilityLim = (prefabID) => { return 1; };
		
		public Func<int, float> GetMulAbDur = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbCD = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbUseLim = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbAPCost = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbAOE = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbEffHitC = (prefabID) => { return 1; };
		public Func<int, float> GetMulAbSwFacDur = (prefabID) => { return 1; };
		
		public Func<int, float> GetMulEffDuration = (prefabID) => { return 1; };
		public Func<int, float> GetMulEffHP = (prefabID) => { return 1; };
		public Func<int, float> GetMulEffAP = (prefabID) => { return 1; };
		
	}

}