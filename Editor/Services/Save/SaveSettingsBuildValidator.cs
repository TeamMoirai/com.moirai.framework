using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Save.Editor
{
    /// <summary>
    /// 构建期存档密钥自检：把 <see cref="SaveServiceSettings.UsesPlaceholderSaveKey"/> 这条运行期判据在出包前再走一遍，
    /// 而不是在编辑器里另写一套规则。
    /// <para>占位口令/盐随包发布，任何人都能派生同一把密钥——「加密存档」当场失效。
    /// 已有存档可能正是用占位值写成的，所以运行期不拦（拦了把配置问题升级成存档打不开），只在出包这一步报。</para>
    /// <para><b>默认只告警</b>：本包被他人消费，因为一项配置把别人的构建拦停是工单，不是提醒。
    /// 需要硬失败时设环境变量 <c>MOIRAI_SAVE_SETTINGS_STRICT=1</c>（值非 "0" 即视为开严）。</para>
    /// </summary>
    public class SaveSettingsBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>开严开关：CI 上想让占位密钥直接挡包时设成 1。</summary>
        public const string StrictEnvironmentVariable = "MOIRAI_SAVE_SETTINGS_STRICT";

        /// <summary>排在资源清单生成之后：配置问题该在产物落盘后再报。</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            SaveServiceSettings settings = LoadSettingsWithoutCreating();
            if (settings == null)
            {
                return;
            }

            if (!settings.UsesPlaceholderSaveKey)
            {
                return;
            }

            const string detail = "存档密钥仍是出厂占位值（SECURITY: 发布前替换为项目专属口令与盐），" +
                                  "当前加密等价于不加密。详见 SaveServiceSettings → AESEncryptedSaveHandler → StaticSaveKeyProvider。";
            Debug.LogWarning($"[SaveSettings] {detail}");

            if (IsStrictMode())
            {
                throw new BuildFailedException(
                    $"SaveServiceSettings still uses the placeholder save key and {StrictEnvironmentVariable} is set; {detail}");
            }
        }

        /// <summary>
        /// 是否开严。读取 <see cref="StrictEnvironmentVariable"/>，缺省为关。
        /// </summary>
        internal static bool IsStrictMode()
        {
            string value = Environment.GetEnvironmentVariable(StrictEnvironmentVariable);
            return !string.IsNullOrEmpty(value) && value != "0";
        }

        /// <summary>
        /// 只读地取设置资产。
        /// <para>刻意不走 <see cref="FrameworkSettings{T}.Instance"/>：那条路径在资产缺失时会新建并写入一份，
        /// 在构建回调里往工程写资产不是我们能替用户做的决定。</para>
        /// </summary>
        private static SaveServiceSettings LoadSettingsWithoutCreating()
        {
            return Resources.Load<SaveServiceSettings>("SaveServiceSettings");
        }
    }
}
