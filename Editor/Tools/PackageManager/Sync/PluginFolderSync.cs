using System;
using System.Collections.Generic;
using System.IO;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 把一个已装插件目录反向同步回本地 git 工作树里的插件源目录。
    /// </summary>
    /// <remarks>
    /// 源是 <c>Packages/&lt;检出目录名&gt;.&lt;id&gt;/</c>，落点是 <c>&lt;反向同步文件夹&gt;/&lt;id&gt;/</c>。<br />
    /// 按镜像同步：同路径覆盖，目标里多出的文件删掉；安装时生成的根 <c>package.json</c> 与它的 <c>.meta</c> 是嵌入包的产物，不写回也不删。<br />
    /// 插件目录里的 <c>.meta</c> 是分支上跟踪的内容，照常写回；<c>.git</c> 目录一律不碰。
    /// </remarks>
    internal static class PluginFolderSync
    {
        private const string GENERATED_MANIFEST_NAME = "package.json";
        private const string GENERATED_MANIFEST_META_NAME = "package.json.meta";
        private const string GIT_METADATA_FOLDER_NAME = ".git";

        /// <summary>
        /// 同步一个插件；目录形状不对时直接判非，不写任何文件。
        /// </summary>
        internal static PluginSyncReport Sync(string pluginId, string sourceFolder, string targetRoot)
        {
            var report = new PluginSyncReport(pluginId);

            if (!PluginManifest.IsValidId(pluginId))
            {
                report.Fail($"插件标识 '{pluginId}' 不符合字符集 [a-z0-9._-]，不交给文件系统");
                return report;
            }

            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                report.Fail($"已装目录不存在：{sourceFolder}");
                return report;
            }

            if (string.IsNullOrEmpty(targetRoot) || !Directory.Exists(targetRoot))
            {
                report.Fail($"反向同步文件夹不存在：{targetRoot}");
                return report;
            }

            try
            {
                string source = Path.GetFullPath(sourceFolder);
                string target = Path.GetFullPath(Path.Combine(targetRoot, pluginId));
                if (IsSameOrUnder(target, source) || IsSameOrUnder(source, target))
                {
                    report.Fail($"同步落点与已装目录重叠，拒绝把目录复制进它自己：{source} ↔ {target}");
                    return report;
                }

                Directory.CreateDirectory(target);
                var synced = CopySourceFiles(source, target, report);
                DeleteExtraFiles(target, synced, report);
            }
            catch (Exception exception)
            {
                report.Fail($"同步 {pluginId} 失败：{exception.Message}　已写入的部分不回滚");
            }

            return report;
        }

        /// <summary>
        /// 把源目录里的内容写到落点，返回本次参与同步的相对路径集合。
        /// </summary>
        private static HashSet<string> CopySourceFiles(string source, string target, PluginSyncReport report)
        {
            var synced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

            for (int i = 0; i < files.Length; i++)
            {
                string relative = RelativePath(source, files[i]);
                if (!IsSyncedPath(relative)) continue;

                string destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.Copy(files[i], destination, true);
                synced.Add(relative);
                report.CountCopied();
            }

            return synced;
        }

        /// <summary>
        /// 镜像收口：落点里不在源集合内的文件删掉；安装产物与 git 元数据不在同步面内。
        /// </summary>
        private static void DeleteExtraFiles(string target, HashSet<string> synced, PluginSyncReport report)
        {
            string[] files = Directory.GetFiles(target, "*", SearchOption.AllDirectories);

            for (int i = 0; i < files.Length; i++)
            {
                string relative = RelativePath(target, files[i]);
                if (!IsSyncedPath(relative)) continue;
                if (synced.Contains(relative)) continue;

                File.Delete(files[i]);
                report.CountDeleted();
            }
        }

        /// <summary>
        /// 该相对路径参不参与同步：根 package.json 与其 .meta 是安装时生成的，.git 目录不碰。
        /// </summary>
        private static bool IsSyncedPath(string relativePath)
        {
            if (string.Equals(relativePath, GENERATED_MANIFEST_NAME, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(relativePath, GENERATED_MANIFEST_META_NAME, StringComparison.OrdinalIgnoreCase)) return false;

            string[] segments = relativePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], GIT_METADATA_FOLDER_NAME, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        private static string RelativePath(string root, string fullPath)
        {
            return fullPath.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        /// <summary>候选路径与根相同或位于根之下。</summary>
        private static bool IsSameOrUnder(string candidate, string root)
        {
            string normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;

            return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
