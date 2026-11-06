using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Moirai.Atropos.Audio.Editor
{
    /// <summary>
    /// 构建期音频后端能力自检：出包前把播放选项资产里「用到了当前选定后端不消费的能力」逐条报出来。
    /// </summary>
    /// <remarks>
    /// 默认只告警；设环境变量 <c>MOIRAI_AUDIO_SETTINGS_STRICT</c>（值非 "0" 即视为开严）改为硬失败。 <br />
    /// 判据在运行期热路径上不存在——本类是这条语义分叉唯一的事前出口，能力取自后端自报，缺资产时只读不新建。 <br />
    /// 资产面只携带时长裁剪与独奏两项（播放选项资产没有起播延迟字段），延迟起播那条判据留给代码侧配置的将来。
    /// </remarks>
    public class AudioBackendCapabilityBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>开严开关：CI 上想让语义分叉直接挡包时设成 1。</summary>
        public const string StrictEnvironmentVariable = "MOIRAI_AUDIO_SETTINGS_STRICT";

        /// <summary>排在资源清单生成之后：配置问题该在产物落盘后再报。</summary>
        public int callbackOrder => 100;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            AudioServiceSettings settings = LoadSettingsWithoutCreating();
            if (settings == null)
            {
                return;
            }

            AudioBackendCapabilities capabilities = settings.BackendCapabilities;
            var problems = new List<AudioCapabilityIssue>(8);
            CollectAssetIssues(capabilities, problems);
            if (problems.Count == 0)
            {
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogWarning($"[AudioBackendCapability] {problems[i].AssetPath} → {problems[i].Field}：{problems[i].Detail}");
            }

            if (IsStrictMode())
            {
                throw new BuildFailedException($"AudioPlayOptionsSO 有 {problems.Count} 处用到当前音频后端不消费的能力，" +
                    $"且 {StrictEnvironmentVariable} 已设置；详见上方的 Warning。");
            }
        }

        /// <summary>
        /// 逐份播放选项资产跑纯判据，把冲突追加到 <paramref name="problems"/>。
        /// </summary>
        /// <param name="capabilities">当前选定后端自报的能力快照。</param>
        /// <param name="problems">结论收集容器。</param>
        private static void CollectAssetIssues(in AudioBackendCapabilities capabilities, List<AudioCapabilityIssue> problems)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioPlayOptionsSO");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                AudioPlayOptionsSO asset = AssetDatabase.LoadAssetAtPath<AudioPlayOptionsSO>(assetPath);
                if (asset == null)
                {
                    continue;
                }

                AudioBackendCapabilityPolicy.CollectIssues(capabilities, ToPlayOptions(asset), assetPath, problems);
            }
        }

        /// <summary>
        /// 把资产上的时间/独奏配置折成判据用的选项值。
        /// </summary>
        /// <remarks>
        /// 随机区间取上界——区间上界非正时随机结果也不可能落在会被裁剪的正区间上；<c>InitialDelay</c> 无对应字段，保持默认。
        /// </remarks>
        private static AudioPlayOptions ToPlayOptions(AudioPlayOptionsSO asset)
        {
            AudioPlayOptions options = AudioPlayOptions.Default;
            Vector2 time = asset.PlaybackTime;
            Vector2 duration = asset.PlaybackDuration;
            options.PlaybackTime = Mathf.Max(time.x, time.y);
            options.PlaybackDuration = Mathf.Max(duration.x, duration.y);
            options.SoloMode = asset.SoloMode;
            return options;
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
        private static AudioServiceSettings LoadSettingsWithoutCreating()
        {
            return Resources.Load<AudioServiceSettings>("AudioServiceSettings");
        }
    }
}
