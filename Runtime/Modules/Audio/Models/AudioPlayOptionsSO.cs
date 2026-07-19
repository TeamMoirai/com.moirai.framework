using System;
using Moirai.Atropos.Attributes;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

namespace Moirai.Atropos.Audio
{
	/// <summary>
	/// 保存 AudioModule 播放数据的可编写脚本对象
	/// </summary>
	[Serializable]
	[CreateAssetMenu(menuName = "Moirai Framework/Audio/Play Options SO")]
	// ReSharper disable once InconsistentNaming
	public class AudioPlayOptionsSO : ScriptableObject
	{
		/// <!-- 音频剪辑 -->
		private const string AUDIO_CLIP_GROUP = "音频剪辑 [Audio Clip]";

		// 指定音频
		[Header("音频 [Audio]")]
		[InspectorGroup(AUDIO_CLIP_GROUP, ColorsUtility.EColor.Teal)]
		[Tooltip("要播放的音频")]
		[SerializeField] private AudioClip m_Audio;
		public AudioClip Audio => m_Audio;

		// 随机音频
		[Header("随机音频 [Random Audio]")]
		[InspectorGroup(AUDIO_CLIP_GROUP)]
		[Tooltip("播放随机音频的数组")]
		[SerializeField] private AudioClip[] m_RandomAudio;
		public AudioClip[] RandomAudio => m_RandomAudio;
		[InspectorGroup(AUDIO_CLIP_GROUP)]
		[Tooltip("随机的 SFX 音频将按顺序播放，而不是随机播放")]
		[SerializeField] private bool m_SequentialOrder = false;
		public bool SequentialOrder => m_SequentialOrder;
		[InspectorGroup(AUDIO_CLIP_GROUP)]
		[Tooltip("如果按顺序播放（SequentialOrder），则判断是否在最后一个索引处停住，直到冷却时间结束（SequentialOrderHoldCooldownDuration）或调用 ResetSequentialIndex 方法")]
		[ShowIf(nameof(m_SequentialOrder))]
		[SerializeField] private bool m_SequentialOrderHoldLast = false;
		public bool SequentialOrderHoldLast => m_SequentialOrderHoldLast;
		[InspectorGroup(AUDIO_CLIP_GROUP)]
		[Tooltip("如果处于 SequentialOrderHoldLast 模式，则在此持续时间后，索引将自动重置为 0，除非它是 0，在这种情况下，将被忽略，则在此持续时间后，索引将自动重置为 0，除非它是 0，在这种情况下，将被忽略")]
		[ShowIf(nameof(ShowSequentialOrderHoldCooldownDuration))]
		[SerializeField] private float m_SequentialOrderHoldCooldownDuration = 2f;
		public float SequentialOrderHoldCooldownDuration => m_SequentialOrderHoldCooldownDuration;
		private bool ShowSequentialOrderHoldCooldownDuration => m_SequentialOrder && m_SequentialOrderHoldLast;
		[InspectorGroup(AUDIO_CLIP_GROUP)]
		[Tooltip("随机顺序播放，直到全部播放完毕。一旦全部播完，列表就会再次洗牌，然后重新开始")]
		[HideIf(nameof(m_SequentialOrder))]
		[SerializeField] private bool m_RandomUnique = false;
		public bool RandomUnique => m_RandomUnique;

		/// <!-- 声音属性 -->
		private const string AUDIO_PROPERTIES_GROUP = "声音属性 [Audio Properties]";

