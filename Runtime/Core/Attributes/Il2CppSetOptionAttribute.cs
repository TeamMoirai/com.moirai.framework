using System;

namespace Unity.IL2CPP.CompilerServices
{
    /// <summary>
    /// IL2CPP IL→C++ 转换的代码生成选项。
    /// </summary>
    /// <remarks>
    /// 枚举数值须与引擎内官方副本（<c>com.unity.logging</c> 内部定义）一致：
    /// 特性 blob 按底层 <c>int</c> 编码且不携带枚举类型身份，il2cpp 转换器仅按数值匹配语义。
    /// </remarks>
    internal enum Option
    {
        /// <summary>空检查代码生成开关（全局默认启用）。</summary>
        /// <remarks>关闭后空引用解引用不再抛 <c>NullReferenceException</c>，而是直接崩溃或晚于应插入空检查处出错。</remarks>
        NullChecks = 1,

        /// <summary>数组越界检查代码生成开关（全局默认启用）。</summary>
        /// <remarks>关闭后界外读写不再抛 <c>IndexOutOfRangeException</c>，会无运行时检查地访问越界内存。</remarks>
        ArrayBoundsChecks = 2,

        /// <summary>除零检查代码生成开关（全局默认关闭）。</summary>
        /// <remarks>开启后除零抛 <c>DivideByZeroException</c>。</remarks>
        DivideByZeroChecks = 3,
    }

    /// <summary>标注在程序集/结构体/类/方法/属性/委托上，指示 IL2CPP 转换器关闭某项运行时检查。</summary>
    /// <remarks>
    /// 仅影响 IL2CPP Player 构建；Editor 下 Mono 保持全量隐式检查，框架显式 <c>GameException</c> 校验不受影响。 <br />
    /// 转换器按属性完整类型名匹配、不校验程序集身份，故各程序集可自带 internal 同名副本。
    /// </remarks>
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

        /// <summary>获取目标代码生成选项。</summary>
        public Option Option { get; private set; }

        /// <summary>获取选项值（true 启用 / false 关闭对应检查）。</summary>
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
