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

		internal override void Localize()
		{
			ChangeID(m_TextId);
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

			m_TextId = textId;
			var text = GameModule.Localization.GetTextFromId(textId);
			_injector.Inject(text, this);
			return true;
		}

		public void Clear()
		{
			m_TextId = null;
			_injector.Inject("", this);
		}
	}
}
