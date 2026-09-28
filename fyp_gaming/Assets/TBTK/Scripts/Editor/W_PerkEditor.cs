using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class PerkEditorWindow : TBEditorWindow {
		
		[MenuItem ("Tools/TBTK/PerkEditor", false, 10)]
		static void OpenPerkEditor () { Init(); }
		
		private static PerkEditorWindow window;
		
		public static void Init (int prefabID=-1) {
			window = (PerkEditorWindow)EditorWindow.GetWindow(typeof (PerkEditorWindow), false, "PerkEditor");
			window.minSize=new Vector2(420, 300);
			
			TBE.Init();
			
			window.InitLabel();
			
			//if(prefabID>=0) window.selectID=PerkDB.GetPrefabIndex(prefabID);
			
			if(prefabID>=0){
				window.selectID=PerkDB.GetPrefabIndex(prefabID);
				window.newSelectID=window.selectID;
				window.newSelectDelay=1;
			}
			
			window._SelectItem();
		}
		
		
		private static string[] typeLabel;
		private static string[] typeTooltip;
		
		private static string[] statsTypeLabel;
		private static string[] statsTypeTooltip;
		
		private static string[] effModTypeLabel;
		private static string[] effModTypeTooltip;
		
		public void InitLabel(){
			int enumLength = Enum.GetValues(typeof(_PerkType)).Length;
			typeLabel=new string[enumLength];		typeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){	
				_PerkType type=(_PerkType)i;
				typeLabel[i]=type.ToString();
				if(type==_PerkType.NewUnitAbility)			typeTooltip[i]="Add new ability to unit";
				if(type==_PerkType.NewFactionAbility)		typeTooltip[i]="Add new ability to player faction";
				if(type==_PerkType.ModifyUnit)				typeTooltip[i]="Modify unit";
				if(type==_PerkType.ModifyUnitAbility)		typeTooltip[i]="Modify unit ability";
				if(type==_PerkType.ModifyFactionAbility)	typeTooltip[i]="Modify faction ability";
				if(type==_PerkType.ModifyEffect)				typeTooltip[i]="Modify effect";
			}
			
			enumLength = Enum.GetValues(typeof(Perk._StatsType)).Length;
			statsTypeLabel=new string[enumLength];
			statsTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				statsTypeLabel[i]=((Perk._StatsType)i).ToString();
				if((Perk._StatsType)i==Perk._StatsType.Modifier) statsTypeTooltip[i]="The value in the perk will be directly added to the target";
				if((Perk._StatsType)i==Perk._StatsType.Multiplier) statsTypeTooltip[i]="The value in the effect will be be used to multiply the target";
			}
			
			enumLength = Enum.GetValues(typeof(Perk._EffModType)).Length;
			effModTypeLabel=new string[enumLength];
			effModTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				effModTypeLabel[i]=((Perk._EffModType)i).ToString();
				if((Perk._EffModType)i==Perk._EffModType.Append) effModTypeTooltip[i]="Add the new effect(s) to target's existing effect list";
				if((Perk._EffModType)i==Perk._EffModType.Replace) effModTypeTooltip[i]="Replace the entire target's existing effect list with specified effects";
			}
		}
		
		
		public void OnGUI(){
			TBE.InitGUIStyle();
			
			if(!CheckIsPlaying()) return;
			if(window==null) Init();
			
			List<Perk> perkList=PerkDB.GetList();
			
			Undo.RecordObject(this, "window");
			Undo.RecordObject(PerkDB.GetDB(), "perkDB");
			
			if(GUI.Button(new Rect(Math.Max(260, window.position.width-120), 5, 100, 25), "Save")){
				PerkDB.UpdateLabel();
				
				GUI.FocusControl(null);
				TBE.SetDirty();
			}
			
			if(GUI.Button(new Rect(5, 5, 120, 25), "Create New")) Select(NewItem());
			if(perkList.Count>0 && GUI.Button(new Rect(130, 5, 100, 25), "Clone Selected")) Select(NewItem(selectID));
			
			
			float startX=5;	float startY=55;
			
			if(minimiseList){ if(GUI.Button(new Rect(startX, startY-20, 30, 18), ">>")) minimiseList=false; }
			else{ if(GUI.Button(new Rect(startX, startY-20, 30, 18), "<<")) minimiseList=true; }
			
			Vector2 v2=DrawPerkList(startX, startY, perkList);
			startX=v2.x+25;
			
			if(perkList.Count==0) return;
			
			if(newSelectDelay>0){
				newSelectDelay-=1;		GUI.FocusControl(null);
				if(newSelectDelay==0) _SelectItem();
				else Repaint();
			}
			
			if(selectID>=perkList.Count) return;
			
			
			Rect visibleRect=new Rect(startX, startY, window.position.width-startX, window.position.height-startY);
			Rect contentRect=new Rect(startX, startY, contentWidth, contentHeight);
			
			scrollPos = GUI.BeginScrollView(visibleRect, scrollPos, contentRect);
			
				v2=DrawPerkConfigurator(startX, startY, perkList[selectID]);
				contentWidth=v2.x-startX;
				contentHeight=v2.y-55;
			
			GUI.EndScrollView();
			
			if(GUI.changed) TBE.SetDirty();
		}
		
		
		private bool foldBasic;
		private bool foldType;
		private bool foldStats;
		
		private Vector2 DrawPerkConfigurator(float startX, float startY, Perk item){
			float maxX=startX;
			
				startY=TBE.DrawBasicInfo(startX, startY, item);
			
			//~ spaceX+=12;
			
			foldBasic=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldBasic, "Basic Setting", TBE.foldoutS);
			if(foldBasic){
				startX+=10;
				
				TBE.Label(startX, startY+=spaceY, width, height, "Cost:", "The cost to unlock the perk\nThe resource in question is the variable 'currency' in PerkManager");
				item.cost=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.cost);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Min. Perk Point:", "Minimum number of perk required to be unlocked before the perk becomes available");
				item.minPerkPoint=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.minPerkPoint);
				
				//TBE.Label(startX, startY+=spaceY, width, height, "Repeatable:", "");
				//item.repeatable=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.repeatable);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Prereq Perk:", "Perk(s) required to be unlocked before the perk becomes available");
				for(int i=0; i<item.prereq.Count; i++){
					TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
					
					int index=PerkDB.GetPrefabIndex(item.prereq[i]);
					index=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), index, PerkDB.label);
					int prefabID=PerkDB.GetItem(index).prefabID;
					if(prefabID!=item.prefabID && !item.prereq.Contains(prefabID)) item.prereq[i]=prefabID;
					
					if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.prereq.RemoveAt(i);
				}
				
				if(item.prereq.Count<PerkDB.GetCount()-1){
					int newIdx=-1;		CheckColor(item.prereq.Count, 0);
					startY+=item.prereq.Count>0 ? spaceY : 0 ;
					newIdx=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, PerkDB.label);
					if(newIdx>=0){
						int newPID=PerkDB.GetItemID(newIdx);
						if(newPID!=item.prefabID && !item.prereq.Contains(newPID)) item.prereq.Add(newPID);
					}
					ResetColor();
				}
				
				startX-=10;
			}
			
			
			startY+=spaceY*0.5f;
			
			
			foldType=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldType, "Type and Corresponding Parameters", TBE.foldoutS);
			if(foldType){
				startX+=10;
				
				startY+=spaceY*0.5f;
				
				int type=(int)item.type;		contL=TBE.SetupContL(typeLabel, typeTooltip);
				TBE.Label(startX, startY+=spaceY, width, height, "Perk Type:", "", TBE.headerS);
				type = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), type, contL);
				item.type=(_PerkType)type;
				
				startY+=spaceY*0.5f;
				
				item.VerifyItemPIDList();
				
				if(item.type==_PerkType.NewUnitAbility){
					TBE.Label(startX, startY+=spaceY, width, height, "Ability To Add:", "");
					int abIdx=AbilityUDB.GetPrefabIndex(item.abilityPID);
					abIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), abIdx, AbilityUDB.label);
					if(abIdx>0) item.abilityPID=AbilityUDB.GetItemID(abIdx);
					
					startY+=spaceY*0.5f;
					
					startY=DrawItemPIDList_Unit(startX, startY, item);
				}
				else if(item.type==_PerkType.NewFactionAbility){
					TBE.Label(startX, startY+=spaceY, width, height, "Ability To Add:", "");
					int abIdx=AbilityFDB.GetPrefabIndex(item.abilityPID);
					abIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), abIdx, AbilityFDB.label);
					if(abIdx>0) item.abilityPID=AbilityFDB.GetItemID(abIdx);
				}
				else if(item.type==_PerkType.ModifyUnit){
					startY=DrawItemPIDList_Unit(startX, startY, item);
				}
				else if(item.type==_PerkType.ModifyUnitAbility){
					startY=DrawItemPIDList_Ability(startX, startY, item, true);
				}
				else if(item.type==_PerkType.ModifyFactionAbility){
					startY=DrawItemPIDList_Ability(startX, startY, item, false);
				}
				else if(item.type==_PerkType.ModifyEffect){
					startY=DrawItemPIDList_Effect(startX, startY, item);
				}
				
				startX-=10;
			}
			
			
			startY+=spaceY*0.5f;
			
			
			foldStats=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldStats, "Stats", TBE.foldoutS);
			if(foldStats){
				startX+=10;
				
				if(item.UseStats()) startY=DrawStats(startX, startY, item);
				else TBE.Label(startX, startY+=spaceY, width*2, height, " - Stats not applicable for perk type", "");
				
				startX-=10;
			}
			
			startY+=spaceY*0.5f;
			
			foldEffectSection=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldEffectSection, "Effects", TBE.foldoutS);
			if(foldEffectSection){
				startX+=10;
				
				if(item.type==_PerkType.ModifyUnit){
					startY=DrawEffectOnItemList(startX, startY, item);
					startY=DrawImmuneEffectOnItemList(startX, startY, item);
				}
				else if(item.type==_PerkType.ModifyUnitAbility){
					startY=DrawEffectOnItemList(startX, startY, item);
				}
				else if(item.type==_PerkType.ModifyFactionAbility){
					startY=DrawEffectOnItemList(startX, startY, item);
				}
				else TBE.Label(startX, startY+=spaceY, width*2, height, " - Not applicable for type", "");
				
				startX-=10;
			}
			
			
			startY+=spaceY*2.5f;
			
				GUIStyle style=new GUIStyle("TextArea");	style.wordWrap=true;
				cont=new GUIContent("Perk description (for runtime and editor): ", "");
				EditorGUI.LabelField(new Rect(startX, startY, 400, height), cont);
				item.desp=EditorGUI.DelayedTextField(new Rect(startX, startY+spaceY-3, 270, 150), item.desp, style);
			
			return new Vector2(maxX, startY+170);
		}
		
		
		
		public float DrawStats(float startX, float startY, Perk perk){
			//Stats item=perk.stats;
			
				int statsType=(int)perk.statsType;		contL=TBE.SetupContL(statsTypeLabel, statsTypeTooltip);
				TBE.Label(startX, startY+=spaceY, width, height, "Stats Type:", "");
				statsType = EditorGUI.Popup(new Rect(startX+spaceX, startY, 2*widthS+3, height), new GUIContent(""), statsType, contL);
				perk.statsType=(Perk._StatsType)statsType;
			
				if(GUI.Button(new Rect(startX+spaceX+2*widthS+5, startY, widthS*2-12, height), "Reset")) perk.Reset();
			
			startY+=spaceY*0.5f;
			
				bool isMultiplier=perk.statsType==Perk._StatsType.Multiplier;
			
				if(perk.type==_PerkType.ModifyUnit){
					startY=DrawStatsUnitNEff(startX, startY, perk.stats, true, isMultiplier);
					//startY=DrawEffectOnItemList(startX, startY-spaceY, perk);
					//startY=DrawImmuneEffectOnItemList(startX-10, startY-spaceY, perk);
				}
				else if(perk.type==_PerkType.ModifyUnitAbility){
					startY=DrawStatsAbility(startX, startY, perk.stats, true, isMultiplier);
					//startY=DrawEffectOnItemList(startX-10, startY, perk);
				}
				else if(perk.type==_PerkType.ModifyFactionAbility){
					startY=DrawStatsAbility(startX, startY, perk.stats, false, isMultiplier);
					//startY=DrawEffectOnItemList(startX-10, startY, perk);
				}
				else if(perk.type==_PerkType.ModifyEffect){
					startY=DrawStatsUnitNEff(startX, startY, perk.stats, false, isMultiplier);
				}
			
			return startY+spaceY;
		}
		
		
		#region stats
		private static bool foldEffectSection=true;
		public float DrawEffectOnItemList(float startX, float startY, Perk item){
			int spaceX=120; int spaceY=18; int width=150; int height=16;  //int widthS=40;
			
			startY+=spaceY;//*0.5f;
			
				//~ foldEffectSection=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldEffectSection, "Effects", TBE.foldoutS);
				//~ if(foldEffectSection){
					//startX+=12;
					
					TBE.Label(startX, startY, width, height, "Effect On Attack/Hit:", "The effect to be applied to the target when the unit attack or the ability hit");
					
					GUI.color=item.effIDList.Count==0 ? Color.grey : Color.white ;
					int effModType=(int)item.effModType;		contL=TBE.SetupContL(effModTypeLabel, effModTypeTooltip);
					TBE.Label(startX, startY+=spaceY, width, height, " - Mod. Type:", "");
					effModType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), effModType, contL);	GUI.color=Color.white;
					item.effModType=(Perk._EffModType)effModType;
					
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Effects:", "");
					for(int i=0; i<item.effIDList.Count; i++){
						TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
						
						int effIdx=EffectDB.GetPrefabIndex(item.effIDList[i]);
						effIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), effIdx, EffectDB.label);
						int prefabID=EffectDB.GetItemID(effIdx);
						if(!item.effIDList.Contains(prefabID)) item.effIDList[i]=prefabID;
						
						if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")){ item.effIDList.RemoveAt(i); }
					}
					
					if(item.effIDList.Count<EffectDB.GetCount()){
						int newIdx=-1;		CheckColor(item.effIDList.Count, 0);
						startY+=item.effIDList.Count>0 ? spaceY : 0 ;
						newIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, EffectDB.label);
						if(newIdx>=0){
							int newPID=EffectDB.GetItemID(newIdx);
							if(!item.effIDList.Contains(newPID)) item.effIDList.Add(newPID);
						}
						ResetColor();
					}
					
					//startX-=12;
				//~ }
			
			return startY;
		}
		
		
		public float DrawImmuneEffectOnItemList(float startX, float startY, Perk item){
			int spaceX=120; int spaceY=18; int width=150; int height=16;  //int widthS=40;
			
			startY+=spaceY*0.5f;
			
			//if(foldEffectSection){
				//startX+=12;
				
				//TBE.Label(startX, startY+=spaceY, width, height, " - Immune Effects:", "");
				TBE.Label(startX, startY+=spaceY, width, height, "Immune Effects:", "The effects the unit is immuned to");
				for(int i=0; i<item.unitImmuneEffIDList.Count; i++){
					TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
					
					int effIdx=EffectDB.GetPrefabIndex(item.unitImmuneEffIDList[i]);
					effIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), effIdx, EffectDB.label);
					int prefabID=EffectDB.GetItemID(effIdx);
					if(!item.unitImmuneEffIDList.Contains(prefabID)) item.unitImmuneEffIDList[i]=prefabID;
					
					if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")){ item.unitImmuneEffIDList.RemoveAt(i); }
				}
				
				if(item.unitImmuneEffIDList.Count<EffectDB.GetCount()){
					int newIdx=-1;		CheckColor(item.effIDList.Count, 0);
					startY+=item.unitImmuneEffIDList.Count>0 ? spaceY : 0 ;
					newIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, EffectDB.label);
					if(newIdx>=0){
						int newPID=EffectDB.GetItemID(newIdx);
						if(!item.unitImmuneEffIDList.Contains(newPID)) item.unitImmuneEffIDList.Add(newPID);
					}
					ResetColor();
				}
				
				//startX-=12;
			//}
			
			return startY;
		}
		
		
		public float DrawStatsAbility(float startX, float startY, Stats item, bool isUnit, bool isMultiplier){
			int spaceX=120; int spaceY=18; int width=150; int widthS=40; int height=16; 
			
			string tt=isMultiplier ? "Multiplier value applied to target ability " : "Modifier value applied to target ability '";
			string ttValue="' attribute" + ( isMultiplier ? TBE.ChanceTT() : "" );
			
				TBE.Label(startX, startY+=spaceY, width, height, "Cooldown:", tt+"Cooldown"+ttValue);
				item.abCooldown=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abCooldown);
			
				TBE.Label(startX, startY+=spaceY, width, height, "Use Limit:", tt+"Use Limit"+ttValue);
				item.abUseLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abUseLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "AP Cost:", tt+"Action-Point Cost"+ttValue);
				if(isUnit) item.abApCost=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abApCost);
				else TBE.Label(startX+spaceX, startY, widthS, height, "n/a", "");
				
			startY+=spaceY*0.5f;
		
				TBE.Label(startX, startY+=spaceY, width, height, "Attack Range:", tt+"Attack Range"+ttValue);
				item.attackRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attackRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "AOE Range:", tt+"Area-Of-Effect (AOE) Range"+ttValue);
				item.abAoeRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abAoeRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Duration:", tt+"Effect Duration"+ttValue+"\nFor ability that involves faction-switching and reveal-FogOfWar only, for other effect use perk to modify the effect item");
				item.abDuration=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abDuration);
			
			startY+=spaceY*0.5f;
			
				startY=DrawStatsOffence(startX, startY, item, isMultiplier);
				
			startY+=spaceY*0.5f;
				
				TBE.Label(startX, startY+=spaceY, width, height, "Effect Hit Chance:", tt+"Effect Hit Chance"+ttValue);
				item.abEffHitChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abEffHitChance);
				
			return startY;
		}
		
		
		public static float DrawStatsOffence(float startX, float startY, Stats item, bool isMultiplier){
			int spaceX=120; int spaceY=18; int width=150; int widthS=40; int height=16; 
			
			string tt=isMultiplier ? "Multiplier value applied to target unit/ability '" : "Modifier value applied to target unit/ability '";
			string ttValue="' attribute" + ( isMultiplier ? TBE.ChanceTT() : "" );
			
				TBE.Label(startX, startY+=spaceY, width, height, "HP Dmg. Min/Max:", tt+"Hit-Point Damage"+ttValue);
				item.dmgHPMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dmgHPMin);
				item.dmgHPMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+2, startY, widthS, height), item.dmgHPMax);
			
				TBE.Label(startX, startY+=spaceY, width, height, "AP Dmg. Min/Max:", tt+"Action-Point Damage"+ttValue);
				item.dmgAPMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dmgAPMin);
				item.dmgAPMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+2, startY, widthS, height), item.dmgAPMax);
			
				TBE.Label(startX, startY+=spaceY, width, height, "Attack:", tt+"Attack"+ttValue);
				item.attack=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attack);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Hit Chance:", tt+"Hit Chance"+ttValue);
				item.hit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Chance:", tt+"Critical-Hit Chance"+ttValue);
				item.critChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critChance);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Muliplier:", tt+"Critical Multiplier"+ttValue);
				item.critMultiplier=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critMultiplier);
			
			return startY;
		}
		
		private static bool foldCnO=true;
		public static float DrawStatsUnitNEff(float startX, float startY, Stats item, bool isUnit, bool isMultiplier){
			int spaceX=120; int spaceY=18; int width=150; int widthS=40; int height=16; 
			
				string tt=isMultiplier ? "Multiplier value applied to target unit/effect '" : "Modifier value applied to target unit/effect '";
				string ttValue="' attribute" + ( isMultiplier ? TBE.ChanceTT() : "" );
			
				if(!isUnit){
					string ett=isMultiplier ? "Multiplier value applied to effect 'apply-per-turn' attribute " : "Modifier value applied to effect 'apply-per-turn' ";
					
					TBE.Label(startX, startY+=spaceY, width, height, "Effect Duration:", ett+"'Duration'"+ttValue);
					item.effduration=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.effduration);
					
					TBE.Label(startX, startY+=spaceY, width, height, "Effect HP Modifier:", ett+"'Hit-Point Modifier'"+ttValue);
					item.effHP=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.effHP);
					
					TBE.Label(startX, startY+=spaceY, width, height, "Effect AP Modifier:", ett+"'Action-Point Modifier'"+ttValue);
					item.effAP=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.effAP);
					
					startY+=spaceY*0.5f;
				}
				
				TBE.Label(startX, startY+=spaceY, width, height, "Hit Point (HP):", tt+"Hit-Point"+ttValue);
				item.hp=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hp);
				
					TBE.Label(startX, startY+=spaceY, width, height, "  - Regen:", tt+"Hit-Point-Regen"+ttValue);
					item.hpRegen=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hpRegen);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Action Point (AP):", tt+"Action-Point"+ttValue);
				item.ap=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.ap);
				
					TBE.Label(startX, startY+=spaceY, width, height, "  - Regen:", tt+"Action-Point-Regen"+ttValue);
					item.apRegen=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.apRegen);
			
			startY+=spaceY*0.5f;
			
				startY=DrawStatsOffence(startX, startY, item, isMultiplier);
			
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Defense:", tt+"Defense"+ttValue);
				item.defense=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.defense);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Dodge Chance:", tt+"Dodge Chance"+ttValue);
				item.dodge=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dodge);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Reduc:", tt+"Critical Reduction Chance"+ttValue);
				item.critReduc=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critReduc);
			
			startY+=spaceY*0.5f;
			
				foldCnO=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldCnO, "Counter & Overwatch", TBE.foldoutS);
				if(foldCnO){
					startX+=12;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Counter:", tt+"Counter-Attack"+ttValue);	startY-=2;
					TBE.Label(startX, startY+=spaceY, width, height, " - Damage Mul.:", tt+"Counter-Attack Damage Multiplier"+ttValue);
					item.cDmgMultip=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cDmgMultip);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Hit Penalty:", tt+"Counter-Attack Hit Chance Penalty"+ttValue);	
					item.cHitPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cHitPenalty);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Crit Penalty:", tt+"Counter-Attack Critical-Hit Chance Penalty"+ttValue);
					item.cCritPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cCritPenalty);	startY-=2;
					
					startY+=5;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Overwatch:", "Overwatch-Attack");	startY-=2;
					TBE.Label(startX, startY+=spaceY, width, height, " - Damage Mul.:", tt+"Overwatch-Attack Damage Multiplier"+ttValue);
					item.oDmgMultip=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oDmgMultip);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Hit Penalty:", tt+"Overwatch-Attack Hit Chance Penalty"+ttValue);
					item.oHitPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oHitPenalty);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Crit Penalty:", tt+"Overwatch-Attack Critical-Hit Chance Penalty"+ttValue);
					item.oCritPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oCritPenalty);	startY-=2;
					
					startX-=12;
				}
				
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Turn Priority:", tt+"Turn Priority"+ttValue);
				item.turnPriority=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.turnPriority);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Move Range:", tt+"Move Range"+ttValue);
				item.moveRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.moveRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Attack Range:", tt+"Attack Range"+ttValue);
				item.attackRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attackRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Sight:", tt+"Sight"+ttValue);
				item.sight=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.sight);
			
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Move Limit:", tt+"Move Limit"+ttValue);
				item.moveLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.moveLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Attack Limit:", tt+"Attack Limit"+ttValue);
				item.attackLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attackLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Counter Limit:", tt+"Counter Limit"+ttValue);
				item.counterLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.counterLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Ability Limit:", tt+"Ability Limit"+ttValue);
				item.abilityLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abilityLimit);
			
			return startY+spaceY;
		}
		#endregion
		
		
		
		
		
		public float DrawItemPIDList_Effect(float startX, float startY, Perk item){
			TBE.Label(startX, startY+=spaceY, width, height, "Target Effect:", "Check to have the setting of the perk apply to all effects");
				
			for(int i=0; i<item.itemPIDList.Count; i++){
				TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
				
				int index=EffectDB.GetPrefabIndex(item.itemPIDList[i]);
				index=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), index, EffectDB.label);
				int prefabID=EffectDB.GetItemID(index);
				if(prefabID<0 || !item.itemPIDList.Contains(prefabID)) item.itemPIDList[i]=prefabID;
				
				if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.itemPIDList.RemoveAt(i);
			}
			
			if(item.itemPIDList.Count<EffectDB.GetCount()){
				int newIdx=-1;		CheckColor(item.itemPIDList.Count, 0);
				startY+=item.itemPIDList.Count>0 ? spaceY : 0 ;
				newIdx=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, EffectDB.label);
				if(newIdx>=0){
					int newPID=EffectDB.GetItemID(newIdx);
					if(!item.itemPIDList.Contains(newPID)) item.itemPIDList.Add(newPID);
				}
				ResetColor();
			}
			
			return startY;
		}
		
		public float DrawItemPIDList_Unit(float startX, float startY, Perk item){
			TBE.Label(startX, startY+=spaceY, width, height, "Apply to All Unit:", "Check to have the setting of the perk apply to all units");
			item.applyToAll=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.applyToAll);
			
			if(!item.applyToAll){
				TBE.Label(startX, startY+=spaceY, width, height, "Target Unit:", "");
				
				for(int i=0; i<item.itemPIDList.Count; i++){
					TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
					
					int index=UnitDB.GetPrefabIndex(item.itemPIDList[i]);
					index=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), index, UnitDB.label);
					int prefabID=UnitDB.GetItemID(index);
					if(prefabID<0 || !item.itemPIDList.Contains(prefabID)) item.itemPIDList[i]=prefabID;
					
					if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.itemPIDList.RemoveAt(i);
				}
				
				if(item.itemPIDList.Count<UnitDB.GetCount()){
					int newIdx=-1;		CheckColor(item.itemPIDList.Count, 0);
					startY+=item.itemPIDList.Count>0 ? spaceY : 0 ;
					newIdx=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, UnitDB.label);
					if(newIdx>=0){
						int newPID=UnitDB.GetItemID(newIdx);
						if(!item.itemPIDList.Contains(newPID)) item.itemPIDList.Add(newPID);
					}
					ResetColor();
				}
			}
			else TBE.Label(startX+spaceX, startY, width, height, "-", "");
			
			return startY;
		}
		
		public float DrawItemPIDList_Ability(float startX, float startY, Perk item, bool isUnit){
			TBE.Label(startX, startY+=spaceY, width, height, "Apply to All Ability:", "Check to have the setting of the perk apply to all abilities");
			item.applyToAll=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.applyToAll);
			
			if(!item.applyToAll){
				TBE.Label(startX, startY+=spaceY, width, height, "Target Ability:", "");
				
				for(int i=0; i<item.itemPIDList.Count; i++){
					TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
					
					int index=isUnit ? AbilityUDB.GetPrefabIndex(item.itemPIDList[i]) : AbilityFDB.GetPrefabIndex(item.itemPIDList[i]);
					index=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), index, isUnit ? AbilityUDB.label : AbilityFDB.label);
					int prefabID=isUnit ? AbilityUDB.GetItemID(index) : AbilityFDB.GetItemID(index);
					if(prefabID<0 || !item.itemPIDList.Contains(prefabID)) item.itemPIDList[i]=prefabID;
					
					if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.itemPIDList.RemoveAt(i);
				}
				
				if(item.itemPIDList.Count<(isUnit ? AbilityUDB.GetCount() : AbilityFDB.GetCount())){
					int newIdx=-1;		CheckColor(item.itemPIDList.Count, 0);
					startY+=item.itemPIDList.Count>0 ? spaceY : 0 ;
					newIdx=EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newIdx, isUnit ? AbilityUDB.label : AbilityFDB.label);
					if(newIdx>=0){
						int newPID=isUnit ? AbilityUDB.GetItemID(newIdx) : AbilityFDB.GetItemID(newIdx);
						if(!item.itemPIDList.Contains(newPID)) item.itemPIDList.Add(newPID);
					}
					ResetColor();
				}
			}
			else TBE.Label(startX+spaceX, startY, width, height, "-", "");
			
			return startY;
		}
		
		
		
		protected Vector2 DrawPerkList(float startX, float startY, List<Perk> perkList){
			List<EItem> list=new List<EItem>();
			for(int i=0; i<perkList.Count; i++){
				EItem item=new EItem(perkList[i].prefabID, perkList[i].name, perkList[i].icon);
				list.Add(item);
			}
			return DrawList(startX, startY, window.position.width, window.position.height, list);
		}
		
		public static int NewItem(int idx=-1){ return window._NewItem(idx); }
		private int _NewItem(int idx=-1){
			Perk item=null;
			if(idx<0){ item=new Perk(); item.Reset(); }
			if(idx>=0) item=PerkDB.GetList()[idx].Clone();
			
			item.prefabID=TBE.GenerateNewID(PerkDB.GetPrefabIDList());
			
			PerkDB.GetList().Add(item);
			PerkDB.UpdateLabel();
			
			return PerkDB.GetList().Count-1;
		}
		
		protected override void DeleteItem(){
			PerkDB.GetList().RemoveAt(deleteID);
			PerkDB.UpdateLabel();
		}
		
		protected override void SelectItem(){ }
		private void _SelectItem(){ 
			selectID=newSelectID;
			if(PerkDB.GetList().Count<=0) return;
			selectID=Mathf.Clamp(selectID, 0, PerkDB.GetList().Count-1);
			
			Repaint();
		}
		//~ protected override void SelectItem(){ SelectItem(selectID); }
		//~ private void SelectItem(int newID){ 
			//~ selectID=newID;
			//~ if(PerkDB.GetList().Count<=0) return;
			//~ selectID=Mathf.Clamp(selectID, 0, PerkDB.GetList().Count-1);
		//~ }
		
		protected override void ShiftItemUp(){ 	if(selectID>0) ShiftItem(-1); }
		protected override void ShiftItemDown(){ if(selectID<PerkDB.GetList().Count-1) ShiftItem(1); }
		private void ShiftItem(int dir){
			Perk item=PerkDB.GetList()[selectID];
			PerkDB.GetList()[selectID]=PerkDB.GetList()[selectID+dir];
			PerkDB.GetList()[selectID+dir]=item;
			selectID+=dir;
		}
		
		
	}
	
}
