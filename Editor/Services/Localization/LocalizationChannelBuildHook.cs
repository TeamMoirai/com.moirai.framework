using System;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 构建期渠道语言烘焙钩子：出包前读取 CLI 参数 <c>localizationLanguage</c>（须为命令行参数片段），
    /// 非空即按值烘焙 <c>LocalizationBuildConfig</c>；缺省不动现有烘焙产物。
    /// <para>CI 约定：<c>-CustomArgs:platform=Android;localizationLanguage=zh-Hans</c>。
    /// 解析失败抛异常进构建报告——错渠道的包不能悄悄落地。</para>
    /// <para>取参前先探 <c>-CustomArgs:</c> 前缀存在性：命令行根本没有自定义参数的构建（编辑器 GUI 发起、
    /// UTF 测试玩家构建等）按「缺省不动」静默早退——<c>CommandLineReader.GetCustomArgument</c> 在前缀或键
    /// 缺失时 LogError，而预处理钩子里的 LogError 会直接判构建失败，不能拿它当无参构建的默认路径
    /// （2026-09-28 L3 首跑因此被挡）。前缀在而键缺失仍走响亮报错，CI 错渠道的包不能悄悄落地。</para>
    /// </summary>
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
