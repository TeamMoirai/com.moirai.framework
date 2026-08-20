# Resource Service

> A resource management system based on YooAsset, providing reference counting, multi-package loading, cancellation control, and encryption/decryption capabilities.

The Resource service (`ResourceService`) provides a business-oriented wrapper around [YooAsset](https://github.com/tuyoogame/YooAsset): unified synchronous/asynchronous loading entry points, object-pool-based reference counting and automatic recycling, multi-resource-package (Package) support, and two Bundle encryption/decryption schemes (FileOffset and FileStream). Access via `GameApp.Services.GetRequiredService<IResourceService>()` (`IResourceService`). Load results are cached in the `AssetObject` object pool, so loading the same asset repeatedly incurs zero overhead. The play mode in the editor is driven by the `ResourceServiceDriver` component and can be switched via EditorPrefs.

## Core Features

- Four play modes: `EditorSimulateMode` (editor simulation), `OfflinePlayMode` (standalone), `HostPlayMode` (online hot update), `WebPlayMode` (WebGL, supports WeChat Mini Game file system)
- Reference counting: Load results are registered in the `AssetObject` multi-instance object pool; after `UnloadAsset`, when the count reaches zero the asset can be recycled
- Cancellation and timeout: `LoadAssetAsync<T>` supports `CancellationToken`; concurrent loads of the same address automatically wait, with a 60-second timeout warning in the editor
- Automatic unload on instantiation: Instances created by `LoadGameObject` / `LoadGameObjectAsync` are attached with `AssetsReference`, which automatically returns the reference when destroyed
- Asset encryption: `EncryptionType.FileOffSet` (32-byte offset) and `EncryptionType.FileStream` (XOR stream encryption), with web-side decryption implementation
- Hot update download: Request remote manifest version, update manifest, create downloader, and clear cache files, all available
- UI extensions: `SetSprite` / `SetSubSprite` (sub-sprite/atlas individual sprites) / `SetMaterial` extension methods, automatically managing lifecycle and reference release

## Core Types

Namespace: `Moirai.Atropos.Resource`

| Class/Interface | Description |
|---------|------|
| `IResourceService` | Resource manager interface, defines all APIs for loading, unloading, and package operations; accessed via `GameApp.Services.GetRequiredService<IResourceService>()` |
| `ResourceService` | Internal implementation (`internal sealed partial class`, split into four partial parts: main logic / Pool / AssetObject / Services) |
| `ResourceServiceDriver` | MonoBehaviour driver component, configures play mode, encryption type, download parameters, and resource pool parameters in the Inspector, and periodically executes `UnloadUnusedAssets` |
| `ResourceExtComponent` | Resource extension component (internal), maintains the individual sprite object pool `AssetItemObject` and frame-by-frame recycling, used by the `SetSprite` extension series |
| `AssetsReference` | MonoBehaviour reference component, records the relationship between an instance and its source asset, automatically calls `UnloadAsset` on `OnDestroy` |
| `AssetsSetHelper` | Static extension class: `Image/SpriteRenderer/MeshRenderer.SetMaterial`, `SetSharedMaterial` (synchronous/asynchronous) |
| `SetSpriteExtensions` | Static extension class: `Image/SpriteRenderer.SetSprite`, `SetSubSprite` |
| `SetSpriteObject` / `AssetItemObject` / `LoadAssetObject` / `ISetAssetObject` | Object pool entries and memory pool protocol implementations for individual sprite loading |
| `SubSpriteReference` | Sub-sprite reference counting component, automatically releases the corresponding `SubAssetsHandle` when the GameObject is destroyed |
| `LoadAssetCallbacks` | Callback-based loading callback set, combining success/failure/progress delegates |
| `LoadAssetSuccessCallback` and other delegates | Signatures such as `(string assetName, object asset, float duration, object userData)`, along with scene loading/unloading callback groups |
| `LoadResourceStatus` | Load result status enum: `Success / NotExist / NotReady / DependencyError / TypeError / AssetError` |
| `HasAssetResult` | Asset existence check result: `NotExist / AssetOnline / AssetOnDisk / AssetOnFileSystem / BinaryOnDisk / BinaryOnFileSystem / Valid` |
| `EncryptionType` | Encryption method enum: `None / FileOffSet / FileStream` |
| `FileStreamEncryption` / `FileOffsetEncryption` | Build-side encryption services (implement YooAsset `IEncryptionServices`) |
| `FileStreamDecryption` / `FileOffsetDecryption` and Web variants | Runtime decryption services (implement `IDecryptionServices` / `IWebDecryptionServices`) |
| `RemoteServices` | Remote resource address query service (internal), concatenates primary/backup URLs |

## Quick Start

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

// Synchronous loading
Sprite icon = GameApp.Services.GetRequiredService<IResourceService>().LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// Asynchronous loading (UniTask, supports CancellationToken)
var cts = new CancellationTokenSource();
Texture2D tex = await GameApp.Services.GetRequiredService<IResourceService>().LoadAssetAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token);

// Asynchronous instantiation into the scene: reference is automatically released on Destroy, no manual UnloadAsset needed
GameObject hero = await GameApp.Services.GetRequiredService<IResourceService>().LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// Asynchronous callback-based (success / failure / progress)
GameApp.Services.GetRequiredService<IResourceService>().LoadAssetAsync(
    "Assets/AssetRaw/Audio/bgm.mp3", 0,
    new LoadAssetCallbacks(
        (assetName, asset, duration, userData) => { /* success */ },
        (assetName, status, errorMessage, userData) => { /* failure, status is LoadResourceStatus */ },
        (assetName, progress, userData) => { /* progress 0~1 */ }),
    null);

