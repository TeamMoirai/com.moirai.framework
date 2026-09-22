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

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (!string.IsNullOrEmpty(localizedTextID)) return DescribeResourceIdPreview(localizedTextID);

			var index = LocalizationService.EditorPreviewLanguageIndex;
			if (index < 0) return null;

			return $"[{LocalizationService.EditorPreviewLanguage.Name}] 索引 {index} → " +
			       $"clips: {DescribeIndexedElement(clips, index)}";
		}
#endif

		internal override void Localize()
		{
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error($"AudioLocalizer {name}: no AudioSource found.");
				return;
			}

			// 资源模式（配置了文本 ID）下 clips 数组可空：注入器忽略传入数据、自行异步加载
			if (!string.IsNullOrEmpty(localizedTextID))
			{
				if (!LocalizationService.Has(localizedTextID))
				{
					if (Application.isPlaying) LogUtility.Error($"Text ID: {localizedTextID} 不可用。");
					return;
				}

				_injector.Inject<AudioClip, AudioLocalizer>(null, this);
				return;
			}

			var index = LocalizationService.CurrentLanguageIndex;
			if (index < 0) return;

			if (clips != null && index < clips.Length)
			{
				_injector.Inject(clips[index], this);
			}
			else
			{
				LogUtility.Error("AudioLocalizer {0}: clips has no entry for language index {1}.", name, index);
			}
		}

		public bool ChangeID(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;
			// 同 ID 早退：避免重复异步加载；语言切换走 Localize() 仍会重刷
			if (textId == localizedTextID) return true;
#if UNITY_EDITOR
			// Timeline 预览
			if (!Application.isPlaying)
			{
				return false;
				// todo 编辑器预览
				// GameApp.Localization.LoadInEditor();
				// Prepare();
			}
#endif
			if (!LocalizationService.Has(textId))
			{
				if (Application.isPlaying) LogUtility.Error($"Text ID: {textId} 不可用。");
				return false;
			}

			this.localizedTextID = textId;
			// 同步注入器资源 ID，避免 Prepare 时冻结的旧地址继续生效
			if (_injector is AudioSourceInjector audioInjector)
			{
				audioInjector.SetLocalizedId(textId);
			}

			// 与 TextLocalizer.ChangeID 语义一致：记录后立即应用
			Localize();
			return true;
		}

		public void Clear()
		{
			localizedTextID = null;
			_injector?.Clear();
		}
	}
}
