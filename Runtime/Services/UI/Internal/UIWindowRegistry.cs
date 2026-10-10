using System;
using System.Collections.Generic;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口注册表：类型句柄到「描述符 + 工厂」的唯一登记处，由 <c>UIWindowCodegen</c> 在模块初始化期逐类型填表。
    /// </summary>
    /// <remarks>
    /// 登记发生在模块初始化期（各程序集加载即注册自己的 <c>[Window]</c> 窗口类），之后只读。 <br />
    /// 工厂是编译期生成的 <c>static () =&gt; new X()</c>：零运行期反射、IL2CPP 同构，开窗路径不碰 <c>Activator</c>。 <br />
    /// 重复注册不抛（模块初始化期抛出等于把编辑器整崩），记一条 Fatal 并保留先到那份——跨程序集不会同型，撞号只来自手写与生成双登记。 <br />
    /// 线程契约：注册与读取都在主线程（模块初始化与开窗同线程）。
    /// </remarks>
    public static class UIWindowRegistry
    {
        /// <summary>类型句柄到注册条目的表：比较器与内核服务查找同款，零装箱。</summary>
        private static readonly Dictionary<RuntimeTypeHandle, UIWindowRegistryEntry> s_Entries =
            new Dictionary<RuntimeTypeHandle, UIWindowRegistryEntry>(64, ContractHandleComparer.Instance);

        /// <summary>
        /// 登记一个窗口类。
        /// </summary>
        /// <remarks>由生成的模块初始化器调用；手写补登同一类型时保留先到那份并记一条 Fatal。</remarks>
        /// <param name="windowType">窗口类。</param>
        /// <param name="descriptor">注册期解析好的元数据描述符。</param>
        /// <param name="factory">窗口工厂（编译期生成的无参构造委托）。</param>
        public static void Register(Type windowType, UIWindowDescriptor descriptor, Func<UIWindow> factory)
        {
            if (windowType == null || factory == null)
            {
                return;
            }

            if (s_Entries.ContainsKey(windowType.TypeHandle))
            {
                LogUtility.Fatal("UI 窗口 '{0}' 被重复注册：保留先到那份（手写补登与生成登记撞了同型）", windowType.FullName);
                return;
            }

            s_Entries[windowType.TypeHandle] = new UIWindowRegistryEntry(descriptor, factory);
        }

        /// <summary>
        /// 按类型取登记条目。
        /// </summary>
        /// <param name="windowType">窗口类。</param>
        /// <param name="entry">登记的条目；未登记时为默认值。</param>
        /// <returns>已登记时为真。</returns>
        internal static bool TryGet(Type windowType, out UIWindowRegistryEntry entry)
        {
            return s_Entries.TryGetValue(windowType.TypeHandle, out entry);
        }

        /// <summary>
        /// 注册条目：描述符与工厂成对，开窗一次查表全取。
        /// </summary>
        internal readonly struct UIWindowRegistryEntry
        {
            /// <summary>注册期解析好的元数据描述符。</summary>
            public readonly UIWindowDescriptor Descriptor;

            /// <summary>窗口工厂（编译期生成的无参构造委托）。</summary>
            public readonly Func<UIWindow> Factory;

            /// <summary>
            /// 构造一条登记。
            /// </summary>
            /// <param name="descriptor">元数据描述符。</param>
            /// <param name="factory">窗口工厂。</param>
            public UIWindowRegistryEntry(UIWindowDescriptor descriptor, Func<UIWindow> factory)
            {
                Descriptor = descriptor;
                Factory = factory;
            }
        }
    }
}
