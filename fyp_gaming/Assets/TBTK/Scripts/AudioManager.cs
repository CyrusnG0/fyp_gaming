using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TBTK{

	public class AudioManager : MonoBehaviour {
		
		public int audioSourceCount=10;
		private List<AudioSource> audioSourceList=new List<AudioSource>();
		
		private static AudioManager instance;
		
		public void Awake(){
			if(instance!=null) return;
			instance=this;
			
			CreateAudioSource();
		}
		
		void CreateAudioSource(){
			audioSourceList=new List<AudioSource>();
			for(int i=0; i<audioSourceCount; i++){
				GameObject obj=new GameObject("AudioSource"+(i+1));
				
				AudioSource src=obj.AddComponent<AudioSource>();
				src.playOnAwake=false; src.loop=false; src.volume=1; //src.spatialBlend=.75f;
				
				obj.transform.parent=transform; obj.transform.localPosition=Vector3.zero;
				
				audioSourceList.Add(src);
			}
		}
		
		//call to play a specific clip
		public static void PlaySound(AudioClip clip, Vector3 pos=default(Vector3)){ if(instance!=null) instance._PlaySound(clip, pos); }
		public void _PlaySound(AudioClip clip, Vector3 pos=default(Vector3)){
			if(clip==null) return;
			int Idx=GetUnusedAudioSourceIdx();
			audioSourceList[Idx].transform.position=pos;
			audioSourceList[Idx].clip=clip;		audioSourceList[Idx].Play();
		}
		
		//check for the next free, unused audioObject
		private int GetUnusedAudioSourceIdx(){
			for(int i=0; i<audioSourceList.Count; i++){ if(!audioSourceList[i].isPlaying) return i; }
			return 0;	//if everything is used up, use item number zero
		}
		
		
		
		
		[Header("Sound Effect")]
		public AudioClip playerWon;
		public static void OnPlayerWon(){ if(instance!=null) PlaySound(instance.playerWon); }
		public AudioClip playerLost;
		public static void OnPlayerLost(){ if(instance!=null) PlaySound(instance.playerLost); }
		
		public AudioClip abilityActivated;
		public static void OnAbilityActivated(){ if(instance!=null) PlaySound(instance.abilityActivated); }
		
		public AudioClip perkUnlocked;
		public static void OnPerkUnlocked(){ if(instance!=null) PlaySound(instance.perkUnlocked); }
		
		public AudioClip invalidAction;
		public static void OnInvalidAction(){ if(instance!=null) PlaySound(instance.invalidAction); }
		
	}

}