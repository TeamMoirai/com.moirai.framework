# PlayerLoop 逻辑驱动

> 剥离 MonoBehaviour 的游戏帧逻辑驱动：由 Unity PlayerLoop 直接回调，订阅不随场景/宿主销毁而丢失。

## 背景

旧版 `GameApp` 把 `Update`/`FixedUpdate`/`LateUpdate` 订阅挂在隐藏 Mono 宿主的**实例事件**上。该宿主在初始场景加载前可能被意外销毁，导致全部订阅丢失，服务 Tick 静默停摆。

`Runtime/Services/Timer`（`TimerService`）是统一的计时子系统（四级时间轮 + 帧计时），提供 `Delay` / `WaitFrame` 等能力；它经 `IServiceTickable` 由 `GameServices.Tick` 推进，而 `GameServices.Tick` 本身装配在本驱动的 Update **核心钩子**上（先于全部用户订户，且不参与熔断）。`IUpdateHandler` 面向游戏侧系统与 DI 组合根，二者不要混用。

## 架构

| 组件 | 职责 |
|------|------|
| `internal PlayerLoopDriver`（`Moirai.Atropos`） | 零分配静态注册表 + Drive 入口；框架内部，不对外 |
| `internal PlayerLoopInjector`（`Moirai.Atropos`） | 注入/移除 Unity `PlayerLoopSystem` |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | 接口式逻辑处理器（推荐，DI 友好），经 `GameApp.AddXxxHandler` 注册 |
| `IPlayerLoopPriority` | 可选驱动顺序（数值小者先执行） |
| `GameApp` 静态外观 | **对外唯一入口**：`AddUpdateListener` 等 Action 订阅 + `AddXxxHandler` / `AddFrameHandler` 接口订阅，自身不含 MonoBehaviour |
| `GameAppHost` | 唯一的轻量 Mono 宿主：协程 / Gizmos / ApplicationPause，只做转发 |

注入点：

- `PlayerLoop.Update` 开头 → Framework Update
- `PlayerLoop.FixedUpdate` 开头 → Framework FixedUpdate
- `PlayerLoop.PreLateUpdate` 末尾 → Framework LateUpdate（晚于 MonoBehaviour.LateUpdate）

注入时基于 `GetCurrentPlayerLoop()`，保留 UniTask 等第三方系统。`SubsystemRegistration` 记录默认循环，仅供调试窗 `RestoreDefault` 显式复原；关闭流程用 `RemoveMoiraiSystems()` 只摘本框架的三个标记。

每个 Drive 入口先调用 `GameTime.StartFrame()` 采样本帧时间快照，再依次驱动接口 Handler 与 Action 回调——**两类订阅读到的是同一帧的值**。

## 快速上手

```csharp
using Moirai.Atropos;

// 接口方式：可携带状态、经 DI 注入依赖，并可用 IPlayerLoopPriority 指定阶段内顺序
public sealed class MySystem : IUpdateHandler
{
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // 禁止堆分配
    }
}

// 游戏侧 / 组合根中注册
GameApp.AddUpdateHandler(mySystem);
GameApp.RemoveUpdateHandler(mySystem);

// 一个类实现多个阶段接口时，一次登记其实现的全部阶段
GameApp.AddFrameHandler(myMultiStageSystem);

// 无状态的零散挂钩
GameApp.AddUpdateListener(OnUpdate);
GameApp.RemoveUpdateListener(OnUpdate);
```

> `PlayerLoopDriver` 本身是 **internal**：注册表、`Raise*` 引擎事件转发、`ResetForTests` 一类测试接缝都不对外，
> 游戏侧一律走上面的 `GameApp` 门面。框架内部（含 `InternalsVisibleTo` 白名单程序集）可直接用
> `PlayerLoopDriver.Register` —— 它是同名三重载，传一个多阶段对象会因二义性编译不过，
> 需 `RegisterAll(handler)` 或显式转型 `Register((ILateUpdateHandler)handler)`；
> 门面的 `AddXxxHandler` 按参数类型各自唯一，没有这个问题。

## 零分配契约

`DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` 及所有 Handler 实现：

- 使用 `for` 循环，禁止 LINQ / 闭包 / 字符串拼接
- 驱动中注册/注销进入**所属阶段各自**的延迟缓冲，该阶段迭代结束后提交；跨阶段注册互不串台
- 订阅方抛异常时由 `finally` 复位 driving 标记并提交缓冲，不会永久滞留（异常本身的处置见下节）
- **线程契约**：注册表无锁，注册/注销仅允许主线程（越线程 fail-fast 断言，而非静默丢订阅）；后台线程先经 `MainThreadDispatcher.Post/Send` 回主线程
- Profiler Marker：`PlayerLoopDriver.Update` 等

## 异常处置

订户异常按**编译期分级**处置，与内核 `ServiceScope` 同一约定（`RETHROW_TICK_EXCEPTIONS`）：

| 构建 | 行为 |
|------|------|
| 编辑器 / 开发构建 | `Error` 级记录完整栈后**上抛**——缺陷第一时间暴露，不做静默降级 |
| 发布构建 | `Error` 级记录后**隔离续跑**——单个订户不截断同阶段其余订户 |

