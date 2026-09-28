using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "EffectDB", menuName = "TBTK_DB/EffectDB", order = 1)]
	public class EffectDB : ScriptableObject {
		
		public List<Effect> effectList=new List<Effect>();
		
		public static EffectDB LoadDB(){
			return Resources.Load("DB_TBTK/EffectDB", typeof(EffectDB)) as EffectDB;
		}
		
		
		#region runtime code
		public static EffectDB instance;
		public static EffectDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			return instance;
		}
		
		public static EffectDB GetDB(){ return Init(); }
		public static List<Effect> GetList(){ return Init().effectList; }
		public static Effect GetItem(int index){ Init(); return (index>=0 && index<instance.effectList.Count) ? instance.effectList[index] : null; }
		public static int GetItemID(int index){ Init(); return (index>=0 && index<instance.effectList.Count) ? instance.effectList[index].prefabID : -1; }
		public static int GetCount(){ Init(); return instance.effectList.Count; }
		
		public static List<int> GetPrefabIDList(){ Init();
			List<int> prefabIDList=new List<int>();
			for(int i=0; i<instance.effectList.Count; i++) prefabIDList.Add(instance.effectList[i].prefabID);
			return prefabIDList;
		}
		
		public static Effect GetPrefab(int pID){ Init();
			for(int i=0; i<instance.effectList.Count; i++){
				if(instance.effectList[i].prefabID==pID) return instance.effectList[i];
			}
			return null;
		}
		
		public static int GetPrefabIndex(int pID){ Init();
			for(int i=0; i<instance.effectList.Count; i++){
				if(instance.effectList[i].prefabID==pID) return i;
			}
			return -1;
		}
		public static int GetPrefabIndex(Effect effect){
			if(effect==null) return -1;
			return GetPrefabIndex(effect.prefabID);
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
			for(int i=0; i<effectList.Count; i++){
				effectList[i].prefabID=i;
				UnityEditor.EditorUtility.SetDirty(this);
			}
		}
		#endif
		
	}
	
	
}
