using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class AbilityEditorWindow : TBEditorWindow {
		
		[MenuItem ("Tools/TBTK/AbilityEditor", false, 10)]
		static void OpenAbilityEditor () { Init(); }
		
		private static AbilityEditorWindow window;
		
		public static void Init (int prefabID=-1) {
			window = (AbilityEditorWindow)EditorWindow.GetWindow(typeof (AbilityEditorWindow), false, "AbilityEditor");
			window.minSize=new Vector2(420, 300);
			
			TBE.Init();
			
			window.InitLabel();
			
			//if(prefabID>=0) window.selectID=AbilityUDB.GetPrefabIndex(prefabID);
			
			if(prefabID>=0){
				window.selectID=AbilityUDB.GetPrefabIndex(prefabID);
				window.newSelectID=window.selectID;
				window.newSelectDelay=1;
			}
			
			window._SelectItem();
		}
		
		
		private static string[] abTypeLabel;
		private static string[] abTypeTooltip;
		
		private static string[] tgtTypeLabel;
		private static string[] tgtTypeTooltip;
		
		private static string[] impactLabel;
		private static string[] impactTooltip;
		
		public void InitLabel(){
			int enumLength = Enum.GetValues(typeof(Ability._AbilityType)).Length;
			abTypeLabel=new string[enumLength];		abTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){	
				Ability._AbilityType type=(Ability._AbilityType)i;
				abTypeLabel[i]=type.ToString();
				if(type==Ability._AbilityType.Generic)		abTypeTooltip[i]="Generic Ability";
				if(type==Ability._AbilityType.SpawnUnit)	abTypeTooltip[i]="The ability will spawn a new unit on a specific node on the grid";
				if(type==Ability._AbilityType.Teleport)	abTypeTooltip[i]="Unit Only - The ability will teleport a specific unit to a specific node on the grid";
				if(type==Ability._AbilityType.Charge)		abTypeTooltip[i]="Unit Only - The ability enable unit to target a specific direction and move along it.\nThe ability will still applies the generic effect towards the valid unit at the end of the path";
				if(type==Ability._AbilityType.Line)			abTypeTooltip[i]="Unit Only - The ability enable unit to target and attack a specific direction.\nThe ability will still applies the generic effect towards the all the valid unit along that direction";
				if(type==Ability._AbilityType.Cone)		abTypeTooltip[i]="Unit Only - The ability enable unit to target and attack a conical shape area.\nThe ability will still applies the generic effect towards the all the valid unit along that direction";
				if(type==Ability._AbilityType.ScanFogOfWar)			
																		abTypeTooltip[i]="The ability will force the target node to reveal its fog-of-war";
				if(type==Ability._AbilityType.DeployBlock) 
																		abTypeTooltip[i]="The ability will deploy a block to a specific node on the grid (blocking the node)";
				if(type==Ability._AbilityType.None) 		abTypeTooltip[i]="The ability doesn't do anything\nThis is for custom ability where you can add your own script in any of the spawned object when the ability is used";
			}
			
			enumLength = Enum.GetValues(typeof(Ability._TargetType)).Length;
			tgtTypeLabel=new string[enumLength];		tgtTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){	
				Ability._TargetType type=(Ability._TargetType)i;
				tgtTypeLabel[i]=type.ToString();
				if(type==Ability._TargetType.AllNode)		tgtTypeTooltip[i]="Target any node";
				if(type==Ability._TargetType.AllUnit)			tgtTypeTooltip[i]="Target node with unit only";
				if(type==Ability._TargetType.HostileUnit)	tgtTypeTooltip[i]="Target node with hostile unit only";
				if(type==Ability._TargetType.FriendlyUnit)	tgtTypeTooltip[i]="Target node with friendly unit only";
				if(type==Ability._TargetType.EmptyNode)	tgtTypeTooltip[i]="Target empty node only";
			}
			
			enumLength = Enum.GetValues(typeof(Ability._ImpactType)).Length;
			impactLabel=new string[enumLength];		impactTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){	
				Ability._ImpactType type=(Ability._ImpactType)i;
				impactLabel[i]=type.ToString();
				if(type==Ability._ImpactType.None)			impactTooltip[i]="No immediate effect on hit/impact";
				if(type==Ability._ImpactType.Negative)		impactTooltip[i]="Negative effect on hit/impact";
				if(type==Ability._ImpactType.Positive)		impactTooltip[i]="Positive effect on hit/impact";
			}
		}
		
		public static bool editUnitAbility=true;
		
		public void OnGUI(){
			TBE.InitGUIStyle();
			
			if(!CheckIsPlaying()) return;
			if(window==null) Init();
			
			string txt=editUnitAbility ? "Editing Unit" : "Editing Faction" ;
			if(GUI.Button(new Rect(305, 5, 120, 25), txt)) editUnitAbility=!editUnitAbility;
			
			List<Ability> abilityList=editUnitAbility ? AbilityUDB.GetList() : AbilityFDB.GetList() ;
			
			Undo.RecordObject(this, "window");
			Undo.RecordObject(EffectDB.GetDB(), "abilityDB");
			
			if(GUI.Button(new Rect(Math.Max(260, window.position.width-120), 5, 100, 25), "Save")){
				AbilityUDB.UpdateLabel();
				AbilityFDB.UpdateLabel();
				
				GUI.FocusControl(null);
				TBE.SetDirty();
			}
			
			if(GUI.Button(new Rect(5, 5, 120, 25), "Create New")) Select(NewItem());
			if(abilityList.Count>0 && GUI.Button(new Rect(130, 5, 100, 25), "Clone Selected")) Select(NewItem(selectID));
			
			float startX=5;	float startY=55;
			
			if(minimiseList){ if(GUI.Button(new Rect(startX, startY-20, 30, 18), ">>")) minimiseList=false; }
			else{ if(GUI.Button(new Rect(startX, startY-20, 30, 18), "<<")) minimiseList=true; }
			
			Vector2 v2=DrawAbilityList(startX, startY, abilityList);
			startX=v2.x+25;
			
			if(abilityList.Count==0) return;
			
			if(newSelectDelay>0){
				newSelectDelay-=1;		GUI.FocusControl(null);
				if(newSelectDelay==0) _SelectItem();
				else Repaint();
			}
			
			if(selectID>=abilityList.Count) return;
			
			Rect visibleRect=new Rect(startX, startY, window.position.width-startX, window.position.height-startY);
			Rect contentRect=new Rect(startX, startY, contentWidth, contentHeight);
			
			scrollPos = GUI.BeginScrollView(visibleRect, scrollPos, contentRect);
			
				v2=DrawAbilityConfigurator(startX, startY, abilityList[selectID]);
				contentWidth=v2.x-startX;
				contentHeight=v2.y-55;
			
			GUI.EndScrollView();
			
			if(GUI.changed) TBE.SetDirty();
		}
		
		
		private bool foldSetting=true;
		private bool foldCost=true;
		private bool foldVisual=true;
		private bool foldAttributesOnImpact=true;
		
		private string txt="";
		
		private Vector2 DrawAbilityConfigurator(float startX, float startY, Ability item){
			float maxX=startX;
			
				startY=TBE.DrawBasicInfo(startX, startY, item);
			
				int abType=(int)item.type;		contL=TBE.SetupContL(abTypeLabel, abTypeTooltip);		int initialType=abType;
				TBE.Label(startX, startY+=spaceY, width, height, "Effect Type:", "", TBE.headerS);
				abType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), abType, contL);
				item.type=(Ability._AbilityType)abType;
			
				if(!editUnitAbility){
					bool revert=false;
					if(item.type==Ability._AbilityType.Teleport)	revert=true;
					if(item.type==Ability._AbilityType.Charge)		revert=true;
					if(item.type==Ability._AbilityType.Line)			revert=true;
					if(item.type==Ability._AbilityType.Cone)		revert=true;
					
					if(revert){
						Debug.LogWarning(item.type+" type is not supported by faction ability");
						item.type=(Ability._AbilityType)initialType;
					}
				}
			
			startY+=spaceY*0.5f;
			
				foldSetting=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldSetting, "Settings", TBE.foldoutS);
				if(foldSetting){
					startX+=10;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Cooldown:", "How many turn before the ability can be used again once it's used");
					item.cooldown=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.cooldown);
					
					TBE.Label(startX, startY+=spaceY, width, height, "Use Limit:", "How many time the ability can be used in a single game");
					GUI.color=(item.useLimit>0) ? Color.white : Color.grey ;
					item.useLimit=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.useLimit);	GUI.color=Color.white;
					
					//startY+=spaceY*0.5f;
					
					if(editUnitAbility){
						foldCost=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldCost, "Activation Cost");
						if(foldCost){
							startX+=10;
							
							TBE.Label(startX, startY+=spaceY, width, height, "move Cost:", "The cost of using the ability that counts towards movement limit ");
							item.moveCost=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.moveCost);
						
							TBE.Label(startX, startY+=spaceY, width, height, "attack Cost:", "The cost of using the ability that counts towards attack limit");
							item.attackCost=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.attackCost);
							
							TBE.Label(startX, startY+=spaceY, width, height, "Ability Cost:", "The cost of using the ability that counts towards ability limit");
							item.abilityCost=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.abilityCost);
						
							TBE.Label(startX, startY+=spaceY, width, height, "AP Cost:", "The action-point cost of using the ability");
							item.apCost=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.apCost);
							
							TBE.Label(startX, startY+=spaceY, width, height, "End All Action:", "Check to end all action available to the unit once the ability is used");
							item.endAllActionAfterUse=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.endAllActionAfterUse);
							
							startX-=10;	
						}
					}
					
					startY+=spaceY*0.5f;
					
					Ability._TargetType cachedTgtType=item.targetType;
					if(!editUnitAbility && !item.requireTarget){
						if(cachedTgtType==Ability._TargetType.AllNode || cachedTgtType==Ability._TargetType.EmptyNode){ 
							item.targetType=Ability._TargetType.AllUnit;
							cachedTgtType=Ability._TargetType.AllUnit;
						}
					}
					
					int tgtType=(int)item.targetType;		contL=TBE.SetupContL(tgtTypeLabel, tgtTypeTooltip);
					TBE.Label(startX, startY+=spaceY, width, height, "Target Type:", "", TBE.headerS);
					tgtType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), tgtType, contL);
					item.targetType=(Ability._TargetType)tgtType;
					
					if(!editUnitAbility && !item.requireTarget){
						if(item.targetType==Ability._TargetType.AllNode || cachedTgtType==Ability._TargetType.EmptyNode){
							Debug.Log("target type not supported for faction ability that doesn't require target input");
							item.targetType=cachedTgtType;
						}
					}
					
					TBE.Label(startX, startY+=spaceY, width, height, "Require Target:", "Check if the ability will require the player to actively select a target\nIf left uncheck, as unity-ability the ability will target the source unit, as faction ability the ability will target will valid target on the grid");
					item.requireTarget=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.requireTarget);
					
					if(editUnitAbility){
						TBE.Label(startX, startY+=spaceY, width, height, "Require LOS:", "Check if the ability require the target to be in line-of-sight");
						if(!item.requireTarget) TBE.Label(startX+spaceX, startY, widthS, height, "n/a");
						else item.requireLos=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.requireLos);
					}
				
					if(editUnitAbility){
						TBE.Label(startX, startY+=spaceY, width, height, "Range (min/max):", "The effective range of the ability");
						if(!item.requireTarget) TBE.Label(startX+spaceX, startY, widthS, height, "n/a");
						else{
							GUI.color=item.rangeMin>1 ? Color.white : Color.grey ;
							item.rangeMin=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.rangeMin); GUI.color=Color.white;
							item.range=EditorGUI.DelayedIntField(new Rect(startX+spaceX+widthS, startY, widthS, height), item.range);
						}
					}
					
					bool typeSupportAOE=!(item.type==Ability._AbilityType.Teleport || item.type==Ability._AbilityType.SpawnUnit || item.type==Ability._AbilityType.DeployBlock || item.type==Ability._AbilityType.Cone);
					TBE.Label(startX, startY+=spaceY, width, height, "AOE Range:", "The effective area-of-effect (AOE) range of the ability at the target node");
					if((!editUnitAbility && !item.requireTarget) || !typeSupportAOE) TBE.Label(startX+spaceX, startY, widthS, height, "n/a", "");
					else{
						GUI.color=(item.aoeRange>0) ? Color.white : Color.grey ;
						item.aoeRange=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.aoeRange);	GUI.color= Color.white;
					}
					
					
					if(item.type==Ability._AbilityType.Cone){
						TBE.Label(startX, startY+=spaceY, width, height, "FOV:", "The Field-Of-View of the ability in angle. Determine how wide the cone is");
						item.fov=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.fov);
					}
					
					
					startY+=spaceY*0.5f;
					
					if(editUnitAbility){
						TBE.Label(startX, startY+=spaceY, width, height, "Use Attack Sequence:", "Check to have the unit using the ability runs a standard attack sequence at the target (the unit will aim towards the target and fire a shoot-object)");
						if(!item.requireTarget) TBE.Label(startX+spaceX+10, startY, widthS, height, "n/a");
						else item.useAttackSequence=EditorGUI.Toggle(new Rect(startX+spaceX+10, startY, widthS, height), item.useAttackSequence);
						
						TBE.Label(startX, startY+=spaceY, width, height, "Aim At Target Unit:", "Check to have the unit aim at the unit (if there's one) when using the ability\nOtherwise the ability will aim at the node");
						if(!item.requireTarget || !item.useAttackSequence || item.type==Ability._AbilityType.Line ||  item.type==Ability._AbilityType.Cone) TBE.Label(startX+spaceX+10, startY, widthS, height, "-");
						else item.aimAtUnit=EditorGUI.Toggle(new Rect(startX+spaceX+10, startY, widthS, height), item.aimAtUnit);
						
						TBE.Label(startX, startY+=spaceY, width, height, "Shoot Object:", "OPTIONAL: The alternate shoot-object to use for the ability\nIf left unassgined, the unit will use its default shoot-object");
						if(!item.requireTarget || !item.useAttackSequence) TBE.Label(startX+spaceX+10, startY, width, height, "-");
						else item.shootObject=(ShootObject)EditorGUI.ObjectField(new Rect(startX+spaceX+10, startY, width-10, height), item.shootObject, typeof(ShootObject), true);
					
						//startY+=10;
					}
					
					startX-=10;
				}
			
			startY+=spaceY*0.5f;
			
				foldAttributesOnImpact=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldAttributesOnImpact, "Attributes On Impact", TBE.foldoutS);
				if(foldAttributesOnImpact){
					startX+=10;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Impact Delay:", "The delay in second before any effect is actually being applied to the target\nThis is for any visual effect (if there's any) to play out");
					item.impactDelay=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.impactDelay);
					
					if(item.type==Ability._AbilityType.None){
						
					}
					if(item.type==Ability._AbilityType.SpawnUnit){
						item.requireTarget=true;
						item.targetType=Ability._TargetType.EmptyNode;
						item.useAttackSequence=false;
						
						//GUIStyle styleSO=item.spawnUnitPrefab==null ? TBE.conflictS : null;
						//TBE.Label(startX, startY+=spaceY, width, height, "Spawn Unit:", "", styleSO);
						//item.spawnUnitPrefab=(Unit)EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), item.spawnUnitPrefab, typeof(Unit), true);
						
						TBE.Label(startX, startY+=spaceY, width, height, "Unit To Spawn:", "The unit prefab to spawn when the ability is used");
						int unitIdx=UnitDB.GetPrefabIndex(item.spawnUnitPrefab);
						unitIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), unitIdx, UnitDB.label);
						item.spawnUnitPrefab=unitIdx>=0 ? UnitDB.GetItem(unitIdx) : null;
						
						if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.spawnUnitPrefab=null;
					}
					if(item.type==Ability._AbilityType.DeployBlock){
						item.requireTarget=true;
						item.targetType=Ability._TargetType.EmptyNode;
						item.useAttackSequence=false;
						
						TBE.Label(startX, startY+=spaceY, width, height, "Block Prefab:", "The block prefab to spawn when the ability is used");
						item.obstaclePrefab = (GameObject)EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), item.obstaclePrefab, typeof(GameObject), true);
						
						if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.spawnUnitPrefab=null;
					}
					if(item.type==Ability._AbilityType.Teleport){
						item.requireTarget=true;
						item.targetType=Ability._TargetType.EmptyNode;
						item.useAttackSequence=false;
					}
					if(item.type==Ability._AbilityType.ScanFogOfWar){
						item.targetType=Ability._TargetType.AllNode;
						
						TBE.Label(startX, startY+=spaceY, width, height, "Duration:", "The number of turn in which the fog-of-war will be revealed");
						if(!item.switchFaction) TBE.Label(startX+spaceX, startY, widthS, height, "-", "");
						else item.duration=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.duration);
					}
					if(item.type==Ability._AbilityType.Generic || item.type==Ability._AbilityType.Charge || item.type==Ability._AbilityType.Line ||  item.type==Ability._AbilityType.Cone){
						startY+=spaceY*0.5f;
						
						if(item.type==Ability._AbilityType.Charge){
							item.requireTarget=true;
						}
						else if(item.type==Ability._AbilityType.Line || item.type==Ability._AbilityType.Cone){
							item.requireTarget=true;
						}
						
						
						int impactType=(int)item.impactType;		contL=TBE.SetupContL(impactLabel, impactTooltip);
						TBE.Label(startX, startY+=spaceY, width, height, "Impact Type:", "");
						impactType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), impactType, contL);
						item.impactType=(Ability._ImpactType)impactType;
						
						if(item.impactType!=Ability._ImpactType.None){
							TBE.Label(startX, startY+=spaceY, width, height, "Damage Type:", "");
							if(item.impactType==Ability._ImpactType.Negative){
								item.damageType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), item.damageType, TBE.GetDamageLabel());
							}
							else TBE.Label(startX+spaceX, startY, width, height, "-");
							
							
							TBE.Label(startX, startY+=spaceY, width, height, "HP Modifier Min/Max:", "The immediate change applied to the target's hit-point upon the ability impact/hit");
							GUI.color=(item.hpModifierMin!=0 || item.hpModifierMax!=0) ? Color.white : Color.grey ;
							item.hpModifierMin=EditorGUI.DelayedIntField(new Rect(startX+spaceX+3, startY, widthS, height), item.hpModifierMin);
							item.hpModifierMax=EditorGUI.DelayedIntField(new Rect(startX+spaceX+widthS+5, startY, widthS, height), item.hpModifierMax);	GUI.color=Color.white;
							
							TBE.Label(startX, startY+=spaceY, width, height, "AP Modifier Min/Max:", "The immediate change applied to the target's action-point upon the ability impact/hit");
							GUI.color=(item.apModifierMin!=0 || item.apModifierMax!=0) ? Color.white : Color.grey ;
							item.apModifierMin=EditorGUI.DelayedIntField(new Rect(startX+spaceX+3, startY, widthS, height), item.apModifierMin);
							item.apModifierMax=EditorGUI.DelayedIntField(new Rect(startX+spaceX+widthS+5, startY, widthS, height), item.apModifierMax);	GUI.color=Color.white;
							
							startY+=spaceY*0.5f;
							
							if(item.impactType==Ability._ImpactType.Negative){
								TBE.Label(startX, startY+=spaceY, width, height, "Factor Target Stats:", "Check to factor in target stats (defend, dodge, etc.) to calculate the damage");
								item.factorInTargetStats=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.factorInTargetStats);
								
								TBE.Label(startX, startY+=spaceY, width, height, " - Attack:", "The attack value of the ability\nUsed along with the target's defense to modify the effective damage"+TBE.AttackTT());
								if(!item.factorInTargetStats) TBE.Label(startX+spaceX, startY, widthS, height, "-");
								else item.attack=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attack);
								
								TBE.Label(startX, startY+=spaceY, width, height, " - Hit Chance:", "The chance for the ability to hit\nThis will be negate by the target's dodge chance"+TBE.ChanceTT());
								if(!item.factorInTargetStats) TBE.Label(startX+spaceX, startY, widthS, height, "-");
								else item.hitChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hitChance);
								
								TBE.Label(startX, startY+=spaceY, width, height, " - Crit. Chance:", "The chance for the ability to score a critical hit\nThis will be negate by the target's critical reduction chance"+TBE.ChanceTT());
								if(!item.factorInTargetStats) TBE.Label(startX+spaceX, startY, widthS, height, "-");
								else item.critChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critChance);
								
								TBE.Label(startX, startY+=spaceY, width, height, " - Crit. Multiplier:", "The damage multiplier to use when the ability score a critical hit"+TBE.ChanceTT());
								if(!item.factorInTargetStats) TBE.Label(startX+spaceX, startY, widthS, height, "-");
								else item.critMultiplier=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critMultiplier);
							}
						}
						
						startY+=spaceY*0.5f;
						
						TBE.Label(startX, startY+=spaceY, width, height, "Effect Hit Chance:", "The chance of any effect item being applied\nApplies to ClearAllEffect and SwitchFaction too"+TBE.ChanceTT());
						GUI.color=(item.effHitChance>0) ? Color.white : Color.grey ;
						item.effHitChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.effHitChance);
						
						TBE.Label(startX, startY+=spaceY, width, height, "Effect On Hit:", "The effect to be applied to the target when the ability hit");
						for(int i=0; i<item.effectIDList.Count; i++){
							TBE.Label(startX+spaceX-height, startY, width, height, "-");
							
							int effIdx=EffectDB.GetPrefabIndex(item.effectIDList[i]);
							effIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), effIdx, EffectDB.label);
							if(effIdx>=0) item.effectIDList[i]=EffectDB.GetItemID(effIdx);
						
							if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.effectIDList.RemoveAt(i); 
							
							startY+=spaceY;
						}
						
						int newEffID=-1;
						newEffID = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newEffID, EffectDB.label);
						if(newEffID>=0) newEffID=EffectDB.GetItemID(newEffID);
						if(newEffID>=0 && !item.effectIDList.Contains(newEffID)) item.effectIDList.Add(newEffID);
						
						TBE.Label(startX, startY+=spaceY, width, height, "Clear All Effect:", "Check to have the ability clear all the active effects on the target");
						item.clearAllEffect=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.clearAllEffect);
						
						startY+=spaceY*0.5f;
						
						TBE.Label(startX, startY+=spaceY, width, height, "Switch Faction:", "Check to have the ability switch the target faction");
						item.switchFaction=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.switchFaction);
						
						TBE.Label(startX, startY+=spaceY, width, height, " - Duration:", "The duration in turn in which the target unit's faction is switched");
						if(!item.switchFaction) TBE.Label(startX+spaceX, startY, widthS, height, "n/a", "");
						else item.duration=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.duration);
						
						TBE.Label(startX, startY+=spaceY, width, height, " - Controllable:", "Check to let the player take control of the unit when the target is switched");
						if(!item.switchFaction) TBE.Label(startX+spaceX, startY, widthS, height, "n/a", "");
						else item.switchFacControllable=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.switchFacControllable);
						
						GUI.color=Color.white;
					}
					
					startX-=10;
				}
				
			startY+=spaceY*0.5f;
				
				foldVisual=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldVisual, "Visual & Audio Setting", TBE.foldoutS);
				if(foldVisual){
					startX+=10;
					
					if(editUnitAbility){
						txt="OPTIONAL: The effect object to spawn when the ability is activated\nYou can also add custom script on this object to have your own custom ability effect";
						startY=DrawVisualObject(startX, startY+=spaceY, item.effectOnUse, "Effect On Use:", txt);
					}
					
					txt="OPTIONAL: The effect object to spawn when on each individual target of the ability\nYou can also add custom script on this object to have your own custom ability effect";
					startY=DrawVisualObject(startX, startY+=spaceY, item.effectOnHit, "Effect On Hit:", txt);
					
					TBE.Label(startX, startY+=spaceY*1.5f, width, height, "Activation Sound:", "OPTIONAL: Audio clip to play when the ability is activated");
					item.activateSound=(AudioClip)EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), item.activateSound, typeof(AudioClip), true);
					
					startX-=10;
				}
				
			
			startY+=spaceY*2;
			
				GUIStyle style=new GUIStyle("TextArea");	style.wordWrap=true;
				cont=new GUIContent("Ability description (for runtime and editor): ", "");
				EditorGUI.LabelField(new Rect(startX, startY, 400, height), cont);
				item.desp=EditorGUI.DelayedTextField(new Rect(startX, startY+spaceY-3, 270, 150), item.desp, style);
			
			return new Vector2(maxX, startY+170);
		}
		
		
		
		
		protected Vector2 DrawAbilityList(float startX, float startY, List<Ability> abilityList){
			List<EItem> list=new List<EItem>();
			for(int i=0; i<abilityList.Count; i++){
				EItem item=new EItem(abilityList[i].prefabID, abilityList[i].name, abilityList[i].icon);
				list.Add(item);
			}
			return DrawList(startX, startY, window.position.width, window.position.height, list);
		}
		
		public static int NewItem(int idx=-1){ return window._NewItem(idx); }
		private int _NewItem(int idx=-1){
			Ability item=null;
			if(idx<0) item=new Ability(); 
			
			if(editUnitAbility){
				if(idx>=0) item=AbilityUDB.GetList()[idx].Clone();
				
				item.prefabID=TBE.GenerateNewID(AbilityUDB.GetPrefabIDList());
				
				AbilityUDB.GetList().Add(item);
				AbilityUDB.UpdateLabel();
				
				return AbilityUDB.GetList().Count-1;
			}
			else{
				if(idx>=0) item=AbilityFDB.GetList()[idx].Clone();
				
				item.prefabID=TBE.GenerateNewID(AbilityFDB.GetPrefabIDList());
				
				AbilityFDB.GetList().Add(item);
				AbilityFDB.UpdateLabel();
				
				return AbilityFDB.GetList().Count-1;
			}
		}
		
		protected override void DeleteItem(){
			if(editUnitAbility){
				AbilityUDB.GetList().RemoveAt(deleteID);
				AbilityUDB.UpdateLabel();
			}
			else{
				AbilityFDB.GetList().RemoveAt(deleteID);
				AbilityFDB.UpdateLabel();
			}
		}
		
		protected override void SelectItem(){ }
		private void _SelectItem(){ 
			selectID=newSelectID;
			
			if(editUnitAbility){
				if(AbilityUDB.GetList().Count<=0) return;
				selectID=Mathf.Clamp(selectID, 0, AbilityUDB.GetList().Count-1);
			}
			else{
				if(AbilityFDB.GetList().Count<=0) return;
				selectID=Mathf.Clamp(selectID, 0, AbilityFDB.GetList().Count-1);
			}
			
			Repaint();
		}
		//~ protected override void SelectItem(){ SelectItem(selectID); }
		//~ private void SelectItem(int newID){ 
			//~ selectID=newID;
			
			//~ if(editUnitAbility){
				//~ if(AbilityUDB.GetList().Count<=0) return;
				//~ selectID=Mathf.Clamp(selectID, 0, AbilityUDB.GetList().Count-1);
			//~ }
			//~ else{
				//~ if(AbilityFDB.GetList().Count<=0) return;
				//~ selectID=Mathf.Clamp(selectID, 0, AbilityFDB.GetList().Count-1);
			//~ }
		//~ }
		
		protected override void ShiftItemUp(){ 	if(selectID>0) ShiftItem(-1); }
		protected override void ShiftItemDown(){ if(selectID<(editUnitAbility ? AbilityUDB.GetList().Count : AbilityFDB.GetList().Count)-1) ShiftItem(1); }
		private void ShiftItem(int dir){
			if(editUnitAbility){
				Ability item=AbilityUDB.GetList()[selectID];
				AbilityUDB.GetList()[selectID]=AbilityUDB.GetList()[selectID+dir];
				AbilityUDB.GetList()[selectID+dir]=item;
			}
			else{
				Ability item=AbilityFDB.GetList()[selectID];
				AbilityFDB.GetList()[selectID]=AbilityFDB.GetList()[selectID+dir];
				AbilityFDB.GetList()[selectID+dir]=item;
			}
			selectID+=dir;
		}
		
		
	}
	
}
