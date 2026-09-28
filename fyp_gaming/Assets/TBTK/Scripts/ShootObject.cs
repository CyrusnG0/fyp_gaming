using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace TBTK{

	public class ShootObject : MonoBehaviour {
		
		#if UNITY_EDITOR
		public static bool inspector=false;
		#endif
		
		public enum _Type{ Projectile, Beam, Effect, Missile, }
		public _Type type;
		
		public delegate void HitCallback();
		public HitCallback hitCallback;
		
		[Header("Projectile & Missile")]
		
		public bool straightProjectile;
		
		[Space(8)] 
		public float speed=10;
		public float elevation=2;		//how elevated the shoot trajectory is (x-axis)
		public float swerve=2;			//how elevated the shoot trajectory is (y-axis)
		public float falloffRange=1;	//below this range, the elevation will gradually decrease, try set to match the max range of the tower
		
		private float eta=1;				//estimated time to hit target, used to adjust offsetPos during runtime
		private float effElevation=1;		//actual elevation used in runtime, recalculated based on falloffRange
		private float effSwerve=0;		//actual elevation used in runtime, recalculated based on falloffRange
		
		//offset to the targetPos for the SO to aim for, adjust in runtime to create a trajectory
		//it's always (0, value, 0), and value is consistently droping as the SO approach the target, making the SO aim above the target and drops overtime
		private Vector3 offsetPos=Vector3.zero;	
		
		[Space(8)] 
		public bool useTrajectoryCurve;
		public AnimationCurve trajectory;
		
		private float initialDist;
		private Vector3 initialPos;
		
		
		[Space(8)] public List<TrailRenderer> trailList=new List<TrailRenderer>();
		
		private Vector3 shootDir;
		private Vector3 shootDirP;
		
		
		
		[Header("Beam")]
		public List<LineRenderer> lines=new List<LineRenderer>();
		public float beamDuration=0.5f;
		public float startWidth=0.25f;
		private List<Vector3> linePos=new List<Vector3>{ Vector3.zero, Vector3.zero };
		private Vector3 tgtPos;
		
		
		[Header("Effect")]
		public float effectDuration=0.5f;
		public bool attachToShootPoint=false;
		
		
		[Header("Visual and Audio")]
		public VisualObject effectShoot=new VisualObject();
		public VisualObject effectHit=new VisualObject();
		
		public AudioClip shootSound;
		public AudioClip hitSound;
		
		
		[Header("Runtime Attribute (For Debugging)")]
		public Unit tgtUnit;
		public float tgtRadius=0;
		public Vector3 targetPos;
		
		//public AttackInfo attackInfo;
		
		public float shootTime;
		public Transform shootPoint;
		
		private bool shot=false;
		private bool hit=false;
		
		protected GameObject thisObj;	//public GameObject GetObj(){ return thisObj; }
		protected Transform thisT;		//public Transform GetT(){ return thisT; }
		public Vector3 GetPos(){ return thisT!=null ? thisT.position : transform.position ; }
		public Quaternion GetRot(){ return thisT!=null ? thisT.rotation : transform.rotation ; }
		
		public void Awake(){
			thisT=transform;
			thisObj=gameObject;
			
			if(type==_Type.Beam){
				for(int i=0; i<lines.Count; i++){
					if(lines[i]==null){ lines.RemoveAt(i); i-=1; }
				}
				if(lines.Count==0) Debug.LogWarning("Beam type shoot-object hasn't been assigned any LineRenderer");
			}
		}
		
		
		void OnEnable(){
			if(trailList==null) return;
			for(int i=0; i<trailList.Count; i++) trailList[i].Clear();
		}
		
		
		
		//called by Unit to fire the shoot-object, all initial calculation for a shot goes here
		//~ public void InitShoot(AttackInfo aInfo, Transform shootP=null){
			//~ if(aInfo.tgtUnit==null){
				//~ ObjectPoolManager.Unspawn(thisObj);
				//~ return;
			//~ }
			
			//~ shootPoint=shootP;
			//~ attackInfo=aInfo;
			//~ InitShoot(aInfo.tgtUnit);
			
			//~ if(attachToShootPoint) thisT.parent=shootPoint;
		//~ }
		public void InitShoot(Node node, HitCallback cb=null, Transform shootP=null, Vector3 offset=default(Vector3)){
			tgtRadius=GridManager.GetNodeSize()*0.1f;
			targetPos=node.GetPos()+offset;
			
			InitShoot(cb, shootP);
		}
		public void InitShoot(Unit tUnit, HitCallback cb=null, Transform shootP=null){
			tgtUnit=tUnit;
			tgtRadius=tgtUnit.GetRadius();
			targetPos=tgtUnit.GetTargetPoint();
			
			InitShoot(cb, shootP);
		}
		public void InitShoot(HitCallback cb=null, Transform shootP=null){
			if(attachToShootPoint) thisT.parent=shootPoint;
			
			shot=true;	hit=false; shootTime=Time.time;
			
			hitCallback=cb;
			
			if(type==_Type.Projectile || type==_Type.Missile){
				if(type==_Type.Missile || !straightProjectile){
					shootDir=(targetPos-GetPos()).normalized;
					shootDirP=new Vector3(shootDir.z, 0, -shootDir.x);
					
					initialPos=GetPos();
					initialDist=Vector3.Distance(GetPos(), targetPos);
					
					//estimate the time taken to reach the target (roughly) and calculate the effective elevation based on falloffRange
					float disMultiplier=Mathf.Clamp((initialDist-(falloffRange*.5f))/falloffRange, 0, 1);
					
					if(!useTrajectoryCurve || type==_Type.Missile){
						eta=initialDist/speed;
						effElevation=(type==_Type.Projectile ? elevation : Rand.Range(0, elevation))*disMultiplier;
					}
					
					if(type==_Type.Missile){
						effSwerve=Rand.Range(0, swerve)*disMultiplier;
						effSwerve*=( Rand.value()<0.5f ? 1 : -1 );
					}
				}
			}
			else if(type==_Type.Beam){
				if(shootPoint!=null) thisT.parent=shootPoint;
			}
			else if(type==_Type.Effect){
				thisT.LookAt(targetPos);
			}
			
			effectShoot.Spawn(GetPos(), GetRot());
			AudioManager.PlaySound(shootSound);
		}
		
		private void UpdateTargetPos(){
			if(tgtUnit!=null) targetPos=tgtUnit.GetTargetPoint();
			else tgtRadius=GridManager.GetNodeSize()*0.1f;
		}
		
		void Update(){
			if(!shot || hit) return;
			
			UpdateTargetPos();
			
			if(type==_Type.Projectile || type==_Type.Missile){
				if(type==_Type.Projectile && straightProjectile){
					thisT.LookAt(targetPos);
					
					float dist=Vector3.Distance(targetPos+offsetPos, GetPos());
					
					if(dist>tgtRadius) thisT.Translate(thisT.forward*Mathf.Min(Time.deltaTime*speed, dist), Space.World);
					else Hit(GetPos());
				}
				else{
					if(type==_Type.Projectile && useTrajectoryCurve){
						float totalDist=Vector3.Distance(initialPos, targetPos);
						float travelledDist=(Time.time-shootTime) * speed;
						
						Vector3 dummyPos=initialPos + (targetPos-initialPos).normalized * travelledDist;
						dummyPos+=new Vector3(0, trajectory.Evaluate(travelledDist/totalDist), 0);
						
						thisT.position=dummyPos;
						
						if(travelledDist>totalDist+.1f || Vector3.Distance(targetPos, GetPos())<tgtRadius) Hit(GetPos());
					}
					else{
						//calculate the offset position based on the shoot time and eta
						float t=Mathf.Min((Time.time-shootTime)/eta, 1);
						offsetPos=new Vector3(0, (1-t)*effElevation, 0);
						
						if(type==_Type.Missile){
							Vector3 os=shootDirP*(1-t)*effSwerve;
							offsetPos=new Vector3(os.x, offsetPos.y, os.z);
						}
						
						Vector3 dir=(targetPos+offsetPos-GetPos()).normalized;
						thisT.LookAt(GetPos()+dir);
						
						float dist=Vector3.Distance(targetPos+offsetPos, GetPos());
						
						if(dist>tgtRadius) thisT.Translate(dir*Mathf.Min(Time.deltaTime*speed, dist), Space.World);
						else Hit(GetPos());
					}
				}
			}
			else if(type==_Type.Beam){
				float durRemain=Mathf.Clamp(beamDuration-(Time.time-shootTime), 0, beamDuration);
				
				if(durRemain<=0) Hit(ModifyTargetPosWithTgtRadius());
				else{
					for(int i=0; i<lines.Count; i++){
						linePos[0]=GetPos(); linePos[1]=ModifyTargetPosWithTgtRadius();
						lines[i].SetPositions(linePos.ToArray());
						lines[i].widthMultiplier = Mathf.Lerp(startWidth, 0, 1-durRemain/beamDuration);
					}
				}
			}
			else if(type==_Type.Effect){
				if(Time.time-shootTime>effectDuration){
					Hit(ModifyTargetPosWithTgtRadius());
				}
			}
		}
		
		
		void Hit(Vector3 hitPos){
			if(hit) return;
			hit=true;
			
			effectHit.Spawn(hitPos, Quaternion.identity);
			AudioManager.PlaySound(hitSound);
			
			//if(attackInfo!=null && tgtUnit!=null) tgtUnit.ApplyAttack(attackInfo);
			if(hitCallback!=null) hitCallback();
			
			ObjectPoolManager.Unspawn(thisObj);
		}
		
		
		Vector3 ModifyTargetPosWithTgtRadius(){
			Vector3 dir=(GetPos()-targetPos).normalized;
			return targetPos+dir*tgtRadius;
		}
		
		
		//public float GetElevationAngle(Vector3 sPos, Vector3 tPos){
		//	if(type!=_Type.Projectile) return 0;
		//	
		//	float dist=Vector3.Distance(sPos, tPos);
		//	float elev=elevation*Mathf.Clamp((dist-(falloffRange*.5f))/falloffRange, 0, 1);
		//	return -Mathf.Atan(elev/dist)*Mathf.Rad2Deg;
		//}
		
		
		
		void OnDrawGizmos(){
			Gizmos.DrawLine(GetPos(), targetPos);
		}
		
	}

}