using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 字符串工具静态外观，提供格式化、连接、构建和操作功能。
    /// 通过可插拔的 <see cref="StringHandler"/> 实现底层池化策略，减少 GC 压力。
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
    [HandlerHost(typeof(StringHandler))]
    public static partial class StringUtility
    {
        private static StringHandler CreateDefaultHandler()
        {
#if ZSTRING_INSTALLED
            return new ZStringHandler();
#else
            return new DefaultStringHandler();
#endif
        }

        /// <summary>
        /// 获取一个池化字符串构建器适配器。
        /// </summary>
        /// <param name="capacity">初始容量（字符数）。</param>
        /// <returns>可复用的 <see cref="StringHandler.IStringBuilder"/>，使用后须调用 <see cref="IDisposable.Dispose"/> 或 <see cref="StringHandler.IStringBuilder.ToStringAndDispose"/> 归还池。</returns>
        public static StringHandler.IStringBuilder CreateStringBuilder(int capacity = 256) => Handler.CreateStringBuilder(capacity);

        /// <summary>
        /// 使用适配器构建字符串（简化模式），自动管理构建器生命周期。
        /// </summary>
        /// <param name="action">构建字符串的操作；接收一个池化 <see cref="StringHandler.IStringBuilder"/>，方法返回后自动归还池。</param>
        /// <returns>构建的字符串。</returns>
        public static string GetString(Action<StringHandler.IStringBuilder> action) => Handler.GetString(action);

        /// <summary>
        /// 清空所有缓存和池。通常在场景切换时调用。
        /// </summary>
        public static void Clear()
        {
            Handler.Clear();
        }
    }
}
