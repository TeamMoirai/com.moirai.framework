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

        /// <summary>
        /// 数据块键（自动派生：场景名:物体路径）。
        /// </summary>
        public string ResolvedBlockKey => _resolvedBlockKey;

        /// <summary>
        /// 注册到组件存档注册表并解析块键。
        /// </summary>
        private void Awake()
        {
            _resolvedBlockKey = string.IsNullOrWhiteSpace(BlockKey)
                ? gameObject.scene.name + ":" + TransformPath()
                : BlockKey;
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
        /// <para>格式：逐绑定写入「键 = 组件类型全名」的嵌套作用域；同类型多绑定时后者追加（键重复由编辑器 UI 约束避免）。</para>
        /// </summary>
        /// <param name="writer">键值写入器。</param>
        internal void Capture(ref SaveKeyValueWriter writer)
        {
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
        /// 从键值字节恢复本组件的全部启用字段（主线程调用）。
        /// <para>顶层记录键 = 组件类型全名；按类型名路由到绑定，作用域内精确消费记录；未知类型跳过、缺失绑定跳过。</para>
        /// </summary>
        /// <param name="reader">键值读取器。</param>
        internal void Restore(ref SaveKeyValueReader reader)
        {
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                if (type != ESaveKvType.Object)
                {
                    reader.SkipRecordPayload();
                    continue;
                }

                string typeName = Encoding.UTF8.GetString(key);
                if (!TryFindBinding(typeName, out SaveTargetBinding binding, out ISaveComponentCapturer capturer))
                {
                    reader.SkipRecordPayload();
                    continue;
                }

                int recordCount = reader.ReadChildCount();
                var mask = new SaveFieldMask(capturer.FieldNames, binding.EnabledFields);
                capturer.Restore(binding.Target, ref reader, recordCount, mask);
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
        /// </summary>
        private string TransformPath()
        {
            return transform.parent == null
                ? name
                : transform.parent.name + "/" + name;
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
