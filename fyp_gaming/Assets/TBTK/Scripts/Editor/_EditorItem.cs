using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK {

	[System.Serializable]
	public class EItem{
		public int ID=-1;
		public string name;
		public Sprite icon;
		
		public EItem(int id=-1, string n="", Sprite ic=null){
			ID=id;	name=n;	icon=ic;
		}
	}

}