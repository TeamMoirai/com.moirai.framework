using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Asset
{
    public static partial class AssetBindingExtension
    {
        #region SetMaterial [SET MATERIAL]

        /// <summary>
        /// 设置 Image 的材质。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this Image image, string location, bool isAsync = false,
            string packageName = "")
        {
            SetMaterial(image, location, EAssetBindingOption.None, isAsync, packageName);
        }

        /// <summary>
        /// 设置 Image 的材质。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this Image image, string location, EAssetBindingOption options,
            bool isAsync = false, string packageName = "")
        {
            if (image == null)
            {
                throw new MoiraiException("SetMaterial failed. Because image is null.");
            }

            if (!TryGetAssetService(out _, out IAssetBindingService bindingService))
            {
                return;
            }

            AssetOwner owner = AssetOwner.EnsureFor(image, bindingService);
            if (isAsync)
            {
                FireAndForget(bindingService.BindImageMaterialAsync(owner, image,
                    MaterialKey(location, packageName), options));
                return;
            }

            bindingService.BindImageMaterial(owner, image, MaterialKey(location, packageName), options);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的材质。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this SpriteRenderer spriteRenderer, string location, bool isAsync = false,
            string packageName = "")
        {
            SetMaterial(spriteRenderer, location, EAssetBindingOption.None, isAsync, packageName);
        }

        /// <summary>
        /// 设置 SpriteRenderer 的材质。
        /// </summary>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this SpriteRenderer spriteRenderer, string location,
            EAssetBindingOption options, bool isAsync = false, string packageName = "")
        {
            if (spriteRenderer == null)
            {
                throw new MoiraiException("SetMaterial failed. Because spriteRenderer is null.");
            }

            if (!TryGetAssetService(out _, out IAssetBindingService bindingService))
            {
                return;
            }

            AssetOwner owner = AssetOwner.EnsureFor(spriteRenderer, bindingService);
            if (isAsync)
            {
                FireAndForget(bindingService.BindSharedMaterialAsync(owner, spriteRenderer,
                    MaterialKey(location, packageName), options));
                return;
            }

            bindingService.BindSharedMaterial(owner, spriteRenderer, MaterialKey(location, packageName), options);
        }

        /// <summary>
        /// 设置 MeshRenderer 的材质。
        /// </summary>
        /// <param name="meshRenderer">目标 MeshRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="needInstance">是否需要材质实例。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this MeshRenderer meshRenderer, string location, bool needInstance = true,
            bool isAsync = false, string packageName = "")
        {
            SetMaterial(meshRenderer, location, EAssetBindingOption.None, needInstance, isAsync, packageName);
        }

        /// <summary>
        /// 设置 MeshRenderer 的材质。
        /// </summary>
        /// <param name="meshRenderer">目标 MeshRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="needInstance">是否需要材质实例。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this MeshRenderer meshRenderer, string location,
            EAssetBindingOption options, bool needInstance = true, bool isAsync = false,
            string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new MoiraiException("SetMaterial failed. Because meshRenderer is null.");
            }

            if (!TryGetAssetService(out _, out IAssetBindingService bindingService))
            {
                return;
            }

            AssetOwner owner = AssetOwner.EnsureFor(meshRenderer, bindingService);
            if (isAsync)
            {
                if (needInstance)
                {
                    FireAndForget(bindingService.BindMaterialInstanceAsync(owner, meshRenderer,
                        MaterialKey(location, packageName), options));
                }
                else
                {
                    FireAndForget(bindingService.BindSharedMaterialAsync(owner, meshRenderer,
                        MaterialKey(location, packageName), options));
                }

                return;
            }

            if (needInstance)
            {
                bindingService.BindMaterialInstance(owner, meshRenderer, MaterialKey(location, packageName),
                    options);
            }
            else
            {
                bindingService.BindSharedMaterial(owner, meshRenderer, MaterialKey(location, packageName),
                    options);
            }
        }

        /// <summary>
        /// 设置 MeshRenderer 的共享材质。
        /// </summary>
        /// <param name="meshRenderer">目标 MeshRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetSharedMaterial(this MeshRenderer meshRenderer, string location, bool isAsync = false,
            string packageName = "")
        {
            SetSharedMaterial(meshRenderer, location, EAssetBindingOption.None, isAsync, packageName);
        }

        /// <summary>
        /// 设置 MeshRenderer 的共享材质。
        /// </summary>
        /// <param name="meshRenderer">目标 MeshRenderer。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetSharedMaterial(this MeshRenderer meshRenderer, string location,
            EAssetBindingOption options, bool isAsync = false, string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new MoiraiException("SetSharedMaterial failed. Because meshRenderer is null.");
            }

            if (!TryGetAssetService(out _, out IAssetBindingService bindingService))
            {
                return;
            }

            AssetOwner owner = AssetOwner.EnsureFor(meshRenderer, bindingService);
            if (isAsync)
            {
                FireAndForget(bindingService.BindSharedMaterialAsync(owner, meshRenderer,
                    MaterialKey(location, packageName), options));
                return;
            }

            bindingService.BindSharedMaterial(owner, meshRenderer, MaterialKey(location, packageName), options);
        }

        private static AssetKey MaterialKey(string location, string packageName)
        {
            return new AssetKey(location, packageName, typeof(Material), EAssetKind.Material);
        }

        #endregion
    }
}