using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 构建期设置自检：出包前复用运行期那份「只报不改」的判据 <see cref="ResourceServiceSettings.GetConfigurationIssues"/>，配置有问题时报出。
    /// </summary>
    /// <remarks>
    /// 默认只告警；设环境变量 <c>MOIRAI_RESOURCE_SETTINGS_STRICT=1</c>（值非 "0" 即视为开严）改为硬失败。
    /// </remarks>
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
        /// 只读地取设置资产，资产缺失时不创建。
        /// </summary>
        /// <remarks>
        /// 刻意不走 <see cref="ResourceServiceSettings.Instance"/>：那条路径在资产缺失时会新建并写入一份。
        /// </remarks>
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
