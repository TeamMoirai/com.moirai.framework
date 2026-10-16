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
        internal const string MANIFEST_SUFFIX = ".plugin.json";

        /// <summary>菜单标签的长度上限：分组名与显示名都要当树路径的一节用。</summary>
        internal const int MAX_LABEL_LENGTH = 40;

        /// <summary>类型全名与包 id 的长度上限：清单文本来自远端分支，读侧不设免上限。</summary>
        internal const int MAX_TYPE_NAME_LENGTH = 200;

        public string id;
        public string name;
        public string function;
        public string description;
        public string author;
        public string version;
        public string minUnityVersion;
        public string minFrameworkVersion;

        /// <summary>Unity 侧不再提供的第一个版本；空或缺省 = 不设上限。</summary>
        /// <remarks>区间写成 <c>[min, max)</c>：达到 max 即不提供。上限按**编辑器内部版本号**写
        /// （<c>Application.unityVersion</c> 的形，如 Unity 6.3 是 <c>6000.3.0</c>），与 <see cref="minUnityVersion"/> 同源同形。</remarks>
        public string maxUnityVersion;

        /// <summary>框架侧不再提供的第一个版本；空或缺省 = 不设上限。</summary>
        public string maxFrameworkVersion;
        public string[] tags;
        public PluginDependency[] dependencies;
        public PluginUpmDependency[] upmDependencies;

        /// <summary>本插件为哪些抽象基类提供实现；选填，缺席时下拉退回只显示已编译候选。</summary>
        public PluginProvider[] providers;

        /// <summary>这份清单在分支树里的来源路径，由发现层填入；不参与 JSON 往返。</summary>
        internal string SourceTreePath;

        /// <summary>
        /// 解析并校验一份清单文本。
        /// </summary>
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

            if (string.IsNullOrEmpty(candidate.minFrameworkVersion) || !PluginVersion.TryParse(candidate.minFrameworkVersion, out _))
            {
                errors.Add(Describe(sourcePath, $"minFrameworkVersion '{candidate.minFrameworkVersion}' 不是三段十进制版本号"));
            }

            ValidateCeiling(candidate, sourcePath, errors);

            ValidateLabels(candidate, sourcePath, errors);
            ValidateDependencies(candidate, sourcePath, errors);
            ValidateUpmDependencies(candidate, sourcePath, errors);
            ValidateProviders(candidate, sourcePath, errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// 判定标识符可否同时作目录名。
        /// </summary>
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

        /// <summary>
        /// 校验两个可选上限：写了就必须是三段版本号，且必须高于同侧地板。
        /// </summary>
        /// <remarks>地板不低于上限的区间是**空集**——谁都装不上，却没有一处会解释原因，
        /// 所以在这里按清单错误点名拒掉，而不是留到规划期把每条插件都报成版本不符。</remarks>
        private static void ValidateCeiling(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            CheckCeiling(candidate.minUnityVersion, candidate.maxUnityVersion, "maxUnityVersion", sourcePath, errors);
            CheckCeiling(candidate.minFrameworkVersion, candidate.maxFrameworkVersion, "maxFrameworkVersion", sourcePath, errors);
        }

        private static void CheckCeiling(string floorText, string ceilingText, string fieldName, string sourcePath, List<string> errors)
        {
            if (string.IsNullOrEmpty(ceilingText)) return;

            if (!PluginVersion.TryParse(ceilingText, out PluginVersion ceiling))
            {
                errors.Add(Describe(sourcePath, $"{fieldName} '{ceilingText}' 不是三段十进制版本号"));
                return;
            }

            if (!PluginVersion.TryParse(floorText, out PluginVersion floor)) return;
            if (ceiling > floor) return;

            errors.Add(Describe(sourcePath, $"{fieldName} '{ceilingText}' 不高于地板 '{floorText}'，区间为空，任何编辑器都装不上"));
        }

        /// <summary>版本区间违例的类别；<see cref="None"/> 表示满足。</summary>
        internal enum VersionViolation : byte
        {
            None = 0,
            FloorUnmet,
            CeilingExceeded
        }

        /// <summary>
        /// 当前编辑器版本落在 <c>[minUnityVersion, maxUnityVersion)</c> 内没有。
        /// </summary>
        internal bool SatisfiesUnityVersion(PluginVersion unityVersion)
            => TryGetUnityVersionViolation(unityVersion, out _, out _) == VersionViolation.None;

        /// <summary>
        /// 当前框架版本落在 <c>[minFrameworkVersion, maxFrameworkVersion)</c> 内没有。
        /// </summary>
        internal bool SatisfiesFrameworkVersion(PluginVersion frameworkVersion)
            => TryGetFrameworkVersionViolation(frameworkVersion, out _, out _) == VersionViolation.None;

        /// <summary>
        /// 判 Unity 侧区间，给出类别与可直接展示的文案；满足时返回 <see cref="VersionViolation.None"/>。
        /// </summary>
        /// <remarks>规划器与详情面共用这一条：按钮置灰的理由必须与拒绝安装的理由同源，不能各数各的。</remarks>
        internal VersionViolation TryGetUnityVersionViolation(PluginVersion unityVersion, out VersionViolation kind, out string message)
        {
            kind = VersionViolation.None;
            message = null;

            // 地板读不懂按不满足处理（与改造前同判），别让一个坏版本号变成"随便装"
            if (!PluginVersion.TryParse(minUnityVersion, out PluginVersion floor))
            {
                kind = VersionViolation.FloorUnmet;
                message = $"minUnityVersion '{minUnityVersion}' 读不懂，无法判编辑器版本是否够";
                return kind;
            }

            if (unityVersion < floor)
            {
                kind = VersionViolation.FloorUnmet;
                message = $"需要 Unity {minUnityVersion} 及以上，当前编辑器是 {unityVersion}";
                return kind;
            }

            if (!string.IsNullOrEmpty(maxUnityVersion)
                && PluginVersion.TryParse(maxUnityVersion, out PluginVersion ceiling) && unityVersion >= ceiling)
            {
                kind = VersionViolation.CeilingExceeded;
                message = $"当前 Unity 版本不再支持（{unityVersion} 已到停止提供的那一档 {maxUnityVersion}）";
                return kind;
            }

            return kind;
        }

        /// <summary>
        /// 判框架侧区间，给出类别与可直接展示的文案；读不出框架版本时由调用方先跳过这条。
        /// </summary>
        internal VersionViolation TryGetFrameworkVersionViolation(PluginVersion frameworkVersion, out VersionViolation kind, out string message)
        {
            kind = VersionViolation.None;
            message = null;

            if (!PluginVersion.TryParse(minFrameworkVersion, out PluginVersion floor))
            {
                kind = VersionViolation.FloorUnmet;
                message = $"minFrameworkVersion '{minFrameworkVersion}' 读不懂，无法判框架版本是否够";
                return kind;
            }

            if (frameworkVersion < floor)
            {
                kind = VersionViolation.FloorUnmet;
                message = $"需要框架 {minFrameworkVersion} 及以上，当前框架是 {frameworkVersion}";
                return kind;
            }

            if (!string.IsNullOrEmpty(maxFrameworkVersion)
                && PluginVersion.TryParse(maxFrameworkVersion, out PluginVersion ceiling) && frameworkVersion >= ceiling)
            {
                kind = VersionViolation.CeilingExceeded;
                message = $"当前框架版本不再提供维护（{frameworkVersion} 已到停止提供的那一档 {maxFrameworkVersion}）";
                return kind;
            }

            return kind;
        }

        /// <summary>
        /// 标签字符集：可读即可，分隔符与控制字符一律拒。
        /// </summary>
        /// <remarks>菜单树按 <c>"分组/条目"</c> 拼路径，标签里一个 <c>/</c> 就会多撑出一层节点。</remarks>
        internal static bool IsValidLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length > MAX_LABEL_LENGTH) return false;

            for (int i = 0; i < label.Length; i++)
            {
                char c = label[i];
                if (c == '/' || c == '\\' || char.IsControl(c)) return false;
            }

            return true;
        }

        /// <summary>类型全名字符集：路径分隔符、空白与控制字符一律拒——它们是菜单树与反射查找的注入面。</summary>
        internal static bool IsValidTypeName(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || candidate.Length > MAX_TYPE_NAME_LENGTH) return false;

            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                if (c == '/' || c == '\\' || char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            }

            return true;
        }

        /// <summary>UPM 包 id 字符集：小写点分段，与 Unity 的注册表惯例同形。</summary>
        internal static bool IsValidUpmPackageId(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || candidate.Length > MAX_TYPE_NAME_LENGTH) return false;

            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                if (!allowed) return false;
            }

            return true;
        }

        private static void ValidateLabels(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            if (!string.IsNullOrEmpty(candidate.function) && !IsValidLabel(candidate.function))
            {
                errors.Add(Describe(sourcePath,
                    $"function '{candidate.function}' 不许含 / \\ 或控制字符，长度上限 {MAX_LABEL_LENGTH}"));
            }

            if (!string.IsNullOrEmpty(candidate.name) && !IsValidLabel(candidate.name))
            {
                errors.Add(Describe(sourcePath,
                    $"name '{candidate.name}' 不许含 / \\ 或控制字符，长度上限 {MAX_LABEL_LENGTH}"));
            }
        }

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

        private static void ValidateProviders(PluginManifest candidate, string sourcePath, List<string> errors)
        {
            if (candidate.providers == null) return;

            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidate.providers.Length; i++)
            {
                PluginProvider provider = candidate.providers[i];
                if (provider == null)
                {
                    errors.Add(Describe(sourcePath, $"providers[{i}] 为空"));
                    continue;
                }

                if (!IsValidTypeName(provider.providerBase))
                {
                    errors.Add(Describe(sourcePath,
                        $"providers[{i}].providerBase '{provider.providerBase}' 不是合法类型全名（拒空白 / \\ 与控制字符，长度上限 {MAX_TYPE_NAME_LENGTH}）"));
                }

                if (!IsValidTypeName(provider.providerType))
                {
                    errors.Add(Describe(sourcePath,
                        $"providers[{i}].providerType '{provider.providerType}' 不是合法类型全名（拒空白 / \\ 与控制字符，长度上限 {MAX_TYPE_NAME_LENGTH}）"));
                }
                else if (!seenTypes.Add(provider.providerType))
                {
                    errors.Add(Describe(sourcePath, $"providers[{i}].providerType '{provider.providerType}' 在本清单内重复，下拉会铺出两行同名条目"));
                }

                if (!string.IsNullOrEmpty(provider.title) && !IsValidLabel(provider.title))
                {
                    errors.Add(Describe(sourcePath,
                        $"providers[{i}].title 不许含 / \\ 或控制字符，长度上限 {MAX_LABEL_LENGTH}"));
                }

                // 判据与 requiresUpm 的「空」语义：键缺席=null 表示只随本插件本体，放行；写了（含显式空串）就必须是合法包 id。
                if (provider.requiresUpm != null && !IsValidUpmPackageId(provider.requiresUpm))
                {
                    errors.Add(Describe(sourcePath,
                        $"providers[{i}].requiresUpm '{provider.requiresUpm}' 不是小写点分的 UPM 包 id"));
                }
            }
        }

        private static bool EndsWithManifestName(string sourcePath, string id)
        {
            string[] segments = sourcePath.Replace('\\', '/').Split('/');
            string fileName = segments[segments.Length - 1];
            if (!string.Equals(fileName, id + MANIFEST_SUFFIX, StringComparison.Ordinal)) return false;

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
