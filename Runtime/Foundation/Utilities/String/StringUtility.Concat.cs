namespace Moirai.Atropos
{
    public static partial class StringUtility
    {
        /// <summary>
        /// 将单个值转换为字符串，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T">值类型。</typeparam>
        /// <param name="value">要连接的值。</param>
        /// <returns>值的字符串表示。</returns>
        public static string Concat<T>(T value)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Concat(value);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 依次连接两个值为字符串，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T1">第一个值的类型。</typeparam>
        /// <typeparam name="T2">第二个值的类型。</typeparam>
        /// <param name="value1">第一个值。</param>
        /// <param name="value2">第二个值。</param>
        /// <returns>连接后的字符串。</returns>
        public static string Concat<T1, T2>(T1 value1, T2 value2)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Concat(value1, value2);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 依次连接三个值为字符串，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T1">第一个值的类型。</typeparam>
        /// <typeparam name="T2">第二个值的类型。</typeparam>
        /// <typeparam name="T3">第三个值的类型。</typeparam>
        /// <param name="value1">第一个值。</param>
        /// <param name="value2">第二个值。</param>
        /// <param name="value3">第三个值。</param>
        /// <returns>连接后的字符串。</returns>
        public static string Concat<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Concat(value1, value2, value3);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 依次连接四个值为字符串，内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <typeparam name="T1">第一个值的类型。</typeparam>
        /// <typeparam name="T2">第二个值的类型。</typeparam>
        /// <typeparam name="T3">第三个值的类型。</typeparam>
        /// <typeparam name="T4">第四个值的类型。</typeparam>
        /// <param name="value1">第一个值。</param>
        /// <param name="value2">第二个值。</param>
        /// <param name="value3">第三个值。</param>
        /// <param name="value4">第四个值。</param>
        /// <returns>连接后的字符串。</returns>
        public static string Concat<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            var sb = CreateStringBuilder();
            try
            {
                return sb.Concat(value1, value2, value3, value4);
            }
            finally
            {
                sb.Dispose();
            }
        }
    }
}
