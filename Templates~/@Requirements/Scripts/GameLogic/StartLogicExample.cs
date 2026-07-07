using Moirai.Atropos;
using GameLogic.UI;
using Moirai.Atropos.ConfigTable;

namespace GameLogic
{
    public sealed partial class HotfixEntry
    {
        private static partial void StartGameLogic()
        {
            Log.Info("Starting GameLogic...");
            GameModule.UI.ShowUIAsync<StartScreen>("StartScreen", GetWindowLocation("start"), false, "Start Screen");
        }

        /// <summary>
        /// 从配置表获取弹窗资产的位置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private static string GetWindowLocation(string id)
        {
            // Log.Info($"Load UI: {id}");
            return ConfigMgr.Instance.GetUIWindowLocation(id);
        }
    }
}