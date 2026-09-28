using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{
	
	public class AI : MonoBehaviour {
		
		#if UNITY_EDITOR
		public static bool inspector=false;
		#endif
		
		
		public enum _AIBehaviour { passive, aggressive, }// evasive }
		
		
		[Tooltip("Check to override individual unit behaviour setting")]
		public bool overrideUnitSetting=false;
		
		public _AIBehaviour aiBehaviour=_AIBehaviour.aggressive;
		public bool requireTrigger=true;	//when true, unit starts in passive state, then switch to aggressive or evasive (doesnt apply for passive)
		
		public static bool IsPassive(Unit unit){
			if(!instance.overrideUnitSetting) return unit.IsPassive();
			else return instance.aiBehaviour==AI._AIBehaviour.passive;
		}
		public static bool IsAggressive(Unit unit){ 
			if(!instance.overrideUnitSetting) return unit.IsAggressive();
			else return (instance.aiBehaviour==AI._AIBehaviour.aggressive && !instance.requireTrigger) || unit.triggered;
		}
		
		//public static bool IsPassive(Unit unit){
			//if(!instance.overrideUnitSetting) return unit.IsPassive();
			//else return instance.aiBehaviour==AI._AIBehaviour.passive || !IsAggressive(unit);
		//}
		//public static bool IsAggressive(Unit unit){ 
			//if(!instance.overrideUnitSetting) return unit.IsPassive();
			//else return instance.aiBehaviour==AI._AIBehaviour.aggressive && (unit.triggered || !instance.requireTrigger);
		//}
		
		
		[Tooltip("Delay in second between each units when they take their turn")]
		public float delayBetweenUnit=0.25f;
		
		[Space(10)] [Tooltip("When checked, AI will always use the best option available")]
		public bool alwaysUseBestOption=true;
		
		[Space(10)]
		[Tooltip("How much the damage for a potential attack is going to be weight into making the AI decision ")]
		public float damageMultiplier=1;
		[Tooltip("How much the hit chance for a potential attack is going to be weight into making the AI decision ")]
		public float hitChanceMultiplier=1;
		[Tooltip("How much the critical chance for a potential attack is going to be weight into making the AI decision ")]
		public float critChanceMultiplier=1;
		[Tooltip("How much of giving chase on out of range target is going to be weight into making the AI decision\nOnly applies there are no target within range")]
		public float pursueMultiplier=1;
		[Tooltip("How much cover is going to be weight into making the AI decision\nOnly applies when cover system is enabled ")]
		public float coverMultiplier=1;
		
		public static float dmgMul(){ return instance.damageMultiplier; }
		public static float hitMul(){ return instance.hitChanceMultiplier; }
		public static float critMul(){ return instance.critChanceMultiplier; }
		public static float pursueMul(){ return instance.pursueMultiplier; }
		public static float coverMul(){ return instance.coverMultiplier; }
		
		
		
		public static AI instance;
		public static void Init(){
			if(instance==null) instance=(AI)FindObjectOfType(typeof(AI));
		}
		
		void Awake(){ instance=this; actionInProgress=false; }
		
		
		[Space(10)] private static bool actionInProgress=false;
		public static bool ActionInProgress(){ return actionInProgress; }
		
		
		public static void MoveUnit(Unit unit){ instance.StartCoroutine(_MoveUnit(unit)); }
		public static IEnumerator _MoveUnit(Unit unit){
			actionInProgress=true;
			
			TBTK.OnGameMessage("- AI's Turn -");
			TBTK.OnSelectUnit(unit);
			
			yield return instance.StartCoroutine(AIRoutineUnit(unit));
			
			if(unit==null || unit.hp<=0) yield return new WaitForSeconds(0.25f);
			if(instance.delayBetweenUnit>0) yield return new WaitForSeconds(instance.delayBetweenUnit);
			
			actionInProgress=false;
			
			GameControl.EndTurn();
		}
		
		public static void MoveFaction(Faction faction){ instance.StartCoroutine(_MoveFaction(faction)); }
		public static IEnumerator _MoveFaction(Faction faction){
			actionInProgress=true;
			
			TBTK.OnGameMessage("- AI's Turn -");
			
			List<Unit> unitList=new List<Unit>( faction.unitList );
			
			if(TurnControl.EnableUnitLimit() || Rand.value()<0.4f){
				List<Unit> newList=new List<Unit>();
				while(unitList.Count>0){
					int rand=Rand.Range(0, unitList.Count);
					newList.Add(unitList[rand]);
					unitList.RemoveAt(rand);
				}
				unitList=newList;
			}
			
			if(unitList.Count>TurnControl.GetUnitLimit()){
				while(unitList.Count>TurnControl.GetUnitLimit()) unitList.RemoveAt(unitList.Count-1);
			}
			
			for(int i=0; i<unitList.Count; i++){
				if(unitList[i]==null) continue;
				
				Unit unit=unitList[i];					TBTK.OnSelectUnit(unit);
				yield return instance.StartCoroutine(AIRoutineUnit(unit));
				
				if(unit==null || (unit.hp<=0 && unit.IsVisible())){ yield return new WaitForSeconds(0.25f); }
				
				if(unit!=null && instance.delayBetweenUnit>0 && unit.IsVisible()) yield return new WaitForSeconds(instance.delayBetweenUnit);
			}
			
			actionInProgress=false;
			
			GameControl.EndTurn();
		}
		
		public static IEnumerator AIRoutineUnit(Unit unit){
			if(unit.IsStunned()) yield break;
			
			int safetyCounter=0;
			while(unit.CanMove() || unit.CanAttack()){
				AIAction action=AnalyseAction(unit);
				
				if(action==null){
					//Debug.Log("No valid action for unit - "+unit.gameObject);
					yield break;
				}
				
				if(action.tgtNode==unit.node && action.tgtUnit==null){
					safetyCounter+=1;
					if(safetyCounter>3) yield break;
					else continue;
				}
				
				if(action.tgtNode!=unit.node){
					yield return instance.StartCoroutine(unit.MoveRoutine(action.tgtNode));
				}
				
				if(unit.hp<=0) yield break;
				
				if(unit!=null && action.tgtUnit!=null){
					yield return instance.StartCoroutine(unit.AttackRoutine(action.tgtUnit.node));
				}
				
				yield return null;
			}
			
			yield return null;
		}
		
		public static AIAction AnalyseAction(Unit unit){
			//Debug.Log("AnalyseAction "+unit.gameObject);
			
			if(IsPassive(unit) && Rand.value()<0.7f) return new AIAction(unit.node);
			
			List<AIAction> actionList=new List<AIAction>();
			
			//List<Unit> hostileList=new List<Unit>();
			bool nearestHostileScanned=false;
			Node nearestHostileNode=null;	float nearestHostileDist=Mathf.Infinity;
			//Node furthestHostileNode=null;	float furthestHostileDist=0;
			float maxNearestDistToHostile=0;	float minNearestDistToHostile=Mathf.Infinity;
			bool hasTargetWithinRange=false;
			
			float unitDamage=-99;
			
			List<Node> walkableList=unit.CanMove() ? GridManager.SetupWalkableList(unit) : null;
			if(walkableList==null) walkableList=new List<Node>();
			walkableList.Insert(0, unit.node);
			
			List<Unit> allhostileList=null;
			if(GameControl.EnableCoverSystem()) allhostileList=UnitManager.GetAllHostileUnits(unit.GetFacID());
			
			
			for(int i=0; i<walkableList.Count; i++){
				List<Node> attackNodeList=GridManager.GetAttackableList(unit, walkableList[i]);
				
				Vector2 cover=CheckCover(walkableList[i], unit, allhostileList);
				float coverScore=100 * (cover[0]!=0 ? cover[0] : cover[1]) * 0.5f ; 	//bcz full cover value is 2
				
				if(attackNodeList.Count>0){
					if(unitDamage<=-99) unitDamage=(unit.GetDmgHPMin() + unit.GetDmgHPMax()) * 0.5f;
					
					for(int n=0; n<attackNodeList.Count; n++){
						AIAction action=new AIAction(walkableList[i], attackNodeList[n].unit);
						
						Attack attack=new Attack(unit, attackNodeList[n].unit, walkableList[i], false, false);
						action.score+=(0.5f*(attack.damageHPMin+attack.damageHPMax))/unitDamage*100*instance.damageMultiplier;
						action.score+=attack.hitChance*100f*instance.hitChanceMultiplier;
						action.score+=attack.critChance*100f*instance.critChanceMultiplier;
						action.score+=coverScore;
						
						//~ float score1=(0.5f*(attack.damageHPMin+attack.damageHPMax))/unitDamage*100*instance.damageMultiplier;
						//~ float score2=attack.hitChance*100f*instance.hitChanceMultiplier;
						//~ float score3=attack.critChance*100f*instance.critChanceMultiplier;
						//~ float score4=coverScore;
						//~ float ccover=Attack.GetCover(walkableList[i], attackNodeList[n]);
						
						//~ Debug.Log((0.5f*(attack.damageHPMin+attack.damageHPMax))+"   "+unitDamage+"   "+attack.cover+"    "+score1+"   "+score2+"   "+score3+"   "+score4+"   ");
						
						//Debug.Log(coverScore+"   "+attack.cover+"   "+attack.hitChance+"    "+attack.critChance+"   "+action.score);
						//Debug.DrawLine(walkableList[i].GetPos(), walkableList[i].GetPos()+new Vector3(0, 1, 0)*(action.score*0.01f), Color.white, 2);
						
						actionList.Add(action);
					}
					
					hasTargetWithinRange=true;
				}
				else{
					
					AIAction action=new AIAction(walkableList[i], coverScore);
					
					if(IsPassive(unit)){
						action.score+=Mathf.Min(0, 3-GridManager.GetDistance(walkableList[i], unit.node));
					}
					else if(IsAggressive(unit)){
						//Node nearestHostileNode=null;	float nearestHostileDist=0;
						
						if(!nearestHostileScanned){
							nearestHostileScanned=true;
							
							if(allhostileList==null) allhostileList=UnitManager.GetAllHostileUnits(unit.GetFacID());
							
							if(allhostileList.Count>0){
								int nearestIdx=0;	//float nearest=Mathf.Infinity;
								//int furthestIdx=0;	//float furthest=0;
								for(int n=0; n<allhostileList.Count; n++){
									float dist=GridManager.GetDistance(unit.node, allhostileList[n].node);
									if(dist<nearestHostileDist){ nearestHostileDist=dist; nearestIdx=n; }
									//if(dist>furthest){ furthestHostileDist=dist; furthestIdx=n; }
								}
								nearestHostileNode=allhostileList[nearestIdx].node;
								//furthestHostileNode=hostileList[furthestIdx].node;
							}
						}
						
						//if(nearestHostileNode==null){
							//List<Unit> hostileList=UnitManager.GetAllHostileUnits(unit.GetFacID());
							//~ int nearestIdx=0;	float nearest=Mathf.Infinity;
							//~ int furthestIdx=0;	float furthest=0;
							//~ for(int n=0; n<hostileList.Count; n++){
								//~ float dist=GridManager.GetDistance(unit.node, hostileList[n].node);
								//~ if(dist<nearest){ nearest=dist; nearestIdx=n; }
								//~ if(dist>furthest){ furthest=dist; furthestIdx=n; }
							//~ }
							//nearestHostileNode=hostileList[nearestIdx].node;
							//nearestHostileDist=nearest;
						//}
							
						if(nearestHostileNode!=null){
							action.scoreAlt=GridManager.GetDistance(walkableList[i], nearestHostileNode);
							//float nearestDistToHostile=GridManager.GetDistance(walkableList[i], nearestHostileNode);
							//action.scoreAlt=Mathf.Max(0, nearestHostileDist-nearestDistToHostile);// * instance.pursueMultiplier;
							//action.scoreAlt*=Rand.Range(0.75f, 1.25f);
							
							//Debug.Log(nearestHostileDist+"   "+nearestDistToHostile+"   "+action.scoreAlt+"   "+action.score);
							//Debug.Log(action.scoreAlt+"   "+maxNearestDistToHostile);
							
							if(action.scoreAlt<minNearestDistToHostile) minNearestDistToHostile=action.scoreAlt;
							if(action.scoreAlt>maxNearestDistToHostile) maxNearestDistToHostile=action.scoreAlt;
							//if(action.scoreAlt>maxNearestDistToHostile) maxNearestDistToHostile=nearestDistToHostile;
						}
						
							//~ if(nearest==furthest) action.score+=1;
							//~ else action.score+=Mathf.Abs(nearest-furthest)/(furthest-nearest) * instance.pursueMultiplier;
							
						//float distToNearestHostile=GridManager.GetDistance(walkableList[i], nearestHostileNode);
						//
						//action.score+=Mathf.Max(0, nearestHostileDist-distToNearestHostile)*instance.pursueMultiplier;
					}
				
					actionList.Add(action);
				}
			}
			
			if(IsAggressive(unit)){
				for(int i=0; i<actionList.Count; i++){
					if(!actionList[i].CachedScore()) continue;
					
					float range=maxNearestDistToHostile-minNearestDistToHostile;
					actionList[i].scoreAlt=1-(actionList[i].scoreAlt-minNearestDistToHostile)/range;
					
					//Debug.Log(i+" alt score - "+actionList[i].scoreAlt+"   "+actionList[i].score);
					//actionList[i].scoreAlt=(maxNearestDistToHostile-actionList[i].scoreAlt);///maxNearestDistToHostile * instance.pursueMultiplier;
					//Debug.Log("             - "+actionList[i].scoreAlt+"   "+maxNearestDistToHostile);
					//~ actionList[i].score+=(100*actionList[i].scoreAlt/maxNearestDistToHostile) * instance.pursueMultiplier;
					
					actionList[i].score+=100 * actionList[i].scoreAlt * instance.pursueMultiplier;
				}
			}
			
			//no target in range but unit has attacked
			//unit has either just destroyed a unit within range or use up or it's attack option
			if(!hasTargetWithinRange && unit.attackThisTurn<=0){	
				if(!unit.CanMove()) return null;
				
				if(IsAggressive(unit) && !GameControl.EnableCoverSystem()){
					//List<Node> path=AStar.SearchWalkableNode(unit.node, nearestHostileNode, unit.canMovePastUnit, unit.canMovePastObs, true);
					List<Node> path=AStar.SearchWalkableNode(unit.node, nearestHostileNode, AStar.BypassUnitCode(unit), unit.canMovePastObs, true);
					if(path.Count>0){
						int idx=Mathf.Clamp(unit.GetMoveRange()-1, 0, path.Count-1);
						AIAction action=new AIAction(path[idx], 0);
						return action;
					}
				}
			}
			
			if(actionList.Count==0) return null;
			
			//sorting the list according to the score
			List<AIAction> newList=new List<AIAction>();
			while(actionList.Count>0){
				int highestIdx=-1;		float highest=-Mathf.Infinity;
				for(int i=0; i<actionList.Count; i++){
					if(actionList[i].score>highest){ highest=actionList[i].score;	highestIdx=i; }
				}
				
				if(highestIdx<0) break;
				
				if(instance.alwaysUseBestOption) return actionList[highestIdx];
				
				newList.Add(actionList[highestIdx]);
				actionList.RemoveAt(highestIdx);
			}
			
			actionList=newList;
			
			if(actionList.Count==0) return null;
			
			if(actionList[0].score>0){
				for(int i=1; i<actionList.Count; i++){
					if(actionList[i].score>0) continue;
					actionList.RemoveAt(i);		i-=1;
				}
			}
			
			if(actionList.Count==0) return null;
			
			int rand=Rand.GetOption(new List<float>{ 1.5f, 0.25f, 0.125f, 0.075f });
			rand=Mathf.Min(rand, actionList.Count-1);
			
			return actionList[rand];
		}
		/*
		public static AIAction __AnalyseAction(Unit unit){
			List<AIAction> actionList=new List<AIAction>();
			
			List<Node> walkableList=GridManager.SetupWalkableList(unit);
			walkableList.Insert(0, unit.node);
			
			for(int i=0; i<walkableList.Count; i++){
				List<Node> attackNodeList=GridManager.GetAttackableList(unit, walkableList[i]);
				//
				
				float coverScore=100 * CheckCover(walkableList[i], unit) * instance.coverMultiplier;
				
				if(attackNodeList.Count>0){
					for(int n=0; n<attackNodeList.Count; n++){
						
						AIAction action=new AIAction(walkableList[i], attackNodeList[n].unit);
						
						Attack attack=new Attack(unit, attackNodeList[n].unit, false, false);
						action.score+=0.5f*(attack.damageHPMin+attack.damageHPMax)*instance.damageMultiplier;
						action.score+=attack.hitChance*100*instance.hitChanceMultiplier;
						action.score+=attack.critChance*100*instance.critChanceMultiplier;
						action.score+=coverScore;
						
						actionList.Add(action);
					}
				}
				else{
					
				}
			}
			
			if(actionList.Count==0){
				if(unit.IsPassive()){
					if(Rand.value()<0.7f) return new AIAction(unit.node);
					else{
						for(int i=1; i<walkableList.Count; i++){
							float coverScore=100 * CheckCover(walkableList[i], unit) * instance.coverMultiplier;
							float dist=GridManager.GetDistance(unit.node, walkableList[i]);
							if(dist==1) actionList.Add(new AIAction(walkableList[i], coverScore));
							else if(dist==2 && Rand.value()<0.3f) actionList.Add(new AIAction(walkableList[i], coverScore));
						}
						
						//if unit is not triggered or cover system is not active,
						//just move randomly, otherwise let the sorting algorithm at the end of the function to choose the node with higeste cover
						if(actionList.Count>0 && (!unit.triggered || !GameControl.EnableCoverSystem())) 
							return actionList[Rand.Range(0, actionList.Count)];
					}
				}
				else if(unit.IsAggressive()){
					float furthestDist=0;	
					List<Unit> hostileList=UnitManager.GetAllHostileUnits(unit.facID);
					
					for(int i=0; i<walkableList.Count; i++){
						float nearest=Mathf.Infinity;
						for(int n=0; n<hostileList.Count; n++){
							float dist=GridManager.GetDistance(walkableList[i], hostileList[n].node);
							if(dist>furthestDist) furthestDist=dist;
							if(dist<nearest) nearest=dist;
						}
						
						actionList.Add(new AIAction(walkableList[i], nearest, 100*CheckCover(walkableList[i], unit)*instance.coverMultiplier));
					}
					
					for(int i=0; i<actionList.Count; i++){
						actionList[i].score=instance.pursueMultiplier * (furthestDist-actionList[i].score)/furthestDist;
						actionList[i].score+=actionList[i].scoreAlt;
					}
				}
			}
			
			int highestIdx=-1;		float highest=-1;
			for(int i=0; i<actionList.Count; i++){
				if(actionList[i].score>highest){
					highest=actionList[i].score;	highestIdx=i;
				}
			}
			
			//Debug.Log("AI action: "+actionList.Count+"   "+highestIdx);
			
			if(actionList.Count>0){
				if(highestIdx>=0) return actionList[highestIdx];
				else return actionList[0];
			}
			
			return null;
		}
		*/
		
		public static Vector2 CheckCover(Node node, Unit unit, List<Unit> hostileList){
			if(!GameControl.EnableCoverSystem()) return Vector2.zero;
			
			//consider xcom style side stepping for square grid
			
			int lowestCover=2;	float totalCover=0;
			
			//List<Unit> hostileList=UnitManager.GetAllHostileUnits(unit.GetFacID());
			for(int i=0; i<hostileList.Count; i++){
				//~ if(GridManager.GetDistance(hostileList[i].node, node)>hostileList[i].GetAttackRange()) continue;
				//~ //Vector3 dir=hostileList[i].node.GetPos()-node.GetPos();
				//~ //int cover=node.GetCover(Utility.Vector2ToAngle(new Vector2(dir.x, dir.z)));
				//~ int cover=node.GetCover(GridManager.GetAngle(node, hostileList[i].node, false));
				//~ if(cover<lowestCover) lowestCover=cover;
				
				int cover=Attack.GetCover(hostileList[i].node, node);
				if(cover<lowestCover) lowestCover=cover;
				
				totalCover+=cover;
			}
			
			totalCover=totalCover/(float)hostileList.Count * 0.4f;	
			
			//Color color=lowestCover==0 ? Color.red : Color.white ;
			//Debug.DrawLine(node.GetPos(), node.GetPos()+new Vector3(0, 1, 0)*(totalCover), color, 2);
			
			return new Vector2(lowestCover, totalCover);
			
			//~ Vector3 avgPos=Vector3.zero;
			//~ List<Unit> hostileList=UnitManager.GetAllHostileUnits(unit.facID);
			//~ for(int n=0; n<hostileList.Count; n++) avgPos+=hostileList[n].GetPos();
			//~ avgPos/=hostileList.Count;
			
			//~ Vector3 dir=avgPos-node.GetPos();	
			//~ return node.GetCover(Utility.Vector2ToAngle(new Vector2(dir.x, dir.z)));
		}
		
	}
	
	
	
	public class AIAction{
		public Node tgtNode;
		public Unit tgtUnit;
		public float score=0;
		public float scoreAlt=-999;	//used to temporarily cache value
		
		public AIAction(Node node, Unit tgtU=null){
			tgtNode=node;	tgtUnit=tgtU;
		}
		public AIAction(Node node, float ss, float ssAlt=0){
			tgtNode=node;	score=ss;	scoreAlt=ssAlt;
		}
		
		public bool CachedScore(){ return scoreAlt!=-999; }
	}
	
}