using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 构建期渠道语言烘焙钩子：出包前读取 CLI 参数 <c>localizationLanguage</c>（须为命令行参数片段），
    /// 非空即按值烘焙 <c>LocalizationBuildConfig</c>；缺省不动现有烘焙产物。
    /// <para>CI 约定：<c>-CustomArgs:platform=Android;localizationLanguage=zh-Hans</c>。
    /// 解析失败抛异常进构建报告——错渠道的包不能悄悄落地。</para>
    /// </summary>
    public sealed class LocalizationChannelBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            // CommandLineReader 位于全局命名空间（与 ReleaseTools 同一用法）
            var language = CommandLineReader.GetCustomArgument("localizationLanguage");
            if (string.IsNullOrEmpty(language)) return;

            LocalizationBuildBaker.BakeLanguage(language);
        }
    }
}
