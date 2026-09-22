using Moirai.Atropos;
using UnityEngine;

namespace Moirai.Atropos.Tests.Player
{
    /// <summary>
    /// 玩家测试宿主的启动前置：把框架的自动启动掐掉。
    /// <para>玩家里 <see cref="GameApp"/> 默认经 <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> 自动启动
    /// （<c>GameAppSettings.Initiation</c> 里一句 <c>if (GameApp.AutoBoot) GameApp.Boot()</c>）。测试玩家跑的是空场景，
    /// 启动链在 <c>UGUIHandler.OnInit</c> 缺 UIRoot 处报 <c>[FAT] UIRoot not found!</c> 后停住——实测带
    /// <c>-runTests</c> 与不带 <c>-runTests</c> 单独启动同样停在那一行，测试运行根本轮不到，玩家侧门禁因此从未真正执行过。</para>
    /// <para>这里在 <c>AfterAssembliesLoaded</c>（早于读取点 <c>BeforeSceneLoad</c>）用框架自带的
    /// <see cref="GameApp.AutoBoot"/> 开关把启动时机收回，让玩家变成一个干净的测试宿主。本程序集只随
    /// 玩家测试构建存在（<c>UNITY_INCLUDE_TESTS</c> + <c>!UNITY_EDITOR</c>），生产包不含它。</para>
    /// <para>需要框架已启动的玩家用例应自行调用 <see cref="GameApp.Boot"/> 并自备场景依赖，不要依赖本前置被移除。</para>
    /// </summary>
    internal static class PlayerTestBootstrap
    {
        /// <summary>
        /// 关闭框架自动启动（玩家测试宿主专用）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void DisableFrameworkAutoBoot()
        {
            GameApp.AutoBoot = false;
        }
    }
}
