using System;
using System.Globalization;
using System.Text;
using Moirai.Atropos.Save;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// KVT 结构化预览格式化器：把键值捕获字节解析为缩进树文本（存档浏览器预览面板用）。
    /// <para>解析失败返回 <c>null</c>（调用方回退十六进制采样）；行数/深度上限截断防巨型块卡死 UI。</para>
    /// </summary>
    internal static class SaveKvPreviewFormatter
    {
        /// <summary>键名 UTF8 解码器（与 KVT 格式约定一致，无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>输出总行数上限（超出追加截断标记）。</summary>
        private const int MAX_LINES = 400;

        /// <summary>嵌套深度上限（超出消费不渲染）。</summary>
        private const int MAX_DEPTH = 12;

        /// <summary>
        /// 将 KVT 块载荷格式化为缩进树文本。
        /// </summary>
        /// <param name="bytes">KVT 块载荷字节。</param>
        /// <returns>树形文本；载荷非法或为空为 <c>null</c>。</returns>
        internal static string Format(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var reader = new SaveKeyValueReader(bytes);
            var builder = new StringBuilder(Math.Min(bytes.Length * 2, 64 * 1024));
            int lines = 0;
            try
            {
                while (lines < MAX_LINES && reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
                {
                    AppendRecord(ref reader, builder, s_Utf8.GetString(key), type, 0, ref lines);
                }
            }
            catch (Exception)
            {
                return null; // 载荷非法——调用方回退十六进制采样
            }

            if (lines >= MAX_LINES)
            {
                builder.Append("… (truncated at ").Append(MAX_LINES).AppendLine(" lines)");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 追加一条对象级记录（键 + 载荷）。
        /// </summary>
        private static void AppendRecord(ref SaveKeyValueReader reader, StringBuilder builder, string key, ESaveKvType type, int depth, ref int lines)
        {
            AppendIndent(builder, depth);
            builder.Append(key).Append(": ");
            lines++;
            AppendPayload(ref reader, builder, type, depth, ref lines);
            builder.Append('\n');
        }

        /// <summary>
        /// 追加一条集合元素记录（无键，"- " 前缀）。
        /// </summary>
        private static void AppendElement(ref SaveKeyValueReader reader, StringBuilder builder, ESaveKvType type, int depth, ref int lines)
        {
            AppendIndent(builder, depth);
            builder.Append("- ");
            lines++;
            AppendPayload(ref reader, builder, type, depth, ref lines);
            builder.Append('\n');
        }

        /// <summary>
        /// 追加载荷（标量直写；容器递归消费子项）。
        /// </summary>
        private static void AppendPayload(ref SaveKeyValueReader reader, StringBuilder builder, ESaveKvType type, int depth, ref int lines)
        {
            switch (type)
            {
                case ESaveKvType.Null:
                    builder.Append("null");
                    return;
                case ESaveKvType.Bool:
                    builder.Append(reader.ReadBoolean() ? "true" : "false");
                    return;
                case ESaveKvType.SByte:
                    builder.Append(reader.ReadSByte().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Byte:
                    builder.Append(reader.ReadByte().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Int16:
                    builder.Append(reader.ReadInt16().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.UInt16:
                    builder.Append(reader.ReadUInt16().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Int32:
                    builder.Append(reader.ReadInt32().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.UInt32:
                    builder.Append(reader.ReadUInt32().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Int64:
                    builder.Append(reader.ReadInt64().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.UInt64:
                    builder.Append(reader.ReadUInt64().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Single:
                    builder.Append(reader.ReadSingle().ToString("G9", CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Double:
                    builder.Append(reader.ReadDouble().ToString("G17", CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Decimal:
                    builder.Append(reader.ReadDecimal().ToString(CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.Char:
                    builder.Append('\'').Append(reader.ReadChar()).Append('\'');
                    return;
                case ESaveKvType.String:
                    builder.Append('"').Append(reader.ReadString()).Append('"');
                    return;
                case ESaveKvType.DateTime:
                    builder.Append(reader.ReadDateTime().ToString("o", CultureInfo.InvariantCulture));
                    return;
                case ESaveKvType.TimeSpan:
                    builder.Append(reader.ReadTimeSpan().ToString());
                    return;
                case ESaveKvType.Vector2:
                    var v2 = reader.ReadVector2();
                    AppendVector(builder, v2.x, v2.y);
                    return;
                case ESaveKvType.Vector3:
                    var v3 = reader.ReadVector3();
                    AppendVector(builder, v3.x, v3.y, v3.z);
                    return;
                case ESaveKvType.Vector4:
                    var v4 = reader.ReadVector4();
                    AppendVector(builder, v4.x, v4.y, v4.z, v4.w);
                    return;
                case ESaveKvType.Quaternion:
                    var q = reader.ReadQuaternion();
                    AppendVector(builder, q.x, q.y, q.z, q.w);
                    return;
                case ESaveKvType.Color:
                    var c = reader.ReadColor();
                    AppendVector(builder, c.r, c.g, c.b, c.a);
                    return;
                case ESaveKvType.Rect:
                    var rect = reader.ReadRect();
                    builder.Append("(x:").Append(rect.x.ToString("G9", CultureInfo.InvariantCulture))
                        .Append(", y:").Append(rect.y.ToString("G9", CultureInfo.InvariantCulture))
                        .Append(", w:").Append(rect.width.ToString("G9", CultureInfo.InvariantCulture))
                        .Append(", h:").Append(rect.height.ToString("G9", CultureInfo.InvariantCulture)).Append(')');
                    return;
                case ESaveKvType.Bounds:
                    var bounds = reader.ReadBounds();
                    builder.Append("center");
                    AppendVector(builder, bounds.center.x, bounds.center.y, bounds.center.z);
                    builder.Append(" extents");
                    AppendVector(builder, bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    return;
                case ESaveKvType.Sequence:
                    AppendSequence(ref reader, builder, depth, ref lines);
                    return;
                case ESaveKvType.Map:
                    AppendMap(ref reader, builder, depth, ref lines);
                    return;
                case ESaveKvType.Object:
                    AppendObject(ref reader, builder, depth, ref lines);
                    return;
                default:
                    // 未知类型（未来扩展）——跳过载荷保对齐
                    builder.Append('<').Append(type).Append('>');
                    reader.SkipRecordPayload();
                    return;
            }
        }

        /// <summary>
        /// 追加嵌套对象（[4B 子项数][对象级记录…]）。
        /// </summary>
        private static void AppendObject(ref SaveKeyValueReader reader, StringBuilder builder, int depth, ref int lines)
        {
            int childCount = reader.ReadChildCount();
            builder.Append("{ // ").Append(childCount).Append(childCount == 1 ? " field" : " fields");
            if (depth >= MAX_DEPTH)
            {
                ConsumeRecords(ref reader, childCount);
                builder.Append(" … }");
                return;
            }

            builder.Append('\n');
            for (int i = 0; i < childCount && lines < MAX_LINES; i++)
            {
                reader.ReadRecord(out ReadOnlySpan<byte> childKey, out ESaveKvType childType);
                AppendRecord(ref reader, builder, s_Utf8.GetString(childKey), childType, depth + 1, ref lines);
            }

            AppendIndent(builder, depth);
            builder.Append('}');
        }

        /// <summary>
        /// 追加序列（[4B 元素数][元素记录…]）。
        /// </summary>
        private static void AppendSequence(ref SaveKeyValueReader reader, StringBuilder builder, int depth, ref int lines)
        {
            int elementCount = reader.ReadChildCount();
            builder.Append("[ // ").Append(elementCount).Append(elementCount == 1 ? " element" : " elements");
            if (depth >= MAX_DEPTH)
            {
                for (int i = 0; i < elementCount; i++)
                {
                    reader.ReadElement(out _);
                    reader.SkipRecordPayload();
                }

                builder.Append(" … ]");
                return;
            }

            builder.Append('\n');
            for (int i = 0; i < elementCount && lines < MAX_LINES; i++)
            {
                reader.ReadElement(out ESaveKvType elementType);
                AppendElement(ref reader, builder, elementType, depth + 1, ref lines);
            }

            AppendIndent(builder, depth);
            builder.Append(']');
        }

        /// <summary>
        /// 追加映射（[4B 键值对数][键记录][值记录]…；键按标量渲染）。
        /// </summary>
        private static void AppendMap(ref SaveKeyValueReader reader, StringBuilder builder, int depth, ref int lines)
        {
            int pairCount = reader.ReadChildCount();
            builder.Append("{ // ").Append(pairCount).Append(pairCount == 1 ? " pair" : " pairs");
            if (depth >= MAX_DEPTH)
            {
                for (int i = 0; i < pairCount * 2; i++)
                {
                    reader.ReadElement(out _);
                    reader.SkipRecordPayload();
                }

                builder.Append(" … }");
                return;
            }

            builder.Append('\n');
            for (int i = 0; i < pairCount && lines < MAX_LINES; i++)
            {
                reader.ReadElement(out ESaveKvType keyType);
                string keyText = ReadScalarAsText(ref reader, keyType);
                reader.ReadElement(out ESaveKvType valueType);
                AppendIndent(builder, depth + 1);
                builder.Append(keyText).Append(": ");
                lines++;
                AppendPayload(ref reader, builder, valueType, depth + 1, ref lines);
                builder.Append('\n');
            }

            AppendIndent(builder, depth);
            builder.Append('}');
        }

        /// <summary>
        /// 读取标量元素为文本（映射键渲染用；非标量消费后占位）。
        /// </summary>
        private static string ReadScalarAsText(ref SaveKeyValueReader reader, ESaveKvType type)
        {
            switch (type)
            {
                case ESaveKvType.Null: return "null";
                case ESaveKvType.Bool: return reader.ReadBoolean() ? "true" : "false";
                case ESaveKvType.SByte: return reader.ReadSByte().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Byte: return reader.ReadByte().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Int16: return reader.ReadInt16().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.UInt16: return reader.ReadUInt16().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Int32: return reader.ReadInt32().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.UInt32: return reader.ReadUInt32().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Int64: return reader.ReadInt64().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.UInt64: return reader.ReadUInt64().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Single: return reader.ReadSingle().ToString("G9", CultureInfo.InvariantCulture);
                case ESaveKvType.Double: return reader.ReadDouble().ToString("G17", CultureInfo.InvariantCulture);
                case ESaveKvType.Decimal: return reader.ReadDecimal().ToString(CultureInfo.InvariantCulture);
                case ESaveKvType.Char: return "'" + reader.ReadChar() + "'";
                case ESaveKvType.String: return "\"" + reader.ReadString() + "\"";
                case ESaveKvType.DateTime: return reader.ReadDateTime().ToString("o", CultureInfo.InvariantCulture);
                case ESaveKvType.TimeSpan: return reader.ReadTimeSpan().ToString();
                default:
                    // 非标量键（容器/向量族）——消费载荷占位（编辑器预览不展开）
                    reader.SkipRecordPayload();
                    return "<" + type + ">";
            }
        }

        /// <summary>
        /// 消费指定数量的对象级记录（深度上限占位渲染用）。
        /// </summary>
        private static void ConsumeRecords(ref SaveKeyValueReader reader, int count)
        {
            for (int i = 0; i < count; i++)
            {
                reader.ReadRecord(out _, out _);
                reader.SkipRecordPayload();
            }
        }

        /// <summary>
        /// 追加缩进（每级两空格）。
        /// </summary>
        private static void AppendIndent(StringBuilder builder, int depth)
        {
            for (int i = 0; i < depth; i++)
            {
                builder.Append("  ");
            }
        }

        /// <summary>
        /// 追加向量族（分量按 G9 不变格式）。
        /// </summary>
        private static void AppendVector(StringBuilder builder, params float[] components)
        {
            builder.Append('(');
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(components[i].ToString("G9", CultureInfo.InvariantCulture));
            }

            builder.Append(')');
        }
    }
}
