using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Save.Editor
{
    /// <summary>
    /// 构建期存档配置自检：出包前复用运行期判据，报出占位密钥与解析不到的默认序列化后端。
    /// </summary>
    /// <remarks>
    /// 默认只告警；设环境变量 <c>MOIRAI_SAVE_SETTINGS_STRICT=1</c>（值非 "0" 即视为开严）改为硬失败。 <br />
    /// 运行期刻意不拦：已有存档可能正是用占位密钥写下的，把它升级成打不开的存档比配置没改更糟； <br />
    /// 默认后端名字配错同理——回退内置 JSON 仍读写自洽，但那份档从此不是操作者以为的格式，故必须在出包前说一次。
    /// </remarks>
    public class SaveSettingsBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>开严开关：CI 上想让配置瑕疵直接挡包时设成 1。</summary>
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

            List<string> problems = CollectProblems(settings);
            if (problems.Count == 0)
            {
                return;
            }

            string detail = string.Join(" ", problems);
            Debug.LogWarning($"[SaveSettings] {detail}");

            if (IsStrictMode())
            {
                throw new BuildFailedException($"SaveServiceSettings has unresolved configuration and " +
                    $"{StrictEnvironmentVariable} is set; {detail}");
            }
        }

        /// <summary>
        /// 逐项跑配置自检判据，收集要报的问题。
        /// </summary>
        /// <param name="settings">已加载的设置资产（缺资产时不会被调用）。</param>
        /// <returns>待上报的问题清单，全部通过时为空。</returns>
        private static List<string> CollectProblems(SaveServiceSettings settings)
        {
            var problems = new List<string>(2);

            if (settings.UsesPlaceholderSaveKey)
            {
                problems.Add("存档密钥仍是出厂占位值（SECURITY: 发布前替换为项目专属口令与盐），当前加密等价于不加密；" +
                             "详见 SaveServiceSettings → AESEncryptedSaveHandler → StaticSaveKeyProvider。");
            }

            if (!settings.DefaultBackendResolves(out string configuredName))
            {
                problems.Add($"默认序列化后端配的类型名解析不到 ISaveSerializer 实现（配置值：'{configuredName}'），" +
                             "存档会静默按内置 JSON 写；详见 SaveServiceSettings → Default Backend。");
            }

            return problems;
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
        /// 只读地取设置资产，资产缺失时不创建。
        /// </summary>
        /// <remarks>
        /// 刻意不走 <see cref="FrameworkSettings{T}.Instance"/>：那条路径在资产缺失时会新建并写入一份。
        /// </remarks>
        private static SaveServiceSettings LoadSettingsWithoutCreating()
        {
            return Resources.Load<SaveServiceSettings>("SaveServiceSettings");
        }
    }
}
