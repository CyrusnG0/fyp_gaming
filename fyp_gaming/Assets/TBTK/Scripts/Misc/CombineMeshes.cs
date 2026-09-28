using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Copy meshes from children into the parent's Mesh.
// CombineInstance stores the list of meshes.  These are combined
// and assigned to the attached Mesh.

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class CombineMeshes : MonoBehaviour
{
	//public Material material;
	
	public Material matHexFog;
	public Material matSqFog;
	
    void Start(){
		Combine();
	}
	
	private bool combined=false;
    public void Combine()
    {
		//if(!TBTK.GridManager.CombineGridMesh())	return;
		
		if(combined) return;
		
		combined=true;
		
		//if(TBTK.GridManager.UseIndividualCollider()) return;
		
        MeshRenderer[] rendererList = GetComponentsInChildren<MeshRenderer>();	//this will get the parent too, so start the loop at 1
        List<MeshFilter> meshFilterList = new List<MeshFilter>();
		
		for(int i=1; i<rendererList.Length; i++){
			if(rendererList[i].enabled) meshFilterList.Add(rendererList[i].gameObject.GetComponent<MeshFilter>());
			
			if(!TBTK.GridManager.UseIndividualCollider() || !rendererList[i].enabled) rendererList[i].gameObject.SetActive(false);
		}
		
		MeshFilter[] meshFilters = new MeshFilter[meshFilterList.Count];
		for(int i=0; i<meshFilterList.Count; i++) meshFilters[i]=meshFilterList[i];
		
        //MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];

        int n = 0;
        while (n < meshFilters.Length) {
			combine[n].mesh = meshFilters[n].sharedMesh;
			combine[n].transform = meshFilters[n].transform.localToWorldMatrix;
			
            n++;
        }
		
        transform.GetComponent<MeshFilter>().mesh = new Mesh();
        transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);
        transform.gameObject.SetActive(true);
		
		if(TBTK.GameControl.EnableFogOfWar()){
			if(TBTK.GridManager.IsHexGrid()){
				transform.GetComponent<MeshRenderer>().material=matHexFog;
			}
			else{
				transform.GetComponent<MeshRenderer>().material=matSqFog;
			}
		}
		else{
			Transform childT=transform.GetChild(0);
			if(childT!=null) transform.GetComponent<MeshRenderer>().material=childT.GetComponent<MeshRenderer>().material;
		}
    }
}