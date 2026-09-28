using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public class Attack{
		
		public Ability ability;
		public Unit srcUnit;
		public Unit tgtUnit;
		
		//private const float overwatchHitPenalty=0.2f;
		//private const float overwatchHitPenalty=0.2f;
		
		public int cover=0;
		//private const float coverHitPenalty=0.2f;
		
		public float hitChance;
		public float critChance;
		
		public bool hit;
		public bool crit;
		
		public float damageHPMin;
		public float damageHPMax;
		
		public float damageAPMin;
		public float damageAPMax;
		
		public float damageHP;
		public float damageAP;
		
		public Attack(Unit sUnit, Unit tUnit, Node srcNode=null, bool isCounter=false, bool isOverwatch=false){
			srcUnit=sUnit;		tgtUnit=tUnit;		
			if(srcNode==null) srcNode=srcUnit.node;
			
			int range=GridManager.GetDistance(srcNode, tgtUnit.node);
			
			float srcUnitHit=0; float srcUnitAttack=0;	float dmgMultiplier=1;
			
			if(srcUnit.hasMeleeAttack && range<=srcUnit.GetAttackRangeMelee()){
				srcUnitHit=srcUnit.GetHitM();
				srcUnitAttack=srcUnit.GetAttackM();
				
				hitChance=srcUnitHit/(srcUnitHit+tgtUnit.GetDodge());
				if(isCounter) hitChance-=srcUnit.GetCHitPenalty();
				if(isOverwatch) hitChance-=srcUnit.GetOHitPenalty();
				hit=Rand.value()<hitChance;
				
				critChance=Mathf.Clamp(srcUnit.GetCritChanceM()-Mathf.Max(0, tgtUnit.GetCritReduc()), 0, 1);
				if(isCounter) critChance-=srcUnit.GetCCritPenalty();
				if(isOverwatch) critChance-=srcUnit.GetOCritPenalty();
				crit=Rand.value()<critChance;
				
				dmgMultiplier=srcUnitAttack/(srcUnitAttack+tgtUnit.GetDefense());
				dmgMultiplier*=DamageTable.GetMultiplier(srcUnit.damageTypeMelee, tgtUnit.armorType);
				
				if(isCounter) dmgMultiplier*=srcUnit.GetCDmgMul();
				if(isOverwatch) dmgMultiplier*=srcUnit.GetODmgMul();
				
				damageHPMin=srcUnit.GetDmgHPMinM()*dmgMultiplier;
				damageHPMax=srcUnit.GetDmgHPMaxM()*dmgMultiplier;
				
				damageAPMin=srcUnit.GetDmgAPMinM()*dmgMultiplier;
				damageAPMax=srcUnit.GetDmgAPMaxM()*dmgMultiplier;
				
				if(hit){
					damageHP=Rand.Range(damageHPMin, damageHPMax);
					damageAP=Rand.Range(damageAPMin, damageAPMax);
					
					if(crit){
						float critMultiplierM=srcUnit.GetCritMultiplierM();
						damageHP*=critMultiplierM;
						damageAP*=critMultiplierM;
					}
				}
				
				return;
			}
			
			if(GameControl.EnableCoverSystem() && !isOverwatch){
				cover=GetCover(srcNode, tgtUnit.node);
			}
			
			srcUnitHit=srcUnit.GetHit();
			srcUnitAttack=srcUnit.GetAttack();
			
			hitChance=srcUnitHit/(srcUnitHit+tgtUnit.GetDodge())-(cover*GameControl.GetCoverDodgeBonus());
			if(isCounter) hitChance-=srcUnit.GetCHitPenalty();
			if(isOverwatch) hitChance-=srcUnit.GetOHitPenalty();
			hitChance=Mathf.Clamp(hitChance, 0, 1);
			hit=Rand.value()<hitChance;
			
			critChance=Mathf.Clamp(srcUnit.GetCritChance()-Mathf.Max(0, tgtUnit.GetCritReduc())+(2-cover)*GameControl.GetCoverCritBonus(), 0, 1);
			if(isCounter) critChance-=srcUnit.GetCCritPenalty();
			if(isOverwatch) critChance-=srcUnit.GetOCritPenalty();
			critChance=Mathf.Clamp(critChance, 0, 1);
			crit=Rand.value()<critChance;
			
			dmgMultiplier=srcUnitAttack/(srcUnitAttack+tgtUnit.GetDefense());
			dmgMultiplier*=DamageTable.GetMultiplier(srcUnit.damageType, tgtUnit.armorType);
			
			if(isCounter) dmgMultiplier*=srcUnit.GetCDmgMul();
			if(isOverwatch) dmgMultiplier*=srcUnit.GetODmgMul();
			
			damageHPMin=srcUnit.GetDmgHPMin()*dmgMultiplier;
			damageHPMax=srcUnit.GetDmgHPMax()*dmgMultiplier;
			
			damageAPMin=srcUnit.GetDmgAPMin()*dmgMultiplier;
			damageAPMax=srcUnit.GetDmgAPMax()*dmgMultiplier;
			
			if(hit){
				damageHP=Rand.Range(damageHPMin, damageHPMax);
				damageAP=Rand.Range(damageAPMin, damageAPMax);
				
				if(crit){
					float critMultiplierM=srcUnit.GetCritMultiplier();
					damageHP*=critMultiplierM;
					damageAP*=critMultiplierM;
				}
			}
			
			//Debug.Log("cover:"+cover+",   "+hitChance+"-"+hit+"    -"+critChance+"-"+crit+"   "+damageHP);
			//Debug.Log(srcUnit.GetDamageMin()+"   "+srcUnit.GetDamageMax()+"   "+dmgMultiplier+"   "+crit+"   "+damage);
		}
		
		public Attack(Ability ab, Unit tUnit){
			ability=ab;		tgtUnit=tUnit;
			
			float srcHit=ability.GetHit();
			float srcAttack=ability.GetAttack();
			
			float dmgMultiplier=1;
			
			if(ability.factorInTargetStats){
				hitChance=srcHit/(srcHit+tgtUnit.GetDodge());
				critChance=Mathf.Clamp(ability.GetCritChance()-Mathf.Max(0, tgtUnit.GetCritReduc()), 0, 1);
				
				dmgMultiplier=srcAttack/(srcAttack+tgtUnit.GetDefense());
				dmgMultiplier*=DamageTable.GetMultiplier(ability.damageType, tgtUnit.armorType);
			}
			else{
				hitChance=ability.GetHit();		
				critChance=ability.GetCritChance();
				dmgMultiplier=DamageTable.GetMultiplier(ability.damageType, tgtUnit.armorType);
			}
			
			hitChance=Mathf.Clamp(hitChance, 0, 1);
			critChance=Mathf.Clamp(critChance, 0, 1);
			
			hit=Rand.value()<hitChance;
			crit=Rand.value()<critChance;
			
			damageHPMin=ability.GetHPMin()*dmgMultiplier;
			damageHPMax=ability.GetHPMax()*dmgMultiplier;
			
			damageAPMin=ability.GetAPMin()*dmgMultiplier;
			damageAPMax=ability.GetAPMax()*dmgMultiplier;
			
			if(hit){
				damageHP=Rand.Range(damageHPMin, damageHPMax);
				damageAP=Rand.Range(damageAPMin, damageAPMax);
				
				if(crit){
					float critMultiplierM=ability.GetCritMultiplier();
					damageHP*=critMultiplierM;
					damageAP*=critMultiplierM;
				}
			}
		}
		
		
		public static int GetCover(Node srcNode, Node tgtNode){
			//~ Vector3 dir=(tgtNode.GetPos()-srcNode.GetPos()).normalized;
			//~ //Debug.DrawLine(srcNode.GetPos(), srcNode.GetPos()+dir*6, Color.red, 1);
			
			//~ float angle=Mathf.Round(GridManager.GetAngle(tgtNode, srcNode, false));
			//~ int cover=tgtNode.GetCover(angle);
			//~ //Debug.Log(tgtNode.idxX+","+tgtNode.idxZ+"      angle - "+angle+"    cover - "+cover+"    "+dir);
			
			//~ string coverTxt="";
			//~ for(int i=0; i<tgtNode.coverList.Count; i++){
				//~ coverTxt+=tgtNode.coverList[i]+", ";
			//~ }
			//~ //Debug.Log("cover list: "+coverTxt);
			
			int cover=tgtNode.GetCover(Mathf.Round(GridManager.GetAngle(tgtNode, srcNode, false)));
			
			if(GridManager.IsSquareGrid() && cover>0){	//check side stepping
				
				//Debug.DrawLine(srcNode.GetPos(), tgtNode.GetPos(), Color.green, 1);
				//cant rely on default calculation at diagonal, angle at 45 does not always give the correct adjacent node
				if((Mathf.Round(GridManager.GetAngle(tgtNode, srcNode, false))-45)%90==0){
					
					float distTH=GridManager.EnableDiagonalNeighbour() ? 1 : 2 ;
					if(GridManager.GetDistance(srcNode, tgtNode)<=distTH){
						Vector3 centerP=(tgtNode.GetPos()+srcNode.GetPos())*0.5f;
						cover=CheckCoverSideStepping(srcNode, centerP, tgtNode, cover);
						return cover;
					}
				}
				
				cover=CheckCoverSideStepping(srcNode, srcNode.GetPos(), tgtNode, cover);
			}
			
			return cover;
		}
		public static int CheckCoverSideStepping(Node srcNode, Vector3 origin, Node tgtNode, int cover){
			Vector3 dir=(tgtNode.GetPos()-srcNode.GetPos()).normalized;
			Vector3 dirP=new Vector3(dir.z, 0, -dir.x);
			float nodeS=GridManager.GetNodeSize();
			
			//Utility.DebugDrawCrossT(origin+dirP*nodeS);
			
			Node neighbour=srcNode.GetNeighbourFromPos(origin+dirP*nodeS);
			if(neighbour!=null && !neighbour.IsBlocked(srcNode)){
				//Debug.DrawLine(neighbour.GetPos(), tgtNode.GetPos(), Color.green, 1);
				int coverAlt1=tgtNode.GetCover(GridManager.GetAngle(tgtNode, neighbour, false));
				if(coverAlt1<cover) cover=coverAlt1;
			}
			
			if(cover>0){
				
				//Utility.DebugDrawCrossT(origin-dirP*nodeS);
				
				neighbour=srcNode.GetNeighbourFromPos(origin-dirP*nodeS);
				if(neighbour!=null && !neighbour.IsBlocked(srcNode)){
					//Debug.DrawLine(neighbour.GetPos(), tgtNode.GetPos(), Color.green, 1);
					int coverAlt2=tgtNode.GetCover(GridManager.GetAngle(tgtNode, neighbour, false));
					if(coverAlt2<cover) cover=coverAlt2;
				}
			}
			
			return cover;
		}
		
	}
	
}
