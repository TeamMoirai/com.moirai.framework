using System;
using System.Globalization;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        #region 写入原语 [WRITE SINKS]

        /// <summary>
        /// 写入原语接口：值的编码差异（char / UTF8 字节、转义、数字格式化、缩进）由各 Sink 实现。
        /// 结构逻辑（分派/容器/守卫/反射成员遍历）统一在 <see cref="JsonWriter{TSink}"/> 中单一实现。
        /// </summary>
        /// <remarks>
        /// <para><b>调用粒度契约</b>：接口按"每值"分发（每个字段/元素一次调用），而非每字符——
        /// 接口开销被原语内部的工作量摊薄；Sink 为 struct 经 ref 传递，无装箱。</para>
        /// <para><b>WriteAscii 契约</b>：仅接收保证 ASCII 的内容（结构片段 / InvariantCulture 数值串）；
        /// 实现保留防御性 UTF8 回退以正确处理意外输入。</para>
        /// </remarks>
        internal interface IJsonSink
        {
            /// <summary>预留至少 count 字符/字节的容量（CharSink 为 no-op）。</summary>
            void Reserve(int count);

            /// <summary>写入 ASCII 结构字符（{ } [ ] , : 等）。</summary>
            void WriteAscii(char c);

            /// <summary>写入 ASCII 原始串（结构片段，如 "{\"key\":"）。</summary>
            void WriteAscii(string s);

            /// <summary>写入带引号的转义字符串（含代理对处理）。</summary>
            void WriteEscaped(string s);

            /// <summary>写入带引号的转义单字符。</summary>
            void WriteEscaped(char c);

            /// <summary>写入有符号整数（各 Sink 用最优格式化：栈缓冲数字 / 直写字节）。</summary>
            void WriteInt64(long v);

            /// <summary>写入无符号整数。</summary>
            void WriteUInt64(ulong v);

            /// <summary>写入换行 + level 个制表符（readable 模式缩进；字节路径语义相同）。</summary>
            void WriteIndent(int level);
        }

        /// <summary>
        /// 字符串 Sink（基于池化 <see cref="StringHandler.IStringBuilder"/>）。
        /// </summary>
        internal struct CharSink : IJsonSink
        {
            private readonly StringHandler.IStringBuilder _sb;

            public CharSink(StringHandler.IStringBuilder sb)
            {
                _sb = sb;
            }

            public void Reserve(int count)
            {
            }

            public void WriteAscii(char c)
            {
                _sb.Append(c);
            }

            public void WriteAscii(string s)
            {
                _sb.Append(s);
            }

            public void WriteIndent(int level)
            {
                _sb.Append("\r\n");
                if (level > 0) _sb.Append('\t', level);
            }

            public void WriteInt64(long v)
            {
                Span<char> buffer = stackalloc char[21]; // '-' + 20 位数字
                int pos = 0;

                ulong digits;
                if (v < 0)
                {
                    buffer[pos++] = '-';
                    digits = v == long.MinValue ? unchecked((ulong)(-(v + 1)) + 1UL) : (ulong)(-v);
                }
                else
                {
                    digits = (ulong)v;
                }

                pos = FormatDigits(buffer, pos, digits);
                _sb.Append((ReadOnlySpan<char>)buffer.Slice(0, pos));
            }

            public void WriteUInt64(ulong v)
            {
                Span<char> buffer = stackalloc char[20];
                int pos = FormatDigits(buffer, 0, v);
                _sb.Append((ReadOnlySpan<char>)buffer.Slice(0, pos));
            }

            public void WriteEscaped(string s)
            {
                // 无转义快路径（实测最快）：整体一次 Append，免段循环逐字符簿记
                if (!NeedsEscape(s))
                {
                    _sb.Append('"').Append(s).Append('"');
                    return;
                }

                _sb.Append('"');

                // 批量段复制：扫描到下一个转义点，将两转义点之间的整段一次 Append（接口调用从每字符降为每段；
                // 实测对稀疏转义长文本 -40%~-71%，仅密集转义（段长≤3）时劣于逐字符）
                int i = 0;
                int segStart = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '\\' || c == '"' || c < ' ')
                    {
                        if (i > segStart) _sb.Append(s, segStart, i - segStart);

                        switch (c)
                        {
                            case '\\': _sb.Append("\\\\"); break;
                            case '"': _sb.Append("\\\""); break;
                            case '\b': _sb.Append("\\b"); break;
                            case '\f': _sb.Append("\\f"); break;
                            case '\n': _sb.Append("\\n"); break;
                            case '\r': _sb.Append("\\r"); break;
                            default: _sb.Append("\\u").Append(((int)c).ToString("X4")); break;
                        }

                        i++;
                        segStart = i;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (segStart < s.Length) _sb.Append(s, segStart, s.Length - segStart);

                _sb.Append('"');
            }

            public void WriteEscaped(char c)
            {
                _sb.Append('"');
                switch (c)
                {
                    case '\\': _sb.Append("\\\\"); break;
                    case '"': _sb.Append("\\\""); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            _sb.Append("\\u").Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            _sb.Append(c);
                        }

                        break;
                }

                _sb.Append('"');
            }

            /// <summary>数字低位在前写入后原地反转。返回写入后的长度。</summary>
            private static int FormatDigits(Span<char> buffer, int pos, ulong v)
            {
                if (v == 0)
                {
                    buffer[pos++] = '0';
                    return pos;
                }

                int digitStart = pos;
                while (v >= 10)
                {
                    buffer[pos++] = (char)('0' + (v % 10));
                    v /= 10;
                }

                buffer[pos++] = (char)('0' + v);

                int left = digitStart, right = pos - 1;
                while (left < right)
                {
                    char tmp = buffer[left];
                    buffer[left] = buffer[right];
                    buffer[right] = tmp;
                    left++;
                    right--;
                }

                return pos;
            }

            /// <summary>是否含需转义字符（引号/反斜杠/控制字符）。</summary>
            private static bool NeedsEscape(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"' || c == '\\' || c < ' ') return true;
                }

                return false;
            }
        }

        /// <summary>
        /// UTF8 字节 Sink：直接向字节数组写 UTF8（手动编码器，含代理对 → 4 字节、无效代理 → U+FFFD）。
        /// </summary>
        /// <remarks>Buffer 为引用类型字段，经 <c>ref</c> 传递的 Sink 扩容后对调用方可见。</remarks>
        internal struct Utf8Sink : IJsonSink
        {
            public byte[] Buffer;
            public int Position;

            public Utf8Sink(byte[] buffer)
            {
                Buffer = buffer;
                Position = 0;
            }

            public void Reserve(int count)
            {
                int required = Position + count;
                if (required <= Buffer.Length) return;

                int newSize = Buffer.Length * 2;
                while (newSize < required) newSize *= 2;

                var grown = new byte[newSize];
                Array.Copy(Buffer, grown, Position);
                Buffer = grown;
            }

            public void WriteAscii(char c)
            {
                Reserve(1);
                Buffer[Position++] = (byte)c;
            }

            public void WriteAscii(string s)
            {
                if (string.IsNullOrEmpty(s)) return;

                Reserve(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch < 0x80)
                    {
                        // Reserve 已覆盖 s.Length 字节，ASCII 快速路径无需逐字符 Reserve
                        Buffer[Position++] = (byte)ch;
                    }
                    else
                    {
                        WriteUtf8Rune(ch);
                    }
                }
            }

            public void WriteIndent(int level)
            {
                Reserve(2 + level);
                Buffer[Position++] = 0x0D;
                Buffer[Position++] = 0x0A;
                for (int i = 0; i < level; i++) Buffer[Position++] = 0x09;
            }

            public void WriteInt64(long v)
            {
                Reserve(21); // '-' + 20 位数字
                if (v < 0)
                {
                    Buffer[Position++] = (byte)'-';
                    // long.MinValue 取负溢出，经 unchecked 转 ulong 绝对值处理
                    WriteDigits(v == long.MinValue ? unchecked((ulong)(-(v + 1)) + 1UL) : (ulong)(-v));
                    return;
                }

                WriteDigits((ulong)v);
            }

            public void WriteUInt64(ulong v)
            {
                Reserve(20);
                WriteDigits(v);
            }

            public void WriteEscaped(string s)
            {
                // 最坏情况：每个字符转义为 6 字节（\uXXXX）+ 两侧引号；一次 Reserve 后循环内直接写
                Reserve(s.Length * 6 + 2);
                Buffer[Position++] = (byte)'"';

                // 单遍逐字符：段复制结构在字节路径实测反而回退 8-43%（段内仍需逐字符 UTF8 编码，
                // 且外层探测 + 段写入把无转义字符扫两遍）——段复制仅对 StringBuilder 批量 Append 有意义
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '\\':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'\\';
                            break;
                        case '"':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'"';
                            break;
                        case '\b':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'b';
                            break;
                        case '\f':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'f';
                            break;
                        case '\n':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'n';
                            break;
                        case '\r':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'r';
                            break;
                        case '\t':
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'t';
                            break;
                        default:
                            if (c < ' ')
                            {
                                Buffer[Position++] = (byte)'\\';
                                Buffer[Position++] = (byte)'u';
                                WriteHex4(c);
                            }
                            else if (c < 0x80)
                            {
                                Buffer[Position++] = (byte)c;
                            }
                            else if (char.IsHighSurrogate(c))
                            {
                                // 代理对：与低位代理合并为码点编码；孤立代理替换为 U+FFFD
                                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                                {
                                    WriteUtf8Rune(0x10000u + ((uint)(c - 0xD800) << 10) + (uint)(s[i + 1] - 0xDC00));
                                    i++;
                                }
                                else
                                {
                                    WriteUtf8Rune(0xFFFD);
                                }
                            }
                            else if (char.IsLowSurrogate(c))
                            {
                                WriteUtf8Rune(0xFFFD);
                            }
                            else
                            {
                                WriteUtf8Rune(c);
                            }

                            break;
                    }
                }

                Buffer[Position++] = (byte)'"';
            }

            public void WriteEscaped(char c)
            {
                Reserve(8);
                Buffer[Position++] = (byte)'"';
                switch (c)
                {
                    case '\\':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'\\';
                        break;
                    case '"':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'"';
                        break;
                    case '\b':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'b';
                        break;
                    case '\f':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'f';
                        break;
                    case '\n':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'n';
                        break;
                    case '\r':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'r';
                        break;
                    case '\t':
                        Buffer[Position++] = (byte)'\\';
                        Buffer[Position++] = (byte)'t';
                        break;
                    default:
                        if (c < ' ')
                        {
                            Buffer[Position++] = (byte)'\\';
                            Buffer[Position++] = (byte)'u';
                            WriteHex4(c);
                        }
                        else if (char.IsSurrogate(c))
                        {
                            WriteUtf8Rune(0xFFFD);
                        }
                        else
                        {
                            WriteUtf8Rune(c);
                        }

                        break;
                }

                Buffer[Position++] = (byte)'"';
            }

            /// <summary>数字低位在前写入后原地反转。调用方须已 Reserve(20)。</summary>
            private void WriteDigits(ulong v)
            {
                if (v == 0)
                {
                    Buffer[Position++] = (byte)'0';
                    return;
                }

                int digitStart = Position;
                while (v >= 10)
                {
                    Buffer[Position++] = (byte)('0' + (v % 10));
                    v /= 10;
                }

                Buffer[Position++] = (byte)('0' + v);

                int left = digitStart, right = Position - 1;
                while (left < right)
                {
                    byte tmp = Buffer[left];
                    Buffer[left] = Buffer[right];
                    Buffer[right] = tmp;
                    left++;
                    right--;
                }
            }

            /// <summary>写入 4 位大写十六进制（\uXXXX 转义用）。调用方须已 Reserve。</summary>
            private void WriteHex4(char c)
            {
                uint v = c;
                for (int shift = 12; shift >= 0; shift -= 4)
                {
                    uint nibble = (v >> shift) & 0xF;
                    Buffer[Position++] = (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
                }
            }

            /// <summary>将码点（≤0x10FFFF）编码为 UTF8（1-4 字节）。</summary>
            private void WriteUtf8Rune(uint rune)
            {
                if (rune < 0x80)
                {
                    Reserve(1);
                    Buffer[Position++] = (byte)rune;
                }
                else if (rune < 0x800)
                {
                    Reserve(2);
                    Buffer[Position++] = (byte)(0xC0 | (rune >> 6));
                    Buffer[Position++] = (byte)(0x80 | (rune & 0x3F));
                }
                else if (rune < 0x10000)
                {
                    Reserve(3);
                    Buffer[Position++] = (byte)(0xE0 | (rune >> 12));
                    Buffer[Position++] = (byte)(0x80 | ((rune >> 6) & 0x3F));
                    Buffer[Position++] = (byte)(0x80 | (rune & 0x3F));
                }
                else
                {
                    Reserve(4);
                    Buffer[Position++] = (byte)(0xF0 | (rune >> 18));
                    Buffer[Position++] = (byte)(0x80 | ((rune >> 12) & 0x3F));
                    Buffer[Position++] = (byte)(0x80 | ((rune >> 6) & 0x3F));
                    Buffer[Position++] = (byte)(0x80 | (rune & 0x3F));
                }
            }
        }

        /// <summary>UTF8 字节路径的线程本地 scratch 缓冲（含 1MB 保留上限）。</summary>
        internal static class ByteScratch
        {
            private const int INITIAL_CAPACITY = 256;

            /// <summary>线程缓冲保留上限：超限的扩容缓冲不归还（防长线程常驻大 buffer），下次调用重新生长。</summary>
            private const int MAX_RETAINED = 1 << 20; // 1MB

            [ThreadStatic]
            private static byte[] t_Buffer;

            /// <summary>取出（或新建）scratch 缓冲。写入完成后调用 <see cref="Return"/> 归还。</summary>
            public static byte[] Rent()
            {
                return t_Buffer ??= new byte[INITIAL_CAPACITY];
            }

            /// <summary>归还缓冲（超上限丢弃，下次从初始容量重新生长）。</summary>
            public static void Return(byte[] buffer)
            {
                t_Buffer = buffer.Length <= MAX_RETAINED ? buffer : null;
            }
        }

        #endregion
    }
}
