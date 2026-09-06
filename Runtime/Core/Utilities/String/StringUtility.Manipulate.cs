namespace Moirai.Atropos
{
    public static partial class StringUtility
    {
        #region 插入 [INSERT]

        /// <summary>
        /// 在源字符串的指定位置插入字符串，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="index">插入位置（从零开始的字符索引）。</param>
        /// <param name="value">要插入的字符串。</param>
        /// <returns>插入后的新字符串。</returns>
        public static string Insert(string source, int index, string value)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Insert(index, value);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 在源字符串的指定位置插入字符，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="index">插入位置（从零开始的字符索引）。</param>
        /// <param name="value">要插入的字符。</param>
        /// <returns>插入后的新字符串。</returns>
        public static string Insert(string source, int index, char value)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Insert(index, value);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 在源字符串的指定位置重复插入字符串，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="index">插入位置（从零开始的字符索引）。</param>
        /// <param name="value">要插入的字符串。</param>
        /// <param name="count">重复插入次数。</param>
        /// <returns>插入后的新字符串。</returns>
        public static string Insert(string source, int index, string value, int count)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Insert(index, value, count);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        #endregion

        #region 移除 [REMOVE]

        /// <summary>
        /// 从源字符串中移除指定范围的字符，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="startIndex">移除起始位置（从零开始的字符索引）。</param>
        /// <param name="length">要移除的字符数。</param>
        /// <returns>移除后的新字符串。</returns>
        public static string Remove(string source, int startIndex, int length)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Remove(startIndex, length);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        #endregion

        #region 替换 [REPLACE]

        /// <summary>
        /// 在源字符串中替换字符，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="oldChar">要替换的字符。</param>
        /// <param name="newChar">替换后的字符。</param>
        /// <returns>替换后的新字符串。</returns>
        public static string Replace(string source, char oldChar, char newChar)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Replace(oldChar, newChar);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 在源字符串的指定范围内替换字符，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="oldChar">要替换的字符。</param>
        /// <param name="newChar">替换后的字符。</param>
        /// <param name="startIndex">替换范围起始位置（从零开始的字符索引）。</param>
        /// <param name="count">替换范围内的字符数。</param>
        /// <returns>替换后的新字符串。</returns>
        public static string Replace(string source, char oldChar, char newChar, int startIndex, int count)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Replace(oldChar, newChar, startIndex, count);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 在源字符串中替换字符串，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="oldValue">要替换的子字符串。</param>
        /// <param name="newValue">替换后的子字符串。</param>
        /// <returns>替换后的新字符串。</returns>
        public static string Replace(string source, string oldValue, string newValue)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Replace(oldValue, newValue);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 在源字符串的指定范围内替换字符串，返回新字符串。内部走池化构建器，自动管理生命周期。
        /// </summary>
        /// <param name="source">源字符串。null 时返回 <see cref="string.Empty"/>。</param>
        /// <param name="oldValue">要替换的子字符串。</param>
        /// <param name="newValue">替换后的子字符串。</param>
        /// <param name="startIndex">替换范围起始位置（从零开始的字符索引）。</param>
        /// <param name="count">替换范围内的字符数。</param>
        /// <returns>替换后的新字符串。</returns>
        public static string Replace(string source, string oldValue, string newValue, int startIndex, int count)
        {
            if (source == null) return string.Empty;

            var sb = CreateStringBuilder();
            try
            {
                sb.Append(source);
                sb.Replace(oldValue, newValue, startIndex, count);
                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        #endregion
    }
}
