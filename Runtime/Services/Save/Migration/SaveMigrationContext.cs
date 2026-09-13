using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档迁移上下文（<see cref="ISaveMigrator.Migrate"/> 的纯数据操作面）。
    /// <para>操作对象为当前迁移步的存档块集合：块级 <see cref="RenameBlock"/>/<see cref="DeleteBlock"/>/<see cref="TransformBlock{T}"/>；
    /// 字段级 <see cref="RenameField(string, string, string)"/>/<see cref="RetypeField{TOld, TNew}(string, string, Func{TOld, TNew})"/>
    /// 按块记录的后端分发——JSON 后端走 DOM 变换（需 Newtonsoft.Json），KeyValue 后端走 KVT 记录重写，
    /// 二进制后端（MessagePack/MemoryPack/Protobuf）的字段级操作不受支持（拒绝并记告警；用 <see cref="TransformBlock{T}"/> 保留旧类型整对象迁移，
    /// 键序纪律由分析器 MIRAI400/401 把关）。</para>
    /// <para>目标块/字段不存在时操作为无操作（返回 <c>false</c>，兼容从未写过该块的旧档）；
    /// 反序列化失败/格式损坏等真异常记为迁移失败并中止整条迁移链。</para>
    /// </summary>
    public sealed class SaveMigrationContext
    {
#if NEWTONSOFT_JSON_INSTALLED
        private const bool JsonFieldOpsAvailable = true;
#else
        private const bool JsonFieldOpsAvailable = false;
#endif

        /// <summary>当前迁移步的起始版本（存档加载时的数据版本）。</summary>
        private readonly int _fromVersion;

        /// <summary>当前迁移步的目标版本。</summary>
        private readonly int _toVersion;

        /// <summary>存档文件名（日志上下文；手工构造路径集合时为 null）。</summary>
        private readonly string _fileName;

        /// <summary>存档文件夹名称（日志上下文）。</summary>
        private readonly string _folderName;

        /// <summary>当前块集合（迁移器就地改写；管理器在链末取回）。</summary>
        private List<SaveBlockEntry> _blocks;

        /// <summary>迁移失败明细（首个真异常；null = 未失败）。</summary>
        private string _errorDetail;

        /// <summary>
        /// 创建迁移上下文（仅迁移管理器调用）。
        /// </summary>
        internal SaveMigrationContext(List<SaveBlockEntry> blocks, int fromVersion, int toVersion, string fileName, string folderName)
        {
            _blocks = blocks;
            _fromVersion = fromVersion;
            _toVersion = toVersion;
            _fileName = fileName;
            _folderName = folderName;
        }

        /// <summary>
        /// 当前迁移步的起始版本。
        /// </summary>
        public int FromVersion => _fromVersion;

        /// <summary>
        /// 当前迁移步的目标版本。
        /// </summary>
        public int ToVersion => _toVersion;

        /// <summary>
        /// 存档文件名（经内部核心直调时可能为 null）。
        /// </summary>
        public string FileName => _fileName;

        /// <summary>
        /// 存档文件夹名称。
        /// </summary>
        public string FolderName => _folderName;

        /// <summary>当前块集合（管理器在链末取回）。</summary>
        internal List<SaveBlockEntry> Blocks => _blocks;

        /// <summary>迁移失败明细（null = 未失败）。</summary>
        internal string ErrorDetail => _errorDetail;

        /// <summary>
        /// 当前存档内的全部块键快照（数组拷贝）。
        /// </summary>
        public string[] BlockKeys
        {
            get
            {
                var keys = new string[_blocks.Count];
                for (int i = 0; i < _blocks.Count; i++)
                {
                    keys[i] = _blocks[i].Key;
                }

                return keys;
            }
        }

        /// <summary>
        /// 指定块是否存在。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public bool HasBlock(string key)
        {
            return SaveBlockComposer.TryFind(_blocks, key, out _);
        }

        /// <summary>
        /// 块改名（版本/后端/载荷不变；目标键已存在记迁移失败——会静默覆盖语义过重）。
        /// </summary>
        /// <param name="oldKey">旧块键。</param>
        /// <param name="newKey">新块键。</param>
        /// <returns>旧块存在并完成改名返回 <c>true</c>；旧块不存在为无操作返回 <c>false</c>。</returns>
        public bool RenameBlock(string oldKey, string newKey)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (!string.Equals(_blocks[i].Key, oldKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (SaveBlockComposer.TryFind(_blocks, newKey, out _))
                {
                    RecordError(StringUtility.Format("RenameBlock target key '{0}' already exists.", newKey));
                    return false;
                }

                SaveBlockEntry entry = _blocks[i];
                _blocks[i] = new SaveBlockEntry(newKey, entry.DataVersion, entry.Backend, entry.Bytes);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 删除块（废弃数据移除；块不存在为无操作）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <returns>块存在并完成删除返回 <c>true</c>。</returns>
        public bool DeleteBlock(string key)
        {
            if (!SaveBlockComposer.TryFind(_blocks, key, out _))
            {
                return false;
            }

            _blocks = SaveBlockComposer.Remove(_blocks, key);
            return true;
        }

        /// <summary>
        /// 整块变换：按块记录的后端反序列化为 <typeparamref name="T"/> → 转换 → 同后端回写（块不存在为无操作）。
        /// <para>二进制后端迁移的正路——旧类型保留在工程中即可读出旧档；<paramref name="newDataVersion"/> 用于同步推进块级模式版本
        /// （避免 <see cref="SaveDataBlock.OnMigrate"/> 类型级级联对同一变更重复执行）。</para>
        /// </summary>
        /// <typeparam name="T">块数据类型（旧形态）。</typeparam>
        /// <param name="key">数据块键。</param>
        /// <param name="transform">对象变换（返回替换对象）。</param>
        /// <param name="newDataVersion">变换后的块级模式版本（&lt; 0 = 保持不变）。</param>
        /// <returns>块存在并完成变换返回 <c>true</c>。</returns>
        public bool TransformBlock<T>(string key, Func<T, T> transform, int newDataVersion = -1)
        {
            if (!SaveBlockComposer.TryFind(_blocks, key, out SaveBlockEntry entry))
            {
                return false;
            }

            if (!SaveSerializerRegistry.TryGet(entry.Backend, out ISaveSerializer serializer))
            {
                RecordError(StringUtility.Format("TransformBlock backend '{0}' of block '{1}' is not registered.", entry.Backend, key));
                return false;
            }

            try
            {
                T data = serializer.Deserialize<T>(entry.Bytes);
                T transformed = transform(data);
                if (transformed is null)
                {
                    RecordError(StringUtility.Format("TransformBlock transform of block '{0}' returned null.", key));
                    return false;
                }

                byte[] bytes = serializer.Serialize(transformed);
                ReplaceBlock(key, new SaveBlockEntry(key, newDataVersion >= 0 ? newDataVersion : entry.DataVersion, entry.Backend, bytes));
                return true;
            }
            catch (Exception exception)
            {
                RecordError(StringUtility.Format("TransformBlock of block '{0}' failed, exception: {1}.", key, exception.GetType().Name));
                return false;
            }
        }

        /// <summary>
        /// 字段改名（块根 JSON 顶层属性 / KVT 全作用域记录键；字段不存在为无操作）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <param name="oldField">旧字段名。</param>
        /// <param name="newField">新字段名。</param>
        /// <returns>字段存在并完成改名返回 <c>true</c>。</returns>
        public bool RenameField(string key, string oldField, string newField)
        {
            return ApplyFieldOp(key, oldField, (SaveBlockEntry entry, out byte[] result) =>
                entry.Backend == ESaveBackend.KeyValue
                    ? SaveKvTransformer.RenameField(entry.Bytes, oldField, newField, out result)
                    : RenameJsonField(entry.Bytes, oldField, newField, out result));
        }

        /// <summary>字段级操作委托（变换器结果经 out 返回，未命中时 <paramref name="result"/> 为源载荷引用）。</summary>
        private delegate bool FieldOpDelegate(SaveBlockEntry entry, out byte[] result);

        /// <summary>
        /// 字段改名（块键经 <typeparamref name="TData"/> 的 <see cref="SaveDataAttribute"/> 声明解析；类型未声明块键记迁移失败）。
        /// </summary>
        /// <typeparam name="TData">块数据类型。</typeparam>
        /// <param name="oldField">旧字段名。</param>
        /// <param name="newField">新字段名。</param>
        /// <returns>字段存在并完成改名返回 <c>true</c>。</returns>
        public bool RenameField<TData>(string oldField, string newField)
        {
            if (!TryResolveBlockKey<TData>(out string key))
            {
                return false;
            }

            return RenameField(key, oldField, newField);
        }

        /// <summary>
        /// 字段改型（JSON 顶层属性 DOM 改型 / KVT 标量记录装箱改型；字段不存在为无操作）。
        /// <para>KVT 侧 <typeparamref name="TNew"/> 须为 KVT 支持的标量类型（枚举按底层类型）；
        /// JSON 侧建议限定基元/字符串/DateTime，复杂类型改型请用 <see cref="TransformBlock{T}"/>。</para>
        /// </summary>
        /// <typeparam name="TOld">旧值类型。</typeparam>
        /// <typeparam name="TNew">新值类型。</typeparam>
        /// <param name="key">数据块键。</param>
        /// <param name="field">目标字段名。</param>
        /// <param name="convert">值转换器。</param>
        /// <returns>字段存在并完成改型返回 <c>true</c>。</returns>
        public bool RetypeField<TOld, TNew>(string key, string field, Func<TOld, TNew> convert)
        {
            return ApplyFieldOp(key, field, (SaveBlockEntry entry, out byte[] result) =>
            {
                if (entry.Backend == ESaveBackend.KeyValue)
                {
                    if (!SaveKvBoxed.TryGetKvType(typeof(TNew), out ESaveKvType newType))
                    {
                        throw new SaveKvFormatException(StringUtility.Format("KVT retype target type '{0}' is not a supported scalar type.", typeof(TNew).Name));
                    }

                    return SaveKvTransformer.RetypeField(entry.Bytes, field, BoxedConvert(convert), newType, out result);
                }

                return RetypeJsonField(entry.Bytes, field, convert, out result);
            });
        }

        /// <summary>
        /// 字段改型（块键经 <typeparamref name="TData"/> 的 <see cref="SaveDataAttribute"/> 声明解析）。
        /// </summary>
        /// <typeparam name="TData">块数据类型。</typeparam>
        /// <typeparam name="TOld">旧值类型。</typeparam>
        /// <typeparam name="TNew">新值类型。</typeparam>
        /// <param name="field">目标字段名。</param>
        /// <param name="convert">值转换器。</param>
        /// <returns>字段存在并完成改型返回 <c>true</c>。</returns>
        public bool RetypeField<TData, TOld, TNew>(string field, Func<TOld, TNew> convert)
        {
            if (!TryResolveBlockKey<TData>(out string key))
            {
                return false;
            }

            return RetypeField(key, field, convert);
        }

        /// <summary>
        /// 记录迁移失败（首个异常为准；管理器据此中止迁移链）。
        /// </summary>
        /// <param name="detail">失败明细。</param>
        internal void RecordError(string detail)
        {
            _errorDetail ??= detail;
        }

        /// <summary>
        /// 字段级操作统一派发：定位块 → 按后端分发到 KVT/JSON 变换器 → 命中则替换载荷（版本/后端不变）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        /// <param name="field">字段名（日志用）。</param>
        /// <param name="operation">后端分发的变换操作。</param>
        /// <returns>字段命中返回 <c>true</c>。</returns>
        private bool ApplyFieldOp(string key, string field, FieldOpDelegate operation)
        {
            if (!SaveBlockComposer.TryFind(_blocks, key, out SaveBlockEntry entry))
            {
                return false;
            }

            if (entry.Backend != ESaveBackend.Json && entry.Backend != ESaveBackend.KeyValue)
            {
                // 二进制后端的字段级操作不受支持（字节布局由键序契约冻结；走 TransformBlock 整对象迁移）
                LogUtility.Warning("[SaveService] Migration field op on binary block '{0}' (backend: {1}) is not supported, use TransformBlock instead.", key, entry.Backend);
                return false;
            }

            if (entry.Backend == ESaveBackend.Json && !JsonFieldOpsAvailable)
            {
                RecordError(StringUtility.Format("Field op on JSON block '{0}' requires Newtonsoft.Json (com.unity.nuget.newtonsoft-json).", key));
                return false;
            }

            try
            {
                if (!operation(entry, out byte[] transformed))
                {
                    return false;
                }

                ReplaceBlock(key, new SaveBlockEntry(key, entry.DataVersion, entry.Backend, transformed));
                return true;
            }
            catch (Exception exception)
            {
                RecordError(StringUtility.Format("Field op on block '{0}' field '{1}' failed, exception: {2}.", key, field, exception.GetType().Name));
                return false;
            }
        }

        /// <summary>
        /// JSON 字段改名（可用性已在派发层校验）。
        /// </summary>
        private bool RenameJsonField(byte[] source, string oldField, string newField, out byte[] result)
        {
#if NEWTONSOFT_JSON_INSTALLED
            return SaveJsonTransformer.RenameField(source, oldField, newField, out result);
#else
            result = source;
            return false;
#endif
        }

        /// <summary>
        /// JSON 字段改型（可用性已在派发层校验）。
        /// </summary>
        private bool RetypeJsonField<TOld, TNew>(byte[] source, string field, Func<TOld, TNew> convert, out byte[] result)
        {
#if NEWTONSOFT_JSON_INSTALLED
            return SaveJsonTransformer.RetypeField(source, field, convert, out result);
#else
            result = source;
            return false;
#endif
        }

        /// <summary>
        /// 将泛型改型转换器适配为装箱转换器（KVT 通路）。
        /// </summary>
        private static Func<object, object> BoxedConvert<TOld, TNew>(Func<TOld, TNew> convert)
        {
            return boxed => convert(boxed is null ? default : (TOld)boxed);
        }

        /// <summary>
        /// 解析块数据类型声明的块键。
        /// </summary>
        private bool TryResolveBlockKey<TData>(out string key)
        {
            if (SaveBlockDescriptor<TData>.HasAttribute)
            {
                key = SaveBlockDescriptor<TData>.Key;
                return true;
            }

            RecordError(StringUtility.Format("Type '{0}' has no SaveDataAttribute, block key cannot be resolved.", typeof(TData).FullName));
            key = null;
            return false;
        }

        /// <summary>
        /// 按键替换块条目（就地）。
        /// </summary>
        private void ReplaceBlock(string key, SaveBlockEntry replacement)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (string.Equals(_blocks[i].Key, key, StringComparison.Ordinal))
                {
                    _blocks[i] = replacement;
                    return;
                }
            }
        }
    }
}
