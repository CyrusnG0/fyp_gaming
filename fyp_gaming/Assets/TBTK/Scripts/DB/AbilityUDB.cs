using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "AbilityUDB", menuName = "TBTK_DB/AbilityUDB", order = 1)]
	public class AbilityUDB : ScriptableObject {
		
		public List<Ability> abilityList=new List<Ability>();
		
		public static AbilityUDB LoadDB(){
			return Resources.Load("DB_TBTK/AbilityUDB", typeof(AbilityUDB)) as AbilityUDB;
		}
		
		
		#region runtime code
		public static AbilityUDB instance;
		public static AbilityUDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			return instance;
		}
		
		public static AbilityUDB GetDB(){ return Init(); }
		public static List<Ability> GetList(){ return Init().abilityList; }
		public static Ability GetItem(int index){ Init(); return (index>=0 && index<instance.abilityList.Count) ? instance.abilityList[index] : null; }
		public static int GetItemID(int index){ Init(); return (index>=0 && index<instance.abilityList.Count) ? instance.abilityList[index].prefabID : -1; }
		public static int GetCount(){ Init(); return instance.abilityList.Count; }
		
		public static List<int> GetPrefabIDList(){ Init();
			List<int> prefabIDList=new List<int>();
			for(int i=0; i<instance.abilityList.Count; i++) prefabIDList.Add(instance.abilityList[i].prefabID);
			return prefabIDList;
		}
		
		public static Ability GetPrefab(int pID){ Init();
			for(int i=0; i<instance.abilityList.Count; i++){
				if(instance.abilityList[i].prefabID==pID) return instance.abilityList[i];
			}
			return null;
		}
		
		public static int GetPrefabIndex(int pID){ Init();
			for(int i=0; i<instance.abilityList.Count; i++){
				if(instance.abilityList[i].prefabID==pID) return i;
			}
			return -1;
		}
		public static int GetPrefabIndex(Ability ability){
			if(ability==null) return -1;
			return GetPrefabIndex(ability.prefabID);
		}
		
		public static string[] label;
		public static void UpdateLabel(){
			label=new string[GetList().Count];
			for(int i=0; i<GetList().Count; i++) label[i]=i+" - "+GetList()[i].name;
		}
		#endregion
		
		
		#if UNITY_EDITOR
		[ContextMenu ("Reset PrefabID")]
		public void ResetPrefabID(){
			for(int i=0; i<abilityList.Count; i++){
				abilityList[i].prefabID=i;
				UnityEditor.EditorUtility.SetDirty(this);
			}
		}
		#endif
		
	}
	
	
}
