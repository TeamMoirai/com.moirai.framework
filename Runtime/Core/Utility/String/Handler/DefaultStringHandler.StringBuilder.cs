using System;
using System.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认字符串构建器适配器。<br/>
    /// 包装 <see cref="System.Text.StringBuilder"/>，提供统一的操作接口。
    /// </summary>
    public sealed class DefaultStringBuilder : StringHandler.IStringBuilder
    {
        internal StringBuilder builder;
        internal bool inPool;

        public DefaultStringBuilder(StringBuilder builder)
        {
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public DefaultStringBuilder(int capacity = 256)
        {
            builder = new StringBuilder(capacity);
        }

        public int Length => builder.Length;

        public char this[int index]
        {
            get => builder[index];
            set => builder[index] = value;
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        public string ToString(int startIndex, int length)
        {
            return builder.ToString(startIndex, length);
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
            builder.CopyTo(sourceIndex, destination, destinationIndex, count);
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
            builder.Append(value);
            return this;
        }

        public StringHandler.IStringBuilder Append(char[] value, int startIndex, int charCount)
        {
            builder.Append(value, startIndex, charCount);
            return this;
        }

        public StringHandler.IStringBuilder Append(object value)
        {
            builder.Append(value);
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
            builder.Append(value);
            builder.AppendLine();
            return this;
        }

        public StringHandler.IStringBuilder AppendLine(ReadOnlySpan<char> value)
        {
            builder.Append(value);
            builder.AppendLine();
            return this;
        }

        #endregion

        #region Format

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format(string format)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.Append(format);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T>(string format, T arg)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化字符串并返回结果（0GC）
        /// </summary>
        public string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            builder.Clear();
            builder.AppendFormat(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
            return builder.ToString();
        }

        #endregion

        #region Concat

        /// <summary>
        /// 连接值并返回结果（0GC）
        /// </summary>
        public string Concat<T>(T value)
        {
            builder.Clear();
            builder.Append(value);
            return builder.ToString();
        }

        /// <summary>
        /// 连接值并返回结果（0GC）
        /// </summary>
        public string Concat<T1, T2>(T1 value1, T2 value2)
        {
            builder.Clear();
            builder.Append(value1);
            builder.Append(value2);
            return builder.ToString();
        }

        /// <summary>
        /// 连接值并返回结果（0GC）
        /// </summary>
        public string Concat<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            builder.Clear();
            builder.Append(value1);
            builder.Append(value2);
            builder.Append(value3);
            return builder.ToString();
        }

        /// <summary>
        /// 连接值并返回结果（0GC）
        /// </summary>
        public string Concat<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            builder.Clear();
            builder.Append(value1);
            builder.Append(value2);
            builder.Append(value3);
            builder.Append(value4);
            return builder.ToString();
        }

        #endregion

        #region Join

        /// <summary>
        /// 使用分隔符连接数组元素并返回结果（0GC）
        /// </summary>
        public string Join<T>(string separator, ReadOnlySpan<T> values)
        {
            if (values.IsEmpty) return string.Empty;
            builder.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) builder.Append(separator);
                builder.Append(values[i]);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 使用分隔符连接数组元素并返回结果（0GC）
        /// </summary>
        public string Join<T>(string separator, T[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;
            builder.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) builder.Append(separator);
                builder.Append(values[i]);
            }
            return builder.ToString();
        }

        #endregion

        #region Insert

        public StringHandler.IStringBuilder Insert(int index, string value)
        {
            builder.Insert(index, value);
            return this;
        }

        public StringHandler.IStringBuilder Insert(int index, char value)
        {
            builder.Insert(index, value);
            return this;
        }

        public StringHandler.IStringBuilder Insert(int index, string value, int count)
        {
            builder.Insert(index, value, count);
            return this;
        }

        #endregion

        #region Remove

        public StringHandler.IStringBuilder Remove(int startIndex, int length)
        {
            builder.Remove(startIndex, length);
            return this;
        }

        #endregion

        #region Replace

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
            DefaultStringHandler.Return(this);
        }
    }
}
