# Scene 服务

> 基于资源系统（ResourceService）场景加载管线的主/子场景管理服务，自动适配 YooAsset、Addressable 等资源后端，提供异步加载、挂起激活、进度回调与子场景卸载能力。

场景服务（`Moirai.Atropos.Scene`）的默认后端 `DefaultSceneHandler` 通过 `ResourceService.LoadSceneAsync` 加载场景，资源系统按当前配置的后端（YooAsset、Addressable 等）自动应用对应适配器，产出统一的 `ResourceSceneHandle` 句柄。场景服务区分主场景（`LoadSceneMode.Single`，同时只能存在一个）与子场景（`LoadSceneMode.Additive`，可叠加多个）。支持加载到 90% 时挂起、就绪后统一激活的平滑切换模式，并在主场景加载完成后可选触发资源回收。通过 `SceneService` 静态访问器使用。

## 核心特性

- 主场景 / 子场景双轨管理：Single 模式替换主场景，Additive 模式登记到子场景表
- 挂起加载：`suspendLoad` 加载完毕后不自动激活，调用 `UnSuspend` 手动激活，适合做加载完成的统一时机控制
- 进度回调：`progressCallBack` 每帧回报场景句柄加载进度（0~1），成功完成时以 1.0 收尾一次（失败不伪报完成进度），回调异常被隔离记录不中断加载
- 错误契约：加载失败（重复加载、在途互斥、后端错误等）抛出 `GameException`（fail-fast）；卸载失败返回 `false` 并保留登记供重试
- 生命周期事件：`MainSceneChanged` / `SubSceneLoaded` / `SubSceneUnloaded` 主线程同步触发，订阅者异常被隔离记录
- 防重入保护：同一场景加载/卸载过程中重复请求会被拒绝（加载抛 `GameException`，卸载告警并返回 `false`）
- 双标识查询：查询/激活/卸载接口同时接受资源地址与场景短名（短名碰撞时后注册者覆盖并告警）
- 取消语义：`LoadSceneAsync` 的 `CancellationToken` 仅放弃等待（底层加载不可中止），登记与事件由处理器在加载真正结束时收尾
- 垃圾回收：主场景加载完成后按 `gcCollect` 参数执行 `ForceUnloadUnusedAssets`
- 多资源包支持：回调式 `LoadScene` 可指定 `packageName` 从指定资源包加载
- 资源后端自适应：场景加载经 `ResourceService` 管线，切换 YooAsset / Addressable 后端无需修改场景代码

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.Scene.SceneService` | 场景服务静态外观（`[HandlerHost]`）。未注册时外观调用静默降级（查询返回默认值、加载返回无效场景）；`Handler` 属性懒加载从 `SceneServiceSettings` 创建处理器，处理器配置为空时抛 `InvalidOperationException` |
| `Moirai.Atropos.Scene.SceneServiceHandler` | 处理器抽象基类，定义后端契约；默认实现 `DefaultSceneHandler` 经 `ResourceService` 加载并管理主/子场景 |
| `Moirai.Atropos.Resource.ResourceSceneHandle` | 资源系统场景句柄抽象，由资源后端（YooAsset / Addressable）适配实现，承载加载进度、激活、解除挂起与卸载 |

## 快速上手

```csharp
// 异步加载主场景（await 用法）
UnityEngine.SceneManagement.Scene scene =
    await SceneService.LoadSceneAsync("GameMain", LoadSceneMode.Single);

// 异步加载子场景并监听进度
await SceneService.LoadSceneAsync(
    "BattleMap", LoadSceneMode.Additive,
    progressCallBack: p => loadingBar.value = p);

// 回调式加载（可指定资源包名）
SceneService.LoadScene(
    "GameMain", packageName: "main-package",
    sceneMode: LoadSceneMode.Single,
    callBack: s => { /* 加载完成，s 为 Scene */ },
    progressCallBack: p => Debug.Log($"进度: {p}"));

// 卸载子场景（失败返回 false 并保留登记，可重试）
bool ok = await SceneService.UnloadAsync("BattleMap");
SceneService.Unload("BattleMap", callBack: success => Debug.Log($"卸载结果: {success}"));

