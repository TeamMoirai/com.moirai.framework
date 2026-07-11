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
            /// 转换为字符串。
            /// </summary>
            string ToString();

            /// <summary>
            /// 转换为字符串并释放（ToString + Dispose）。
            /// </summary>
            string ToStringAndDispose();

            /// <summary>
            /// 清空内容。
            /// </summary>
            IStringBuilder Clear();

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
            /// 追加换行。
            /// </summary>
            IStringBuilder AppendLine();

            /// <summary>
            /// 追加字符串和换行。
            /// </summary>
            IStringBuilder AppendLine(string value);

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
        }
    }
}