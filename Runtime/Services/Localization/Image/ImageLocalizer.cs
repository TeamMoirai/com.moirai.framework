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

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (!string.IsNullOrEmpty(localizedTextID)) return DescribeResourcePreview(localizedTextID);

			var index = PreviewLanguageIndex();
			if (index < 0) return null;

			return IndexedPreviewHeader(index) +
			       $"sprites: {DescribeIndexedElement(sprites, index)} / " +
			       $"textures: {DescribeIndexedElement(textures, index)} / " +
			       $"texture2Ds: {DescribeIndexedElement(texture2Ds, index)}";
		}
#endif

		internal override void Localize()
		{
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error($"ImageLocalizer {name}: no target render component found.");
				return;
			}

			// 数据未就绪（表未加载完）：静默推迟，首载成功的语言切换会重注入——「未就绪」不按缺译报错
			if (!IsLocalizationDataReady) return;

			// 资源模式：有文本 ID 时单趟解析出地址作为载荷交注入器异步加载；索引模式才需要语言下标
			if (!string.IsNullOrEmpty(localizedTextID))
			{
				if (!LocalizationService.TryGetTextFromId(localizedTextID, out var address))
				{
					if (Application.isPlaying) LogUtility.Error($"Text ID: {localizedTextID} 不可用。");
					return;
				}

				_injector.Inject(address, this);
				return;
			}

			var index = LocalizationService.CurrentLanguageIndex;
			if (index < 0) return;

			// ImageInjectorBase：无文本 ID 时按索引取预分配数组
			_injector.Inject(index, this);
		}

		public bool ChangeID(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;
			// 同 ID 早退：避免重复异步加载；语言切换走 Localize() 仍会重刷
			if (textId == localizedTextID) return true;
#if UNITY_EDITOR
			// 非播放态不注入：编辑态没有后端可取资产，写进组件还会把场景标脏
			if (!Application.isPlaying) return false;
#endif
			if (!LocalizationService.TryGetTextFromId(textId, out var address))
			{
				if (Application.isPlaying) LogUtility.Error($"Text ID: {textId} 不可用。");
				return false;
			}

			this.localizedTextID = textId;
			// 同步注入器资源 ID，避免 Prepare 时冻结的旧地址继续生效
			if (_injector is ImageInjectorBase imageInjector)
			{
				imageInjector.SetLocalizedId(textId);
			}

			// 与 TextLocalizer.ChangeID 语义一致：记录后立即应用（地址已单趟解析，直接注入）
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error($"ImageLocalizer {name}: no target render component found.");
				return true;
			}

			_injector.Inject(address, this);
			return true;
		}

		public void Clear()
		{
			localizedTextID = null;
			_injector?.Clear();
		}
	}
}
