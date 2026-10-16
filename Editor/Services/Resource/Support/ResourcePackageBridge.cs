using System;
using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 资源包名清单的编辑器桥：传入 <see cref="ResourceServiceHandler"/> 取该后端可用的资源包名数组。
    /// </summary>
    /// <remarks>
    /// 包名清单是编辑器侧数据（YooAsset 读收集器设置，见 <see cref="BundleCollectorSettingData"/>），运行时无此需求， <br />
    /// 故住 Editor 程序集而非 Runtime 接缝； <br />
    /// 返回 <c>null</c> 表示后端自报不消费包名轴（只有一张隐式目录的后端，如 Addressables 插件；检视器不绘制包名行）； <br />
    /// 空数组表示后端消费包名但清单未就绪（收集器未配置）或无编辑器桥（自定义后端），检视器退回自由文本。 <br />
    /// 本桥被检视器逐绘制帧调用，清单按域缓存，<see cref="EditorApplication.projectChanged"/> 后失效。 <br />
    /// <see cref="GetPackageNames"/> 对外公开：宿主自绘引用行（不走 <c>AssetReferenceDrawer</c>）时按同一清单、同一缓存取包名，避免第二份收集器读取实现。
    /// </remarks>
    public static class ResourcePackageBridge
    {
        /// <summary>包名清单缓存：null 与「后端不消费包名」语义一致，一并缓存。</summary>
        private static string[] s_PackageNames;

        private static bool s_PackageNamesCached;

        /// <summary>收集器包名清单缓存：按域有效，<see cref="InvalidatePackageNames"/> 后重读。</summary>
        private static string[] s_YooAssetPackages;

        private static bool s_YooAssetPackagesCached;

        static ResourcePackageBridge()
        {
            // 收集器配置与设置资产都住在资产库里：导入/移动/保存均触发 projectChanged，域重载本身即复位
            EditorApplication.projectChanged += InvalidatePackageNames;
        }

        /// <summary>
        /// 取后端可用的资源包名数组（逐绘制帧调用，走缓存），<c>null</c> / 空数组的语义见类型备注。
        /// </summary>
        public static string[] GetPackageNames()
        {
            if (s_PackageNamesCached)
            {
                return s_PackageNames;
            }

            ResourceServiceHandler handler = ResourceServiceSettings.ResourceServiceHandler;

            // 「要不要接管包名行」由被判定方自报（处理器能力属性）：后端可以住在插件装配里，
            // 编辑器装配既不许引用插件装配、也不该用 is 具体类型判它——那种判定会被静默裁成恒假。
            if (handler != null && !handler.ConsumesPackageNames)
            {
                s_PackageNames = null;
            }
            else
            {
                // 清单来源仍然只有内建后端的收集器：其余后端消费包名但没有编辑器桥，交空数组让检视器退回自由文本。
                s_PackageNames = handler is YooAssetHandler ? CollectYooAssetPackages() : Array.Empty<string>();
            }

            s_PackageNamesCached = true;
            return s_PackageNames;
        }

        /// <summary>
        /// 使包名清单缓存失效（<see cref="EditorApplication.projectChanged"/> 触发，收集器改动落盘即生效）。
        /// </summary>
        internal static void InvalidatePackageNames()
        {
            s_PackageNames = null;
            s_PackageNamesCached = false;
            s_YooAssetPackages = null;
            s_YooAssetPackagesCached = false;
        }

        /// <summary>
        /// 收集器设置（<see cref="BundleCollectorSettingData"/>）里已配置的资源包名，保持配置顺序、跳过空名；
        /// 按域缓存，失效口径同 <see cref="GetPackageNames"/>。
        /// </summary>
        /// <remarks>
        /// 不可直读：<see cref="BundleCollectorSettingData.HasSettingAsset"/> 内部是全工程
        /// <c>AssetDatabase.FindAssets("t:BundleCollectorSetting")</c>（实测单次约 27ms，峰值 606ms），
        /// 而本方法经 <c>AssetReference</c> 与 <c>ResourcePackageDropdown</c> 两条检视路径按绘制帧调用。
        /// </remarks>
        internal static string[] CollectYooAssetPackages()
        {
            if (s_YooAssetPackagesCached)
            {
                return s_YooAssetPackages;
            }

            s_YooAssetPackages = ReadYooAssetPackages();
            s_YooAssetPackagesCached = true;
            return s_YooAssetPackages;
        }

        /// <summary>无缓存直读收集器配置。</summary>
        private static string[] ReadYooAssetPackages()
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
