using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using TBTK;

public class JRPG : MonoBehaviour {

	void OnEnable(){
		TBTK.TBTK.onGameStartE += OnGameStart ;
	}
	void OnDisable(){
		TBTK.TBTK.onGameStartE -= OnGameStart ;
	}
	
	void OnGameStart(){
		List<Unit> unitList=UnitManager.GetAllUnitList();
		for(int i=0; i<unitList.Count; i++){
			unitList[i].stats.moveRange=0;
			unitList[i].stats.attackRange=10;
		}
		
		UnitManager.SelectUnit(UnitManager.GetSelectedUnit());
	}
	
}
