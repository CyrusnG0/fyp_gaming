//using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK {

	public class TBE {
		
		#if UNITY_2018_2_OR_OLDER
		public static bool IsPrefabOrPrefabInstance(GameObject obj){
			PrefabType type=PrefabUtility.GetPrefabType(obj);
			return type==PrefabType.Prefab || type==PrefabType.PrefabInstance;
		}
		public static bool IsPrefab(GameObject obj){
			return obj==null ? false : PrefabUtility.GetPrefabType(obj)==PrefabType.Prefab;
		}
		#endif
		
		
		public static DamageTableDB damageTableDB;
		public static AbilityUDB uAbilityDB;
		public static AbilityFDB fAbilityDB;
		public static EffectDB effectDB;
		public static PerkDB perkDB;
		public static CollectibleDB collectibleDB;
		public static UnitDB unitDB;
		
		public static GUIStyle headerS;
		public static GUIStyle foldoutS;
		public static GUIStyle foldoutLS;
		public static GUIStyle conflictS;
		
		private static bool init=false;
		public static void Init(){
			if(init) return;
			
			init=true;	//Debug.Log(" - Init Editor - ");
			
			damageTableDB=DamageTableDB.Init();
			uAbilityDB=AbilityUDB.Init();
			fAbilityDB=AbilityFDB.Init();
			effectDB=EffectDB.Init();
			perkDB=PerkDB.Init();
			collectibleDB=CollectibleDB.Init();
			unitDB=UnitDB.Init();
			
			DamageTableDB.UpdateLabel();
			AbilityUDB.UpdateLabel();
			AbilityFDB.UpdateLabel();
			EffectDB.UpdateLabel();
			PerkDB.UpdateLabel();
			CollectibleDB.UpdateLabel();
			UnitDB.UpdateLabel();
		}
		
		private static bool initUIStyle=false;
		public static void InitGUIStyle(){
			if(initUIStyle) return;
			
			initUIStyle=true;
			
			headerS=new GUIStyle("Label");
			headerS.fontStyle=FontStyle.Bold;
			
			foldoutS=new GUIStyle("foldout");
			foldoutS.fontStyle=FontStyle.Bold;
			foldoutS.normal.textColor = Color.grey;
			
			foldoutLS=new GUIStyle("foldout");
			foldoutLS.normal.textColor = Color.grey;
			
			conflictS=new GUIStyle("Label");
			conflictS.normal.textColor = Color.red;
		}
		
		
		
		public static GUIContent[] SetupContL(string[] label, string[] tooltip){
			GUIContent[] contL=new GUIContent[label.Length];
			for(int i=0; i<contL.Length; i++) contL[i]=new GUIContent(label[i], tooltip[i]);
			return contL;
		}
		
		public static void Label(float x, float y, float width, float height, string lb, string tooltip="", GUIStyle style=null){
			if(style==null) EditorGUI.LabelField(new Rect(x, y, width, height), new GUIContent(lb, tooltip));
			else EditorGUI.LabelField(new Rect(x, y, width, height), new GUIContent(lb, tooltip), style);
		}
		
		public static int GenerateNewID(List<int> list, int ID=0){
			while(list.Contains(ID)) ID+=1;
			return ID;
		}
		
		
		public static string[] GetArmorLabel(){ return DamageTableDB.armorlb; }
		public static string[] GetDamageLabel(){ return DamageTableDB.damagelb; }
		
		
		public static void SetDirty(){ 
			EditorUtility.SetDirty(damageTableDB);
			EditorUtility.SetDirty(uAbilityDB);
			EditorUtility.SetDirty(fAbilityDB);
			EditorUtility.SetDirty(effectDB);
			EditorUtility.SetDirty(perkDB);
			EditorUtility.SetDirty(collectibleDB);
			EditorUtility.SetDirty(unitDB);
			
			#if UNITY_2018_3_OR_NEWER
			for(int i=0; i<unitDB.objList.Count; i++) EditorUtility.SetDirty(unitDB.objList[i]);
			for(int i=0; i<collectibleDB.objList.Count; i++) EditorUtility.SetDirty(collectibleDB.objList[i]);
			#else
			for(int i=0; i<unitDB.unitList.Count; i++) EditorUtility.SetDirty(unitDB.unitList[i]);
			for(int i=0; i<collectibleDB.collectibleList.Count; i++) EditorUtility.SetDirty(collectibleDB.collectibleList[i]);
			#endif
		}
		
		
		public static string AttackTT(){ return "\n\ndamage mul.=attack/(attack+defense)"; }
		public static string ChanceTT(){ return "\n\n0.1 being 10%, 0.7 being 70% and so on"; }
		
		
		public static float DrawBasicInfo(float startX, float startY, TBTKItem item){
			int spaceX=120; int spaceY=18; int width=150; int height=16;
			DrawSprite(new Rect(startX, startY, 60, 60), item.icon);	startX+=65;
			
			TBE.Label(startX, startY+=5, width, height, "Name:", "The item name to be displayed in game");
			item.name=EditorGUI.TextField(new Rect(startX+spaceX-65, startY, width, height), item.name);
			
			TBE.Label(startX, startY+=spaceY, width, height, "Icon:", "The item icon to be displayed in game, must be a sprite");
			item.icon=(Sprite)EditorGUI.ObjectField(new Rect(startX+spaceX-65, startY, width, height), item.icon, typeof(Sprite), false);
			
			TBE.Label(startX, startY+=spaceY, width, height, "PrefabID: "+item.prefabID.ToString());
			//TBE.Label(startX+spaceX-65, startY, width, height, item.prefabID.ToString(), "");
			
			return startY+spaceY;
		}
		
		//~ public static float DrawBasicInfo(float startX, float startY, TBMonoItem item){
			//~ int spaceX=120; int spaceY=18; int width=150; int height=16;
			//~ DrawSprite(new Rect(startX, startY, 60, 60), unit.icon);	startX+=65;
			
			//~ TBE.Label(startX, startY+=5, width, height, "Name:", "The item name to be displayed in game");
			//~ unit.unitName=EditorGUI.TextField(new Rect(startX+spaceX-65, startY, width, height), unit.unitName);
			
			//~ TBE.Label(startX, startY+=spaceY, width, height, "Icon:", "The item icon to be displayed in game, must be a sprite");
			//~ unit.icon=(Sprite)EditorGUI.ObjectField(new Rect(startX+spaceX-65, startY, width, height), unit.icon, typeof(Sprite), false);
			
			//~ TBE.Label(startX, startY+=spaceY, width, height, "Prefab:", "The prefab object of the unit\nClick this to highlight it in the ProjectTab");
			//~ EditorGUI.ObjectField(new Rect(startX+spaceX-65, startY, width, height), unit.gameObject, typeof(GameObject), false);
			
			//~ return startY+spaceY*2;
		//~ }
		
		public static float DrawBasicInfo(float startX, float startY, TBMonoItem item){
			int spaceX=120; int spaceY=18; int width=150; int height=16;
			DrawSprite(new Rect(startX, startY, 60, 60), item.icon);	startX+=65;
			
			TBE.Label(startX, startY+=5, width, height, "Name:", "The item name to be displayed in game");
			item.itemName=EditorGUI.TextField(new Rect(startX+spaceX-65, startY, width, height), item.itemName);
			
			TBE.Label(startX, startY+=spaceY, width, height, "Icon:", "The item icon to be displayed in game, must be a sprite");
			item.icon=(Sprite)EditorGUI.ObjectField(new Rect(startX+spaceX-65, startY, width, height), item.icon, typeof(Sprite), false);
			
			TBE.Label(startX, startY+=spaceY, width, height, "Prefab:", "The prefab object of the unit\nClick this to highlight it in the ProjectTab");
			EditorGUI.ObjectField(new Rect(startX+spaceX-65, startY, width, height), item.gameObject, typeof(GameObject), false);
			
			return startY+spaceY*2;
		}
		
		
		public static bool DrawSprite(Rect rect, Sprite sprite, string tooltip="", bool drawBox=true){
			if(drawBox) GUI.Box(rect, new GUIContent("", tooltip));
			
			if(sprite!=null){
				Texture t = sprite.texture;
				Rect tr = sprite.textureRect;
				Rect r = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height );
				
				rect.x+=2;
				rect.y+=2;
				rect.width-=4;
				rect.height-=4;
				GUI.DrawTextureWithTexCoords(rect, t, r);
			}
			
			//if(addXButton){
			//	rect.width=12;	rect.height=12;
			//	bool flag=GUI.Button(rect, "X", GetXButtonStyle());
			//	return flag;
			//}
			
			return false;
		}
		
	}
	
}
