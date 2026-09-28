using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rand {
	
	public static int Range(int min, int max){ return (int)Random.Range(min, max); }
	public static float Range(float min, float max){ return Random.Range(min, max); }
	
	public static float value(){ return Random.value; }
	
	
	public static int GetOption(int count){ return Random.Range(0, count); }
	
	public static int GetOption(List<float> odds){
		float th=0;
		List<float> thList=new List<float>();
		for(int i=0; i<odds.Count; i++){
			//Debug.Log(" - "+odds[i]);
			th+=odds[i];
			thList.Add(th);
		}
		
		if(th==0) return -1;
		
		//string text="";
		//for(int i=0; i<thList.Count; i++) text+=thList[i]+"  ";
		//Debug.Log("      || "+text);
		
		float rand=UnityEngine.Random.Range(0, thList[thList.Count-1]);
		for(int i=0; i<thList.Count; i++){
			if(rand<thList[i]) return i;
		}
		
		return 0;
	}
	
}
