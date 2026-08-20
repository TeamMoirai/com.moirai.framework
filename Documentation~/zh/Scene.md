# Scene 服务

> 基于 YooAsset 场景句柄的主/子场景管理服务，提供异步加载、挂起激活、进度回调与子场景卸载能力。

场景服务（`Moirai.Atropos.Scene`）封装 YooAsset 的 `SceneHandle`，区分主场景（`LoadSceneMode.Single`，同时只能存在一个）与子场景（`LoadSceneMode.Additive`，可叠加多个）。支持加载到 90% 时挂起、就绪后统一激活的平滑切换模式，并在主场景加载完成后可选触发资源回收。通过 `GameApp.Services.GetRequiredService<ISceneService>()` 静态访问器使用。

## 核心特性

- 主场景 / 子场景双轨管理：Single 模式替换主场景，Additive 模式登记到子场景字典
- 挂起加载：`suspendLoad` 加载完毕后不自动激活，调用 `UnSuspend` 手动激活，适合做加载完成的统一时机控制
- 进度回调：`progressCallBack` 每帧回报 `SceneHandle.Progress`（0~1）
- 防重入保护：同一场景加载/卸载过程中重复请求会被拒绝并记录日志
- 垃圾回收：主场景加载完成后按 `gcCollect` 参数执行 `ForceUnloadUnusedAssets`
- 多资源包支持：回调式 `LoadScene` 可指定 `packageName` 从指定 YooAsset 资源包加载

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.Scene.ISceneService` | 场景服务接口，`GameApp.Services.GetRequiredService<ISceneService>()` 返回此类型 |
| `Moirai.Atropos.Scene.SceneService` | 场景服务实现，内部持有 `YooAsset.SceneHandle` 管理主/子场景 |

## 快速上手

```csharp
// 异步加载主场景（await 用法）
UnityEngine.SceneManagement.Scene scene =
    await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("GameMain", LoadSceneMode.Single);

// 异步加载子场景并监听进度
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync(
    "BattleMap", LoadSceneMode.Additive,
    progressCallBack: p => loadingBar.value = p);

// 回调式加载（可指定资源包名）
GameApp.Services.GetRequiredService<ISceneService>().LoadScene(
    "GameMain", packageName: "main-package",
    sceneMode: LoadSceneMode.Single,
    callBack: s => { /* 加载完成，s 为 Scene */ },
    progressCallBack: p => Debug.Log($"进度: {p}"));

// 卸载子场景
bool ok = await GameApp.Services.GetRequiredService<ISceneService>().UnloadAsync("BattleMap");
GameApp.Services.GetRequiredService<ISceneService>().Unload("BattleMap", callBack: () => Debug.Log("已卸载"));

// 查询
string main = GameApp.Services.GetRequiredService<ISceneService>().CurrentMainSceneName;
bool loaded = GameApp.Services.GetRequiredService<ISceneService>().IsContainScene("BattleMap");
bool isMain = GameApp.Services.GetRequiredService<ISceneService>().IsMainScene("GameMain");
```

## 进阶用法

### 挂起加载与统一激活

`suspendLoad = true` 时场景加载完毕后保持挂起，常用于过渡黑屏/Loading 场景中等待资源与逻辑全部就绪后一次性切入：

```csharp
// 发起挂起加载（子场景同理）
GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("GameMain", suspendLoad: true);

// 一切就绪后激活场景
bool activated = GameApp.Services.GetRequiredService<ISceneService>().ActivateScene("GameMain");   // 激活为当前活动场景
bool resumed = GameApp.Services.GetRequiredService<ISceneService>().UnSuspend("GameMain");         // 仅解除挂起
```

### 多子场景叠加

Additive 子场景可同时加载多个（以 `location` 为键），适合大世界分块、独立玩法房间等结构：

```csharp
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("ChunkA", LoadSceneMode.Additive);
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("ChunkB", LoadSceneMode.Additive);

// 服务关闭（Shutdown）时会自动卸载全部子场景
```

### 加载优先级

`priority` 参数透传给 YooAsset，用于在多个加载请求并发时调整该场景的加载优先级（默认 100）。

## 注意事项

- 场景资源需纳入 YooAsset 收集构建；编辑器下请先通过 `YooAsset/Editor PlayMode` 选择模拟模式
- 重复加载已在加载中的同地址场景会被拒绝（Log.Error）；重复加载已存在的子场景会抛出 `GameException`
- `Unload` / `UnloadAsync` 仅针对 Additive 子场景，主场景通过加载新的 Single 场景替换，请勿对主场景调用卸载
- 主场景加载完成后默认触发 `ForceUnloadUnusedAssets(gcCollect)`，加载期间如有暂存资源引用需注意（可将 `gcCollect` 置为 false 关闭）
- `progressCallBack` 在句柄完成或失效前每帧回调，回调内勿执行耗时操作

---
[« 返回主 README](../../README.md) · [UI](UI.md) · [Input](Input.md)
