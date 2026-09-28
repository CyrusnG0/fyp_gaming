using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(ShootObject))] [CanEditMultipleObjects]
	public class I_ShootObjectEditor : TBEditorInspector {
		
		private ShootObject instance;
		
		public override void Awake(){
			base.Awake();
			instance = (ShootObject)target;
			
			InitLabel();
		}
		
		
		private string[] soTypeLabel;
		private string[] soTypeTooltip;
		
		void InitLabel(){
			int enumLength = Enum.GetValues(typeof(ShootObject._Type)).Length;
			soTypeLabel=new string[enumLength];		soTypeTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				soTypeLabel[i]=((ShootObject._Type)i).ToString();
				
				if((ShootObject._Type)i==ShootObject._Type.Projectile) 
					soTypeTooltip[i]="A typical projectile, travels from turret shoot-point towards target";
				if((ShootObject._Type)i==ShootObject._Type.Missile) 
					soTypeTooltip[i]="An altenernate projectile, travels from turret shoot-point towards target but can swerve side way";
				if((ShootObject._Type)i==ShootObject._Type.Beam) 
					soTypeTooltip[i]="Used to render laser or any beam like effect. The shootObject doest move instead uses LineRenderer component to render beam from shoot-point to target";
				if((ShootObject._Type)i==ShootObject._Type.Effect) 
					soTypeTooltip[i]="A shootObject uses to show various firing effect. The shootObject will remain at shootPoint so it can act as a shoot effect";
			}
		}
		
		protected SerializedProperty srlPpt;
		
		public override void OnInspectorGUI(){
			base.OnInspectorGUI();
			
			GUI.changed = false;
			Undo.RecordObject(instance, "ShootObject");
			
			serializedObject.Update();
			
			EditorGUILayout.Space();
			
				srlPpt=serializedObject.FindProperty("type");
				
				if(srlPpt.hasMultipleDifferentValues){
					EditorGUILayout.HelpBox("Editing of type specify attribute is unavailable when selecting multiple shoot object of different type", MessageType.Warning);
				}
				else if(!srlPpt.hasMultipleDifferentValues){
					
					int type=(int)instance.type;
					cont=new GUIContent("Type:", "Type of the shoot object");
					contL=TBE.SetupContL(soTypeLabel, soTypeTooltip);
					type = EditorGUILayout.Popup(cont, type, contL);
					instance.type=(ShootObject._Type)type;
					srlPpt.enumValueIndex=type;
					
					if(type==(int)ShootObject._Type.Projectile || type==(int)ShootObject._Type.Missile){
						cont=new GUIContent("  Speed:", "The travel speed of the shootObject");
						instance.speed=EditorGUILayout.FloatField(cont, instance.speed);
						//EditorGUILayout.PropertyField(serializedObject.FindProperty("speed"), cont);
						
						if(type!=(int)ShootObject._Type.Missile){
							cont=new GUIContent("  Straight Projectile:", "Check to have the projectile move in a straight line directly to the target");
							instance.straightProjectile=EditorGUILayout.Toggle(cont, instance.straightProjectile);
						}
						else instance.straightProjectile=false;
						
						if(instance.straightProjectile){
							EditorGUILayout.LabelField("");
							EditorGUILayout.LabelField("");
						}
						else{
							bool nonTrajectory=false;
							
							if(type==(int)ShootObject._Type.Projectile){
								cont=new GUIContent("  Use AnimationCurve:", "Check to use simulated trajectory");
								instance.useTrajectoryCurve=EditorGUILayout.Toggle(cont, instance.useTrajectoryCurve);
								
								if(instance.useTrajectoryCurve){
									cont=new GUIContent("  Trajectory:", "The trajectory of the shoot-object");
									instance.trajectory=EditorGUILayout.CurveField(cont, instance.trajectory);
									
									nonTrajectory=true;
								}
							}
							
							if(!nonTrajectory){
								cont=new GUIContent("  Max Height:", "The maximum height in the shoot trajectory\nSet to 0 for a straight shot");
								instance.elevation=EditorGUILayout.FloatField(cont, instance.elevation);
								//EditorGUILayout.PropertyField(serializedObject.FindProperty("elevation"), cont);
								
								cont=new GUIContent("  Fall Off Range:", "The shot trajectory elevation will gradually decrease if get closer than this range\nIt's recommanded to match this value to the range of the tower");
								instance.falloffRange=EditorGUILayout.FloatField(cont, instance.falloffRange);
								//EditorGUILayout.PropertyField(serializedObject.FindProperty("falloffRange"), cont);
								
								if(type==(int)ShootObject._Type.Missile){
									cont=new GUIContent("  Swerve:", "The swerve towards left or right in the shoot trajectory\nSet to 0 for a straight shot");
									instance.swerve=EditorGUILayout.FloatField(cont, instance.swerve);
								}
							}
						}
					}
					else if(type==(int)ShootObject._Type.Beam){
						if(serializedObject.isEditingMultipleObjects){
							EditorGUILayout.HelpBox("Assignment of LineRenderer component is not supported for multi-instance editing", MessageType.Info);
						}
						else{
							if(instance.lines.Count==0) instance.lines.Add(null);
							instance.lines[0]=(LineRenderer)EditorGUILayout.ObjectField("  LineRenderer", instance.lines[0], typeof(LineRenderer), true);
						}
						
						cont=new GUIContent("  Beam Duration:", "The active duration of the beam");
						instance.beamDuration=EditorGUILayout.FloatField(cont, instance.beamDuration);
						//EditorGUILayout.PropertyField(serializedObject.FindProperty("beamDuration"), cont);
						
						cont=new GUIContent("  Start Width:", "The starting width of the beam");
						instance.startWidth=EditorGUILayout.FloatField(cont, instance.startWidth);
						//EditorGUILayout.PropertyField(serializedObject.FindProperty("startWidth"), cont);
					}
					else if(type==(int)ShootObject._Type.Effect){
						cont=new GUIContent("  Effect Duration:", "How long the effect will last");
						instance.effectDuration=EditorGUILayout.FloatField(cont, instance.effectDuration);
						//EditorGUILayout.PropertyField(serializedObject.FindProperty("effectDuration"), cont);
					}
				}
				
			EditorGUILayout.Space();
			EditorGUILayout.Space();
				
				cont=new GUIContent("Effect (Shoot):", "The visual effect to spawn at the shoot-point when the shoot-object is fired");
				DrawVisualObject(instance.effectShoot, cont);
				
				EditorGUILayout.Space();
				EditorGUILayout.Space();
				
				cont=new GUIContent("Effect (Hit):", "The visual effect to spawn at the hit-point when the shoot-object hit its target");
				DrawVisualObject(instance.effectHit, cont);
				
			EditorGUILayout.Space();
			EditorGUILayout.Space();
				
				cont=new GUIContent("Shoot Sound:", "The audio clip to play when the shoot-object fires");
				instance.shootSound=(AudioClip)EditorGUILayout.ObjectField(cont, instance.shootSound, typeof(AudioClip), true);
				
				cont=new GUIContent("Hit Sound:", "The audio clip to play when the shoot-object hits");
				instance.hitSound=(AudioClip)EditorGUILayout.ObjectField(cont, instance.hitSound, typeof(AudioClip), true);
				
			EditorGUILayout.Space();
			
			//DrawDefaultInspector();
			ShootObject.inspector=DefaultInspector(ShootObject.inspector);
			
			serializedObject.ApplyModifiedProperties();
			
			if(GUI.changed) EditorUtility.SetDirty(instance);
		}
		
		
		//~ protected void DrawVisualObject(VisualObject vo, GUIContent gContent){
			//~ vo.obj=(GameObject)EditorGUILayout.ObjectField(gContent, vo.obj, typeof(GameObject), true);
			
			//~ cont=new GUIContent(" - Auto Destroy:", "Check if the spawned effect should be destroyed automatically");
			//~ if(vo.obj!=null) vo.autoDestroy=EditorGUILayout.Toggle(cont, vo.autoDestroy);
			//~ else EditorGUILayout.LabelField(" - Auto Destroy:", "-");
			
			//~ cont=new GUIContent(" - Effect Duration:", "How long before the spawned effect object is destroyed");
			//~ if(vo.obj!=null && vo.autoDestroy) vo.duration=EditorGUILayout.FloatField(cont, vo.duration);
			//~ else EditorGUILayout.LabelField(" - Effect Duration:", "-");
		//~ }
		
	}
	
}
