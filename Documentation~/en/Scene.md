# Scene Service

> Main/sub-scene management service built on the resource system (ResourceService) scene loading pipeline, automatically adapting to resource backends such as YooAsset and Addressables, providing async loading, suspend activation, progress callbacks, and sub-scene unloading.

The scene service's (`Moirai.Atropos.Scene`) default backend `DefaultSceneHandler` loads scenes through `ResourceService.LoadSceneAsync`; the resource system automatically applies the adapter matching the configured backend (YooAsset, Addressables, etc.) and produces a unified `ResourceSceneHandle`. It distinguishes between main scenes (`LoadSceneMode.Single`, only one at a time) and sub-scenes (`LoadSceneMode.Additive`, multiple can be stacked). It supports a smooth transition mode where loading can be suspended at 90% progress and then activated uniformly when ready, with optional garbage collection after the main scene finishes loading. Accessible via the `SceneService` static accessor.

## Core Features

- Dual-track main scene / sub-scene management: Single mode replaces the main scene, Additive mode registers sub-scenes in a registry table
- Suspend loading: When `suspendLoad` is enabled, the scene does not auto-activate after loading; call `UnSuspend` to manually activate. Suitable for unified timing control of load completion
- Progress callback: `progressCallBack` reports the scene handle's loading progress (0 to 1) every frame, finishing with exactly one 1.0 report on success (no fake completion on failure); callback exceptions are isolated and logged without interrupting the load
- Error contract: load failures (duplicate loads, in-flight conflicts, backend errors, etc.) throw `GameException` (fail-fast); unload failures return `false` and keep the registration for retry
- Lifecycle events: `MainSceneChanged` / `SubSceneLoaded` / `SubSceneUnloaded` fire synchronously on the main thread; subscriber exceptions are isolated and logged
- Re-entry protection: Duplicate requests for the same scene during loading/unloading are rejected (load throws `GameException`, unload warns and returns `false`)
- Dual-identity queries: query/activate/unload APIs accept both resource location and scene short name (on short-name collision the later registration wins with a warning)
- Cancellation semantics: the `CancellationToken` of `LoadSceneAsync` only abandons waiting (the underlying load cannot be aborted); registration and events are finalized by the handler when the load actually completes
- Garbage collection: After the main scene finishes loading, `ForceUnloadUnusedAssets` is executed according to the `gcCollect` parameter
- Multi-package support: The callback-based `LoadScene` can specify a `packageName` to load from a specific resource package
- Backend adaptation: Scene loading goes through the `ResourceService` pipeline — switching between YooAsset / Addressables backends requires no scene code changes

## Core Types

| Class/Interface | Description |
|---------|------|
| `Moirai.Atropos.Scene.SceneService` | Scene service static facade (`[HandlerHost]`). When unregistered, facade calls silently degrade (queries return defaults, loads return an invalid scene); the `Handler` property lazily creates the handler from `SceneServiceSettings` and throws `InvalidOperationException` if the configured handler is null |
| `Moirai.Atropos.Scene.SceneServiceHandler` | Handler abstract base class defining the backend contract; the default implementation `DefaultSceneHandler` loads and manages main/sub scenes via `ResourceService` |
| `Moirai.Atropos.Resource.ResourceSceneHandle` | Resource system scene handle abstraction, implemented per resource backend (YooAsset / Addressables), carrying load progress, activation, unsuspend, and unload |

## Quick Start

```csharp
// Async load main scene (await usage)
UnityEngine.SceneManagement.Scene scene =
    await SceneService.LoadSceneAsync("GameMain", LoadSceneMode.Single);

// Async load sub-scene with progress tracking
await SceneService.LoadSceneAsync(
    "BattleMap", LoadSceneMode.Additive,
    progressCallBack: p => loadingBar.value = p);

// Callback-based loading (can specify package name)
SceneService.LoadScene(
    "GameMain", packageName: "main-package",
    sceneMode: LoadSceneMode.Single,
    callBack: s => { /* Load complete, s is the Scene */ },
    progressCallBack: p => Debug.Log($"Progress: {p}"));

// Unload sub-scene (failure returns false and keeps the registration for retry)
bool ok = await SceneService.UnloadAsync("BattleMap");
SceneService.Unload("BattleMap", callBack: success => Debug.Log($"Unload result: {success}"));

// Query
string main = SceneService.CurrentMainSceneName;
bool loaded = SceneService.IsContainScene("BattleMap");
bool isMain = SceneService.IsMainScene("GameMain");
IReadOnlyCollection<string> subs = SceneService.LoadedSubSceneLocations;
```

