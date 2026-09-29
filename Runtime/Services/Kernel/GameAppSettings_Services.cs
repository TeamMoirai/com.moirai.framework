using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    partial class GameAppSettings
    {
        /// <summary>
        /// 注册 App 作用域服务并启动游戏流程（Composition Root）。
        /// </summary>
        /// <remarks>
        /// 顺序固定： ① 内置服务注册清单（源生成器按 <see cref="AutoRegisterServiceAttribute"/> 标记生成）→ ② <br />
        /// <see cref="GameApp.ServicesComposing"/> 交回项目侧注册自有 App 服务 → ③ <see cref="ServiceWorld.InitializeAsync"/> 提交两阶段构建的第二阶段。
        /// 初始化顺序由 <see cref="ServiceDependencyAttribute"/> 声明拓扑决定、与注册顺序无关，缺失依赖与循环依赖 fail-fast。 <br />
        /// 需要调试面板的服务应声明 <c>[ServiceDependency(typeof(DebuggerService))]</c> 以保证拓扑先行。 <br />
        /// 由 <see cref="GameApp.Boot"/> 调用，触发点 <see cref="GameAppSettings.Initiation"/> 相位为 <c>BeforeSceneLoad</c>。
        /// </remarks>
        internal static partial UniTaskVoid InitializeAppServices()
        {
            return Initialize();

            async UniTaskVoid Initialize()
            {
                try
                {
                    // 注册（第一阶段：仅入图，顺序无关；清单由源生成器按 [AutoRegisterService] 生成）
                    BuiltinServiceRegistration.RegisterAll(GameServices.Default);

                    // 项目侧扩展点：此时入图的服务与内置服务一并参与下面的拓扑排序
                    GameApp.InvokeServicesComposing();

                    // 初始化（第二阶段：依赖图拓扑排序统一驱动 OnInit）
                    await GameServices.Default.InitializeAsync();
                }
                catch (System.Exception exception)
                {
                    // 本方法是 fire-and-forget 的 UniTaskVoid：不接住异常，Forget() 在发布构建会
                    // 静默吞掉它，启动失败就只剩一块黑屏。先记录再交回项目上报。
                    LogUtility.Error("GameApp boot failed: service composition threw. {0}", exception);
                    GameApp.RaiseBootFailed(exception);
                }
            }
        }
    }
}
