using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "DamageTableDB", menuName = "TBTK_DB/DamageTableDB", order = 1)]
	public class DamageTableDB : ScriptableObject {
		
		public List<ArmorType> armorTypeList=new List<ArmorType>();
		public List<DamageType> damageTypeList=new List<DamageType>();
		
		public static DamageTableDB LoadDB(){
			return Resources.Load("DB_TBTK/DamageTableDB", typeof(DamageTableDB)) as DamageTableDB;
		}
		
		
		#region runtime code
		public static DamageTableDB instance;
		public static DamageTableDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			return instance;
		}
		
		public static DamageTableDB GetDB(){ return Init(); }
		public static List<ArmorType> GetArmorList(){ return Init().armorTypeList; }
		public static List<DamageType> GetDamageList(){ return Init().damageTypeList; }
		
		
		public static string[] armorlb;
		public static string[] damagelb;
		public static void UpdateLabel(){
			armorlb=new string[GetArmorList().Count];
			for(int i=0; i<GetArmorList().Count; i++) armorlb[i]=i+" - "+GetArmorList()[i].name;
			
			damagelb=new string[GetDamageList().Count];
			for(int i=0; i<GetDamageList().Count; i++) damagelb[i]=i+" - "+GetDamageList()[i].name;
		}
		#endregion
		
	}
	
	
}
