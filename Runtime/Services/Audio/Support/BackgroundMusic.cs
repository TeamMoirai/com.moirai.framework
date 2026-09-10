using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Atropos.Audio
{
	/// <summary>
	/// 对象在实例化时播放背景音乐。
	/// <para>分层设计：不同 <see cref="m_ID"/> 的 Music 可同时播放（如 BGM + 气氛层 + 压力层）。</para>
	/// <para>同 ID 替换：再次 Play 仅淡出/停止本 ID，不影响其它分层。</para>
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
		[Tooltip("分层 ID：不同 ID 可同时播放；同 ID 再次 Play 会替换本层")]
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
		/// 播放本层背景音乐（同 ID 替换，不同 ID 分层共存）。
		/// </summary>
		protected virtual void Start()
		{
			Play();
		}

		[Button]
		protected virtual void Play()
		{
			// 仅替换同 ID 分层；其它 ID 的 Music 保持播放
			AudioService.StopByID(m_ID, m_Fade ? m_FadeDuration : 0f);

			AudioPlayOptions options = AudioPlayOptions.Default;
			options.ID = m_ID;
			options.Volume = m_Volume;
			options.Pitch = m_Pitch;
			options.Loop = m_Loop;
			options.Persistent = m_Persistent;
			options.AudioTrack = EAudioTrack.Music;
			options.FadeInOnPlay = m_Fade;
			options.FadeInInitialVolume = m_FadeInitialVolume;
			options.FadeInDuration = m_FadeDuration;
			options.FadeInTweenEase = m_FadeTweenEase;
			options.SoloSingleTrack = m_SoloSingleTrack;
			options.SoloAllTracks = m_SoloAllTracks;
			options.AutoUnSoloOnEnd = m_AutoUnSoloOnEnd;

			if (m_DirectReference)
			{
				if (m_AudioClip != null) AudioService.Play(m_AudioClip, options);
				else
				{
					LogUtility.Warning("Audio Resource is null");
				}
			}
			else
			{
				AudioService.Play(m_SoundClip.Path, options, true, false);
			}
		}

		[Button]
		protected virtual void Stop()
		{
			// 只停本层，保留其它分层
			AudioService.StopByID(m_ID, 0f);
		}

		protected virtual void OnDestroy()
		{
			// 场景卸载时清理本层，避免句柄悬挂
			if (Application.isPlaying)
			{
				AudioService.StopByID(m_ID, 0f);
			}
		}
	}
}
