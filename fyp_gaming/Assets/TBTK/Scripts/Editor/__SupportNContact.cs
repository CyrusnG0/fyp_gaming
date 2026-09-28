using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;

namespace TBTK{

	public class __SupportNContact : EditorWindow {
		
		[MenuItem ("Tools/TBTK/Support and Contact", false, 100)]
		static void OpenSupportNContactWindow() { Init(); }
		
		private static __SupportNContact window;
		
		public static void Init () {
			window = (__SupportNContact)EditorWindow.GetWindow(typeof (__SupportNContact), true, "Support/Contact");
			window.minSize=new Vector2(375, 250);
		}
		
		void OnGUI () {
			if(window==null) Init();
			
			float startX=5;
			float startY=5;
			float spaceX=70;
			float spaceY=18;
			float width=230;
			float height=17;
			
			GUIStyle style=new GUIStyle("Label");
			style.fontSize=16;
			style.fontStyle=FontStyle.Bold;
			
			GUIContent cont=new GUIContent("Turn-Based ToolKit (TBTK)");
			EditorGUI.LabelField(new Rect(startX, startY, 300, 30), cont, style);
			
			EditorGUI.LabelField(new Rect(startX, startY+8, 300, height), "_______________________________________");
			
			startY+=30;
			EditorGUI.LabelField(new Rect(startX, startY, width, height), " - Version:");
			EditorGUI.LabelField(new Rect(startX+spaceX, startY, width, height), "3.1.5");
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), " - Release:");
			//EditorGUI.LabelField(new Rect(startX+spaceX, startY, width, height), "5 January 2021");
			EditorGUI.LabelField(new Rect(startX+spaceX, startY, width, height), "1 April 2022");
			
			startY+=15;
			
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), "Developed by K.Song Tan");
			
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), " - Email:");
			EditorGUI.TextField(new Rect(startX+spaceX, startY, width, height), "k.songtan@gmail.com");
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), " - Twitter:");
			EditorGUI.TextField(new Rect(startX+spaceX, startY, width, height), "SongTan@SongGameDev");
			
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), " - Website:");
			EditorGUI.TextField(new Rect(startX+spaceX, startY, width, height), "http://www.songgamedev.com/");
			if(GUI.Button(new Rect(startX+spaceX+width+10, startY, 50, height), "Open")){
				Application.OpenURL("http://www.songgamedev.com/");
			}
			
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, width, height), " - Support:");
			EditorGUI.TextField(new Rect(startX+spaceX, startY, width, height), "https://forum.unity.com/threads/turnbased-toolkit-tbtk.195426/");
			if(GUI.Button(new Rect(startX+spaceX+width+10, startY, 50, height), "Open")){
				Application.OpenURL("https://forum.unity.com/threads/turnbased-toolkit-tbtk.195426/");
			}
			
			
			startY+=spaceY;
			EditorGUI.LabelField(new Rect(startX, startY+=spaceY, 300, height), "        Your feedback is much appreciated!");
			if(GUI.Button(new Rect(startX, startY+=spaceY, 300, height), "Please Rate TBTK!")){
				Application.OpenURL("https://www.assetstore.unity3d.com/#!/content/10686");
			}
			
		}
		
	}

}