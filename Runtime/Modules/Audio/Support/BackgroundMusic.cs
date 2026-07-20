using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Atropos.Audio
{
	/// <summary>
	/// 对象在实例化时播放背景音乐。
	/// 注意：一次只能播放一种背景音乐。
	/// </summary>
	public class BackgroundMusic : MonoBehaviour
	{
		[Tooltip("直接引用？")]
		[SerializeField] private bool m_DirectReference = true;
		[Tooltip("需要播放的背景音乐")]
		[ShowIf(nameof(m_DirectReference))]
		[SerializeField] private AudioClip m_AudioClip;
		[Tooltip("需要播放的背景音乐")]
		[HideIf(nameof(m_DirectReference))]
		[SerializeField] private AudioClipInfo m_SoundClip;
		[Tooltip("背景音乐的 ID")]
		[SerializeField] private int m_ID = 10001;

		[Range(0f, 2f)]
		[SerializeField] private float m_Volume = 1f;

		[Range(-3f, 3f)]
		[SerializeField] private float m_Pitch = 1f;

		[SerializeField] private bool m_Loop = true;
		[SerializeField] private bool m_Persistent = true;

		[Header("过渡 [Fade]")]
		[SerializeField] private bool m_Fade = false;
		[ShowIf(nameof(m_Fade))]
		[SerializeField] private float m_FadeInitialVolume = 0f;
		[ShowIf(nameof(m_Fade))]
		[SerializeField] private float m_FadeDuration = 1f;
		[FormerlySerializedAs("m_FadeTween")]
		[ShowIf(nameof(m_Fade))]
		[SerializeField] private TweenEase m_FadeTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart);

		[Header("独奏 [Solo]")]
		[SerializeField] private bool m_SoloSingleTrack = false;
		[SerializeField] private bool m_SoloAllTracks = false;
		[SerializeField] private bool m_AutoUnSoloOnEnd = false;

		/// <summary>
		/// <see cref="AudioModule"/> 播放背景音乐。
		/// </summary>
		protected virtual void Start()
		{
			PlayBGM();
		}

		[Button]
		protected virtual void PlayBGM()
		{
			var agents = GameModule.Audio?.FindAgentsByID(m_ID);
			if (agents == null) return;

			foreach (var agent in agents)
			{
				if (agent.ID != m_ID) continue;

				if ((agent.IsPlaying && agent.AudioResource.volume == 0f) || agent.IsPaused)
				{
					agent.Stop();
				}
				else if (agent.IsPlaying || agent.AudioResource.isPlaying)
				{
					agent.Stop(m_FadeDuration);
				}
			}

			AudioPlayOptions options = AudioPlayOptions.Default;
			options.ID = m_ID;
			options.Volume = m_Volume;
			options.Pitch = m_Pitch;
			options.Loop = m_Loop;
			options.Persistent = m_Persistent;
			options.AudioTrack = AudioTrack.Music;
			options.FadeInOnPlay = m_Fade;
			options.FadeInInitialVolume = m_FadeInitialVolume;
			options.FadeInDuration = m_FadeDuration;
			options.FadeInTweenEase = m_FadeTweenEase;
			options.SoloSingleTrack = m_SoloSingleTrack;
			options.SoloAllTracks = m_SoloAllTracks;
			options.AutoUnSoloOnEnd = m_AutoUnSoloOnEnd;

			if (m_DirectReference)
			{
				if (m_AudioClip != null) AudioPlayEvent.Trigger(m_AudioClip, options);
				else
				{
					Debug.LogWarning("Audio Resource is null");
				}
			}
			else
			{
				AudioPlayEvent.Trigger(m_SoundClip.Path, options, true, false);
			}
		}
	}
}