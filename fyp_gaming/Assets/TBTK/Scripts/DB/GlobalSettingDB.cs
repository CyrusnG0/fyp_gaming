using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;

using TBTK;

namespace TBTK {

	[CreateAssetMenu(fileName = "GlobalSettingDB", menuName = "TBTK_DB/GlobalSettingDB", order = 1)]
	public class GlobalSettingDB : ScriptableObject {
		
		//GameControl
		public bool enableUnitDeployment=false;
		
		
		public bool autoEndTurn=false;
		public bool endMoveAfterAttack=false;
		public bool enableCounterAttack=false;
		public bool restoreAPOnTurn;
		public bool useAPToMove;
		public bool useAPToAttack;
		
		public int apPerMove=1;
		//public int apPerNode=0;
		public int apPerAttack=1;
		
		public bool enableSideStepping;
		public bool enableFogOfWar;
		public bool enableCoverSystem;
		public float coverCritBonus=0.25f;
		public float coverDodgeBonus=0.3f;
		
		
		//TurnControl
		public _TurnMode turnMode;
		public bool allowSwitching=true;
		
		public bool allowUnitSwitching=false;
		public bool waitForUnitDestroy=false;
		
		public _CDTracking cdTracking;
		
		
		
		public static GlobalSettingDB LoadDB(){
			return Resources.Load("DB_TBTK/GlobalSettingDB", typeof(GlobalSettingDB)) as GlobalSettingDB;
		}
		
		#region runtime code
		public static GlobalSettingDB instance;
		public static GlobalSettingDB Init(){
			if(instance!=null) return instance;
			instance=LoadDB();
			return instance;
		}
		
		public static GlobalSettingDB GetDB(){ return Init(); }
		#endregion
		
	}
	
	
}