连续失败熔断在两档构建下都生效：同一订户在同一阶段**连续**异常达到 `FailureTripThreshold`（默认 300，约 120fps 下 2.5 秒）即被摘出该阶段并 `Warning` 一次——开发构建是先摘除、当帧仍上抛，故熔断在编辑器里也观察得到。成功一次即归零计数，故间歇性故障不会被累计成熔断；重新注册完全重置。

两处豁免，确保订户级故障永远无法反过来禁用框架自身：

- **核心钩子**（`SetCoreUpdateCallback` 等）：`GameServices.Tick/FixedTick/LateTick` 走这里，先于全部用户订户执行，且**永不参与熔断**。框架自身的心跳若能被熔断摘除，一个项目订户的连抛就会让整层服务静默停摆且无恢复路径。核心钩子仍按上表分级处置（开发期上抛、发布期隔离）。
- **关闭 / 销毁广播**（`AddApplicationQuitCallback`、`AddDestroyCallback`）：逐项调用（`GetInvocationList` 有分配，故只用于一次性广播），单项异常不阻止其余项，且**开发构建也不上抛**——这些回调的职责就是清理，截断等于静默漏掉后续每一项的释放动作。

其余 Unity 事件表（`focusChanged`、pause、gizmos）仍是裸多播调用：某一项抛出会截断该次广播中排在后面的订户。

`IUpdateHandler` 实现若是已 `Destroy` 的 `MonoBehaviour`，调用即抛 `MissingReferenceException`；`Drive` 的 `null` 判定只覆盖被摘除的槽位，不替代 Unity 的伪造 null 检查——请在 `OnDestroy` 里显式 `Unregister`。

## 生命周期

| 时机 | 行为 |
|------|------|
| `SubsystemRegistration` | 记录默认 PlayerLoop；Driver 标记 Shutdown；`GameAppHost` 复位退出标记 |
| `GameApp.Initialize`（`BeforeSceneLoad`） | `PlayerLoopDriver.Initialize()` 注入并装配内置核心钩子，随后物化 `GameAppHost`；注入后按循环实况校验三标记，缺失则不置注入标志并告警 |
| `AfterSceneLoad` | 自愈校验：第三方（`BeforeSceneLoad` 及其之前，含同阶段晚于本框架者）基于默认循环重建导致标记丢失时，按循环实况自动补插并告警。**相位不可提前到 `BeforeSceneLoad` 或更早**——注入发生在 `BeforeSceneLoad`，早于它时 `s_Injected` 恒为 false，首行判定即返回，校验永不执行 |
| `GameApp.Shutdown` / 退出 Play | 广播 Destroy → 清空注册表 → `RemoveMoiraiSystems()` 只摘本框架三个标记 → 销毁宿主。**不再** `RestoreDefault` 整条循环 |
| ECS 重置 PlayerLoop 后 | 发生于 `AfterSceneLoad` 之前的重建由自愈校验补插；更晚的重建（如自定义 bootstrap 末尾）需在那之后调用 `PlayerLoopInjector.Reinject()` |

## DI（VContainer 等）

将 Handler 实现注册为服务，在组合根 `InitializeAsync` 后：

```csharp
var system = container.Resolve<MySystem>();
GameApp.AddUpdateHandler(system);
```

驱动与对象创建解耦；注销走 `GameApp.RemoveUpdateHandler(system)`，多阶段系统用
`GameApp.AddFrameHandler` / `RemoveFrameHandler`。

## 兼容注意

- **UniTask**：注入基于当前循环，不覆盖 UniTask 系统。关闭走 `RemoveMoiraiSystems()`，UniTask 的注入**原样保留**，其 `await` 在框架关闭后仍能续跑——退出流程里的异步存档、调试器的 `Shutdown (Restart)`（`GameApp.Shutdown()` 之后接着 `LoadScene`）都依赖这一点。过去这里用 `RestoreDefault`，会把整条引擎默认循环盖回去、连带拆掉 UniTask 的 Pump 且它不会自行重新注入；禁用域重载时退出 Play 还会让编辑模式的 UniTask 停摆到下次域重载。确需复原整条循环时用调试窗的 `RestoreDefault` 按钮，属显式操作。
- **ECS/DOTS**：Entities 可能在 `BeforeSceneLoad` 重置 PlayerLoop——发生于自愈校验之前/早期的重建会被自动补插；若重置在校验之后（如自定义 bootstrap），初始化完成后 `PlayerLoopInjector.Reinject()`。
- **ApplicationPause**：Unity 无纯 C# 事件，由 `GameAppHost.OnApplicationPause` 转发到 `PlayerLoopDriver.RaiseApplicationPause`；订阅存在静态表，宿主重建即恢复派发。
- **Gizmos**：同理，`GameAppHost.OnDrawGizmos(Selected)` 转发到 `PlayerLoopDriver.RaiseDrawGizmos(Selected)`；仅编辑器有派发者，打包后表为空即无副作用。
- **协程**：`GameApp.StartCoroutine` 经 `GameAppHost.Instance` 取用；宿主销毁只影响在跑的协程，不影响任何订阅。

## 编辑器调试

菜单 **Window → PlayerLoop Debugger**：

- 递归打印当前 PlayerLoop，Moirai 注入点标记 `<< Moirai`
- 显示各阶段 Handler / Callback 数量
- 按钮：Ensure Injected / Reinject / Restore Default

---
[« 返回文档索引](Index.md) · [GameApp](GameApp.md)
