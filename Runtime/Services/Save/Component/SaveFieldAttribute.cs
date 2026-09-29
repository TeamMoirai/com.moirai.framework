using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 无代码保存字段标记：标注于 MonoBehaviour 字段上，由 SaveHost SourceGenerator 编译期生成强类型捕获器。
    /// </summary>
    /// <remarks>
    /// 运行期按 <see cref="SaveComponent"/> 的勾选配置过滤捕获字段（编译期生成全字段捕获代码，勾选只是运行时掩码）。
    /// 支持类型（SG v2）：基元/枚举/字符串、Unity 数学类型（Vector2/3/4、Quaternion、Color、Rect、Bounds）、DateTime/TimeSpan、 <br />
    /// 集合（<c>T[]/List/Queue/Stack/HashSet/Dictionary</c>，元素递归支持标量与嵌套数据类，引用元素暂不支持）、嵌套 <see cref="SaveDataAttribute"/> 标注类、 <br />
    /// UnityEngine.Object 引用。
    /// 其中 GameObject/Component 派生按场景引用存 <see cref="SaveObjectIdentity"/> 稳定 ID，其余按资产引用存 <see cref="SaveAssetCatalog"/> 定位串。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class SaveFieldAttribute : Attribute
    {
        /// <summary>
        /// 存档键（null = 使用字段名）。重命名会破坏旧档读取，需要改名时显式指定键保持稳定。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 创建字段标记（键 = 字段名）。
        /// </summary>
        public SaveFieldAttribute()
        {
            Key = null;
        }

        /// <summary>
        /// 创建字段标记并显式指定存档键。
        /// </summary>
        /// <param name="key">存档键（稳定标识，字段重命名不影响旧档）。</param>
        public SaveFieldAttribute(string key)
        {
            Key = key;
        }
    }
}
