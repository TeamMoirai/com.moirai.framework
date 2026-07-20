using System.Collections;
using Moirai.Atropos;
using Moirai.Atropos.Attributes;
using Sirenix.OdinInspector;
using Moirai.Atropos.Audio;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Test
{
    public class AudioModuleTest : MonoBehaviour
    {
        [ResourcePath(EStr.AssetDatabase, typeof(AudioClip))]
        public string audioPath;

        public AudioClip audioClip;

        public AudioTrack track = AudioTrack.Sfx;
    
        [InlineButton(nameof(GenerateID), "生成")]
        public int audioID;
        public bool followTarget;
        
        public bool fadeIn;
        [ShowIf(nameof(fadeIn))]
        public float fadeDuration;
        [ShowIf(nameof(fadeIn))]
        public  float fadeInitialVolume = 0f;
        [FormerlySerializedAs("tween")] [ShowIf(nameof(fadeIn))]
        public TweenEase tweenEase;
    
        [ToggleLeft, OnValueChanged(nameof(OnSoloSingleTrackChanged))]
        public bool soloSingleTrack;
        private void OnSoloSingleTrackChanged(bool value) { if (value) { soloAllTracks = false;} }
        [ToggleLeft, OnValueChanged(nameof(OnSoloAllTracksChanged))]
        public bool soloAllTracks;
        private void OnSoloAllTracksChanged(bool value) { if (value) { soloSingleTrack = false;} }
        [ShowIf(nameof(ShowAutoUnSoloOnEnd))]
        public bool autoUnSoloOnEnd;
        private bool ShowAutoUnSoloOnEnd => soloSingleTrack || soloAllTracks;

        [Header("Test")]
        
        [ReadOnly] public bool masterMute;
        [ReadOnly] public bool mute;
        
        [OnValueChanged(nameof(SetMasterVolume))]
		[Range(0, 1)] public float masterVolume = 1f;
        
        [OnValueChanged(nameof(SetVolume))]
        [Range(0, 1)] public float volume = 1f;
        
        private IEnumerator Start()
        {
            yield return null;
            
            volume = GameModule.Audio.GetTrackVolume(track);
            mute = GameModule.Audio.GetTrackMute(track);
            
            masterVolume = GameModule.Audio.MasterVolume;
            masterMute = GameModule.Audio.MasterMute;
        }
        
        private void GenerateID()
        {
            audioID = audioClip == null ? audioPath.GetHashCode() : audioClip.GetInstanceID();
        }
    
        [Button]
        private void PlayAudioByPath()
        {
            if (!Application.isPlaying) return;

            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogError("先设置 audioPath 再播放！");
            }
            
            GameModule.Audio.Play(audioPath, track, transform.position,
                id: audioID,
                fade: fadeIn, fadeInitialVolume:fadeInitialVolume, fadeDuration: fadeDuration, fadeTweenEase: tweenEase,
                attachToTransform: followTarget ? transform : null,
                soloSingleTrack:soloSingleTrack, soloAllTracks:soloAllTracks, autoUnSoloOnEnd:autoUnSoloOnEnd);
        }

        [Button]
        private void PauseAudioByPath()
        {
            if (!Application.isPlaying) return;

            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogError("先设置 audioPath 再暂停！");
            }
            
            GameModule.Audio.Pause(audioID);
        }

        [Button]
        private void UnPauseAudioByPath()
        {
            if (!Application.isPlaying) return;

            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogError("先设置 audioPath 再恢复！");
            }
            
            GameModule.Audio.UnPause(audioID);
        }

        [Button]
        private void StopAudioByPath()
        {
            if (!Application.isPlaying) return;
            
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogError("先设置 audioPath 再停止！");
            }

            GameModule.Audio.Stop(audioID, 0f);
        }

        [Button]
        private void PlayAudioByClip()
        {
            if (!Application.isPlaying) return;
            
            if (audioClip == null)
            {
                Debug.LogError("先设置 audioClip 再播放！");
            }
            
            GameModule.Audio.Play(audioClip, track, transform.position,
                id: audioID,
                fade: fadeIn, fadeInitialVolume:fadeInitialVolume, fadeDuration: fadeDuration, fadeTweenEase: tweenEase,
                attachToTransform: followTarget ? transform : null,
                soloSingleTrack:soloSingleTrack, soloAllTracks:soloAllTracks, autoUnSoloOnEnd:autoUnSoloOnEnd);
        }
    
        private void SetVolume()
        {
            if (!Application.isPlaying) return;
            
            GameModule.Audio.SetTrackVolume(track, volume);
        }
    
        [Button]
        private void ToggleTrackMute()
        {
            if (!Application.isPlaying) return;
            
            GameModule.Audio.SetTrackMute(track, !GameModule.Audio.GetTrackMute(track));
            mute = GameModule.Audio.GetTrackMute(track);
        }
        
        private void SetMasterVolume()
        {
            if (!Application.isPlaying) return;
            
            GameModule.Audio.MasterVolume = masterVolume;
        }
    
        [Button]
        private void ToggleMasterMute()
        {
            if (!Application.isPlaying) return;
            
            GameModule.Audio.MasterMute = !GameModule.Audio.MasterMute;
            masterMute = GameModule.Audio.MasterMute;
        }
        
        private float finalVolume = 1f;
        [Button]
        private void ToggleFadeAudio()
        {
            finalVolume = finalVolume == 0f ? 1f : 0f;
            AudioFadeEvent.Trigger(AudioFadeEventMode.PlayFade, audioID, 5, finalVolume, new TweenEase(TweenUtility.EEase.InCubic));
        }
    }
}