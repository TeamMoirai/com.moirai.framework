using System;
using System.Globalization;
using System.Text;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        // ByteReader 的数值解析与类型转换部分（partial 拆分自 DefaultJson.ByteReader.cs）：
        // 手工整数解析（含溢出预判）、浮点栈缓冲转换、类型化 token 读取、字符串→类型转换。
        internal sealed partial class ByteReader
        {
            #region 类型转换 [CONVERSIONS]
            /// <summary>字符串 → 目标类型。非数值类型委托 <see cref="TypeConverter"/>；数值类型走字节 span 解析。</summary>
            private object ConvertFromString(string s, Type type)
            {
                return TypeConverter.TryConvertFromString(s, type, out object result)
                    ? result
                    : ParseQuotedNumberBytes(type, s);
            }

            private object ConvertDictionaryKey(string s, Type keyType)
            {
                return TypeConverter.TryConvertDictionaryKey(s, keyType, out object result)
                    ? result
                    : ParseQuotedNumberBytes(keyType, s);
            }

            /// <summary>
            /// 带引号数值（历史格式）：string → 字节 span 解析。
            /// 数值串必为 ASCII：≤64 字符经栈缓冲拷贝零堆分配（避免 Encoding.UTF8.GetBytes）；超长/非 ASCII 回退。
            /// </summary>
            private object ParseQuotedNumberBytes(Type type, string s)
            {
                if (s.Length > 0 && s.Length <= NUMBER_CHAR_BUFFER)
                {
                    Span<byte> buffer = stackalloc byte[NUMBER_CHAR_BUFFER];
                    int i = 0;
                    while (i < s.Length)
                    {
                        char ch = s[i];
                        if (ch > 0x7F) break; // 数值 token 必为 ASCII
                        buffer[i] = (byte)ch;
                        i++;
                    }

                    if (i == s.Length)
                    {
                        return ParseNumberSpanBytes(type, buffer.Slice(0, s.Length));
                    }
                }

                return ParseNumberSpanBytes(type, Encoding.UTF8.GetBytes(s));
            }

            #endregion

            #region 数值 [NUMBERS]
            private object ParseNumber(Type type)
            {
                return ParseNumberSpanBytes(type, ScanNumberToken());
            }

            /// <summary>扫描数值 token（支持历史带引号形式）。</summary>
            private ReadOnlySpan<byte> ScanNumberToken()
            {
                SkipWhitespace();

                bool quoted = false;
                if (Peek() == (byte)'"')
                {
                    quoted = true;
                    _pos++;
                }

                int start = _pos;
                while (_pos < _json.Length)
                {
                    byte c = _json[_pos];
                    if ((c >= (byte)'0' && c <= (byte)'9') || c == (byte)'-' || c == (byte)'+' ||
                        c == (byte)'.' || c == (byte)'e' || c == (byte)'E')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (quoted)
                {
                    if (_pos >= _json.Length || _json[_pos] != (byte)'"')
                    {
                        Throw("Unterminated quoted number.");
                    }

                    _pos++;
                }

                return new ReadOnlySpan<byte>(_json, start, _pos - start - (quoted ? 1 : 0));
            }

            private object ParseNumberSpanBytes(Type type, ReadOnlySpan<byte> s)
            {
                if (s.IsEmpty)
                {
                    Throw("Empty numeric token.");
                }

                if (type.IsEnum)
                {
                    if (TryParseInt64Bytes(s, out long enumRaw))
                    {
                        return Enum.ToObject(type, enumRaw);
                    }

                    if (TryParseUInt64Bytes(s, out ulong enumRawU))
                    {
                        return Enum.ToObject(type, enumRawU);
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid numeric value for enum '{1}'.", EncodeSpan(s), type.Name));
                }

                bool isFloatTarget = type == typeof(float) || type == typeof(double) || type == typeof(decimal);
                if (!isFloatTarget && type != typeof(bool) && type != typeof(string))
                {
                    object direct = TryParseIntegralBytes(type, s);
                    if (direct != null) return direct;

                    if (TryParseDoubleBytes(s, out double integral) &&
                        !double.IsNaN(integral) && !double.IsInfinity(integral) && integral == Math.Floor(integral))
                    {
                        try
                        {
                            return Convert.ChangeType(integral, type, CultureInfo.InvariantCulture);
                        }
                        catch (Exception)
                        {
                            Throw(StringUtility.Format("'{0}' is out of range for '{1}'.", EncodeSpan(s), type.Name));
                        }
                    }

                    if (type == typeof(char))
                    {
                        Throw(StringUtility.Format("'{0}' is not a valid char value.", EncodeSpan(s)));
                    }

                    Throw(StringUtility.Format("'{0}' is not a valid integer for '{1}'.", EncodeSpan(s), type.Name));
                }

                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Single:
                        if (TryParseSingleBytes(s, out float f)) return f;
                        break;
                    case TypeCode.Double:
                        if (TryParseDoubleBytes(s, out double d)) return d;
                        break;
                    case TypeCode.Decimal:
                        if (TryParseDecimalBytes(s, out decimal m)) return m;
                        break;
                    case TypeCode.Boolean:
                        if (TryParseDoubleBytes(s, out double b)) return Math.Abs(b) > 0d;
                        break;
                    case TypeCode.String:
                        return EncodeSpan(s);
                    default:
                        if (type == typeof(string) || type == typeof(object)) return EncodeSpan(s);
                        break;
                }

                Throw(StringUtility.Format("'{0}' is not a valid value for '{1}'.", EncodeSpan(s), type.Name));
                return null;
            }

            private static object TryParseIntegralBytes(Type type, ReadOnlySpan<byte> s)
            {
                // 解析到 long/ulong 后按目标范围收窄；越界返回 null 交由 double 回退路径抛出准确错误
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte:
                        if (TryParseInt64Bytes(s, out long sb) && sb >= sbyte.MinValue && sb <= sbyte.MaxValue) return (sbyte)sb;
                        return null;
                    case TypeCode.Byte:
                        if (TryParseUInt64Bytes(s, out ulong b) && b <= byte.MaxValue) return (byte)b;
                        return null;
                    case TypeCode.Int16:
                        if (TryParseInt64Bytes(s, out long sh) && sh >= short.MinValue && sh <= short.MaxValue) return (short)sh;
                        return null;
                    case TypeCode.UInt16:
                        if (TryParseUInt64Bytes(s, out ulong ush) && ush <= ushort.MaxValue) return (ushort)ush;
                        return null;
                    case TypeCode.Int32:
                        if (TryParseInt64Bytes(s, out long i) && i >= int.MinValue && i <= int.MaxValue) return (int)i;
                        return null;
                    case TypeCode.UInt32:
                        if (TryParseUInt64Bytes(s, out ulong ui) && ui <= uint.MaxValue) return (uint)ui;
                        return null;
                    case TypeCode.Int64:
                        return TryParseInt64Bytes(s, out long l) ? (object)l : null;
                    case TypeCode.UInt64:
                        return TryParseUInt64Bytes(s, out ulong ul) ? (object)ul : null;
                    default:
                        return null;
                }
            }

            // ===== 手工整数解析（ASCII 数字循环，零分配） =====

            private static bool TryParseInt64Bytes(ReadOnlySpan<byte> s, out long value)
            {
                value = 0;
                int i = 0;
                bool negative = false;
                if (i < s.Length && s[i] == (byte)'-')
                {
                    negative = true;
                    i++;
                }

                if (i >= s.Length) return false;

                // long 范围上限（负数侧允许到 9223372036854775808 = long.MinValue 绝对值）
                ulong limit = negative ? 9223372036854775808UL : 9223372036854775807UL;

                ulong acc = 0;
                while (i < s.Length)
                {
                    byte c = s[i];
                    if (c < (byte)'0' || c > (byte)'9') return false;

                    ulong digit = (uint)(c - '0');
                    // 溢出预判必须在乘法之前：先乘后查会因无符号回绕绕过检查（如 20 位数字回绕后落回范围内）
                    if (acc > (ulong.MaxValue - digit) / 10) return false;

                    acc = acc * 10 + digit;
                    if (acc > limit) return false;
                    i++;
                }

                // 负数侧 acc 可达 9223372036854775808UL（long.MinValue 绝对值）——超出 long.MaxValue，
                // 转换需显式 unchecked（0 - acc 回绕即为 long.MinValue，与取负语义一致）
                value = negative ? unchecked((long)(0 - acc)) : (long)acc;
                return true;
            }

            private static bool TryParseUInt64Bytes(ReadOnlySpan<byte> s, out ulong value)
            {
                value = 0;
                int i = 0;
                if (i < s.Length && s[i] == (byte)'-') return false;

                if (i >= s.Length) return false;

                ulong acc = 0;
                while (i < s.Length)
                {
                    byte c = s[i];
                    if (c < (byte)'0' || c > (byte)'9') return false;

                    ulong digit = (uint)(c - '0');
                    if (acc > (ulong.MaxValue - digit) / 10) return false; // 溢出

                    acc = acc * 10 + digit;
                    i++;
                }

                value = acc;
                return true;
            }

            // ===== 浮点解析（ASCII token → 调用方栈上 char 缓冲 → TryParse，零堆分配） =====

            private const int NUMBER_CHAR_BUFFER = 64;
            private const int NUMBER_NOT_ASCII = -1;

            private static bool TryParseSingleBytes(ReadOnlySpan<byte> s, out float value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? float.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : float.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static bool TryParseDoubleBytes(ReadOnlySpan<byte> s, out double value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? double.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : double.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static bool TryParseDecimalBytes(ReadOnlySpan<byte> s, out decimal value)
            {
                Span<char> buffer = stackalloc char[NUMBER_CHAR_BUFFER];
                int len = PrepareNumberChars(s, buffer);
                return len >= 0
                    ? decimal.TryParse(buffer.Slice(0, len), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    : decimal.TryParse(EncodeSpan(s), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            /// <summary>
            /// 将 ASCII 数值 token 复制进调用方栈缓冲，返回复制的字符数。
            /// 超长或含非 ASCII 返回 <see cref="NUMBER_NOT_ASCII"/>（调用方回退字符串路径）。
            /// </summary>
            private static int PrepareNumberChars(ReadOnlySpan<byte> s, Span<char> buffer)
            {
                if (s.Length == 0 || s.Length > buffer.Length) return NUMBER_NOT_ASCII;

                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] > 0x7F) return NUMBER_NOT_ASCII; // 数值 token 必为 ASCII
                    buffer[i] = (char)s[i];
                }

                return s.Length;
            }

            private static string EncodeSpan(ReadOnlySpan<byte> s)
            {
                return Encoding.UTF8.GetString(s.ToArray());
            }

            // ===== 类型化 token 读取（供基元数组零装箱路径） =====

            private bool ReadBooleanToken()
            {
                SkipWhitespace();
                if (MatchLiteral("true")) return true;
                if (MatchLiteral("false")) return false;

                Throw("Invalid boolean literal in array.");
                return false;
            }

            private sbyte ReadIntegralSByte()
            {
                long v = ReadInt64Token();
                if (v < sbyte.MinValue || v > sbyte.MaxValue) ThrowIntegralRange("sbyte");
                return (sbyte)v;
            }

            private byte ReadIntegralByte()
            {
                ulong v = ReadUInt64Token();
                if (v > byte.MaxValue) ThrowIntegralRange("byte");
                return (byte)v;
            }

            private short ReadIntegralInt16()
            {
                long v = ReadInt64Token();
                if (v < short.MinValue || v > short.MaxValue) ThrowIntegralRange("short");
                return (short)v;
            }

            private ushort ReadIntegralUInt16()
            {
                ulong v = ReadUInt64Token();
                if (v > ushort.MaxValue) ThrowIntegralRange("ushort");
                return (ushort)v;
            }

            private int ReadIntegralInt32()
            {
                long v = ReadInt64Token();
                if (v < int.MinValue || v > int.MaxValue) ThrowIntegralRange("int");
                return (int)v;
            }

            private uint ReadIntegralUInt32()
            {
                ulong v = ReadUInt64Token();
                if (v > uint.MaxValue) ThrowIntegralRange("uint");
                return (uint)v;
            }

            /// <summary>long token：直取；科学计数法/溢出回退 double 且必须整值（兼容 "1e2" 类输入）。</summary>
            private long ReadInt64Token()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseInt64Bytes(s, out long v)) return v;

                if (TryParseDoubleBytes(s, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) &&
                    d >= long.MinValue && d <= long.MaxValue)
                {
                    return (long)d;
                }

                Throw(StringUtility.Format("'{0}' is not a valid integer for 'long'.", EncodeSpan(s)));
                return 0;
            }

            private ulong ReadUInt64Token()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseUInt64Bytes(s, out ulong v)) return v;

                if (TryParseDoubleBytes(s, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) &&
                    d >= 0 && d <= ulong.MaxValue)
                {
                    return (ulong)d;
                }

                Throw(StringUtility.Format("'{0}' is not a valid integer for 'ulong'.", EncodeSpan(s)));
                return 0;
            }

            private float ReadSingleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseSingleBytes(s, out float f)) return f;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'float'.", EncodeSpan(s)));
                return 0;
            }

            private double ReadDoubleToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseDoubleBytes(s, out double d)) return d;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'double'.", EncodeSpan(s)));
                return 0;
            }

            private decimal ReadDecimalToken()
            {
                ReadOnlySpan<byte> s = ScanNumberToken();
                if (TryParseDecimalBytes(s, out decimal m)) return m;

                Throw(StringUtility.Format("'{0}' is not a valid value for 'decimal'.", EncodeSpan(s)));
                return 0;
            }

            private void ThrowIntegralRange(string typeName)
            {
                Throw(StringUtility.Format("Numeric value is out of range for '{0}'.", typeName));
            }

            #endregion
        }
    }
}
