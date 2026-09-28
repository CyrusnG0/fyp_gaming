using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace TBTK{
	
	public class CollectibleManager : MonoBehaviour {
		
		#if UNITY_EDITOR
		public static bool inspector=false;
		#endif
		
		public int activeItemLimit=4;
		
		public bool generateInGame=true;
		public int maxSpawnPerTurn=2;
		public float spawnChance=0.5f;
		
		public VisualObject effectOnSpawn;
		
		
		public List<int> unavailableIDList=new List<int>();
		public List<Collectible> itemList=new List<Collectible>();	//filled up in runtime
		
		
		public List<Collectible> activeItemList=new List<Collectible>();
		
		private static CollectibleManager instance;
		
		public static void Init(){
			if(instance==null) instance=(CollectibleManager)FindObjectOfType(typeof(CollectibleManager));
			
			if(instance==null) return;
			
			for(int i=0; i<instance.activeItemList.Count; i++){
				if(instance.activeItemList[i]==null){ instance.activeItemList.RemoveAt(i); i-=1; }
			}
			
			instance.InitItemList();
		}
		
		
		public static void ItemTriggered(Collectible item){
			if(instance==null) return;
			instance.activeItemList.Remove(item);
		}
		
		
		public void PlaceItemAtNode(Collectible item, Node node){
			//~ float rotUnit=tile.type==_TileType.Hex ? 60 : 90 ;
			//~ Quaternion rotation=Quaternion.Euler(0, Random.Range(0, 6)*rotUnit, 0);
			
			//~ itemObj.transform.position=tile.GetPos();
			//~ itemObj.transform.rotation=rotation;
			//~ itemObj.transform.parent=tile.transform;
			
			
			if(node!=null){
				item.transform.position=node.GetPos();
				//item.transform.parent=node.GetObjT();
				
				item.transform.name=node.GetObjT().name+"_"+item.transform.name;
				
				node.collectible=item;
			}
			
			item.transform.parent=GridGenerator.GetGridObjParent();
			
			AddItem(item);
		}
		public void AddItem(Collectible item){
			activeItemList.Add(item);
		}
		public void RemoveItem(Collectible item){
			activeItemList.Remove(item);
		}
		
		
		
		public static bool NewTurn(){ return instance!=null ? instance._NewTurn() : false; }
		public bool _NewTurn(){
			if(!generateInGame) return false;
			if(activeItemList.Count>=activeItemLimit) return false;
			
			bool spawned=false;
			for(int i=0; i<maxSpawnPerTurn; i++){
				if(activeItemList.Count>=activeItemLimit) break;
				if(Random.value>spawnChance) continue;
				
				Node node=GetRandomNode();
				if(node==null) continue;
				
				int rand=Random.Range(0, itemList.Count);
				GameObject itemObj=(GameObject)Instantiate(itemList[rand].gameObject);
				PlaceItemAtNode(itemObj.GetComponent<Collectible>(), node);
				
				effectOnSpawn.Spawn(node.GetPos());
				
				spawned=true;
			}
			
			return spawned;
		}
		
		
		void InitItemList(){
			itemList=new List<Collectible>( CollectibleDB.GetList() );
			
			for(int i=0; i<itemList.Count; i++){
				if(!unavailableIDList.Contains(itemList[i].prefabID)) continue;
				itemList.RemoveAt(i);	i-=1;
			}
		}
		
		
		public void Generate(){
			//if(!generateInGame) return;
			
			InitItemList();
			
			Debug.Log(" Generate collectible");
			
			int itemCount=Random.Range(activeItemLimit/2, activeItemLimit+1);
			int iterateCount=0;
			while(itemCount>0){
				iterateCount+=1;
				if(iterateCount>10) break;
				
				Node node=GetRandomNode();
				if(node==null) break;
				
				if(node.HasObstacle()) Debug.Log("real strange");
				
				int rand=Rand.Range(0, itemList.Count);
				#if UNITY_EDITOR
					GameObject itemObj=(GameObject)PrefabUtility.InstantiatePrefab(itemList[rand].gameObject);
				#else
					GameObject itemObj=(GameObject)MonoBehaviour.Instantiate(itemList[rand].gameObject);
				#endif
				
				PlaceItemAtNode(itemObj.GetComponent<Collectible>(), node);
				
				itemCount-=1;
			}
		}
		
		private Node GetRandomNode(){
			Node node=null;	List<List<Node>> grid=GridManager.GetGrid();
			
			int iterateCount=0;
			while(true){
				iterateCount+=1;
				if(iterateCount>20) break;
				
				node=grid[Rand.Range(0, GridManager.DimensionX())][Rand.Range(0, GridManager.DimensionZ()-1)];
				if(!node.walkable || !node.IsEmpty() || node.deployFacID>=0) continue;
				
				break;
			}
			
			return node;
		}
		
	}

}