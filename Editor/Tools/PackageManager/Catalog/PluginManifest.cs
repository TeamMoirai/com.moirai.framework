using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 插件自带的清单，文件名固定为 <c>&lt;id&gt;.plugin.json</c>。
    /// </summary>
    /// <remarks>
    /// 只由包管理器解析，Unity 不读它——包内被 Unity 读的那一份是分支根的 <c>package.json</c>，工具对它只读校验、永不写。<br />
    /// 解析与校验分两件事：<see cref="TryParse"/> 经包内 JSON 门面取对象，<see cref="Validate"/> 是纯判据、可脱离解析通道单测。<br />
    /// 校验失败的清单不回写盘上的原文件。
    /// </remarks>
    [Serializable]
    internal sealed class PluginManifest
    {
        /// <summary>清单文件名后缀。</summary>
        internal const string ManifestSuffix = ".plugin.json";

        public string id;
        public string name;
        public string function;
        public string description;
        public string author;
        public string version;
        public string minUnityVersion;
        public string[] tags;
        public PluginDependency[] dependencies;
        public PluginUpmDependency[] upmDependencies;

        /// <summary>解析并校验一份清单文本。</summary>
        public static bool TryParse(string sourcePath, string json, out PluginManifest manifest, out List<string> errors)
        {
            manifest = null;
            errors = new List<string>();

            if (string.IsNullOrEmpty(json))
            {
                errors.Add(Describe(sourcePath, "清单内容为空"));
                return false;
            }

            PluginManifest parsed;
            try
            {
                parsed = JsonUtility.ToObject<PluginManifest>(json);
            }
            catch (Exception exception)
            {
                errors.Add(Describe(sourcePath, $"清单不是合法 JSON：{exception.Message}"));
                return false;
            }

            if (parsed == null)
            {
                errors.Add(Describe(sourcePath, "清单解析结果为空"));
                return false;
            }

            if (!Validate(parsed, sourcePath, errors)) return false;

            manifest = parsed;
            return true;
        }

        /// <summary>
        /// 校验清单字段是否构成可安装的一体。
        /// </summary>
        /// <remarks>纯判据，不碰解析通道也不碰磁盘；任一违规都记进 <paramref name="errors"/>，不做"跳过该条继续"的降级。</remarks>
        internal static bool Validate(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            if (string.IsNullOrEmpty(candidate.id))
            {
                errors.Add(Describe(sourcePath, "缺 id"));
            }
            else if (!IsValidId(candidate.id))
            {
                errors.Add(Describe(sourcePath, $"id '{candidate.id}' 不符合字符集 [a-z0-9._-]，也不得以点开头"));
            }
            else if (!string.IsNullOrEmpty(sourcePath) && !EndsWithManifestName(sourcePath, candidate.id))
            {
                errors.Add(Describe(sourcePath, $"目录名与清单文件名的 id '{candidate.id}' 不符"));
            }

            if (string.IsNullOrEmpty(candidate.version) || !PluginVersion.TryParse(candidate.version, out _))
            {
                errors.Add(Describe(sourcePath, $"version '{candidate.version}' 不是三段十进制版本号"));
            }

            if (string.IsNullOrEmpty(candidate.minUnityVersion) || !PluginVersion.TryParse(candidate.minUnityVersion, out _))
            {
                errors.Add(Describe(sourcePath, $"minUnityVersion '{candidate.minUnityVersion}' 不是三段十进制版本号"));
            }

            ValidateDependencies(candidate, sourcePath, errors);
            ValidateUpmDependencies(candidate, sourcePath, errors);

            return errors.Count == 0;
        }

        /// <summary>判定标识符可否同时作目录名。</summary>
        internal static bool IsValidId(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || candidate[0] == '.') return false;

            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                bool allowed = (c >= 'a' && c <= 'z')
                               || (c >= '0' && c <= '9')
                               || c == '.'
                               || c == '-'
                               || c == '_';
                if (!allowed) return false;
            }

            return true;
        }

        /// <summary>当前编辑器版本是否达到该插件的最低要求。</summary>
        internal bool SatisfiesUnityVersion(PluginVersion unityVersion)
        {
            if (!PluginVersion.TryParse(minUnityVersion, out PluginVersion floor)) return false;
            return unityVersion >= floor;
        }

        /// <summary>取该插件要求的最低 Unity 版本。</summary>
        internal bool TryGetUnityFloor(out PluginVersion floor) => PluginVersion.TryParse(minUnityVersion, out floor);

        private static void ValidateDependencies(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            if (candidate.dependencies == null) return;

            for (int i = 0; i < candidate.dependencies.Length; i++)
            {
                PluginDependency dependency = candidate.dependencies[i];
                if (dependency == null)
                {
                    errors.Add(Describe(sourcePath, $"dependencies[{i}] 为空"));
                    continue;
                }

                if (!IsValidId(dependency.id))
                    errors.Add(Describe(sourcePath, $"dependencies[{i}].id '{dependency.id}' 非法"));

                if (!PluginVersion.TryParse(dependency.version, out _))
                    errors.Add(Describe(sourcePath, $"dependencies[{i}].version '{dependency.version}' 不是三段十进制版本号"));

                if (!PluginVersionOperatorUtility.TryParse(dependency.versionOperator, out _))
                    errors.Add(Describe(sourcePath,
                        $"dependencies[{i}].versionOperator '{dependency.versionOperator}' 不是 >= > = <= < 之一"));
            }
        }

        private static void ValidateUpmDependencies(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            if (candidate.upmDependencies == null) return;

            for (int i = 0; i < candidate.upmDependencies.Length; i++)
            {
                PluginUpmDependency dependency = candidate.upmDependencies[i];
                if (dependency == null)
                {
                    errors.Add(Describe(sourcePath, $"upmDependencies[{i}] 为空"));
                    continue;
                }

                if (string.IsNullOrEmpty(dependency.id))
                    errors.Add(Describe(sourcePath, $"upmDependencies[{i}].id 为空"));

                if (string.IsNullOrEmpty(dependency.version))
                    errors.Add(Describe(sourcePath, $"upmDependencies[{i}].version 为空"));
            }
        }

        private static bool EndsWithManifestName(string sourcePath, string id)
        {
            string[] segments = sourcePath.Replace('\\', '/').Split('/');
            string fileName = segments[segments.Length - 1];
            if (!string.Equals(fileName, id + ManifestSuffix, StringComparison.Ordinal)) return false;

            // 父目录名必须精确等于 id：只判"路径以 <id>.plugin.json 结尾"会让 id=board
            // 蒙过目录 blackboard（后缀匹配撞上前缀包含）。
            if (segments.Length < 2) return true;
            string folder = segments[segments.Length - 2];
            return folder.Length == 0 || string.Equals(folder, id, StringComparison.Ordinal);
        }

        private static string Describe(string sourcePath, string reason)
        {
            return string.IsNullOrEmpty(sourcePath) ? reason : $"{sourcePath}：{reason}";
        }
    }
}
