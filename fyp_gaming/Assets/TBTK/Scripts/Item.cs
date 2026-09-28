using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace TBTK{

	[System.Serializable]
	public class TBTKItem{
		[HideInInspector] public int prefabID=-1;
		[HideInInspector] public int instanceID=-1;	
		
		public Sprite icon;
		public string name="Item";
		public string desp="Item's description";
		
		public void Clone(TBTKItem src, TBTKItem tgt){
			tgt.prefabID=src.prefabID;	tgt.instanceID=src.instanceID;
			tgt.icon=src.icon;	tgt.name=src.name;		tgt.desp=src.desp;
		}
	}
	
	
	public class TBMonoItem : MonoBehaviour {
		[HideInInspector] public int prefabID=-1;
		[HideInInspector] public int instanceID=-1;	
		
		public Sprite icon;
		public string itemName="Name";
		public string desp="Description";
		
		[Space(5)]
		public Transform thisT;			public Transform GetT(){ return thisT; }
		public GameObject thisObj;	public GameObject GetObj(){ return thisObj; }
		
		public Node node;
		public Vector3 GetPos(){ return thisT!=null ? thisT.position : transform.position; }
	}

}