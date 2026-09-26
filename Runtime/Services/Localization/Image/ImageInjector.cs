using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// <see cref="Image"/> 本地化注入器，将本地化图片资源应用到 UI 图片组件。
	/// <para>预期资源类型为 <see cref="Sprite"/>；若加载到的是 <see cref="Texture2D"/>，会自动创建 Sprite 后再应用。</para>
	/// </summary>
	public class ImageInjector : ImageInjectorBase
	{
		private readonly Image _image;
		private readonly Sprite[] _sprites;
		// Texture2D 转换出的运行时 Sprite，切换/清理/销毁时释放，避免泄漏
		private Sprite _convertedSprite;

		/// <summary>
		/// 创建针对指定 <see cref="Image"/> 的本地化图片注入器。
		/// </summary>
		/// <param name="image">目标 <see cref="Image"/> 组件。</param>
		/// <param name="sprites">预分配的 Sprite 数组，供索引模式使用。</param>
		public ImageInjector(Image image, Sprite[] sprites)
		{
			_image = image;
			_sprites = sprites;
		}

		/// <inheritdoc/>
		protected override void OnDispose()
		{
			DestroyConvertedSprite();
		}

		/// <inheritdoc/>
		protected override void ClearTarget()
		{
			if (_image != null) _image.sprite = null;
		}

		/// <inheritdoc/>
		protected override void ApplyFromArray(int index)
		{
			if (_sprites == null || index < 0 || index >= _sprites.Length)
			{
				LogUtility.Error("ImageInjector: sprites array invalid for language index {0}.", index);
				return;
			}

			if (_image == null) return;

			DestroyConvertedSprite();
			_image.sprite = _sprites[index];
		}

		/// <inheritdoc/>
		protected override void ApplyAsset(Object asset)
		{
			if (_image == null) return; // 异步加载期间组件已销毁

			DestroyConvertedSprite();
			_image.sprite = asset as Sprite;
		}

		/// <inheritdoc/>
		protected override string GetExpectedTypeName() => nameof(Sprite);

		/// <inheritdoc/>
		protected override bool IsExpectedType(Object asset) => asset is Sprite;

		/// <inheritdoc/>
		protected override bool IsConvertibleType(Object asset) => asset is Texture2D;

		/// <inheritdoc/>
		protected override bool TryConvertAndApply(Object asset)
		{
			if (asset is Texture2D texture)
			{
				if (_image == null) return true; // 目标已销毁，转换结果丢弃

				DestroyConvertedSprite();
				_convertedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
				_image.sprite = _convertedSprite;
				return true;
			}
			return false;
		}

		private void DestroyConvertedSprite()
		{
			if (_convertedSprite == null) return;

			if (Application.isPlaying) Object.Destroy(_convertedSprite);
			else Object.DestroyImmediate(_convertedSprite);
			_convertedSprite = null;
		}
	}
}
