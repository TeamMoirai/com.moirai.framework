using System;
using System.Buffers.Binary;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 键值捕获写入器（KVT 格式，ref struct）。
    /// <para>记录布局：对象级 <c>[2B 键长][键 UTF8][1B 类型][4B 载荷长][载荷]</c>；集合元素级 <c>[1B 类型][4B 载荷长][载荷]</c>。
    /// 每个节点都带显式载荷长度——读取侧可 O(1) 跳过未知键（字段废弃向后兼容的关键）。</para>
    /// <para>嵌套对象/序列/映射经 Begin/End 对写入（End 回填载荷长度）；由 SaveHost SourceGenerator 生成的捕获器驱动；
    /// 须在主线程调用（读取 MonoBehaviour 字段）。</para>
    /// </summary>
    public ref struct SaveKeyValueWriter
    {
        /// <summary>UTF-8 编码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        private byte[] _buffer;
        private int _position;

        /// <summary>嵌套帧栈（Begin 记录载荷长度占位偏移，End 回填）。</summary>
        private NestingFrame[] _nestingStack;
        private int _nestingDepth;

        /// <summary>
        /// 创建写入器（初始容量不足时自动倍增）。
        /// </summary>
        /// <param name="initialCapacity">初始缓冲区容量（字节）。</param>
        public SaveKeyValueWriter(int initialCapacity)
        {
            if (initialCapacity < 16)
            {
                initialCapacity = 16;
            }

            _buffer = new byte[initialCapacity];
            _position = 0;
            _nestingStack = null;
            _nestingDepth = 0;
        }

        /// <summary>
        /// 已写入的字节数。
        /// </summary>
        public int Length => _position;

        /// <summary>
        /// 导出全部写入内容（拷贝为独立数组，作为块载荷）。
        /// </summary>
        /// <returns>写入内容数组。</returns>
        public byte[] ToArray()
        {
            if (_nestingDepth != 0)
            {
                throw new InvalidOperationException("KVT nested scope is not closed.");
            }

            byte[] result = new byte[_position];
            Buffer.BlockCopy(_buffer, 0, result, 0, _position);
            return result;
        }

        #region 基元字段 [PRIMITIVE FIELDS]

        /// <summary>
        /// 写入布尔字段。
        /// </summary>
        public void WriteBoolean(string key, bool value)
        {
            BeginRecord(key, ESaveKvType.Bool, 1);
            _buffer[_position++] = value ? (byte)1 : (byte)0;
        }

        /// <summary>
        /// 写入有符号字节字段。
        /// </summary>
        public void WriteSByte(string key, sbyte value)
        {
            BeginRecord(key, ESaveKvType.SByte, 1);
            _buffer[_position++] = unchecked((byte)value);
        }

        /// <summary>
        /// 写入无符号字节字段。
        /// </summary>
        public void WriteByte(string key, byte value)
        {
            BeginRecord(key, ESaveKvType.Byte, 1);
            _buffer[_position++] = value;
        }

        /// <summary>
        /// 写入有符号 16 位字段。
        /// </summary>
        public void WriteInt16(string key, short value)
        {
            BeginRecord(key, ESaveKvType.Int16, 2);
            BinaryPrimitives.WriteInt16LittleEndian(Advance(2), value);
        }

        /// <summary>
        /// 写入无符号 16 位字段。
        /// </summary>
        public void WriteUInt16(string key, ushort value)
        {
            BeginRecord(key, ESaveKvType.UInt16, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(Advance(2), value);
        }

        /// <summary>
        /// 写入有符号 32 位字段。
        /// </summary>
        public void WriteInt32(string key, int value)
        {
            BeginRecord(key, ESaveKvType.Int32, 4);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), value);
        }

        /// <summary>
        /// 写入无符号 32 位字段。
        /// </summary>
        public void WriteUInt32(string key, uint value)
        {
            BeginRecord(key, ESaveKvType.UInt32, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(Advance(4), value);
        }

        /// <summary>
        /// 写入有符号 64 位字段。
        /// </summary>
        public void WriteInt64(string key, long value)
        {
            BeginRecord(key, ESaveKvType.Int64, 8);
            BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value);
        }

        /// <summary>
        /// 写入无符号 64 位字段。
        /// </summary>
        public void WriteUInt64(string key, ulong value)
        {
            BeginRecord(key, ESaveKvType.UInt64, 8);
            BinaryPrimitives.WriteUInt64LittleEndian(Advance(8), value);
        }

        /// <summary>
        /// 写入单精度浮点字段。
        /// </summary>
        public void WriteSingle(string key, float value)
        {
            BeginRecord(key, ESaveKvType.Single, 4);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>
        /// 写入双精度浮点字段。
        /// </summary>
        public void WriteDouble(string key, double value)
        {
            BeginRecord(key, ESaveKvType.Double, 8);
            BinaryPrimitives.WriteInt64LittleEndian(Advance(8), BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>
        /// 写入十进制字段。
        /// </summary>
        public void WriteDecimal(string key, decimal value)
        {
            BeginRecord(key, ESaveKvType.Decimal, 16);
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value).CopyTo(bits);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), bits[0]);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), bits[1]);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), bits[2]);
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), bits[3]);
        }

        /// <summary>
        /// 写入字符字段。
        /// </summary>
        public void WriteChar(string key, char value)
        {
            BeginRecord(key, ESaveKvType.Char, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(Advance(2), value);
        }

        /// <summary>
        /// 写入字符串字段（null 写为 Null 记录）。
        /// </summary>
        public void WriteString(string key, string value)
        {
            if (value == null)
            {
                BeginRecord(key, ESaveKvType.Null, 0);
                return;
            }

            int byteCount = s_Utf8.GetByteCount(value);
            BeginRecord(key, ESaveKvType.String, byteCount);
            s_Utf8.GetBytes(value, Advance(byteCount));
        }

        /// <summary>
        /// 写入日期时间字段。
        /// </summary>
        public void WriteDateTime(string key, DateTime value)
        {
            BeginRecord(key, ESaveKvType.DateTime, 9);
            BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value.Ticks);
            _buffer[_position++] = (byte)value.Kind;
        }

        /// <summary>
        /// 写入时间跨度字段。
        /// </summary>
        public void WriteTimeSpan(string key, TimeSpan value)
        {
            BeginRecord(key, ESaveKvType.TimeSpan, 8);
            BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value.Ticks);
        }

        #endregion

        #region Unity 数学字段 [UNITY MATH FIELDS]

        /// <summary>
        /// 写入二维向量字段。
        /// </summary>
        public void WriteVector2(string key, Vector2 value)
        {
            BeginRecord(key, ESaveKvType.Vector2, 8);
            Span<byte> span = Advance(8);
            WriteFloat(span, value.x);
            WriteFloat(span.Slice(4), value.y);
        }

        /// <summary>
        /// 写入三维向量字段。
        /// </summary>
        public void WriteVector3(string key, Vector3 value)
        {
            BeginRecord(key, ESaveKvType.Vector3, 12);
            Span<byte> span = Advance(12);
            WriteFloat(span, value.x);
            WriteFloat(span.Slice(4), value.y);
            WriteFloat(span.Slice(8), value.z);
        }

        /// <summary>
        /// 写入四维向量字段。
        /// </summary>
        public void WriteVector4(string key, Vector4 value)
        {
            BeginRecord(key, ESaveKvType.Vector4, 16);
            WriteFloat4(span: Advance(16), value.x, value.y, value.z, value.w);
        }

        /// <summary>
        /// 写入四元数字段。
        /// </summary>
        public void WriteQuaternion(string key, Quaternion value)
        {
            BeginRecord(key, ESaveKvType.Quaternion, 16);
            WriteFloat4(span: Advance(16), value.x, value.y, value.z, value.w);
        }

        /// <summary>
        /// 写入颜色字段。
        /// </summary>
        public void WriteColor(string key, Color value)
        {
            BeginRecord(key, ESaveKvType.Color, 16);
            WriteFloat4(span: Advance(16), value.r, value.g, value.b, value.a);
        }

        /// <summary>
        /// 写入矩形字段。
        /// </summary>
        public void WriteRect(string key, Rect value)
        {
            BeginRecord(key, ESaveKvType.Rect, 16);
            WriteFloat4(span: Advance(16), value.x, value.y, value.width, value.height);
        }

        /// <summary>
        /// 写入包围盒字段。
        /// </summary>
        public void WriteBounds(string key, Bounds value)
        {
            BeginRecord(key, ESaveKvType.Bounds, 24);
            Span<byte> span = Advance(24);
            WriteFloat(span, value.center.x);
            WriteFloat(span.Slice(4), value.center.y);
            WriteFloat(span.Slice(8), value.center.z);
            WriteFloat(span.Slice(12), value.extents.x);
            WriteFloat(span.Slice(16), value.extents.y);
            WriteFloat(span.Slice(20), value.extents.z);
        }

        /// <summary>写入单精度浮点（netstandard2.1 无 BinaryPrimitives 浮点重载，经位模式转换）。</summary>
        private static void WriteFloat(Span<byte> span, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span, BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>写入 4 个连续 float。</summary>
        private void WriteFloat4(Span<byte> span, float a, float b, float c, float d)
        {
            WriteFloat(span, a);
            WriteFloat(span.Slice(4), b);
            WriteFloat(span.Slice(8), c);
            WriteFloat(span.Slice(12), d);
        }

        #endregion

        #region 嵌套与集合 [NESTED / COLLECTIONS]

        /// <summary>
        /// 开始写入嵌套对象字段（配合 <see cref="EndNested"/>；null 嵌套用 <see cref="WriteNull(string)"/>）。
        /// </summary>
        /// <param name="key">字段键。</param>
        /// <param name="fieldCount">嵌套对象字段数。</param>
        public void BeginNestedObject(string key, int fieldCount)
        {
            BeginRecord(key, ESaveKvType.Object, -1);
            WriteLengthPlaceholder();
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), fieldCount);
            PushNestingFrame();
        }

        /// <summary>
        /// 开始写入序列字段（List/T[]/HashSet；配合元素写入与 <see cref="EndNested"/>；null 用 <see cref="WriteNull(string)"/>）。
        /// </summary>
        /// <param name="key">字段键。</param>
        /// <param name="elementCount">元素数。</param>
        public void BeginSequence(string key, int elementCount)
        {
            BeginRecord(key, ESaveKvType.Sequence, -1);
            WriteLengthPlaceholder();
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), elementCount);
            PushNestingFrame();
        }

        /// <summary>
        /// 开始写入映射字段（Dictionary；键值对交替写入；配合 <see cref="EndNested"/>；null 用 <see cref="WriteNull(string)"/>）。
        /// </summary>
        /// <param name="key">字段键。</param>
        /// <param name="pairCount">键值对数。</param>
        public void BeginMap(string key, int pairCount)
        {
            BeginRecord(key, ESaveKvType.Map, -1);
            WriteLengthPlaceholder();
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), pairCount);
            PushNestingFrame();
        }

        /// <summary>
        /// 结束当前嵌套作用域（回填载荷长度）。
        /// </summary>
        public void EndNested()
        {
            if (_nestingDepth == 0)
            {
                throw new InvalidOperationException("KVT nested scope is not open.");
            }

            NestingFrame frame = _nestingStack[--_nestingDepth];
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(frame.LengthPlaceholderOffset), _position - frame.PayloadStart);
        }

        /// <summary>
        /// 写入空引用字段（字符串/嵌套对象/集合均可；集合元素场景传 <see cref="string.Empty"/> 键）。
        /// </summary>
        public void WriteNull(string key)
        {
            BeginRecord(key, ESaveKvType.Null, 0);
        }

        #endregion

        #region 写入管线 [WRITE PIPELINE]

        /// <summary>
        /// 写入对象级记录头：[2B 键长][键 UTF8][1B 类型][4B 载荷长占位]。
        /// </summary>
        private void BeginRecord(string key, ESaveKvType type, int payloadLength)
        {
            int keyByteCount = s_Utf8.GetByteCount(key);
            EnsureCapacity(3 + keyByteCount + 4);
            BinaryPrimitives.WriteUInt16LittleEndian(Advance(2), (ushort)keyByteCount);
            s_Utf8.GetBytes(key, Advance(keyByteCount));
            _buffer[_position++] = (byte)type;
            if (payloadLength >= 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(Advance(4), payloadLength);
            }
        }

        /// <summary>
        /// 写入 4 字节载荷长度占位（由 <see cref="EndNested"/> 回填）。
        /// </summary>
        private void WriteLengthPlaceholder()
        {
            BinaryPrimitives.WriteInt32LittleEndian(Advance(4), 0);
        }

        /// <summary>
        /// 推进指定字节数并返回写入跨度（先确保容量）。
        /// </summary>
        private Span<byte> Advance(int count)
        {
            EnsureCapacity(count);
            Span<byte> span = _buffer.AsSpan(_position, count);
            _position += count;
            return span;
        }

        /// <summary>
        /// 确保缓冲区可再容纳指定字节（不足时倍增扩容）。
        /// </summary>
        private void EnsureCapacity(int count)
        {
            if (_position + count <= _buffer.Length)
            {
                return;
            }

            int newSize = _buffer.Length * 2;
            while (newSize < _position + count)
            {
                newSize *= 2;
            }

            Array.Resize(ref _buffer, newSize);
        }

        /// <summary>
        /// 压入嵌套帧（记录载荷长度占位偏移与载荷起点）。
        /// </summary>
        private void PushNestingFrame()
        {
            if (_nestingStack == null)
            {
                _nestingStack = new NestingFrame[8];
            }
            else if (_nestingDepth == _nestingStack.Length)
            {
                Array.Resize(ref _nestingStack, _nestingDepth * 2);
            }

            _nestingStack[_nestingDepth++] = new NestingFrame(_position - 4, _position);
        }

        /// <summary>嵌套帧：长度占位偏移 + 载荷起点。</summary>
        private readonly struct NestingFrame
        {
            public readonly int LengthPlaceholderOffset;
            public readonly int PayloadStart;

            public NestingFrame(int lengthPlaceholderOffset, int payloadStart)
            {
                LengthPlaceholderOffset = lengthPlaceholderOffset;
                PayloadStart = payloadStart;
            }
        }

        #endregion
    }
}