		// 音量
		[Header("音量 [Volume]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP, ColorsUtility.EColor.Chocolate)]
		[Tooltip("播放音频的最小音量")]
		[Range(0f, 2f)]
		[SerializeField] private float m_MinVolume = 1f;
		public float MinVolume => m_MinVolume;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("播放音频的最大音量")]
		[Range(0f, 2f)]
		[SerializeField] private float m_MaxVolume = 1f;
		public float MaxVolume => m_MaxVolume;

		// 音调
		[Header("音调 [Pitch]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("播放音频的最小音调")]
		[Range(-3f, 3f)]
		[SerializeField] private float m_MinPitch = 1f;
		public float MinPitch => m_MinPitch;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("播放音频的最大音调")]
		[Range(-3f, 3f)]
		[SerializeField] private float m_MaxPitch = 1f;
		public float MaxPitch => m_MaxPitch;

		// 时间
		[Header("时间 [Time]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("开始播放音频的时间（以秒为单位，在定义的最小值和最大值之间随机），相当于 AudioSource API 的 Time")]
		[VectorLabel("Min", "Max")]
		[SerializeField] private Vector2 m_PlaybackTime = new Vector2(0f, 0f);
		public Vector2 PlaybackTime => m_PlaybackTime;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("播放音频的持续时间（以秒为单位，在定义的最小值和最大值之间随机）。如果 min 和 max 为零，则忽略。")]
		[VectorLabel("Min", "Max")]
		[SerializeField] private Vector2 m_PlaybackDuration = new Vector2(0f, 0f);
		public Vector2 PlaybackDuration => m_PlaybackDuration;

		// 音频模块选项
		[Header("音频模块选项 [Audio Module Options]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("播放音频的音轨。选择与音频性质相匹配的")]
		[SerializeField] private AudioTrack m_AudioTrack = AudioTrack.Sfx;
		public AudioTrack AudioTrack => m_AudioTrack;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("音频的 ID，用于之后再次找到该音频，eg：sound control")]
		[SerializeField] private int m_ID = 0;
		public int ID => m_ID;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果不想在任意预设音轨上播放，则在此音频组上播放音频")]
		[SerializeField] private AudioMixerGroup m_AudioGroup = null;
		public AudioMixerGroup AudioGroup => m_AudioGroup;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("（出于某种原因）如果不想使用内置池系统，可以在此处设置")]
		[SerializeField] private AudioSource m_RecycleAudioSource = null;
		public AudioSource RecycleAudioSource => m_RecycleAudioSource;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("是否应循环播放")]
		[SerializeField] private bool m_Loop = false;
		public bool Loop => m_Loop;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("转到另一个场景时是否应继续播放此音频")]
		[SerializeField] private bool m_Persistent = false;
		public bool Persistent => m_Persistent;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果同一音频已在播放，是否仍播放")]
		[SerializeField] private bool m_DoNotPlayIfClipAlreadyPlaying = false;
		public bool DoNotPlayIfClipAlreadyPlaying => m_DoNotPlayIfClipAlreadyPlaying;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("此声音允许同时播放的最大实例数量。使用-1表示无同时播放数量限制。")]
		[SerializeField] private int m_MaximumConcurrentInstances = 3;
		public int MaximumConcurrentInstances => m_MaximumConcurrentInstances;

		// 淡入
		[Header("淡入 [Fade In]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("是否在播放时淡入此音频")]
		[SerializeField] private bool m_FadeInOnPlay = false;
		public bool FadeInOnPlay => m_FadeInOnPlay;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("开始淡入的初始音量")]
		[ShowIf(nameof(m_FadeInOnPlay))]
		[SerializeField] private float m_FadeInInitialVolume = 0f;
		public float FadeInInitialVolume => m_FadeInInitialVolume;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("音频淡入的持续时间（以秒为单位）")]
		[ShowIf(nameof(m_FadeInOnPlay))]
		[SerializeField] private float m_FadeInDuration = 1f;
		public float FadeInDuration => m_FadeInDuration;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("音频淡入的补间动画")]
		[ShowIf(nameof(m_FadeInOnPlay))]
		[SerializeField] private Tween m_FadeInTween = new Tween(EEaseType.InOutQuart);
		public Tween FadeInTween => m_FadeInTween;

