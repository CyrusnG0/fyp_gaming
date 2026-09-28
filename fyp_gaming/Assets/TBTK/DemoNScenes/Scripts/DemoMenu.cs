using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

using TBTK;

public class DemoMenu : MonoBehaviour {

	public class SceneInfo{
		public string sceneName="";
		public string sceneDesp="";
	}
	
	public List<UIButton> buttonList=new List<UIButton>();
	
	[Space(5)] 
	public Text lbTooltip;
	//public Image imgPreview;
	
	//[Space(10)]
	//public List<Sprite> previewImgList=new List<Sprite>();
	
	[Space(10)]
	public List<SceneInfo> sceneInfoList=new List<SceneInfo>();
	
	
	void Start () {
		for(int i=0; i<buttonList.Count; i++){
			buttonList[i].Init();
			
			int idx=i;	buttonList[i].button.onClick.AddListener(delegate { OnButton(idx); });
			buttonList[i].SetCallback(this.OnHoverButton, this.OnExitButton);
		}
		
		
		SceneInfo entry=new SceneInfo();
		entry.sceneName="Demo_Classic";
		entry.sceneDesp="A simple hex-grid level where the turn order is determine according to individual unit stats";
		sceneInfoList.Add(entry);
		
		entry=new SceneInfo();
		entry.sceneName="Demo_XCom";
		entry.sceneDesp="A level designed to imitate the gameplay of X-Com";
		sceneInfoList.Add(entry);
		
		entry=new SceneInfo();
		entry.sceneName="Demo_JRPG";
		entry.sceneDesp="A classic JRPG turn-based combat";
		sceneInfoList.Add(entry);
		
		entry=new SceneInfo();
		entry.sceneName="Demo_Persistent_PreGame";
		entry.sceneDesp="An simple example of how TBTK can be used for persistent unit progress through levels";
		sceneInfoList.Add(entry);
		
		
		OnExitButton(null);
	}
	
	
	public void OnHoverButton(GameObject butObj){
		int idx=0;
		for(int i=0; i<buttonList.Count; i++){
			if(buttonList[i].rootObj==butObj) idx=i;
		}
		
		if(idx<sceneInfoList.Count) lbTooltip.text=sceneInfoList[idx].sceneDesp;
		
		//~ if(idx<previewImgList.Count){
			//~ imgPreview.sprite=previewImgList[idx];
			//~ imgPreview.gameObject.SetActive(true);
		//~ }
	}
	public void OnExitButton(GameObject butObj){
		lbTooltip.text="";
		//imgPreview.gameObject.SetActive(false);
	}
	
	public void OnButton(int idx){
		if(idx<sceneInfoList.Count) SceneManager.LoadScene(sceneInfoList[idx].sceneName);
	}
	
	
	
}
