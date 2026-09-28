using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public static class TBTK {
		
		public static int GetLayerUnit(){ return 31; }
		public static int GetLayerInvisible(){ return 30; }
		
		public static int GetLayerNode(){ return 29; }
		public static int GetLayerObsFullCover(){ return 28; }
		public static int GetLayerObsHalfCover(){ return 27; }	//obstacleHalfCover
		public static int GetLayerTerrain(){ return 25; }
		
		public static int LayerUI(){ return 5; }	//layer5 is named UI by Unity's default
		
		
		
		
		public delegate void gameMessageHandler(string msg);
		public static event gameMessageHandler onGameMessageE;			
		public static void OnGameMessage(string msg){ if(onGameMessageE!=null) onGameMessageE(msg); }
		
		
		public delegate void textOverlayHandler(string msg, Vector3 pos, Color color=default(Color));
		public static event textOverlayHandler onTextOverlayE;			
		public static void TextOverlay(string msg, Vector3 pos, Color color=default(Color)){ if(onTextOverlayE!=null) onTextOverlayE(msg, pos, color); }
		
		
		
		public delegate void DeploymentHandler(Faction fac);
		public static event DeploymentHandler onDeploymentE;			
		public static void OnDeployment(Faction fac){ if(onDeploymentE!=null) onDeploymentE(fac); }
		
		
		public delegate void GameStartHandler();
		public static event GameStartHandler onGameStartE;			
		public static void OnGameStart(){ if(onGameStartE!=null) onGameStartE(); }
		
		public delegate void GameOverHandler(bool playerWon);
		public static event GameOverHandler onGameOverE;			
		public static void OnGameOver(bool playerWon){ if(onGameOverE!=null) onGameOverE(playerWon); }
		
		
		
		public delegate void ActionInProgressHandler(bool flag);
		public static event ActionInProgressHandler onActionInProgressE;			
		public static void OnActionInProgress(bool flag){ if(onActionInProgressE!=null) onActionInProgressE(flag); }
		
		
		public delegate void NewTurnHandler();
		public static event NewTurnHandler onNewTurnE;			
		public static void OnNewTurn(){ if(onNewTurnE!=null) onNewTurnE(); }
		
		
		public delegate void NewUnitHandler(Unit unit, int facIdx);
		public static event NewUnitHandler onNewUnitE;			
		public static void OnNewUnit(Unit unit, int facIdx){ if(onNewUnitE!=null) onNewUnitE(unit, facIdx); }
		
		public delegate void SelectUnitHandler(Unit unit);
		public static event SelectUnitHandler onSelectUnitE;			
		public static void OnSelectUnit(Unit unit){ if(onSelectUnitE!=null) onSelectUnitE(unit); }
		
		public delegate void SelectFactionHandler(Faction fac);
		public static event SelectFactionHandler onSelectFactionE;			
		public static void OnSelectFaction(Faction fac){ if(onSelectFactionE!=null) onSelectFactionE(fac); }
		
		
		public delegate void AbilityTargetingHandler(Ability Ability);
		public static event AbilityTargetingHandler onAbilityTargetingE;			
		public static void OnAbilityTargeting(Ability Ability){ if(onAbilityTargetingE!=null) onAbilityTargetingE(Ability); }
		
		
		public delegate void UnitDestroyedHandler(Unit unit);
		public static event UnitDestroyedHandler onUnitDestroyedE;			
		public static void OnUnitDestroyed(Unit unit){ if(onUnitDestroyedE!=null) onUnitDestroyedE(unit); }
		
		
		public delegate void UnitOrderChanged();
		public static event UnitOrderChanged onUnitOrderChangedE;			
		public static void OnUnitOrderChanged(){ if(onUnitOrderChangedE!=null) onUnitOrderChangedE(); }
	}

}