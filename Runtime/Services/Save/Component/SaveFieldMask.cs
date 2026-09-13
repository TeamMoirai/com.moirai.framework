using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 字段捕获掩码：勾选字段（键名集合）→ 字段索引位图（索引对应 <see cref="ISaveComponentCapturer.FieldNames"/>）。
    /// <para>每组件构建一次并缓存（构建需键名集合查找，捕获热路径仅布尔数组索引测试）。</para>
    /// </summary>
    public sealed class SaveFieldMask
    {
        /// <summary>启用表（索引 = 字段索引）。</summary>
        private readonly bool[] _enabled;

        /// <summary>
        /// 创建字段掩码。
        /// </summary>
        /// <param name="fieldNames">捕获器的全量字段名数组。</param>
        /// <param name="enabledKeys">启用的字段键集合（null/空 = 全部禁用；配置中出现但捕获器不存在的键被忽略，由编辑器 UI 预警漂移）。</param>
        public SaveFieldMask(string[] fieldNames, IReadOnlyCollection<string> enabledKeys)
        {
            _enabled = new bool[fieldNames.Length];
            if (enabledKeys == null || enabledKeys.Count == 0)
            {
                return;
            }

            var keys = new HashSet<string>(enabledKeys, StringComparer.Ordinal);
            for (int i = 0; i < fieldNames.Length; i++)
            {
                if (keys.Contains(fieldNames[i]))
                {
                    _enabled[i] = true;
                }
            }
        }

        /// <summary>
        /// 指定索引的字段是否参与捕获。
        /// </summary>
        /// <param name="index">字段索引。</param>
        /// <returns>参与返回 <c>true</c>。</returns>
        public bool IsEnabled(int index)
        {
            return (uint)index < (uint)_enabled.Length && _enabled[index];
        }
    }
}
