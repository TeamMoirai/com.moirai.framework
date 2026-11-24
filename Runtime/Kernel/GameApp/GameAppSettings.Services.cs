using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;

namespace Moirai.Atropos
{
    partial class GameAppSettings
    {
        /// <summary>
        /// 注册 App 作用域服务并启动游戏流程（Composition Root）。
        /// </summary>
        /// <remarks>
        /// 三步固定序：内置清单入图（生成器按 <see cref="ProviderHandlerHostAttribute"/> 发射 <c>WarmHandler</c> 预热） <br />
        /// → <see cref="GameApp.onServicesComposing"/> 交回项目侧注册 → 提交 <see cref="ServiceWorld"/> 的第二阶段。 <br />
        /// 初始化顺序由 <see cref="ServiceDependencyAttribute"/> 拓扑决定、与注册顺序无关，缺失与循环依赖 fail-fast。 <br />
        /// 由 <see cref="GameApp.Boot"/> 在 <c>BeforeSceneLoad</c> 相位调用。
        /// </remarks>
        internal static partial UniTaskVoid InitializeAppServices()
        {
            return Initialize();

            async UniTaskVoid Initialize()
            {
                try
                {
                    // 起步清世界：关闭域重载时编辑器会话的静态世界会泄漏进播放态，boot 必须从干净世界起步才走确定序。
                    GameServices.Shutdown();

                    // 第一阶段：仅入图，顺序无关
                    BuiltinServiceRegistration.RegisterAll(GameServices.Default);

                    // UIService 双轨多缓存：按目录关停档降序注册，容器反序拆除即档位升序收口
                    UIService.RegisterEnabledHandlers();

                    // 项目侧扩展点：此时入图的服务与内置服务一并参与拓扑排序
                    GameApp.InvokeServicesComposing();

                    // 第二阶段：按依赖图拓扑序统一驱动 OnInit
                    await GameServices.Default.InitializeAsync();
                }
                catch (System.Exception exception)
                {
                    // fire-and-forget 的 UniTaskVoid：不接住异常，发布构建会静默吞掉，启动失败只剩一块黑屏。
                    LogUtility.Error("GameApp boot failed: service composition threw. {0}", exception);

                    // 失败回滚：半初始化的世界不得带着 PlayerLoop 继续跑——已激活服务在激活期抛出时已摘出轮询，
                    // 但世界整体仍处未完成态（拓扑中断、依赖未就绪），继续轮询既无意义也不确定。
                    // GameApp.Shutdown 内部已含 GameServices.Shutdown（服务世界 + 清槽广播），一次调用收口全部；
                    // 若它自身抛出（回调异常等），再用 GameServices.Shutdown 兜底关掉服务世界——确保不因启动面
                    // 收尾失败而把半初始化世界留在轮询里。两处 Shutdown 均幂等，重叠调用只多一次空转。
                    // 回滚自身异常不掩盖原始启动失败——逐项隔离记录。
                    try
                    {
                        GameApp.Shutdown();
                    }
                    catch (System.Exception rollbackEx)
                    {
                        LogUtility.Error("GameApp boot rollback: GameApp.Shutdown threw. {0}", rollbackEx);
                        try
                        {
                            GameServices.Shutdown();
                        }
                        catch (System.Exception nestedEx)
                        {
                            LogUtility.Error("GameApp boot rollback: GameServices.Shutdown (fallback) threw. {0}", nestedEx);
                        }
                    }

                    GameApp.RaiseBootFailed(exception);
                }
            }
        }
    }
}
