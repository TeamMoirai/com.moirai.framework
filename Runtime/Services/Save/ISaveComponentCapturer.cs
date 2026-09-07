using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档组件捕获器契约（SaveHost SourceGenerator 逐组件类型生成的强类型实现）。
    /// <para>捕获写入「键 = 组件类型全名」的嵌套作用域（载荷 = 启用字段记录集），
    /// 恢复由调用方（<see cref="SaveComponent"/>）读出作用域头后按记录数精确消费——绑定间顺序解耦。</para>
    /// </summary>
    public interface ISaveComponentCapturer
    {
        /// <summary>
        /// 捕获器负责的组件类型。
        /// </summary>
        Type ComponentType { get; }

        /// <summary>
        /// 全量字段名数组（索引 = 掩码索引；内容 = 存档键，默认字段名）。
        /// </summary>
        string[] FieldNames { get; }

        /// <summary>
        /// 将组件的启用字段捕获为键值字节（写入类型名键的嵌套作用域，主线程调用）。
        /// </summary>
        /// <param name="component">组件实例。</param>
        /// <param name="writer">键值写入器。</param>
        /// <param name="mask">字段掩码。</param>
        void Capture(object component, ref SaveKeyValueWriter writer, in SaveFieldMask mask);

        /// <summary>
        /// 从键值字节恢复组件的启用字段（精确消费 recordCount 条记录；未知键跳过、禁用字段跳过、缺失字段保留当前值；主线程调用）。
        /// </summary>
        /// <param name="component">组件实例。</param>
        /// <param name="reader">键值读取器（已进入本类型的作用域，指向首条记录）。</param>
        /// <param name="recordCount">作用域内记录数（捕获期写入的启用字段数）。</param>
        /// <param name="mask">字段掩码。</param>
        void Restore(object component, ref SaveKeyValueReader reader, int recordCount, in SaveFieldMask mask);
    }
}
