using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace TBTK{

	public class PerkManager : MonoBehaviour {
		
		public bool inGameScene;
		public static bool InGameScene(){ return instance.inGameScene; }
		
		
		[Space(8)]
		public bool loadProgressFromCache;
		public bool saveProgressToCache;
		
		public static int cacheCurrency=1;
		public static List<int> cacheUnlockedIDList=new List<int>();
		
		
		[Space(8)]
		public int currency=0;
		public static int GetPerkCurrency(){ return instance.currency; }
		public static void GainCurrency(int value){ instance.currency+=value; }
		public static void SpendCurrency(int value){ instance.currency=Mathf.Max(0, instance.currency-value); }
		
		[HideInInspector] public int perkPoint=0;
		public static int GetPerkPoint(){ return instance.perkPoint; }
		
		
		public List<int> unavailableIDList=new List<int>();
		public List<int> unlockedIDList=new List<int>();
		
		List<Perk> perkList=new List<Perk>();
		public static List<Perk> GetPerkList(){ return instance.perkList; }
		public static Perk GetPerkOfIndex(int idx){ return instance.perkList[idx]; }
		
		private static PerkManager instance;
		public static bool PerkSystemEnabled(){ return instance!=null ; }
		
		void Awake() {
			if(instance==null) instance=this;
			else if(instance!=this){ Destroy(gameObject); return; }
			
			uPIDList.Clear();
			uaPIDList.Clear();
			faPIDList.Clear();
			ePIDList.Clear();
			uPIDAbilityList.Clear();
			
			List<Perk> dbList=PerkDB.GetList();
			for(int i=0; i<dbList.Count; i++){
				if(!unavailableIDList.Contains(dbList[i].prefabID)){
					Perk perk=dbList[i].Clone();
					perkList.Add(perk);
				}
			}
			
			if(loadProgressFromCache){
				currency=cacheCurrency;
				unlockedIDList=cacheUnlockedIDList;
			}
			
			for(int i=0; i<perkList.Count; i++){
				if(unlockedIDList.Contains(perkList[i].prefabID) && !perkList[i].unlocked){
					_UnlockPerk(perkList[i], false);	//dont use currency since these are pre-purchased perk
				}
			}
		}
		
		void OnDestroy(){
			if(!saveProgressToCache) return;
			
			cacheCurrency=currency;
			
			cacheUnlockedIDList.Clear();
			for(int i=0; i<perkList.Count; i++){
				if(perkList[i].IsUnlocked()) cacheUnlockedIDList.Add(perkList[i].prefabID);
			}
		}
		
		
		
		
		public static int GetIndexFromID(int prefabID){
			if(instance==null) return -1;
			for(int i=0; i<instance.perkList.Count; i++){ if(instance.perkList[i].prefabID==prefabID) return i; }
			return -1;
		}
		
		public static Perk GetPerkFromIndex(int idx){ return instance.perkList[idx]; }
		public static Perk GetPerkFromID(int prefabID){
			int idx=GetIndexFromID(prefabID);
			return (idx>=0 ? instance.perkList[idx] : null);
		}
		
		public static string IsPerkAvailable(int perkID){
			int idx=GetIndexFromID(perkID);
			return (idx>=0 ? instance.perkList[idx].IsAvailable() : "PerkID doesnt correspond to any perk");
		}
		public static bool IsPerkUnlocked(int perkID){
			int idx=GetIndexFromID(perkID);
			return (idx>=0 ? instance.perkList[idx].unlocked : false);
		}
		
		
		public static string UnlockPerk(int prefabID, bool useCurrency=true){ 
			int idx=GetIndexFromID(prefabID);
			return (idx>=0 ? instance._UnlockPerk(instance.perkList[idx], useCurrency) : "PerkID doesnt correspond to any perk");
		}
		public static string UnlockPerk(Perk perk, bool useCurrency=true){ return instance._UnlockPerk(perk, useCurrency); }
		public string _UnlockPerk(Perk perk, bool useCurrency=true){
			string text=perk.Unlock(useCurrency); 
			if(text!="") return text;
			
			perkPoint+=1;
			
			return "";
		}
		
		
		#region new FactionAbility
			//called as soon as a perk is unlocked
			public static void NewFactionAbilityPerkUnlocked(int abPID){	
				UnitManager.AddAbilityToPlayerFaction(abPID);
			}
		#endregion
		
		
		#region new UnitAbility
			//called as soon as a perk is unlocked
			public static void NewUnitAbilityPerkUnlocked(Perk perk){	
				uPIDAbilityList.Add(perk.prefabID);
				UnitManager.AddAbilityToPlayerUnit(perk.abilityPID, perk.applyToAll ? null : perk.itemPIDList);
			}
			
			public static List<int> uPIDAbilityList=new List<int>();	//prefabID of all new unit ability perk that has been unlocked
			
			//in case a unit is spawned after the perk is unlocked
			public static List<int> GetUnitAbilityID(int prefabID){	
				List<int> unitABIDList=new List<int>();
				for(int i=0; i<uPIDAbilityList.Count; i++){ 
					int pID=GetPerkFromID(uPIDAbilityList[i]).GetAbilityPID(prefabID);
					if(pID>=0) unitABIDList.Add(pID);
				}
				return unitABIDList;
			}
		#endregion
		
		
		#region unit
			public static List<int> uPIDList=new List<int>();	//prefabID of all unit related perk that has been unlocked
			public static void AddUnitPerkID(int ID){ uPIDList.Add(ID); } 
			
			#region unit modifer
			public static float GetUnitModHP(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModHP(prefabID); }  return value;
			}
			public static float GetUnitModAP(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModAP(prefabID); } return value;
			}
			public static float GetUnitModHPRegen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModHPRegen(prefabID); } return value;
			}
			public static float GetUnitModAPRegen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModAPRegen(prefabID); } return value;
			}
			public static float GetUnitModAttack(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModAttack(prefabID); } return value;
			}
			public static float GetUnitModDefense(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDefense(prefabID); } return value;
			}
			public static float GetUnitModHit(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModHit(prefabID); } return value;
			}
			public static float GetUnitModDodge(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDodge(prefabID); } return value;
			}
			public static float GetUnitModDmgHPMin(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDmgHPMin(prefabID); } return value;
			}
			public static float GetUnitModDmgHPMax(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDmgHPMax(prefabID); } return value;
			}
			public static float GetUnitModDmgAPMin(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDmgAPMin(prefabID); } return value;
			}
			public static float GetUnitModDmgAPMax(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModDmgAPMax(prefabID); } return value;
			}
			public static float GetUnitModCritC(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCritC(prefabID); } return value;
			}
			public static float GetUnitModCritR(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCritR(prefabID); } return value;
			}
			public static float GetUnitModCritM(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCritM(prefabID); } return value;
			}
			
			public static float GetUnitModCDmgMul(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCDmgMul(prefabID); } return value;
			}
			public static float GetUnitModCHitPen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCHitPen(prefabID); } return value;
			}
			public static float GetUnitModCCritPen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCCritPen(prefabID); } return value;
			}
			public static float GetUnitModODmgMul(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModODmgMul(prefabID); } return value;
			}
			public static float GetUnitModOHitPen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModOHitPen(prefabID); } return value;
			}
			public static float GetUnitModOCritPen(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModOCritPen(prefabID); } return value;
			}
			
			public static float GetUnitModARange(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModARange(prefabID); } return value;
			}
			public static float GetUnitModARangeMin(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModARangeMin(prefabID); } return value;
			}
			public static float GetUnitModMRange(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModMRange(prefabID); } return value;
			}
			public static float GetUnitModTPrioity(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModTPrioity(prefabID); } return value;
			}
			public static float GetUnitModSight(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModSight(prefabID); } return value;
			}
			public static float GetUnitModMoveLim(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModMoveLim(prefabID); } return value;
			}
			public static float GetUnitModAttackLim(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModAttackLim(prefabID); } return value;
			}
			public static float GetUnitModCounterLim(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModCounterLim(prefabID); } return value;
			}
			public static float GetUnitModAbilityLim(int prefabID, float value=0){
				for(int i=0; i<uPIDList.Count; i++){ value+=GetPerkFromID(uPIDList[i]).GetModAbilityLim(prefabID); } return value;
			}
			#endregion //unit modifer
		
			#region unit multiplier
			public static float GetUnitMulHP(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulHP(prefabID); } return value;
			}
			public static float GetUnitMulAP(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulAP(prefabID); } return value;
			}
			public static float GetUnitMulHPRegen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulHPRegen(prefabID); } return value;
			}
			public static float GetUnitMulAPRegen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulAPRegen(prefabID); } return value;
			}
			public static float GetUnitMulAttack(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulAttack(prefabID); } return value;
			}
			public static float GetUnitMulDefense(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDefense(prefabID); } return value;
			}
			public static float GetUnitMulHit(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulHit(prefabID); } return value;
			}
			public static float GetUnitMulDodge(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDodge(prefabID); } return value;
			}
			public static float GetUnitMulDmgHPMin(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDmgHPMin(prefabID); } return value;
			}
			public static float GetUnitMulDmgHPMax(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDmgHPMax(prefabID); } return value;
			}
			public static float GetUnitMulDmgAPMin(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDmgAPMin(prefabID); } return value;
			}
			public static float GetUnitMulDmgAPMax(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulDmgAPMax(prefabID); } return value;
			}
			public static float GetUnitMulCritC(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCritC(prefabID); } return value;
			}
			public static float GetUnitMulCritR(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCritR(prefabID); } return value;
			}
			public static float GetUnitMulCritM(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCritM(prefabID); } return value;
			}
			
			public static float GetUnitMulCDmgMul(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCDmgMul(prefabID); } return value;
			}
			public static float GetUnitMulCHitPen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCHitPen(prefabID); } return value;
			}
			public static float GetUnitMulCCritPen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCCritPen(prefabID); } return value;
			}
			public static float GetUnitMulODmgMul(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulODmgMul(prefabID); } return value;
			}
			public static float GetUnitMulOHitPen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulOHitPen(prefabID); } return value;
			}
			public static float GetUnitMulOCritPen(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulOCritPen(prefabID); } return value;
			}
			
			public static float GetUnitMulARange(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulARange(prefabID); } return value;
			}
			public static float GetUnitMulARangeMin(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulARangeMin(prefabID); } return value;
			}
			public static float GetUnitMulMRange(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulMRange(prefabID); } return value;
			}
			public static float GetUnitMulTPrioity(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulTPrioity(prefabID); } return value;
			}
			public static float GetUnitMulSight(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulSight(prefabID); } return value;
			}
			public static float GetUnitMulMoveLim(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulMoveLim(prefabID); } return value;
			}
			public static float GetUnitMulAttackLim(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulAttackLim(prefabID); } return value;
			}
			public static float GetUnitMulCounterLim(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulCounterLim(prefabID); } return value;
			}
			public static float GetUnitMulAbilityLim(int prefabID, float value=1){
				for(int i=0; i<uPIDList.Count; i++){ value*=GetPerkFromID(uPIDList[i]).GetMulAbilityLim(prefabID); } return value;
			}
			#endregion //unit multiplier
		
			public static List<int> ModifyUnitAttackEffectList(int prefabID, List<int> list){
				for(int i=0; i<uPIDList.Count; i++){ list=GetPerkFromID(uPIDList[i]).ModifyEffectList(prefabID, list); } return list;
			}
			
			public static List<int> ModifyUnitImmuneEffectList(int prefabID, List<int> list){
				for(int i=0; i<uPIDList.Count; i++){ list=GetPerkFromID(uPIDList[i]).ModifyImmuneEffectList(prefabID, list); } return list;
			}
			
		#endregion //unit
		
		
		
		
		#region UnitAbility
		public static List<int> uaPIDList=new List<int>();	//prefabID of all UnitAbility related perk that has been unlocked
		public static void AddUnitAbilityPerkID(int ID){ uaPIDList.Add(ID); } 
			
			#region UnitAbility modifier
			public static float GetUAbilityModAttack(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAttack(prefabID); } return value;
			}
			public static float GetUAbilityModHit(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModHit(prefabID); } return value;
			}
			public static float GetUAbilityModDmgHPMin(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModDmgHPMin(prefabID); } return value;
			}
			public static float GetUAbilityModDmgHPMax(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModDmgHPMax(prefabID); } return value;
			}
			public static float GetUAbilityModDmgAPMin(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModDmgAPMin(prefabID); } return value;
			}
			public static float GetUAbilityModDmgAPMax(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModDmgAPMax(prefabID); } return value;
			}
			public static float GetUAbilityModCritC(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModCritC(prefabID); } return value;
			}
			public static float GetUAbilityModCritM(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModCritM(prefabID); } return value;
			}
			
			public static float GetUAbilityModRange(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModARange(prefabID); } return value;
			}
			public static float GetUAbilityModAOE(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbAOE(prefabID); } return value;
			}
			
			public static float GetUAbilityModDur(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbDur(prefabID); } return value;
			}
			public static float GetUAbilityModCD(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbCD(prefabID); } return value;
			}
			public static float GetUAbilityModUseLim(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbUseLim(prefabID); } return value;
			}
			public static float GetUAbilityModAPCost(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbAPCost(prefabID); } return value;
			}
			public static float GetUAbilityModEffHitC(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbEffHitC(prefabID); } return value;
			}
			public static float GetUAbilityModSwFacDur(int prefabID, float value=0){
				for(int i=0; i<uaPIDList.Count; i++){ value+=GetPerkFromID(uaPIDList[i]).GetModAbSwFacDur(prefabID); } return value;
			}
			#endregion //UnitAbility modifier
			
			#region UnitAbility multiplier
			public static float GetUAbilityMulAttack(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAttack(prefabID); } return value;
			}
			public static float GetUAbilityMulHit(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulHit(prefabID); } return value;
			}
			public static float GetUAbilityMulDmgHPMin(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulDmgHPMin(prefabID); } return value;
			}
			public static float GetUAbilityMulDmgHPMax(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulDmgHPMax(prefabID); } return value;
			}
			public static float GetUAbilityMulDmgAPMin(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulDmgAPMin(prefabID); } return value;
			}
			public static float GetUAbilityMulDmgAPMax(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulDmgAPMax(prefabID); } return value;
			}
			public static float GetUAbilityMulCritC(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulCritC(prefabID); } return value;
			}
			public static float GetUAbilityMulCritM(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulCritM(prefabID); } return value;
			}
			
			public static float GetUAbilityMulRange(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulARange(prefabID); } return value;
			}
			public static float GetUAbilityMulAOE(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbAOE(prefabID); } return value;
			}
			
			public static float GetUAbilityMulDur(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbDur(prefabID); } return value;
			}
			public static float GetUAbilityMulCD(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbCD(prefabID); } return value;
			}
			public static float GetUAbilityMulUseLim(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbUseLim(prefabID); } return value;
			}
			public static float GetUAbilityMulAPCost(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbAPCost(prefabID); } return value;
			}
			public static float GetUAbilityMulEffHitC(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbEffHitC(prefabID); } return value;
			}
			public static float GetUAbilityMulSwFacDur(int prefabID, float value=1){
				for(int i=0; i<uaPIDList.Count; i++){ value*=GetPerkFromID(uaPIDList[i]).GetMulAbSwFacDur(prefabID); } return value;
			}
			#endregion //UnitAbility multiplier
			
			public static List<int> ModifyUAbilityEffectList(int prefabID, List<int> list){
				for(int i=0; i<uaPIDList.Count; i++){ list=GetPerkFromID(uaPIDList[i]).ModifyEffectList(prefabID, list); } return list;
			}
			
		#endregion	//unit ability
			
			
			
			
		#region FactionAbility
		public static List<int> faPIDList=new List<int>();	//prefabID of all UnitAbility related perk that has been unlocked
		public static void AddFactionAbilityPerkID(int ID){ faPIDList.Add(ID); } 
			
			#region FactionAbility modifier
			public static float GetFAbilityModAttack(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAttack(prefabID); } return value;
			}
			public static float GetFAbilityModHit(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModHit(prefabID); } return value;
			}
			public static float GetFAbilityModDmgHPMin(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModDmgHPMin(prefabID); } return value;
			}
			public static float GetFAbilityModDmgHPMax(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModDmgHPMax(prefabID); } return value;
			}
			public static float GetFAbilityModDmgAPMin(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModDmgAPMin(prefabID); } return value;
			}
			public static float GetFAbilityModDmgAPMax(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModDmgAPMax(prefabID); } return value;
			}
			public static float GetFAbilityModCritC(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModCritC(prefabID); } return value;
			}
			public static float GetFAbilityModCritM(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModCritM(prefabID); } return value;
			}
			
			public static float GetFAbilityModRange(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModARange(prefabID); } return value;
			}
			public static float GetFAbilityModAOE(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbAOE(prefabID); } return value;
			}
			
			public static float GetFAbilityModDur(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbDur(prefabID); } return value;
			}
			public static float GetFAbilityModCD(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbCD(prefabID); } return value;
			}
			public static float GetFAbilityModUseLim(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbUseLim(prefabID); } return value;
			}
			public static float GetFAbilityModAPCost(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbAPCost(prefabID); } return value;
			}
			public static float GetFAbilityModEffHitC(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbEffHitC(prefabID); } return value;
			}
			public static float GetFAbilityModSwFacDur(int prefabID, float value=0){
				for(int i=0; i<faPIDList.Count; i++){ value+=GetPerkFromID(faPIDList[i]).GetModAbSwFacDur(prefabID); } return value;
			}
			#endregion //unit FactionAbility modifier
			
			#region FactionAbility multiplier
			public static float GetFAbilityMulAttack(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAttack(prefabID); } return value;
			}
			public static float GetFAbilityMulHit(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulHit(prefabID); } return value;
			}
			public static float GetFAbilityMulDmgHPMin(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulDmgHPMin(prefabID); } return value;
			}
			public static float GetFAbilityMulDmgHPMax(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulDmgHPMax(prefabID); } return value;
			}
			public static float GetFAbilityMulDmgAPMin(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulDmgAPMin(prefabID); } return value;
			}
			public static float GetFAbilityMulDmgAPMax(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulDmgAPMax(prefabID); } return value;
			}
			public static float GetFAbilityMulCritC(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulCritC(prefabID); } return value;
			}
			public static float GetFAbilityMulCritM(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulCritM(prefabID); } return value;
			}
			
			public static float GetFAbilityMulRange(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulARange(prefabID); } return value;
			}
			public static float GetFAbilityMulAOE(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbAOE(prefabID); } return value;
			}
			
			public static float GetFAbilityMulDur(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbDur(prefabID); } return value;
			}
			public static float GetFAbilityMulCD(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbCD(prefabID); } return value;
			}
			public static float GetFAbilityMulUseLim(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbUseLim(prefabID); } return value;
			}
			public static float GetFAbilityMulAPCost(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbAPCost(prefabID); } return value;
			}
			public static float GetFAbilityMulEffHitC(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbEffHitC(prefabID); } return value;
			}
			public static float GetFAbilityMulSwFacDur(int prefabID, float value=1){
				for(int i=0; i<faPIDList.Count; i++){ value*=GetPerkFromID(faPIDList[i]).GetMulAbSwFacDur(prefabID); } return value;
			}
			#endregion //FactionAbility multiplier
			
			public static List<int> ModifyFAbilityEffectList(int prefabID, List<int> list){
				for(int i=0; i<faPIDList.Count; i++){ list=GetPerkFromID(faPIDList[i]).ModifyEffectList(prefabID, list); } return list;
			}
			
		#endregion	//faction ability
	
		
		
		#region effect
		public static List<int> ePIDList=new List<int>();	//prefabID of all unit related perk that has been unlocked
		public static void AddEffectPerkID(int ID){ ePIDList.Add(ID); } 
		
			#region Effect modifer
			public static float GetEffModHP(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModHP(prefabID); } return value;
			}
			public static float GetEffModAP(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModAP(prefabID); } return value;
			}
			public static float GetEffModAttack(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModAttack(prefabID); } return value;
			}
			public static float GetEffModDefense(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDefense(prefabID); } return value;
			}
			public static float GetEffModHit(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModHit(prefabID); } return value;
			}
			public static float GetEffModDodge(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDodge(prefabID); } return value;
			}
			public static float GetEffModDmgHPMin(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDmgHPMin(prefabID); } return value;
			}
			public static float GetEffModDmgHPMax(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDmgHPMax(prefabID); } return value;
			}
			public static float GetEffModDmgAPMin(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDmgAPMin(prefabID); } return value;
			}
			public static float GetEffModDmgAPMax(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModDmgAPMax(prefabID); } return value;
			}
			public static float GetEffModCritC(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCritC(prefabID); } return value;
			}
			public static float GetEffModCritR(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCritR(prefabID); } return value;
			}
			public static float GetEffModCritM(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCritM(prefabID); } return value;
			}
			
			public static float GetEffModCDmgMul(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCDmgMul(prefabID); } return value;
			}
			public static float GetEffModCHitPen(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCHitPen(prefabID); } return value;
			}
			public static float GetEffModCCritPen(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCCritPen(prefabID); } return value;
			}
			public static float GetEffModODmgMul(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModODmgMul(prefabID); } return value;
			}
			public static float GetEffModOHitPen(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModOHitPen(prefabID); } return value;
			}
			public static float GetEffModOCritPen(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModOCritPen(prefabID); } return value;
			}
			
			public static float GetEffModARange(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModARange(prefabID); } return value;
			}
			public static float GetEffModMRange(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModMRange(prefabID); } return value;
			}
			public static float GetEffModTPrioity(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModTPrioity(prefabID); } return value;
			}
			public static float GetEffModSight(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModSight(prefabID); } return value;
			}
			public static float GetEffModMoveLim(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModMoveLim(prefabID); } return value;
			}
			public static float GetEffModAttackLim(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModAttackLim(prefabID); } return value;
			}
			public static float GetEffModCounterLim(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModCounterLim(prefabID); } return value;
			}
			public static float GetEffModAbilityLim(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModAbilityLim(prefabID); } return value;
			}
			
			public static float GetEffModEffDuration(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModEffDuration(prefabID); } return value;
			}
			public static float GetEffModEffHP(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModEffHP(prefabID); } return value;
			}
			public static float GetEffModEffAP(int prefabID, float value=0){
				for(int i=0; i<ePIDList.Count; i++){ value+=GetPerkFromID(ePIDList[i]).GetModEffAP(prefabID); } return value;
			}
			#endregion //effect modifer
		
			#region Effect multiplier
			public static float GetEffMulHP(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulHP(prefabID); } return value;
			}
			public static float GetEffMulAP(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulAP(prefabID); } return value;
			}
			public static float GetEffMulAttack(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulAttack(prefabID); } return value;
			}
			public static float GetEffMulDefense(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDefense(prefabID); } return value;
			}
			public static float GetEffMulHit(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulHit(prefabID); } return value;
			}
			public static float GetEffMulDodge(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDodge(prefabID); } return value;
			}
			public static float GetEffMulDmgHPMin(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDmgHPMin(prefabID); } return value;
			}
			public static float GetEffMulDmgHPMax(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDmgHPMax(prefabID); } return value;
			}
			public static float GetEffMulDmgAPMin(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDmgAPMin(prefabID); } return value;
			}
			public static float GetEffMulDmgAPMax(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulDmgAPMax(prefabID); } return value;
			}
			public static float GetEffMulCritC(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCritC(prefabID); } return value;
			}
			public static float GetEffMulCritR(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCritR(prefabID); } return value;
			}
			public static float GetEffMulCritM(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCritM(prefabID); } return value;
			}
			
			public static float GetEffMulCDmgMul(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCDmgMul(prefabID); } return value;
			}
			public static float GetEffMulCHitPen(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCHitPen(prefabID); } return value;
			}
			public static float GetEffMulCCritPen(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCCritPen(prefabID); } return value;
			}
			public static float GetEffMulODmgMul(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulODmgMul(prefabID); } return value;
			}
			public static float GetEffMulOHitPen(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulOHitPen(prefabID); } return value;
			}
			public static float GetEffMulOCritPen(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulOCritPen(prefabID); } return value;
			}
			
			public static float GetEffMulARange(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulARange(prefabID); } return value;
			}
			public static float GetEffMulMRange(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulMRange(prefabID); } return value;
			}
			public static float GetEffMulTPrioity(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulTPrioity(prefabID); } return value;
			}
			public static float GetEffMulSight(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulSight(prefabID); } return value;
			}
			public static float GetEffMulMoveLim(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulMoveLim(prefabID); } return value;
			}
			public static float GetEffMulAttackLim(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulAttackLim(prefabID); } return value;
			}
			public static float GetEffMulCounterLim(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulCounterLim(prefabID); } return value;
			}
			public static float GetEffMulAbilityLim(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulAbilityLim(prefabID); } return value;
			}
			
			public static float GetEffMulEffDuration(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulEffDuration(prefabID); } return value;
			}
			public static float GetEffMulEffHP(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetModEffHP(prefabID); } return value;
			}
			public static float GetEffMulEffAP(int prefabID, float value=1){
				for(int i=0; i<ePIDList.Count; i++){ value*=GetPerkFromID(ePIDList[i]).GetMulEffAP(prefabID); } return value;
			}
			#endregion //effect multiplier
		
		#endregion //effect
	}

}