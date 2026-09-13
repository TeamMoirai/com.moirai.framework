using System;
using System.Collections.Generic;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档组件捕获器注册表：组件类型 → 生成捕获器（SaveHost SourceGenerator 经模块初始化器自注册，零反射）。
    /// <para>未注册类型（未标 <see cref="SaveFieldAttribute"/> 字段或生成器未覆盖）在 <see cref="SaveComponent"/> 捕获期记录告警并跳过。</para>
    /// </summary>
    public static class SaveCapturerRegistry
    {
        /// <summary>组件类型 → 捕获器实例。</summary>
        private static readonly Dictionary<Type, ISaveComponentCapturer> s_Capturers = new Dictionary<Type, ISaveComponentCapturer>();

        /// <summary>
        /// 注册捕获器（同类型重复注册以最新为准——程序集重载场景幂等）。
        /// </summary>
        /// <param name="componentType">组件类型。</param>
        /// <param name="capturer">捕获器实例。</param>
        public static void Register(Type componentType, ISaveComponentCapturer capturer)
        {
            s_Capturers[componentType] = capturer;
        }

        /// <summary>
        /// 尝试获取指定组件类型的捕获器。
        /// </summary>
        /// <param name="componentType">组件类型。</param>
        /// <param name="capturer">命中时的捕获器实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(Type componentType, out ISaveComponentCapturer capturer)
        {
            return s_Capturers.TryGetValue(componentType, out capturer);
        }
    }
}
