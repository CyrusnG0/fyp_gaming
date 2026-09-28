using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{
	
	[System.Serializable]
	public class Stats{
		public float hp=10;
		public float ap=10;
		
		public float hpRegen=0;
		public float apRegen=2;
		
		public float attack=80;
		public float defense=20;
		
		public float hit=70;
		public float dodge=20f;
		
		public float dmgHPMin=4;
		public float dmgHPMax=7;
		
		public float dmgAPMin=4;
		public float dmgAPMax=7;
		
		public float critChance=.15f;
		public float critReduc=0;
		public float critMultiplier=2;
		
		public float cDmgMultip=0.5f;		//counter multiplier
		public float cHitPenalty=0.5f;		//counter multiplier
		public float cCritPenalty=0.5f;		//counter multiplier
		
		public float oDmgMultip=0.5f;		//overwatch multiplier
		public float oHitPenalty=0.5f;		//overwatch multiplier
		public float oCritPenalty=0.5f;		//overwatch multiplier
		
		public float attackRange=3;
		public float attackRangeMin=0;
		public float moveRange=3;
		public float turnPriority=1;
		public float sight=10;
		
		public float moveLimit=1;
		public float attackLimit=1;
		public float counterLimit=1;
		public float abilityLimit=1;
		
		//used by perk only, to modify ability
		public float abDuration=2;	//use for switch-fac and reveal-fog
		public float abCooldown=2;
		public float abUseLimit=0;
		public float abApCost=0;
		public float abAoeRange=0;
		public float abEffHitChance=0;
		//public float abSwitchFacDur=0;
		
		//used by perk only, to modify effect
		public float effduration=0;
		public float effHP=0;
		public float effAP=0;
		
		public void ResetAsModifier(){
			hp=0;					ap=0;					hpRegen=0;			apRegen=0;
			attack=0;				defense=0;				hit=0;					dodge=0;
			dmgHPMin=0;			dmgHPMax=0;			dmgAPMin=0;			dmgAPMax=0;
			critChance=0;			critReduc=0;			critMultiplier=0;
			cDmgMultip=0;		cHitPenalty=0;		cCritPenalty=0;
			oDmgMultip=0;		oHitPenalty=0;		oCritPenalty=0;
			attackRange=0;		attackRangeMin=0;	moveRange=0;		turnPriority=0;			sight=0;
			moveLimit=0;			attackLimit=0;			counterLimit=0;		abilityLimit=0;
			
			abDuration=0;			abCooldown=0;		abUseLimit=0;			abApCost=0;		abAoeRange=0;		abEffHitChance=0;
			
			effduration=0;			effHP=0;				effAP=0;
		}
		public void ResetAsMultiplier(){
			hp=1;					ap=1;					hpRegen=1;			apRegen=1;		
			attack=1;				defense=1;				hit=1;					dodge=1;
			dmgHPMin=1;			dmgHPMax=1;			dmgAPMin=1;			dmgAPMax=1;
			critChance=1;			critReduc=1;			critMultiplier=1;
			cDmgMultip=1;		cHitPenalty=1;		cCritPenalty=1;
			oDmgMultip=1;		oHitPenalty=1;		oCritPenalty=1;
			attackRange=1;		attackRangeMin=1;	moveRange=1;		turnPriority=1;			sight=1;
			moveLimit=1;			attackLimit=1;			counterLimit=1;		abilityLimit=1;
			
			abDuration=1;			abCooldown=1;		abUseLimit=1;			abApCost=1;		abAoeRange=1;		abEffHitChance=1;
			
			effduration=1;			effHP=1;				effAP=1;
		}
		
		public void ApplyMultiplier(Stats stats){
			hp*=stats.hp;		hpRegen*=stats.hpRegen;
			ap*=stats.ap;		apRegen*=stats.apRegen;
			
			attack*=stats.attack;
			defense*=stats.defense;
			
			hit*=stats.hit;
			dodge*=stats.dodge;
			
			dmgHPMin*=stats.dmgHPMin;
			dmgHPMax*=stats.dmgHPMax;
			dmgAPMin*=stats.dmgAPMin;
			dmgAPMax*=stats.dmgAPMax;
			
			critChance*=stats.critChance;
			critReduc*=stats.critReduc;
			critMultiplier*=stats.critMultiplier;
			
			cDmgMultip*=stats.cDmgMultip;
			cHitPenalty*=stats.cHitPenalty;
			cCritPenalty*=stats.cCritPenalty;
			
			oDmgMultip*=stats.oDmgMultip;
			oHitPenalty*=stats.oHitPenalty;
			oCritPenalty*=stats.oCritPenalty;
			
			attackRange*=stats.attackRange;
			attackRangeMin*=stats.attackRangeMin;
			moveRange*=stats.moveRange;
			turnPriority*=stats.turnPriority;
			sight*=stats.sight;
			
			moveLimit*=stats.moveLimit;
			attackLimit*=stats.attackLimit;
			counterLimit*=stats.counterLimit;
			abilityLimit*=stats.abilityLimit;
			
			abDuration*=stats.abDuration;
			abCooldown*=stats.abCooldown;
			abUseLimit*=stats.abUseLimit;
			abApCost*=stats.abApCost;
			abAoeRange*=stats.abAoeRange;
			abEffHitChance*=stats.abEffHitChance;
			
			effduration*=stats.effduration;
			effHP*=stats.effHP;
			effAP*=stats.effAP;
		}
		public void ApplyModifier(Stats stats){
			hp+=stats.hp;		hpRegen+=stats.hpRegen;
			ap+=stats.ap;		apRegen+=stats.apRegen;
			
			attack+=stats.attack;
			defense+=stats.defense;
			
			hit+=stats.hit;
			dodge+=stats.dodge;
			
			dmgHPMin+=stats.dmgHPMin;
			dmgHPMax+=stats.dmgHPMax;
			dmgAPMin+=stats.dmgAPMin;
			dmgAPMax+=stats.dmgAPMax;
			
			critChance+=stats.critChance;
			critReduc+=stats.critReduc;
			critMultiplier+=stats.critMultiplier;
			
			cDmgMultip+=stats.cDmgMultip;
			cHitPenalty+=stats.cHitPenalty;
			cCritPenalty+=stats.cCritPenalty;
			
			oDmgMultip+=stats.oDmgMultip;
			oHitPenalty+=stats.oHitPenalty;
			oCritPenalty+=stats.oCritPenalty;
			
			attackRange+=stats.attackRange;
			attackRangeMin+=stats.attackRangeMin;
			moveRange+=stats.moveRange;
			turnPriority+=stats.turnPriority;
			sight+=stats.sight;
			
			moveLimit+=stats.moveLimit;
			attackLimit+=stats.attackLimit;
			counterLimit+=stats.counterLimit;
			abilityLimit+=stats.abilityLimit;
			
			abDuration+=stats.abDuration;
			abCooldown+=stats.abCooldown;
			abUseLimit+=stats.abUseLimit;
			abApCost+=stats.abApCost;
			abAoeRange+=stats.abAoeRange;
			
			effduration+=stats.effduration;
			effHP+=stats.effHP;
			effAP+=stats.effAP;
		}
		
		public Stats Clone(){ return ObjectCopier.Clone(this); }
	}
	
}
