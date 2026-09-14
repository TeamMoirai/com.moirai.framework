using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// <see cref="RawImage"/> 本地化注入器，将本地化纹理资源应用到 RawImage 组件。
	/// <para>预期资源类型为 <see cref="Texture"/>；若加载到的是 <see cref="Sprite"/>，会改用其底层纹理。</para>
	/// </summary>
	public class RawImageInjector : ImageInjectorBase
	{
		private readonly RawImage _rawImage;
		private readonly Texture[] _textures;

		/// <summary>
		/// 创建针对指定 <see cref="RawImage"/> 的本地化纹理注入器。
		/// </summary>
		/// <param name="rawImage">目标 <see cref="RawImage"/> 组件。</param>
		/// <param name="localizedTextID">资源文本 ID，非空时改由资源系统按其加载本地化资源。</param>
		/// <param name="textures">预分配的纹理数组，供索引模式使用。</param>
		public RawImageInjector(RawImage rawImage, string localizedTextID, Texture[] textures)
			: base(localizedTextID)
		{
			_rawImage = rawImage;
			_textures = textures;
		}

		/// <inheritdoc/>
		protected override void ClearTarget()
		{
			if (_rawImage != null) _rawImage.texture = null;
		}

		/// <inheritdoc/>
		protected override void ApplyFromArray(int index)
		{
			if (_textures == null || index < 0 || index >= _textures.Length)
			{
				LogUtility.Error("RawImageInjector: textures array invalid for language index {0}.", index);
				return;
			}

			if (_rawImage == null) return;

			_rawImage.texture = _textures[index];
		}

		/// <inheritdoc/>
		protected override void ApplyAsset(Object asset)
		{
			if (_rawImage == null) return; // 异步加载期间组件已销毁

			_rawImage.texture = asset as Texture;
		}

		/// <inheritdoc/>
		protected override string GetExpectedTypeName() => "Texture";

		/// <inheritdoc/>
		protected override bool IsExpectedType(Object asset) => asset is Texture;

		/// <inheritdoc/>
		protected override bool IsConvertibleType(Object asset) => asset is Sprite;

		/// <inheritdoc/>
		protected override bool TryConvertAndApply(Object asset)
		{
			if (asset is Sprite sprite)
			{
				if (_rawImage == null) return true; // 目标已销毁

				_rawImage.texture = sprite.texture;
				return true;
			}
			return false;
		}
	}
}
