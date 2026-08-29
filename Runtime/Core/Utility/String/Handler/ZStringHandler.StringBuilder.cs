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
        internal bool inPool;

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

        public char this[int index]
        {
            get => builder.AsSpan()[index];
            set => builder.ReplaceAt(value, index);
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        public string ToString(int startIndex, int length)
        {
            return builder.AsSpan().Slice(startIndex, length).ToString();
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

        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            builder.AsSpan().Slice(sourceIndex, count).CopyTo(destination.AsSpan(destinationIndex));
        }

        #region 追加 [APPEND]

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

        public StringHandler.IStringBuilder Append(string value, int startIndex, int count)
        {
            builder.Append(value, startIndex, count);
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

        public StringHandler.IStringBuilder Append(char[] value)
        {
            if (value != null) builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(char[] value, int startIndex, int charCount)
        {
            builder.Append(value, startIndex, charCount);
            return this;
        }

        public StringHandler.IStringBuilder Append(object value)
        {
            if (value != null) builder.Append(value.ToString());
            return this;
        }

        public StringHandler.IStringBuilder Append(uint value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(ulong value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(byte value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(short value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(decimal value)
        {
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder AppendLine(char value)
        {
            builder.AppendLine(value);
            return this;
        }

        public StringHandler.IStringBuilder AppendLine(ReadOnlySpan<char> value)
        {
            builder.AppendLine(value);
            return this;
        }

        #endregion

        #region 格式化 [FORMAT]

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

        #region 拼接 [CONCAT]

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

        #region 连接 [JOIN]

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

        #region 插入 [INSERT]

        public StringHandler.IStringBuilder Insert(int index, string value)
        {
            builder.Insert(index, value);
            return this;
        }

        public StringHandler.IStringBuilder Insert(int index, char value)
        {
            // 零分配：走 ReadOnlySpan<char> 重载，避免 value.ToString() 分配
            Span<char> buffer = stackalloc char[1];
            buffer[0] = value;
            builder.Insert(index, (ReadOnlySpan<char>)buffer, 1);
            return this;
        }

        public StringHandler.IStringBuilder Insert(int index, string value, int count)
        {
            builder.Insert(index, value, count);
            return this;
        }

        #endregion

        #region 移除 [REMOVE]

        public StringHandler.IStringBuilder Remove(int startIndex, int length)
        {
            builder.Remove(startIndex, length);
            return this;
        }

        #endregion

        #region 替换 [REPLACE]

        public StringHandler.IStringBuilder Replace(char oldChar, char newChar)
        {
            builder.Replace(oldChar, newChar);
            return this;
        }

        public StringHandler.IStringBuilder Replace(char oldChar, char newChar, int startIndex, int count)
        {
            builder.Replace(oldChar, newChar, startIndex, count);
            return this;
        }

        public StringHandler.IStringBuilder Replace(string oldValue, string newValue)
        {
            builder.Replace(oldValue, newValue);
            return this;
        }

        public StringHandler.IStringBuilder Replace(string oldValue, string newValue, int startIndex, int count)
        {
            builder.Replace(oldValue, newValue, startIndex, count);
            return this;
        }

        #endregion

        public void Dispose()
        {
            if (inPool) return; // 防止重复归还
            ZStringHandler.Return(this);
        }
    }
}
#endif
