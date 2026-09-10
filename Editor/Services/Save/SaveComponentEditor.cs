using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Save;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.Save
{
    /// <summary>
    /// SaveComponent 编辑器：为每个目标绑定展示其组件类型上全部 <see cref="SaveFieldAttribute"/> 标注字段（含私有），
    /// 勾选结果写回绑定配置；运行期捕获器全字段生成、按勾选掩码过滤（编译期/编辑期正交）。
    /// <para>字段清单经反射一次性缓存（编辑器专用路径）；未注册捕获器的组件类型给予告警提示。</para>
    /// </summary>
    [CustomEditor(typeof(SaveComponent))]
    public sealed class SaveComponentEditor : UnityEditor.Editor
    {
        /// <summary>字段清单缓存（组件类型 → 可保存字段元信息）。</summary>
        private static readonly Dictionary<Type, SaveFieldMeta[]> s_FieldMetaCache = new Dictionary<Type, SaveFieldMeta[]>();

        /// <summary>绑定折叠状态。</summary>
        private readonly List<bool> _foldouts = new List<bool>();

        /// <summary>可保存字段元信息。</summary>
        private readonly struct SaveFieldMeta
        {
            /// <summary>存档键（显式指定或字段名）。</summary>
            public readonly string Key;

            /// <summary>展示名（字段名，键显式指定时附注）。</summary>
            public readonly string DisplayName;

            /// <summary>字段类型显示名。</summary>
            public readonly string TypeName;

            public SaveFieldMeta(string key, string displayName, string typeName)
            {
                Key = key;
                DisplayName = displayName;
                TypeName = typeName;
            }
        }

        /// <summary>
        /// 绘制默认序列化字段 + 各绑定勾选列表。
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var component = (SaveComponent)target;

            // Targets 列表数量可能经默认 Inspector 变化——同步折叠状态
            while (_foldouts.Count < component.Targets.Count)
            {
                _foldouts.Add(true);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("字段勾选（编译期 [SaveField] 字段）", EditorStyles.boldLabel);
            for (int i = 0; i < component.Targets.Count; i++)
            {
                DrawBinding(component, i);
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(component);
            }
        }

        /// <summary>
        /// 绘制单个绑定的目标信息与字段勾选。
        /// </summary>
        private void DrawBinding(SaveComponent component, int index)
        {
            SaveTargetBinding binding = component.Targets[index];
            if (binding == null || binding.Target == null)
            {
                EditorGUILayout.HelpBox($"绑定 {index}: 目标组件为空", MessageType.Warning);
                return;
            }

            _foldouts[index] = EditorGUILayout.Foldout(_foldouts[index], $"[{index}] {binding.Target.GetType().Name}", true);
            if (!_foldouts[index])
            {
                return;
            }

            SaveFieldMeta[] metas = GetFieldMetas(binding.Target.GetType());
            if (metas.Length == 0)
            {
                EditorGUILayout.HelpBox("该组件类型没有 [SaveField] 标注字段（或未生成捕获器）", MessageType.Warning);
                return;
            }

            if (!SaveCapturerRegistry.TryGet(binding.Target.GetType(), out _))
            {
                EditorGUILayout.HelpBox("该组件类型尚未注册捕获器——确认字段标注与编译状态", MessageType.Warning);
            }

            EditorGUI.indentLevel++;
            var enabledSet = new HashSet<string>(binding.EnabledFields, StringComparer.Ordinal);
            foreach (SaveFieldMeta meta in metas)
            {
                bool checkedState = enabledSet.Contains(meta.Key);
                bool newChecked = EditorGUILayout.Toggle($"{meta.DisplayName}  ({meta.TypeName})", checkedState);
                if (newChecked != checkedState)
                {
                    if (newChecked)
                    {
                        binding.EnabledFields.Add(meta.Key);
                    }
                    else
                    {
                        binding.EnabledFields.Remove(meta.Key);
                    }

                    GUI.changed = true;
                }
            }

            if (GUILayout.Button("清除勾选", GUILayout.Width(80)))
            {
                binding.EnabledFields.Clear();
                GUI.changed = true;
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 获取组件类型的可保存字段元信息（反射一次并缓存）。
        /// </summary>
        /// <param name="componentType">组件类型。</param>
        /// <returns>字段元信息数组。</returns>
        private static SaveFieldMeta[] GetFieldMetas(Type componentType)
        {
            if (s_FieldMetaCache.TryGetValue(componentType, out SaveFieldMeta[] metas))
            {
                return metas;
            }

            var list = new List<SaveFieldMeta>();
            foreach (FieldInfo fieldInfo in componentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                SaveFieldAttribute attribute = fieldInfo.GetCustomAttribute<SaveFieldAttribute>(false);
                if (attribute == null)
                {
                    continue;
                }

                string key = attribute.Key ?? fieldInfo.Name;
                string displayName = attribute.Key == null ? fieldInfo.Name : $"{fieldInfo.Name} → {attribute.Key}";
                list.Add(new SaveFieldMeta(key, displayName, fieldInfo.FieldType.Name));
            }

            metas = list.ToArray();
            s_FieldMetaCache[componentType] = metas;
            return metas;
        }
    }
}
