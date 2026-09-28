using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class EffectEditorWindow : TBEditorWindow {
		
		[MenuItem ("Tools/TBTK/EffectEditor", false, 10)]
		static void OpenEffectEditor () { Init(); }
		
		private static EffectEditorWindow window;
		
		public static void Init (int prefabID=-1) {
			window = (EffectEditorWindow)EditorWindow.GetWindow(typeof (EffectEditorWindow), false, "EffectEditor");
			window.minSize=new Vector2(420, 300);
			
			TBE.Init();
			
			window.InitLabel();
			
			//if(prefabID>=0) window.selectID=EffectDB.GetPrefabIndex(prefabID);
			
			if(prefabID>=0){
				window.selectID=EffectDB.GetPrefabIndex(prefabID);
				window.newSelectID=window.selectID;
				window.newSelectDelay=1;
			}
			
			window._SelectItem();
		}
		
		
		private static string[] impactLabel;
		private static string[] impactTooltip;
		
		private static string[] effTypeLabel;
		private static string[] effTypeTooltip;
		
		public void InitLabel(){
			int enumLength = Enum.GetValues(typeof(Effect._ImpactType)).Length;
			impactLabel=new string[enumLength];		impactTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){	
				Effect._ImpactType type=(Effect._ImpactType)i;
				impactLabel[i]=type.ToString();
				if(type==Effect._ImpactType.None)			impactTooltip[i]="No immediate effect on hit/impact";
				if(type==Effect._ImpactType.Negative)		impactTooltip[i]="Negative effect on hit/impact";
				if(type==Effect._ImpactType.Positive)		impactTooltip[i]="Positive effect on hit/impact";
			}
			
			enumLength = Enum.GetValues(typeof(Effect._EffType)).Length;
			effTypeLabel=new string[enumLength];
			effTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				effTypeLabel[i]=((Effect._EffType)i).ToString();
				if((Effect._EffType)i==Effect._EffType.Modifier) effTypeTooltip[i]="The value in the effect will be directly added to the target unit";
				if((Effect._EffType)i==Effect._EffType.Multiplier) effTypeTooltip[i]="The value in the effect will be be used to multiply the target unit's base value";
			}
		}
		
		
		public void OnGUI(){
			TBE.InitGUIStyle();
			
			if(!CheckIsPlaying()) return;
			if(window==null) Init();
			
			List<Effect> effectList=EffectDB.GetList();
			
			Undo.RecordObject(this, "window");
			Undo.RecordObject(EffectDB.GetDB(), "abilityDB");
			
			if(GUI.Button(new Rect(Math.Max(260, window.position.width-120), 5, 100, 25), "Save")){
				EffectDB.UpdateLabel();
				
				GUI.FocusControl(null);
				TBE.SetDirty();
			}
			
			if(GUI.Button(new Rect(5, 5, 120, 25), "Create New")) Select(NewItem());
			if(effectList.Count>0 && GUI.Button(new Rect(130, 5, 100, 25), "Clone Selected")) Select(NewItem(selectID));
			
			
			float startX=5;	float startY=55;
			
			if(minimiseList){ if(GUI.Button(new Rect(startX, startY-20, 30, 18), ">>")) minimiseList=false; }
			else{ if(GUI.Button(new Rect(startX, startY-20, 30, 18), "<<")) minimiseList=true; }
			
			Vector2 v2=DrawEffectList(startX, startY, effectList);
			startX=v2.x+25;
			
			if(effectList.Count==0) return;
			
			if(newSelectDelay>0){
				newSelectDelay-=1;		GUI.FocusControl(null);
				if(newSelectDelay==0) _SelectItem();
				else Repaint();
			}
			
			if(selectID>=effectList.Count) return;
			
			Rect visibleRect=new Rect(startX, startY, window.position.width-startX, window.position.height-startY);
			Rect contentRect=new Rect(startX, startY, contentWidth, contentHeight);
			
			scrollPos = GUI.BeginScrollView(visibleRect, scrollPos, contentRect);
			
				v2=DrawEffectConfigurator(startX, startY, effectList[selectID]);
				contentWidth=v2.x-startX;
				contentHeight=v2.y-55;
			
			GUI.EndScrollView();
			
			if(GUI.changed) TBE.SetDirty();
		}
		
		
		private bool foldImpact;
		private bool foldStats;
		
		private Vector2 DrawEffectConfigurator(float startX, float startY, Effect item){
			float maxX=startX;
			
				startY=TBE.DrawBasicInfo(startX, startY, item);
			
			//spaceX+=12;
			
				//TBE.Label(startX, startY+=spaceY, width, height, "Stackable:", "Check if the effect can stack if apply on a same unit with repeatably");
				//item.stackable=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.stackable);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Duration:", "The long the effect will last (in second)\n\nNot applicable when the effect is used as unit's Aura");
				item.duration=Mathf.Round(EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.duration));
			
				TBE.Label(startX, startY+=spaceY, width, height, "Range (for Aura):", "The effective range of the effect when it's used as aura\n\nOnly applicable when the effect is used as unit's Aura");
				item.range=EditorGUI.DelayedIntField(new Rect(startX+spaceX, startY, widthS, height), item.range);
			
			startY+=spaceY*0.5f;
				
				TBE.Label(startX, startY+=spaceY, width, height, "Overwatch:", "Check to have the have the effect put the unit to overwatch state (fire on any hostile entering attack range)\n\nNot appliable when effect is used as Aura");
				item.overwatch=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.overwatch);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Stun Target:", "Check if the effect effect will stun its target\n\nNot appliable when effect is used as Aura");
				item.stun=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.stun);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Disable Ability:", "Check if the effect effect will prevent its target from using any ability\n\nNot appliable when effect is used as Aura");
				item.disableAbility=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.disableAbility);
				
			startY+=spaceY*0.5f;
			
			
			foldImpact=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldImpact, "Stats (apply per turn)", TBE.foldoutS);
			if(foldImpact){
				startX+=10;
				
				int impactType=(int)item.impactType;		contL=TBE.SetupContL(impactLabel, impactTooltip);
				TBE.Label(startX, startY+=spaceY, width, height, "Type:", "Not appliable when effect is used as Aura");
				impactType = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), new GUIContent(""), impactType, contL);
				item.impactType=(Effect._ImpactType)impactType;
				
				if(item.impactType!=Effect._ImpactType.None){
					TBE.Label(startX, startY+=spaceY, width, height, "HP Modifier Min/Max:", "The value applied to the target's hit-point\n\nNot appliable when effect is used as Aura");
					item.hpModifierMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+3, startY, widthS, height), item.hpModifierMin);
					item.hpModifierMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+5, startY, widthS, height), item.hpModifierMax);
					
					TBE.Label(startX, startY+=spaceY, width, height, "AP Modifier Min/Max:", "The value applied to the target's action-point\n\nNot appliable when effect is used as Aura");
					item.apModifierMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+3, startY, widthS, height), item.apModifierMin);
					item.apModifierMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+5, startY, widthS, height), item.apModifierMax);
				}
				else{
					TBE.Label(startX, startY+=spaceY, width, height, "HP Modifier Min/Max:", "");
					TBE.Label(startX+spaceX+3, startY, widthS, height, "-");
					
					TBE.Label(startX, startY+=spaceY, width, height, "AP Modifier Min/Max:", "");
					TBE.Label(startX+spaceX+3, startY, widthS, height, "-");
				}
				
				startX-=10;
			}
			
			startY+=spaceY*0.5f;
			
			foldStats=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldStats, "Stats (persistent)", TBE.foldoutS);
			if(foldStats){
				startX+=10;
				
				int type=(int)item.effType;		contL=TBE.SetupContL(effTypeLabel, effTypeTooltip);
				TBE.Label(startX, startY+=spaceY, width, height, "Type:", "", TBE.headerS);
				type = EditorGUI.Popup(new Rect(startX+spaceX, startY, 2*widthS+3, height), new GUIContent(""), type, contL);
				item.effType=(Effect._EffType)type;
				
				if(GUI.Button(new Rect(startX+spaceX+2*widthS+5, startY, widthS*2-12, height), "Reset")) item.Reset();
				
				//TBE.Label(startX, startY+=spaceY, width, height, "Multipliers:", "", TBE.headerS);
				startY=DrawStats(startX, startY+=spaceY, item.stats, item.effType==Effect._EffType.Multiplier);	startY-=spaceY;
				
				startX-=10;
			}
			
			startY+=spaceY;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Visual Effect:", "", TBE.headerS);	startX+=12;	spaceX-=12;
				
				startY=DrawVisualObject(startX, startY+=spaceY, item.hitVisualEffect, "On Hit:", "The effect to spawn when the effect first hit a unit");
				
				TBE.Label(startX, startY+=spaceY, width, height, "Active:", "The visual object to stay on target as long as the effect is active");
				item.activeVisualEffect=(Transform)EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), item.activeVisualEffect, typeof(Transform), true);
				
				startX-=12;	spaceX+=12;
			
			startY+=spaceY*2;
			
				GUIStyle style=new GUIStyle("TextArea");	style.wordWrap=true;
				cont=new GUIContent("Effect description (for runtime and editor): ", "");
				EditorGUI.LabelField(new Rect(startX, startY, 400, height), cont);
				item.desp=EditorGUI.DelayedTextField(new Rect(startX, startY+spaceY-3, 270, 150), item.desp, style);
			
			return new Vector2(maxX, startY+170);
		}
		
		
		
		
		#region stats
		private static bool foldCnO=true;
		public static float DrawStats(float startX, float startY, Stats item, bool isMultiplier){
			int spaceX=120; int spaceY=18; int width=150; int widthS=40; int height=16; 
			
				string tt=isMultiplier ? "Multiplier value applied to target's " : "Modifier value applied to target's ";
				string ttValue=isMultiplier ? TBE.ChanceTT() : "" ;
			
				TBE.Label(startX, startY, width, height, "Hit Point (HP):", tt+"hit-point"+ttValue);
				item.hp=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hp);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Action Point (AP):", tt+"action-point"+ttValue);
				item.ap=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.ap);
			
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "HP Dmg. Min/Max:", tt+"hit-point damage"+ttValue);
				item.dmgHPMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dmgHPMin);
				item.dmgHPMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+2, startY, widthS, height), item.dmgHPMax);
			
				TBE.Label(startX, startY+=spaceY, width, height, "AP Dmg. Min/Max:", tt+"action-point damage"+ttValue);
				item.dmgAPMin=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dmgAPMin);
				item.dmgAPMax=EditorGUI.DelayedFloatField(new Rect(startX+spaceX+widthS+2, startY, widthS, height), item.dmgAPMax);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Attack:", tt+"attack"+ttValue);
				item.attack=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attack);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Hit Chance:", tt+"hit-chance"+ttValue);
				item.hit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.hit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Chance:", tt+"critical-hit chance"+ttValue);
				item.critChance=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critChance);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Muliplier:", tt+"critical multiplier"+ttValue);
				item.critMultiplier=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critMultiplier);
				
				//TBE.Label(startX, startY+=spaceY, width, height, "Counter Muliplier:", tt+"Attack"+ttValue);
				//item.cMultiplier=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.cMultiplier);
			
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Defense:", tt+"defense"+ttValue);
				item.defense=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.defense);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Dodge Chance:", tt+"dodge chance"+ttValue);
				item.dodge=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.dodge);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Crit Reduc:", tt+"critical reduction chance"+ttValue);
				item.critReduc=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.critReduc);
			
			startY+=spaceY*0.5f;
			
				foldCnO=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldCnO, "Counter & Overwatch", TBE.foldoutS);
				if(foldCnO){
					startX+=12;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Counter:", "additional stats to use for counter-attack");	startY-=2;
					TBE.Label(startX, startY+=spaceY, width, height, " - Damage Mul.:", tt+"counter attack damage"+ttValue);
					item.cDmgMultip=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cDmgMultip);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Hit Penalty:", tt+"counter attack hit chance"+ttValue);	
					item.cHitPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cHitPenalty);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Crit Penalty:", tt+"counter attack critical-hit chance"+ttValue);
					item.cCritPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.cCritPenalty);	startY-=2;
					
					startY+=5;
					
					TBE.Label(startX, startY+=spaceY, width, height, "Overwatch:", "additional stats to use for overwatch attack");	startY-=2;
					TBE.Label(startX, startY+=spaceY, width, height, " - Damage Mul.:", tt+"overwatch attack damage"+ttValue);
					item.oDmgMultip=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oDmgMultip);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Hit Penalty:", tt+"overwatch attack hit chance"+ttValue);
					item.oHitPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oHitPenalty);	startY-=2;
					
					TBE.Label(startX, startY+=spaceY, width, height, " - Crit Penalty:", tt+"overwatch attack critical-hit chance"+ttValue);
					item.oCritPenalty=EditorGUI.DelayedFloatField(new Rect(startX+spaceX-12, startY, widthS, height), item.oCritPenalty);	startY-=2;
					
					startX-=12;
				}
				
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Turn Priority:", tt+"turn priority"+ttValue);
				item.turnPriority=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.turnPriority);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Move Range:", tt+"move range"+ttValue);
				item.moveRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.moveRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Attack Range:", tt+"attack range"+ttValue);
				item.attackRange=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attackRange);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Sight:", tt+"sight"+ttValue);
				item.sight=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.sight);
			
			startY+=spaceY*0.5f;
			
				TBE.Label(startX, startY+=spaceY, width, height, "Move Limit:", tt+"move limit"+ttValue);
				item.moveLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.moveLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Attack Limit:", tt+"attack limit"+ttValue);
				item.attackLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.attackLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Counter Limit:", tt+"counter limit"+ttValue);
				item.counterLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.counterLimit);
				
				TBE.Label(startX, startY+=spaceY, width, height, "Ability Limit:", tt+"ability limit"+ttValue);
				item.abilityLimit=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), item.abilityLimit);
			
			return startY+spaceY;
		}
		#endregion
		
		
		
		
		protected Vector2 DrawEffectList(float startX, float startY, List<Effect> effectList){
			List<EItem> list=new List<EItem>();
			for(int i=0; i<effectList.Count; i++){
				EItem item=new EItem(effectList[i].prefabID, effectList[i].name, effectList[i].icon);
				list.Add(item);
			}
			return DrawList(startX, startY, window.position.width, window.position.height, list);
		}
		
		public static int NewItem(int idx=-1){ return window._NewItem(idx); }
		private int _NewItem(int idx=-1){
			Effect item=null;
			if(idx<0){ item=new Effect(); item.Reset(); }
			if(idx>=0) item=EffectDB.GetList()[idx].Clone();
			
			item.prefabID=TBE.GenerateNewID(EffectDB.GetPrefabIDList());
			
			EffectDB.GetList().Add(item);
			EffectDB.UpdateLabel();
			
			return EffectDB.GetList().Count-1;
		}
		
		protected override void DeleteItem(){
			EffectDB.GetList().RemoveAt(deleteID);
			EffectDB.UpdateLabel();
		}
		
		
		protected override void SelectItem(){ }
		private void _SelectItem(){ 
			selectID=newSelectID;
			if(EffectDB.GetList().Count<=0) return;
			selectID=Mathf.Clamp(selectID, 0, EffectDB.GetList().Count-1);
			
			Repaint();
		}
		
		//~ protected override void SelectItem(){ SelectItem(selectID); }
		//~ private void SelectItem(int newID){ 
			//~ selectID=newID;
			//~ if(EffectDB.GetList().Count<=0) return;
			//~ selectID=Mathf.Clamp(selectID, 0, EffectDB.GetList().Count-1);
		//~ }
		
		protected override void ShiftItemUp(){ 	if(selectID>0) ShiftItem(-1); }
		protected override void ShiftItemDown(){ if(selectID<EffectDB.GetList().Count-1) ShiftItem(1); }
		private void ShiftItem(int dir){
			Effect item=EffectDB.GetList()[selectID];
			EffectDB.GetList()[selectID]=EffectDB.GetList()[selectID+dir];
			EffectDB.GetList()[selectID+dir]=item;
			selectID+=dir;
		}
		
		
	}
	
}
