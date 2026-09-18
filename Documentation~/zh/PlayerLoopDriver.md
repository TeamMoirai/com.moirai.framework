# PlayerLoop 逻辑驱动

> 剥离 MonoBehaviour 的游戏帧逻辑驱动：由 Unity PlayerLoop 直接回调，订阅不随场景/宿主销毁而丢失。

## 背景

旧版 `GameApp` 将 `Update`/`FixedUpdate`/`LateUpdate` 订阅挂在隐藏宿主 `MainBehaviour`（`[UpdateDriver]`）实例事件上。该宿主在初始场景加载前可能被意外销毁，导致全部订阅丢失，服务 Tick 静默停摆。

`Runtime/Core/Schedulers` 是 Timer/FrameCounter 子系统（Unreal TimerManager 风格），依赖场景 GameObject，且无通用 `IUpdateHandler` 注册面，**不能**作为游戏逻辑驱动。

## 架构

| 组件 | 职责 |
|------|------|
| `Moirai.Atropos.FrameLoop.PlayerLoopDriver` | 零分配静态注册表 + Drive 入口 |
| `Moirai.Atropos.FrameLoop.PlayerLoopInjector` | 注入/恢复 Unity `PlayerLoopSystem` |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | 接口式逻辑处理器（推荐，DI 友好） |
| `IPlayerLoopPriority` | 可选驱动顺序（数值小者先执行） |
| `GameApp` 静态 API | 兼容层：`AddUpdateListener` 等转发到 Driver |
| `CoroutineHost` | 仅协程 / Gizmos / ApplicationPause |

注入点：

- `PlayerLoop.Update` 开头 → Framework Update
- `PlayerLoop.FixedUpdate` 开头 → Framework FixedUpdate
- `PlayerLoop.PreLateUpdate` 末尾 → Framework LateUpdate（晚于 MonoBehaviour.LateUpdate）

注入时基于 `GetCurrentPlayerLoop()`，保留 UniTask 等第三方系统。`SubsystemRegistration` 记录默认循环，Shutdown 时恢复。

## 快速上手

```csharp
using Moirai.Atropos.FrameLoop;

// 接口方式（推荐：DI 注入依赖后注册）
public sealed class MySystem : IUpdateHandler
{
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // 禁止堆分配
    }
}

// 组合根中
PlayerLoopDriver.Initialize();
PlayerLoopDriver.Register(new MySystem());

// 或兼容 API
GameApp.AddUpdateListener(OnUpdate);
GameApp.AddUpdateListener -= ... // RemoveUpdateListener
```

## 零分配契约

`DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` 及所有 Handler 实现：

- 使用 `for` 循环，禁止 LINQ / 闭包 / 字符串拼接
- 驱动中注册/注销进入延迟缓冲，迭代结束后提交
- Profiler Marker：`PlayerLoopDriver.Update` 等

## 生命周期

| 时机 | 行为 |
|------|------|
| `SubsystemRegistration` | 记录默认 PlayerLoop；Driver 标记 Shutdown |
| `GameApp.Initialize`（AfterAssembliesLoaded） | `PlayerLoopDriver.Initialize()` 注入并注册内置 Tick |
| `GameApp.Shutdown` / 退出 Play | 广播 Destroy → 清空注册表 → `SchedulerRunner.Shutdown` → 恢复默认 PlayerLoop |
| ECS 重置 PlayerLoop 后 | 调用 `PlayerLoopInjector.Reinject()` |

## DI（VContainer 等）

将 Handler 实现注册为服务，在组合根 `InitializeAsync` 后：

```csharp
var system = container.Resolve<MySystem>();
PlayerLoopDriver.Register(system);
```

驱动与对象创建解耦；注销 `Unregister(system)`。

## 兼容注意

- **UniTask**：注入基于当前循环，不覆盖 UniTask 系统；退出 Play 恢复默认循环后由 UniTask 自行重新初始化。
- **ECS/DOTS**：Entities 可能在 `BeforeSceneLoad` 重置 PlayerLoop，初始化完成后 `PlayerLoopInjector.Reinject()`。
- **ApplicationPause**：无纯 C# API，仍由 `CoroutineHost` 转发到 `PlayerLoopDriver.RaiseApplicationPause`。
- **协程**：仍需 `GameApp.StartCoroutine`（`[CoroutineHost]`）；宿主销毁只影响协程，不影响帧订阅。

## 编辑器调试

菜单 **Window → PlayerLoop Debugger**：

- 递归打印当前 PlayerLoop，Moirai 注入点标记 `<< Moirai`
- 显示各阶段 Handler / Callback 数量
- 按钮：Ensure Injected / Reinject / Restore Default

---
[« 返回文档索引](Index.md) · [GameApp](UpdateDriver.md)
