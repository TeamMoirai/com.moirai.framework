using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Atropos.Localization
{
	public class AudioLocalizer : LocalizerBase
	{
		[SerializeField, FormerlySerializedAs("localizedTextID")] private string m_LocalizedTextID = "";

		[Tooltip("按语言索引的音频资源：下标 = 当前语言列下标（下标越界或空元素保持原音频）")]
		[SerializeField, FormerlySerializedAs("clips"), LanguageIndexed] private AudioClip[] m_Clips;

		[SerializeField, FormerlySerializedAs("playFromSamePositionWhenInject")] private bool m_PlayFromSamePositionWhenInject;
		public bool PlayFromSamePositionWhenInject => m_PlayFromSamePositionWhenInject;

		protected override void Prepare()
		{
			var component = ComponentFinder.Find<AudioSource>(this);
			if (component == null) return;

			if (component is AudioSource audio)
			{
				_injector = new AudioSourceInjector(audio);
			}
		}

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (!string.IsNullOrEmpty(m_LocalizedTextID)) return DescribeAssetPreview(m_LocalizedTextID);

			var index = PreviewLanguageIndex();
			if (index < 0) return null;

			return IndexedPreviewHeader(index) + $"clips: {DescribeIndexedElement(m_Clips, index)}";
		}
#endif

		internal override void Localize()
		{
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error("AudioLocalizer {0}: no AudioSource found.", name);
				return;
			}

			// 数据未就绪（表未加载完）：静默推迟，首载成功的语言切换会重注入——「未就绪」不按缺译报错
			if (!IsLocalizationDataReady) return;

			// 资源模式（配置了文本 ID）下 clips 数组可空：单趟解析出 location 作为载荷交注入器异步加载
			if (!string.IsNullOrEmpty(m_LocalizedTextID))
			{
				if (!LocalizationService.TryGetTextFromId(m_LocalizedTextID, out var location))
				{
					if (Application.isPlaying) LogUtility.Error("Text ID: {0} is incorrect.", m_LocalizedTextID);
					return;
				}

				_injector.Inject(location, this);
				return;
			}

			var index = LocalizationService.CurrentLanguageIndex;
			if (index < 0) return;

			if (m_Clips != null && index < m_Clips.Length)
			{
				_injector.Inject(m_Clips[index], this);
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
			if (textId == m_LocalizedTextID) return true;
#if UNITY_EDITOR
			// 非播放态不注入：编辑态没有后端可取资产，写进组件还会把场景标脏
			if (!Application.isPlaying) return false;
#endif
			if (!LocalizationService.TryGetTextFromId(textId, out var location))
			{
				if (Application.isPlaying) LogUtility.Error("Text ID: {0} is incorrect.", textId);
				return false;
			}

			this.m_LocalizedTextID = textId;

			// 与 TextLocalizer.ChangeID 语义一致：记录后立即应用（location 已单趟解析，直接注入）
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error("AudioLocalizer {0}: no AudioSource found.", name);
				return true;
			}

			_injector.Inject(location, this);
			return true;
		}

		public void Clear()
		{
			m_LocalizedTextID = null;
			_injector?.Clear();
		}
	}
}
