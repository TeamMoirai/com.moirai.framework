using System;
using System.Collections.Generic;
using System.Text;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// KVT 模板差分器测试：标量变动/嵌套递归/序列整条/新增记录/类型漂移/恒透传作用域/体积收缩/坏档异常。
    /// <para>差分块内容经 <see cref="SaveKeyValueReader"/> 读回断言（纯函数无场景依赖）。</para>
    /// </summary>
    public class SaveKvDifferTests
    {
        /// <summary>UTF-8 解码器。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 构造基准 KVT：$schemas(T=1) + T 作用域{a=1, b="x", n{c=2, d=3}, s=[1,2]}。
        /// </summary>
        private static byte[] BuildBaseline()
        {
            var writer = new SaveKeyValueWriter(256);
            writer.BeginNestedObject("$schemas", 1);
            writer.WriteInt32("T", 1);
            writer.EndNested();
            writer.BeginNestedObject("T", 4);
            writer.WriteInt32("a", 1);
            writer.WriteString("b", "x");
            writer.BeginNestedObject("n", 2);
            writer.WriteInt32("c", 2);
            writer.WriteInt32("d", 3);
            writer.EndNested();
            writer.BeginSequence("s", 2);
            writer.WriteInt32Element(1);
            writer.WriteInt32Element(2);
            writer.EndNested();
            writer.EndNested();
            return writer.ToArray();
        }

        /// <summary>
        /// 构造当前 KVT（与基准同构；参数化覆写各字段）。
        /// </summary>
        private static byte[] BuildCurrent(int a = 1, string b = "x", int c = 2, int d = 3, int[] sequence = null, bool extraRecord = false, bool driftA = false)
        {
            sequence ??= new[] { 1, 2 };
            var writer = new SaveKeyValueWriter(256);
            writer.BeginNestedObject("$schemas", 1);
            writer.WriteInt32("T", 1);
            writer.EndNested();
            writer.BeginNestedObject("T", extraRecord ? 5 : 4);
            if (driftA)
            {
                writer.WriteString("a", "drifted");
            }
            else
            {
                writer.WriteInt32("a", a);
            }

            writer.WriteString("b", b);
            writer.BeginNestedObject("n", 2);
            writer.WriteInt32("c", c);
            writer.WriteInt32("d", d);
            writer.EndNested();
            writer.BeginSequence("s", sequence.Length);
            for (int i = 0; i < sequence.Length; i++)
            {
                writer.WriteInt32Element(sequence[i]);
            }

            writer.EndNested();
            if (extraRecord)
            {
                writer.WriteInt32("e", 99);
            }

            writer.EndNested();
            return writer.ToArray();
        }

        /// <summary>
        /// 收集指定作用域的记录键（顶层读到流尾；嵌套路径按子项数精确消费）。
        /// </summary>
        private static List<string> CollectKeys(byte[] bytes, params string[] scopePath)
        {
            var keys = new List<string>();
            var reader = new SaveKeyValueReader(bytes);
            int limit = -1;
            for (int depth = 0; depth < scopePath.Length; depth++)
            {
                bool entered = false;
                while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
                {
                    if (type == ESaveKvType.Object && s_Utf8.GetString(key) == scopePath[depth])
                    {
                        limit = reader.ReadChildCount();
                        entered = true;
                        break;
                    }

                    reader.SkipRecordPayload();
                }

                Assert.IsTrue(entered, $"作用域路径段 '{scopePath[depth]}' 未找到");
            }

            int read = 0;
            while ((limit < 0 || read < limit) && reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType _))
            {
                keys.Add(s_Utf8.GetString(key));
                reader.SkipRecordPayload();
                read++;
            }

            return keys;
        }

        [Test]
        public void Diff_NoChange_OnlySchemasPassThrough()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent());
            List<string> topKeys = CollectKeys(diff);
            CollectionAssert.AreEquivalent(new[] { "$schemas" }, topKeys, "零变动时差分块应只剩模式版本作用域");
        }

        [Test]
        public void Diff_ScalarChange_WritesOnlyChangedField()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent(a: 9));
            List<string> topKeys = CollectKeys(diff);
            CollectionAssert.AreEquivalent(new[] { "$schemas", "T" }, topKeys);
            List<string> scopeKeys = CollectKeys(diff, "T");
            CollectionAssert.AreEquivalent(new[] { "a" }, scopeKeys, "未变动字段不应进入差分块");
        }

        [Test]
        public void Diff_NestedFieldChange_CarriesSparseNestedScope()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent(c: 7));
            List<string> scopeKeys = CollectKeys(diff, "T");
            CollectionAssert.AreEquivalent(new[] { "n" }, scopeKeys);
            List<string> nestedKeys = CollectKeys(diff, "T", "n");
            CollectionAssert.AreEquivalent(new[] { "c" }, nestedKeys, "嵌套对象应只携带变动内层字段");
        }

        [Test]
        public void Diff_SequenceChange_CarriesWholeRecord()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent(sequence: new[] { 1, 3 }));
            List<string> scopeKeys = CollectKeys(diff, "T");
            CollectionAssert.AreEquivalent(new[] { "s" }, scopeKeys, "集合任一变动整条携带（元素级差分为 v2 范围）");
        }

        [Test]
        public void Diff_NewRecordNotInBaseline_CopiedRaw()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent(extraRecord: true));
            List<string> scopeKeys = CollectKeys(diff, "T");
            CollectionAssert.Contains(scopeKeys, "e", "基准缺失的记录应整条透传");
        }

        [Test]
        public void Diff_TypeDrift_CopiedRaw()
        {
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), BuildCurrent(driftA: true));
            List<string> scopeKeys = CollectKeys(diff, "T");
            CollectionAssert.Contains(scopeKeys, "a", "记录类型漂移应整条透传");
        }

        [Test]
        public void Diff_NullBaseline_ReturnsFullCapture()
        {
            byte[] current = BuildCurrent(a: 5);
            Assert.AreSame(current, SaveKvDiffer.Diff(null, current), "无基准应退化为全量捕获（同引用）");
            Assert.AreSame(current, SaveKvDiffer.Diff(Array.Empty<byte>(), current));
        }

        [Test]
        public void Diff_SchemaScope_AlwaysCopiedEvenWithoutBaselineEntry()
        {
            // 基准不含 $schemas（异常构造）——恒透传语义仍应保留该作用域
            var writer = new SaveKeyValueWriter(64);
            writer.BeginNestedObject("T", 1);
            writer.WriteInt32("a", 1);
            writer.EndNested();
            byte[] baselineWithoutSchemas = writer.ToArray();

            byte[] diff = SaveKvDiffer.Diff(baselineWithoutSchemas, BuildCurrent());
            List<string> topKeys = CollectKeys(diff);
            CollectionAssert.Contains(topKeys, "$schemas", "模式版本作用域恒透传");
        }

        [Test]
        public void Diff_SingleFieldChange_SmallerThanFullCapture()
        {
            byte[] full = BuildCurrent(a: 9);
            byte[] diff = SaveKvDiffer.Diff(BuildBaseline(), full);
            Assert.Less(diff.Length, full.Length, "单字段变动的差分块必须小于全量捕获");
        }

        [Test]
        public void Diff_MalformedCurrent_ThrowsFormatException()
        {
            byte[] malformed = new byte[] { 0, 5, (byte)'k' }; // 键长 5 超过剩余数据
            Assert.Throws<SaveKvFormatException>(() => SaveKvDiffer.Diff(BuildBaseline(), malformed));
        }
    }
}
