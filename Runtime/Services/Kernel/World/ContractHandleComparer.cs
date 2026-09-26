using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 以 <see cref="RuntimeTypeHandle.Value"/>（类型句柄指针）判定契约键相等性的比较器。
    /// <para>契约解析是注册、注销与跨作用域绑定维护每次都要走的热点：显式给出指针相等与指针哈希，
    /// 就不把取键语义寄托在 <see cref="RuntimeTypeHandle"/> 各运行时实现自身的哈希策略上
    /// （同包的 <c>MemoryPoolRegistry</c> 已按 <c>Type.TypeHandle.Value</c> 指针建表）。</para>
    /// </summary>
    internal sealed class ContractHandleComparer : IEqualityComparer<RuntimeTypeHandle>
    {
        /// <summary>
        /// 获取比较器单例实例。
        /// </summary>
        public static readonly ContractHandleComparer Instance = new ContractHandleComparer();

        private ContractHandleComparer() { }

        /// <summary>
        /// 按类型句柄指针判断两个契约键是否同型。
        /// </summary>
        /// <param name="x">待比较的第一个句柄。</param>
        /// <param name="y">待比较的第二个句柄。</param>
        /// <returns>句柄指向同一类型返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public bool Equals(RuntimeTypeHandle x, RuntimeTypeHandle y) => x.Value == y.Value;

        /// <summary>
        /// 取类型句柄指针的哈希码。
        /// </summary>
        /// <param name="obj">待计算哈希码的句柄。</param>
        /// <returns>句柄指针的哈希码。</returns>
        public int GetHashCode(RuntimeTypeHandle obj) => obj.Value.GetHashCode();
    }
}