## Advanced Usage

### Suspend Loading and Unified Activation

When `suspendLoad = true`, the scene remains suspended after loading completes. This is commonly used in transition black screen / loading scenes where you wait for all resources and logic to be ready before switching over in one go:

```csharp
// Initiate suspend loading (works the same for sub-scenes)
SceneService.LoadSceneAsync("GameMain", suspendLoad: true);

// Activate the scene once everything is ready
bool activated = SceneService.ActivateScene("GameMain");   // Activate as the current active scene
bool resumed = SceneService.UnSuspend("GameMain");         // Only unsuspend
```

### Multiple Sub-Scene Stacking

Multiple Additive sub-scenes can be loaded simultaneously (keyed by `location`), suitable for large world chunks, independent gameplay rooms, etc.:

```csharp
await SceneService.LoadSceneAsync("ChunkA", LoadSceneMode.Additive);
await SceneService.LoadSceneAsync("ChunkB", LoadSceneMode.Additive);

// All sub-scenes are automatically unloaded on service shutdown
```

### Loading Priority

The `priority` parameter is passed through to the resource backend to adjust the loading priority of a scene when multiple loading requests are concurrent (default is 100).

### Lifecycle Events

Events fire when main/sub-scene loads and unloads complete, carrying the normalized scene short name. They fire synchronously on the main thread; a throwing subscriber is only logged and does not affect the others. Static events are cleared when the service shuts down.

```csharp
SceneService.MainSceneChanged += name => Debug.Log($"Main scene changed: {name}");
SceneService.SubSceneLoaded += name => Debug.Log($"Sub scene loaded: {name}");
SceneService.SubSceneUnloaded += name => Debug.Log($"Sub scene unloaded: {name}");
```

### Abandoning the Wait

A scene load, once started, cannot be aborted (neither the engine nor resource backends support cancellation). The `CancellationToken` of `LoadSceneAsync` only cancels the wait and progress callbacks (abandon-wait semantics) — the awaiting caller receives an `OperationCanceledException`, while the load itself continues; registration and the `SubSceneLoaded` / `MainSceneChanged` events are finalized by the handler when the load actually completes:

```csharp
try
{
    await SceneService.LoadSceneAsync("BattleMap", LoadSceneMode.Additive,
        cancellationToken: timeoutToken);
}
catch (OperationCanceledException)
{
    // Wait timed out / cancelled: the scene keeps loading in the background
    // and is registered (with events) once it completes
}
```

## Notes

- Scene assets must be collected and built by the resource backend (YooAsset collector / Addressables group); in the editor with the YooAsset backend, first select a simulation mode via `YooAsset/Editor PlayMode`
- Load failures always throw `GameException`: re-loading an address with an in-flight operation, re-loading an already registered sub-scene, main-scene mutex (another main scene is loading), cross-mode duplicates (a location registered as sub-scene being loaded as Single, or vice versa), resource service not ready, and backend load errors
- The callback-based `LoadScene` never throws: it invokes the callback exactly once regardless of outcome, with the default scene on failure — callers must check `Scene.IsValid()`
- Unload failures (backend unload errors, invalid handles) return `false` / callback `false` and keep the registration, so retrying is safe; unload requests with an unknown address or an in-flight operation are not started (warning logged)
- `Unload` / `UnloadAsync` only apply to Additive sub-scenes; the main scene is replaced by loading a new Single scene — do not call unload on the main scene
- Query/activate/unload APIs accept both resource location and scene short name; scene short names should be globally unique — on collision the later registration overwrites the reverse index with a warning, and name-based operations may resolve to the wrong scene
- After the main scene finishes loading, `ForceUnloadUnusedAssets(gcCollect)` is triggered by default; pay attention to any temporary asset references during loading (set `gcCollect` to false to disable)
- `progressCallBack` is called every frame until the handle completes or becomes invalid; do not perform expensive operations inside the callback; it finishes with exactly one 1.0 report on success and never fakes completion on failure
- A suspended load (`suspendLoad`) started by this service must eventually be `UnSuspend`ed — the underlying load cannot be aborted and never completes while suspended

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [UI](UI.md) · [Input](Input.md)
