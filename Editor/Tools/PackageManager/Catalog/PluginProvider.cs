using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 清单里的一段「本插件为哪个抽象基类提供哪个实现类」，供 Provider 下拉铺未安装行。
    /// </summary>
    /// <remarks>
    /// 只由框架编辑器读，Unity 不读清单；<see cref="providerType"/> 存在性就是「已装且 SDK 齐」的判据，
    /// 因此插件不必为下拉另写一份占位实现类。<br />
    /// JSON 键名不取 <c>base</c>：那是 C# 保留字，<c>JsonUtility</c> 的字段名即键名，撞不上。
    /// </remarks>
    [Serializable]
    internal sealed class PluginProvider
    {
        /// <summary>被实现基类的类型全名；下拉按它把行归到对应字段。</summary>
        public string providerBase;

        /// <summary>本插件装好且其 SDK 齐时应当出现的实现类类型全名。</summary>
        public string providerType;

        /// <summary>下拉行显示名；空时回退清单的 <c>name</c>。</summary>
        public string title;

        /// <summary>该实现所需的第三方 UPM 包 id；空表示只随本插件本体。</summary>
        public string requiresUpm;
    }
}
