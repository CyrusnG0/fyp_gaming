using UnityEngine;
using System.Collections;

using TBTK;

namespace TBTK{

	public class TextureScroll : MonoBehaviour {
		
		public Material mat;
		
		public Vector2 uvAnimationRate = new Vector2( 1.0f, 0.0f );
		private Vector2 uvOffset = Vector2.zero;
		
		void Awake(){
			if(mat==null) mat=transform.GetComponent<Renderer>().material;
		}
		
		void Update(){
			uvOffset += ( uvAnimationRate * Time.deltaTime );
			mat.SetTextureOffset("_MainTex", uvOffset );
		}
		
	}

}
