using Moirai.Atropos.Asset;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// <see cref="RawImage"/> 本地化注入器：把本地化纹理资源应用到 RawImage 组件。
	/// </summary>
	/// <remarks>预期 <see cref="Texture"/>（<see cref="Sprite"/> 改用其底层纹理）；索引模式为 <see cref="AssetReference{TObject}"/> 数组，解析成 location 后与资源模式共用租约管线。</remarks>
	public class RawImageInjector : ImageInjectorBase
	{
		private readonly RawImage _rawImage;
		private readonly AssetReference<Texture>[] _textures;

		/// <summary>
		/// 创建针对指定 <see cref="RawImage"/> 的本地化纹理注入器。
		/// </summary>
		/// <param name="rawImage">目标 <see cref="RawImage"/> 组件。</param>
		/// <param name="textures">预分配的纹理软引用数组，供索引模式使用。</param>
		public RawImageInjector(RawImage rawImage, AssetReference<Texture>[] textures)
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

			// 空元素保持组件原纹理，不报错
			var reference = _textures[index];
			if (reference == null || string.IsNullOrEmpty(reference.GUID)) return;

			ApplyFromReference(reference);
		}

		/// <summary>解析软引用为 location 后交租约管线。</summary>
		private void ApplyFromReference(AssetReference<Texture> reference)
		{
			if (!reference.TryGetLocation(out string location))
			{
				LogUtility.Warning("RawImageInjector: GUID not resolved: {0}", reference.GUID);
				return;
			}

			ApplyFromLocation(location).Forget();
		}

		/// <inheritdoc/>
		protected override void ApplyAsset(Object asset)
		{
			if (_rawImage == null) return; // 异步加载期间组件已销毁

			_rawImage.texture = asset as Texture;
		}

		/// <inheritdoc/>
		protected override string GetExpectedTypeName() => nameof(Texture);

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
