namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 版本运算符字面量的解析面。
    /// </summary>
    internal static class PluginVersionOperatorUtility
    {
        /// <summary>解析运算符字面量，未识别一律判非。</summary>
        public static bool TryParse(string text, out PluginVersionOperator value)
        {
            switch (text)
            {
                case ">=":
                    value = PluginVersionOperator.GreaterOrEqual;
                    return true;
                case ">":
                    value = PluginVersionOperator.Greater;
                    return true;
                case "=":
                    value = PluginVersionOperator.Equal;
                    return true;
                case "<=":
                    value = PluginVersionOperator.LessOrEqual;
                    return true;
                case "<":
                    value = PluginVersionOperator.Less;
                    return true;
                default:
                    value = PluginVersionOperator.Unknown;
                    return false;
            }
        }
    }
}
