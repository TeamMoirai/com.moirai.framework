using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	public class ImageLocalizer : LocalizerBase
	{
		public string localizedTextID = "";
		public string propertyName = "_MainTex";
		public Texture2D[] texture2Ds;
		public Sprite[] sprites;
		public Texture[] textures;
		
		protected override void Prepare()
		{
			var component = ComponentFinder.Find<Image, RawImage, SpriteRenderer, Renderer>(this);
			if (component == null) return;
			
			if (component is Image image)
			{
				_injector = new ImageInjector(image, localizedTextID, sprites);
			}
			else if (component is RawImage rawImage)
			{
				_injector = new RawImageInjector(rawImage, localizedTextID, textures);
			}
			else if (component is SpriteRenderer spriteRenderer)
			{
				_injector = new SpriteRendererInjector(spriteRenderer, localizedTextID, sprites);
			}
			else if (component is Renderer renderer)
			{
				_injector = new TextureInjector(renderer, localizedTextID, propertyName, texture2Ds);
			}
		}

		internal override void Localize()
		{
			ChangeID(localizedTextID);
			var index = GameModule.Localization.CurrentLanguageIndex;
			_injector.Inject(index, this);
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
