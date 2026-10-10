using System;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 构建期渠道语言烘焙钩子：出包前读取命令行参数 <c>localizationLanguage</c>，非空即按值烘焙 <c>LocalizationBuildConfig</c>，缺省不动现有烘焙产物。
    /// </summary>
    /// <remarks>
    /// 参数须为 <c>-CustomArgs:</c> 片段，如 <c>-CustomArgs:platform=Android;localizationLanguage=zh-Hans</c>。 <br />
    /// 前缀缺失的构建（GUI 发起、测试玩家构建）按「缺省不动」静默早退；前缀存在而键缺失则报错并判构建失败。 <br />
    /// 解析失败抛异常进构建报告。
    /// </remarks>
    public sealed class LocalizationChannelBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            // 无任何 -CustomArgs: 片段的构建（GUI 发起、测试玩家构建）按「缺省不动」早退；
            // 不先探前缀就取参，GetCustomArgument 的缺参 LogError 会把无参构建整个判死。
            if (!Environment.GetCommandLineArgs().Any(arg => arg.Contains("-CustomArgs:")))
            {
                return;
            }

            // CommandLineReader 位于全局命名空间（与 ReleaseTools 同一用法）
            var language = CommandLineReader.GetCustomArgument("localizationLanguage");
            if (string.IsNullOrEmpty(language)) return;

            LocalizationBuildBaker.BakeLanguage(language);
        }
    }
}