// Synchronous instantiation
GameObject go = GameApp.Services.GetRequiredService<IResourceService>().LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);

// Unload manually loaded resources (LoadAsset series; recycled by the object pool when reference count reaches zero)
GameApp.Services.GetRequiredService<IResourceService>().UnloadAsset(icon);
```

## Configuration and Extensions

### Play Mode and Encryption

Configured on the `ResourceServiceDriver` component in the scene in the editor (can also be switched via the menu `YooAsset/Editor PlayMode`; editor settings take precedence over serialized values; on device, `EditorSimulateMode` automatically falls back to `OfflinePlayMode`):

- `PlayMode`: Four play modes, determines whether `InitPackage` uses simulated build, built-in file system, cache file system, or web file system
- `EncryptionType`: `None / FileOffSet / FileStream`, the runtime creates the corresponding decryption service based on this
- `PackageName`: Default resource package name (default `DefaultPackage`); for multi-package projects, use the `packageName` parameter in each API to specify other packages
- `Milliseconds`: Maximum time slice per frame for the asynchronous system (default 30ms)
- `AutoUnloadBundleWhenUnused`: Automatically unload resource bundles when reference count reaches zero
- `DownloadingMaxNum` / `FailedTryAgain`: Download concurrency (default 10) and failure retry count (default 3)
- `UpdatableWhilePlaying`: Download while playing
- `AssetAutoReleaseInterval / AssetCapacity / AssetExpireTime / AssetPriority`: Resource object pool parameters
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`: Minimum/maximum interval for unused asset recycling (default 60s / 300s)

### Hot Update Process API

```csharp
IResourceService res = GameApp.Services.GetRequiredService<IResourceService>();

// Initialize a specified resource package (needInitMainFest: true also requests and updates the manifest, for standalone OtherPackage scenarios)
await res.InitPackage("DefaultPackage");

// Online mode: request remote version -> update manifest -> create downloader -> download
var op = res.RequestPackageVersionAsync();
res.PackageVersion = op.PackageVersion;
res.UpdatePackageManifestAsync(res.PackageVersion);
var downloader = res.CreateResourceDownloader();   // then poll the downloader

// Remote address and cache cleanup
res.SetRemoteServicesUrl("https://cdn.example.com/res", "https://backup.example.com/res");
res.ClearCacheFilesAsync();                        // clear unused cache files
res.ClearAllBundleFiles();                         // clear sandbox path
```

### UI / Render Component Extensions

```csharp
// Set an individual sprite for an Image (auto-cached, automatically recycled when the object is destroyed or the sprite is changed)
image.SetSprite("Assets/AssetRaw/UI/icon.png", setNativeSize: true);

// Set a sub-sprite from an atlas (SubAssets, shared SubAssetsHandle via reference counting)
image.SetSubSprite("Assets/AssetRaw/UI/atlas.png", "btn_ok");

// Set a material
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", needInstance: true);
```

### Asset Query and Handles

```csharp
HasAssetResult result = GameApp.Services.GetRequiredService<IResourceService>().HasAsset("Assets/AssetRaw/UI/icon.png");
bool valid = GameApp.Services.GetRequiredService<IResourceService>().CheckLocationValid("Assets/AssetRaw/UI/icon.png");
AssetInfo[] infos = GameApp.Services.GetRequiredService<IResourceService>().GetAssetInfos("Preload");   // batch get by tag

// When fine-grained control over handle lifecycle is needed (bypassing object pool counting)
AssetHandle handle = GameApp.Services.GetRequiredService<IResourceService>().LoadAssetAsyncHandle<GameObject>("path");
// ... use handle.AssetObject, then handle.Dispose() when done
```

## Notes

- `LoadAsset<T>` / `LoadGameObject` returns pooled shared objects; do not `Destroy` them directly. Use `UnloadAsset` to return the reference when you want to destroy; use the `LoadGameObject` series for instantiation.
- `LoadAssetAsync<T>` returns `null` and releases the internal handle when cancelled (via `cancellationToken`); the caller must check for null.
- The WebGL platform does not support `ForceUnloadAllAssets`; calling it will only print a warning.
- The callback-based `LoadAssetAsync(string, int, LoadAssetCallbacks, object, string)` is `async void`; exceptions are reported via `LoadAssetFailureCallback` (`LoadResourceStatus.AssetError`).
- The build-side encryption method (`FileStreamEncryption`, etc.) must match the runtime decryption side. The XOR key for `BundleStream` is a fixed constant (`KEY = 64`), intended only to prevent direct reading.
- `GetAssetInfo` caches results for the default package in a dictionary. After switching manifests (hot update completed), call `UnloadUnusedAssets()` first to get the latest information (this clears the cache).
- On low memory, the system callback `GameApp.OnLowMemory` triggers `ForceUnloadUnusedAssets(true)`, followed by `Resources.UnloadUnusedAssets` and `GC.Collect`.

---
[« Back to Main README](../../README_EN.md) · [Procedure](Procedure.md) · [Save](Save.md)