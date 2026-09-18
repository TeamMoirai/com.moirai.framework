# UpdateDriver / GameApp 帧驱动

> 为非 MonoBehaviour 代码提供 Unity 生命周期代理：协程托管、帧更新注入与 Unity 事件注入。

## 架构变更（重要）

帧逻辑订阅已从 MonoBehaviour 宿主迁移到 **`PlayerLoopDriver`**（`Runtime/Core/PlayerLoop`，命名空间 `Moirai.Atropos.FrameLoop`）：

- 订阅存储在 **静态注册表**，不挂在任何 GameObject 上
- 场景切换、宿主被意外销毁 **不会丢失** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy` 订阅
- 旧问题：初始场景加载前 `[UpdateDriver]` 宿主可能被销毁，`MainBehaviour` 实例事件全部失效

协程 / Editor Gizmos / `OnApplicationPause` 仍由轻量宿主 `CoroutineHost`（`[CoroutineHost]`）承接——宿主被销毁只影响这三类，帧订阅不受影响。

详细设计见 [PlayerLoopDriver](PlayerLoopDriver.md)。

## 核心特性

- 协程托管：`GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- 帧更新注入：`GameApp.AddUpdateListener` 等 API **同步**写入 `PlayerLoopDriver`（不再 `UniTask.Yield` 延迟挂载）
- Unity 事件：`AddDestroyListener`（Shutdown 时广播）、`AddOnApplicationPauseListener`、Gizmos 相关
- 关闭即清理：`GameApp.Shutdown` 清空 Driver 注册表并恢复默认 PlayerLoop

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `GameApp` | 框架入口静态外观：生命周期、协程、订阅 API |
| `PlayerLoopDriver` | 剥离 Mono 的逻辑驱动器（静态） |
| `IUpdateHandler` 等 | 接口式 Handler，推荐新代码使用 |
| `GameApp.CoroutineHost` | 协程 / Gizmos / Pause 轻量宿主 |

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
- Gizmos API 仅编辑器生效，且依赖 `CoroutineHost`。
- 请勿手动销毁 `[CoroutineHost]`；销毁后惰性重建，帧订阅不受影响。
- 退出 Play 时会恢复默认 PlayerLoop（含移除 UniTask 注入）；再次进入 Play 由各库重新初始化。

---
[« 返回文档索引](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
