using Cysharp.Threading.Tasks;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 音频源注入器。
	/// <para>当指定本地化文本 ID 时，注入时会异步从资源系统加载对应的 <see cref="AudioClip"/> 并播放；否则直接播放传入的音频数据。</para>
	/// </summary>
	public class AudioSourceInjector : IInjector
	{
		private readonly string _localizedTextID;
		private readonly AudioSource _audio;
		private LocalizerBase _localizer;

		/// <summary>
		/// 创建音频源注入器。
		/// </summary>
		/// <param name="audio">目标音频源。</param>
		/// <param name="localizedTextID">本地化文本 ID，解析出的文本将作为资源地址加载音频；为空时直接播放传入数据。</param>
		public AudioSourceInjector(AudioSource audio, string localizedTextID)
		{
			_localizedTextID = localizedTextID;
			_audio = audio;
		}

		/// <summary>
		/// 向音频源注入音频并播放。
		/// <para>若构造时指定了本地化文本 ID，则忽略 <paramref name="localizedData"/>，异步从资源系统加载对应的 <see cref="AudioClip"/>；否则直接将 <paramref name="localizedData"/> 作为音频片段播放。</para>
		/// </summary>
		/// <typeparam name="T1">待注入数据的类型，无文本 ID 时应为 <see cref="AudioClip"/>。</typeparam>
		/// <typeparam name="T2">本地化器类型。</typeparam>
		/// <param name="localizedData">待注入的本地化数据，通常为音频片段。</param>
		/// <param name="localizer">发起注入的本地化器。</param>
		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			if (string.IsNullOrEmpty(_localizedTextID))
			{
				Play(localizedData as AudioClip);
			}
			else
			{
				_localizer = localizer;
				ApplyFromResource().Forget();
			}
		}

		/// <summary>
		/// 根据本地化文本 ID 从资源系统异步加载音频片段并播放。
		/// </summary>
		private async UniTaskVoid ApplyFromResource()
		{
			string textIDValue = LocalizationService.GetTextFromId(_localizedTextID);
			var lease = await ResourceService.LoadLeaseAsync<AudioClip>(textIDValue);

			Play(lease.Asset);
			lease.Dispose();
		}

		/// <summary>
		/// 播放指定音频片段，并保留原播放状态与进度。
		/// <para>若播放前音频源正在播放，则更换片段后继续播放；是否恢复至原播放进度取决于 <see cref="AudioLocalizer.playFromSamePositionWhenInject"/> 配置。</para>
		/// </summary>
		/// <param name="audioClip">待播放的音频片段。</param>
		void Play(AudioClip audioClip)
		{
			var isPlaying = _audio.isPlaying;
			var time = _audio.time;
			if (isPlaying) _audio.Stop();
			var playFromSamePosition = (_localizer as AudioLocalizer)?.playFromSamePositionWhenInject;
			
			_audio.clip = audioClip;
			if (isPlaying)
			{
				_audio.Play();
				if (playFromSamePosition.HasValue && playFromSamePosition.Value)
				{
					_audio.time = time;
				}
				else
				{
					_audio.time = 0f;
				}
			}
		}
	}
}
