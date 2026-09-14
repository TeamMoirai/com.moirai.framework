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
