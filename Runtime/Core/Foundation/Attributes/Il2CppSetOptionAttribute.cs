using System;

namespace Unity.IL2CPP.CompilerServices
{
    /// <summary>
    /// IL2CPP IL→C++ 转换的代码生成选项。
    /// <para>与本引擎内唯一官方副本（com.unity.logging 内部定义）保持同命名空间、同名成员与同数值；
    /// 自定义特性 blob 中枚举参数按底层 int 编码且不携带枚举类型身份，il2cpp 转换器仅按数值匹配语义。</para>
    /// </summary>
    internal enum Option
    {
        /// <summary>
        /// 空检查代码生成开关（全局默认启用）。
        /// <para>关闭后生成代码不再抛出 NullReferenceException——多数情况下空引用解引用直接崩溃，
        /// 且崩溃点可能晚于本应插入空检查的位置。</para>
        /// </summary>
        NullChecks = 1,

        /// <summary>
        /// 数组越界检查代码生成开关（全局默认启用）。
        /// <para>关闭后生成代码不再抛出 IndexOutOfRangeException，可无运行时检查地读写数组界外内存，须极度谨慎。</para>
        /// </summary>
        ArrayBoundsChecks = 2,

        /// <summary>
        /// 除零检查代码生成开关（全局默认关闭）。
        /// <para>开启后生成代码中除零将抛出 DivideByZeroException；绝大多数代码无需处理该异常，通常保持关闭。</para>
        /// </summary>
        DivideByZeroChecks = 3,
    }

    /// <summary>
    /// 标注在程序集/结构体/类/方法/属性/委托上，指示 IL2CPP 转换器覆盖某项运行时检查的全局设置。
    /// <para>il2cpp 转换器按属性完整类型名匹配、不校验程序集身份，故本引擎未随 UnityEngine 公开该类型时，
    /// 各程序集自带 internal 同名副本即可生效（UniTask 等 Cysharp 库同款做法），与 com.unity.logging
    /// 的内部副本按程序集隔离互不冲突。</para>
    /// <para>仅影响 IL2CPP Player 构建；Editor 下 Mono 保持全量隐式检查，开发期 Fail-Fast 语义不变，
    /// 框架公共 API 的显式 GameException 校验亦不受隐式检查关闭影响。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// [assembly: Il2CppSetOption(Option.NullChecks, false)]
    /// [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    /// public static void HotPathMethod() { /* ... */ }
    /// </code>
    /// </example>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Struct | AttributeTargets.Class
        | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Delegate,
        Inherited = false,
        AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : Attribute
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取目标代码生成选项。
        /// </summary>
        public Option Option { get; private set; }

        /// <summary>
        /// 获取选项值（true 启用 / false 关闭对应检查）。
        /// </summary>
        public object Value { get; private set; }

        #endregion

        #region 构造 [CONSTRUCTORS]

        /// <summary>
        /// 构造 IL2CPP 代码生成选项标注。
        /// </summary>
        /// <param name="option">目标代码生成选项。</param>
        /// <param name="value">是否启用该检查。</param>
        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }

        #endregion
    }
}
