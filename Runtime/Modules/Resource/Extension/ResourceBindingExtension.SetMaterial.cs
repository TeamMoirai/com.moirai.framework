using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Resource
{
    public static partial class ResourceBindingExtension
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
            SetMaterial(image, location, EResourceBindingOption.None, isAsync, packageName);
        }

        /// <summary>
        /// 设置 Image 的材质。
        /// </summary>
        /// <param name="image">目标 Image。</param>
        /// <param name="location">资源定位地址。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="isAsync">是否异步加载。</param>
        /// <param name="packageName">资源包名称。</param>
        public static void SetMaterial(this Image image, string location, EResourceBindingOption options,
            bool isAsync = false, string packageName = "")
        {
            if (image == null)
            {
                throw new GameException("SetMaterial failed. Because image is null.");
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = EnsureOwner(bindingService, image);
            if (isAsync)
            {
                bindingService.BindImageMaterialAsync(owner, image, MaterialKey(location, packageName), options)
                    .Forget();
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
            SetMaterial(spriteRenderer, location, EResourceBindingOption.None, isAsync, packageName);
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
            EResourceBindingOption options, bool isAsync = false, string packageName = "")
        {
            if (spriteRenderer == null)
            {
                throw new GameException("SetMaterial failed. Because spriteRenderer is null.");
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = EnsureOwner(bindingService, spriteRenderer);
            if (isAsync)
            {
                bindingService.BindSharedMaterialAsync(owner, spriteRenderer, MaterialKey(location, packageName),
                    options).Forget();
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
            SetMaterial(meshRenderer, location, EResourceBindingOption.None, needInstance, isAsync, packageName);
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
            EResourceBindingOption options, bool needInstance = true, bool isAsync = false,
            string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new GameException("SetMaterial failed. Because meshRenderer is null.");
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = EnsureOwner(bindingService, meshRenderer);
            if (isAsync)
            {
                if (needInstance)
                {
                    bindingService.BindMaterialInstanceAsync(owner, meshRenderer,
                        MaterialKey(location, packageName), options).Forget();
                }
                else
                {
                    bindingService.BindSharedMaterialAsync(owner, meshRenderer,
                        MaterialKey(location, packageName), options).Forget();
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
            SetSharedMaterial(meshRenderer, location, EResourceBindingOption.None, isAsync, packageName);
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
            EResourceBindingOption options, bool isAsync = false, string packageName = "")
        {
            if (meshRenderer == null)
            {
                throw new GameException("SetSharedMaterial failed. Because meshRenderer is null.");
            }

            if (!TryGetResourceService(out _, out IResourceBindingService bindingService))
            {
                return;
            }

            ResourceOwner owner = EnsureOwner(bindingService, meshRenderer);
            if (isAsync)
            {
                bindingService.BindSharedMaterialAsync(owner, meshRenderer, MaterialKey(location, packageName),
                    options).Forget();
                return;
            }

            bindingService.BindSharedMaterial(owner, meshRenderer, MaterialKey(location, packageName), options);
        }

        private static ResourceKey MaterialKey(string location, string packageName)
        {
            return new ResourceKey(location, packageName, typeof(Material), EResourceAssetKind.Material);
        }

        #endregion
    }
}