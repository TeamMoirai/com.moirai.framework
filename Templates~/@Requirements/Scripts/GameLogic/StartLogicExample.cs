using Moirai.Atropos;
using Moirai.Atropos.UI;
using GameLogic.UI;
using Moirai.Atropos.ConfigTable;

namespace GameLogic
{
    public static partial class HotfixEntry
    {
        private static partial void StartGameLogic()
        {
            LogUtility.Info("Starting GameLogic...");
            UIService.ShowUIAsync<StartScreen>("StartScreen", GetWindowLocation("start"), false, "Start Screen");
        }

        /// <summary>
        /// 从配置表获取弹窗资产的位置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private static string GetWindowLocation(string id)
        {
            // LogUtility.Info($"Load UI: {id}");
            return ConfigMgr.GetUIWindowLocation(id);
        }
    }
}