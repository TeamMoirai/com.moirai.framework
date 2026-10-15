using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 插件清单里的一条包间依赖。
    /// </summary>
    [Serializable]
    internal sealed class PluginDependency
    {
        public string id;
        public string version;
        public string versionOperator;

        /// <summary>判定给定已装版本是否满足这条依赖。</summary>
        /// <remarks>
        /// 运算符或版本不可解析时一律判不满足——不做"解析失败当作满足"的降级。
        /// </remarks>
        internal bool Matches(PluginVersion installed)
        {
            if (!PluginVersion.TryParse(version, out PluginVersion required)) return false;
            if (!PluginVersionOperatorUtility.TryParse(versionOperator, out PluginVersionOperator op)) return false;

            switch (op)
            {
                case PluginVersionOperator.GreaterOrEqual: return installed >= required;
                case PluginVersionOperator.Greater: return installed > required;
                case PluginVersionOperator.Equal: return installed == required;
                case PluginVersionOperator.LessOrEqual: return installed <= required;
                case PluginVersionOperator.Less: return installed < required;
                default: return false;
            }
        }
    }
}
