# Scene Service

> Main/sub-scene management service based on YooAsset scene handles, providing async loading, suspend activation, progress callbacks, and sub-scene unloading.

The scene service (`Moirai.Atropos.Scene`) wraps YooAsset's `SceneHandle`, distinguishing between main scenes (`LoadSceneMode.Single`, only one at a time) and sub-scenes (`LoadSceneMode.Additive`, multiple can be stacked). It supports a smooth transition mode where loading can be suspended at 90% progress and then activated uniformly when ready, with optional garbage collection after the main scene finishes loading. Accessible via the `GameApp.Services.GetRequiredService<ISceneService>()` static accessor.

## Core Features

- Dual-track main scene / sub-scene management: Single mode replaces the main scene, Additive mode registers sub-scenes in a dictionary
- Suspend loading: When `suspendLoad` is enabled, the scene does not auto-activate after loading; call `UnSuspend` to manually activate. Suitable for unified timing control of load completion
- Progress callback: `progressCallBack` reports `SceneHandle.Progress` (0 to 1) every frame
- Re-entry protection: Duplicate requests for the same scene during loading/unloading are rejected and logged
- Garbage collection: After the main scene finishes loading, `ForceUnloadUnusedAssets` is executed according to the `gcCollect` parameter
- Multi-package support: The callback-based `LoadScene` can specify a `packageName` to load from a specific YooAsset package

## Core Types

| Class/Interface | Description |
|---------|------|
| `Moirai.Atropos.Scene.ISceneService` | Scene service interface, returned by `GameApp.Services.GetRequiredService<ISceneService>()` |
| `Moirai.Atropos.Scene.SceneService` | Scene service implementation, internally holds `YooAsset.SceneHandle` to manage main/sub scenes |

## Quick Start

```csharp
// Async load main scene (await usage)
UnityEngine.SceneManagement.Scene scene =
    await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("GameMain", LoadSceneMode.Single);

// Async load sub-scene with progress tracking
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync(
    "BattleMap", LoadSceneMode.Additive,
    progressCallBack: p => loadingBar.value = p);

// Callback-based loading (can specify package name)
GameApp.Services.GetRequiredService<ISceneService>().LoadScene(
    "GameMain", packageName: "main-package",
    sceneMode: LoadSceneMode.Single,
    callBack: s => { /* Load complete, s is the Scene */ },
    progressCallBack: p => Debug.Log($"Progress: {p}"));

// Unload sub-scene
bool ok = await GameApp.Services.GetRequiredService<ISceneService>().UnloadAsync("BattleMap");
GameApp.Services.GetRequiredService<ISceneService>().Unload("BattleMap", callBack: () => Debug.Log("Unloaded"));

// Query
string main = GameApp.Services.GetRequiredService<ISceneService>().CurrentMainSceneName;
bool loaded = GameApp.Services.GetRequiredService<ISceneService>().IsContainScene("BattleMap");
bool isMain = GameApp.Services.GetRequiredService<ISceneService>().IsMainScene("GameMain");
```

## Advanced Usage

### Suspend Loading and Unified Activation

When `suspendLoad = true`, the scene remains suspended after loading completes. This is commonly used in transition black screen / loading scenes where you wait for all resources and logic to be ready before switching over in one go:

```csharp
// Initiate suspend loading (works the same for sub-scenes)
GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("GameMain", suspendLoad: true);

// Activate the scene once everything is ready
bool activated = GameApp.Services.GetRequiredService<ISceneService>().ActivateScene("GameMain");   // Activate as the current active scene
bool resumed = GameApp.Services.GetRequiredService<ISceneService>().UnSuspend("GameMain");         // Only unsuspend
```

### Multiple Sub-Scene Stacking

Multiple Additive sub-scenes can be loaded simultaneously (keyed by `location`), suitable for large world chunks, independent gameplay rooms, etc.:

```csharp
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("ChunkA", LoadSceneMode.Additive);
await GameApp.Services.GetRequiredService<ISceneService>().LoadSceneAsync("ChunkB", LoadSceneMode.Additive);

// All sub-scenes are automatically unloaded on service shutdown
```

### Loading Priority

The `priority` parameter is passed through to YooAsset to adjust the loading priority of a scene when multiple loading requests are concurrent (default is 100).

## Notes

- Scene assets must be included in YooAsset collection and built; in the editor, first select a simulation mode via `YooAsset/Editor PlayMode`
- Duplicate loading of a scene address that is already being loaded will be rejected (Log.Error); duplicate loading of an existing sub-scene will throw a `GameException`
- `Unload` / `UnloadAsync` only apply to Additive sub-scenes; the main scene is replaced by loading a new Single scene — do not call unload on the main scene
- After the main scene finishes loading, `ForceUnloadUnusedAssets(gcCollect)` is triggered by default; pay attention to any temporary asset references during loading (set `gcCollect` to false to disable)
- `progressCallBack` is called every frame until the handle completes or becomes invalid; do not perform expensive operations inside the callback

---
[« Back to Main README](../../README_EN.md) · [UI](UI.md) · [Input](Input.md)