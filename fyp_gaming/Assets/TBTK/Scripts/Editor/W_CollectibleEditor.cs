using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class CollectibleEditorWindow : TBEditorWindow {
		
		[MenuItem ("Tools/TBTK/CollectibleEditor", false, 10)]
		static void OpenCollectibleEditor () { Init(); }
		
		private static CollectibleEditorWindow window;
		
		public static void Init (int prefabID=-1) {
			window = (CollectibleEditorWindow)EditorWindow.GetWindow(typeof (CollectibleEditorWindow), false, "CollectibleEditor");
			window.minSize=new Vector2(420, 300);
			
			TBE.Init();
			
			//window.InitLabel();
			
			//if(prefabID>=0) window.selectID=CollectibleDB.GetPrefabIndex(prefabID);
			
			if(prefabID>=0){
				window.selectID=CollectibleDB.GetPrefabIndex(prefabID);
				window.newSelectID=window.selectID;
				window.newSelectDelay=1;
			}
			
			window._SelectItem();
		}
		
		
		public void OnGUI(){
			TBE.InitGUIStyle();
			
			if(!CheckIsPlaying()) return;
			if(window==null) Init();
			
			List<Collectible> itemList=CollectibleDB.GetList();
			
			Undo.RecordObject(this, "window");
			Undo.RecordObject(CollectibleDB.GetDB(), "collectibleDB");
			
			if(GUI.Button(new Rect(Math.Max(260, window.position.width-120), 5, 100, 25), "Save")) TBE.SetDirty();
			
			Collectible newItem=null;
			TBE.Label(5, 7, 150, 17, "Add New Collectible:", "Drag collectible prefab to this slot to add it to the list");
			newItem=(Collectible)EditorGUI.ObjectField(new Rect(130, 7, width, height), newItem, typeof(Collectible), false);
			if(newItem!=null) Select(NewItem(newItem));
			
			float startX=5;	float startY=55;
			
			if(minimiseList){ if(GUI.Button(new Rect(startX, startY-20, 30, 18), ">>")) minimiseList=false; }
			else{ if(GUI.Button(new Rect(startX, startY-20, 30, 18), "<<")) minimiseList=true; }
			
			Vector2 v2=DrawItemList(startX, startY, itemList);
			startX=v2.x+25;
			
			if(itemList.Count==0) return;
			
			if(newSelectDelay>0){
				newSelectDelay-=1;		GUI.FocusControl(null);
				if(newSelectDelay==0) _SelectItem();
				else Repaint();
			}
			
			if(selectID>=itemList.Count) return;
			
			Rect visibleRect=new Rect(startX, startY, window.position.width-startX, window.position.height-startY);
			Rect contentRect=new Rect(startX, startY, contentWidth, contentHeight);
			
			scrollPos = GUI.BeginScrollView(visibleRect, scrollPos, contentRect);
			
				EditorGUI.BeginChangeCheck();
				
				v2=DrawItemConfigurator(startX, startY, itemList[selectID]);
				contentWidth=v2.x-startX;
				contentHeight=v2.y-55;
				
				if(EditorGUI.EndChangeCheck()){
					#if UNITY_2018_3_OR_NEWER
					//GameObject unitObj=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(itemList[selectID].gameObject));
					//Collectible selectedItem=unitObj.GetComponent<Collectible>();
					//selectedItem=itemList[selectID];
					//GameObject obj=PrefabUtility.SavePrefabAsset(selectedItem.gameObject);
					
					string assetPath = AssetDatabase.GetAssetPath(itemList[selectID].gameObject);
					
					GameObject itemObj=PrefabUtility.LoadPrefabContents(assetPath);
					Collectible selectedObj=itemObj.GetComponent<Collectible>();
					
					EditorUtility.CopySerialized(itemList[selectID], selectedObj);
					
					PrefabUtility.SaveAsPrefabAsset(itemObj, assetPath);
					PrefabUtility.UnloadPrefabContents(itemObj);
					#endif
				}
			
			GUI.EndScrollView();
			
			if(GUI.changed) TBE.SetDirty();
		}
		
		
		private Vector2 DrawItemConfigurator(float startX, float startY, Collectible item){
			float maxX=startX;
			
				startY=TBE.DrawBasicInfo(startX, startY, item);
			
				TBE.Label(startX, startY, width, height, "AbilityOnTriggered:", "The ability applies to target when triggered");
				
				int abIdx=AbilityFDB.GetPrefabIndex(item.abilityID);
				abIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), abIdx, AbilityFDB.label);
				if(abIdx>=0) item.abilityID=AbilityFDB.GetItemID(abIdx);
				if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")) item.abilityID=-1; 
			
			
			startY+=spaceY;
				
				TBE.Label(startX, startY+=spaceY, width, height, "Randomize Effect:", "Check to have the collectible randomly select one of the effects to apply to the target\nIf left unchecked, all effect specified will be applied");
				item.randomizedEffect=EditorGUI.Toggle(new Rect(startX+spaceX, startY, widthS, height), item.randomizedEffect);
			
				TBE.Label(startX, startY+=spaceY, width, height, "EffectOnTriggered:", "The effect applies to target when triggered");
				for(int i=0; i<item.effectIDList.Count; i++){
					TBE.Label(startX+spaceX-height, startY, width, height, "-");
					
					int effIdx=EffectDB.GetPrefabIndex(item.effectIDList[i]);		bool removeEff=false;
					effIdx = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), effIdx, EffectDB.label);
					if(GUI.Button(new Rect(startX+spaceX+width+3, startY, height, height), "-")){ item.effectIDList.RemoveAt(i); removeEff=true; }
					
					if(effIdx>=0 && !removeEff) item.effectIDList[i]=EffectDB.GetItemID(effIdx);
					
					startY+=spaceY;
				}
				
				int newEffID=-1;
				newEffID = EditorGUI.Popup(new Rect(startX+spaceX, startY, width, height), newEffID, EffectDB.label);
				if(newEffID>=0) newEffID=EffectDB.GetItemID(newEffID);
				if(newEffID>=0 && !item.effectIDList.Contains(newEffID)) item.effectIDList.Add(newEffID);
			
				
			startY+=spaceY;
				
			
				string txt="OPTIONAL: The effect object to spawn when the item is spawned on the grid";
				startY=DrawVisualObject(startX, startY+=spaceY, item.effectOnSpawn, "Effect On Spawned:", txt);
				
				startY+=spaceY*0.5f;
				
				txt="OPTIONAL: The effect object to spawn when the item is triggered\nYou can also add custom script on this object to have your own custom effect";
				startY=DrawVisualObject(startX, startY+=spaceY, item.effectOnTrigger, "Effect On Triggered:", txt);
			
				
			startY+=spaceY*2;
			
				GUIStyle style=new GUIStyle("TextArea");	style.wordWrap=true;
				cont=new GUIContent("Item description (for runtime and editor): ", "");
				EditorGUI.LabelField(new Rect(startX, startY, 400, height), cont);
				item.desp=EditorGUI.DelayedTextField(new Rect(startX, startY+spaceY-3, 270, 150), item.desp, style);
			
			return new Vector2(maxX, startY+170);
		}
		
		
		
		
		protected Vector2 DrawItemList(float startX, float startY, List<Collectible> itemList){
			List<EItem> list=new List<EItem>();
			for(int i=0; i<itemList.Count; i++){
				EItem item=new EItem(itemList[i].prefabID, itemList[i].itemName, itemList[i].icon);
				list.Add(item);
			}
			return DrawList(startX, startY, window.position.width, window.position.height, list);
		}
		
		public static int NewItem(Collectible item){ return window._NewItem(item); }
		private int _NewItem(Collectible item){
			if(CollectibleDB.GetList().Contains(item)) return selectID;
			
			item.prefabID=TBE.GenerateNewID(CollectibleDB.GetPrefabIDList());
			
			#if UNITY_2018_3_OR_NEWER
			GameObject obj=PrefabUtility.SavePrefabAsset(item.gameObject);
			item=obj.GetComponent<Collectible>();
			#endif
			
			CollectibleDB.GetList().Add(item);
			CollectibleDB.UpdateLabel();
			
			return CollectibleDB.GetList().Count-1;
		}
		
		protected override void DeleteItem(){
			CollectibleDB.GetList().RemoveAt(deleteID);
			CollectibleDB.UpdateLabel();
		}
		
		//~ protected override void SelectItem(){ SelectItem(selectID); }
		//~ private void SelectItem(int newID){ 
			//~ selectID=newID;
			//~ if(CollectibleDB.GetList().Count<=0) return;
			//~ selectID=Mathf.Clamp(selectID, 0, CollectibleDB.GetList().Count-1);
		//~ }
		
		protected override void SelectItem(){ }
		private void _SelectItem(){ 
			selectID=newSelectID;
			if(UnitDB.GetList().Count<=0) return;
			
			selectID=Mathf.Clamp(selectID, 0, UnitDB.GetList().Count-1);
			UpdateObjHierarchyList(UnitDB.GetList()[selectID].transform);
			
			Repaint();
		}
		
		protected override void ShiftItemUp(){ 	if(selectID>0) ShiftItem(-1); }
		protected override void ShiftItemDown(){ if(selectID<CollectibleDB.GetList().Count-1) ShiftItem(1); }
		private void ShiftItem(int dir){
			Collectible item=CollectibleDB.GetList()[selectID];
			CollectibleDB.GetList()[selectID]=CollectibleDB.GetList()[selectID+dir];
			CollectibleDB.GetList()[selectID+dir]=item;
			selectID+=dir;
		}
		
		
	}
	
}
