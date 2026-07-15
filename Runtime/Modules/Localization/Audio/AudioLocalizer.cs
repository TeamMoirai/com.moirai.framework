using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public class AudioLocalizer : LocalizerBase
	{
		public string localizedTextID = "";
		public AudioClip[] clips;
		public bool playFromSamePositionWhenInject;

		protected override void Prepare()
		{
			var component = ComponentFinder.Find<AudioSource>(this);
			if (component == null) return;

			if (component is AudioSource audio)
			{
				_injector = new AudioSourceInjector(audio, localizedTextID);
			}
		}

		internal override void Localize()
		{
			ChangeID(localizedTextID);
			var index = GameModule.Localization.CurrentLanguageIndex;
			_injector.Inject(clips[index], this);
		}
		
		public bool ChangeID(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;
#if UNITY_EDITOR
			// Timeline 预览
			if (!Application.isPlaying)
			{
				return false;
				// todo 编辑器预览
				// GameModule.Localization.LoadInEditor();
				// Prepare();
			}
#endif
			if (!GameModule.Localization.Has(textId))
			{
				if (Application.isPlaying) Log.Error($"Text ID: {textId} 不可用。");
				return false;
			}
			this.localizedTextID = textId;
			var text = GameModule.Localization.GetTextFromId(textId);
			_injector.Inject(text, this);
			return true;
		}
		public void Clear()
		{
			localizedTextID = null;
			_injector.Inject("", this);
		}
	}
}
