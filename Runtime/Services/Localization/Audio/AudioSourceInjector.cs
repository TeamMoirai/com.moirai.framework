using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 音频源注入器。
	/// <para>当指定本地化文本 ID 时，注入时会异步从资源系统加载对应的 <see cref="AudioClip"/> 并播放；否则直接播放传入的音频数据。</para>
	/// <para>资源路径下租约由注入器持有直到下次加载或销毁，防止音频播放期间被周期性 UnloadUnusedAssets 回收。</para>
	/// </summary>
	public class AudioSourceInjector : IInjector, IDisposable
	{
		private string _localizedTextID;
		private readonly AudioSource _audio;
		private LocalizerBase _localizer;
		// 当前语言音频资源的租约：持有引用防止资源在播放期间被回收
		private ResourceAssetLease<AudioClip> _currentLease;
		// 加载版本号：语言快速连续切换或销毁时丢弃过期的异步加载结果
		private int _loadVersion;

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
			// 数组模式也需要记录本地化器：Play 依赖其 playFromSamePositionWhenInject 配置
			_localizer = localizer;

			if (string.IsNullOrEmpty(_localizedTextID))
			{
				Play(localizedData as AudioClip);
			}
			else
			{
				ApplyFromResource().Forget();
			}
		}

		/// <summary>
		/// 更新资源模式下使用的本地化文本 ID（仅记录，下次注入生效）。
		/// </summary>
		public void SetLocalizedId(string localizedTextID)
		{
			_localizedTextID = localizedTextID;
		}

		/// <summary>
		/// 释放当前持有的资源租约，并使在途异步加载失效。
		/// </summary>
		public void Dispose()
		{
			// 递增版本号：销毁后完成的加载会自行丢弃并释放租约，避免泄漏
			_loadVersion++;
			_currentLease.Dispose();
			_currentLease = default;
		}

		/// <summary>
		/// 停止播放、清空资源 ID，并释放当前音频资源租约。
		/// </summary>
		public void Clear()
		{
			_localizedTextID = null;
			Dispose();
			if (_audio == null) return;

			_audio.Stop();
			_audio.clip = null;
		}

		/// <summary>
		/// 根据本地化文本 ID 从资源系统异步加载音频片段并播放。
		/// </summary>
		private async UniTaskVoid ApplyFromResource()
		{
			var version = ++_loadVersion;
			string textIDValue = LocalizationService.GetTextFromId(_localizedTextID);
			var lease = await ResourceService.LoadLeaseAsync<AudioClip>(textIDValue);

			// 加载期间发生了更新的切换或已销毁，丢弃过期结果
			if (version != _loadVersion)
			{
				lease.Dispose();
				return;
			}

			if (!lease.IsValid)
			{
				LogUtility.Error("AudioSourceInjector: failed to load audio clip for id '{0}'.", _localizedTextID);
				return;
			}

			// 释放上一份语言的租约（Dispose 内部对未持有状态短路），持有本次资源直到下次加载或销毁
			_currentLease.Dispose();
			_currentLease = lease;
			Play(lease.Asset);
		}

		/// <summary>
		/// 播放指定音频片段，并保留原播放状态与进度。
		/// <para>若播放前音频源正在播放，则更换片段后继续播放；是否恢复至原播放进度取决于 <see cref="AudioLocalizer.playFromSamePositionWhenInject"/> 配置。</para>
		/// </summary>
		/// <param name="audioClip">待播放的音频片段。</param>
		void Play(AudioClip audioClip)
		{
			if (_audio == null) return; // 异步加载期间组件已销毁

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
