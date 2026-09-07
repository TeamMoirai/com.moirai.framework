using UnityEngine;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 渲染器纹理本地化注入器，将本地化纹理设置到 <see cref="Renderer"/> 材质的指定纹理属性。
	/// <para>预期资源类型为 <see cref="Texture2D"/>；若加载到的是 <see cref="Sprite"/>，会改用其底层纹理。</para>
	/// </summary>
	public class TextureInjector : ImageInjectorBase
	{
		private readonly Renderer _renderer;
		private readonly string _propertyName;
		private readonly Texture2D[] _texture2Ds;

		/// <summary>
		/// 创建针对指定 <see cref="Renderer"/> 的纹理本地化注入器。
		/// </summary>
		/// <param name="renderer">目标 <see cref="Renderer"/> 组件。</param>
		/// <param name="localizedTextID">资源文本 ID，非空时改由资源系统按其加载本地化资源。</param>
		/// <param name="propertyName">材质上的纹理属性名（如 <c>_MainTex</c>）。</param>
		/// <param name="texture2Ds">预分配的 Texture2D 数组，供索引模式使用。</param>
		public TextureInjector(Renderer renderer, string localizedTextID, string propertyName, Texture2D[] texture2Ds)
			: base(localizedTextID)
		{
			_renderer = renderer;
			_propertyName = propertyName;
			_texture2Ds = texture2Ds;
		}

		/// <inheritdoc/>
		protected override void ApplyFromArray(int index)
		{
			_renderer.material.SetTexture(_propertyName, _texture2Ds[index]);
		}

		/// <inheritdoc/>
		protected override void ApplyAsset(Object asset)
		{
			_renderer.material.SetTexture(_propertyName, asset as Texture2D);
		}

		/// <inheritdoc/>
		protected override string GetExpectedTypeName() => "Texture2D";

		/// <inheritdoc/>
		protected override bool IsExpectedType(Object asset) => asset is Texture2D;

		/// <inheritdoc/>
		protected override bool IsConvertibleType(Object asset) => asset is Sprite;

		/// <inheritdoc/>
		protected override bool TryConvertAndApply(Object asset)
		{
			if (asset is Sprite sprite)
			{
				_renderer.material.SetTexture(_propertyName, sprite.texture);
				return true;
			}
			return false;
		}
	}
}
