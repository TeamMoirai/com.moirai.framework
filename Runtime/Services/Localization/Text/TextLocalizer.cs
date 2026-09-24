#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
using TMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	public class TextLocalizer : LocalizerBase
	{
		[SerializeField] private string m_TextId;

		protected override void Prepare()
		{
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
			var component = ComponentFinder.Find<TextMesh, Text, TMP_Text>(this);
#else
			var component = ComponentFinder.Find<TextMesh, Text>(this);
#endif
			if (component == null) return;

			if (component is TextMesh textMesh)
			{
				_injector = new TextMeshInjector(textMesh);
			}
			else if (component is Text text)
			{
				_injector = new UITextInjector(text);
			}
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
			else if (component is TMP_Text tmp)
			{
				_injector = new TMPInjector(tmp);
			}
#endif
		}

		internal override void Localize() => Apply(m_TextId);

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (string.IsNullOrEmpty(m_TextId)) return null;

			return LocalizationService.EditorPreviewHasText(m_TextId)
				? LocalizationService.ResolveForEditorPreview(m_TextId)
				: $"<{m_TextId}> 表内无此 ID";
		}
#endif

		public bool ChangeID(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;
			// 同 ID 早退：Timeline 每帧调用 ChangeID，避免重复查询与文本重排版
			if (textId == m_TextId) return true;
			return Apply(textId);
		}

		/// <summary>
		/// 应用指定文本 ID 的本地化字符串到目标组件。
		/// </summary>
		private bool Apply(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;

#if UNITY_EDITOR
			// 非播放态不把译文写回目标组件（会标脏场景并留下"忘了还原"的错文案）；
			// 编辑器预览改由 Inspector 侧的 LocalizerPreviewEditor 出字，Timeline 预览另议。
			if (!Application.isPlaying)
			{
				return false;
			}
#endif

			// 数据未就绪（表未加载完）：静默推迟——首次加载成功的语言切换会重注入全部本地化器；
			// 「未就绪」不是「缺译」，不该按缺译给每个本地化器刷一条错误日志
			if (!IsLocalizationDataReady) return false;

			if (!LocalizationService.Has(textId))
			{
				if (Application.isPlaying) LogUtility.Error($"Text ID: {textId} 不可用。");
				return false;
			}

			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error($"TextLocalizer {name}: no injectable text component found.");
				return false;
			}

			m_TextId = textId;
			_injector.Inject(LocalizationService.GetTextFromId(textId), this);
			return true;
		}

		public void Clear()
		{
			m_TextId = null;
			_injector?.Clear();
		}
	}
}
