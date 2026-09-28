using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIScreen : MonoBehaviour {

		protected GameObject thisObj;
		protected RectTransform rectT;
		protected CanvasGroup canvasGroup;
		
		public virtual void Awake(){
			thisObj=gameObject;
			rectT=thisObj.GetComponent<RectTransform>();
			canvasGroup=thisObj.GetComponent<CanvasGroup>();
			if(canvasGroup==null) canvasGroup=thisObj.AddComponent<CanvasGroup>();
			
			//canvasGroup.interactable=false;
			//canvasGroup.blocksRaycasts=false;
			
			canvasGroup.alpha=0;
			rectT.anchoredPosition=new Vector3(0, 0, 0);
		}

		// Use this for initialization
		public virtual void Start () {
		
		}
		
		
		public virtual void Show(float duration=0.25f){ __Show(duration); }
		public void __Show(float duration){
			canvasGroup.interactable=true;
			canvasGroup.blocksRaycasts=true;
			
			thisObj.SetActive(true);
			
			if(duration>0) UI.FadeIn(canvasGroup, duration, thisObj);
			else canvasGroup.alpha=1;
		}
		public virtual void Hide(float duration=0.25f){ __Hide(duration); }
		public void __Hide(float duration){
			//canvasGroup.interactable=false;
			//canvasGroup.blocksRaycasts=false;
			
			if(duration>0) UI.FadeOut(canvasGroup, duration, thisObj);
			else{
				canvasGroup.alpha=0;
				thisObj.SetActive(false);
			}
		}
		
	}

}