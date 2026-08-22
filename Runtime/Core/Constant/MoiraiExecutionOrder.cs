namespace Moirai.Atropos
{
    internal static class MoiraiExecutionOrder
    {
        /// <summary>
        /// <see cref="GameApp"/>游戏入口的执行顺序
        /// </summary>
        public const int GAME_APP_ORDER = -10000;

        /// <summary>
        /// 游戏框架一些设置的执行顺序
        /// </summary>
        public const int SETTINGS_ORDER = GAME_APP_ORDER - 10;
    }
}