// 查询
string main = SceneService.CurrentMainSceneName;
bool loaded = SceneService.IsContainScene("BattleMap");
bool isMain = SceneService.IsMainScene("GameMain");
IReadOnlyCollection<string> subs = SceneService.LoadedSubSceneLocations;
```

## 进阶用法

### 挂起加载与统一激活

`suspendLoad = true` 时场景加载完毕后保持挂起，常用于过渡黑屏/Loading 场景中等待资源与逻辑全部就绪后一次性切入：

```csharp
// 发起挂起加载（子场景同理）
SceneService.LoadSceneAsync("GameMain", suspendLoad: true);

// 一切就绪后激活场景
bool activated = SceneService.ActivateScene("GameMain");   // 激活为当前活动场景
bool resumed = SceneService.UnSuspend("GameMain");         // 仅解除挂起
```

### 多子场景叠加

Additive 子场景可同时加载多个（以 `location` 为键），适合大世界分块、独立玩法房间等结构：

```csharp
await SceneService.LoadSceneAsync("ChunkA", LoadSceneMode.Additive);
await SceneService.LoadSceneAsync("ChunkB", LoadSceneMode.Additive);

// 服务关闭（Shutdown）时会自动卸载全部子场景
```

### 加载优先级

`priority` 参数透传给资源后端，用于在多个加载请求并发时调整该场景的加载优先级（默认 100）。

### 生命周期事件

主/子场景加载与卸载完成时派发事件，参数为归一化场景短名；事件在主线程同步触发，单个订阅者异常仅记录日志、不影响其余订阅者。服务关闭时静态事件被清空。

```csharp
SceneService.MainSceneChanged += name => Debug.Log($"主场景已切换: {name}");
SceneService.SubSceneLoaded += name => Debug.Log($"子场景已加载: {name}");
SceneService.SubSceneUnloaded += name => Debug.Log($"子场景已卸载: {name}");
```

### 取消等待

场景加载一经发起不可中止（引擎与资源后端均无中止能力）。`LoadSceneAsync` 的 `CancellationToken` 仅取消等待与进度回调（放弃等待语义）——await 方收到 `OperationCanceledException`，加载本身仍会继续，登记与 `SubSceneLoaded` / `MainSceneChanged` 事件由处理器在加载真正结束时收尾：

```csharp
try
{
    await SceneService.LoadSceneAsync("BattleMap", LoadSceneMode.Additive,
        cancellationToken: timeoutToken);
}
catch (OperationCanceledException)
{
    // 等待超时/取消：场景仍在后台加载，完成后照常登记并派发事件
}
```

## 注意事项

- 场景资源需纳入资源后端收集构建（YooAsset 收集器 / Addressables 组）；编辑器下使用 YooAsset 后端时请先通过 `YooAsset/Editor PlayMode` 选择模拟模式
- 加载失败一律抛出 `GameException`：重复加载在途中的同地址场景、重复加载已登记的子场景、主场景加载互斥（另一主场景在途）、跨模式同址（已登记为子场景又以 Single 加载，或反之）、资源服务未就绪、后端加载错误
- 回调式 `LoadScene` 不抛异常：无论成败恰好回调一次，失败时以默认场景回调，调用方须检查 `Scene.IsValid()`
- 卸载失败（后端卸载错误、句柄失效）返回 `false` / 回调 `false` 并保留登记，可安全重试；无效地址或存在在途操作时卸载不发起（告警日志）
- `Unload` / `UnloadAsync` 仅针对 Additive 子场景，主场景通过加载新的 Single 场景替换，请勿对主场景调用卸载
- 查询/激活/卸载接口同时接受资源地址与场景短名；场景短名应全局唯一——碰撞时后注册者覆盖反向索引并打 Warning，按名操作可能解析到错误对象
- 主场景加载完成后默认触发 `ForceUnloadUnusedAssets(gcCollect)`，加载期间如有暂存资源引用需注意（可将 `gcCollect` 置为 false 关闭）
- `progressCallBack` 在句柄完成或失效前每帧回调，回调内勿执行耗时操作；成功完成时以 1.0 收尾一次，失败不伪报完成进度
- 本服务发起的挂起加载（`suspendLoad`）必须最终 `UnSuspend`——底层加载无中止能力，挂起不解除则加载永不完成

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [UI](UI.md) · [Input](Input.md)
