using Cysharp.Threading.Tasks;
using Moirai.Atropos.Audio;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Input;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Save;
using Moirai.Atropos.Scene;
using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;

namespace Moirai.Atropos
{
    public partial class GameAppSettings
    {
        /// <summary>
        /// 注册 App 作用域服务并启动游戏流程（Composition Root）。
        /// <para>① 无序注册全部 App 服务——服务实例仅由手动注册创建，
        /// <see cref="ServiceDependencyAttribute"/> 声明在世界初始化时做拓扑排序
        /// （缺失依赖与循环依赖 fail-fast），初始化顺序由声明决定、与注册顺序无关；</para>
        /// <para>② <see cref="ServiceWorld.InitializeAsync"/> 提交两阶段构建的第二阶段；</para>
        /// <para>调试器依赖：各服务 OnInit 经 <see cref="DebuggerService"/> 注册调试面板——
        /// 需要调试面板的服务应声明 <c>[ServiceDependency(typeof(DebuggerService))]</c> 以保证拓扑序。</para>
        /// <para>由 <see cref="GameAppSettings.Initiation"/> 在 <c>AfterAssembliesLoaded</c> 阶段调用。</para>
        /// </summary>
        private static partial UniTaskVoid InitializeAppServices()
        {
            return Initialize();

            async UniTaskVoid Initialize()
            {
                // 注册（第一阶段：仅入图，顺序无关）
                GameServices.RegisterService(EServiceScopeKind.App, new DebuggerService());
                GameServices.RegisterService(EServiceScopeKind.App, new ResourceService());
                GameServices.RegisterService(EServiceScopeKind.App, new TimerService());
                GameServices.RegisterService(EServiceScopeKind.App, new ObjectPoolService());
                GameServices.RegisterService(EServiceScopeKind.App, new GameObjectPoolService());
                GameServices.RegisterService(EServiceScopeKind.App, new LocalizationService());
                GameServices.RegisterService(EServiceScopeKind.App, new UIService());
                GameServices.RegisterService(EServiceScopeKind.App, new SceneService());
                GameServices.RegisterService(EServiceScopeKind.App, new AudioService());
                GameServices.RegisterService(EServiceScopeKind.App, new InputService());
                GameServices.RegisterService(EServiceScopeKind.App, new SaveService());
                GameServices.RegisterService(EServiceScopeKind.App, new ConfigTableService());
                GameServices.RegisterService(EServiceScopeKind.App, new ProcedureService());

                // 初始化（第二阶段：依赖图拓扑排序统一驱动 OnInit）
                await GameServices.Default.InitializeAsync();
            }
        }
    }
}
