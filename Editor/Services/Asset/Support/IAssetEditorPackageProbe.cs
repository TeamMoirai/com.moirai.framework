namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 后端在编辑器侧提供的两项读数：资源包名清单与默认包寻址位。
    /// </summary>
    /// <remarks>
    /// 实现跟着后端走：谁的程序集里有这个后端，谁登记探针，并由 <see cref="AppliesTo"/> 自己认领。<br />
    /// 框架侧只按认领结果取数，永不 <c>is</c> 具体后端类型——那种判定会被装配缺失静默裁成恒假。
    /// </remarks>
    public interface IAssetEditorPackageProbe
    {
        /// <summary>
        /// 这份探针是否适用于当前后端实例。不认领时桥退回空数组，绝不拿别人的清单冒名。
        /// </summary>
        bool AppliesTo(IAssetService handler);

        /// <summary>
        /// 收集器里配好的资源包名清单。
        /// </summary>
        string[] PackageNames();

        /// <summary>
        /// 默认包是否开了寻址模式：UI 代码生成器据此决定预制体的 AB 路径取哪一套。
        /// </summary>
        bool DefaultPackageIsAddressable();
    }
}
