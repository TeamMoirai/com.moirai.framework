using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 构建期设置自检：把运行期那份"只报不改"的判据（<see cref="ResourceServiceSettings.GetConfigurationIssues"/>）
    /// 在出包前原样再走一遍，而不是在编辑器里另写一套规则。
    /// <para><b>默认只告警</b>：本包被他人消费，因为一项配置把别人的构建拦停是工单，不是提醒。
    /// 需要硬失败时设环境变量 <c>MOIRAI_RESOURCE_SETTINGS_STRICT=1</c>（值非 "0" 即视为开严）。</para>
    /// </summary>
    public class ResourceSettingsBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>开严开关：CI 上想让坏配置直接挡包时设成 1。</summary>
        public const string StrictEnvironmentVariable = "MOIRAI_RESOURCE_SETTINGS_STRICT";

        /// <summary>排在资源清单生成之后：那条会写 StreamingAssets，配置问题该在产物落盘后再报。</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            ResourceServiceSettings settings = LoadSettingsWithoutCreating();
            if (settings == null)
            {
                return;
            }

            var issues = new ResourceSettingsIssue[8];
            int count = settings.GetConfigurationIssues(issues, issues.Length);
            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Debug.LogWarning($"[ResourceSettings] {issues[i].Detail}");
            }

            if (IsStrictMode())
            {
                throw new BuildFailedException(
                    $"ResourceServiceSettings has {count} configuration issue(s) and {StrictEnvironmentVariable} is set; " +
                    "see the warnings above.");
            }
        }

        /// <summary>
        /// 只读地取设置资产。
        /// <para>刻意不走 <see cref="ResourceServiceSettings.Instance"/>：那条路径在资产缺失时会
        /// <b>新建并写入一份</b>，在构建回调里往工程写资产不是我们能替用户做的决定。</para>
        /// </summary>
        private static ResourceServiceSettings LoadSettingsWithoutCreating()
        {
            return Resources.Load<ResourceServiceSettings>("ResourceServiceSettings");
        }

        private static bool IsStrictMode()
        {
            string value = Environment.GetEnvironmentVariable(StrictEnvironmentVariable);
            return !string.IsNullOrEmpty(value) && value != "0";
        }
    }
}
