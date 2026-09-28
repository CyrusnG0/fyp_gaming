using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	[System.Serializable]
	public class Effect : TBTKItem {	//attack buff/debuff to be apply on target
		//~ public int srcID=0;	//assigned during runtime, unique to unit-type/ability that cast it, used to determined if the effect existed on target if it's not stackable
		
		//~ public enum _SrcType{ Unit, FAbility, UAbility, Perk, None }
		//~ public _SrcType srcType=_SrcType.None;
		//~ public int srcPrefabID=0;
		
		public bool showInUnitInfo=true;	//not in used yet
		
		public bool stackable=false;
		public float duration=1;
		[HideInInspector] public float durationRemain=1;	//runtime attribute, only used on applied effect
		
		public int range=2;	//for when the effect is used as aura only
		
		public enum _ImpactType{None, Negative, Positive}
		public _ImpactType impactType;
		
		public bool HasNoImpact(){ return impactType==_ImpactType.None; }
		public bool HasPositiveImpact(){ return impactType==_ImpactType.Positive; }
		public bool HasNegativeImpact(){ return impactType==_ImpactType.Negative; }
		
		public int damageType=0;
		public float hpModifierMin=5;
		public float hpModifierMax=5;
		public int GetRandHPModifier(){ return (int)Rand.Range(hpModifierMin, hpModifierMax); }
		
		public float apModifierMin=5;
		public float apModifierMax=5;
		public int GetRandAPModifier(){ return (int)Rand.Range(apModifierMin, apModifierMax); }
		
		
		public enum _EffType{ Modifier, Multiplier }
		public _EffType effType=_EffType.Multiplier;
		public bool IsMultiplier(){ return effType==_EffType.Multiplier; }
		
		public Stats stats=new Stats();
		
		
		public bool overwatch=false;
		public bool stun=false;
		public bool disableAbility=false;
		
		
		public VisualObject hitVisualEffect;
		public Transform activeVisualEffect;
		
		
		public void Reset(){
			if(IsMultiplier())	 stats.ResetAsMultiplier(); 
			else					 stats.ResetAsModifier(); 
		}
		
		
		public Effect Clone(bool applyPerk=false){
			Effect clone=new Effect();
			
			base.Clone(this, clone);
			
			clone.showInUnitInfo=showInUnitInfo;
			
			clone.stackable=stackable;	clone.duration=duration;	clone.range=range;
			
			clone.impactType=impactType;			clone.damageType=damageType;
			clone.hpModifierMin=hpModifierMin;		clone.hpModifierMax=hpModifierMax;
			clone.apModifierMin=apModifierMin;		clone.apModifierMax=apModifierMax;
			
			clone.effType=effType;
			clone.stats=stats.Clone();
			
			clone.overwatch=overwatch;
			clone.stun=stun;
			clone.disableAbility=disableAbility;
			
			clone.hitVisualEffect=hitVisualEffect.Clone();
			clone.activeVisualEffect=activeVisualEffect;
			
			if(applyPerk) clone.ApplyPerk();
			
			return clone;
		}
		
		
		public void ApplyPerk(){
			int pID=prefabID;
			
			duration=duration 							* PerkManager.GetEffMulEffDuration(pID) + PerkManager.GetEffModEffDuration(pID) ;
			
			hpModifierMin=hpModifierMin 				* PerkManager.GetEffMulEffHP(pID) + PerkManager.GetEffModEffHP(pID) ;
			hpModifierMax=hpModifierMax 			* PerkManager.GetEffMulEffHP(pID) + PerkManager.GetEffModEffHP(pID) ;
			
			apModifierMin=apModifierMin 				* PerkManager.GetEffMulEffAP(pID) + PerkManager.GetEffModEffAP(pID) ;
			apModifierMax=apModifierMax 			* PerkManager.GetEffMulEffAP(pID) + PerkManager.GetEffModEffAP(pID) ;
			
			
			stats.attack=stats.attack 				* PerkManager.GetEffMulAttack(pID) + PerkManager.GetEffModAttack(pID) ;
			stats.defense=stats.defense 			* PerkManager.GetEffMulDefense(pID) + PerkManager.GetEffModDefense(pID) ;
			
			stats.hit=stats.hit 							* PerkManager.GetEffMulHit(pID) + PerkManager.GetEffModHit(pID) ;
			stats.dodge=stats.dodge 					* PerkManager.GetEffMulDodge(pID) + PerkManager.GetEffModDodge(pID) ;
			
			stats.dmgHPMin=stats.dmgHPMin 		* PerkManager.GetEffMulDmgHPMin(pID) + PerkManager.GetEffModDmgHPMin(pID) ;
			stats.dmgHPMax=stats.dmgHPMax 		* PerkManager.GetEffMulDmgHPMax(pID) + PerkManager.GetEffModDmgHPMax(pID) ;
			stats.dmgAPMin=stats.dmgAPMin 		* PerkManager.GetEffMulDmgAPMin(pID) + PerkManager.GetEffModDmgAPMin(pID) ;
			stats.dmgAPMax=stats.dmgAPMax 		* PerkManager.GetEffMulDmgAPMax(pID) + PerkManager.GetEffModDmgAPMax(pID) ;
			
			stats.critChance=stats.critChance 		* PerkManager.GetEffMulCritC(pID) + PerkManager.GetEffModCritC(pID) ;
			stats.critReduc=stats.critReduc 		* PerkManager.GetEffMulCritR(pID) + PerkManager.GetEffModCritR(pID) ;
			stats.critMultiplier=stats.critMultiplier 	* PerkManager.GetEffMulCritM(pID) + PerkManager.GetEffModCritM(pID) ;
			
			stats.cDmgMultip=stats.cDmgMultip 	* PerkManager.GetEffMulCDmgMul(pID) + PerkManager.GetEffModCDmgMul(pID) ;
			stats.cHitPenalty=stats.cHitPenalty 	* PerkManager.GetEffMulCHitPen(pID) + PerkManager.GetEffModCHitPen(pID) ;
			stats.cCritPenalty=stats.cCritPenalty 	* PerkManager.GetEffMulCCritPen(pID) + PerkManager.GetEffModCCritPen(pID) ;
			
			stats.oDmgMultip=stats.oDmgMultip 	* PerkManager.GetEffMulODmgMul(pID) + PerkManager.GetEffModODmgMul(pID) ;
			stats.oHitPenalty=stats.oHitPenalty 	* PerkManager.GetEffMulOHitPen(pID) + PerkManager.GetEffModOHitPen(pID) ;
			stats.oCritPenalty=stats.oCritPenalty 	* PerkManager.GetEffMulOCritPen(pID) + PerkManager.GetEffModOCritPen(pID) ;
			
			stats.attackRange=stats.attackRange	* PerkManager.GetEffMulARange(pID) + PerkManager.GetEffModARange(pID) ;
			stats.moveRange=stats.moveRange 	* PerkManager.GetEffMulMRange(pID) + PerkManager.GetEffModMRange(pID) ;
			stats.turnPriority=stats.turnPriority 	* PerkManager.GetEffMulTPrioity(pID) + PerkManager.GetEffModTPrioity(pID) ;
			stats.sight=stats.sight 					* PerkManager.GetEffMulSight(pID) + PerkManager.GetEffModSight(pID) ;
			
			stats.moveLimit=stats.moveLimit 		* PerkManager.GetEffMulMoveLim(pID) + PerkManager.GetEffModMoveLim(pID) ;
			stats.attackLimit=stats.attackLimit 	* PerkManager.GetEffMulAttackLim(pID) + PerkManager.GetEffModAttackLim(pID) ;
			stats.counterLimit=stats.counterLimit 	* PerkManager.GetEffMulCounterLim(pID) + PerkManager.GetEffModCounterLim(pID) ;
			stats.abilityLimit=stats.abilityLimit 		* PerkManager.GetEffMulAbilityLim(pID) + PerkManager.GetEffModAbilityLim(pID) ;
		}
		
	}
	
}
