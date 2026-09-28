using UnityEngine;
using System.Collections;

public class Hover : MonoBehaviour {

	public float magnitude;
	public float frequency;
	private float offset;
	
	private Transform thisT;
	private float anchor;
	
	// Use this for initialization
	void Start () {
		thisT=transform;
		anchor=thisT.localPosition.y;
		offset=Random.Range(0, Mathf.PI);
	}
	
	// Update is called once per frame
	void Update () {
		float hover=magnitude*(1+Mathf.Sin(Time.time*frequency+offset));
		thisT.localPosition=new Vector3(thisT.localPosition.x, anchor+hover, thisT.localPosition.z);
	}
}
