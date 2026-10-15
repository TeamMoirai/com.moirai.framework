using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一次目录扫描的产出：可用清单、逐文件错误与按 id 的索引。
    /// </summary>
    /// <remarks>
    /// 冲突的 id 一个都不进 <see cref="ById"/>：<c>board</c> 与 <c>blackboard</c> 撞名时两侧都不该被当成可装。<br />
    /// 单文件失败只影响它自己，扫描继续——一格坏不清盘才不会把可读的包也藏起来。
    /// </remarks>
    internal sealed class PluginCatalogResult
    {
        private readonly List<PluginManifest> _manifests = new List<PluginManifest>();
        private readonly List<string> _errors = new List<string>();
        private readonly Dictionary<string, PluginManifest> _byId = new Dictionary<string, PluginManifest>();

        /// <summary>解析成功且 id 唯一的清单。</summary>
        internal IReadOnlyList<PluginManifest> Manifests => _manifests;

        /// <summary>逐文件判出的错误，含树路径。</summary>
        internal IReadOnlyList<string> Errors => _errors;

        /// <summary>按 id 取清单。</summary>
        internal IReadOnlyDictionary<string, PluginManifest> ById => _byId;

        internal void AddManifest(PluginManifest manifest)
        {
            _manifests.Add(manifest);
            _byId[manifest.id] = manifest;
        }

        internal void AddError(string error) => _errors.Add(error);

        /// <summary>把撞名的 id 从索引与清单里一起撤掉。</summary>
        internal void RejectConflictingId(string id)
        {
            _byId.Remove(id);
            for (int i = _manifests.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_manifests[i].id, id, StringComparison.Ordinal)) _manifests.RemoveAt(i);
            }
        }
    }
}
