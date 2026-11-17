namespace Moirai.LubanLocalization;

/// <summary>
/// 单趟管线内的"当前趟"上下文：本趟代码/数据的暂存落点，以及是否丢弃产物（校验趟）。
/// 输出永远先进暂存区，管线在全部趟次完成后统一做字节级提交或 CHECK 比对。
/// </summary>
internal sealed class MoiraiStage : IDisposable
{
    internal static MoiraiStage? Current;

    // 与旧管线一致：暂存生成在当前目录（Config/）下的 Temp/ 里（已被 .gitignore 覆盖），
    // 不落到系统临时目录（C 盘），避免跨盘与残留难寻。
    private static readonly string RootBase = Path.Combine("Temp", "moirai_stage");

    private readonly string _root = Path.Combine(RootBase, Guid.NewGuid().ToString("N"));

    internal string Root => _root;
    internal string CodeDirectory { get; private set; } = string.Empty;
    internal string DataDirectory { get; private set; } = string.Empty;
    internal bool Discard { get; private set; }

    internal MoiraiStage()
    {
        Directory.CreateDirectory(_root);
        Current = this;
    }

    internal string DiscardDirectory => Path.Combine(_root, "discard");

    internal void BeginRun(string codeDirectory, string dataDirectory, bool discard)
    {
        CodeDirectory = codeDirectory;
        DataDirectory = dataDirectory;
        Discard = discard;
        Directory.CreateDirectory(codeDirectory);
        Directory.CreateDirectory(dataDirectory);
    }

    public void Dispose()
    {
        Current = null;
        Directory.Delete(_root, true);
    }
}
