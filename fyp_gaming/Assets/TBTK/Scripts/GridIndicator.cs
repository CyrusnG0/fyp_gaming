using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public class GridIndicator : MonoBehaviour {
		
		[Header("Square")]
		public Transform sqCursor;
		public Transform sqSelect;
		public Transform sqMovable;
		public Transform sqHostile;
		public Transform sqAbility;
		public Transform sqDeploy;
		public Transform sqFog;
		
		[HideInInspector] public List<Transform> sqMovableList=new List<Transform>();
		[HideInInspector] public List<Transform> sqHostileList=new List<Transform>();
		[HideInInspector] public List<Transform> sqAbilityList=new List<Transform>();
		[HideInInspector] public List<Transform> sqDeployList=new List<Transform>();
		
		
		
		[Header("Hex")]
		public Transform hexCursor;
		public Transform hexSelect;
		public Transform hexMovable;
		public Transform hexHostile;
		public Transform hexAbility;
		public Transform hexDeploy;
		public Transform hexFog;
		
		[HideInInspector] public List<Transform> hexMovableList=new List<Transform>();
		[HideInInspector] public List<Transform> hexHostileList=new List<Transform>();
		[HideInInspector] public List<Transform> hexAbilityList=new List<Transform>();
		[HideInInspector] public List<Transform> hexDeployList=new List<Transform>();
		
		
		
		[Header("Common")]
		public Transform coverOverlayF;
		public Transform coverOverlayH;
		
		[HideInInspector] public List<Transform> coverOverlayFList=new List<Transform>();
		[HideInInspector] public List<Transform> coverOverlayHList=new List<Transform>();
		
		[HideInInspector] public List<Transform> fogList=new List<Transform>();
		
		
		
		[Header("Runtime")]
		public Transform cursor;
		public Transform select;
		
		[HideInInspector] public List<Transform> extraCursorList=new List<Transform>();	//for aoe
		
		
		public static GridIndicator instance;
		
		public void Awake(){
			instance=this;
		}
			
		void Start(){
			bool manualDeployment=UnitManager.RequireManualDeployment();
			
			if(GridManager.IsHexGrid()){
				for(int i=0; i<30; i++) AddElement(hexMovable, hexMovableList);
				for(int i=0; i<15; i++) AddElement(hexHostile, hexHostileList);
				for(int i=0; i<20; i++) AddElement(hexAbility, hexAbilityList);
				if(manualDeployment){ for(int i=0; i<10; i++) AddElement(hexDeploy, hexDeployList); }
			}
			else{
				for(int i=0; i<30; i++) AddElement(sqMovable, sqMovableList);
				for(int i=0; i<15; i++) AddElement(sqHostile, sqHostileList);
				for(int i=0; i<20; i++) AddElement(sqAbility, sqAbilityList);
				if(manualDeployment){ for(int i=0; i<10; i++) AddElement(sqDeploy, hexDeployList); }
			}
			
			if(GameControl.EnableFogOfWar()){
				if(GridManager.IsHexGrid() && hexFog!=null){ 
					for(int i=0; i<GridManager.GetNodeCount(); i++) AddElement(hexFog, fogList);
				}
				if(GridManager.IsSquareGrid() && sqFog!=null){ 
					for(int i=0; i<GridManager.GetNodeCount(); i++) AddElement(sqFog, fogList);
				}
			}
			
			if(GameControl.EnableCoverSystem()){
				int count=GridManager.IsHexGrid() ? 6 : 4 ;
				float scale=GridManager.IsHexGrid() ? 0.45f : 0.6f ;
				for(int i=0; i<count; i++) AddElement(coverOverlayF, coverOverlayFList, false, scale);
				for(int i=0; i<count; i++) AddElement(coverOverlayH, coverOverlayHList, false, scale);
			}
			
			select=GridManager.IsHexGrid() ? hexSelect : sqSelect ;
			select=AddElement(select);
			select.gameObject.SetActive(true);
			
			cursor=GridManager.IsHexGrid() ? hexCursor : sqCursor ;
			cursor=AddElement(cursor);
			cursor.gameObject.SetActive(true);
			
			SetCursor(null);	cursor.position=new Vector3(0, 9999, 0);
			SetSelect(null);
		}
		
		
		void OnEnable(){ TBTK.onActionInProgressE += OnActionInProgress ; }
		void OnDisable(){ TBTK.onActionInProgressE -= OnActionInProgress ; }
		
		void OnActionInProgress(bool flag){ if(flag) HideAll(); }
		
		
		
		
		private Node lastNode;
		
		
		private void CheckCursorCount(int count){
			while(count>extraCursorList.Count){
				Transform cursorX=AddElement(cursor, extraCursorList, true);
				cursorX.localScale=cursor.localScale;
			}
		}
		
		
		public static void SetCursor(Node node){ instance._SetCursor(node); }
		public void _SetCursor(Node node){
			if(node==lastNode) return;
			
			lastNode=node;
			
			if(AbilityManager.IsWaitingForTarget()){
				if(node!=null){
					
					List<Node> list=new List<Node>();
					
					if(AbilityManager.IsCurAbilityCone()){
						int fov=AbilityManager.GetCurAbilityFOV();
						list=GridManager.GetNodesInACone(AbilityManager.GetCurNode(), node, AbilityManager.GetCurAbilityRange(), AbilityManager.GetCurAbilityRangeMin(), fov);
						
						CheckCursorCount(list.Count);
						
						node=null;
					}
					else{
						int aoe=AbilityManager.GetCurAbilityAOE();
						if(aoe>0){
							list=GridManager.GetNodesWithinDistance(node, aoe);
							
							CheckCursorCount(list.Count);
						}
					}
					
					for(int i=0; i<extraCursorList.Count; i++){
						if(i<list.Count) extraCursorList[i].position=list[i].GetPos();
						else extraCursorList[i].position=new Vector3(0, 9999, 0);
					}
				}
				else{
					for(int i=0; i<extraCursorList.Count; i++) extraCursorList[i].position=new Vector3(0, 9999, 0);
				}
			}
			
			cursor.position=node!=null ? node.GetPos() : new Vector3(0, 9999, 0);
			
			if(AbilityManager.IsWaitingForTarget()) return;
			if(!GameControl.EnableCoverSystem()) return;
			
			for(int i=0; i<coverOverlayFList.Count; i++) coverOverlayFList[i].gameObject.SetActive(false);
			for(int i=0; i<coverOverlayHList.Count; i++) coverOverlayHList[i].gameObject.SetActive(false);
			
			if(GridManager.CanMoveTo(node)){
				for(int i=0; i<node.coverList.Count; i++){
					if(node.coverList[i]==0) continue;
					
					Transform coverOverlay=node.coverList[i]==1 ? coverOverlayHList[i] : coverOverlayFList[i] ;
					
					coverOverlay.rotation=Quaternion.Euler(0, 360-Utility.Vector3ToAngle(GridManager.nodeDirList[i])-90, 0);
					coverOverlay.position=node.GetPos()+GridManager.nodeDirList[i]*GridManager.GetNodeSize()*0.375f;
					coverOverlay.gameObject.SetActive(true);
				}
			}
		}
		
		public static void SetSelect(Node node){
			instance.select.position=node!=null ? node.GetPos() : new Vector3(0, 9999, 0);
		}
		
		
		
		public static void ShowFog(){ instance._ShowFog(); }
		public void _ShowFog(){
			if(GridManager.IsHexGrid() && hexFog==null) return;
			if(GridManager.IsSquareGrid() && sqFog==null) return;
			
			List<List<Node>> grid=GridManager.GetGrid();
			
			int count=0;
			for(int x=0; x<grid.Count; x++){
				for(int z=0; z<grid[x].Count; z++){
					if(grid[x][z].IsVisible()) continue;
					
					if(count>=fogList.Count) AddElement(GridManager.IsHexGrid() ? hexFog : sqFog, fogList);
					
					fogList[count].position=grid[x][z].GetPos();
					fogList[count].gameObject.SetActive(true);
					count+=1;
				}
			}
			
			for(int i=count; i<fogList.Count; i++) fogList[i].gameObject.SetActive(false);
			
				//~ if(nList.Count>hexDeployList.Count){
					//~ int count=nList.Count-hexDeployList.Count;
					//~ for(int i=0; i<count; i++) AddElement(hexDeploy, hexDeployList);
				//~ }
				
				//~ for(int i=0; i<hexDeployList.Count; i++){
					//~ if(i<nList.Count) hexDeployList[i].position=nList[i].GetPos();
					//~ hexDeployList[i].gameObject.SetActive(i<nList.Count);
				//~ }
		}
		//~ public static void ClearFog(){ instance._ClearFog(); }
		//~ public void _ClearFog(){
			//~ if(GridManager.IsHexGrid()){ for(int i=0; i<hexFogList.Count; i++) hexFogList[i].gameObject.SetActive(false); }
			//~ else{ for(int i=0; i<sqFogList.Count; i++) sqFogList[i].gameObject.SetActive(false); }
		//~ }
		
		
		
		public static void ShowDeployment(List<Node> nList){ instance._ShowDeployment(nList); }
		public void _ShowDeployment(List<Node> nList){
			
			if(GridManager.IsHexGrid()){
				if(nList.Count>hexDeployList.Count){
					int count=nList.Count-hexDeployList.Count;
					for(int i=0; i<count; i++) AddElement(hexDeploy, hexDeployList);
				}
				
				for(int i=0; i<hexDeployList.Count; i++){
					if(i<nList.Count) hexDeployList[i].position=nList[i].GetPos();
					hexDeployList[i].gameObject.SetActive(i<nList.Count);
				}
			}
			else{
				if(nList.Count>sqDeployList.Count){
					int count=nList.Count-sqDeployList.Count;
					for(int i=0; i<count; i++) AddElement(sqDeploy, sqDeployList);
				}
				
				for(int i=0; i<sqDeployList.Count; i++){
					if(i<nList.Count) sqDeployList[i].position=nList[i].GetPos();
					sqDeployList[i].gameObject.SetActive(i<nList.Count);
				}
			}
		}
		public static void HideDeployment(){ instance._HideDeployment(); }
		public void _HideDeployment(){
			if(GridManager.IsHexGrid()){
				for(int i=0; i<hexDeployList.Count; i++) hexDeployList[i].gameObject.SetActive(false);
			}
			else{
				for(int i=0; i<sqDeployList.Count; i++) sqDeployList[i].gameObject.SetActive(false);
			}
		}
	
	
		public static void ShowMovable(List<Node> nList){ instance._ShowMovable(nList); }
		public void _ShowMovable(List<Node> nList){
			HideAbility();
			
			if(GridManager.IsHexGrid()){
				if(nList.Count>hexMovableList.Count){
					int count=nList.Count-hexMovableList.Count;
					for(int i=0; i<count; i++) AddElement(hexMovable, hexMovableList);
				}
				
				for(int i=0; i<hexMovableList.Count; i++){
					if(i<nList.Count) hexMovableList[i].position=nList[i].GetPos();
					hexMovableList[i].gameObject.SetActive(i<nList.Count);
				}
			}
			else{
				if(nList.Count>sqMovableList.Count){
					int count=nList.Count-sqMovableList.Count;
					for(int i=0; i<count; i++) AddElement(sqMovable, sqMovableList);
				}
				
				for(int i=0; i<sqMovableList.Count; i++){
					if(i<nList.Count) sqMovableList[i].position=nList[i].GetPos();
					sqMovableList[i].gameObject.SetActive(i<nList.Count);
				}
			}
		}
		
		
		
		[HideInInspector]	public List<Node> cachedHostileList=new List<Node>();
		
		public static void ShowHostile(List<Node> nList){ instance._ShowHostile(nList); }
		public void _ShowHostile(List<Node> nList, bool cacheList=true){
			HideAbility();
			
			if(GridManager.IsHexGrid()){
				if(nList.Count>hexHostileList.Count){
					int count=nList.Count-hexHostileList.Count;
					for(int i=0; i<count; i++) AddElement(hexHostile, hexHostileList);
				}
				
				for(int i=0; i<hexHostileList.Count; i++){
					if(i<nList.Count) hexHostileList[i].position=nList[i].GetPos();
					hexHostileList[i].gameObject.SetActive(i<nList.Count);
				}
			}
			else{
				if(nList.Count>sqHostileList.Count){
					int count=nList.Count-sqHostileList.Count;
					for(int i=0; i<count; i++) AddElement(sqHostile, sqHostileList);
				}
				
				for(int i=0; i<sqHostileList.Count; i++){
					if(i<nList.Count) sqHostileList[i].position=nList[i].GetPos();
					sqHostileList[i].gameObject.SetActive(i<nList.Count);
				}
			}
			
			if(cacheList) cachedHostileList=nList;
		}
		
		private static bool previewingHostile=false;
		public static void PreviewHostile(List<Node> nList){ instance._PreviewHostile(nList); }
		public void _PreviewHostile(List<Node> nList){
			previewingHostile=true;
			_ShowHostile(nList, false);
		}
		public static void ClearPreviewHostile(){
			if(!previewingHostile) return;
			previewingHostile=false;
			ShowHostile(instance.cachedHostileList);
		}
		
		
		
		public static void ShowAbility(List<Node> nList){ instance._ShowAbility(nList); }
		public void _ShowAbility(List<Node> nList){
			HideNormal();
			
			if(GridManager.IsHexGrid()){
				if(nList.Count>hexAbilityList.Count){
					int count=nList.Count-hexAbilityList.Count;
					for(int i=0; i<count; i++) AddElement(hexAbility, hexAbilityList);
				}
				
				for(int i=0; i<hexAbilityList.Count; i++){
					if(i<nList.Count) hexAbilityList[i].position=nList[i].GetPos();
					hexAbilityList[i].gameObject.SetActive(i<nList.Count);
				}
			}
			else{
				if(nList.Count>sqAbilityList.Count){
					int count=nList.Count-sqAbilityList.Count;
					for(int i=0; i<count; i++) AddElement(sqAbility, sqAbilityList);
				}
				
				for(int i=0; i<sqAbilityList.Count; i++){
					if(i<nList.Count) sqAbilityList[i].position=nList[i].GetPos();
					sqAbilityList[i].gameObject.SetActive(i<nList.Count);
				}
			}
		}
		
		
		public static void HideAll(){ instance._HideAll(); }
		public void _HideAll(){
			SetSelect(null);
			
			HideNormal();
			_HideAbility();
		}
		
		public void HideNormal(){
			HideMove();
			HideHostile();
		}
		public void HideMove(){
			if(GridManager.IsHexGrid()){
				for(int i=0; i<hexMovableList.Count; i++) hexMovableList[i].gameObject.SetActive(false);
			}
			else{
				for(int i=0; i<sqMovableList.Count; i++) sqMovableList[i].gameObject.SetActive(false);
			}
		}
		public void HideHostile(){
			if(GridManager.IsHexGrid()){
				for(int i=0; i<hexHostileList.Count; i++) hexHostileList[i].gameObject.SetActive(false);
			}
			else{
				for(int i=0; i<sqHostileList.Count; i++) sqHostileList[i].gameObject.SetActive(false);
			}
		}
		
		public static void HideAbility(){ instance._HideAbility(); }
		public void _HideAbility(){
			if(GridManager.IsHexGrid()){
				for(int i=0; i<hexAbilityList.Count; i++) hexAbilityList[i].gameObject.SetActive(false);
			}
			else{
				for(int i=0; i<sqAbilityList.Count; i++) sqAbilityList[i].gameObject.SetActive(false);
			}
			
			for(int i=0; i<extraCursorList.Count; i++) extraCursorList[i].position=new Vector3(0, 9999, 0);
		}
		
		
		private Transform AddElement(Transform prefab, List<Transform> tgtList=null, bool activeState=false, float scale=1){
			Transform newItem=(Transform)Instantiate(prefab);
			newItem.localScale*=GridManager.GetNodeSize(false) * scale;
			newItem.parent=transform;
			newItem.gameObject.SetActive(activeState);
			
			if(tgtList!=null) tgtList.Add(newItem);
			
			return newItem;
		}
		
	}

}