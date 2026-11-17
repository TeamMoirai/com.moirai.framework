using Luban;
using Luban.Pipeline;
using Luban.Schema;

namespace Moirai.LubanLocalization;

/// <summary>
/// 单进程多语言导出管线（插件路线）。基于 Luban 公开 API，不修改 Luban 源码：
/// - DefaultPipeline 的全局状态全部经 PipelineScope.Current 解析，可按不同 variants 顺序多次调用；
/// - 自定义 OutputSaver（moirai-stage）把每趟产物改写进暂存区，真实目录零直写；
/// - 全部趟次完成后统一做字节级提交（只写变更、删过期文件及 .meta）或 CHECK 比对。
///
/// 趟次与旧 gen.sh 多趟路线一一对应（conf、target、variant 完全一致，产物逐字节相同）：
///   1. 校验趟：合并 schema（VALIDATE_CONF，含 L10n 表），ref/path 等数据校验，--strict 语义，产物丢弃；
///   2. 常规表趟：CONF，代码 + 数据一趟，语言无关；
///   3. 多语言代码趟：L10N_CONF，仅代码，variant=首语言；
///   4. 各语言数据趟：L10N_CONF，仅数据，variant=语言 i，落各自语言子目录；
///   5. 语言常量 L10nLanguages.cs（内容与旧路线逐字节一致）+ 生成码头部空行剥离。
///
/// 用法：dotnet Luban.dll -t client -p moirai-l10n --conf luban.conf
///          -x moirai.config=config.ini [-x moirai.check=true] [-x moirai.format=bin|json]
/// 配置全部来自 config.ini（键与 gen.sh 共用，无第二份真源）。
/// </summary>
[Pipeline("moirai-l10n")]
public sealed class MoiraiLocalizationPipeline : IPipeline
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void Run(PipelineArguments args)
    {
        string configPath = EnvManager.Current.GetOptionOrDefaultRaw("moirai.config", "config.ini");
        bool check = EnvManager.Current.GetBoolOptionOrDefault("moirai", "check", false, false);
        var ini = new IniConfig(configPath);

        string format = EnvManager.Current.GetOptionOrDefaultRaw("moirai.format", ini.Get("DATA_FORMAT").ToLowerInvariant());
        (string codeTarget, string dataTarget) = format switch
        {
            "bin" => ("cs-bin", "bin"),
            "json" => ("cs-simple-json", "json"),
            _ => throw new InvalidOperationException($"moirai.format / DATA_FORMAT 只认 bin 或 json，收到：{format}"),
        };

        string[] languages = ini.Get("L10N_LANGUAGES", required: true)
            .Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (languages.Length == 0 || languages.Distinct(StringComparer.Ordinal).Count() != languages.Length)
            throw new InvalidOperationException("L10N_LANGUAGES 必须是非空、不重复的语言码清单。");
        foreach (string language in languages)
            if (language.Split('/', '\\').Length != 1 || language.Contains(':'))
                throw new InvalidOperationException($"语言码不能作为目录名：{language}");

        string codeOut = ini.Resolve(ini.Get("CODE_OUTPUT_PATH_CLIENT", required: true));
        string dataRoot = EnsureTrailingSeparator(ini.Resolve(ini.Get("DATA_OUTPUT_PATH_CLIENT", required: true)));
        string langListCode = ini.Resolve(ini.Get("L10N_LANG_LIST_CODE", required: true));
        string namespaceName = ini.Get("L10N_LANG_CLASS_NAMESPACE");
        string langListRelative = GetRelativeUnder(langListCode, codeOut);

        var loader = new GlobalConfigLoader();
        LubanConfig confMain = loader.Load(ini.Get("CONF", required: true));
        LubanConfig confL10n = loader.Load(ini.Get("L10N_CONF", required: true));
        LubanConfig confValidate = loader.Load(ini.Get("VALIDATE_CONF", required: true));

        using var stage = new MoiraiStage();
        string stageCode = Path.Combine(stage.Root, "code");
        string stageDataCommon = Path.Combine(stage.Root, "data", "common");

        // 趟 1：校验趟（合并 schema，--strict 语义），产物丢弃
        RunInner(args, confValidate, VariantDefault(languages[0]), null, new[] { dataTarget },
            stage.DiscardDirectory, stage.DiscardDirectory, discard: true, strict: true);

        // 趟 2：常规表（语言无关，代码 + 数据），对应旧路线 2/4
        RunInner(args, confMain, null, new[] { codeTarget }, new[] { dataTarget },
            stageCode, stageDataCommon, discard: false, strict: false);

        // 趟 3：多语言代码（与语言无关），对应旧路线 3/4
        RunInner(args, confL10n, VariantDefault(languages[0]), new[] { codeTarget }, null,
            stageCode, stageDataCommon, discard: false, strict: false);

        // 趟 4：各语言数据，对应旧路线 4/4 的每语言一趟
        foreach (string language in languages)
            RunInner(args, confL10n, VariantDefault(language), null, new[] { dataTarget },
                stageCode, Path.Combine(stage.Root, "data", language), discard: false, strict: false);

        // 语言常量 + Luban 生成码头部空行剥离（都在暂存区内进行）
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(stageCode, langListRelative.Replace('/', Path.DirectorySeparatorChar)))!);
        File.WriteAllBytes(Path.Combine(stageCode, langListRelative.Replace('/', Path.DirectorySeparatorChar)),
            LanguageConstant.Build(languages, namespaceName));
        LanguageConstant.StripGeneratedHeaderBlankLine(stageCode,
            Path.Combine(stageCode, langListRelative.Replace('/', Path.DirectorySeparatorChar)));

        if (check)
        {
            Console.WriteLine("[moirai] CHECK：比对暂存区与已提交产物（真实产物零写入）");
            CheckOutputs(codeOut, dataRoot, languages, stageCode, stageDataCommon, Path.Combine(stage.Root, "data"));
        }
        else
        {
            CommitOutputs(codeOut, dataRoot, languages, stageCode, stageDataCommon, Path.Combine(stage.Root, "data"));
        }
    }

    private static Dictionary<string, string> VariantDefault(string language) =>
        new(StringComparer.Ordinal) { ["default"] = language };

    /// <summary>
    /// 在同一进程内以指定的 conf / variants / targets 跑一趟标准 DefaultPipeline。
    /// 趟产物经 moirai-stage saver 落进 MoiraiStage 当前趟指定的暂存子目录。
    /// </summary>
    private static void RunInner(PipelineArguments baseArgs, LubanConfig conf, Dictionary<string, string>? variants,
        string[]? codeTargets, string[]? dataTargets, string codeDir, string dataDir, bool discard, bool strict)
    {
        MoiraiStage.Current!.BeginRun(codeDir, dataDir, discard);
        var run = new PipelineArguments
        {
            Target = baseArgs.Target,
            ForceLoadTableDatas = false,
            IncludeTags = baseArgs.IncludeTags,
            ExcludeTags = baseArgs.ExcludeTags,
            CodeTargets = codeTargets?.ToList() ?? new List<string>(),
            DataTargets = dataTargets?.ToList() ?? new List<string>(),
            SchemaCollector = baseArgs.SchemaCollector,
            Config = conf,
            OutputTables = baseArgs.OutputTables,
            TimeZone = baseArgs.TimeZone,
            CustomArgs = baseArgs.CustomArgs,
            Variants = variants,
        };
        new DefaultPipeline().Run(run);
        if (strict && GenerationContext.Current.AnyValidatorFail)
            throw new InvalidOperationException("Luban 数据校验失败（校验趟 --strict 语义），已中止，未提交任何产物。");
    }

    // ---------- 提交与 CHECK ----------

    private sealed class Stats
    {
        internal int Written;
        internal int Deleted;
    }

    private static void CommitOutputs(string codeOut, string dataRoot, string[] languages,
        string stageCode, string stageDataCommon, string stageDataRoot)
    {
        var stats = new Stats();
        SyncTree(stats, codeOut, stageCode);
        SyncTopLevel(stats, dataRoot, stageDataCommon);
        foreach (string language in languages)
            SyncTree(stats, Path.Combine(dataRoot, language), Path.Combine(stageDataRoot, language));
        DeleteObsoleteLanguageDirs(stats, dataRoot, languages);
        Console.WriteLine($"[moirai] 提交完成：更新 {stats.Written} 个文件，删除 {stats.Deleted} 个过期文件/目录。");
    }

    private static void CheckOutputs(string codeOut, string dataRoot, string[] languages,
        string stageCode, string stageDataCommon, string stageDataRoot)
    {
        var diffs = new List<string>();
        CompareTree(diffs, codeOut, stageCode);
        CompareTopLevel(diffs, dataRoot, stageDataCommon);
        foreach (string language in languages)
            CompareTree(diffs, Path.Combine(dataRoot, language), Path.Combine(stageDataRoot, language));
        CheckObsoleteLanguageDirs(diffs, dataRoot, languages);
        if (diffs.Count != 0)
            throw new InvalidOperationException("CHECK 失败：生成产物与仓库不一致（先跑一次导出并提交产物）：\n" + string.Join("\n", diffs));
        Console.WriteLine("[moirai] CHECK 通过：生成产物与仓库一致。");
    }

    /// <summary>递归同步 stage → real：只写字节有变化或新增的文件；过期文件连同 .meta 删除。</summary>
    private static void SyncTree(Stats stats, string realDir, string stageDir)
    {
        Dictionary<string, byte[]> expected = CollectExpected(stageDir);
        Dictionary<string, string> existing = CollectExisting(realDir);
        foreach ((string relative, byte[] bytes) in expected.OrderBy(pair => pair.Key, PathComparer))
        {
            string target = Path.Combine(realDir, relative);
            if (existing.TryGetValue(relative, out string old) && File.ReadAllBytes(old).AsSpan().SequenceEqual(bytes)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
            stats.Written++;
        }
        foreach (string relative in existing.Keys.Except(expected.Keys, PathComparer))
            DeleteFileWithMeta(stats, Path.Combine(realDir, relative));
        DeleteEmptyDirs(stats, realDir);
    }

    /// <summary>数据根目录只同步顶层文件（语言无关的普通表 bytes）；语言子目录由语言趟与过期目录清理负责。</summary>
    private static void SyncTopLevel(Stats stats, string realDir, string stageDir)
    {
        var expected = Directory.Exists(stageDir)
            ? Directory.GetFiles(stageDir, "*", SearchOption.TopDirectoryOnly)
                .ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, PathComparer)
            : new Dictionary<string, byte[]>(PathComparer);
        var existing = Directory.Exists(realDir)
            ? Directory.GetFiles(realDir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".meta", LanguageConstant.PathComparison))
                .ToDictionary(f => Path.GetFileName(f)!, f => f, PathComparer)
            : new Dictionary<string, string>(PathComparer);
        foreach ((string name, byte[] bytes) in expected.OrderBy(pair => pair.Key, PathComparer))
        {
            string target = Path.Combine(realDir, name);
            if (existing.TryGetValue(name, out string old) && File.ReadAllBytes(old).AsSpan().SequenceEqual(bytes)) continue;
            Directory.CreateDirectory(realDir);
            File.WriteAllBytes(target, bytes);
            stats.Written++;
        }
        foreach (string name in existing.Keys.Except(expected.Keys, PathComparer))
            DeleteFileWithMeta(stats, Path.Combine(realDir, name));
    }

    /// <summary>真实数据根下不属于语言清单的子目录（含 bytes 的语言数据目录）整体删除。</summary>
    private static void DeleteObsoleteLanguageDirs(Stats stats, string dataRoot, string[] languages)
    {
        if (!Directory.Exists(dataRoot)) return;
        foreach (string dir in Directory.GetDirectories(dataRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(dir);
            if (languages.Contains(name, StringComparer.Ordinal)) continue;
            if (!Directory.GetFiles(dir, "*.bytes", SearchOption.AllDirectories).Any()) continue;
            Directory.Delete(dir, true);
            if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta");
            stats.Deleted++;
            Console.WriteLine($"[moirai] 删除过期语言目录：{dir}");
        }
    }

    private static void CompareTree(List<string> diffs, string realDir, string stageDir)
    {
        Dictionary<string, byte[]> expected = CollectExpected(stageDir);
        Dictionary<string, string> existing = CollectExisting(realDir);
        foreach ((string relative, byte[] bytes) in expected.OrderBy(pair => pair.Key, PathComparer))
        {
            if (!existing.TryGetValue(relative, out string old))
                diffs.Add($"缺失（暂存区有、仓库没有）：{Path.Combine(realDir, relative)}");
            else if (!File.ReadAllBytes(old).AsSpan().SequenceEqual(bytes))
                diffs.Add($"内容差异：{old}");
        }
        foreach (string relative in existing.Keys.Except(expected.Keys, PathComparer))
            diffs.Add($"多余（仓库有、本次生成没有）：{Path.Combine(realDir, relative)}");
    }

    private static void CompareTopLevel(List<string> diffs, string realDir, string stageDir)
    {
        Dictionary<string, byte[]> expected = Directory.Exists(stageDir)
            ? Directory.GetFiles(stageDir, "*", SearchOption.TopDirectoryOnly)
                .ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes, PathComparer)
            : new Dictionary<string, byte[]>(PathComparer);
        Dictionary<string, string> existing = Directory.Exists(realDir)
            ? Directory.GetFiles(realDir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".meta", LanguageConstant.PathComparison))
                .ToDictionary(f => Path.GetFileName(f)!, f => f, PathComparer)
            : new Dictionary<string, string>(PathComparer);
        foreach ((string name, byte[] bytes) in expected.OrderBy(pair => pair.Key, PathComparer))
        {
            if (!existing.TryGetValue(name, out string old))
                diffs.Add($"缺失（暂存区有、仓库没有）：{Path.Combine(realDir, name)}");
            else if (!File.ReadAllBytes(old).AsSpan().SequenceEqual(bytes))
                diffs.Add($"内容差异：{old}");
        }
        foreach (string name in existing.Keys.Except(expected.Keys, PathComparer))
            diffs.Add($"多余（仓库有、本次生成没有）：{Path.Combine(realDir, name)}");
    }

    private static void CheckObsoleteLanguageDirs(List<string> diffs, string dataRoot, string[] languages)
    {
        if (!Directory.Exists(dataRoot)) return;
        foreach (string dir in Directory.GetDirectories(dataRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(dir);
            if (languages.Contains(name, StringComparer.Ordinal)) continue;
            if (!Directory.GetFiles(dir, "*.bytes", SearchOption.AllDirectories).Any()) continue;
            diffs.Add($"多余的语言目录：{dir}");
        }
    }

    private static Dictionary<string, byte[]> CollectExpected(string stageDir) => Directory.Exists(stageDir)
        ? Directory.GetFiles(stageDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(stageDir, f), f => File.ReadAllBytes(f), PathComparer)
        : new Dictionary<string, byte[]>(PathComparer);

    private static Dictionary<string, string> CollectExisting(string realDir) => Directory.Exists(realDir)
        ? Directory.GetFiles(realDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta", LanguageConstant.PathComparison))
            .ToDictionary(f => Path.GetRelativePath(realDir, f), f => f, PathComparer)
        : new Dictionary<string, string>(PathComparer);

    private static void DeleteFileWithMeta(Stats stats, string path)
    {
        File.Delete(path);
        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        stats.Deleted++;
    }

    /// <summary>自底向上删除清空后的目录及目录 .meta，避免 Unity 里残留空生成目录。</summary>
    private static void DeleteEmptyDirs(Stats stats, string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (Directory.GetFiles(dir).Length != 0 || Directory.GetDirectories(dir).Length != 0) continue;
            Directory.Delete(dir);
            if (File.Exists(dir + ".meta")) File.Delete(dir + ".meta");
        }
    }

    // ---------- 小工具 ----------

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path : path + Path.DirectorySeparatorChar;

    private static string GetRelativeUnder(string path, string root)
    {
        string prefix = EnsureTrailingSeparator(root);
        if (!path.StartsWith(prefix, LanguageConstant.PathComparison))
            throw new InvalidOperationException($"L10N_LANG_LIST_CODE（{path}）必须位于 CODE_OUTPUT_PATH_CLIENT（{root}）之下，插件路线才能映射语言常量的暂存位置。");
        return path[prefix.Length..].Replace('\\', '/');
    }
}
