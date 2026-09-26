using System;
using System.Collections.Generic;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;

namespace Service.Save
{
    /// <summary>
    /// KVT 元素级记录测试：序列/映射/嵌套对象元素的写入-读取往返、null 元素、类型不符跳过、缓冲区边界回归。
    /// </summary>
    public class SaveKeyValueElementTests
    {
        [Test]
        public void Sequence_PrimitiveElements_RoundTrips()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginSequence("items", 4);
            writer.WriteInt32Element(10);
            writer.WriteInt32Element(-20);
            writer.WriteInt32Element(int.MaxValue);
            writer.WriteInt32Element(0);
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type));
            Assert.AreEqual("items", System.Text.Encoding.UTF8.GetString(key));
            Assert.AreEqual(ESaveKvType.Sequence, type);
            Assert.AreEqual(4, reader.ReadChildCount());

            var values = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(reader.ReadElement(out ESaveKvType elementType));
                Assert.AreEqual(ESaveKvType.Int32, elementType);
                values.Add(reader.ReadInt32());
            }

            CollectionAssert.AreEqual(new[] { 10, -20, int.MaxValue, 0 }, values);
            Assert.IsFalse(reader.ReadRecord(out _, out _), "序列消费完毕后不应再有记录");
        }

        [Test]
        public void Sequence_StringAndUnityMathElements_RoundTrips()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginSequence("mixed", 3);
            writer.WriteStringElement("Moirai⑵");
            writer.WriteVector3Element(new Vector3(1f, 2f, 3f));
            writer.WriteQuaternionElement(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f));
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Sequence, type);
            Assert.AreEqual(3, reader.ReadChildCount());

            Assert.IsTrue(reader.ReadElement(out ESaveKvType e1));
            Assert.AreEqual(ESaveKvType.String, e1);
            Assert.AreEqual("Moirai⑵", reader.ReadString());

            Assert.IsTrue(reader.ReadElement(out ESaveKvType e2));
            Assert.AreEqual(ESaveKvType.Vector3, e2);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), reader.ReadVector3());

            Assert.IsTrue(reader.ReadElement(out ESaveKvType e3));
            Assert.AreEqual(ESaveKvType.Quaternion, e3);
            Assert.AreEqual(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f), reader.ReadQuaternion());
        }

        [Test]
        public void Sequence_NullElement_RoundTrips()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginSequence("names", 3);
            writer.WriteStringElement("a");
            writer.WriteNullElement();
            writer.WriteStringElement("c");
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Sequence, type);
            Assert.AreEqual(3, reader.ReadChildCount());

            var values = new string[3];
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(reader.ReadElement(out ESaveKvType elementType));
                if (elementType == ESaveKvType.Null)
                {
                    reader.SkipRecordPayload(); // Null 元素仍带 4B 长度前缀（值=0），须消费保持游标对齐
                    values[i] = null;
                }
                else
                {
                    values[i] = reader.ReadString();
                }
            }

            CollectionAssert.AreEqual(new[] { "a", null, "c" }, values);
        }

        [Test]
        public void Map_StringKeys_NestedObjectValues_RoundTrips()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginMap("stats", 2);

            writer.WriteStringElement("boss");
            writer.BeginNestedObjectElement(2);
            writer.WriteInt32("kills", 5);
            writer.WriteSingle("time", 12.5f);
            writer.EndNested();

            writer.WriteStringElement("minion");
            writer.BeginNestedObjectElement(1);
            writer.WriteInt32("kills", 42);
            writer.EndNested();

            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Map, type);
            Assert.AreEqual(2, reader.ReadChildCount());

            var result = new Dictionary<string, (int kills, float time)>();
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(reader.ReadElement(out ESaveKvType keyType));
                Assert.AreEqual(ESaveKvType.String, keyType);
                string mapKey = reader.ReadString();

                Assert.IsTrue(reader.ReadElement(out ESaveKvType valueType));
                Assert.AreEqual(ESaveKvType.Object, valueType);
                int fieldCount = reader.ReadChildCount();

                int kills = 0;
                float time = 0f;
                for (int f = 0; f < fieldCount; f++)
                {
                    Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> fieldKey, out ESaveKvType fieldType));
                    string fieldName = System.Text.Encoding.UTF8.GetString(fieldKey);
                    if (fieldName == "kills" && fieldType == ESaveKvType.Int32)
                    {
                        kills = reader.ReadInt32();
                    }
                    else if (fieldName == "time" && fieldType == ESaveKvType.Single)
                    {
                        time = reader.ReadSingle();
                    }
                    else
                    {
                        reader.SkipRecordPayload();
                    }
                }

                result[mapKey] = (kills, time);
            }

            Assert.AreEqual((5, 12.5f), result["boss"]);
            Assert.AreEqual((42, 0f), result["minion"]);
        }

        [Test]
        public void Sequence_NestedSequences_RoundTrips()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginSequence("matrix", 2);
            writer.BeginSequenceElement(2);
            writer.WriteInt32Element(1);
            writer.WriteInt32Element(2);
            writer.EndNested();
            writer.BeginSequenceElement(1);
            writer.WriteInt32Element(3);
            writer.EndNested();
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Sequence, type);
            Assert.AreEqual(2, reader.ReadChildCount());

            var rows = new List<List<int>>();
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(reader.ReadElement(out ESaveKvType rowType));
                Assert.AreEqual(ESaveKvType.Sequence, rowType);
                int count = reader.ReadChildCount();
                var row = new List<int>(count);
                for (int j = 0; j < count; j++)
                {
                    Assert.IsTrue(reader.ReadElement(out ESaveKvType cellType));
                    Assert.AreEqual(ESaveKvType.Int32, cellType);
                    row.Add(reader.ReadInt32());
                }

                rows.Add(row);
            }

            CollectionAssert.AreEqual(new[] { 1, 2 }, rows[0]);
            CollectionAssert.AreEqual(new[] { 3 }, rows[1]);
        }

        [Test]
        public void Element_TypeMismatch_SkipKeepsCursorAligned()
        {
            var writer = new SaveKeyValueWriter(64);
            writer.BeginSequence("items", 3);
            writer.WriteInt32Element(7);
            writer.WriteInt32Element(8);
            writer.WriteInt32Element(9);
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out _));
            Assert.AreEqual(3, reader.ReadChildCount());

            var values = new int[3];
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(reader.ReadElement(out ESaveKvType elementType));
                // 模拟元素类型演进为 Int64：不符则跳过载荷，槽位保持默认值
                if (elementType == ESaveKvType.Int64)
                {
                    values[i] = (int)reader.ReadInt64();
                }
                else
                {
                    reader.SkipRecordPayload();
                }
            }

            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, values);
            Assert.IsFalse(reader.ReadRecord(out _, out _), "跳过后游标应与序列末尾对齐");
        }

        [Test]
        public void Writer_TinyCapacity_LongKey_DoesNotOverflow()
        {
            // 回归：记录头恰好耗尽缓冲区时，1 字节载荷（Bool/Byte/SByte/DateTime.Kind）不得越界
            var writer = new SaveKeyValueWriter(16);
            writer.WriteBoolean("key123456", true);
            writer.WriteByte("key1234567", 200);
            writer.WriteDateTime("key12345678", new DateTime(2026, 9, 11, 1, 2, 3, DateTimeKind.Utc));
            writer.WriteStringElement("element-payload-boundary");

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType t1));
            Assert.AreEqual(ESaveKvType.Bool, t1);
            Assert.IsTrue(reader.ReadBoolean());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType t2));
            Assert.AreEqual(ESaveKvType.Byte, t2);
            Assert.AreEqual(200, reader.ReadByte());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType t3));
            Assert.AreEqual(ESaveKvType.DateTime, t3);
            Assert.AreEqual(new DateTime(2026, 9, 11, 1, 2, 3, DateTimeKind.Utc), reader.ReadDateTime());
            // 首条元素级记录（无键）：对象级游标结束后续按元素读取
            Assert.IsTrue(reader.ReadElement(out ESaveKvType t4));
            Assert.AreEqual(ESaveKvType.String, t4);
            Assert.AreEqual("element-payload-boundary", reader.ReadString());
        }

        [Test]
        public void EmptySequenceAndMap_RoundTrip()
        {
            var writer = new SaveKeyValueWriter(16);
            writer.BeginSequence("empty_seq", 0);
            writer.EndNested();
            writer.BeginMap("empty_map", 0);
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType t1));
            Assert.AreEqual(ESaveKvType.Sequence, t1);
            Assert.AreEqual(0, reader.ReadChildCount());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType t2));
            Assert.AreEqual(ESaveKvType.Map, t2);
            Assert.AreEqual(0, reader.ReadChildCount());
            Assert.IsFalse(reader.ReadRecord(out _, out _));
        }

        [Test]
        public void Sequence_AllScalarKinds_RoundTrips()
        {
            var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
            var span = TimeSpan.FromMinutes(90);

            var writer = new SaveKeyValueWriter(128);
            writer.BeginSequence("all", 16);
            writer.WriteBooleanElement(true);
            writer.WriteSByteElement(-8);
            writer.WriteByteElement(250);
            writer.WriteInt16Element(-1600);
            writer.WriteUInt16Element(65000);
            writer.WriteInt32Element(-32000);
            writer.WriteUInt32Element(4000000000u);
            writer.WriteInt64Element(-6400000000L);
            writer.WriteUInt64Element(18000000000000000000ul);
            writer.WriteSingleElement(3.25f);
            writer.WriteDoubleElement(6.5d);
            writer.WriteDecimalElement(7.75m);
            writer.WriteCharElement('字');
            writer.WriteDateTimeElement(stamp);
            writer.WriteTimeSpanElement(span);
            writer.WriteColorElement(new Color(0.1f, 0.2f, 0.3f, 0.4f));
            writer.EndNested();

            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Sequence, type);
            Assert.AreEqual(16, reader.ReadChildCount());

            Assert.AreEqual(ESaveKvType.Bool, Next(ref reader)); Assert.IsTrue(reader.ReadBoolean());
            Assert.AreEqual(ESaveKvType.SByte, Next(ref reader)); Assert.AreEqual((sbyte)-8, reader.ReadSByte());
            Assert.AreEqual(ESaveKvType.Byte, Next(ref reader)); Assert.AreEqual((byte)250, reader.ReadByte());
            Assert.AreEqual(ESaveKvType.Int16, Next(ref reader)); Assert.AreEqual((short)-1600, reader.ReadInt16());
            Assert.AreEqual(ESaveKvType.UInt16, Next(ref reader)); Assert.AreEqual((ushort)65000, reader.ReadUInt16());
            Assert.AreEqual(ESaveKvType.Int32, Next(ref reader)); Assert.AreEqual(-32000, reader.ReadInt32());
            Assert.AreEqual(ESaveKvType.UInt32, Next(ref reader)); Assert.AreEqual(4000000000u, reader.ReadUInt32());
            Assert.AreEqual(ESaveKvType.Int64, Next(ref reader)); Assert.AreEqual(-6400000000L, reader.ReadInt64());
            Assert.AreEqual(ESaveKvType.UInt64, Next(ref reader)); Assert.AreEqual(18000000000000000000ul, reader.ReadUInt64());
            Assert.AreEqual(ESaveKvType.Single, Next(ref reader)); Assert.AreEqual(3.25f, reader.ReadSingle());
            Assert.AreEqual(ESaveKvType.Double, Next(ref reader)); Assert.AreEqual(6.5d, reader.ReadDouble());
            Assert.AreEqual(ESaveKvType.Decimal, Next(ref reader)); Assert.AreEqual(7.75m, reader.ReadDecimal());
            Assert.AreEqual(ESaveKvType.Char, Next(ref reader)); Assert.AreEqual('字', reader.ReadChar());
            Assert.AreEqual(ESaveKvType.DateTime, Next(ref reader)); Assert.AreEqual(stamp, reader.ReadDateTime());
            Assert.AreEqual(ESaveKvType.TimeSpan, Next(ref reader)); Assert.AreEqual(span, reader.ReadTimeSpan());
            Assert.AreEqual(ESaveKvType.Color, Next(ref reader)); Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 0.4f), reader.ReadColor());
        }

        /// <summary>
        /// 读取下一个元素类型（ref struct 须按引用传递以推进游标）。
        /// </summary>
        private static ESaveKvType Next(ref SaveKeyValueReader reader)
        {
            Assert.IsTrue(reader.ReadElement(out ESaveKvType type));
            return type;
        }
    }
}
