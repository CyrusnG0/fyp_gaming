using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public class Collectible : TBMonoItem {
		
		public static bool inspector=false;
		
		[Space(8)] 
		public VisualObject effectOnSpawn;
		public VisualObject effectOnTrigger;
		
		
		public int abilityID=-1;
		
		public bool randomizedEffect=false;
		public List<int> effectIDList=new List<int>();
		
		public void Spawn(){
			effectOnSpawn.Spawn(GetPos());
		}
		
		public IEnumerator Trigger(Unit unit){
			if(abilityID>=0){
				Ability ability=AbilityFDB.GetPrefab(abilityID);
				if(ability!=null){
					ability=ability.Clone();
					yield return StartCoroutine(ability.HitTarget(unit.node));
				}
			}
			
			if(effectIDList.Count>=0){
				if(randomizedEffect && effectIDList.Count>1){
					unit.ApplyEffect(new List<int>{ effectIDList[Rand.Range(0, effectIDList.Count-1)] });
				}
				else unit.ApplyEffect(effectIDList);
			}
			
			effectOnTrigger.Spawn(GetPos());
			
			//node.collectible=null;
			
			CollectibleManager.ItemTriggered(this);
			
			Destroy(gameObject);
		}
		
	}
	
}