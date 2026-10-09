using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Scene;
using Moirai.Atropos.UI;
using Moirai.GameLogic.UI;
using UnityEngine;

namespace Moirai.GameLogic
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

            await UniTask.Delay(10 * 1000);
            
            // UI加载（第二个参数是窗口标识：内置资源档拼 Resources 父目录，否则按标识查配置表；标识同时是栈上身份）
            UIService.ShowUIAsync<StartScreen, string>("Loading...", "start");
            
            // 场景加载
            await SceneService.LoadSceneAsync("Assets/AssetRaw/Default/Scene/start.unity");

            // UI关闭
            UIService.CloseUI<StartScreen>("start");
            
            // 播放音频
            var coinsHandle = AudioService.Play("Assets/AssetRaw/Default/Audio/Coins.wav", AudioPlayOptions.CreateLooping(EAudioTrack.Sfx));
            await UniTask.Delay(5 * 1000);
            AudioService.Stop(coinsHandle);

            // await GameObjectPoolService.WarmupAsync("Assets/AssetRaw/Default/UI/Window/StartScreen", 5);
            var instance = GameObjectPoolService.Spawn("Assets/AssetRaw/Default/UI/Window/StartScreen");
            await UniTask.Delay(5 * 1000);
            GameObjectPoolService.Despawn(instance);
        }
    }
}