using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using GameLogic.UI;
using Moirai.Atropos.Audio;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Scene;
using Moirai.Atropos.UI;
using UnityEngine;

namespace GameLogic
{
    public static partial class HotfixEntry
    {
        private static partial void StartGameLogic()
        {
            LogUtility.Info("Starting GameLogic...");
            TestService().Forget();
        }

        private static async UniTaskVoid TestService()
        {
            // 多语言测试
            LogUtility.Debug("Test Localization => {0}",
                LocalizationService.Localize("[l10n]test:{l10n:test} | [i18n]test_only_zh:{i18n:test_only_zh} | [g11n]test_only_en:{g11n:test_only_en}"));

            // UI加载
            UIService.ShowUIAsync<StartScreen>("StartScreen", GetWindowLocation("start"), false, "Loading...");            
            
            // 场景加载
            await SceneService.LoadSceneAsync("Assets/AssetRaw/Default/Scene/start.unity");

            // UI关闭
            UIService.CloseUI<StartScreen>("StartScreen");
            
            // 播放音频
            var coinsHandle = AudioService.Play("Assets/AssetRaw/Default/Audio/Coins.wav", EAudioTrack.Sfx, Vector3.zero, loop:true);
            await UniTask.Delay(5 * 1000);
            AudioService.Stop(coinsHandle);
        }

        /// <summary>
        /// 从配置表获取弹窗资产的位置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private static string GetWindowLocation(string id)
        {
            // LogUtility.Info("Load UI: {0}", id);
            return ConfigTableService.GetUIWindowLocation(id);
        }
    }
}