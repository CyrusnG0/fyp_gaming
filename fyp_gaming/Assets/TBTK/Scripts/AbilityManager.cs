using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{
	
	public class AbilityManager : MonoBehaviour {
		
		public static AbilityManager instance;
		public static void Init(){
			if(instance==null) instance=(AbilityManager)FindObjectOfType(typeof(AbilityManager));
			
		}
		
		private bool waitingForTargetU=false;
		private bool waitingForTargetF=false;
		
		public static bool IsWaitingForTarget(){ return IsWaitingForTargetU() | IsWaitingForTargetF() ; }
		public static bool IsWaitingForTargetU(){ return instance.waitingForTargetU ; }
		public static bool IsWaitingForTargetF(){ return instance.waitingForTargetF ; }
		
		public static void WaitingForTargetU(){ instance.waitingForTargetU=true; }
		public static void WaitingForTargetF(){ instance.waitingForTargetF=true; }
		
		public static void ClearWaitingForTarget(){ instance.waitingForTargetU=false; instance.waitingForTargetF=false; }
		
		
		private int curAbilityAOE=0;	//for GridIndicator
		public static int GetCurAbilityAOE(){ return instance.curAbilityAOE; }
		
		private Node curNode;	//for GridIndicator
		public static Node GetCurNode(){ return instance.curNode; }
		private bool curAbilityIsCone;	//for GridIndicator
		public static bool IsCurAbilityCone(){ return instance.curAbilityIsCone; }
		private int curAbilityFOV=0;	//for GridIndicator
		public static int GetCurAbilityFOV(){ return instance.curAbilityFOV; }
		private int curAbilityRange=0;	//for GridIndicator
		private int curAbilityRangeMin=0;	//for GridIndicator
		public static int GetCurAbilityRange(){ return instance.curAbilityRange; }
		public static int GetCurAbilityRangeMin(){ return instance.curAbilityRangeMin; }
		
		
		private Unit currentUnit;	private int unitAbilityIdx=-1;
		//public static int GetSelectedIdx(){ return instance.unitAbilityIdx; }
		
		private Faction currentFac;	private int facAbilityIdx=-1;
		
		public static int GetSelectedIdx(){ 
			if(instance.currentUnit!=null) return instance.unitAbilityIdx;
			if(instance.currentFac!=null) return instance.facAbilityIdx;
			return -1;
		}
		
		
		public static void AbilityTargetModeUnit(Unit unit, Ability ability){	ExitAbilityTargetMode();
			GridManager.SetupAbilityTargetList(unit, ability);
			instance.currentUnit=unit;
			instance.unitAbilityIdx=ability.index;
			
			instance.curAbilityIsCone=false;
			
			if(ability.TargetCone()){
				instance.curAbilityIsCone=true;//ability.TargetCone();
				instance.curAbilityFOV=ability.fov;
				instance.curAbilityRange=ability.GetRange();
				instance.curAbilityRangeMin=ability.GetRangeMin();
				instance.curNode=unit.node;
			}
			else instance.curAbilityAOE=ability.GetAOE();
			
			TBTK.OnAbilityTargeting(ability);
			
			WaitingForTargetU();
		}
		
		public static void AbilityTargetModeFac(Faction fac, Ability ability){	ExitAbilityTargetMode();
			GridManager.SetupAbilityTargetList(fac, ability);
			instance.currentFac=fac;				
			instance.facAbilityIdx=ability.index;
			instance.curAbilityAOE=ability.GetAOE();
			
			instance.curAbilityIsCone=false;
			
			TBTK.OnAbilityTargeting(ability);
			
			WaitingForTargetF();
		}
		
		public static void ExitAbilityTargetMode(bool resetIndicator=true){
			instance.currentUnit=null;		instance.unitAbilityIdx=-1;
			instance.currentFac=null;		instance.facAbilityIdx=-1;
			
			GridManager.ClearAbilityTargetList(resetIndicator);
			ClearWaitingForTarget();
			
			TBTK.OnAbilityTargeting(null);
		}
		
		public static bool AbilityTargetSelected(Node node){ return instance._AbilityTargetSelected(node); }
		public bool _AbilityTargetSelected(Node node){
			if(!curAbilityIsCone && !GridManager.InAbilityTargetList(node)) return false;
			
			if(unitAbilityIdx>=0 && currentUnit!=null){
				currentUnit.UseAbility(unitAbilityIdx, node);
			}
			
			if(facAbilityIdx>=0 && currentFac!=null){
				currentFac.UseAbility(facAbilityIdx, node);
			}
			
			ExitAbilityTargetMode(false);
			
			return true;
		}
	}
	
}