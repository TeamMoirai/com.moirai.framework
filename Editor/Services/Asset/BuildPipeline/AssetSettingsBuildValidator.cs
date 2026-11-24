using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 构建期设置自检：出包前复用运行期那份「只报不改」的判据 <see cref="AssetServiceSettings.GetConfigurationIssues"/>，配置有问题时报出。
    /// </summary>
    /// <remarks>
    /// 发布构建（非 Development 包）默认开严——坏配置直接挡包；开发构建保持只告警，不干扰日常出包调试。 <br />
    /// 环境变量是显式覆写通道：设为 <c>1</c> 强制开严（含开发构建），设为 <c>0</c> 强制关严（发布构建也放行，责任自负）， <br />
    /// 未设置时按构建档取默认。
    /// </remarks>
    public class AssetSettingsBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>开严开关：显式覆写通道——1 强制开严、0 强制关严，未设置按构建档取默认（发布构建开严）。</summary>
        public const string STRICT_ENVIRONMENT_VARIABLE = "MOIRAI_RESOURCE_SETTINGS_STRICT";

        /// <summary>排在资源清单生成之后：那条会写 StreamingAssets，配置问题该在产物落盘后再报。</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            AssetServiceSettings settings = LoadSettingsWithoutCreating();
            if (settings == null)
            {
                return;
            }

            var issues = new AssetSettingsIssue[8];
            int count = settings.GetConfigurationIssues(issues, issues.Length);
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Debug.LogWarning($"[ResourceSettings] {issues[i].Detail}");
            }

            if (IsStrictMode(report))
            {
                throw new BuildFailedException(
                    $"AssetServiceSettings has {count} configuration issue(s) and strict mode is active " +
                    $"({STRICT_ENVIRONMENT_VARIABLE}); see the warnings above.");
            }
        }

        /// <summary>
        /// 只读地取设置资产，资产缺失时不创建。
        /// </summary>
        /// <remarks>
        /// 刻意不走 <see cref="AssetServiceSettings.Instance"/>：那条路径在资产缺失时会新建并写入一份。
        /// </remarks>
        private static AssetServiceSettings LoadSettingsWithoutCreating()
        {
            return Resources.Load<AssetServiceSettings>("AssetServiceSettings");
        }

        /// <summary>
        /// 是否开严：非 Development 包默认开严；环境变量为显式覆写通道（1 开 / 0 关），未设置按构建档取默认。
        /// </summary>
        /// <remarks>
        /// 判据取自本次构建的 <see cref="BuildOptions"/>（<c>Development</c> 位），不读编辑器全局态。
        /// </remarks>
        private static bool IsStrictMode(BuildReport report)
        {
            string value = Environment.GetEnvironmentVariable(STRICT_ENVIRONMENT_VARIABLE);
            if (!string.IsNullOrEmpty(value))
            {
                return value != "0";
            }

            return (report.summary.options & BuildOptions.Development) == 0;
        }
    }
}
