
//using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {
	
	public class TBEditorWindow : EditorWindow {
		
		protected float contentHeight=0;
		protected float contentWidth=0;
		
		protected Vector2 scrollPos;
		
		protected GUIContent cont;
		protected GUIContent[] contL;
			
		protected int spaceX=120;
		protected int spaceY=18;
		protected int width=150;
		protected int widthS=40;
		protected int height=16;
		
		
		public Color white=Color.white;
		public Color grey=Color.grey;
		
		protected void CheckColor(int value, int TH){ GUI.color=(value>TH ? GUI.color : grey); }
		protected void ResetColor(){ GUI.color=white; }
		
		
		protected bool CheckIsPlaying(){
			if(Application.isPlaying){
				EditorGUILayout.HelpBox("Cannot edit while game is playing", MessageType.Info);
				return false;
			}
			return true;
		}
		
		protected float DrawVisualObject(float startX, float startY, VisualObject vo, string label, string tooltip){
			if(vo==null) vo=new VisualObject();
			
			TBE.Label(startX, startY, width, height, label, tooltip);
			vo.obj=(GameObject)EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), vo.obj, typeof(GameObject), true);
			//Debug.Log(vo.obj+"   "+EditorGUI.ObjectField(new Rect(startX+spaceX, startY, width, height), vo.obj, typeof(GameObject), true));
			
			TBE.Label(startX, startY+=spaceY, width, height, " - Auto Destroy:", "Check if the spawned effect should be destroyed automatically");
			if(vo.obj!=null) vo.autoDestroy=EditorGUI.Toggle(new Rect(startX+spaceX, startY, width, height), vo.autoDestroy);
			else TBE.Label(startX+spaceX, startY, width, height, "-");
			
			TBE.Label(startX, startY+=spaceY, width, height, " - Effect Duration:", "How long before the spawned effect object is destroyed");
			if(vo.obj!=null && vo.autoDestroy) vo.duration=EditorGUI.DelayedFloatField(new Rect(startX+spaceX, startY, widthS, height), vo.duration);
			else TBE.Label(startX+spaceX, startY, widthS, height, "-");
			
			return startY;
		}
		
		
		
		
		
		#region list
		protected virtual void SelectItem(){}
		protected virtual void DeleteItem(){}
		protected virtual void ShiftItemUp(){}
		protected virtual void ShiftItemDown(){}
		
		protected bool minimiseList=false;
		protected Rect visibleRectList;
		protected Rect contentRectList;
		protected Vector2 scrollPosList;
		
		public int deleteID=-1;
		public int selectID=0;
		public int newSelectID;
		public int newSelectDelay;
			
		protected void Select(int ID){
			if(selectID==ID) return;
			
			newSelectID=ID;
			newSelectDelay=5;
			
			if(selectID*35<scrollPosList.y) scrollPosList.y=selectID*35;
			if(selectID*35>scrollPosList.y+visibleRectList.height-40) scrollPosList.y=selectID*35-visibleRectList.height+40;
			
			SelectItem();
		}
		
		protected Vector2 DrawList(float startX, float startY, float winWidth, float winHeight, List<EItem> list, bool drawRemove=true, bool shiftItem=true, bool clampSelectID=true){
			float width=minimiseList ? 60 : 260;
			
			if(!minimiseList && shiftItem){
				if(GUI.Button(new Rect(startX+180, startY-20, 40, 18), "up")){
					ShiftItemUp();
					if(selectID*35<scrollPosList.y) scrollPosList.y=selectID*35;
				}
				if(GUI.Button(new Rect(startX+222, startY-20, 40, 18), "down")){
					ShiftItemDown();	
					if(visibleRectList.height-35<selectID*35) scrollPosList.y=(selectID+1)*35-visibleRectList.height+5;
				}
			}
			
			visibleRectList=new Rect(startX, startY, width+15, winHeight-startY-5);
			contentRectList=new Rect(startX, startY, width, list.Count*35+5);
			
			GUI.color=new Color(.8f, .8f, .8f, 1f);
			GUI.Box(visibleRectList, "");
			GUI.color=Color.white;
			
			scrollPosList = GUI.BeginScrollView(visibleRectList, scrollPosList, contentRectList);
			
				startY+=5;	startX+=5;
			
				for(int i=0; i<list.Count; i++){
					
					TBE.DrawSprite(new Rect(startX, startY+(i*35), 30, 30), list[i].icon);
					
					if(minimiseList){
						if(selectID==i) GUI.color = new Color(0, 1f, 1f, 1f);
						if(GUI.Button(new Rect(startX+35, startY+(i*35), 30, 30), "")) Select(i);
						GUI.color = Color.white;
						continue;
					}
					
					if(selectID==i) GUI.color = new Color(0, 1f, 1f, 1f);
					if(GUI.Button(new Rect(startX+35, startY+(i*35), 150+(!drawRemove ? 60 : 0), 30), list[i].name)) Select(i);
					GUI.color = Color.white;
					
					if(!drawRemove) continue;
					
					if(deleteID==i){
						if(GUI.Button(new Rect(startX+190, startY+(i*35), 60, 15), "cancel")) deleteID=-1;
						
						GUI.color = Color.red;
						if(GUI.Button(new Rect(startX+190, startY+(i*35)+15, 60, 15), "confirm")){
							if(selectID>=deleteID) Select(Mathf.Max(0, selectID-1));
							DeleteItem();	deleteID=-1;
						}
						GUI.color = Color.white;
					}
					else{
						if(GUI.Button(new Rect(startX+190, startY+(i*35), 60, 15), "remove")) deleteID=i;
					}
				}
			
			GUI.EndScrollView();
			
			if(clampSelectID) selectID=Mathf.Clamp(selectID, 0, list.Count-1);
			
			return new Vector2(startX+width+10, startY);
		}
		#endregion
		
		
		
		#region setup hierarchy list
		protected static int GetIndexFromHierarchy(Transform objT, Transform[] objHList){
			if(objT==null) return 0;
			for(int i=1; i<objHList.Length; i++){ if(objT==objHList[i]) return i; }
			return 0;
		}
		
		protected Transform[] objHierarchyList=new Transform[0];
		protected string[] objHierarchylabel=new string[0];
	
		protected void UpdateObjHierarchyList(Transform objT){//, SetObjListCallback callback){
			List<Transform> objHList=new List<Transform>();
			List<string> objNList=new List<string>();
			
			ObjectHierarchy ObjH=GetTransformInHierarchyRecursively(objT);
			
			objHList.Add(null);		objHList.Add(objT);
			objNList.Add(" - ");		objNList.Add("   -"+objT.name);
			
			for(int i=0; i<ObjH.listT.Count; i++) objHList.Add(ObjH.listT[i]);
			 for(int i=0; i<ObjH.listName.Count; i++){
				while(objNList.Contains(ObjH.listName[i])) ObjH.listName[i]+=".";
				objNList.Add(ObjH.listName[i]);
			}
			
			objHierarchyList=objHList.ToArray();
			objHierarchylabel=objNList.ToArray();
		}
		
		private static ObjectHierarchy GetTransformInHierarchyRecursively(Transform transform, string label="   "){
			ObjectHierarchy ObjH=new ObjectHierarchy();	label+="   ";
			foreach(Transform t in transform){
				ObjH.listT.Add(t);	ObjH.listName.Add(label+"-"+t.name);
				
				ObjectHierarchy tempHL=GetTransformInHierarchyRecursively(t, label);
				foreach(Transform tt in tempHL.listT) ObjH.listT.Add(tt);
				foreach(string ll in tempHL.listName) ObjH.listName.Add(ll);
			}
			return ObjH;
		}
		
		private class ObjectHierarchy{
			public List<Transform> listT=new List<Transform>();
			public List<string> listName=new List<string>();
		}
		#endregion
		
	}
	
}
