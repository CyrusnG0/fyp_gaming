using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace TBTK{

	public class UIPrompt : UIScreen {

		public delegate void Callback();
		
		public Callback confirmCallback;
		public Callback cancelCallback;
		
		public Text labelMsg;
		
		public UIButton buttonContinue;
		public UIButton buttonConfirm;
		public UIButton buttonCancel;
		
		
		private static UIPrompt instance;
		
		public override void Awake(){
			base.Awake();
			
			instance=this;
		}
		
		public override void Start(){
			buttonContinue.Init();
			buttonContinue.button.onClick.AddListener(delegate { OnConfirmButton(); });
			
			buttonConfirm.Init();
			buttonConfirm.button.onClick.AddListener(delegate { OnConfirmButton(); });
			
			buttonCancel.Init();
			buttonCancel.button.onClick.AddListener(delegate { OnCancelButton(); });
			
			canvasGroup.alpha=1;
			thisObj.SetActive(false);
		}
		
		
		public void OnConfirmButton(){
			if(confirmCallback!=null) confirmCallback();
			thisObj.SetActive(false);
		}
		public void OnCancelButton(){
			if(cancelCallback!=null) cancelCallback();
			thisObj.SetActive(false);
		}
		
		
		public static void Show1(string msg, Callback cb, string butText="CONTINUE"){ instance._Show(msg, cb, butText); }
		public void _Show(string msg, Callback cb, string butText="CONTINUE"){
			labelMsg.text=msg;
			buttonContinue.label.text=butText;
			confirmCallback=cb;
			
			buttonContinue.SetActive(true);
			buttonConfirm.SetActive(false);
			buttonCancel.SetActive(false);
			
			thisObj.SetActive(true);
		}
		
		
		public static void Show2(string msg, Callback cbConfirm, Callback cbCancel, string butText1="CONFIRM", string butText2="CANCEL"){
			instance._Show(msg, cbConfirm, cbCancel, butText1, butText2);
		}
		public void _Show(string msg, Callback cbConfirm, Callback cbCancel, string butText1="CONFIRM", string butText2="CANCEL"){ 
			labelMsg.text=msg;
			buttonConfirm.label.text=butText1;
			buttonCancel.label.text=butText2;
			
			confirmCallback=cbConfirm;
			cancelCallback=cbCancel;
			
			buttonContinue.SetActive(false);
			buttonConfirm.SetActive(true);
			buttonCancel.SetActive(true);
			
			thisObj.SetActive(true);
		}
		
	}

}