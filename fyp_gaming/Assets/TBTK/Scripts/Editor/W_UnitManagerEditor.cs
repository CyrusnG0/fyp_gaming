using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class umEditorWindow : TBEditorWindow {
		
		[MenuItem ("Tools/TBTK/UnitManagerEditor", false, 10)]
		static void OpenUnitManagerEditor () { Init(); }
		
		private static UnitManager instance;
		private static umEditorWindow window;
		
		public static void Init(UnitManager umInstance=null) {
			// Get existing open window or if none, make a new one:
			window = (umEditorWindow)EditorWindow.GetWindow(typeof (umEditorWindow), false, "UnitManager");
			window.minSize=new Vector2(500, 300);
			
			TBE.Init();
			
			if(umInstance!=null) instance=umInstance;
		}
		
		
		
		private bool GetUnitManager(){
			instance=(UnitManager)FindObjectOfType(typeof(UnitManager));
			return instance==null ? false : true ;
		}
		
		
		public void OnGUI() {
			TBE.InitGUIStyle();
			
			//if(!CheckIsPlaying()) return;
			if(window==null) Init();
			if(instance==null && !GetUnitManager()) return;
			
			Undo.RecordObject(this, "window");
			Undo.RecordObject(instance, "UnitManager");
			
			
			float startX=5;	float startY=5;	width=150;		Vector2 v2=Vector2.zero;
			
			
			Rect visibleRect=new Rect(startX, startY, window.position.width-startX*2, window.position.height-(startY+5));
			Rect contentRect=new Rect(startX, startY, contentWidth-20, contentHeight);
			
			
			scrollPos = GUI.BeginScrollView(visibleRect, scrollPos, contentRect);
			
				
				float maxHeight=0;	//float maxWidth=0;	
				for(int i=0; i<instance.factionList.Count; i++){
					v2=FactionConfigurator(startX, startY, instance.factionList[i], i);
					startX+=(spaceX+width+20);
					maxHeight=Mathf.Max(maxHeight, v2.y+5);
				}
				
				
				
				if(GUI.Button(new Rect(startX, startY, width*.8f, height), "Add Faction")){
					List<int> existingIDList=new List<int>();
					for(int i=0; i<instance.factionList.Count; i++) existingIDList.Add(instance.factionList[i].factionID);
					
					Faction fac=new Faction();
					fac.factionID=TBE.GenerateNewID(existingIDList);
					instance.factionList.Add(fac);
				}
				
				contentHeight=maxHeight+0;	contentWidth=instance.factionList.Count*(spaceX+width+20)+20+width*.8f;
			
			GUI.EndScrollView();
			
			
			if(GUI.changed) EditorUtility.SetDirty(instance);
			if(GUI.changed) TBE.SetDirty();
		}
		
		
		private bool foldStartingList=true;
		private bool foldSpawnGroup=true;
		private bool foldAbilityList=true;
		private Vector2 FactionConfigurator(float startX, float startY, Faction fac, int idx){
			GUI.color=new Color(.85f, .85f, .85f, 1f); 
			GUI.Box(new Rect(startX, startY, width+spaceX+15, contentHeight), ""); GUI.color=Color.white;
			
			startX+=5;		startY+=5;
			
			
			GUI.color=new Color(1f, .75f, .5f, 1);
			if(GUI.Button(new Rect(startX, startY, width*.8f, height), "Remove Faction")){
				instance.factionList.Remove(fac);
				return new Vector2(startX, startY+spaceY);
			}
			GUI.color=Color.white;
			
			TBE.Label(startX+width+widthS+28, startY, width, height, "Index: "+idx, "The unique identifier for this faction");
			//TBE.Label(startX+spaceX-widthS, startY, width-widthS, height, ""+idx);
			
			startY+=spaceY;
			
			TBE.Label(startX, startY+=spaceY, width, height, "Name:", "Just for your own reference\nNot used in game");
			fac.name=EditorGUI.DelayedTextField(new Rect(startX+spaceX-widthS, startY, width-widthS, height), fac.name);
			fac.color=EditorGUI.ColorField(new Rect(startX+spaceX+width-widthS*2+5, startY, widthS*2-5, height), fac.color);
			
			
			TBE.Label(startX, startY+=spaceY, width, height, "Playable:", "Check to specify that the faction is to be controlled by a player");
			fac.playableFaction=EditorGUI.Toggle(new Rect(startX+spaceX-widthS, startY, widthS, height), fac.playableFaction);
			
			TBE.Label(startX, startY+=spaceY+5, width, height, "Load From Cache:", "Check to have the faction's starting unit load from the cache stored in previous scene");
			fac.loadFromData=EditorGUI.Toggle(new Rect(startX+spaceX-widthS+28, startY, widthS, height), fac.loadFromData);
			
			TBE.Label(startX, startY+=spaceY, width, height, "Save To Cache:", "Check to have the faction's units save to cache when the game end so it can be load in subsequent scene");
			fac.saveToData=EditorGUI.Toggle(new Rect(startX+spaceX-widthS+28, startY, widthS, height), fac.saveToData);
			
			TBE.Label(startX+123, startY, width, height, " - Loaded Unit Only:", "Check to have the 'Save To Cache' function save the unit loaded from cache only. Any unit in this scene will not be saved");
			if(!fac.saveToData) TBE.Label(startX+123+spaceX-widthS+38, startY, widthS, height, "-", "");
			else fac.saveLoadedUnitOnly=EditorGUI.Toggle(new Rect(startX+123+spaceX-widthS+38, startY, widthS, height), fac.saveLoadedUnitOnly);
			
			
			startY+=spaceY*0.5f;
			
			TBE.Label(startX, startY+=spaceY, width, height, "Unit Direction:", "The direction which the spawned/starting unit should be facing\nClockwise in degree starting from +ve z-axis");
			fac.direction=EditorGUI.FloatField(new Rect(startX+spaceX-widthS+28, startY, widthS, height), fac.direction);
			
			startY+=spaceY*0.5f;
			
			foldStartingList=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldStartingList, "Starting Units", TBE.foldoutS);
			if(foldStartingList){
			
				if(fac.loadFromData){
					TBE.Label(startX+10, startY+=spaceY, width, height, "Starting Units:", "");
					TBE.Label(startX+spaceX-height, startY, width, height, "-   Load from cache");
				}
				else{
					for(int i=0; i<fac.startingUnitList.Count; i++){
						if(fac.startingUnitList[i]==null){ fac.startingUnitList.RemoveAt(i); i-=1; }
					}
					
					TBE.Label(startX+10, startY+=spaceY, width, height, "Starting Units:", "The unit(s) ready to be deployed at the start of the game");
					for(int i=0; i<fac.startingUnitList.Count; i++){
						TBE.Label(startX+spaceX-height, startY, width, height, "-");
						
						int unitIdx=UnitDB.GetPrefabIndex(fac.startingUnitList[i]);		
						unitIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width-height, height), unitIdx, UnitDB.label);
						if(unitIdx>=0) fac.startingUnitList[i]=UnitDB.GetItem(unitIdx);
						
						if(GUI.Button(new Rect(startX+spaceX+width+3-height, startY, height, height), "-")) fac.startingUnitList.RemoveAt(i);
						
						startY+=spaceY;
					}
					
					int newUnitIdx=-1;
					newUnitIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width-height, height), newUnitIdx, UnitDB.label);
					if(newUnitIdx>=0) fac.startingUnitList.Add(UnitDB.GetItem(newUnitIdx));
				}
				
			}
			
			startY+=spaceY;
			
			foldSpawnGroup=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldSpawnGroup, "Spawn Info", TBE.foldoutS);
			if(foldSpawnGroup){
				startX+=10;
				
				if(GUI.Button(new Rect(startX+spaceX-10, startY, width, height), "Add Group")){
					List<int> existingIDList=new List<int>();
					for(int i=0; i<fac.spawnGroupList.Count; i++) existingIDList.Add(fac.spawnGroupList[i].ID);
					
					SpawnGroup group=new SpawnGroup();
					group.ID=TBE.GenerateNewID(existingIDList);
					fac.spawnGroupList.Add(group);
					
					foldSpawnGroup=true;
				}
				
				for(int i=0; i<fac.spawnGroupList.Count; i++){
					startY=DrawSpawnGroup(startX, startY+10, fac.spawnGroupList[i], fac);
				}
				
				startX-=10;
			}
			
			startY+=spaceY;
			
			foldAbilityList=EditorGUI.Foldout(new Rect(startX, startY+=spaceY, spaceX, height), foldAbilityList, "Abilities", TBE.foldoutS);
			if(foldAbilityList){
			
				//TBE.Label(startX, startY+=spaceY, width, height, "Effect On Triggered:", "The effect applies to target when triggered");
				for(int i=0; i<fac.abilityIDList.Count; i++){
					TBE.Label(startX+spaceX-height, startY+=(i>0 ? spaceY : 0), width, height, "-");
					
					int abIdx=AbilityFDB.GetPrefabIndex(fac.abilityIDList[i]);
					abIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width-height, height), abIdx, AbilityFDB.label);
					if(abIdx>=0) fac.abilityIDList[i]=AbilityFDB.GetItemID(abIdx);
					
					if(GUI.Button(new Rect(startX+spaceX+width+3-height, startY, height, height), "-")) fac.abilityIDList.RemoveAt(i);
					
					//startY+=spaceY;
				}
				
				if(fac.abilityIDList.Count<AbilityFDB.GetCount()){
					int newAbID=-1;
					startY+=fac.abilityIDList.Count>0 ? spaceY : 0 ;
					newAbID = EditorGUI.Popup(new Rect(startX+spaceX, startY, width-height, height), newAbID, AbilityFDB.label);
					if(newAbID>=0) newAbID=AbilityFDB.GetItemID(newAbID);
					if(newAbID>=0 && !fac.abilityIDList.Contains(newAbID)) fac.abilityIDList.Add(newAbID);
				}
			}
			
			return new Vector2(startX, startY+spaceY);
		}
		
		
		private float DrawSpawnGroup(float startX, float startY, SpawnGroup group, Faction fac){
			//~ public int ID;
		
		//~ public int countMin=5;
		//~ public int countMax=5;
		
		//~ public List<Unit> unitList=new List<Unit>();
		//~ public List<int> unitCountMinList=new List<int>();
		//~ public List<int> unitCountMaxList=new List<int>();
			
			List<Node> nodeList=GridManager.GetSpawnGroup(fac.factionID, group.ID);
			TBE.Label(startX, startY+=spaceY, width, height, "Node Count: "+nodeList.Count, "The number of node available to this spawn group\nYou can edit this using GridEditor/Node/SpawnArea");
			
			GUI.color=new Color(1f, .75f, .5f, 1);
			if(GUI.Button(new Rect(startX+spaceX+width-(widthS*2)-10, startY, widthS*2, height), "Remove")){
				fac.spawnGroupList.Remove(group);
				return startY;
			}
			GUI.color=Color.white;
			
			
			TBE.Label(startX, startY+=spaceY, width, height, "SpawnCount (Min/Max):", "The number of unit to spawn");
			group.countMin=EditorGUI.IntField(new Rect(startX+width+5, startY, widthS, height), group.countMin);
			group.countMax=EditorGUI.IntField(new Rect(startX+width+5+widthS, startY, widthS, height), group.countMax);
			
			
			
			while(group.unitCountMinList.Count<group.unitList.Count) group.unitCountMinList.Add(0);
			while(group.unitCountMaxList.Count<group.unitList.Count) group.unitCountMaxList.Add(0);
			while(group.unitCountMinList.Count>group.unitList.Count) group.unitCountMinList.RemoveAt(group.unitCountMinList.Count-1);
			while(group.unitCountMaxList.Count>group.unitList.Count) group.unitCountMaxList.RemoveAt(group.unitCountMaxList.Count-1);
			
			//TBE.Label(startX+height, startY+=spaceY, width, height, "    Units", "");
			//TBE.Label(startX+width+height, startY, width, height, "Min/Max", "");		startX+=10;
			for(int i=0; i<group.unitList.Count; i++){
				TBE.Label(startX, startY+=spaceY, width, height, "-");
				
				int unitIdx=UnitDB.GetPrefabIndex(group.unitList[i]);		
				unitIdx = EditorGUI.Popup(new Rect(startX+height, startY, width-height, height), unitIdx, UnitDB.label);
				if(unitIdx>=0) group.unitList[i]=UnitDB.GetItem(unitIdx);
				
				group.unitCountMinList[i]=EditorGUI.IntField(new Rect(startX+width+5, startY, widthS, height), group.unitCountMinList[i]);
				group.unitCountMaxList[i]=EditorGUI.IntField(new Rect(startX+width+5+widthS, startY, widthS, height), group.unitCountMaxList[i]);
				
				if(GUI.Button(new Rect(startX+spaceX+width-height-10, startY, height, height), "-")) group.unitList.RemoveAt(i);
			}
			startX-=10;
			
			if(group.unitList.Count<UnitDB.GetCount()){
				TBE.Label(startX+10, startY+=spaceY, width, height, "-");
				
				int newUnitIdx=-1;
				newUnitIdx = EditorGUI.Popup(new Rect(startX+height+10, startY, width-height, height), newUnitIdx, UnitDB.label);
				if(newUnitIdx>=0) group.unitList.Add(UnitDB.GetItem(newUnitIdx));
			}
			
			return startY;
		}
		
	}

}