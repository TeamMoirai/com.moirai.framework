# GameApp 帧驱动与宿主

> 为非 MonoBehaviour 代码提供 Unity 生命周期代理：协程托管、帧更新注入与 Unity 事件注入。

## 架构变更（重要）

帧逻辑订阅已从 MonoBehaviour 宿主迁移到 **`PlayerLoopDriver`**（`Runtime/Core/GameApp/PlayerLoop`，命名空间 `Moirai.Atropos`，类型为 `internal`，游戏侧经 `GameApp` 门面使用）：

- 订阅存储在 **静态注册表**，不挂在任何 GameObject 上
- 场景切换、宿主被意外销毁 **不会丢失** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy`/`Gizmos`/`Pause` 订阅
- 旧问题：帧订阅挂在隐藏 Mono 宿主的实例事件上，初始场景加载前宿主可能被销毁，订阅全部失效

`GameApp` 本身**不再含任何 MonoBehaviour**（既无嵌套宿主，也无 GameObject 字段）。Unity 只在 MonoBehaviour 上派发的消息——协程、`OnDrawGizmos(Selected)`、`OnApplicationPause`——集中由 `GameAppHost`（`Runtime/Core/GameApp/GameAppHost.cs`）这一个 `SingletonMono_Persistent` 宿主承接；它只做**转发**，订阅仍留在 Driver 的静态表里，因此宿主销毁或重建都不丢订阅。

详细设计见 [PlayerLoopDriver](PlayerLoopDriver.md)。

## 核心特性

- 协程托管：`GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- 帧更新注入：`GameApp.AddUpdateListener`（Action）与 `GameApp.AddUpdateHandler` / `AddFrameHandler`（接口式，支持 `IPlayerLoopPriority`）均 **同步** 写入驱动注册表（不再 `UniTask.Yield` 延迟挂载）
- Unity 事件：`AddDestroyListener`（Shutdown 时广播）、`AddOnApplicationPauseListener`、Gizmos 相关
- 运行态开关：`FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` 承载引擎实况（`GameAppSettings` 只作开机默认值），暂停是引用计数的 `PauseGame` / `ResumeGame`，详见[暂停与速度语义](#暂停与速度语义)
- 关闭即清理：`GameApp.Shutdown` 清空 Driver 注册表、摘除本框架的 PlayerLoop 系统（保留 UniTask 等第三方注入）、退掉未配对完的暂停并释放宿主

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `GameApp` | 框架入口静态外观：生命周期、协程、订阅 API（无 Mono 成员） |
| `PlayerLoopDriver` | 剥离 Mono 的逻辑驱动器（静态） |
| `IUpdateHandler` 等 | 接口式 Handler，推荐新代码使用 |
| `GameAppHost` | 协程 / Gizmos / Pause 轻量宿主（`SingletonMono_Persistent`） |

## 快速上手

```csharp
// 回调方式（兼容旧 API）
GameApp.AddUpdateListener(OnUpdate);
GameApp.AddFixedUpdateListener(OnFixedUpdate);
GameApp.AddLateUpdateListener(OnLateUpdate);
GameApp.RemoveUpdateListener(OnUpdate);

void OnUpdate() { /* 每帧；禁止堆分配 */ }

// 接口方式（推荐：可携带状态、DI 注入依赖、用 IPlayerLoopPriority 指定顺序）
GameApp.AddUpdateHandler(myUpdateHandler);
GameApp.RemoveUpdateHandler(myUpdateHandler);

// 一个类实现多个阶段接口时，一次登记全部阶段
GameApp.AddFrameHandler(myMultiStageSystem);
GameApp.RemoveFrameHandler(myMultiStageSystem);

// 协程
Coroutine co = GameApp.StartCoroutine(SomeRoutine());
GameApp.StopCoroutine(co);

// Destroy（GameApp.Shutdown 时触发）
GameApp.AddDestroyListener(OnShutdown);
```

## 注册时机

`Add*Listener` / `Add*Handler` / `AddFrameHandler` 均为 **同步** 写入静态表，任意初始化阶段调用均安全（含 `SubsystemRegistration`）；真正驱动从 PlayerLoop 注入后的帧开始。

## 运行态与配置分离

`GameAppSettings` 的 `m_FrameRate` / `m_GameSpeed` / `m_RunInBackground` / `m_NeverSleep` **只是开机默认值**：

1. `GameAppSettings.Initiation`（`BeforeSceneLoad`）把资产值推给引擎一次
2. `GameApp.Initialize` 随即用 `SeedRuntimeFromEngine` 从**引擎实况**回读，播种进 `GameApp` 自有的静态字段
3. 之后 `FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` 的读写只碰这些字段与引擎，**不再解引用、也不再回写配置资产**

因此编辑器里 `GameApp.FrameRate = 60` 不会让 Resources 下那份共享 ScriptableObject 跨 Play 会话变脏；判据读的也是实况而非配置意图。

## 暂停与速度语义

`GameSpeed` 是**期望速度**这一唯一真相（映射 `Time.timeScale`）；`PauseGame` 是**引用计数**，两者解耦：

| 调用 | 行为 |
|------|------|
| `PauseGame()` | 计数 +1；仅在 0→1 时把 `Time.timeScale` 压到 0 |
| `ResumeGame()` | 计数 -1；**归零**才把 `Time.timeScale` 回放为当前的 `GameSpeed`。计数已为 0 时是**空操作**，不会拉回任何初值 |
| `GameSpeed = v`（暂停中） | 只更新恢复目标，`timeScale` 保持 0——**暂停优先于速度设定**；负值按 0 处理 |
| `ResetGameSpeed()` | 把期望速度归 1；暂停中调用**不会**顺手解除暂停 |

弹窗 + 切后台 + 剧情过场各自 `PauseGame` 时须各自 `ResumeGame`，最后一个 `ResumeGame` 才真正回速。

**要判什么就读什么**（1.0.2 的 `IsGamePaused` 等价于 `GameSpeed <= 0`，现已解耦）：

| 想知道 | 读 |
|--------|-----|
| 有没有人请求暂停 | `GameApp.IsGamePaused`（计数非零；**调到 0 速定格不算暂停**） |
| 时间是否真的停着 | `Time.timeScale <= 0f`（或 `GameApp.GameSpeed <= 0f`） |
| 暂停了几层 | `GameApp.PauseDepth`（框架内部，调试面板 `Other/Game Settings` 用它显示深度） |

## 关闭后的运行态契约

`GameApp.Shutdown`（幂等）会把**未配对完的暂停一并退掉**：计数归零并回放 `GameSpeed`。留着会有两个后果——关闭后仍要跑的若干帧（重启场景 / 退出期异步落盘）一直冻结在 `timeScale = 0`，且下一次 `Initialize` 会从被冻结的引擎实况播种出 `GameSpeed = 0`，而 `ResumeGame` 在计数 0 是空操作，届时没有任何 API 能把速度救回来。

关闭之后：

- **运行态属性照常可用**：`FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` / `IsGamePaused` / `PauseGame` / `ResumeGame` 不判 `IsShutdown`，读写不抛——它们是引擎状态的门面，不依赖框架存活，写入即刻作用于引擎，并在下一次 `Initialize` 时被重新播种成基线
- **帧订阅与协程不保证**：注册表已清空、宿主已释放，`Add*Listener` / `Add*Handler` 登记了也不会被驱动，`StartCoroutine` 可能拿不到宿主（返回 `null` 并告警）

## 注意事项

- 监听器持有强引用，务必注销；`Shutdown` 时 Driver 统一清空。注销优先持 `Add*Listener` 返回的 `GameApp.Subscription` 并 `Dispose()`——`Remove*Listener(Action)` 按委托相等比较，**事后重写一个同样体的 lambda 摘不掉**（那是新的委托实例），订阅会连闭包一起留到 `Shutdown`。
- Gizmos API 仅编辑器有派发者；订阅写入 Driver 静态表，注册本身在打包后也无害。
- 请勿手动销毁 `[GameAppHost]`；销毁后经 `Instance` 惰性重建，届时只恢复派发，订阅从未丢失。
- 后台线程调用 `GameApp.StartCoroutine` 等会经 `SingletonMono<T>.Instance` 抛出：宿主必须先在主线程物化（`GameApp.Initialize` 已保证）。
- 退出 Play 时只摘除本框架注入的三个 PlayerLoop 系统，UniTask 等第三方注入原样保留；再次进入 Play 由 `GameApp.Initialize` 重新注入。

---
[« 返回文档索引](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
