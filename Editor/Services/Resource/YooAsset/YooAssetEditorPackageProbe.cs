using System;
using System.Collections.Generic;
using Moirai.Atropos.Resource;
using UnityEditor;
using YooAsset.Editor;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// YooAsset 在编辑器侧的包名与寻址位探针：读收集器设置，并只认领自己的后端实例。
    /// </summary>
    /// <remarks>
    /// 本类随 YooAsset 后端一起搬进插件（Task 3），登记点也一起走：框架侧不引用插件装配，
    /// 也不按具体后端类型判定——不装 YooAsset 时这里没人登记，桥退回空数组、检视器退回自由文本。<br />
    /// <see cref="AppliesTo"/> 用 <c>is</c> 判自己的类型是合法的：判定方与后端住在同一个程序集里。
    /// </remarks>
    internal sealed class YooAssetEditorPackageProbe : IResourceEditorPackageProbe
    {
        /// <summary>收集器包名清单缓存：按域有效，<see cref="InvalidateCache"/> 后重读。这份缓存跟着读数住在探针里。</summary>
        private static string[] s_PackageNames;

        private static bool s_PackageNamesCached;

        /// <summary>域加载即登记：覆盖式登记，同一次加载里后登记者生效。</summary>
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ResourcePackageBridge.RegisterEditorPackageProbe(new YooAssetEditorPackageProbe());

            // 私有缓存桥看不见，失效也就由读数自己接：收集器改动落盘（projectChanged）即重读，口径同框架侧的桥
            EditorApplication.projectChanged += InvalidateCache;
        }

        /// <inheritdoc />
        public bool AppliesTo(ResourceServiceHandler handler) => handler is YooAssetHandler;

        /// <inheritdoc />
        public string[] PackageNames()
        {
            if (s_PackageNamesCached)
            {
                return s_PackageNames;
            }

            s_PackageNames = ReadPackageNames();
            s_PackageNamesCached = true;
            return s_PackageNames;
        }

        /// <inheritdoc />
        public bool DefaultPackageIsAddressable()
        {
            try
            {
                return BundleCollectorSettingData.Setting.GetPackage("DefaultPackage")?.EnableAddressable == true;
            }
            catch (Exception)
            {
                // 收集器没配好等同「未开寻址」：这条读数只用来决定 AB 路径取法，不该炸掉代码生成。
                return false;
            }
        }

        /// <summary>使清单缓存失效（<see cref="EditorApplication.projectChanged"/> 触发，收集器改动落盘即生效）。</summary>
        private static void InvalidateCache()
        {
            s_PackageNames = null;
            s_PackageNamesCached = false;
        }

        /// <summary>
        /// 收集器设置（<see cref="BundleCollectorSettingData"/>）里已配置的资源包名，保持配置顺序、跳过空名；
        /// 按域缓存，失效口径同 <see cref="PackageNames"/>。
        /// </summary>
        /// <remarks>
        /// 不可直读：<see cref="BundleCollectorSettingData.HasSettingAsset"/> 内部是全工程
        /// <c>AssetDatabase.FindAssets("t:BundleCollectorSetting")</c>（实测单次约 27ms，峰值 606ms），
        /// 而本方法经 <c>AssetReference</c> 与 <c>ResourcePackageDropdown</c> 两条检视路径按绘制帧调用。
        /// </remarks>
        private static string[] ReadPackageNames()
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
