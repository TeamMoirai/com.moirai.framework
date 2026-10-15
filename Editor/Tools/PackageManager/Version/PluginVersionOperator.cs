namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 插件依赖的版本运算符。
    /// </summary>
    internal enum PluginVersionOperator : byte
    {
        Unknown = 0,
        GreaterOrEqual,
        Greater,
        Equal,
        LessOrEqual,
        Less
    }
}
