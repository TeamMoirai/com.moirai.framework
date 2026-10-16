using System;
using UnityEditor;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 资源包名清单的编辑器桥：传入 <see cref="ResourceServiceHandler"/> 取该后端可用的资源包名数组。
    /// </summary>
    /// <remarks>
    /// 包名清单是编辑器侧数据（由跟着后端走的 <see cref="IResourceEditorPackageProbe"/> 提供），运行时无此需求， <br />
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

        /// <summary>当前登记的编辑器侧探针：由提供后端的程序集自己登记，未登记即没有编辑器读数可用。</summary>
        private static IResourceEditorPackageProbe s_Probe;

        static ResourcePackageBridge()
        {
            // 收集器配置与设置资产都住在资产库里：导入/移动/保存均触发 projectChanged，域重载本身即复位
            EditorApplication.projectChanged += InvalidatePackageNames;
        }

        /// <summary>登记编辑器侧包名探针：由提供该后端的程序集自己调用，覆盖式。</summary>
        public static void RegisterEditorPackageProbe(IResourceEditorPackageProbe probe) => s_Probe = probe;

        /// <summary>当前后端的默认包是否开了寻址（探针不认领时 <c>false</c>）。</summary>
        public static bool DefaultPackageIsAddressable(ResourceServiceHandler handler) =>
            s_Probe != null && s_Probe.AppliesTo(handler) && s_Probe.DefaultPackageIsAddressable();

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

            // 「要不要接管包名行」由 Runtime 侧的处理器自报；「清单从哪读」由跟着后端走的探针自认领。
            // 编辑器装配既不许引用插件装配、也不许用 is 具体后端类型判它——会被静默裁成恒假。
            if (handler != null && !handler.ConsumesPackageNames)
            {
                s_PackageNames = null;
            }
            else if (s_Probe != null && s_Probe.AppliesTo(handler))
            {
                s_PackageNames = s_Probe.PackageNames();
            }
            else
            {
                // 没人认领＝消费包名轴但编辑器读不到清单（自定义后端或未装探针），退回自由文本。
                s_PackageNames = Array.Empty<string>();
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
        }

        /// <summary>用例复位：清掉按域缓存，避免同域内先跑的用例把后跑的用例糊住（探针登记本身跨用例保留）。</summary>
        internal static void Internal_ResetCacheForTest() => InvalidatePackageNames();
    }
}
