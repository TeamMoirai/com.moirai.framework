using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// <see cref="SpriteRenderer"/> 本地化注入器，将本地化图片资源应用到 2D 精灵渲染器。
    /// <para>预期资源类型为 <see cref="Sprite"/>；若加载到的是 <see cref="Texture2D"/>，会自动创建 Sprite 后再应用。</para>
    /// </summary>
    public class SpriteRendererInjector : ImageInjectorBase
    {
        private readonly SpriteRenderer _spriteRenderer;
        private readonly Sprite[] _sprites;

        /// <summary>
        /// 创建针对指定 <see cref="SpriteRenderer"/> 的本地化图片注入器。
        /// </summary>
        /// <param name="spriteRenderer">目标 <see cref="SpriteRenderer"/> 组件。</param>
        /// <param name="localizedTextID">资源文本 ID，非空时改由资源系统按其加载本地化资源。</param>
        /// <param name="sprites">预分配的 Sprite 数组，供索引模式使用。</param>
        public SpriteRendererInjector(SpriteRenderer spriteRenderer, string localizedTextID, Sprite[] sprites)
            : base(localizedTextID)
        {
            _spriteRenderer = spriteRenderer;
            _sprites = sprites;
        }

        /// <inheritdoc/>
        protected override void ApplyFromArray(int index)
        {
            _spriteRenderer.sprite = _sprites[index];
        }

        /// <inheritdoc/>
        protected override void ApplyAsset(Object asset)
        {
            _spriteRenderer.sprite = asset as Sprite;
        }

        /// <inheritdoc/>
        protected override string GetExpectedTypeName() => "Sprite";

        /// <inheritdoc/>
        protected override bool IsExpectedType(Object asset) => asset is Sprite;

        /// <inheritdoc/>
        protected override bool IsConvertibleType(Object asset) => asset is Texture2D;

        /// <inheritdoc/>
        protected override bool TryConvertAndApply(Object asset)
        {
            if (asset is Texture2D texture)
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                _spriteRenderer.sprite = sprite;
                return true;
            }
            return false;
        }
    }
}
