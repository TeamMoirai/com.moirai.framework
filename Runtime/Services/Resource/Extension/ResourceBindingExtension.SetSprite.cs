using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Resource
{
    public static partial class ResourceBindingExtension
    {
        #region Image SetSprite [IMAGE SET SPRITE]

        /// <summary>
        /// 设置 Image 的精灵。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="setNativeSize">是否设置原始尺寸。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSprite(this Image image, string location, bool setNativeSize = false,
            CancellationToken cancellationToken = default)
        {
            EResourceBindingOption options = setNativeSize
                ? EResourceBindingOption.SetNativeSize
                : EResourceBindingOption.None;
            SetSprite(image, location, options, cancellationToken);
        }

        /// <summary>
        /// 设置 Image 的精灵。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSprite(this Image image, string location, EResourceBindingOption options,
            CancellationToken cancellationToken = default)
        {
            if (image == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(image, bindingService);
            bindingService.BindSprite(owner, image, SpriteKey(location), options);
        }

        #endregion

        #region SpriteRenderer SetSprite [SPRITE RENDERER SET SPRITE]

        /// <summary>
        /// 设置 SpriteRenderer 的精灵。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSprite(this SpriteRenderer spriteRenderer, string location,
            CancellationToken cancellationToken = default)
        {
            SetSprite(spriteRenderer, location, EResourceBindingOption.None, cancellationToken);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的精灵。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSprite(this SpriteRenderer spriteRenderer, string location,
            EResourceBindingOption options, CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(spriteRenderer, bindingService);
            bindingService.BindSprite(owner, spriteRenderer, SpriteKey(location), options);
        }

        private static ResourceKey SpriteKey(string location)
        {
            return new ResourceKey(location, string.Empty, typeof(Sprite), EResourceAssetKind.Sprite);
        }

        #endregion

        #region SetSubSprite [SET SUB SPRITE]

        /// <summary>
        /// 设置 Image 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="setNativeSize">是否设置原始尺寸。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSubSprite(this Image image, string location, string spriteName,
            bool setNativeSize = false, CancellationToken cancellationToken = default)
        {
            EResourceBindingOption options = setNativeSize
                ? EResourceBindingOption.SetNativeSize
                : EResourceBindingOption.None;
            SetSubSprite(image, location, spriteName, options, cancellationToken);
        }

        /// <summary>
        /// 设置 Image 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSubSprite(this Image image, string location, string spriteName,
            EResourceBindingOption options, CancellationToken cancellationToken = default)
        {
            if (image == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(image, bindingService);
            bindingService.BindSubSpriteAsync(owner, image,
                new ResourceKey(location, string.Empty, typeof(Sprite), EResourceAssetKind.SubAssets),
                spriteName, options, cancellationToken).Forget();
        }

        /// <summary>
        /// 设置 SpriteRenderer 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName,
            CancellationToken cancellationToken = default)
        {
            SetSubSprite(spriteRenderer, location, spriteName, EResourceBindingOption.None, cancellationToken);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName,
            EResourceBindingOption options, CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(spriteRenderer, bindingService);
            bindingService.BindSubSpriteAsync(owner, spriteRenderer,
                new ResourceKey(location, string.Empty, typeof(Sprite), EResourceAssetKind.SubAssets),
                spriteName, options, cancellationToken).Forget();
        }

        #endregion
    }
}