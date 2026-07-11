#if ZSTRING_INSTALLED
using System;
using Cysharp.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// ZString 字符串构建器适配器。<br/>
    /// 包装 <see cref="Cysharp.Text.Utf16ValueStringBuilder"/>，提供零分配的字符串操作。
    /// </summary>
    public sealed class ZStringBuilder : StringHandler.IStringBuilder
    {
        internal Utf16ValueStringBuilder builder;
        internal bool disposed;

        public ZStringBuilder(Utf16ValueStringBuilder builder)
        {
            this.builder = builder;
            disposed = false;
        }

        public static ZStringBuilder Create()
        {
            return new ZStringBuilder(ZString.CreateStringBuilder());
        }

        public int Length => builder.Length;

        public override string ToString()
        {
            return builder.ToString();
        }

        public string ToStringAndDispose()
        {
            string result = builder.ToString();
            Dispose();
            return result;
        }

        public StringHandler.IStringBuilder Clear()
        {
            builder.Clear();
            return this;
        }

        #region Append

        public StringHandler.IStringBuilder Append(string value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(char value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(char value, int repeatCount)
        {
            builder.Append(value, repeatCount);
            return this;
        }

        public StringHandler.IStringBuilder Append(int value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(long value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(float value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(double value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(bool value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(ReadOnlySpan<char> value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder AppendLine()
        {
            builder.AppendLine();
            return this;
        }

        public StringHandler.IStringBuilder AppendLine(string value)
        {
            builder.AppendLine(value);
            return this;
        }

        #endregion

        #region Format

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format(string format)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return format;
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T>(string format, T arg)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }

        /// <summary>
        /// 格式化字符串（0GC，ZString.Format）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            return ZString.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
        }

        #endregion

        #region Concat

        /// <summary>
        /// 连接值（0GC，ZString.Concat）
        /// </summary>
        public string Concat<T>(T value)
        {
            return ZString.Concat(value);
        }

        /// <summary>
        /// 连接值（0GC，ZString.Concat）
        /// </summary>
        public string Concat<T1, T2>(T1 value1, T2 value2)
        {
            return ZString.Concat(value1, value2);
        }

        /// <summary>
        /// 连接值（0GC，ZString.Concat）
        /// </summary>
        public string Concat<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            return ZString.Concat(value1, value2, value3);
        }

        /// <summary>
        /// 连接值（0GC，ZString.Concat）
        /// </summary>
        public string Concat<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            return ZString.Concat(value1, value2, value3, value4);
        }

        #endregion

        #region Join

        /// <summary>
        /// 使用分隔符连接（0GC，ZString.Join）
        /// </summary>
        public string Join<T>(string separator, ReadOnlySpan<T> values)
        {
            if (values.IsEmpty) return string.Empty;
            return ZString.Join(separator, values);
        }

        /// <summary>
        /// 使用分隔符连接（0GC，ZString.Join）
        /// </summary>
        public string Join<T>(string separator, T[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;
            return ZString.Join(separator, values);
        }

        #endregion

        public void Dispose()
        {
            if (!disposed)
            {
                builder.Dispose();
                disposed = true;
            }
        }
    }
}
#endif
