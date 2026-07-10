using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 字符串工具静态门面，提供格式化、构建和连接功能。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 使用方式：
    /// <code>
    /// // 直接格式化
    /// string msg = StringUtility.Format("HP: {0}/{1}", hp, maxHp);
    ///
    /// // 构建器模式（推荐高频场景）
    /// var sb = StringUtility.CreateStringBuilder();
    /// sb.Append("Hello ").Append(name);
    /// string result = sb.ToStringAndDispose();
    ///
    /// // 简化模式（自动管理生命周期）
    /// string result = StringUtility.GetString(sb => {
    ///     sb.Append("Hello ").Append(name);
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static partial class StringUtility
    {
        private static IStringHelper s_Helper;

        /// <summary>
        /// 设置字符串工具实现。
        /// </summary>
        /// <param name="helper">要设置的工具实现。</param>
        public static void SetHelper(IStringHelper helper)
        {
            s_Helper = helper;
        }

        /// <summary>
        /// 获取一个字符串构建器适配器。
        /// </summary>
        /// <param name="capacity">初始容量。</param>
        /// <returns>可复用的字符串构建器适配器。</returns>
        public static IStringBuilder CreateStringBuilder(int capacity = 256)
        {
            if (s_Helper == null)
            {
                s_Helper = new DefaultStringHelper();
            }
            return s_Helper.CreateStringBuilder(capacity);
        }

        /// <summary>
        /// 使用适配器构建字符串（简化模式）。
        /// </summary>
        /// <param name="action">构建字符串的操作。</param>
        /// <returns>构建的字符串。</returns>
        public static string GetString(Action<IStringBuilder> action)
        {
            if (s_Helper == null)
            {
                s_Helper = new DefaultStringHelper();
            }
            return s_Helper.GetString(action);
        }

        /// <summary>
        /// 清空所有缓存和池。
        /// </summary>
        public static void Clear()
        {
            s_Helper?.Clear();
        }
    }
}
