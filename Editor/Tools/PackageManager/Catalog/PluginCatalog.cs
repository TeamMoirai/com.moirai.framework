using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 分支树路径到插件清单集合的目录发现面。
    /// </summary>
    /// <remarks>
    /// 取文走委托注入：本层因此完全不碰 git，发现规则全部能在无宿主的情况下覆盖。<br />
    /// 候选判据取形状：文件名须为 <c>&lt;id&gt;.plugin.json</c> 且父目录名精确等于同一个 id，两段都要相等。<br />
    /// 同一 id 收进多份时两边一起撤——静默挑一个会让"分支上有两个同名包"这件事消失。
    /// </remarks>
    internal static class PluginCatalog
    {
        /// <summary>
        /// 扫描树路径并解析清单，单文件失败只影响它自己。
        /// </summary>
        internal static PluginCatalogResult Build(IReadOnlyList<string> treePaths, Func<string, string> readBlob)
        {
            var result = new PluginCatalogResult();
            if (treePaths == null || treePaths.Count == 0) return result;

            if (readBlob == null)
            {
                result.AddError("取文委托为空引用");
                return result;
            }

            var pathsById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var conflictIds = new List<string>();

            for (int i = 0; i < treePaths.Count; i++)
            {
                string treePath = treePaths[i];
                if (!TrySplitManifestPath(treePath, out string folder, out string idFromName)) continue;

                if (folder == null)
                {
                    result.AddError($"{treePath}：清单必须住在 {idFromName}/ 目录下");
                    continue;
                }

                if (!string.Equals(folder, idFromName, StringComparison.Ordinal))
                {
                    result.AddError($"{treePath}：目录名 '{folder}' 与清单文件名要求的 '{idFromName}' 不符");
                    continue;
                }

                string json = readBlob(treePath);
                if (string.IsNullOrEmpty(json))
                {
                    result.AddError($"{treePath}：取不到清单内容");
                    continue;
                }

                if (!PluginManifest.TryParse(treePath, json, out PluginManifest manifest, out List<string> parseErrors))
                {
                    // 一个文件一行错误：多行会把"几格坏了"数成"坏了几个字段"。
                    result.AddError(string.Join("；", parseErrors));
                    continue;
                }

                if (!pathsById.TryGetValue(manifest.id, out List<string> paths))
                {
                    paths = new List<string>();
                    pathsById[manifest.id] = paths;
                }
                else if (!conflictIds.Contains(manifest.id))
                {
                    conflictIds.Add(manifest.id);
                }

                paths.Add(treePath);
                manifest.SourceTreePath = treePath;
                result.AddManifest(manifest);
            }

            for (int i = 0; i < conflictIds.Count; i++)
            {
                string id = conflictIds[i];
                result.AddError($"同一 id '{id}' 出现在多个目录：{string.Join("、", pathsById[id])}");
                result.RejectConflictingId(id);
            }

            return result;
        }

        /// <summary>
        /// 按清单文件名的形状挑出候选路径，供上层一次预取。
        /// </summary>
        /// <remarks>只判文件名后缀；目录名与 id 是否相等仍由 <see cref="Build"/> 判，多取一份坏清单不改变结论。</remarks>
        internal static List<string> SelectManifestPaths(IReadOnlyList<string> treePaths)
        {
            var selected = new List<string>();
            for (int i = 0; treePaths != null && i < treePaths.Count; i++)
            {
                if (TrySplitManifestPath(treePaths[i], out _, out _)) selected.Add(treePaths[i]);
            }

            return selected;
        }

        /// <summary>
        /// 按清单文件名的形状拆出目录名与 id，非清单形状返回假。
        /// </summary>
        private static bool TrySplitManifestPath(string treePath, out string folder, out string id)
        {
            folder = null;
            id = null;
            if (string.IsNullOrEmpty(treePath)) return false;

            string[] segments = treePath.Split('/');
            string fileName = segments[segments.Length - 1];
            if (!fileName.EndsWith(PluginManifest.MANIFEST_SUFFIX, StringComparison.Ordinal)) return false;

            id = fileName.Substring(0, fileName.Length - PluginManifest.MANIFEST_SUFFIX.Length);
            if (id.Length == 0) return false;

            if (segments.Length >= 2) folder = segments[segments.Length - 2];
            return true;
        }
    }
}
