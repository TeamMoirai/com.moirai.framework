using Moirai.Atropos.Asset;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// <see cref="SpriteRenderer"/> 本地化注入器：把本地化图片资源应用到 2D 精灵渲染器。
    /// </summary>
    /// <remarks>预期 <see cref="Sprite"/>（<see cref="Texture2D"/> 先转换）；索引模式为 <see cref="SpriteReference"/> 数组，取像走门面 <c>SetSprite</c>。</remarks>
    public class SpriteRendererInjector : ImageInjectorBase
    {
        private readonly SpriteRenderer _spriteRenderer;
        private readonly SpriteReference[] _sprites;
        // Texture2D 转换出的运行时 Sprite，切换/清理/销毁时释放，避免泄漏
        private Sprite _convertedSprite;

        /// <summary>
        /// 创建针对指定 <see cref="SpriteRenderer"/> 的本地化图片注入器。
        /// </summary>
        /// <param name="spriteRenderer">目标 <see cref="SpriteRenderer"/> 组件。</param>
        /// <param name="sprites">预分配的精灵引用数组，供索引模式使用。</param>
        public SpriteRendererInjector(SpriteRenderer spriteRenderer, SpriteReference[] sprites)
        {
            _spriteRenderer = spriteRenderer;
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
            if (_spriteRenderer != null) _spriteRenderer.sprite = null;
        }

        /// <inheritdoc/>
        protected override void ApplyFromArray(int index)
        {
            if (_sprites == null || index < 0 || index >= _sprites.Length)
            {
                LogUtility.Error("SpriteRendererInjector: sprites array invalid for language index {0}.", index);
                return;
            }

            if (_spriteRenderer == null) return;

            // 空元素保持组件原精灵，不报错
            var reference = _sprites[index];
            if (reference == null) return;

            DestroyConvertedSprite();
            reference.SetSprite(_spriteRenderer, allowNull: false);
        }

        /// <inheritdoc/>
        protected override void ApplyAsset(Object asset)
        {
            if (_spriteRenderer == null) return; // 异步加载期间组件已销毁

            DestroyConvertedSprite();
            _spriteRenderer.sprite = asset as Sprite;
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
                if (_spriteRenderer == null) return true; // 目标已销毁，转换结果丢弃

                DestroyConvertedSprite();
                _convertedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                _spriteRenderer.sprite = _convertedSprite;
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
