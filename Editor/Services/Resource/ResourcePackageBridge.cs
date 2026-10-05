using System;
using System.Collections.Generic;
using YooAsset.Editor;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 资源包名清单的编辑器桥：传入 <see cref="ResourceServiceHandler"/> 取该后端可用的资源包名数组。
    /// </summary>
    /// <remarks>
    /// 包名清单是编辑器侧数据（YooAsset 读收集器设置，见 <see cref="BundleCollectorSettingData"/>），运行时无此需求， <br />
    /// 故住 Editor 程序集而非 Runtime 接缝； <br />
    /// 返回 <c>null</c> 表示后端不消费包名（Addressables 单隐式目录，检视器不绘制包名行）； <br />
    /// 空数组表示后端消费包名但清单未就绪（收集器未配置）或无编辑器桥（自定义后端），检视器退回自由文本。
    /// </remarks>
    internal static class ResourcePackageBridge
    {
        /// <summary>
        /// 取后端可用的资源包名数组，<c>null</c> / 空数组的语义见类型备注。
        /// </summary>
        internal static string[] GetPackageNames()
        {
            ResourceServiceHandler handler = ResourceServiceSettings.ResourceServiceHandler;
            
            switch (handler)
            {
                case YooAssetHandler _:
                    return CollectYooAssetPackages();
#if ADDRESSABLES_INSTALLED
                case AddressableHandler _:
                    return null;
#endif
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 收集器设置（<see cref="BundleCollectorSettingData"/>）里已配置的资源包名，保持配置顺序、跳过空名。
        /// </summary>
        internal static string[] CollectYooAssetPackages()
        {
            if (!BundleCollectorSettingData.HasSettingAsset())
            {
                return Array.Empty<string>();
            }

            List<BundleCollectorPackage> packages = BundleCollectorSettingData.Setting.Packages;
            if (packages == null || packages.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[packages.Count];
            int count = 0;
            for (int i = 0; i < packages.Count; i++)
            {
                string packageName = packages[i].PackageName;
                if (!string.IsNullOrEmpty(packageName))
                {
                    names[count++] = packageName;
                }
            }

            Array.Resize(ref names, count);
            return names;
        }
    }
}
