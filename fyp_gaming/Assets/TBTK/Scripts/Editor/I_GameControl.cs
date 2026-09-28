using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(GameControl))]
	public class I_GameControlEditor : TBEditorInspector {
		
		private GameControl instance;
		private TurnControl tControl;
		
		private GlobalSettingDB gsDB;
		
		public override void Awake(){
			base.Awake();
			instance = (GameControl)target;
			
			tControl=instance.gameObject.GetComponent<TurnControl>();
			
			InitLabel();
		}
		
		
		private string[] turnModeLabel;
		private string[] turnModeTooltip;
		
		private string[] cdTrackingLabel;
		private string[] cdTrackingTooltip;
		
		void InitLabel(){
			int enumLength = Enum.GetValues(typeof(_TurnMode)).Length;
			turnModeLabel=new string[enumLength];		turnModeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				turnModeLabel[i]=((_TurnMode)i).ToString();
				if((_TurnMode)i==_TurnMode.FactionPerTurn) 	turnModeTooltip[i]="A game-mode where each faction take turns to move all it's unit";
				if((_TurnMode)i==_TurnMode.UnitPerTurn) 			turnModeTooltip[i]="A game-mode where each unit takes turn to move based on their stats (turn-priority)";
			}
			
			enumLength = Enum.GetValues(typeof(_CDTracking)).Length;
			cdTrackingLabel=new string[enumLength];		cdTrackingTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				cdTrackingLabel[i]=((_CDTracking)i).ToString();
				if((_CDTracking)i==_CDTracking.EveryTurn) 	cdTrackingTooltip[i]="The cooldown of ability and effect ticks for every turn (everytime a unit end its move)";
				if((_CDTracking)i==_CDTracking.EveryRound) 	cdTrackingTooltip[i]="The cooldown of ability and effect ticks for every round (after all units has moved)";
			}
		}
		
		
		public override void OnInspectorGUI(){
			if(gsDB==null) gsDB=GlobalSettingDB.Init();
			
			Undo.RecordObject(instance, "GameControl");
			Undo.RecordObject(tControl, "TurnControl");
			
			base.OnInspectorGUI();
			
			EditorGUIUtility.labelWidth=158;
			
			EditorGUILayout.Space();
			
				cont=new GUIContent("Use Global Setting:", "Check to use the global setting (can be applied to every scene)");
				instance.useGlobalSetting=EditorGUILayout.Toggle(cont, instance.useGlobalSetting);
			
			EditorGUILayout.Space();
			
			
			if(!instance.useGlobalSetting){
			
					cont=new GUIContent("Enable Unit Deployment:", "Check to let the player to place starting units manually before the game starts");
					instance.enableUnitDeployment=EditorGUILayout.Toggle(cont, instance.enableUnitDeployment);
				
				EditorGUILayout.Space();
				
					int turnMode=(int)tControl.turnMode;
					cont=new GUIContent("Turn Mode:", "How the move order is determined");
					turnMode = EditorGUILayout.Popup(cont, turnMode, TBE.SetupContL(turnModeLabel, turnModeTooltip));
					tControl.turnMode=(_TurnMode)turnMode;
					
					cont=new GUIContent("Enable Unit-Limit/Turn:", "Check to set a limit on how many unit player can move in a single turn");
					if(tControl.turnMode==_TurnMode.UnitPerTurn) EditorGUILayout.LabelField("Enable Unit-Limit/Turn:", "n/a");
					else tControl.enableUnitLimit=EditorGUILayout.Toggle(cont, tControl.enableUnitLimit);
				
					cont=new GUIContent(" - Unit-Limit/Turn:", "how many unit player can move in a single turn");
					if(tControl.turnMode==_TurnMode.UnitPerTurn || !tControl.enableUnitLimit) EditorGUILayout.LabelField(" - Unit-Limit/Turn:", "n/a");
					else tControl.facUnitLimit=EditorGUILayout.IntField(cont, tControl.facUnitLimit);
				
					cont=new GUIContent("Allow Unit Switching:", "Check to allow player to choose which unit to move during their turn");
					if(tControl.turnMode==_TurnMode.UnitPerTurn) EditorGUILayout.LabelField("Allow Unit Switching:", "n/a");
					else tControl.allowUnitSwitching=EditorGUILayout.Toggle(cont, tControl.allowUnitSwitching);
					
					cont=new GUIContent("CD Tracking Mode:", "How the cooldown for effect and ability are tracked");
					if(tControl.turnMode==_TurnMode.UnitPerTurn){
						int cdTracking=(int)tControl.cdTracking;
						cdTracking = EditorGUILayout.Popup(cont, cdTracking, TBE.SetupContL(cdTrackingLabel, cdTrackingTooltip));
						tControl.cdTracking=(_CDTracking)cdTracking;
					}
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
					cont=new GUIContent("Wait For Unit Destroyed:", "Check to have the game wait for a destroyed unit to finish its destroyed animation or effect sequence before moving on");
					tControl.waitForUnitDestroy=EditorGUILayout.Toggle(cont, tControl.waitForUnitDestroy);
					
				EditorGUILayout.Space();
					
					cont=new GUIContent("Enable Side-Stepping:", "Check to consider unit side stepping into adjacent node when determining LOS. This allows attack from behind cover and hitting target hiding on the edge of a cover");
					instance.enableSideStepping=EditorGUILayout.Toggle(cont, instance.enableSideStepping);
					
					cont=new GUIContent("Enable Fog-of-War:", "Check to enable fog-of-war");
					instance.enableFogOfWar=EditorGUILayout.Toggle(cont, instance.enableFogOfWar);
					
					cont=new GUIContent("Enable Cover System:", "Check to have obstacle and wall provide cover bonus to adjacent unit");
					instance.enableCoverSystem=EditorGUILayout.Toggle(cont, instance.enableCoverSystem);
					
					cont=new GUIContent(" - Covered Dodge-Bonus:", "Dodge bonus gained when a unit is in covered\nOnly valid when cover system is enabled");
					if(instance.enableCoverSystem) instance.coverDodgeBonus=EditorGUILayout.FloatField(cont, instance.coverDodgeBonus);
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
					cont=new GUIContent(" - Flanked Crit-Bonus:", "Critical bonus gained when attacking a unit that is not in cover\nOnly valid when cover system is enabled");
					if(instance.enableCoverSystem) instance.coverCritBonus=EditorGUILayout.FloatField(cont, instance.coverCritBonus);
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
				EditorGUILayout.Space();
					
					cont=new GUIContent("Auto End Turn:", "Check to automatically end player turn when all move has been depleted");
					instance.autoEndTurn=EditorGUILayout.Toggle(cont, instance.autoEndTurn);
					
					cont=new GUIContent("End Move After Attack:", "End all unit move after the unit perform an attack");
					instance.endMoveAfterAttack=EditorGUILayout.Toggle(cont, instance.endMoveAfterAttack);
					
					cont=new GUIContent("Enable Counter-Attack:", "Enable unit to perform counter-attack when attacked");
					instance.enableCounterAttack=EditorGUILayout.Toggle(cont, instance.enableCounterAttack);
				
				EditorGUILayout.Space();

					cont=new GUIContent("Restore AP on New Turn:", "Fully restore unit Action-Point on new turn");
					instance.restoreAPOnTurn=EditorGUILayout.Toggle(cont, instance.restoreAPOnTurn);
					
					cont=new GUIContent("Consume AP when Move:", "Unit will use Action-Point to move");
					instance.useAPToMove=EditorGUILayout.Toggle(cont, instance.useAPToMove);
					
					cont=new GUIContent(" - AP Cost Per Move:", "Action-Point cost to start moving");
					if(!instance.useAPToMove) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else instance.apPerMove=EditorGUILayout.IntField(cont, instance.apPerMove);
					
					//cont=new GUIContent(" - AP Cost Per Node:", "Action-Point cost per node when moving");
					//if(!instance.useAPToMove) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					//else instance.apPerNode=EditorGUILayout.IntField(cont, instance.apPerNode);
					
					cont=new GUIContent("Consume AP when Attack:", "Unit will use Action-Point for each attack");
					instance.useAPToAttack=EditorGUILayout.Toggle(cont, instance.useAPToAttack);
					
					cont=new GUIContent(" - AP Cost Per Attack:", "Action-Point cost for each attack");
					if(!instance.useAPToAttack) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else instance.apPerAttack=EditorGUILayout.IntField(cont, instance.apPerAttack);
					
			}
			else{
			
					cont=new GUIContent("Enable Unit Deployment:", "Check to let the player to place starting units manually before the game starts");
					gsDB.enableUnitDeployment=EditorGUILayout.Toggle(cont, gsDB.enableUnitDeployment);
				
				EditorGUILayout.Space();
				
					int turnMode=(int)gsDB.turnMode;
					cont=new GUIContent("Turn Mode:", "How the move order is determined");
					turnMode = EditorGUILayout.Popup(cont, turnMode, TBE.SetupContL(turnModeLabel, turnModeTooltip));
					gsDB.turnMode=(_TurnMode)turnMode;
					
					cont=new GUIContent("Allow Unit Switching:", "Check to allow player to choose which unit to move during their turn");
					if(gsDB.turnMode==_TurnMode.UnitPerTurn) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else gsDB.allowUnitSwitching=EditorGUILayout.Toggle(cont, gsDB.allowUnitSwitching);
					
					cont=new GUIContent("CD Tracking Mode:", "How the cooldown for effect and ability are tracked");
					if(gsDB.turnMode==_TurnMode.UnitPerTurn){
						int cdTracking=(int)gsDB.cdTracking;
						cdTracking = EditorGUILayout.Popup(cont, cdTracking, TBE.SetupContL(cdTrackingLabel, cdTrackingTooltip));
						gsDB.cdTracking=(_CDTracking)cdTracking;
					}
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
					cont=new GUIContent("Wait For Unit Destroyed:", "Check to have the game wait for a destroyed unit to finish its destroyed animation or effect sequence before moving on");
					gsDB.waitForUnitDestroy=EditorGUILayout.Toggle(cont, gsDB.waitForUnitDestroy);
					
				EditorGUILayout.Space();
					
					cont=new GUIContent("Enable Side-Stepping:", "Check to consider unit side stepping into adjacent node when determining LOS. This allows attack from behind cover and hitting target hiding on the edge of a cover.");
					gsDB.enableSideStepping=EditorGUILayout.Toggle(cont, gsDB.enableSideStepping);
					
					cont=new GUIContent("Enable Fog-of-War:", "Check to enable fog-of-war");
					gsDB.enableFogOfWar=EditorGUILayout.Toggle(cont, gsDB.enableFogOfWar);
					
					cont=new GUIContent("Enable Cover System:", "Check to have obstacle and wall provide cover bonus to adjacent unit");
					gsDB.enableCoverSystem=EditorGUILayout.Toggle(cont, gsDB.enableCoverSystem);
					
					cont=new GUIContent(" - Covered Dodge-Bonus:", "Dodge bonus gained when a unit is in covered\nOnly valid when cover system is enabled");
					if(gsDB.enableCoverSystem) gsDB.coverDodgeBonus=EditorGUILayout.FloatField(cont, gsDB.coverDodgeBonus);
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
					cont=new GUIContent(" - Flanked Crit-Bonus:", "Critical bonus gained when attacking a unit that is not in cover\nOnly valid when cover system is enabled");
					if(gsDB.enableCoverSystem) gsDB.coverCritBonus=EditorGUILayout.FloatField(cont, gsDB.coverCritBonus);
					else EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					
				EditorGUILayout.Space();
					
					cont=new GUIContent("Auto End Turn:", "Check to automatically end player turn when all move has been depleted");
					gsDB.autoEndTurn=EditorGUILayout.Toggle(cont, gsDB.autoEndTurn);
					
					cont=new GUIContent("Enable Move After Attack:", "Enable unit to move after performing an attack");
					gsDB.endMoveAfterAttack=EditorGUILayout.Toggle(cont, gsDB.endMoveAfterAttack);
					
					cont=new GUIContent("Enable Counter-Attack:", "Enable unit to perform counter-attack when attacked");
					gsDB.enableCounterAttack=EditorGUILayout.Toggle(cont, gsDB.enableCounterAttack);
				
				EditorGUILayout.Space();

					cont=new GUIContent("Restore AP on New Turn:", "Fully restore unit Action-Point on new turn");
					gsDB.restoreAPOnTurn=EditorGUILayout.Toggle(cont, gsDB.restoreAPOnTurn);
					
					cont=new GUIContent("Consume AP when Move:", "Unit will use Action-Point to move");
					gsDB.useAPToMove=EditorGUILayout.Toggle(cont, gsDB.useAPToMove);
					
					cont=new GUIContent(" - AP Cost Per Move:", "Action-Point cost to start moving");
					if(!gsDB.useAPToMove) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else gsDB.apPerMove=EditorGUILayout.IntField(cont, gsDB.apPerMove);
					
					//cont=new GUIContent(" - AP Cost Per Node:", "Action-Point cost per node when moving");
					//if(!gsDB.useAPToMove) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					//else gsDB.apPerNode=EditorGUILayout.IntField(cont, gsDB.apPerNode);
					
					cont=new GUIContent("Consume AP when Attack:", "Unit will use Action-Point for each attack");
					gsDB.useAPToAttack=EditorGUILayout.Toggle(cont, gsDB.useAPToAttack);
					
					cont=new GUIContent(" - AP Cost Per Attack:", "Action-Point cost for each attack");
					if(!gsDB.useAPToAttack) EditorGUILayout.LabelField(cont, new GUIContent("n/a"));
					else gsDB.apPerAttack=EditorGUILayout.IntField(cont, gsDB.apPerAttack);
				
				if(GUI.changed) EditorUtility.SetDirty(gsDB);
			}
			
			EditorGUIUtility.labelWidth=0;
			EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			GameControl.inspector=DefaultInspector(GameControl.inspector);
		}
		
	}
	
}
