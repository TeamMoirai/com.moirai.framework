using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档目标绑定：目标组件 + 勾选参与存档的字段名列表（Inspector 勾选配置的序列化载体）。
    /// <para>字段名须为该组件类型上 <see cref="SaveFieldAttribute"/> 标注字段的捕获键（默认 = 字段名）。</para>
    /// </summary>
    [Serializable]
    public sealed class SaveTargetBinding
    {
        /// <summary>目标组件（同 GameObject 上的可保存组件）。</summary>
        [SerializeField] internal Component Target;

        /// <summary>勾选参与存档的字段键列表。</summary>
        [SerializeField] internal List<string> EnabledFields = new List<string>();
    }

    /// <summary>
    /// 无代码保存组件：挂载到 GameObject，Inspector 勾选目标组件的 <see cref="SaveFieldAttribute"/> 字段，
    /// 运行期经 SaveHost SourceGenerator 生成的强类型捕获器零反射捕获/恢复。
    /// <para>Awake 注册 <see cref="SaveComponentRegistry"/>、OnDestroy 注销；
    /// 存取经 <see cref="SaveService.SaveComponentsAsync"/> / <see cref="SaveService.LoadComponentsAsync"/> 触发（时机由游戏层决定）。</para>
    /// </summary>
    [AddComponentMenu("Moirai/Save Component")]
    [DisallowMultipleComponent]
    public sealed class SaveComponent : MonoBehaviour
    {
        /// <summary>数据块键（空 = 自动派生「场景名:物体路径」；须满足块键规则）。</summary>
        [SerializeField] internal string BlockKey = string.Empty;

        /// <summary>目标绑定列表（目标组件 + 勾选字段）。</summary>
        [SerializeField] internal List<SaveTargetBinding> Targets = new List<SaveTargetBinding>();

        /// <summary>解析后的块键（空串自动派生时在注册期计算）。</summary>
        [NonSerialized] private string _resolvedBlockKey;

        /// <summary>KVT 块内模式版本作用域保留键（"$" 不可能出现在 C# 类型全名中，与绑定作用域键天然隔离）。</summary>
        internal const string SchemaScopeKey = "$schemas";

        /// <summary>
        /// 数据块键（自动派生：场景名:物体路径）。
        /// </summary>
        public string ResolvedBlockKey => _resolvedBlockKey;

        /// <summary>
        /// 注册到组件存档注册表并解析块键。
        /// </summary>
        private void Awake()
        {
            EnsureActivated();
        }

        /// <summary>
        /// 确保块键已解析并注册（幂等）。
        /// <para>实体管线在编辑模式下的兜底入口——非 ExecuteInEditMode 组件的 Awake 在编辑模式不执行，
        /// 生成/恢复后显式调用补齐注册（播放态 Awake 已执行，重复调用无副作用）。</para>
        /// </summary>
        internal void EnsureActivated()
        {
            if (_resolvedBlockKey == null)
            {
                _resolvedBlockKey = string.IsNullOrWhiteSpace(BlockKey)
                    ? gameObject.scene.name + ":" + TransformPath()
                    : BlockKey;
            }

            SaveComponentRegistry.Register(this);
        }

        /// <summary>
        /// 从组件存档注册表注销。
        /// </summary>
        private void OnDestroy()
        {
            SaveComponentRegistry.Unregister(this);
        }

        /// <summary>
        /// 将本组件的全部启用字段捕获为键值字节（主线程调用）。
        /// <para>格式：首个记录为 <see cref="SchemaScopeKey"/> 模式版本作用域（组件类型全名 → <see cref="ISaveComponentCapturer.SchemaVersion"/>，恢复侧路由迁移钩子的依据）；
        /// 随后逐绑定写入「键 = 组件类型全名」的嵌套作用域；同类型多绑定时后者追加（键重复由编辑器 UI 约束避免）。</para>
        /// </summary>
        /// <param name="writer">键值写入器。</param>
        internal void Capture(ref SaveKeyValueWriter writer)
        {
            // 模式版本作用域先行（恢复侧须先于绑定作用域读到版本表才能路由迁移钩子）
            int schemaCount = CountCapturableBindings();
            if (schemaCount > 0)
            {
                writer.BeginNestedObject(SchemaScopeKey, schemaCount);
                for (int i = 0; i < Targets.Count; i++)
                {
                    SaveTargetBinding binding = Targets[i];
                    if (binding == null || binding.Target == null)
                    {
                        continue;
                    }

                    if (!SaveCapturerRegistry.TryGet(binding.Target.GetType(), out ISaveComponentCapturer schemaCapturer))
                    {
                        continue;
                    }

                    writer.WriteInt32(binding.Target.GetType().FullName, schemaCapturer.SchemaVersion);
                }

                writer.EndNested();
            }

            for (int i = 0; i < Targets.Count; i++)
            {
                SaveTargetBinding binding = Targets[i];
                if (binding == null || binding.Target == null)
                {
                    continue;
                }

                if (!SaveCapturerRegistry.TryGet(binding.Target.GetType(), out ISaveComponentCapturer capturer))
                {
                    LogUtility.Warning("[SaveService] No capturer registered for component '{0}' on '{1}'.", binding.Target.GetType().Name, name);
                    continue;
                }

                var mask = new SaveFieldMask(capturer.FieldNames, binding.EnabledFields);
                capturer.Capture(binding.Target, ref writer, mask);
            }
        }

        /// <summary>
        /// 统计可捕获绑定数（目标有效且捕获器已注册）。
        /// </summary>
        private int CountCapturableBindings()
        {
            int count = 0;
            for (int i = 0; i < Targets.Count; i++)
            {
                SaveTargetBinding binding = Targets[i];
                if (binding != null && binding.Target != null && SaveCapturerRegistry.TryGet(binding.Target.GetType(), out _))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 从键值字节恢复本组件的全部启用字段（主线程调用）。
        /// <para>顶层记录键 = 组件类型全名；按类型名路由到绑定，作用域内精确消费记录；未知类型跳过、缺失绑定跳过。
        /// <see cref="SchemaScopeKey"/> 记录提供存档模式版本——与捕获器当前版本不符时走 <see cref="ISaveComponentMigrator"/> 迁移钩子
        /// （组件未实现钩子则记告警并按键匹配容错恢复）。</para>
        /// </summary>
        /// <param name="reader">键值读取器。</param>
        internal void Restore(ref SaveKeyValueReader reader)
        {
            Dictionary<string, int> storedVersions = null;
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                if (type != ESaveKvType.Object)
                {
                    reader.SkipRecordPayload();
                    continue;
                }

                string typeName = Encoding.UTF8.GetString(key);
                if (string.Equals(typeName, SchemaScopeKey, StringComparison.Ordinal))
                {
                    ReadSchemaVersions(ref reader, ref storedVersions);
                    continue;
                }

                if (!TryFindBinding(typeName, out SaveTargetBinding binding, out ISaveComponentCapturer capturer))
                {
                    reader.SkipRecordPayload();
                    continue;
                }

                int recordCount = reader.ReadChildCount();
                int storedVersion = capturer.SchemaVersion;
                if (storedVersions != null)
                {
                    storedVersions.TryGetValue(typeName, out storedVersion);
                    // 版本表缺失该类型条目（旧档新绑定）按当前版本处理——KVT 键匹配天然容错
                    if (storedVersion == 0)
                    {
                        storedVersion = capturer.SchemaVersion;
                    }
                }

                if (storedVersion != capturer.SchemaVersion && binding.Target is ISaveComponentMigrator migrator)
                {
                    migrator.OnMigrateComponent(storedVersion, ref reader, recordCount);
                    continue;
                }

                if (storedVersion != capturer.SchemaVersion)
                {
                    LogUtility.Warning("[SaveService] Component '{0}' schema version mismatch ({1} -> {2}) without ISaveComponentMigrator, restoring by key matching.", typeName, storedVersion, capturer.SchemaVersion);
                }

                var mask = new SaveFieldMask(capturer.FieldNames, binding.EnabledFields);
                capturer.Restore(binding.Target, ref reader, recordCount, mask);
            }
        }

        /// <summary>
        /// 读取模式版本作用域（组件类型全名 → 存档版本）。
        /// </summary>
        private static void ReadSchemaVersions(ref SaveKeyValueReader reader, ref Dictionary<string, int> versions)
        {
            int recordCount = reader.ReadChildCount();
            versions ??= new Dictionary<string, int>(recordCount);
            for (int i = 0; i < recordCount; i++)
            {
                if (!reader.ReadRecord(out ReadOnlySpan<byte> entryKey, out ESaveKvType entryType))
                {
                    return;
                }

                if (entryType == ESaveKvType.Int32)
                {
                    versions[Encoding.UTF8.GetString(entryKey)] = reader.ReadInt32();
                }
                else
                {
                    reader.SkipRecordPayload();
                }
            }
        }

        /// <summary>
        /// 按组件类型全名查找绑定（含捕获器与目标有效性）。
        /// </summary>
        /// <param name="typeName">组件类型全名。</param>
        /// <param name="binding">命中的绑定。</param>
        /// <param name="capturer">对应捕获器。</param>
        /// <returns>命中返回 <c>true</c>。</returns>
        private bool TryFindBinding(string typeName, out SaveTargetBinding binding, out ISaveComponentCapturer capturer)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                SaveTargetBinding candidate = Targets[i];
                if (candidate == null || candidate.Target == null)
                {
                    continue;
                }

                Type targetType = candidate.Target.GetType();
                if (!string.Equals(targetType.FullName, typeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SaveCapturerRegistry.TryGet(targetType, out capturer))
                {
                    continue;
                }

                binding = candidate;
                return true;
            }

            binding = null;
            capturer = null;
            return false;
        }

        /// <summary>
        /// 拼装物体层级路径（场景根到本物体的名称链）。
        /// <para>同场景同名兄弟物体以兄弟索引消歧（「Name」或「Name[N]」，N 为 <see cref="Transform.GetSiblingIndex"/> 中同名次序）。</para>
        /// </summary>
        internal string TransformPath()
        {
            // 一次上行收集链段，再反向追加写出，避免逐级字符串重分配。
            Transform current = transform;
            int depth = 0;
            for (Transform t = current; t != null; t = t.parent)
            {
                depth++;
            }

            var segments = new string[depth];
            for (int i = depth - 1; i >= 0; i--)
            {
                segments[i] = DisambiguatedName(current);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        /// <summary>
        /// 生成同级消歧名：无同名兄弟（或同名根）用裸名；否则追加「[N]」序号（N = 自身在同名序列中的次序，从 0 起）。
        /// </summary>
        /// <param name="target">目标变换组件。</param>
        private static string DisambiguatedName(Transform target)
        {
            Transform parent = target.parent;
            if (parent == null)
            {
                return DisambiguateSceneRoot(target);
            }

            int ordinal = 0;
            int total = 0;
            int selfIndex = target.GetSiblingIndex();
            for (int i = 0; i < parent.childCount; i++)
            {
                if (!string.Equals(parent.GetChild(i).name, target.name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (i < selfIndex)
                {
                    ordinal++;
                }

                total++;
            }

            return total <= 1
                ? target.name
                : StringUtility.Format("{0}[{1}]", target.name, ordinal);
        }

        /// <summary>
        /// 场景根物体消歧：按同名根的层级次序追加序号（根无父级，兄弟索引语义由场景根序承担）。
        /// </summary>
        /// <param name="target">根变换组件。</param>
        private static string DisambiguateSceneRoot(Transform target)
        {
            var roots = new List<GameObject>(target.gameObject.scene.rootCount);
            target.gameObject.scene.GetRootGameObjects(roots);
            int ordinal = 0;
            int total = 0;
            for (int i = 0; i < roots.Count; i++)
            {
                if (!string.Equals(roots[i].name, target.name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (roots[i].transform == target)
                {
                    ordinal = total;
                }

                total++;
            }

            return total <= 1
                ? target.name
                : StringUtility.Format("{0}[{1}]", target.name, ordinal);
        }
    }

    /// <summary>
    /// 存档组件注册表：场景内活跃 <see cref="SaveComponent"/> 的快照源（Awake 注册 / OnDestroy 注销）。
    /// </summary>
    public static class SaveComponentRegistry
    {
        /// <summary>活跃组件表（插入序；快照供存取管线遍历）。</summary>
        private static readonly List<SaveComponent> s_Components = new List<SaveComponent>();

        /// <summary>
        /// 注册组件（幂等）。
        /// </summary>
        /// <param name="component">存档组件。</param>
        public static void Register(SaveComponent component)
        {
            if (!s_Components.Contains(component))
            {
                s_Components.Add(component);
            }
        }

        /// <summary>
        /// 注销组件。
        /// </summary>
        /// <param name="component">存档组件。</param>
        public static void Unregister(SaveComponent component)
        {
            s_Components.Remove(component);
        }

        /// <summary>
        /// 获取活跃组件快照（数组拷贝；遍历期间注册/注销不影响遍历）。
        /// </summary>
        /// <returns>活跃组件数组。</returns>
        public static SaveComponent[] Snapshot()
        {
            return s_Components.ToArray();
        }
    }
}
