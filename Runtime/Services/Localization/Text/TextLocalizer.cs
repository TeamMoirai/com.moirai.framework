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
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
		// 按语言索引的 TMP 字体资产：下标 = 当前语言列下标（与 ImageLocalizer 数组同一约定）；下标越界或空元素保持原字体
		[SerializeField] private TMP_FontAsset[] m_TmpFontAssets;
#endif
		// 按语言索引的 UGUI 字体：下标同上约定；下标越界或空元素保持原字体
		[SerializeField] private Font[] m_UguiFonts;

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

			var status = LocalizationService.ResolvePreviewText(m_TextId, out var text, out var language);
			return status == EPreviewResolveStatus.Resolved
				? text
				: LanguageTag(language) + DescribeUnresolvedPreview(m_TextId, status);
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

			// 单趟解析：缺失时的报错口径不变（此前是 Has + GetTextFromId 两趟）
			if (!LocalizationService.TryGetTextFromId(textId, out var text))
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
			_injector.Inject(text, this);
			ApplyLanguagePresentation();
			return true;
		}

		/// <summary>
		/// 应用语言联动的呈现属性：TMP 的 RTL 方向（阿拉伯/希伯来等）与按语言索引的字体资产。
		/// <para>下标与 <c>LocalizationService.CurrentLanguageIndex</c> 同一约定（同 ImageLocalizer 数组语义）；
		/// 越界或空元素一律保持组件原值，不做清空。</para>
		/// </summary>
		private void ApplyLanguagePresentation()
		{
			var languageIndex = LocalizationService.CurrentLanguageIndex;

#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
			if (_injector is TMPInjector tmpInjector)
			{
				var tmp = tmpInjector.Component;
				if (tmp == null) return;

				tmp.isRightToLeftText = LocalizationService.IsCurrentLanguageRightToLeft;

				if (m_TmpFontAssets != null && languageIndex >= 0 && languageIndex < m_TmpFontAssets.Length)
				{
					var fontAsset = m_TmpFontAssets[languageIndex];
					if (fontAsset != null) tmp.font = fontAsset;
				}

				return;
			}
#endif

			if (_injector is UITextInjector uiTextInjector)
			{
				var uiText = uiTextInjector.Component;
				if (uiText == null) return;

				// UGUI 无 RTL：只按语言索引换字体
				if (m_UguiFonts != null && languageIndex >= 0 && languageIndex < m_UguiFonts.Length)
				{
					var font = m_UguiFonts[languageIndex];
					if (font != null) uiText.font = font;
				}
			}
		}

		public void Clear()
		{
			m_TextId = null;
			_injector?.Clear();
		}
	}
}
