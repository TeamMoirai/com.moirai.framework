using Luban;
using Luban.OutputSaver;

namespace Moirai.LubanLocalization;

/// <summary>
/// moirai-l10n 管线专用的暂存 saver：把每趟产物改写到暂存区，而不是直接写真实目录。
/// - GetOutputDir 覆盖：代码/数据分别落 MoiraiStage 当前趟指定的暂存子目录；
/// - BeforeSave 覆盖：暂存区不做任何清理（旧路线里 saver 清理输出目录正是趟序陷阱的根源），
///   真实目录的过期清理由管线的提交阶段统一负责；
/// - Discard：校验趟只加载与校验数据，不落任何文件。
/// </summary>
[OutputSaver("moirai-stage")]
public sealed class MoiraiStageSaver : LocalFileSaver
{
    protected override string GetOutputDir(OutputFileManifest manifest)
    {
        MoiraiStage stage = MoiraiStage.Current
            ?? throw new InvalidOperationException("moirai-stage 尚未初始化：本 saver 只能由 moirai-l10n 管线使用。");
        return manifest.OutputType == OutputType.Code ? stage.CodeDirectory : stage.DataDirectory;
    }

    protected override void BeforeSave(OutputFileManifest outputFileManifest, string outputDir)
    {
    }

    public override void SaveFile(OutputFileManifest fileManifest, string outputDir, OutputFile outputFile)
    {
        if (MoiraiStage.Current!.Discard) return;
        base.SaveFile(fileManifest, outputDir, outputFile);
    }
}
