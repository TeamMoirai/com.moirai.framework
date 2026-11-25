using Moirai.Atropos.Asset;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	public class ImageLocalizer : LocalizerBase
	{
		[SerializeField] private string m_LocalizedTextID = "";
		[SerializeField] private string m_PropertyName = "_MainTex";

		[Tooltip("按语言索引的二维纹理软引用：下标 = 当前语言列下标（下标越界或空元素保持原二维纹理）")]
		[SerializeField, LanguageIndexed] internal AssetReference<Texture2D>[] m_Texture2Ds;
		[Tooltip("按语言索引的精灵引用：下标 = 当前语言列下标（下标越界或空元素保持原精灵）")]
		[SerializeField, LanguageIndexed] internal SpriteReference[] m_Sprites;
		[Tooltip("按语言索引的纹理软引用：下标 = 当前语言列下标（下标越界或空元素保持原纹理）")]
		[SerializeField, LanguageIndexed] internal AssetReference<Texture>[] m_Textures;

		protected override void Prepare()
		{
			var component = ComponentFinder.Find<Image, RawImage, SpriteRenderer, Renderer>(this);
			if (component == null) return;

			if (component is Image image)
			{
				_injector = new ImageInjector(image, m_Sprites);
			}
			else if (component is RawImage rawImage)
			{
				_injector = new RawImageInjector(rawImage, m_Textures);
			}
			else if (component is SpriteRenderer spriteRenderer)
			{
				_injector = new SpriteRendererInjector(spriteRenderer, m_Sprites);
			}
			else if (component is Renderer renderer)
			{
				_injector = new TextureInjector(renderer, m_PropertyName, m_Texture2Ds);
			}
		}

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (!string.IsNullOrEmpty(m_LocalizedTextID)) return DescribeAssetPreview(m_LocalizedTextID);

			var index = PreviewLanguageIndex();
			if (index < 0) return null;

			return IndexedPreviewHeader(index) +
			       $"sprites: {DescribeIndexedElement(m_Sprites, index)} / " +
			       $"textures: {DescribeIndexedElement(m_Textures, index)} / " +
			       $"texture2Ds: {DescribeIndexedElement(m_Texture2Ds, index)}";
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

			// 资源模式：有文本 ID 时单趟解析出 location 作为载荷交注入器异步加载；索引模式才需要语言下标
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

			// ImageInjectorBase：无文本 ID 时按索引取预分配数组
			_injector.Inject(index, this);
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

			m_LocalizedTextID = textId;

			// 与 TextLocalizer.ChangeID 语义一致：记录后立即应用（location 已单趟解析，直接注入）
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error("ImageLocalizer {0}: no target render component found.", name);
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
