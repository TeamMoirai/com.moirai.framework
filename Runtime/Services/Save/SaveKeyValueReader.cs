using System;
using System.Buffers.Binary;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 键值捕获读取器（KVT 格式，ref struct，纯顺序游标）。
    /// <para>与 <see cref="SaveKeyValueWriter"/> 对偶：每条记录自描述（[1B 类型][4B 载荷长][载荷]，对象级记录另带键），
    /// 未知键经 <see cref="SkipRecordPayload"/> O(1) 跳过（字段废弃向后兼容的关键）；嵌套作用域由生成代码按
    /// 字段数/元素数精确消费（捕获顺序与恢复顺序由同一生成代码决定，顺序天然一致）。</para>
    /// <para>须在主线程调用（写回 MonoBehaviour 字段）。</para>
    /// </summary>
    public ref struct SaveKeyValueReader
    {
        /// <summary>UTF-8 解码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        private ReadOnlySpan<byte> _data;

        /// <summary>
        /// 创建读取器。
        /// </summary>
        /// <param name="data">块载荷字节。</param>
        public SaveKeyValueReader(ReadOnlySpan<byte> data)
        {
            _data = data;
        }

        /// <summary>
        /// 读取下一个对象级记录头（带键；记录头后紧跟载荷长度与载荷）。
        /// </summary>
        /// <param name="key">键（UTF-8 字节跨度，指向数据缓冲区，仅当前记录比较期内有效）。</param>
        /// <param name="type">记录类型。</param>
        /// <returns>还有记录返回 <c>true</c>；遍历结束返回 <c>false</c>。</returns>
        public bool ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type)
        {
            if (_data.Length < 3)
            {
                key = default;
                type = default;
                return false;
            }

            int keyByteCount = BinaryPrimitives.ReadUInt16LittleEndian(_data);
            if (keyByteCount > _data.Length - 3)
            {
                throw new SaveKvFormatException("KVT record key length exceeds remaining data.");
            }

            key = _data.Slice(2, keyByteCount);
            type = (ESaveKvType)_data[2 + keyByteCount];
            _data = _data.Slice(3 + keyByteCount);
            return true;
        }

        /// <summary>
        /// 读取下一个集合元素记录头（无键）。
        /// </summary>
        /// <param name="type">元素类型。</param>
        /// <returns>还有元素返回 <c>true</c>。</returns>
        public bool ReadElement(out ESaveKvType type)
        {
            if (_data.Length < 1)
            {
                type = default;
                return false;
            }

            type = (ESaveKvType)_data[0];
            _data = _data.Slice(1);
            return true;
        }

        /// <summary>
        /// 读取嵌套作用域的子项数（对象字段数/序列元素数/映射对数）。
        /// <para>消费 [4B 载荷长][4B 子项数]。</para>
        /// </summary>
        public int ReadChildCount()
        {
            if (_data.Length < 8)
            {
                throw new SaveKvFormatException("KVT nested scope header is truncated.");
            }

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(_data);
            if (payloadLength < 4 || payloadLength > _data.Length - 4)
            {
                throw new SaveKvFormatException(StringUtility.Format("KVT nested payload length {0} exceeds remaining data {1}.", payloadLength, _data.Length - 4));
            }

            int childCount = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(4));
            _data = _data.Slice(8);
            return childCount;
        }

        /// <summary>
        /// 跳过当前记录载荷（未知键的跳过；记录头已由 <see cref="ReadRecord"/>/<see cref="ReadElement"/> 消费）。
        /// </summary>
        public void SkipRecordPayload()
        {
            int payloadLength = ReadPayloadLength();
            _data = _data.Slice(payloadLength);
        }

        #region 载荷读取 [PAYLOAD READERS]

        /// <summary>
        /// 读取布尔载荷。
        /// </summary>
        public bool ReadBoolean()
        {
            TakeFixedPayload(1);
            bool value = _data[0] != 0;
            _data = _data.Slice(1);
            return value;
        }

        /// <summary>
        /// 读取有符号字节载荷。
        /// </summary>
        public sbyte ReadSByte()
        {
            TakeFixedPayload(1);
            sbyte value = unchecked((sbyte)_data[0]);
            _data = _data.Slice(1);
            return value;
        }

        /// <summary>
        /// 读取无符号字节载荷。
        /// </summary>
        public byte ReadByte()
        {
            TakeFixedPayload(1);
            byte value = _data[0];
            _data = _data.Slice(1);
            return value;
        }

        /// <summary>
        /// 读取有符号 16 位载荷。
        /// </summary>
        public short ReadInt16()
        {
            TakeFixedPayload(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(_data);
            _data = _data.Slice(2);
            return value;
        }

        /// <summary>
        /// 读取无符号 16 位载荷。
        /// </summary>
        public ushort ReadUInt16()
        {
            TakeFixedPayload(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data);
            _data = _data.Slice(2);
            return value;
        }

        /// <summary>
        /// 读取有符号 32 位载荷。
        /// </summary>
        public int ReadInt32()
        {
            TakeFixedPayload(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_data);
            _data = _data.Slice(4);
            return value;
        }

        /// <summary>
        /// 读取无符号 32 位载荷。
        /// </summary>
        public uint ReadUInt32()
        {
            TakeFixedPayload(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data);
            _data = _data.Slice(4);
            return value;
        }

        /// <summary>
        /// 读取有符号 64 位载荷。
        /// </summary>
        public long ReadInt64()
        {
            TakeFixedPayload(8);
            long value = BinaryPrimitives.ReadInt64LittleEndian(_data);
            _data = _data.Slice(8);
            return value;
        }

        /// <summary>
        /// 读取无符号 64 位载荷。
        /// </summary>
        public ulong ReadUInt64()
        {
            TakeFixedPayload(8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data);
            _data = _data.Slice(8);
            return value;
        }

        /// <summary>
        /// 读取单精度浮点载荷。
        /// </summary>
        public float ReadSingle()
        {
            TakeFixedPayload(4);
            float value = ReadFloat(_data);
            _data = _data.Slice(4);
            return value;
        }

        /// <summary>
        /// 读取双精度浮点载荷。
        /// </summary>
        public double ReadDouble()
        {
            TakeFixedPayload(8);
            double value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(_data));
            _data = _data.Slice(8);
            return value;
        }

        /// <summary>
        /// 读取十进制载荷。
        /// </summary>
        public decimal ReadDecimal()
        {
            TakeFixedPayload(16);
            int lo = BinaryPrimitives.ReadInt32LittleEndian(_data);
            int mid = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(4));
            int hi = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(8));
            int flags = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(12));
            _data = _data.Slice(16);
            return new decimal(lo, mid, hi, (flags & 0x80000000) != 0, (byte)((flags >> 16) & 0xFF));
        }

        /// <summary>
        /// 读取字符载荷。
        /// </summary>
        public char ReadChar()
        {
            TakeFixedPayload(2);
            char value = (char)BinaryPrimitives.ReadUInt16LittleEndian(_data);
            _data = _data.Slice(2);
            return value;
        }

        /// <summary>
        /// 读取字符串载荷。
        /// </summary>
        public string ReadString()
        {
            int byteCount = ReadPayloadLength();
            string value = s_Utf8.GetString(_data.Slice(0, byteCount));
            _data = _data.Slice(byteCount);
            return value;
        }

        /// <summary>
        /// 读取日期时间载荷。
        /// </summary>
        public DateTime ReadDateTime()
        {
            TakeFixedPayload(9);
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(_data);
            var kind = (DateTimeKind)_data[8];
            _data = _data.Slice(9);
            return new DateTime(ticks, kind);
        }

        /// <summary>
        /// 读取时间跨度载荷。
        /// </summary>
        public TimeSpan ReadTimeSpan()
        {
            TakeFixedPayload(8);
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(_data);
            _data = _data.Slice(8);
            return new TimeSpan(ticks);
        }

        /// <summary>
        /// 读取二维向量载荷。
        /// </summary>
        public Vector2 ReadVector2()
        {
            TakeFixedPayload(8);
            Vector2 value = new Vector2(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)));
            _data = _data.Slice(8);
            return value;
        }

        /// <summary>
        /// 读取三维向量载荷。
        /// </summary>
        public Vector3 ReadVector3()
        {
            TakeFixedPayload(12);
            Vector3 value = new Vector3(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)));
            _data = _data.Slice(12);
            return value;
        }

        /// <summary>
        /// 读取四维向量载荷。
        /// </summary>
        public Vector4 ReadVector4()
        {
            TakeFixedPayload(16);
            Vector4 value = new Vector4(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)),
                ReadFloat(_data.Slice(12)));
            _data = _data.Slice(16);
            return value;
        }

        /// <summary>
        /// 读取四元数载荷。
        /// </summary>
        public Quaternion ReadQuaternion()
        {
            TakeFixedPayload(16);
            Quaternion value = new Quaternion(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)),
                ReadFloat(_data.Slice(12)));
            _data = _data.Slice(16);
            return value;
        }

        /// <summary>
        /// 读取颜色载荷。
        /// </summary>
        public Color ReadColor()
        {
            TakeFixedPayload(16);
            Color value = new Color(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)),
                ReadFloat(_data.Slice(12)));
            _data = _data.Slice(16);
            return value;
        }

        /// <summary>
        /// 读取矩形载荷。
        /// </summary>
        public Rect ReadRect()
        {
            TakeFixedPayload(16);
            Rect value = new Rect(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)),
                ReadFloat(_data.Slice(12)));
            _data = _data.Slice(16);
            return value;
        }

        /// <summary>
        /// 读取包围盒载荷。
        /// </summary>
        public Bounds ReadBounds()
        {
            TakeFixedPayload(24);
            Bounds value = default;
            value.center = new Vector3(
                ReadFloat(_data),
                ReadFloat(_data.Slice(4)),
                ReadFloat(_data.Slice(8)));
            value.extents = new Vector3(
                ReadFloat(_data.Slice(12)),
                ReadFloat(_data.Slice(16)),
                ReadFloat(_data.Slice(20)));
            _data = _data.Slice(24);
            return value;
        }

        #endregion

        #region 内部读取管线 [READ PIPELINE]

        /// <summary>读取单精度浮点（netstandard2.1 无 BinaryPrimitives 浮点重载，经位模式转换）。</summary>
        private static float ReadFloat(ReadOnlySpan<byte> span)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span));
        }

        /// <summary>
        /// 读取 4 字节载荷长度（不消费载荷）。
        /// </summary>
        /// <returns>载荷长度。</returns>
        private int ReadPayloadLength()
        {
            if (_data.Length < 4)
            {
                throw new SaveKvFormatException("KVT payload length header is truncated.");
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(_data);
            _data = _data.Slice(4);
            if (length < 0 || length > _data.Length)
            {
                throw new SaveKvFormatException(StringUtility.Format("KVT payload length {0} exceeds remaining data {1}.", length, _data.Length));
            }

            return length;
        }

        /// <summary>
        /// 读取并校验定长载荷长度。
        /// </summary>
        /// <param name="expectedLength">期望载荷字节数。</param>
        private void TakeFixedPayload(int expectedLength)
        {
            int length = ReadPayloadLength();
            if (length != expectedLength)
            {
                throw new SaveKvFormatException(StringUtility.Format("KVT payload length mismatch: expected {0}, actual {1}.", expectedLength, length));
            }
        }

        #endregion
    }

    /// <summary>
    /// KVT 格式损坏异常（块载荷与格式约定不符）。
    /// </summary>
    public sealed class SaveKvFormatException : Exception
    {
        /// <summary>
        /// 创建格式损坏异常。
        /// </summary>
        /// <param name="message">描述消息。</param>
        public SaveKvFormatException(string message) : base(message)
        {
        }
    }
}
