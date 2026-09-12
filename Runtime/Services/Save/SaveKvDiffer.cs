using System;
using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// KVT 模板差分器（纯函数）：以预制体模板基准捕获为参照，从实体全量捕获中提炼「仅变动字段」的稀疏 KVT 块。
    /// <para>差分粒度 = 字段级：标量/Null/序列/映射记录按原始字节规范比较，不同才写入；嵌套对象递归差分，
    /// 仅当内层存在变动时才携带该作用域（元素级集合差分为 v2 范围，集合任一变动即整记录携带）。
    /// 基准缺失的记录（运行期新增绑定/模板外字段）整条原始透传。</para>
    /// <para><see cref="SaveComponent.SchemaScopeKey"/> 模式版本作用域恒整条透传（恢复侧迁移路由的依据，不参与差分）。</para>
    /// <para>恢复侧零特殊路径：稀疏块本身就是合法 KVT——生成捕获器键匹配读回、缺失字段保留当前值，
    /// 而实例化出的实体当前值即模板默认值，差分块应用后等价于全量恢复。</para>
    /// <para>仅供实体持久化在保存管线调用（每实体每档一次，非热路径）；格式损坏抛 <see cref="SaveKvFormatException"/>。</para>
    /// </summary>
    internal static class SaveKvDiffer
    {
        /// <summary>UTF-8 编解码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 记录引用：基准作用域内单条记录的原始载荷区间（[4B 载荷长][载荷]，记录头不含）。
        /// </summary>
        private readonly struct RecordRef
        {
            /// <summary>记录类型。</summary>
            internal readonly ESaveKvType Type;

            /// <summary>原始区间在基准字节中的起始偏移。</summary>
            internal readonly int Offset;

            /// <summary>原始区间字节长度。</summary>
            internal readonly int Length;

            internal RecordRef(ESaveKvType type, int offset, int length)
            {
                Type = type;
                Offset = offset;
                Length = length;
            }
        }

        /// <summary>
        /// 作用域索引：键 → 记录引用（嵌套对象子作用域索引懒惰构建并缓存）。
        /// </summary>
        private sealed class ScopeIndex
        {
            /// <summary>基准块字节。</summary>
            internal byte[] Data;

            /// <summary>键 → 记录引用表。</summary>
            internal Dictionary<string, RecordRef> Records;

            /// <summary>键 → 嵌套子作用域索引缓存。</summary>
            internal Dictionary<string, ScopeIndex> Children;

            /// <summary>
            /// 获取嵌套对象记录的子作用域索引（非嵌套记录返回 <c>null</c>；递归差分用）。
            /// </summary>
            /// <param name="key">记录键。</param>
            /// <returns>子作用域索引；记录缺失或非嵌套类型返回 <c>null</c>。</returns>
            internal ScopeIndex GetChild(string key)
            {
                if (Children != null && Children.TryGetValue(key, out ScopeIndex cached))
                {
                    return cached;
                }

                if (!Records.TryGetValue(key, out RecordRef record) || record.Type != ESaveKvType.Object || record.Length < 8)
                {
                    return null;
                }

                // 子作用域字节 = 载荷去掉 [4B 载荷长][4B 子项数] 头
                int childStart = record.Offset + 8;
                int childLength = record.Length - 8;
                var childReader = new SaveKeyValueReader(Data.AsSpan(childStart, childLength));
                ScopeIndex child = BuildIndex(ref childReader, Data, childStart);
                Children ??= new Dictionary<string, ScopeIndex>(StringComparer.Ordinal);
                Children[key] = child;
                return child;
            }
        }

        /// <summary>
        /// 记录判定结果（<see cref="EvaluateRecord"/> 输出）。
        /// </summary>
        private enum ERecordVerdict
        {
            /// <summary>与基准一致——丢弃。</summary>
            Equal = 0,

            /// <summary>整条原始透传（恒透传键/基准缺失/类型漂移/字节不等）。</summary>
            CopyRaw = 1,

            /// <summary>嵌套对象内层有变动——以稀疏子作用域携带。</summary>
            NestedDiff = 2,
        }

        /// <summary>
        /// 提炼差分块：仅写入相对基准变动的记录（含基准缺失记录与 <see cref="SaveComponent.SchemaScopeKey"/> 恒透传）。
        /// </summary>
        /// <param name="baseline">模板基准捕获字节（<c>null</c>/空 = 无基准，直接返回全量捕获）。</param>
        /// <param name="current">实体全量捕获字节。</param>
        /// <returns>稀疏 KVT 块字节；无基准时为 <paramref name="current"/> 本体。</returns>
        public static byte[] Diff(byte[] baseline, byte[] current)
        {
            if (current == null)
            {
                return null;
            }

            if (baseline == null || baseline.Length == 0)
            {
                return current;
            }

            var baselineReader = new SaveKeyValueReader(baseline);
            ScopeIndex baselineIndex = BuildIndex(ref baselineReader, baseline, 0);
            var currentReader = new SaveKeyValueReader(current);
            var writer = new SaveKeyValueWriter(current.Length);
            WriteScopeDiff(baselineIndex, ref currentReader, current, ref writer);
            return writer.ToArray();
        }

        /// <summary>
        /// 构建作用域索引：顺序遍历记录，记录各自的原始载荷区间。
        /// </summary>
        /// <param name="reader">作用域读取器（指向首条记录；嵌套作用域为子项切片）。</param>
        /// <param name="data">块字节（区间偏移的参照系）。</param>
        /// <param name="baseOffset">读取器切片起点在 <paramref name="data"/> 中的偏移（嵌套作用域切片非零——偏移须按全数组基准累计）。</param>
        /// <returns>作用域索引。</returns>
        private static ScopeIndex BuildIndex(ref SaveKeyValueReader reader, byte[] data, int baseOffset)
        {
            var index = new ScopeIndex
            {
                Data = data,
                Records = new Dictionary<string, RecordRef>(StringComparer.Ordinal)
            };

            int sliceLength = reader.Remaining.Length;
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                int rawStart = baseOffset + (sliceLength - reader.Remaining.Length);
                reader.SkipRecordPayload();
                int rawLength = (baseOffset + (sliceLength - reader.Remaining.Length)) - rawStart;
                // 同键后者覆盖（生成捕获器同作用域键唯一；坏档/异常输入下以最后者为准与顺序读语义一致）
                index.Records[s_Utf8.GetString(key)] = new RecordRef(type, rawStart, rawLength);
            }

            return index;
        }

        /// <summary>
        /// 写入一个作用域的差分记录（先计数扫描定总量，再逐条写出——嵌套作用域头部须先知子项数）。
        /// </summary>
        /// <param name="baselineIndex">基准作用域索引（<c>null</c> = 基准缺失，全部透传）。</param>
        /// <param name="currentReader">当前作用域读取器（指向首条记录）。</param>
        /// <param name="currentData">当前块字节。</param>
        /// <param name="writer">差分输出写入器。</param>
        private static void WriteScopeDiff(ScopeIndex baselineIndex, ref SaveKeyValueReader currentReader, byte[] currentData, ref SaveKeyValueWriter writer)
        {
            SaveKeyValueReader countCursor = currentReader;
            int diffCount = CountScopeDiff(baselineIndex, ref countCursor, currentData);

            int written = 0;
            while (written < diffCount && currentReader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                string keyText = s_Utf8.GetString(key);
                switch (EvaluateRecord(baselineIndex, keyText, type, ref currentReader, currentData))
                {
                    case ERecordVerdict.CopyRaw:
                        CopyRawRecord(keyText, type, ref currentReader, ref writer);
                        written++;
                        break;
                    case ERecordVerdict.NestedDiff:
                        WriteNestedDiff(baselineIndex.GetChild(keyText), ref currentReader, currentData, keyText, ref writer);
                        written++;
                        break;
                    default:
                        currentReader.SkipRecordPayload();
                        break;
                }
            }

            // 无论是否命中差分，本作用域剩余记录全部消费（调用方游标语义 = 作用域整体推进）
            while (currentReader.ReadRecord(out _, out _))
            {
                currentReader.SkipRecordPayload();
            }
        }

        /// <summary>
        /// 统计作用域内差分记录数（游标按值传入，调用方游标不受影响）。
        /// </summary>
        /// <param name="baselineIndex">基准作用域索引（<c>null</c> = 基准缺失，全部计为透传）。</param>
        /// <param name="cursor">当前作用域读取器游标副本（独立推进至作用域末尾）。</param>
        /// <param name="currentData">当前块字节。</param>
        /// <returns>差分记录数。</returns>
        private static int CountScopeDiff(ScopeIndex baselineIndex, ref SaveKeyValueReader cursor, byte[] currentData)
        {
            int count = 0;
            while (cursor.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                string keyText = s_Utf8.GetString(key);
                ERecordVerdict verdict = EvaluateRecord(baselineIndex, keyText, type, ref cursor, currentData);
                if (verdict != ERecordVerdict.Equal)
                {
                    count++;
                }

                // 判定不消费载荷——计数扫描统一推进
                cursor.SkipRecordPayload();
            }

            return count;
        }

        /// <summary>
        /// 判定当前记录的差分处置方式。<b>本方法不消费载荷</b>——游标停在该记录载荷起点，由调用方按判定结果推进。
        /// </summary>
        /// <param name="baselineIndex">基准作用域索引（<c>null</c> = 基准缺失，一律 <see cref="ERecordVerdict.CopyRaw"/>）。</param>
        /// <param name="keyText">记录键。</param>
        /// <param name="type">当前记录类型。</param>
        /// <param name="reader">当前作用域读取器（记录头已消费）。</param>
        /// <param name="currentData">当前块字节。</param>
        /// <returns>处置判定。</returns>
        private static ERecordVerdict EvaluateRecord(ScopeIndex baselineIndex, string keyText, ESaveKvType type, ref SaveKeyValueReader reader, byte[] currentData)
        {
            // 模式版本作用域恒透传（恢复侧迁移路由依据，不参与差分）
            if (type == ESaveKvType.Object && string.Equals(keyText, SaveComponent.SchemaScopeKey, StringComparison.Ordinal))
            {
                return ERecordVerdict.CopyRaw;
            }

            if (baselineIndex == null)
            {
                return ERecordVerdict.CopyRaw;
            }

            if (!baselineIndex.Records.TryGetValue(keyText, out RecordRef baselineRecord) || baselineRecord.Type != type)
            {
                return ERecordVerdict.CopyRaw;
            }

            if (type == ESaveKvType.Object)
            {
                // 嵌套对象：递归统计内层变动，有变动以稀疏子作用域携带
                ScopeIndex childIndex = baselineIndex.GetChild(keyText);
                if (childIndex == null)
                {
                    return ERecordVerdict.CopyRaw;
                }

                ReadOnlySpan<byte> raw = PeekRawPayload(reader, currentData);
                if (raw.Length < 8)
                {
                    throw new SaveKvFormatException("KVT nested scope payload is shorter than its header.");
                }

                var childReader = new SaveKeyValueReader(raw.Slice(8));
                return CountScopeDiff(childIndex, ref childReader, currentData) > 0
                    ? ERecordVerdict.NestedDiff
                    : ERecordVerdict.Equal;
            }

            // 标量/Null/序列/映射：原始字节规范比较（序列/映射元素级差分为 v2 范围，任一变动整条携带）
            ReadOnlySpan<byte> currentRaw = PeekRawPayload(reader, currentData);
            return currentRaw.SequenceEqual(baselineIndex.Data.AsSpan(baselineRecord.Offset, baselineRecord.Length))
                ? ERecordVerdict.Equal
                : ERecordVerdict.CopyRaw;
        }

        /// <summary>
        /// 写入嵌套对象的内层差分（当前记录头已消费；本方法完整消费该记录载荷）。
        /// </summary>
        /// <param name="childIndex">基准子作用域索引。</param>
        /// <param name="reader">当前作用域读取器（指向嵌套记录载荷）。</param>
        /// <param name="currentData">当前块字节。</param>
        /// <param name="keyText">记录键。</param>
        /// <param name="writer">差分输出写入器。</param>
        private static void WriteNestedDiff(ScopeIndex childIndex, ref SaveKeyValueReader reader, byte[] currentData, string keyText, ref SaveKeyValueWriter writer)
        {
            ReadOnlySpan<byte> raw = PeekRawPayload(reader, currentData);
            if (raw.Length < 8)
            {
                throw new SaveKvFormatException("KVT nested scope payload is shorter than its header.");
            }

            reader.SkipRecordPayload();

            var childReader = new SaveKeyValueReader(raw.Slice(8));
            SaveKeyValueReader countCursor = childReader;
            int childDiffCount = CountScopeDiff(childIndex, ref countCursor, currentData);
            writer.BeginNestedObject(keyText, childDiffCount);
            WriteScopeDiff(childIndex, ref childReader, currentData, ref writer);
            writer.EndNested();
        }

        /// <summary>
        /// 原始透传当前记录（记录头已消费；本方法完整消费该记录载荷并原样写回）。
        /// </summary>
        /// <param name="keyText">记录键。</param>
        /// <param name="type">记录类型。</param>
        /// <param name="reader">当前作用域读取器。</param>
        /// <param name="writer">差分输出写入器。</param>
        private static void CopyRawRecord(string keyText, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer)
        {
            ReadOnlySpan<byte> before = reader.Remaining;
            reader.SkipRecordPayload();
            ReadOnlySpan<byte> raw = before.Slice(0, before.Length - reader.Remaining.Length);
            writer.WriteRawRecord(keyText, type, raw);
        }

        /// <summary>
        /// 窥视当前记录的原始载荷区间（[4B 载荷长][载荷]；不推进游标——经长度前缀校验边界）。
        /// </summary>
        /// <param name="reader">当前作用域读取器（记录头已消费）。</param>
        /// <param name="currentData">当前块字节。</param>
        /// <returns>原始载荷区间。</returns>
        private static ReadOnlySpan<byte> PeekRawPayload(SaveKeyValueReader reader, byte[] currentData)
        {
            ReadOnlySpan<byte> remaining = reader.Remaining;
            if (remaining.Length < 4)
            {
                throw new SaveKvFormatException("KVT payload length header is truncated.");
            }

            int payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(remaining);
            int rawLength = 4 + payloadLength;
            if (payloadLength < 0 || rawLength > remaining.Length)
            {
                throw new SaveKvFormatException(StringUtility.Format("KVT payload length {0} exceeds remaining data {1}.", payloadLength, remaining.Length - 4));
            }

            return remaining.Slice(0, rawLength);
        }
    }
}

