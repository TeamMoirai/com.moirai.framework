using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public class AudioSourceInjector : IInjector
	{
		private readonly string _localizedTextID;
		private readonly AudioSource _audio;
		private LocalizerBase _localizer;

		public AudioSourceInjector(AudioSource audio, string localizedTextID)
		{
			this._localizedTextID = localizedTextID;
			this._audio = audio;
		}

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

		private async UniTaskVoid ApplyFromResource()
		{
			string textIDValue = GameModule.Localization.GetTextFromId(_localizedTextID);
			var result = await GameModule.Resource.LoadAssetAsync<AudioClip>(textIDValue);
			
			Play(result);
		}

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
