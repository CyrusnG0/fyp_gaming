using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "UnitDB", menuName = "TBTK_DB/UnitDB", order = 1)]
	public class UnitDB : ScriptableObject {
		
		[HideInInspector] public List<GameObject> objList=new List<GameObject>();
		public List<Unit> unitList=new List<Unit>();
		
		public static UnitDB LoadDB(){
			return Resources.Load("DB_TBTK/UnitDB", typeof(UnitDB)) as UnitDB;
		}
		
		
		#region runtime code
		public static UnitDB instance;
		public static UnitDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			
			#if UNITY_2018_3_OR_NEWER && UNITY_EDITOR
			instance.FillObjectList();
			#endif
			
			return instance;
		}
		
		public static UnitDB GetDB(){ return Init(); }
		public static List<Unit> GetList(bool verify=true){ Init();
			if(verify) VerifyList();
			return instance.unitList;
		}
		public static Unit GetItem(int index){ Init(); return (index>=0 && index<instance.unitList.Count) ? instance.unitList[index] : null; }
		public static int GetItemID(int index){ Init(); return (index>=0 && index<instance.unitList.Count) ? instance.unitList[index].prefabID : -1; }
		public static int GetCount(){ Init(); return instance.unitList.Count; }
		
		public static List<int> GetPrefabIDList(){ Init();
			List<int> prefabIDList=new List<int>();
			for(int i=0; i<instance.unitList.Count; i++) prefabIDList.Add(instance.unitList[i].prefabID);
			return prefabIDList;
		}
		
		public static Unit GetPrefab(int pID){ Init();
			for(int i=0; i<instance.unitList.Count; i++){
				if(instance.unitList[i].prefabID==pID) return instance.unitList[i];
			}
			return null;
		}
		
		public static int GetPrefabIndex(int pID){ Init();
			for(int i=0; i<instance.unitList.Count; i++){
				if(instance.unitList[i].prefabID==pID) return i;
			}
			return -1;
		}
		public static int GetPrefabIndex(Unit unit){
			if(unit==null) return -1;
			return GetPrefabIndex(unit.prefabID);
		}
		
		public static void ResetItemPID(int index){
			Init(); if(index>=0 && index<instance.unitList.Count) instance.unitList[index].prefabID=-1;
		}
		
		public static void VerifyList(){
			#if UNITY_2018_3_OR_NEWER
			for(int i=0; i<instance.unitList.Count; i++){
				if(instance.unitList[i]!=null){
					if(instance.objList.Count>i)	instance.objList[i]=instance.unitList[i].gameObject;
					else									instance.objList.Add(instance.unitList[i].gameObject);
					continue;
				}
				if(i<instance.objList.Count && instance.objList[i]!=null){
					Unit unit=instance.objList[i].GetComponent<Unit>();
					if(unit!=null){ instance.unitList[i]=unit; continue; }
				}
				instance.unitList.RemoveAt(i);	i-=1;
			}
			
			while(instance.objList.Count>instance.unitList.Count) instance.objList.RemoveAt(instance.objList.Count-1);
			#else
			for(int i=0; i<instance.unitList.Count; i++){
				if(instance.unitList[i]==null){ instance.unitList.RemoveAt(i);	i-=1; }
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
			for(int i=0; i<unitList.Count; i++){
				unitList[i].prefabID=i;
				UnityEditor.EditorUtility.SetDirty(unitList[i]);
			}
		}
		
		public void FillObjectList(){
			ClearEmptyElement();
			
			objList=new List<GameObject>();
			for(int i=0; i<unitList.Count; i++) objList.Add(unitList[i].gameObject);
		}
		
		public void ClearEmptyElement(){
			for(int i=0; i<instance.unitList.Count; i++){
				if(instance.unitList[i]==null){ instance.unitList.RemoveAt(i); i-=1; }
			}
		}
		#endif
		
	}
	
	
}
