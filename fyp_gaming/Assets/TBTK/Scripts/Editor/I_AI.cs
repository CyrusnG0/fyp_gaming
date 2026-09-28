using UnityEngine;
using UnityEditor;

using System;
using System.Collections;
using System.Collections.Generic;

namespace TBTK{
	
	[CustomEditor(typeof(AI))]
	public class I_AIEditor : TBEditorInspector {
		
		private AI instance;
		
		public override void Awake(){
			base.Awake();
			instance = (AI)target;
			
			InitLabel();
		}
		
		
		private string[] aiBehaviourLabel;
		private string[] aiBehaviourTooltip;
		
		void InitLabel(){
			int enumLength = Enum.GetValues(typeof(AI._AIBehaviour)).Length;
			aiBehaviourLabel=new string[enumLength];		aiBehaviourTooltip=new string[enumLength];
			for(int i=0; i<enumLength; i++){
				aiBehaviourLabel[i]=((AI._AIBehaviour)i).ToString();
				if((AI._AIBehaviour)i==AI._AIBehaviour.passive) 		
					aiBehaviourTooltip[i]="The unit won't actively seek out hostile unless the there are hostile within the faction's sight (using unit sight value when Fog-Of-War is not used)";
				if((AI._AIBehaviour)i==AI._AIBehaviour.aggressive) 	
					aiBehaviourTooltip[i]="The unit will actively seek out hostile to engage";
			}
		}
		
		
		public override void OnInspectorGUI(){
			Undo.RecordObject(instance, "AI");
			
			base.OnInspectorGUI();
			
			EditorGUIUtility.labelWidth=158;
			
			
			EditorGUILayout.Space();
				
				cont=new GUIContent("Override Unit Setting:", "Check to override individual unit behaviour setting");
				instance.overrideUnitSetting=EditorGUILayout.Toggle(cont, instance.overrideUnitSetting);
				
				if(instance.overrideUnitSetting){
					int aiBehaviour=(int)instance.aiBehaviour;
					cont=new GUIContent("AI Behvaiour:", "The behaviour of the AI units");
					aiBehaviour = EditorGUILayout.Popup(cont, aiBehaviour, TBE.SetupContL(aiBehaviourLabel, aiBehaviourTooltip));
					instance.aiBehaviour=(AI._AIBehaviour)aiBehaviour;
				
					cont=new GUIContent("Require Trigger:", "If checked, the unit will start in 'Passive' AI behaviour until it spotted a hostile unit");
					instance.requireTrigger=EditorGUILayout.Toggle(cont, instance.requireTrigger);
				}
				else{
					EditorGUILayout.LabelField(new GUIContent("AI Behvaiour:", "The behaviour of the AI units"), new GUIContent("n/a"));
					EditorGUILayout.LabelField(new GUIContent("Require Trigger:", "If checked, the unit will start in 'Passive' AI behaviour until it spotted a hostile unit"), new GUIContent("n/a"));
				}
			
			EditorGUILayout.Space();
			
				cont=new GUIContent("Always Use Best Option:", "When checked, AI will always use the best option available");
				instance.alwaysUseBestOption=EditorGUILayout.Toggle(cont, instance.alwaysUseBestOption);
			
			EditorGUILayout.Space();
			
				cont=new GUIContent("Damage Multiplier:", "How much the damage for a potential attack is going to be weight into making the AI decision");
				instance.damageMultiplier=EditorGUILayout.FloatField(cont, instance.damageMultiplier);
			
				cont=new GUIContent("Hit Chance Multiplier:", "How much the damage for a potential attack is going to be weight into making the AI decision");
				instance.hitChanceMultiplier=EditorGUILayout.FloatField(cont, instance.hitChanceMultiplier);
			
				cont=new GUIContent("Crit. Chance Multiplier:", "How much the damage for a potential attack is going to be weight into making the AI decision");
				instance.critChanceMultiplier=EditorGUILayout.FloatField(cont, instance.critChanceMultiplier);
				
				cont=new GUIContent("Pursue Multiplier:", "How much the damage for a potential attack is going to be weight into making the AI decision");
				instance.pursueMultiplier=EditorGUILayout.FloatField(cont, instance.pursueMultiplier);
				
				cont=new GUIContent("Cover Multiplier:", "How much the damage for a potential attack is going to be weight into making the AI decision");
				instance.coverMultiplier=EditorGUILayout.FloatField(cont, instance.coverMultiplier);
			
			EditorGUILayout.Space();
			
				cont=new GUIContent("Delay Between Unit:", "Delay in second between each units when they take their turn");
				instance.delayBetweenUnit=EditorGUILayout.FloatField(cont, instance.delayBetweenUnit);
			
			
			EditorGUIUtility.labelWidth=0;
			EditorGUILayout.Space();
			
			AI.inspector=DefaultInspector(AI.inspector);
		}
		
	}
	
}
