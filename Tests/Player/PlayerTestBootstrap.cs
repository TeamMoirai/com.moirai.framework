using Moirai.Atropos;
using UnityEngine;

namespace Moirai.Atropos.Tests.Player
{
    /// <summary>
    /// 玩家测试宿主的启动前置：在玩家域掐掉框架的自动启动，使玩家成为干净的测试宿主。
    /// </summary>
    /// <remarks>
    /// 玩家里 <see cref="GameApp"/> 默认经 <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> 自动启动；测试玩家跑空场景，
    /// UI 后端等不到 <c>UIRootBinding</c> 登记，启动链在 <c>UGUIHandler</c> 报「UI 根尚未绑定」后停住，测试运行轮不到。
    /// 本前置在 <c>AfterAssembliesLoaded</c>（早于读取点 <c>BeforeSceneLoad</c>）经框架自带的 <see cref="GameApp.AutoBoot"/> 开关收回启动时机。
    /// 掐 <c>AutoBoot</c> 仅玩家域生效（<c>#if !UNITY_EDITOR</c>）：编辑器 PlayMode 测试域依赖自动启动链驱动框架与服务，编辑器里绝不能掐。
    /// 本程序集只在含测试程序集的构建里存在（<c>UNITY_INCLUDE_TESTS</c>），生产包不含它。
    /// 需要框架已启动的玩家用例应自行调用 <see cref="GameApp.Boot"/> 并自备场景依赖，不要依赖本前置被移除。
    /// </remarks>
    internal static class PlayerTestBootstrap
    {
        /// <summary>
        /// 关闭框架自动启动（玩家测试宿主专用）。
        /// </summary>
        /// <remarks>
        /// 仅玩家域生效——编辑器 PlayMode 测试域依赖 <c>AutoBoot</c> 链把框架与服务驱动起来（L2 门禁前提），编辑器里绝不能掐。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void DisableFrameworkAutoBoot()
        {
#if !UNITY_EDITOR
            GameApp.AutoBoot = false;
#endif
        }
    }
}
