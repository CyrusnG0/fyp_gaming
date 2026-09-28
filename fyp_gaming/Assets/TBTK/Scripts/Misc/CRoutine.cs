using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public class CRoutine : MonoBehaviour{
		public static CRoutine instance;
		public static CRoutine Get(){ Init(); return instance; }
		
		public static void Init(){
			if(instance!=null) return;
			
			GameObject obj=new GameObject("_CRoutine");
			instance=obj.AddComponent<CRoutine>();
			
			DontDestroyOnLoad(obj);
		}
		
		
		//Run a coroutine
		public static void Run(IEnumerator routine){ Init(); instance.StartCoroutine(routine); }
		
		
		//Call a function after a delay
		public static void Delay(float delay, Func<int> cb, bool rt=false){ Init(); instance.StartCoroutine(instance._Delay(delay, cb, rt)); }
		IEnumerator _Delay(float delay, Func<int> callback, bool realTime){ 
			if(realTime)	yield return instance.StartCoroutine(WaitForRealSeconds(delay));
			else				yield return new WaitForSeconds(delay);
			callback();
		}
		
		
		public static IEnumerator WaitForRealSeconds(float time){ Init();
			float start = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup < start + time) yield return null;
		}
	}
	
}
