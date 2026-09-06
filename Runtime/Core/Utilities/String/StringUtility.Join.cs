using System;

namespace Moirai.Atropos
{
    public static partial class StringUtility
    {
        /// <summary>
        /// 使用分隔符连接 Span 中的元素，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="separator">分隔符。</param>
        /// <param name="values">要连接的元素只读 Span。</param>
        /// <returns>连接后的字符串。</returns>
        public static string Join<T>(string separator, ReadOnlySpan<T> values)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Join(separator, values);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 使用分隔符连接数组元素，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="separator">分隔符。</param>
        /// <param name="values">要连接的元素数组。</param>
        /// <returns>连接后的字符串。</returns>
        public static string Join<T>(string separator, T[] values)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Join(separator, values);
            }
            finally
            {
                sb.Dispose();
            }
        }
    }
}
