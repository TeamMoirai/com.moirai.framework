using System;

namespace Moirai.Atropos
{
    public abstract partial class StringHandler
    {
        /// <summary>
        /// 字符串构建器适配器接口。
        /// 统一 <see cref="System.Text.StringBuilder"/> 和 <see cref="Cysharp.Text.Utf16ValueStringBuilder"/> 的操作。
        /// </summary>
        public interface IStringBuilder : IDisposable
        {
            /// <summary>
            /// 获取当前长度。
            /// </summary>
            int Length { get; }

            /// <summary>
            /// 获取或设置指定位置的字符。
            /// </summary>
            char this[int index] { get; set; }

            /// <summary>
            /// 转换为字符串。
            /// </summary>
            string ToString();

            /// <summary>
            /// 转换为子字符串。
            /// </summary>
            string ToString(int startIndex, int length);

            /// <summary>
            /// 转换为字符串并释放（ToString + Dispose）。
            /// </summary>
            string ToStringAndDispose();

            /// <summary>
            /// 清空内容。
            /// </summary>
            IStringBuilder Clear();

            /// <summary>
            /// 将内容复制到目标字符数组。
            /// </summary>
            void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count);

            #region Append

            /// <summary>
            /// 追加字符串。
            /// </summary>
            IStringBuilder Append(string value);

            /// <summary>
            /// 追加字符。
            /// </summary>
            IStringBuilder Append(char value);

            /// <summary>
            /// 追加字符。
            /// </summary>
            IStringBuilder Append(char value, int repeatCount);

            /// <summary>
            /// 追加 int。
            /// </summary>
            IStringBuilder Append(int value);

            /// <summary>
            /// 追加 long。
            /// </summary>
            IStringBuilder Append(long value);

            /// <summary>
            /// 追加 float。
            /// </summary>
            IStringBuilder Append(float value);

            /// <summary>
            /// 追加 double。
            /// </summary>
            IStringBuilder Append(double value);

            /// <summary>
            /// 追加 bool。
            /// </summary>
            IStringBuilder Append(bool value);

            /// <summary>
            /// 追加 Span 字符串。
            /// </summary>
            IStringBuilder Append(ReadOnlySpan<char> value);

            /// <summary>
            /// 追加字符串。
            /// </summary>
            IStringBuilder Append(string value, int startIndex, int count);
            
            /// <summary>
            /// 追加换行。
            /// </summary>
            IStringBuilder AppendLine();

            /// <summary>
            /// 追加字符串和换行。
            /// </summary>
            IStringBuilder AppendLine(string value);

            /// <summary>
            /// 追加字符数组。
            /// </summary>
            IStringBuilder Append(char[] value);

            /// <summary>
            /// 追加字符数组的子区间。
            /// </summary>
            IStringBuilder Append(char[] value, int startIndex, int charCount);

            /// <summary>
            /// 追加对象。
            /// </summary>
            IStringBuilder Append(object value);

            /// <summary>
            /// 追加 uint。
            /// </summary>
            IStringBuilder Append(uint value);

            /// <summary>
            /// 追加 ulong。
            /// </summary>
            IStringBuilder Append(ulong value);

            /// <summary>
            /// 追加 byte。
            /// </summary>
            IStringBuilder Append(byte value);

            /// <summary>
            /// 追加 short。
            /// </summary>
            IStringBuilder Append(short value);

            /// <summary>
            /// 追加 decimal。
            /// </summary>
            IStringBuilder Append(decimal value);

            /// <summary>
            /// 追加字符和换行。
            /// </summary>
            IStringBuilder AppendLine(char value);

            /// <summary>
            /// 追加 Span 字符串和换行。
            /// </summary>
            IStringBuilder AppendLine(ReadOnlySpan<char> value);

            #endregion

            #region Format

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format(string format);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T>(string format, T arg);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2>(string format, T1 arg1, T2 arg2);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15);

            /// <summary>
            /// 格式化字符串并返回结果。
            /// </summary>
            string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16);

            #endregion

            #region Concat

            /// <summary>
            /// 连接值并返回结果。
            /// </summary>
            string Concat<T>(T value);

            /// <summary>
            /// 连接值并返回结果。
            /// </summary>
            string Concat<T1, T2>(T1 value1, T2 value2);

            /// <summary>
            /// 连接值并返回结果。
            /// </summary>
            string Concat<T1, T2, T3>(T1 value1, T2 value2, T3 value3);

            /// <summary>
            /// 连接值并返回结果。
            /// </summary>
            string Concat<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4);

            #endregion

            #region Join

            /// <summary>
            /// 使用分隔符连接数组元素并返回结果。
            /// </summary>
            string Join<T>(string separator, ReadOnlySpan<T> values);

            /// <summary>
            /// 使用分隔符连接数组元素并返回结果。
            /// </summary>
            string Join<T>(string separator, T[] values);
            #endregion

            #region Insert

            /// <summary>
            /// 在指定位置插入字符串。
            /// </summary>
            IStringBuilder Insert(int index, string value);

            /// <summary>
            /// 在指定位置插入字符。
            /// </summary>
            IStringBuilder Insert(int index, char value);

            /// <summary>
            /// 在指定位置插入字符串多次。
            /// </summary>
            IStringBuilder Insert(int index, string value, int count);

            #endregion

            #region Remove

            /// <summary>
            /// 移除指定范围的字符。
            /// </summary>
            IStringBuilder Remove(int startIndex, int length);

            #endregion

            #region Replace

            /// <summary>
            /// 替换字符。
            /// </summary>
            IStringBuilder Replace(char oldChar, char newChar);

            /// <summary>
            /// 替换字符（指定范围）。
            /// </summary>
            IStringBuilder Replace(char oldChar, char newChar, int startIndex, int count);

            /// <summary>
            /// 替换字符串。
            /// </summary>
            IStringBuilder Replace(string oldValue, string newValue);

            /// <summary>
            /// 替换字符串（指定范围）。
            /// </summary>
            IStringBuilder Replace(string oldValue, string newValue, int startIndex, int count);

            #endregion

        }
    }
}