namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// HandlerHost 源生成器引用的约定常量。
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// 标记 HandlerHost 的特性类型完全限定名，生成器按该名称查找目标类上的特性。
        /// </summary>
        public const string AttributeFullyQualifiedName = "Moirai.Atropos.HandlerHostAttribute";
        /// <summary>
        /// 代码兜底工厂方法的约定名称，懒加载时作为 <see cref="GetHandlerFromSettingsMethodName"/> 返回 null 的回退来源。
        /// </summary>
        public const string CreateDefaultHandlerMethodName = "CreateDefaultHandler";
        /// <summary>
        /// settings 来源工厂方法的约定名称，懒加载时优先调用并要求返回非空值。
        /// </summary>
        public const string GetHandlerFromSettingsMethodName = "GetHandlerFromSettings";
    }
}
