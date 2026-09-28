using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "CollectibleDB", menuName = "TBTK_DB/CollectibleDB", order = 1)]
	public class CollectibleDB : ScriptableObject {
		
		[HideInInspector] public List<GameObject> objList=new List<GameObject>();
		public List<Collectible> collectibleList=new List<Collectible>();
		
		public static CollectibleDB LoadDB(){
			return Resources.Load("DB_TBTK/CollectibleDB", typeof(CollectibleDB)) as CollectibleDB;
		}
		
		
		#region runtime code
		public static CollectibleDB instance;
		public static CollectibleDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			
			#if UNITY_2018_3_OR_NEWER && UNITY_EDITOR
			instance.FillObjectList();
			#endif
			
			return instance;
		}
		
		public static CollectibleDB GetDB(){ return Init(); }
		public static List<Collectible> GetList(bool verify=true){ Init();
			if(verify) VerifyList();
			return instance.collectibleList;
		}
		public static Collectible GetItem(int index){ Init(); return (index>=0 && index<instance.collectibleList.Count) ? instance.collectibleList[index] : null; }
		public static int GetItemID(int index){ Init(); return (index>=0 && index<instance.collectibleList.Count) ? instance.collectibleList[index].prefabID : -1; }
		public static int GetCount(){ Init(); return instance.collectibleList.Count; }
		
		public static List<int> GetPrefabIDList(){ Init();
			List<int> prefabIDList=new List<int>();
			for(int i=0; i<instance.collectibleList.Count; i++) prefabIDList.Add(instance.collectibleList[i].prefabID);
			return prefabIDList;
		}
		
		public static Collectible GetPrefab(int pID){ Init();
			for(int i=0; i<instance.collectibleList.Count; i++){
				if(instance.collectibleList[i].prefabID==pID) return instance.collectibleList[i];
			}
			return null;
		}
		
		public static int GetPrefabIndex(int pID){ Init();
			for(int i=0; i<instance.collectibleList.Count; i++){
				if(instance.collectibleList[i].prefabID==pID) return i;
			}
			return -1;
		}
		public static int GetPrefabIndex(Collectible collectible){
			if(collectible==null) return -1;
			return GetPrefabIndex(collectible.prefabID);
		}
		
		public static void ResetItemPID(int index){
			Init(); if(index>=0 && index<instance.collectibleList.Count) instance.collectibleList[index].prefabID=-1;
		}
		
		public static void VerifyList(){
			#if UNITY_2018_3_OR_NEWER
			for(int i=0; i<instance.collectibleList.Count; i++){
				if(instance.collectibleList[i]!=null){
					if(instance.objList.Count>i)	instance.objList[i]=instance.collectibleList[i].gameObject;
					else									instance.objList.Add(instance.collectibleList[i].gameObject);
					continue;
				}
				if(i<instance.objList.Count && instance.objList[i]!=null){
					Collectible collectible=instance.objList[i].GetComponent<Collectible>();
					if(collectible!=null){ instance.collectibleList[i]=collectible; continue; }
				}
				instance.collectibleList.RemoveAt(i);	i-=1;
			}
			
			while(instance.objList.Count>instance.collectibleList.Count) instance.objList.RemoveAt(instance.objList.Count-1);
			#else
			for(int i=0; i<instance.collectibleList.Count; i++){
				if(instance.collectibleList[i]==null){ instance.collectibleList.RemoveAt(i);	i-=1; }
			}
			#endif
		}
		
		
		public static string[] label;
		public static void UpdateLabel(){
			label=new string[GetList(false).Count];
			for(int i=0; i<label.Length; i++) label[i]=i+" - "+GetItem(i).itemName;
		}
		#endregion
		
		
		#if UNITY_EDITOR
		[ContextMenu ("Reset PrefabID")]
		public void ResetPrefabID(){
			for(int i=0; i<collectibleList.Count; i++){
				collectibleList[i].prefabID=i;
				UnityEditor.EditorUtility.SetDirty(collectibleList[i]);
			}
		}
		
		public void FillObjectList(){
			ClearEmptyElement();
			
			objList=new List<GameObject>();
			for(int i=0; i<collectibleList.Count; i++) objList.Add(collectibleList[i].gameObject);
		}
		
		public void ClearEmptyElement(){
			for(int i=0; i<instance.collectibleList.Count; i++){
				if(instance.collectibleList[i]==null){ instance.collectibleList.RemoveAt(i); i-=1; }
			}
		}
		#endif
		
	}
	
	
}
