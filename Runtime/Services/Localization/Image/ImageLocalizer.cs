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
			if (!string.IsNullOrEmpty(localizedTextID))
			{
				// 类型判据向注入器要：预览与注入必须同一份口径，否则会出现"预览说没问题、运行期报类型错"
				EnsurePreparedForPreview();
				return _injector is ImageInjectorBase injector
					? DescribeResourceIdPreview(localizedTextID, injector.IsExpectedAssetForPreview,
						injector.ExpectedTypeNameForPreview, injector.IsConvertibleAssetForPreview)
					: DescribeResourceIdPreview(localizedTextID);
			}

			var index = PreviewLanguageIndex();
			if (index < 0) return null;

			return $"[{PreviewLanguage().Code}] 索引 {index} → " +
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

			// 资源模式：有文本 ID 时由注入器自行异步加载；索引模式才需要语言下标
			if (!string.IsNullOrEmpty(localizedTextID))
			{
				if (!LocalizationService.Has(localizedTextID))
				{
					if (Application.isPlaying) LogUtility.Error($"Text ID: {localizedTextID} 不可用。");
					return;
				}

				_injector.Inject(0, this);
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
			if (!LocalizationService.Has(textId))
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
