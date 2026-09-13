using System;
using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 实体生成记录：一个动态实体的持久化身份（稳定 ID + 预制体注册键 + 所属场景 + 父对象 ID）。
    /// <para>位置/旋转等运行态不入本表——由实体块（<c>entity:{EntityId}</c>）的组件差分承载。</para>
    /// </summary>
    public readonly struct SaveSpawnRecord
    {
        /// <summary>
        /// 实体稳定标识。
        /// </summary>
        public string EntityId { get; }

        /// <summary>
        /// 预制体注册键（<see cref="SavePrefabRegistry"/>）。
        /// </summary>
        public string PrefabKey { get; }

        /// <summary>
        /// 所属场景名（恢复时同名场景已加载则落位其中，否则落位活跃场景并记告警）。
        /// </summary>
        public string SceneName { get; }

        /// <summary>
        /// 父对象稳定 ID（空 = 场景根；恢复第二轮接线，指向另一实体或预置对象均可）。
        /// </summary>
        public string ParentId { get; }

        /// <summary>
        /// 创建实体生成记录。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="sceneName">所属场景名。</param>
        /// <param name="parentId">父对象稳定 ID（<c>null</c> = 场景根）。</param>
        public SaveSpawnRecord(string entityId, string prefabKey, string sceneName, string parentId)
        {
            EntityId = entityId;
            PrefabKey = prefabKey;
            SceneName = sceneName;
            ParentId = parentId;
        }
    }

    /// <summary>
    /// 实体表块（保留块 <c>__entities</c>）的 KVT 读写（纯函数）。
    /// <para>布局：<c>spawns</c> 序列（元素 = 嵌套对象：id/prefab/scene/parent 四键，可空键写 Null）+
    /// <c>destroyed</c> 序列（字符串元素）。读侧键匹配容错（未知键跳过、缺失键按默认）。</para>
    /// </summary>
    internal static class SaveEntityTable
    {
        /// <summary>记录键：生成记录序列。</summary>
        internal const string SpawnsKey = "spawns";

        /// <summary>记录键：销毁 ID 序列。</summary>
        internal const string DestroyedKey = "destroyed";

        /// <summary>字段键：实体 ID。</summary>
        internal const string IdKey = "id";

        /// <summary>字段键：预制体注册键。</summary>
        internal const string PrefabKey = "prefab";

        /// <summary>字段键：场景名。</summary>
        internal const string SceneKey = "scene";

        /// <summary>字段键：父对象 ID。</summary>
        internal const string ParentKey = "parent";

        /// <summary>UTF-8 解码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 序列化实体表为 KVT 块字节。
        /// </summary>
        /// <param name="spawns">生成记录列表。</param>
        /// <param name="destroyedIds">预置对象销毁 ID 列表。</param>
        /// <returns>KVT 块字节。</returns>
        public static byte[] Write(IReadOnlyList<SaveSpawnRecord> spawns, IReadOnlyList<string> destroyedIds)
        {
            var writer = new SaveKeyValueWriter(128);

            writer.BeginSequence(SpawnsKey, spawns?.Count ?? 0);
            if (spawns != null)
            {
                for (int i = 0; i < spawns.Count; i++)
                {
                    SaveSpawnRecord record = spawns[i];
                    writer.BeginNestedObjectElement(4);
                    WriteStringOrNull(ref writer, IdKey, record.EntityId);
                    WriteStringOrNull(ref writer, PrefabKey, record.PrefabKey);
                    WriteStringOrNull(ref writer, SceneKey, record.SceneName);
                    WriteStringOrNull(ref writer, ParentKey, record.ParentId);
                    writer.EndNested();
                }
            }

            writer.EndNested();

            writer.BeginSequence(DestroyedKey, destroyedIds?.Count ?? 0);
            if (destroyedIds != null)
            {
                for (int i = 0; i < destroyedIds.Count; i++)
                {
                    if (destroyedIds[i] == null)
                    {
                        writer.WriteNullElement();
                    }
                    else
                    {
                        writer.WriteStringElement(destroyedIds[i]);
                    }
                }
            }

            writer.EndNested();
            return writer.ToArray();
        }

        /// <summary>
        /// 从 KVT 块字节反序列化实体表（键匹配容错：未知记录跳过、缺失序列按空表）。
        /// </summary>
        /// <param name="bytes">KVT 块字节（<c>null</c>/空 = 空表）。</param>
        /// <param name="spawns">解析出的生成记录列表。</param>
        /// <param name="destroyedIds">解析出的销毁 ID 列表。</param>
        public static void Read(byte[] bytes, out List<SaveSpawnRecord> spawns, out List<string> destroyedIds)
        {
            spawns = new List<SaveSpawnRecord>();
            destroyedIds = new List<string>();
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            var reader = new SaveKeyValueReader(bytes);
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                if (type != ESaveKvType.Sequence)
                {
                    reader.SkipRecordPayload();
                    continue;
                }

                if (KeyEquals(key, SpawnsKey))
                {
                    ReadSpawns(ref reader, spawns);
                    continue;
                }

                if (KeyEquals(key, DestroyedKey))
                {
                    ReadDestroyed(ref reader, destroyedIds);
                    continue;
                }

                reader.SkipRecordPayload();
            }
        }

        /// <summary>
        /// 读生成记录序列（记录头已消费）。
        /// </summary>
        /// <param name="reader">键值读取器。</param>
        /// <param name="spawns">输出列表。</param>
        private static void ReadSpawns(ref SaveKeyValueReader reader, List<SaveSpawnRecord> spawns)
        {
            int elementCount = reader.ReadChildCount();
            for (int i = 0; i < elementCount; i++)
            {
                if (!reader.ReadElement(out ESaveKvType elementType) || elementType != ESaveKvType.Object)
                {
                    if (elementType != ESaveKvType.Object)
                    {
                        reader.SkipRecordPayload();
                    }

                    continue;
                }

                spawns.Add(ReadSpawnRecord(ref reader));
            }
        }

        /// <summary>
        /// 读单条生成记录（元素头已消费；字段键匹配容错）。
        /// </summary>
        /// <param name="reader">键值读取器。</param>
        /// <returns>生成记录。</returns>
        private static SaveSpawnRecord ReadSpawnRecord(ref SaveKeyValueReader reader)
        {
            int fieldCount = reader.ReadChildCount();
            string entityId = null;
            string prefabKey = null;
            string sceneName = null;
            string parentId = null;
            for (int i = 0; i < fieldCount; i++)
            {
                if (!reader.ReadRecord(out ReadOnlySpan<byte> fieldKey, out ESaveKvType fieldType))
                {
                    break;
                }

                if (fieldType == ESaveKvType.String)
                {
                    if (KeyEquals(fieldKey, IdKey))
                    {
                        entityId = reader.ReadString();
                        continue;
                    }

                    if (KeyEquals(fieldKey, PrefabKey))
                    {
                        prefabKey = reader.ReadString();
                        continue;
                    }

                    if (KeyEquals(fieldKey, SceneKey))
                    {
                        sceneName = reader.ReadString();
                        continue;
                    }

                    if (KeyEquals(fieldKey, ParentKey))
                    {
                        parentId = reader.ReadString();
                        continue;
                    }
                }

                reader.SkipRecordPayload();
            }

            return new SaveSpawnRecord(entityId, prefabKey, sceneName, parentId);
        }

        /// <summary>
        /// 读销毁 ID 序列（记录头已消费）。
        /// </summary>
        /// <param name="reader">键值读取器。</param>
        /// <param name="destroyedIds">输出列表。</param>
        private static void ReadDestroyed(ref SaveKeyValueReader reader, List<string> destroyedIds)
        {
            int elementCount = reader.ReadChildCount();
            for (int i = 0; i < elementCount; i++)
            {
                if (!reader.ReadElement(out ESaveKvType elementType))
                {
                    break;
                }

                if (elementType == ESaveKvType.String)
                {
                    destroyedIds.Add(reader.ReadString());
                }
                else
                {
                    reader.SkipRecordPayload();
                }
            }
        }

        /// <summary>
        /// 写字符串字段（<c>null</c> 写 Null 记录）。
        /// </summary>
        /// <param name="writer">键值写入器。</param>
        /// <param name="key">字段键。</param>
        /// <param name="value">字符串值。</param>
        private static void WriteStringOrNull(ref SaveKeyValueWriter writer, string key, string value)
        {
            if (value == null)
            {
                writer.WriteNull(key);
            }
            else
            {
                writer.WriteString(key, value);
            }
        }

        /// <summary>
        /// 键匹配（UTF-8 解码比较）。
        /// </summary>
        /// <param name="key">记录键（UTF-8 字节跨度）。</param>
        /// <param name="expected">期望键名。</param>
        /// <returns>匹配返回 <c>true</c>。</returns>
        private static bool KeyEquals(ReadOnlySpan<byte> key, string expected)
        {
            return string.Equals(s_Utf8.GetString(key), expected, StringComparison.Ordinal);
        }
    }
}
