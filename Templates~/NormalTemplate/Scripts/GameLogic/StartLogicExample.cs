using Moirai.Atropos;
using GameLogic.UI;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Localization;
using Moirai.Atropos.UI;

namespace GameLogic
{
    public static partial class HotfixEntry
    {
        private static partial void StartGameLogic()
        {
            LogUtility.Info("Starting GameLogic...");
            UIService.ShowUIAsync<StartScreen>("StartScreen", GetWindowLocation("start"), false, "Start Screen");

            // 多语言测试
            LogUtility.Warning("Test Localization => {0}",
                LocalizationService.Localize("[l10n]test:{l10n:test} | [i18n]test_only_zh:{i18n:test_only_zh} | [g11n]test_only_en:{g11n:test_only_en}"));
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