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
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        /// <summary>只读缓存设置 Image 精灵：未加载返回 false，不触发后端加载。</summary>
        public static bool TrySetSprite(this Image image, string location, bool setNativeSize = false,
            string packageName = "")
        {
            if (image == null)
            {
                return false;
            }

            IResourceBindingService bindingService = ResourceService.BindingService;
            if (bindingService == null)
            {
                return false;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(image, bindingService);
            var key = new ResourceKey(location, packageName, typeof(Sprite), EResourceAssetKind.Sprite);
            EResourceBindingOption options = setNativeSize ? EResourceBindingOption.SetNativeSize : EResourceBindingOption.None;
            return bindingService.TryBindSpriteCached(owner, image, key, options) == EResourceBindStatus.Success;
        }

        public static void SetSprite(this Image image, string location, bool setNativeSize = false,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            EResourceBindingOption options = setNativeSize
                ? EResourceBindingOption.SetNativeSize
                : EResourceBindingOption.None;
            SetSprite(image, location, options, cancellationToken, packageName);
        }

        /// <summary>
        /// 设置 Image 的精灵。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSprite(this Image image, string location, EResourceBindingOption options,
            CancellationToken cancellationToken = default, string packageName = "")
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
            bindingService.BindSprite(owner, image, SpriteKey(location, packageName), options);
        }

        #endregion

        #region SpriteRenderer SetSprite [SPRITE RENDERER SET SPRITE]

        /// <summary>
        /// 设置 SpriteRenderer 的精灵。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSprite(this SpriteRenderer spriteRenderer, string location,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            SetSprite(spriteRenderer, location, EResourceBindingOption.None, cancellationToken, packageName);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的精灵。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSprite(this SpriteRenderer spriteRenderer, string location,
            EResourceBindingOption options, CancellationToken cancellationToken = default, string packageName = "")
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
            bindingService.BindSprite(owner, spriteRenderer, SpriteKey(location, packageName), options);
        }

        private static ResourceKey SpriteKey(string location, string packageName) =>
            new ResourceKey(location, packageName, typeof(Sprite), EResourceAssetKind.Sprite);

        private static ResourceKey SubAssetsKey(string location, string packageName) =>
            new ResourceKey(location, packageName, typeof(Sprite), EResourceAssetKind.SubAssets);

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
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSubSprite(this Image image, string location, string spriteName,
            bool setNativeSize = false, CancellationToken cancellationToken = default, string packageName = "")
        {
            EResourceBindingOption options = setNativeSize
                ? EResourceBindingOption.SetNativeSize
                : EResourceBindingOption.None;
            SetSubSprite(image, location, spriteName, options, cancellationToken, packageName);
        }

        /// <summary>
        /// 设置 Image 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSubSprite(this Image image, string location, string spriteName,
            EResourceBindingOption options, CancellationToken cancellationToken = default, string packageName = "")
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
            FireAndForget(bindingService.BindSubSpriteAsync(owner, image,
                SubAssetsKey(location, packageName), spriteName, options, cancellationToken));
        }

        /// <summary>
        /// 设置 SpriteRenderer 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName,
            CancellationToken cancellationToken = default, string packageName = "")
        {
            SetSubSprite(spriteRenderer, location, spriteName, EResourceBindingOption.None, cancellationToken, packageName);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的子精灵（从图集中获取）。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">图集资源定位地址。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="packageName">资源包名称；留空走默认包，DLC 包里的精灵要显式给出。</param>
        public static void SetSubSprite(this SpriteRenderer spriteRenderer, string location, string spriteName,
            EResourceBindingOption options, CancellationToken cancellationToken = default, string packageName = "")
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
            FireAndForget(bindingService.BindSubSpriteAsync(owner, spriteRenderer,
                SubAssetsKey(location, packageName), spriteName, options, cancellationToken));
        }

        #endregion
    }
}