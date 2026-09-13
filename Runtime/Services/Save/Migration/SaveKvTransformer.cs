using System;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// KVT 块载荷迁移变换器（纯函数）：对键值记录流做全量重写，命中规则的记录按键改名/装箱改型，未命中记录原始字节透传。
    /// <para>规则作用于全部对象级记录键（含嵌套对象作用域内的字段键——嵌套 <see cref="SaveDataAttribute"/> 类的字段键同名时一并改名）；
    /// 序列/映射作用域原样透传不下钻（集合元素无键，且当前写入侧不产生元素级嵌套对象）。</para>
    /// <para>仅供迁移总线在加载/写入管线调用（每档一次，非热路径）；格式损坏抛 <see cref="SaveKvFormatException"/> 由调用方归一为迁移失败。</para>
    /// </summary>
    internal static class SaveKvTransformer
    {
        /// <summary>UTF-8 编解码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>记录变换规则（返回 <c>true</c> = 规则已消费该记录，调用方不再透传）。</summary>
        private delegate bool RecordRule(string key, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer);

        /// <summary>
        /// 将块载荷中名为 <paramref name="oldField"/> 的记录键改名为 <paramref name="newField"/>（全作用域递归）。
        /// </summary>
        /// <param name="source">源块载荷。</param>
        /// <param name="oldField">旧字段键。</param>
        /// <param name="newField">新字段键。</param>
        /// <param name="result">变换后的块载荷（未命中时透传为源引用拷贝）。</param>
        /// <returns>存在命中记录返回 <c>true</c>。</returns>
        public static bool RenameField(byte[] source, string oldField, string newField, out byte[] result)
        {
            bool hit = false;
            result = Rewrite(source, (string key, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer) =>
            {
                // 改名在键解析层统一生效（嵌套/集合经透传保留原键或新键），此处不消费标量载荷
                return false;
            }, oldField, newField, ref hit);
            return hit;
        }

        /// <summary>
        /// 将块载荷中名为 <paramref name="field"/> 的标量记录改型（装箱读出旧值 → 转换 → 按 <paramref name="newType"/> 写回；全作用域递归）。
        /// <para>嵌套对象/序列/映射记录不支持改型（命中时抛 <see cref="SaveKvFormatException"/>）；Null 记录以 null 入转换器。</para>
        /// </summary>
        /// <param name="source">源块载荷。</param>
        /// <param name="field">目标字段键。</param>
        /// <param name="convert">值转换器（入参为旧类型装箱值或 null，返回新类型装箱值）。</param>
        /// <param name="newType">新值 KVT 记录类型。</param>
        /// <param name="result">变换后的块载荷。</param>
        /// <returns>存在命中记录返回 <c>true</c>。</returns>
        public static bool RetypeField(byte[] source, string field, Func<object, object> convert, ESaveKvType newType, out byte[] result)
        {
            bool hit = false;
            result = Rewrite(source, (string key, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer) =>
            {
                if (!string.Equals(key, field, StringComparison.Ordinal))
                {
                    return false;
                }

                if (type == ESaveKvType.Object || type == ESaveKvType.Sequence || type == ESaveKvType.Map)
                {
                    throw new SaveKvFormatException(StringUtility.Format("KVT field '{0}' is a nested scope, retype is only supported for scalar records.", field));
                }

                object oldValue = SaveKvBoxed.Read(ref reader, type);
                object newValue = convert(oldValue);
                SaveKvBoxed.Write(ref writer, key, newType, newValue);
                return true;
            }, null, null, ref hit);
            return hit;
        }

        /// <summary>
        /// 全量重写块载荷：逐记录应用改名映射与变换规则，未消费记录原始透传。
        /// </summary>
        /// <param name="source">源块载荷。</param>
        /// <param name="rule">标量记录变换规则。</param>
        /// <param name="renameFrom">键改名旧名（null = 不改名）。</param>
        /// <param name="renameTo">键改名新名。</param>
        /// <param name="hit">是否存在规则/改名命中。</param>
        /// <returns>重写后的块载荷。</returns>
        private static byte[] Rewrite(byte[] source, RecordRule rule, string renameFrom, string renameTo, ref bool hit)
        {
            var reader = new SaveKeyValueReader(source);
            var writer = new SaveKeyValueWriter(source.Length + 16);
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                RewriteRecord(s_Utf8.GetString(key), type, ref reader, ref writer, rule, renameFrom, renameTo, ref hit);
            }

            return writer.ToArray();
        }

        /// <summary>
        /// 重写单条记录（嵌套对象递归；序列/映射透传）。
        /// </summary>
        private static void RewriteRecord(string keyText, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer, RecordRule rule, string renameFrom, string renameTo, ref bool hit)
        {
            string outKey = keyText;
            if (renameFrom != null && string.Equals(keyText, renameFrom, StringComparison.Ordinal))
            {
                outKey = renameTo;
                hit = true;
            }

            if (type == ESaveKvType.Object)
            {
                int childCount = reader.ReadChildCount();
                writer.BeginNestedObject(outKey, childCount);
                for (int i = 0; i < childCount; i++)
                {
                    if (!reader.ReadRecord(out ReadOnlySpan<byte> childKey, out ESaveKvType childType))
                    {
                        throw new SaveKvFormatException("KVT nested scope ended before declared child count.");
                    }

                    RewriteRecord(s_Utf8.GetString(childKey), childType, ref reader, ref writer, rule, renameFrom, renameTo, ref hit);
                }

                writer.EndNested();
                return;
            }

            if (type == ESaveKvType.Sequence || type == ESaveKvType.Map)
            {
                // 集合作用域原样透传（元素无键可改；元素级嵌套对象当前写入侧不产生）
                PassThroughRaw(outKey, type, ref reader, ref writer);
                return;
            }

            if (rule(keyText, type, ref reader, ref writer))
            {
                hit = true;
                return;
            }

            PassThroughRaw(outKey, type, ref reader, ref writer);
        }

        /// <summary>
        /// 透传当前记录（记录头已消费）：捕获 [4B 载荷长][载荷] 原始字节区间并以（可能已改名的）键写回。
        /// </summary>
        private static void PassThroughRaw(string key, ESaveKvType type, ref SaveKeyValueReader reader, ref SaveKeyValueWriter writer)
        {
            ReadOnlySpan<byte> before = reader.Remaining;
            reader.SkipRecordPayload();
            ReadOnlySpan<byte> raw = before.Slice(0, before.Length - reader.Remaining.Length);
            writer.WriteRawRecord(key, type, raw);
        }
    }

    /// <summary>
    /// KVT 标量载荷装箱桥接（迁移改型专用）：记录类型 ↔ CLR 装箱值读出/写回。
    /// <para>装箱分配仅发生在迁移期（每档每字段一次），非热路径。</para>
    /// </summary>
    internal static class SaveKvBoxed
    {
        /// <summary>
        /// 解析 CLR 类型对应的 KVT 记录类型（枚举归一到底层类型）。
        /// </summary>
        /// <param name="clrType">CLR 类型。</param>
        /// <param name="type">对应的 KVT 记录类型。</param>
        /// <returns>支持该类型返回 <c>true</c>。</returns>
        public static bool TryGetKvType(Type clrType, out ESaveKvType type)
        {
            Type target = clrType.IsEnum ? Enum.GetUnderlyingType(clrType) : clrType;
            switch (Type.GetTypeCode(target))
            {
                case TypeCode.Boolean: type = ESaveKvType.Bool; return true;
                case TypeCode.SByte: type = ESaveKvType.SByte; return true;
                case TypeCode.Byte: type = ESaveKvType.Byte; return true;
                case TypeCode.Int16: type = ESaveKvType.Int16; return true;
                case TypeCode.UInt16: type = ESaveKvType.UInt16; return true;
                case TypeCode.Int32: type = ESaveKvType.Int32; return true;
                case TypeCode.UInt32: type = ESaveKvType.UInt32; return true;
                case TypeCode.Int64: type = ESaveKvType.Int64; return true;
                case TypeCode.UInt64: type = ESaveKvType.UInt64; return true;
                case TypeCode.Single: type = ESaveKvType.Single; return true;
                case TypeCode.Double: type = ESaveKvType.Double; return true;
                case TypeCode.Decimal: type = ESaveKvType.Decimal; return true;
                case TypeCode.Char: type = ESaveKvType.Char; return true;
                case TypeCode.String: type = ESaveKvType.String; return true;
                case TypeCode.DateTime: type = ESaveKvType.DateTime; return true;
                default:
                    break;
            }

            if (target == typeof(TimeSpan)) { type = ESaveKvType.TimeSpan; return true; }
            if (target == typeof(Vector2)) { type = ESaveKvType.Vector2; return true; }
            if (target == typeof(Vector3)) { type = ESaveKvType.Vector3; return true; }
            if (target == typeof(Vector4)) { type = ESaveKvType.Vector4; return true; }
            if (target == typeof(Quaternion)) { type = ESaveKvType.Quaternion; return true; }
            if (target == typeof(Color)) { type = ESaveKvType.Color; return true; }
            if (target == typeof(Rect)) { type = ESaveKvType.Rect; return true; }
            if (target == typeof(Bounds)) { type = ESaveKvType.Bounds; return true; }

            type = default;
            return false;
        }

        /// <summary>
        /// 按记录类型装箱读出载荷值（Null 记录返回 null）。
        /// </summary>
        /// <param name="reader">键值读取器（记录头已消费）。</param>
        /// <param name="type">记录类型。</param>
        /// <returns>装箱值。</returns>
        public static object Read(ref SaveKeyValueReader reader, ESaveKvType type)
        {
            switch (type)
            {
                case ESaveKvType.Null: reader.SkipRecordPayload(); return null;
                case ESaveKvType.Bool: return reader.ReadBoolean();
                case ESaveKvType.SByte: return reader.ReadSByte();
                case ESaveKvType.Byte: return reader.ReadByte();
                case ESaveKvType.Int16: return reader.ReadInt16();
                case ESaveKvType.UInt16: return reader.ReadUInt16();
                case ESaveKvType.Int32: return reader.ReadInt32();
                case ESaveKvType.UInt32: return reader.ReadUInt32();
                case ESaveKvType.Int64: return reader.ReadInt64();
                case ESaveKvType.UInt64: return reader.ReadUInt64();
                case ESaveKvType.Single: return reader.ReadSingle();
                case ESaveKvType.Double: return reader.ReadDouble();
                case ESaveKvType.Decimal: return reader.ReadDecimal();
                case ESaveKvType.Char: return reader.ReadChar();
                case ESaveKvType.String: return reader.ReadString();
                case ESaveKvType.DateTime: return reader.ReadDateTime();
                case ESaveKvType.TimeSpan: return reader.ReadTimeSpan();
                case ESaveKvType.Vector2: return reader.ReadVector2();
                case ESaveKvType.Vector3: return reader.ReadVector3();
                case ESaveKvType.Vector4: return reader.ReadVector4();
                case ESaveKvType.Quaternion: return reader.ReadQuaternion();
                case ESaveKvType.Color: return reader.ReadColor();
                case ESaveKvType.Rect: return reader.ReadRect();
                case ESaveKvType.Bounds: return reader.ReadBounds();
                default:
                    throw new SaveKvFormatException(StringUtility.Format("KVT type '{0}' cannot be boxed.", type));
            }
        }

        /// <summary>
        /// 按记录类型装箱写回载荷值（null 值写为 Null 记录；类型与值不符抛异常）。
        /// </summary>
        /// <param name="writer">键值写入器。</param>
        /// <param name="key">记录键。</param>
        /// <param name="type">记录类型。</param>
        /// <param name="value">装箱值（须与记录类型匹配；枚举按底层类型传入装箱值）。</param>
        public static void Write(ref SaveKeyValueWriter writer, string key, ESaveKvType type, object value)
        {
            if (value == null)
            {
                writer.WriteNull(key);
                return;
            }

            switch (type)
            {
                case ESaveKvType.Bool: writer.WriteBoolean(key, (bool)value); return;
                case ESaveKvType.SByte: writer.WriteSByte(key, Convert.ToSByte(value)); return;
                case ESaveKvType.Byte: writer.WriteByte(key, Convert.ToByte(value)); return;
                case ESaveKvType.Int16: writer.WriteInt16(key, Convert.ToInt16(value)); return;
                case ESaveKvType.UInt16: writer.WriteUInt16(key, Convert.ToUInt16(value)); return;
                case ESaveKvType.Int32: writer.WriteInt32(key, Convert.ToInt32(value)); return;
                case ESaveKvType.UInt32: writer.WriteUInt32(key, Convert.ToUInt32(value)); return;
                case ESaveKvType.Int64: writer.WriteInt64(key, Convert.ToInt64(value)); return;
                case ESaveKvType.UInt64: writer.WriteUInt64(key, Convert.ToUInt64(value)); return;
                case ESaveKvType.Single: writer.WriteSingle(key, Convert.ToSingle(value)); return;
                case ESaveKvType.Double: writer.WriteDouble(key, Convert.ToDouble(value)); return;
                case ESaveKvType.Decimal: writer.WriteDecimal(key, (decimal)value); return;
                case ESaveKvType.Char: writer.WriteChar(key, (char)value); return;
                case ESaveKvType.String: writer.WriteString(key, (string)value); return;
                case ESaveKvType.DateTime: writer.WriteDateTime(key, (DateTime)value); return;
                case ESaveKvType.TimeSpan: writer.WriteTimeSpan(key, (TimeSpan)value); return;
                case ESaveKvType.Vector2: writer.WriteVector2(key, (Vector2)value); return;
                case ESaveKvType.Vector3: writer.WriteVector3(key, (Vector3)value); return;
                case ESaveKvType.Vector4: writer.WriteVector4(key, (Vector4)value); return;
                case ESaveKvType.Quaternion: writer.WriteQuaternion(key, (Quaternion)value); return;
                case ESaveKvType.Color: writer.WriteColor(key, (Color)value); return;
                case ESaveKvType.Rect: writer.WriteRect(key, (Rect)value); return;
                case ESaveKvType.Bounds: writer.WriteBounds(key, (Bounds)value); return;
                default:
                    throw new SaveKvFormatException(StringUtility.Format("KVT type '{0}' cannot be written from boxed value.", type));
            }
        }
    }
}
