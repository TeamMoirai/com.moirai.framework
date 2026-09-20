# GameApp 帧驱动与宿主

> 为非 MonoBehaviour 代码提供 Unity 生命周期代理：协程托管、帧更新注入与 Unity 事件注入。

## 架构变更（重要）

帧逻辑订阅已从 MonoBehaviour 宿主迁移到 **`PlayerLoopDriver`**（`Runtime/Core/GameApp/PlayerLoop`，命名空间 `Moirai.Atropos.FrameLoop`）：

- 订阅存储在 **静态注册表**，不挂在任何 GameObject 上
- 场景切换、宿主被意外销毁 **不会丢失** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy`/`Gizmos`/`Pause` 订阅
- 旧问题：帧订阅挂在隐藏 Mono 宿主的实例事件上，初始场景加载前宿主可能被销毁，订阅全部失效

`GameApp` 本身**不再含任何 MonoBehaviour**（既无嵌套宿主，也无 GameObject 字段）。Unity 只在 MonoBehaviour 上派发的消息——协程、`OnDrawGizmos(Selected)`、`OnApplicationPause`——集中由 `GameAppHost`（`Runtime/Core/GameApp/GameAppHost.cs`）这一个 `SingletonMono_Persistent` 宿主承接；它只做**转发**，订阅仍留在 Driver 的静态表里，因此宿主销毁或重建都不丢订阅。

详细设计见 [PlayerLoopDriver](PlayerLoopDriver.md)。

## 核心特性

- 协程托管：`GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- 帧更新注入：`GameApp.AddUpdateListener` 等 API **同步**写入 `PlayerLoopDriver`（不再 `UniTask.Yield` 延迟挂载）
- Unity 事件：`AddDestroyListener`（Shutdown 时广播）、`AddOnApplicationPauseListener`、Gizmos 相关
- 关闭即清理：`GameApp.Shutdown` 清空 Driver 注册表、摘除本框架的 PlayerLoop 系统（保留 UniTask 等第三方注入）并释放宿主

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

// 接口方式（推荐）
PlayerLoopDriver.Register(myUpdateHandler);

// 协程
Coroutine co = GameApp.StartCoroutine(SomeRoutine());
GameApp.StopCoroutine(co);

// Destroy（GameApp.Shutdown 时触发）
GameApp.AddDestroyListener(OnShutdown);
```

## 注册时机

`Add*Listener` / `PlayerLoopDriver.Register` 均为 **同步** 写入静态表，任意初始化阶段调用均安全（含 `SubsystemRegistration`）；真正驱动从 PlayerLoop 注入后的帧开始。

## 注意事项

- 监听器持有强引用，务必成对 `Add`/`Remove`；`Shutdown` 时 Driver 统一清空。
- Gizmos API 仅编辑器有派发者；订阅写入 Driver 静态表，注册本身在打包后也无害。
- 请勿手动销毁 `[GameAppHost]`；销毁后经 `Instance` 惰性重建，届时只恢复派发，订阅从未丢失。
- 后台线程调用 `GameApp.StartCoroutine` 等会经 `SingletonMono<T>.Instance` 抛出：宿主必须先在主线程物化（`GameApp.Initialize` 已保证）。
- 退出 Play 时只摘除本框架注入的三个 PlayerLoop 系统，UniTask 等第三方注入原样保留；再次进入 Play 由 `GameApp.Initialize` 重新注入。

---
[« 返回文档索引](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
