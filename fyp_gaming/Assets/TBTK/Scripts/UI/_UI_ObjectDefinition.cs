using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;


namespace TBTK{
	
	#region UIObject
	[System.Serializable]
	public class UIObject{
		public GameObject rootObj;
		[HideInInspector] public Transform rootT;
		[HideInInspector] public RectTransform rectT;
		
		[HideInInspector] public CanvasGroup canvasG;
		
		[HideInInspector] public Image image;
		[HideInInspector] public Text label;
		
		[HideInInspector] public UIItemCallback itemCallback;
		
		public UIObject(){}
		public UIObject(GameObject obj){ rootObj=obj; Init(); }
		
		public virtual void Init(){
			if(rootObj==null){ Debug.LogWarning("Unassgined rootObj"); return; }
			
			rootT=rootObj.transform;
			rectT=rootObj.GetComponent<RectTransform>();
			
			foreach(Transform child in rectT){
				if(child.name=="Image") image=child.GetComponent<Image>();
				else if(child.name=="Text") label=child.GetComponent<Text>();
			}
		}
		
		public static UIObject Clone(GameObject srcObj, string name="", Vector3 posOffset=default(Vector3)){
			GameObject newObj=UI.Clone(srcObj, name, posOffset);
			return new UIObject(newObj);
		}
		
		public virtual void SetCallback(Callback enter=null, Callback exit=null){
			itemCallback=rootObj.GetComponent<UIItemCallback>();
			if(itemCallback==null) itemCallback=rootObj.AddComponent<UIItemCallback>();
			itemCallback.SetEnterCallback(enter);
			itemCallback.SetExitCallback(exit);
		}
		
		public virtual void SetActive(bool flag){ rootObj.SetActive(flag); }
		
		public void SetImage1(bool flag){ if(image!=null) image.gameObject.SetActive(flag); }
		public void SetLabel1(bool flag){ if(label!=null) label.gameObject.SetActive(flag); }
		
		public void SetImage(Sprite spr){ if(image!=null) image.sprite=spr; }
		public void SetLabel(string txt){ if(label!=null) label.text=txt; }
		
		
		//not being used
		public void SetSound(AudioClip eClip, AudioClip dClip){ if(itemCallback!=null) itemCallback.SetSound(eClip, dClip); }
		public void DisableSound(bool disableHover, bool disablePress){ itemCallback.DisableSound(disableHover, disablePress); }
	}
	#endregion
	
	
	#region UIButton
	[System.Serializable]
	public class UIButton : UIObject{
		[HideInInspector] public Text label2;
		
		[HideInInspector] public Image image2;
		
		[HideInInspector] public Image hovered;
		[HideInInspector] public Image disabled;
		[HideInInspector] public Image highlight;
		
		[HideInInspector] public Button button;
		
		public UIButton(){}
		public UIButton(GameObject obj){ rootObj=obj; Init(); }
		
		public override void Init(){
			base.Init();
			
			button=rootObj.GetComponent<Button>();
			canvasG=rootObj.GetComponent<CanvasGroup>();
			
			foreach(Transform child in rectT){
				if(child.name=="TextAlt")				label2=child.GetComponent<Text>();
				else if(child.name=="ImageAlt")	image2=child.GetComponent<Image>();
				else if(child.name=="Hovered") 	hovered=child.GetComponent<Image>();
				else if(child.name=="Disabled") 	disabled=child.GetComponent<Image>();
				else if(child.name=="Highlight") 	highlight=child.GetComponent<Image>();
			}
		}
		
		public static new UIButton Clone(GameObject srcObj, string name="", Vector3 posOffset=default(Vector3)){
			GameObject newObj=UI.Clone(srcObj, name, posOffset);
			return new UIButton(newObj);
		}
		
		public override void SetCallback(Callback enter=null, Callback exit=null){ base.SetCallback(enter, exit); }
		
		//public override void SetActive(bool flag){ base.SetActive(flag); }
		
		public void SetImage2(Sprite spr){ if(image2!=null) image2.sprite=spr; }
		public void SetLabel2(string txt){ if(label2!=null) label2.text=txt; }
		
		public void SetImage2(bool flag){ if(image2!=null) image2.gameObject.SetActive(flag); }
		public void SetLabel2(bool flag){ if(label2!=null) label2.gameObject.SetActive(flag); }
		public void SetHighlight(bool flag){ if(highlight!=null) highlight.gameObject.SetActive(flag); }
	}
	#endregion
	
	
	
	#region callback
	public delegate void Callback(GameObject uiObj);
	
	public class UIItemCallback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler{	//, IPointerDownHandler, IPointerUpHandler{
		
		private Callback enterCB;
		private Callback exitCB;
		
		public void SetEnterCallback(Callback callback){ enterCB=callback; }
		public void SetExitCallback(Callback callback){ exitCB=callback; }
		
		public void OnPointerEnter(PointerEventData eventData){ 
			//if(enterClip!=null && button!=null && button.interactable) AudioManager.PlayUISound(enterClip);
			if(enterCB!=null) enterCB(thisObj);
		}
		public void OnPointerExit(PointerEventData eventData){ 
			if(exitCB!=null) exitCB(thisObj);
		}
		
		
		private GameObject thisObj;
		void Awake(){
			thisObj=gameObject;
			SetupAudioClip();
		}
		
		
		//audio is not being used in this package
		private bool useCustomAudioClip=false;
		public AudioClip enterClip;
		public AudioClip downClip;
		
		void SetupAudioClip(){
			if(useCustomAudioClip) return;
			//enterClip=AudioManager.GetHoverButtonSound();
			//downClip=AudioManager.GetPressButtonSound();
		}
		
		public void SetSound(AudioClip eClip, AudioClip dClip){
			useCustomAudioClip=true;		enterClip=eClip;	downClip=dClip;
		}
		
		public void DisableSound(bool disableHover, bool disablePress){
			if(disableHover) enterClip=null;
			if(disablePress) downClip=null;
		}
	}
	#endregion

}