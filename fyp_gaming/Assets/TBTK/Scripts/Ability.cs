using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{
	
	[System.Serializable]
	public class Ability : TBTKItem{
		
		public enum _AbilityType{ Generic, Teleport, SpawnUnit, Charge, Line, Cone, ScanFogOfWar, DeployBlock, None,  }
		
		public enum _TargetType{AllNode, AllUnit, HostileUnit, FriendlyUnit, EmptyNode}
		public enum _ImpactType{None, Negative, Positive}
		
		
		[HideInInspector] public Unit srcUnit;	//runtime attribute
		[HideInInspector] public int facID;		//runtime attribute
		[HideInInspector] public int index;		//runtime attribute
		public void Init(Unit unit, int idx){ srcUnit=unit; facID=srcUnit.facID; index=idx; isUnitAbility=true; }
		public void Init(int fID, int idx){ facID=fID; index=idx; isFacAbility=true; }
		
		[HideInInspector] public bool isUnitAbility=false;
		[HideInInspector] public bool isFacAbility=false;
		
		public _AbilityType type;
		public bool IsLine(){ return type==_AbilityType.Line; }
		//public bool IsGeneric(){ return type==_AbilityType.Generic; }
		//public bool IsTeleport(){ return type==_AbilityType.Teleport; }
		
		public _TargetType targetType;
		public bool requireTarget=true;
		public bool requireLos=true;
		public int rangeMin=0;
		public int range=5;
		public int aoeRange=0;
		public bool useLineAOE=false;	//aoe only applies in the directions of the adjacent neighbours
		
		public int fov=60;
		
		//[HideInInspector]
		public bool TargetStraightLineOnly(){ return type==_AbilityType.Charge | type==_AbilityType.Line; }
		public bool TargetCone(){ return type==_AbilityType.Cone; }
		
		public Unit spawnUnitPrefab;
		public GameObject obstaclePrefab;
		
		public int moveCost;
		public int attackCost;
		public int abilityCost;
		public int apCost;
		public bool endAllActionAfterUse;
		
		public int cooldown=1;
		[HideInInspector] private int currentCD;	//runtime attribute
		
		public int useLimit=0;
		[HideInInspector] private int useCount;	//runtime attribute
		
		public float impactDelay=0f;
		
		
		
		//for generic type
		public _ImpactType impactType;
		
		public bool HasNoImpact(){ return impactType==_ImpactType.None; }
		public bool HasPositiveImpact(){ return impactType==_ImpactType.Positive; }
		public bool HasNegativeImpact(){ return impactType==_ImpactType.Negative; }
		
		public int hpModifierMin=5;
		public int hpModifierMax=5;
		public int GetRandHPModifier(){ return (int)Mathf.Round(Rand.Range(GetHPMin(), GetHPMax())); }
		
		public int apModifierMin=5;
		public int apModifierMax=5;
		public int GetRandAPModifier(){ return (int)Mathf.Round(Rand.Range(GetAPMin(), GetAPMax())); }
		
		//used when impactType=_AbInstantImpact.Negative
		public int damageType=0;					//public bool useDamageTable=false;
		public float attack=1;							//public float GetAttack(){ return attack; }
		public float hitChance=1;						//public float GetHit(){ return hitChance; }
		public float critChance=0;					//public float GetCritChance(){ return critChance; }
		public float critMultiplier=2;					//public float GetCritMultiplier(){ return critMultiplier; }
		public bool factorInTargetStats=false;	
		
		
		public float effHitChance=0f;	//applies for effctIDList,  clearAllEffect, switchFaction
		
		public List<int> effectIDList=new List<int>();
		
		public bool clearAllEffect=false;
		
		//for factionSwitch
		public bool switchFaction=false;
		//public int switchFacDuration=1;	
		public bool switchFacControllable=true;	//can be unit be controlled directly after a faction-switch
		
		//public int revealFogDuration=1;
		
		public int duration=1;	//for switch-faction and reveal-fog
		
		//animation
		public bool useAttackSequence;
		public bool aimAtUnit;
		public ShootObject shootObject;
		
		
		//visual effects
		public VisualObject effectOnUse;
		public VisualObject effectOnHit;
		
		public AudioClip activateSound;
		
		
		public enum _AbilityStatus{
			Ready,
			OnCooldown,
			HitUsedLimit,
			Disabled,
			InsufficientAP,
			HitAbilityPerTurnLimit,
			HitMovePerTurnLimit,
		}
		
		public _AbilityStatus IsAvailable(){
			if(currentCD>0) return _AbilityStatus.OnCooldown;
			if(HasUseLimit() && GetUseRemain()<=0) return _AbilityStatus.HitUsedLimit;
			
			if(isUnitAbility){
				if(srcUnit.AbilityDisabled()) return _AbilityStatus.Disabled;
				if(srcUnit.ap<GetAPCost()) return _AbilityStatus.InsufficientAP;
				if(srcUnit.GetAbilityRemain()<abilityCost) return _AbilityStatus.HitAbilityPerTurnLimit;
				if(srcUnit.GetMoveRemain()<moveCost || srcUnit.GetAttackRemain()<attackCost) return _AbilityStatus.HitMovePerTurnLimit;
			}
			
			//check available target?
			return _AbilityStatus.Ready;
		}
		
		public void IterateCD(){ if(currentCD>0) currentCD-=1; }
		public int GetCurrentCD(){ return currentCD-1; }
		
		public bool HasUseLimit(){ return useLimit>0; }
		public int GetUseRemain(){ return GetUseLimit()-useCount; }
		
		public void Activate(){
			Debug.Log("Ability activated - "+name);
			
			useCount+=1;
			currentCD=GetCooldown();
			
			if(srcUnit!=null){
				if(!endAllActionAfterUse){
					srcUnit.moveThisTurn+=moveCost;	
					srcUnit.attackThisTurn+=attackCost;	
					srcUnit.abilityThisTurn+=abilityCost;
					srcUnit.ap-=GetAPCost();
				}
				else srcUnit.EndAllAction();
				
				effectOnUse.Spawn(srcUnit.GetPos());
			}
			
			Debug.Log("Play activateSound - "+activateSound);
			AudioManager.PlaySound(activateSound);
		}
		
		
		
		public IEnumerator HitTarget(Node node){
			Debug.Log("HitTarget "+type);
			
			if(impactDelay>0) yield return new WaitForSeconds(impactDelay);
			
			Debug.Log("HitTarget "+type+"   "+obstaclePrefab);
			
			if(type==_AbilityType.Generic){
				//Debug.Log(!isUnitAbility +"   "+ !requireTarget);
				if(!isUnitAbility && !requireTarget){	//faction ability that doesn't require target will get all valid target on the grid
					List<Node> nodeList=GridManager.GetTargetNodeForNonTargetingFAbility(this);
					//Debug.Log("  "+nodeList.Count);
					for(int i=0; i<nodeList.Count; i++){
						
						nodeList[i].unit.ApplyAttack(this);
						effectOnHit.Spawn(nodeList[i].GetPos());
					}
				}
				else{
					int aoe=GetAOE();
					if(aoe<=0){
						if(node.unit!=null) node.unit.ApplyAttack(this);
					}
					else{
						List<Node> nodeList=GridManager.GetNodesWithinDistance(node, aoe);
						nodeList.Add(node);
						
						for(int i=0; i<nodeList.Count; i++){
							if(nodeList[i].unit==null) continue; 
							
							if(targetType==Ability._TargetType.AllUnit){
								nodeList[i].unit.ApplyAttack(this);
							}
							else if(targetType==Ability._TargetType.HostileUnit){
								if(nodeList[i].unit.GetFacID()!=facID) nodeList[i].unit.ApplyAttack(this);
							}
							else if(targetType==Ability._TargetType.FriendlyUnit){
								if(nodeList[i].unit.GetFacID()==facID) nodeList[i].unit.ApplyAttack(this);
							}
						}
					}
				}
			}
			else if(type==_AbilityType.Teleport){
				if(srcUnit!=null){
					srcUnit.node.unit=null;
					srcUnit.node=node;
					srcUnit.node.unit=srcUnit;
					srcUnit.GetT().position=node.GetPos();
					
					GridManager.SetupFogOfWar();
					UnitManager.CheckAITrigger(srcUnit);
					if(node.collectible!=null) yield return srcUnit.StartCoroutine(node.collectible.Trigger(srcUnit));
				}
			}
			else if(type==_AbilityType.SpawnUnit){
				if(spawnUnitPrefab!=null){
					GameObject obj=(GameObject)MonoBehaviour.Instantiate(spawnUnitPrefab.gameObject, node.GetPos(), Quaternion.identity);
					Unit unit=obj.GetComponent<Unit>();
					unit.node=node;	node.unit=unit;
					unit.hp=unit.GetFullHP();
					UnitManager.AddUnit(unit, srcUnit!=null ? srcUnit.GetFacID() : facID);
					
					GridManager.SetupFogOfWar();
				}
				else Debug.Log("No unit prefab has been assigned!!");
			}
			else if(type==_AbilityType.Charge){
				if(node.unit!=null){
					float wantedAngle=GridManager.GetAngle(srcUnit.node, node, true);	
					float tgtDist=GridManager.GetDistance(srcUnit.node, node);
					List<Node> neighbours=node.GetNeighbourList(true);
					for(int i=0; i<neighbours.Count; i++){
						float dist=GridManager.GetDistance(srcUnit.node, neighbours[i]);
						float angle=Mathf.Abs(wantedAngle-GridManager.GetAngle(srcUnit.node, neighbours[i], true));
						if(dist>=tgtDist || angle>1) continue;
						yield return CRoutine.Get().StartCoroutine(srcUnit.MoveRoutine(neighbours[i], 3));
						break;
					}
				}
				
				int aoe=GetAOE();
				if(aoe<=0){
					if(node.unit!=null) node.unit.ApplyAttack(this);
				}
				else{
					List<Node> nodeList=GridManager.GetNodesWithinDistance(node, aoe);
					for(int i=0; i<nodeList.Count; i++){
						if(nodeList[i].unit!=null && !OnSameFac(srcUnit, nodeList[i].unit)) nodeList[i].unit.ApplyAttack(this);
					}
				}
			}
			else if(type==_AbilityType.Line || type==_AbilityType.Cone){
				List<Node> nodeList=new List<Node>();
				if(type==_AbilityType.Line) nodeList=GridManager.GetNodesInALine(srcUnit.node, GridManager.GetAngle(node, srcUnit.node, true), GetRange(), GetRangeMin());
				if(type==_AbilityType.Cone) nodeList=GridManager.GetNodesInACone(srcUnit.node, node, GetRange(), GetRangeMin(), fov);
				
				for(int i=0; i<nodeList.Count; i++){
					if(nodeList[i].unit==null) continue;
					
					if(targetType==_TargetType.HostileUnit){
						if(!OnSameFac(srcUnit, nodeList[i].unit)) nodeList[i].unit.ApplyAttack(this);
					}
					else if(targetType==_TargetType.FriendlyUnit){
						if(OnSameFac(srcUnit, nodeList[i].unit)) nodeList[i].unit.ApplyAttack(this);
					}
					else nodeList[i].unit.ApplyAttack(this);
				}
			}
			else if(type==_AbilityType.ScanFogOfWar){
				if(GameControl.EnableFogOfWar()){
					List<Node> nodeList=GridManager.GetNodesWithinDistance(node, GetAOE());
					for(int i=0; i<nodeList.Count; i++) nodeList[i].RevealFogOfWar(GetDuration());
				}
			}
			else if(type==_AbilityType.DeployBlock){
				if(obstaclePrefab!=null){
					node.obstacleT=(Transform)MonoBehaviour.Instantiate(obstaclePrefab.transform, node.GetPos(), Quaternion.identity);
					
					List<Node> neighbours=node.GetNeighbourList();
					for(int i=0; i<neighbours.Count; i++){
						if(!neighbours[i].walkable || neighbours[i].HasObstacle()) continue;
						neighbours[i].InitCover();
					}
					
					GridManager.SetupFogOfWar();
				}
				else Debug.Log("No obstacle prefab has been assigned!!");
			}
			
			if(node!=null) effectOnHit.Spawn(node.GetPos());
		}

		public static bool OnSameFac(Unit unit1, Unit unit2){ return unit1.GetFacID()==unit2.GetFacID(); }
		
		
		public bool IsUAB(){ return isUnitAbility; }
		
		
		public List<int> GetRuntimeEffectIDList(){
			if(IsUAB())	return PerkManager.ModifyUAbilityEffectList(prefabID, new List<int>( effectIDList ));
			else			return PerkManager.ModifyFAbilityEffectList(prefabID, new List<int>( effectIDList ));
		}
		
		public int GetAPCost(){ 
			if(IsUAB())	return (int)(apCost * PerkManager.GetUAbilityMulAPCost(prefabID) + PerkManager.GetUAbilityModAPCost(prefabID));
			else 			return (int)(apCost * PerkManager.GetFAbilityMulAPCost(prefabID) + PerkManager.GetFAbilityModAPCost(prefabID));
		}
		public int GetDuration(){ 
			if(IsUAB()) return (int)(duration * PerkManager.GetUAbilityMulDur(prefabID) + PerkManager.GetUAbilityModDur(prefabID));
			else 			return(int)( duration * PerkManager.GetFAbilityMulDur(prefabID) + PerkManager.GetFAbilityModDur(prefabID));
		}
		public int GetCooldown(){ 
			if(IsUAB()) return (int)(cooldown * PerkManager.GetUAbilityMulCD(prefabID) + PerkManager.GetUAbilityModCD(prefabID));
			else 			return(int)( cooldown * PerkManager.GetFAbilityMulCD(prefabID) + PerkManager.GetFAbilityModCD(prefabID));
		}
		public int GetUseLimit(){ 
			if(IsUAB()) return (int)(useLimit * PerkManager.GetUAbilityMulUseLim(prefabID) + PerkManager.GetUAbilityModUseLim(prefabID));
			else 			return (int)(useLimit * PerkManager.GetFAbilityMulUseLim(prefabID) + PerkManager.GetFAbilityModUseLim(prefabID));
		}
		
		
		public float GetAttack(){
			if(IsUAB()) return attack * PerkManager.GetUAbilityMulAttack(prefabID) + PerkManager.GetUAbilityModAttack(prefabID);
			else 			return attack * PerkManager.GetFAbilityMulAttack(prefabID) + PerkManager.GetFAbilityModAttack(prefabID);
		}
		public float GetHit(){
			if(IsUAB()) return hitChance * PerkManager.GetUAbilityMulHit(prefabID) + PerkManager.GetUAbilityModHit(prefabID);
			else 			return hitChance * PerkManager.GetFAbilityMulHit(prefabID) + PerkManager.GetFAbilityModHit(prefabID);
		}
		public float GetHPMin(){
			if(IsUAB()) return hpModifierMin * PerkManager.GetUAbilityMulDmgHPMin(prefabID) + PerkManager.GetUAbilityModDmgHPMin(prefabID);
			else 			return hpModifierMin * PerkManager.GetFAbilityMulDmgHPMin(prefabID) + PerkManager.GetFAbilityModDmgHPMin(prefabID);
		}
		public float GetHPMax(){
			if(IsUAB()) return hpModifierMax * PerkManager.GetUAbilityMulDmgHPMax(prefabID) + PerkManager.GetUAbilityModDmgHPMax(prefabID);
			else 			return hpModifierMax * PerkManager.GetFAbilityMulDmgHPMax(prefabID) + PerkManager.GetFAbilityModDmgHPMax(prefabID);
		}
		public float GetAPMin(){
			if(IsUAB()) return apModifierMin * PerkManager.GetUAbilityMulDmgAPMin(prefabID) + PerkManager.GetUAbilityModDmgAPMin(prefabID);
			else 			return apModifierMin * PerkManager.GetFAbilityMulDmgAPMin(prefabID) + PerkManager.GetFAbilityModDmgAPMin(prefabID);
		}
		public float GetAPMax(){
			if(IsUAB()) return apModifierMax * PerkManager.GetUAbilityMulDmgAPMax(prefabID) + PerkManager.GetUAbilityModDmgAPMax(prefabID);
			else 			return apModifierMax * PerkManager.GetFAbilityMulDmgAPMax(prefabID) + PerkManager.GetFAbilityModDmgAPMax(prefabID);
		}
		public float GetCritChance(){
			if(IsUAB()) return critChance * PerkManager.GetUAbilityMulCritC(prefabID) + PerkManager.GetUAbilityModCritC(prefabID);
			else 			return critChance * PerkManager.GetFAbilityMulCritC(prefabID) + PerkManager.GetFAbilityModCritC(prefabID);
		}
		public float GetCritMultiplier(){
			if(IsUAB()) return critMultiplier * PerkManager.GetUAbilityMulCritM(prefabID) + PerkManager.GetUAbilityModCritM(prefabID);
			else 			return critMultiplier * PerkManager.GetFAbilityMulCritM(prefabID) + PerkManager.GetFAbilityModCritM(prefabID);
		}
		
		public int GetRangeMin(){
			if(IsUAB()) return (int)(rangeMin * PerkManager.GetUAbilityMulRange(prefabID) + PerkManager.GetUAbilityModRange(prefabID));
			else 			return rangeMin;
		}
		public int GetRange(){
			if(IsUAB()) return (int)(range * PerkManager.GetUAbilityMulRange(prefabID) + PerkManager.GetUAbilityModRange(prefabID));
			else 			return range;
		}
		public int GetAOE(){
			if(IsUAB()) return (int)(aoeRange * PerkManager.GetUAbilityMulAOE(prefabID) + PerkManager.GetUAbilityModAOE(prefabID));
			else 			return (int)(aoeRange * PerkManager.GetFAbilityMulAOE(prefabID) + PerkManager.GetFAbilityModAOE(prefabID));
		}
		
		public float GetEffHitChance(){
			if(IsUAB()) return (int)(effHitChance * PerkManager.GetUAbilityMulEffHitC(prefabID) + PerkManager.GetUAbilityModEffHitC(prefabID));
			else 			return (int)(effHitChance * PerkManager.GetFAbilityMulEffHitC(prefabID) + PerkManager.GetFAbilityModEffHitC(prefabID));
		}
		
		
		
		public Ability Clone(){
			Ability clone=new Ability();
			
			base.Clone(this, clone);
			
			clone.type=type;
			
			clone.targetType=targetType;
			clone.requireTarget=requireTarget;		clone.requireLos=requireLos;
			clone.rangeMin=rangeMin;					clone.range=range;							clone.aoeRange=aoeRange;		
			clone.fov=fov;
			
			
			clone.spawnUnitPrefab=spawnUnitPrefab;
			clone.obstaclePrefab=obstaclePrefab;
			
			clone.moveCost=moveCost;				clone.attackCost=attackCost;			clone.abilityCost=abilityCost;
			clone.apCost=apCost;						clone.endAllActionAfterUse=endAllActionAfterUse;
			
			clone.duration=duration;					clone.cooldown=cooldown;				clone.useLimit=useLimit;
			clone.impactDelay=impactDelay;
			
			clone.impactType=impactType;
			clone.hpModifierMin=hpModifierMin;		clone.hpModifierMax=hpModifierMax;
			clone.apModifierMin=apModifierMin;		clone.apModifierMax=apModifierMax;
			
			clone.damageType=damageType;
			clone.attack=attack;						clone.hitChance=hitChance;
			clone.critChance=critChance;			clone.critMultiplier=critMultiplier;
			clone.factorInTargetStats=factorInTargetStats;
			
			clone.effHitChance=effHitChance;
			
			clone.effectIDList=new List<int>( effectIDList );
			clone.clearAllEffect=clearAllEffect;
			
			clone.switchFaction=switchFaction;
			//clone.switchFacDuration=switchFacDuration;
			clone.switchFacControllable=switchFacControllable;
			
			clone.useAttackSequence=useAttackSequence;
			clone.aimAtUnit=aimAtUnit;
			clone.shootObject=shootObject;
			
			clone.effectOnUse=effectOnUse!=null ? effectOnUse.Clone() : new VisualObject();
			clone.effectOnHit=effectOnHit!=null ? effectOnHit.Clone() : new VisualObject();
			
			clone.activateSound=activateSound;
			
			return clone;
		}
	}

}