		// 独奏
		[Header("独奏 [Solo]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("此音频是否应在其目标音轨上以 Solo 模式播放。如果是，则当此音频开始播放时，该音轨上的所有其他音频都将静音")]
		[SerializeField] private bool m_SoloSingleTrack = false;
		public bool SoloSingleTrack => m_SoloSingleTrack;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("此音频是否应在所有音轨上以 Solo 模式播放。如果是，则当此音频开始播放时，所有其他音频都将静音")]
		[SerializeField] private bool m_SoloAllTracks = false;
		public bool SoloAllTracks => m_SoloAllTracks;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果在上面任意 Solo 模式下，一旦音频停止播放，AutoUnSoloOnEnd 将自动取消静音")]
		[SerializeField] private bool m_AutoUnSoloOnEnd = false;
		public bool AutoUnSoloOnEnd => m_AutoUnSoloOnEnd;

		// 空间设置
		[Header("空间设置 [Spatial Settings]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("以立体声方式（左声道或右声道）平移正在播放的音频。仅适用于单声道或立体声。")]
		[Range(-1f,1f)]
		[SerializeField] private float m_PanStereo;
		public float PanStereo => m_PanStereo;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("设置 3D 空间化计算（衰减、多普勒效应等）对该 AudioSource 的影响程度。0.0 使音频变成全 2D 效果，1.0 使其变成全 3D。")]
		[Range(0f,1f)]
		[SerializeField] private float m_SpatialBlend;
		public float SpatialBlend => m_SpatialBlend;

		// 效果
		[Header("效果 [Effects]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("启用/停用效果。绕过音频源的滤波器效果。")]
		[SerializeField] private bool m_BypassEffects = false;
		public bool BypassEffects => m_BypassEffects;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果设置，则不将 AudioListener 上的全局效果应用于 AudioSource 生成的音频信号。不适用于 AudioSource 正在混合器组中播放的情况。")]
		[SerializeField] private bool m_BypassListenerEffects = false;
		public bool BypassListenerEffects => m_BypassListenerEffects;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果设置，则不将来自 AudioSource 的信号分发到与混响区关联的全局混响。")]
		[SerializeField] private bool m_BypassReverbZones = false;
		public bool BypassReverbZones => m_BypassReverbZones;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("此音频源的优先级（相对于场景中存在的所有音频源）。（Priority值为0 表示优先级最高。值为256，表示优先级最低。默认值为 128）。对于音轨值应为 0，避免被意外擦除。")]
		[Range(0, 256)]
		[SerializeField] private int m_Priority = 128;
		public int Priority => m_Priority;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("设置分发到混响区的输出信号量。该量是线性的，范围在 0 到 1 之间，但允许在 1到1.1 范围内进行 10dB 放大，用于实现近场和远距离音频的效果。")]
		[Range(0f, 1.1f)]
		[SerializeField] private float m_ReverbZoneMix = 1f;
		public float ReverbZoneMix => m_ReverbZoneMix;

		// 3D声音设置
		[Header("3D声音设置  [3D Sound Settings]")]
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("设置音频源应用多普勒效果的程度（如果设置为 0，则不应用任意效果）。")]
		[Range(0f, 5f)]
		[SerializeField] private float m_DopplerLevel = 1f;
		public float DopplerLevel => m_DopplerLevel;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("设置 3D 立体声或多声道音频在扬声器空间中的传播角度（以度为单位）。")]
		[Range(0, 360)]
		[SerializeField] private int m_Spread = 0;
		public int Spread => m_Spread;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("音频随距离的衰减方式。对数 (Logarithmic Rolloff)、线性 (Linear Rolloff) 和自定义 (Custom Rolloff)。")]
		[SerializeField] private AudioRolloffMode m_RolloffMode = AudioRolloffMode.Logarithmic;
		public AudioRolloffMode RolloffMode => m_RolloffMode;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("音量停止增大的最小距离")]
		[SerializeField] private float m_MinDistance = 1f;
		public float MinDistance => m_MinDistance;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("（对数衰减）音频停止衰减的最大距离")]
		[SerializeField] private float m_MaxDistance = 500f;
		public float MaxDistance => m_MaxDistance;

		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("使用自定义音量衰减曲线。")]
		[SerializeField] private bool m_UseCustomRolloffCurve = false;
		public bool UseCustomRolloffCurve => m_UseCustomRolloffCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("定义 AudioSource 的音量如何随与 AudioListener 的距离变化而衰减。")]
		[ShowIf(nameof(m_UseCustomRolloffCurve))]
		[SerializeField] private AnimationCurve m_CustomRolloffCurve;
		public AnimationCurve CustomRolloffCurve => m_CustomRolloffCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("是否使用自定义空间混合曲线")]
		[SerializeField] private bool m_UseSpatialBlendCurve = false;
		public bool UseSpatialBlendCurve => m_UseSpatialBlendCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果 UseSpatialBlendCurve 为 true，则用于自定义空间混合的曲线")]
		[ShowIf(nameof(m_UseSpatialBlendCurve))]
		[SerializeField] private AnimationCurve m_SpatialBlendCurve;
		public AnimationCurve SpatialBlendCurve => m_SpatialBlendCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("是否使用自定义混响区域混音曲线")]
		[SerializeField] private bool m_UseReverbZoneMixCurve = false;
		public bool UseReverbZoneMixCurve => m_UseReverbZoneMixCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果 UseSpreadCurve 为 true，则用于自定义扩散的曲线")]
		[ShowIf(nameof(m_UseReverbZoneMixCurve))]
		[SerializeField] private AnimationCurve m_ReverbZoneMixCurve;
		public AnimationCurve ReverbZoneMixCurve => m_ReverbZoneMixCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("是否使用自定义扩散曲线")]
		[SerializeField] private bool m_UseSpreadCurve = false;
		public bool UseSpreadCurve => m_UseSpreadCurve;
		[InspectorGroup(AUDIO_PROPERTIES_GROUP)]
		[Tooltip("如果 UseSpreadCurve 为 true，则用于自定义扩散的曲线")]
		[ShowIf(nameof(m_UseSpreadCurve))]
		[SerializeField] private AnimationCurve m_SpreadCurve;
		public AnimationCurve SpreadCurve => m_SpreadCurve;

		[NonSerialized] private float _lastPlayTimestamp = -float.MaxValue;
		[NonSerialized] private int _currentIndex = 0;
		[NonSerialized] private ShuffleBag<int> _randomUniqueShuffleBag;
		[NonSerialized] private AudioAgent _playedAudioAgent;
		[NonSerialized] private AudioClip _sfx;

		public void Play(Vector3 location)
		{
			if (!Application.isPlaying) return;

			if (_sfx != null)
			{
				if (m_DoNotPlayIfClipAlreadyPlaying)
				{
					if (_playedAudioAgent != null
					    && _playedAudioAgent.AudioResource.clip == _sfx
					    && _playedAudioAgent.IsPlaying)
					{
						return;
					}
				}

				if (m_MaximumConcurrentInstances >= 0)
				{
					if (GameModule.Audio != null && GameModule.Audio.CurrentlyPlayingCount(_sfx) >= m_MaximumConcurrentInstances)
					{
						return;
					}
				}
			}

			_sfx = null;
			if (m_RandomAudio.Length > 0)
			{
				_sfx = PickRandomClip();
			}

			if (_sfx == null)
			{
				_sfx = m_Audio;
			}

			if (_sfx == null) return;

			float volume = Random.Range(m_MinVolume, m_MaxVolume);
			float pitch = Random.Range(m_MinPitch, m_MaxPitch);

			AudioPlayOptions options = new AudioPlayOptions
			{
				Location = location,
				Volume = volume,
				Pitch = pitch,

				AudioTrack = m_AudioTrack,
				ID = m_ID,
				AudioGroup = m_AudioGroup,
				RecycleAudioSource = m_RecycleAudioSource,
				Loop = m_Loop,
				Persistent = m_Persistent,

				FadeInOnPlay = m_FadeInOnPlay,
				FadeInInitialVolume = m_FadeInInitialVolume,
				FadeInDuration = m_FadeInDuration,
				FadeInTween = m_FadeInTween,

				SoloSingleTrack = m_SoloSingleTrack,
				SoloAllTracks = m_SoloAllTracks,
				AutoUnSoloOnEnd = m_AutoUnSoloOnEnd,

				PanStereo = m_PanStereo,
				SpatialBlend = m_SpatialBlend,
				BypassEffects = m_BypassEffects,
				BypassListenerEffects = m_BypassListenerEffects,
				BypassReverbZones = m_BypassReverbZones,
				Priority = m_Priority,
				ReverbZoneMix = m_ReverbZoneMix,
				DopplerLevel = m_DopplerLevel,
				Spread = m_Spread,
				RolloffMode = m_RolloffMode,
				MinDistance = m_MinDistance,
				MaxDistance = m_MaxDistance,
				UseCustomRolloffCurve = m_UseCustomRolloffCurve,
				CustomRolloffCurve = m_CustomRolloffCurve,
				UseSpatialBlendCurve = m_UseSpatialBlendCurve,
				SpatialBlendCurve = m_SpatialBlendCurve,
				UseReverbZoneMixCurve = m_UseReverbZoneMixCurve,
				ReverbZoneMixCurve = m_ReverbZoneMixCurve,
				UseSpreadCurve = m_UseSpreadCurve,
				SpreadCurve = m_SpreadCurve,
			};

			_playedAudioAgent = AudioPlayEvent.Trigger(_sfx, options);
			_lastPlayTimestamp = Time.unscaledTime;
		}

		/// <summary>
		/// 获取随机音频时要播放的下一个索引
		/// </summary>
		/// <returns></returns>
		private AudioClip PickRandomClip()
		{
			int newIndex = 0;

			if (!m_SequentialOrder)
			{
				if (m_RandomUnique)
				{
					newIndex = _randomUniqueShuffleBag.Pick();
				}
				else
				{
					newIndex = Random.Range(0, m_RandomAudio.Length);
				}
			}
			else
			{
				newIndex = _currentIndex;

				if (newIndex >= m_RandomAudio.Length)
				{
					if (m_SequentialOrderHoldLast)
					{
						newIndex--;
						if ((m_SequentialOrderHoldCooldownDuration > 0) &&
						    (Time.unscaledTime - _lastPlayTimestamp > m_SequentialOrderHoldCooldownDuration))
						{
							newIndex = 0;
						}
					}
					else
					{
						newIndex = 0;
					}
				}
				_currentIndex = newIndex + 1;
			}
			return m_RandomAudio[newIndex];
		}
	}
}
