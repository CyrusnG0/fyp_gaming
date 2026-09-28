using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using TBTK;

public class XCom : MonoBehaviour {

	void OnEnable(){
		TBTK.TBTK.onGameStartE += OnGameStart ;
	}
	void OnDisable(){
		TBTK.TBTK.onGameStartE -= OnGameStart ;
	}
	
	void OnGameStart(){
		List<Unit> unitList=UnitManager.GetAllUnitList();
		for(int i=0; i<unitList.Count; i++){
			unitList[i].stats.moveRange=5;
			unitList[i].stats.attackRange=9;
			unitList[i].stats.sight=9;
			
			unitList[i].AddAbility(12);	//overwatch prefabID is 12
		}
		
		UnitManager.SelectUnit(UnitManager.GetSelectedUnit());
		
		GridManager.SetupFogOfWar();
	}
	
}
