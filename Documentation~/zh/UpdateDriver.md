# UpdateDriver 服务

> 为非 MonoBehaviour 代码提供 Unity 生命周期代理：协程托管、帧更新注入与 Unity 事件注入。

`UpdateDriver` 解决纯 C# 类无法访问 Unity 引擎回调的问题。框架内所有服务均为普通 C# 类（见 [Core 服务系统](Core.md)），当业务类需要协程、`Update` 轮询或 `OnApplicationPause` 等引擎回调时，可通过本服务将回调注册到一个常驻的隐藏宿主 `MainBehaviour` 上。服务实现类 `UpdateDriverService` 在首次使用时懒创建名为 `[UpdateDriver]` 的 `DontDestroyOnLoad` 游戏对象，所有引擎回调以事件形式聚合转发。

## 核心特性

- 协程托管：无需在场景中放置 MonoBehaviour 即可启动/停止协程
- 帧更新注入：`Update` / `FixedUpdate` / `LateUpdate` 三类帧回调的注册与移除
- Unity 事件注入：`OnDestroy`、`OnDrawGizmos`、`OnDrawGizmosSelected`、`OnApplicationPause`
- 懒创建宿主：首次调用任意 API 时才创建 `[UpdateDriver]` 常驻对象，零前置开销
- 关闭即清理：服务 `Shutdown` 时清空全部事件并销毁宿主对象

## 核心类型

命名空间：`Moirai.Atropos.UpdateDriver`

| 类/接口 | 说明 |
|---------|------|
| `UpdateDriverService` | 静态外观（`[HandlerHost]`），协程控制、帧更新监听、Unity 事件监听的注册与移除；全部静态 API，经 `Handler` 属性转发（fail-fast：未就绪时按需初始化，工厂缺失时抛异常，不静默降级） |
| `UpdateDriverServiceHandler` | 处理器抽象基类，定义后端契约；默认实现 `UnityUpdateDriverHandler` 管理 `[UpdateDriver]` 宿主 |

宿主 `MainBehaviour`（Handler 的私有嵌套类）为实际挂载的 MonoBehaviour，以 C# 事件聚合各 Unity 回调；Gizmos 相关回调带 `[Conditional("UNITY_EDITOR")]`，仅在编辑器编译生效。

## 快速上手

```csharp
// 静态外观直接调用

// 协程：交由框架宿主驱动，无需自身 MonoBehaviour
Coroutine co = UpdateDriverService.StartCoroutine(SomeRoutine());
UpdateDriverService.StopCoroutine(co);
UpdateDriverService.StopAllCoroutines();

// 帧更新注入：普通类获得 Update 轮询
UpdateDriverService.AddUpdateListener(OnUpdate);
UpdateDriverService.AddFixedUpdateListener(OnFixedUpdate);
UpdateDriverService.AddLateUpdateListener(OnLateUpdate);

void OnUpdate() { /* 每帧调用 */ }
void OnFixedUpdate() { /* 物理帧调用 */ }
void OnLateUpdate() { /* Late 帧调用 */ }

// 移除监听（成对调用，防止泄漏）
UpdateDriverService.RemoveUpdateListener(OnUpdate);
```

## 进阶用法

### Unity 事件注入

```csharp
// 应用暂停/恢复（参数为是否暂停）
UpdateDriverService.AddOnApplicationPauseListener(OnApplicationPause);
void OnApplicationPause(bool pauseStatus) { }

// 编辑器 Gizmos 绘制
UpdateDriverService.AddOnDrawGizmosListener(DrawGizmos);
UpdateDriverService.AddOnDrawGizmosSelectedListener(DrawSelectedGizmos);

// 宿主销毁回调（服务 Shutdown 销毁宿主前不会触发，主要用于宿主被外部销载的场景）
UpdateDriverService.AddDestroyListener(OnHostDestroy);
```

### 框架内部使用

`UpdateDriver` 是框架多个基础设施的底层依赖：`UnityUtility` 中的协程工具经由它执行。服务实现类型在 `AppSettings.Initiation()` 阶段注册，支持在 Inspector 中替换实现。

### 注册时机说明

`AddUpdateListener` / `AddFixedUpdateListener` / `AddLateUpdateListener` 内部通过 UniTask 延迟一帧后真正挂载（FixedUpdate 监听注册在 `PlayerLoopTiming.LastEarlyUpdate` 时机），确保宿主完成当帧初始化后再接收回调；`Remove*` 系列与 Unity 事件监听的添加/移除均为同步生效。

## 注意事项

- 监听器持有强引用，务必成对调用 `Add`/`Remove`，否则目标对象无法被回收；服务 `Shutdown` 时会统一清空。
- Gizmos 与 GizmosSelected 相关 API 仅在编辑器下生效，打包后调用会被编译剔除。
- `StartCoroutine` 传入空方法名或空迭代器时返回 `null`，不做异常处理。
- 宿主对象为 `DontDestroyOnLoad`，跨场景存活；请勿在外部手动销毁 `[UpdateDriver]` 对象，否则协程与监听全部失效。

---
[« 返回主 README](../../README.md) · [Core](Core.md) · [Timer](Timer.md)
