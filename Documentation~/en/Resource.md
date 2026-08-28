# Resource Service

> A resource management system based on YooAsset, featuring a generation-validated Lease/Binding architecture with zero-GC paged slot arrays, timer-wheel expiry, reference counting, multi-package loading, cancellation control, and encryption/decryption capabilities.

The Resource service (`ResourceService`) provides a business-oriented wrapper around [YooAsset](https://github.com/tuyoogame/YooAsset). The module has been fully refactored to use the **Lease/Binding architecture**: resources are managed through generation-validated slot handles (`ResourceLeaseHandle`) and typed leases (`ResourceAssetLease<T>`), while UI/render components can be bound declaratively via `ResourceOwner` + `IResourceBindingService`. Access via `GameApp.Resource` (`IResourceService`).

The internal engine uses **paged slot arrays** (`AssetSlot[][]`, `LeaseSlot[][]`, `BindingSlot[][]`, `OwnerSlot[][]`) with generation validation, **custom zero-GC hash maps** (`ResourceUlongIntMap` with Murmur finalizer, `ResourceIndexMap<TKey,TValue>`), and a **timer-wheel** expiry system (idle buckets + keep-alive buckets, O(1) per-frame processing). Loading dedup is handled via pooled `LoadingOperationState` objects. Frame-drive orchestration (config injection, timer-wheel advancement, unload scheduling, GC throttling, low-memory response) is wired automatically with the service lifecycle through the `ResourceService.Drive*` partial; the editor play mode can still be switched via EditorPrefs.

## Core Features

- **Lease API (recommended):** `ResourceLeaseHandle` / `ResourceAssetLease<T>` provide safe, scoped resource references. `ResourceAssetLease<T>` implements `IDisposable`, enabling `using` statements for automatic release.
- **Binding API (recommended):** `ResourceOwner` MonoBehaviour + `IResourceBindingService` provide declarative resource-component binding (Sprite, Material, PrefabSource, SubSprite) with automatic release on `OnDestroy`.
- **Extension methods:** `Image.SetSprite(location)`, `SpriteRenderer.SetSprite(location)`, `Image.SetSubSprite(location, spriteName)`, `Image/SpriteRenderer/MeshRenderer.SetMaterial(location)`, `MeshRenderer.SetSharedMaterial(location)` — all auto-manage lifecycle via the binding system.
- **Async binding safety:** Version-checked binding requests prevent stale async results from overwriting newer bindings.
- Four play modes: `EditorSimulateMode` (editor simulation), `OfflinePlayMode` (standalone), `HostPlayMode` (online hot update), `WebPlayMode` (WebGL, supports WeChat Mini Game file system)
- **Timer-wheel expiry:** Idle assets (refcount = 0) are released after `IdleAssetExpireTime` seconds. Keep-alive leases extend the lifetime temporarily. `ProcessKeepAlive` processes both queues in O(1) per frame.
- **Loading dedup:** Concurrent loads of the same address share a single `LoadingOperationState` (pooled `MemoryObject`), with waiter tracking and cancellation support.
- Asset encryption: `EncryptionType.FileOffSet` (32-byte offset) and `EncryptionType.FileStream` (XOR stream encryption), with web-side decryption implementation
- Hot update download: Request remote manifest version, update manifest, create downloader, and clear cache files, all available
- **Legacy API (still works):** `LoadAsset<T>` / `LoadAssetAsync<T>` / `UnloadAsset` / callback-style `LoadAssetAsync` are preserved and internally bridged to the lease system via legacy direct ref counting. Marked `[Obsolete]`.

## Core Types

Namespace: `Moirai.Atropos.Resource`

### Lease / Key Types

| Struct/Interface | Description |
|---------|------|
| `ResourceLeaseHandle` | Generation-validated slot handle (`readonly struct`) for safe resource references. Fields: `Index`, `Generation`. Static `Invalid` represents an invalid handle. `IsValid` checks validity. |
| `ResourceAssetLease<T>` | Typed lease (`struct`, implements `IDisposable`) that holds a resource object and auto-releases on `Dispose`. Supports `using` statements. Fields: `Asset`, `Handle`, `IsValid`. |
| `ResourceKey` | `readonly struct` describing resource location, package, type, and kind. Factory method `ResourceKey.Asset<T>(location, packageName)` creates a typed key. `HasResolvedIds` checks internal ID resolution. |
| `ResourceAssetKind` | Enum: `Unknown / Asset / Sprite / Material / Prefab / SubAssets` |
| `ResourceAssetState` | Enum: `Released / Loading / Active / KeepAlive / Idle` |
| `ResourceAssetInfo` | Diagnostic snapshot struct: LoadKeyId, Package, Location, TypeName, Kind, State, DirectRefCount, LegacyDirectRefCount, BindingRefCount, KeepAliveRefCount, RefCountTotal, IdleExpireIn, etc. |
| `ResourceBindingInfo` | Diagnostic snapshot struct for bindings: Active, BindingIndex, OwnerId, TargetComponentId, Lease, Version, SlotType, HasAppliedAsset, etc. |
| `ResourceOwnerInfo` | Diagnostic snapshot struct for owners: Active, OwnerIndex, OwnerId, GameObjectId, Generation, BindingCount, RegisteredTargetCount. |

### Service Interfaces & Components

| Class/Interface | Description |
|---------|------|
| `IResourceService` | Resource manager interface, defines all APIs for loading, leasing, binding, unloading, and package operations; accessed via `GameApp.Resource` |
| `IResourceBindingService` | Declarative resource-component binding service interface, accessed via `IResourceService.BindingService` |
| `ResourceService` | Internal implementation (`internal sealed partial class`, split into: main logic / Records (slot system + timer-wheel) / Cache (capacity & legacy bridging) / Services) |
| `ResourceService.Driver` | Facade partial: wired automatically on `OnInit` (Settings/UpdateSettings single-source injection + frame-drive registration); hosts `DriveTick` timer-wheel advancement and unload/GC scheduling |
| `ResourceOwner` | MonoBehaviour component (`[DisallowMultipleComponent]`), auto-releases all bindings on `OnDestroy`. Provides `ReleaseBindings()`, `ReleaseBindingsInHierarchy(root)`, `EnsureFor(target, bindingService)`. |
| `ResourceBindingExtensions` | Static extension class: `Image/SpriteRenderer.SetSprite`, `Image/SpriteRenderer.SetSubSprite`, `Image/SpriteRenderer/MeshRenderer.SetMaterial`, `MeshRenderer.SetSharedMaterial` |
| `ResourceBindingTypes` | Binding-related enums and interfaces: `ResourceBindStatus`, `ResourceBindingOptions`, `ResourceBindingSlotType` |
| `LoadAssetCallbacks` | Callback-based loading callback set, combining success/failure/progress delegates |
| `LoadAssetSuccessCallback` and other delegates | Signatures such as `(string assetName, object asset, float duration, object userData)`, along with scene loading/unloading callback groups |
| `LoadResourceStatus` | Load result status enum: `Success / NotExist / NotReady / DependencyError / TypeError / AssetError` |
| `HasAssetResult` | Asset existence check result: `NotExist / AssetOnline / AssetOnDisk / AssetOnFileSystem / BinaryOnDisk / BinaryOnFileSystem / Valid` |
| `EncryptionType` | Encryption method enum: `None / FileOffSet / FileStream` |
| `FileStreamEncryption` / `FileOffsetEncryption` | Build-side encryption services (implement YooAsset `IEncryptionServices`) |
| `FileStreamDecryption` / `FileOffsetDecryption` and Web variants | Runtime decryption services (implement `IDecryptionServices` / `IWebDecryptionServices`) |
| `RemoteServices` | Remote resource address query service (internal), concatenates primary/backup URLs |
| `ResourceUlongIntMap` | Custom open-addressing `ulong→int` hash map with Murmur finalizer, SoA layout, zero GC on hot path (internal) |
| `ResourceIndexMap<TKey,TValue>` | Generic open-addressing hash map using struct keys/values, zero GC hot path (internal) |
| `LoadingOperationState` | Pooled `MemoryObject` for async loading dedup: tracks AssetHandle/SubAssetsHandle, waiter count, completion state (internal) |

## Quick Start

### Lease API (recommended)

The Lease API provides explicit ownership with automatic release via `IDisposable`:

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using UnityEngine;

// Synchronous lease with using-statement (auto-releases on scope exit)
using var lease = GameApp.Resource.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
Sprite icon = lease.Asset;

// Asynchronous lease with CancellationToken
var cts = new CancellationTokenSource();
using (var lease2 = await GameApp.Resource.LoadLeaseAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token))
{
    Texture2D tex = lease2.Asset;
    // ... use tex
} // lease2.Dispose() called automatically

// AcquireDirect for manual handle lifecycle control
ResourceKey key = ResourceKey.Asset<GameObject>("Assets/AssetRaw/Prefabs/Hero.prefab");
ResourceLeaseHandle handle = GameApp.Resource.AcquireDirect(key);
if (GameApp.Resource.TryGetLeaseAsset(handle, out Object asset))
{
    // ... use asset
}
GameApp.Resource.Release(handle);

// Async AcquireDirect
ResourceLeaseHandle handle2 = await GameApp.Resource.AcquireDirectAsync(key, cts.Token);
// ... use handle2
GameApp.Resource.Release(handle2);
```

### Binding API (recommended)

Declarative binding via extension methods — no manual `UnloadAsset` needed:

```csharp
// Set sprite on Image (auto-managed: releases old binding, binds new one)
image.SetSprite("Assets/AssetRaw/UI/icon.png", setNativeSize: true);

// Set sprite on SpriteRenderer
spriteRenderer.SetSprite("Assets/AssetRaw/UI/icon.png");

// Set sub-sprite from atlas
image.SetSubSprite("Assets/AssetRaw/UI/atlas.png", "btn_ok");

// Set material (shared material)
meshRenderer.SetSharedMaterial("Assets/AssetRaw/Mat/skin.mat");

// Set material instance (creates a runtime copy)
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", needInstance: true);

// Async material binding
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", isAsync: true);
```

When a `SetSprite`/`SetMaterial` extension method is first called on a component, a `ResourceOwner` is automatically added to the GameObject (if not present) and registered with the binding service. On `OnDestroy`, `ResourceOwner` releases all bindings automatically.

### Legacy API (still works, marked `[Obsolete]`)

```csharp
// Synchronous loading (internally bridged to lease system via legacy direct ref counting)
Sprite icon = GameApp.Resource.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// Asynchronous loading (UniTask, supports CancellationToken)
var cts = new CancellationTokenSource();
Texture2D tex = await GameApp.Resource.LoadAssetAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token);

// Asynchronous instantiation: reference is automatically released on Destroy
GameObject hero = await GameApp.Resource.LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// Synchronous instantiation
GameObject go = GameApp.Resource.LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);

// Unload manually loaded resources (decrements legacy direct ref count)
GameApp.Resource.UnloadAsset(icon);

// Callback-based async (async void; exceptions reported via LoadAssetFailureCallback)
GameApp.Resource.LoadAssetAsync(
    "Assets/AssetRaw/Audio/bgm.mp3", 0,
    new LoadAssetCallbacks(
        (assetName, asset, duration, userData) => { /* success */ },
        (assetName, status, errorMessage, userData) => { /* failure */ },
        (assetName, progress, userData) => { /* progress 0~1 */ }),
    null);
```

> **Note:** `LoadGameObject` / `LoadGameObjectAsync` are **not** obsolete — they use the new lease system internally (via `AcquirePrefabSourceLease`) and attach a `ResourceOwner` to the instance for automatic cleanup.

## Architecture

### Paged Slot Arrays with Generation Validation

All internal state is managed through paged arrays of struct slots:

- `AssetSlot[][]` — resource records (asset, handle, ref counts, expiry queue links, state)
- `LeaseSlot[][]` — lease records (asset ID, kind, state, generation)
- `BindingSlot[][]` — binding records (owner, target, lease, version, slot type)
- `OwnerSlot[][]` — owner records (GameObject ID, generation, binding chain)

Each page is 256 slots (8-bit page index). Slots are allocated from a free-list stack and validated via a monotonically increasing `Generation` counter. A `ResourceLeaseHandle` is only valid if both the slot index and generation match, preventing use-after-free when a slot is recycled.

### Packed 64-bit ResourceKey

Resource identity is packed into a single `ulong` key encoding: package ID (16 bits) + location ID (28 bits) + type ID (16 bits) + asset kind (2 bits) + handle kind (2 bits). String-to-ID mapping is managed via reference-counted registries (`_resourcePackagesById`, `_resourceLocationsById`, `_resourceTypesById`) with free-list recycling.

### Custom Zero-GC Data Structures

- `ResourceUlongIntMap` — open-addressing `ulong→int` map using Murmur3 finalizer for key mixing, SoA (Structure of Arrays) layout (`_keys[]`, `_values[]`, `_states[]`), 70% load factor, tombstone-based deletion. Hot-path operations (TryGetValue/Set) allocate zero GC.
- `ResourceIndexMap<TKey,TValue>` — generic open-addressing map for struct keys/values, used by the binding service for `OwnerSlotKey → binding index` lookup.

### Timer-Wheel Expiry

Two circular bucket arrays (256 buckets each) drive O(1) per-frame expiry:

- **Idle buckets:** When an asset's refcount reaches zero, it enters an idle bucket scheduled to expire after `IdleAssetExpireTime` seconds.
- **Keep-alive buckets:** When a lease is released with `KeepAliveOnRelease` option, the asset's keep-alive refcount is incremented and scheduled to expire after `IdleAssetExpireTime` seconds.

`ProcessKeepAlive(unscaledTime, maxProcessCount)` is called every frame by the facade's `DriveTick` and processes both queues, releasing assets whose expiry tick has passed.

### Loading Dedup

Concurrent loads of the same resource share a single `LoadingOperationState` (pooled `MemoryObject`). The first caller calls `TryBeginLoading` (returns `true`), subsequent callers get `false` and wait via `WaitForLoadingAsync`. When loading completes/fails, `CompleteLoading`/`FailLoading` notifies all waiters and releases the pooled object.

## Lease API Reference

### ResourceLeaseHandle

```csharp
public readonly struct ResourceLeaseHandle
{
    public static readonly ResourceLeaseHandle Invalid;
    public readonly int Index;
    public readonly uint Generation;
    public bool IsValid => Index >= 0 && Generation != 0;
}
```

### ResourceAssetLease\<T\>

```csharp
public struct ResourceAssetLease<T> : IDisposable where T : UnityEngine.Object
{
    public T Asset { get; }
    public ResourceLeaseHandle Handle { get; }
    public bool IsValid { get; }
    public void Dispose(); // calls IResourceService.Release(Handle)
}
```

### ResourceKey

```csharp
public readonly struct ResourceKey
{
    public readonly string PackageName;
    public readonly string Location;
    public readonly Type AssetType;
    public readonly ResourceAssetKind AssetKind;
    public bool HasResolvedIds { get; }

    public ResourceKey(string location, string packageName = "", Type assetType = null,
        ResourceAssetKind assetKind = ResourceAssetKind.Unknown);

    // Factory: creates a typed key for asset loading
    public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object;
}
```

### IResourceService Lease Methods

| Method | Description |
|--------|-------------|
| `ResourceLeaseHandle AcquireDirect(ResourceKey key)` | Synchronously acquire a direct lease. Returns `Invalid` on failure. |
| `UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken)` | Asynchronously acquire a direct lease. |
| `bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)` | Try-acquire variant. |
| `void Release(ResourceLeaseHandle handle)` | Release a lease (decrements ref count). |
| `ResourceAssetLease<T> LoadLease<T>(ResourceKey key)` | Synchronously load and return a typed lease. |
| `ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "")` | Synchronously load and return a typed lease by location. |
| `UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken)` | Asynchronously load and return a typed lease. |
| `UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken, string packageName)` | Asynchronously load and return a typed lease by location. |
| `bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)` | Read the Unity asset from a lease handle. |
| `IResourceBindingService BindingService { get; }` | Access the binding service. |

## Binding API Reference

### ResourceOwner

```csharp
[DisallowMultipleComponent]
public sealed class ResourceOwner : MonoBehaviour
{
    public int OwnerId { get; }
    public ulong GameObjectId { get; }
    public uint Generation { get; }
    public bool IsRegistered { get; }

    public ResourceBindStatus ReleaseBindings(); // Release all bindings on this owner

    // Release all ResourceOwner bindings in a hierarchy
    public static int ReleaseBindingsInHierarchy(GameObject root);

    // Ensure a ResourceOwner exists on the target component's GameObject
    public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService);

    public static void WarmupReleaseBuffer(int capacity);
}
```

### IResourceBindingService

| Method | Description |
|--------|-------------|
| `ResourceBindStatus RegisterOwner(ResourceOwner owner)` | Register an owner. |
| `ResourceBindStatus ReleaseOwner(ResourceOwner owner)` | Release an owner and all its bindings. |
| `ResourceBindStatus ReleaseOwner(int ownerId, uint generation)` | Release by ID + generation. |
| `void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity)` | Preallocate binding data structures. |
| `ResourceBindStatus RegisterTarget(ResourceOwner, Component)` | Register a target component for tracking. |
| `ResourceBindStatus UnregisterTarget(ResourceOwner, Component)` | Unregister a target component. |
| `ResourceBindStatus BindSprite(ResourceOwner, Image, ResourceKey, options)` | Bind a sprite to an Image. |
| `ResourceBindStatus BindSprite(ResourceOwner, SpriteRenderer, ResourceKey, options)` | Bind a sprite to a SpriteRenderer. |
| `UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner, Image, ResourceKey atlasKey, string spriteName, options, CancellationToken)` | Async bind a sub-sprite from an atlas. |
| `ResourceBindStatus BindImageMaterial(ResourceOwner, Image, ResourceKey, options)` | Bind a material to an Image. |
| `UniTask<ResourceBindStatus> BindImageMaterialAsync(ResourceOwner, Image, ResourceKey, options, CancellationToken)` | Async bind a material to an Image. |
| `ResourceBindStatus BindSharedMaterial(ResourceOwner, Renderer, ResourceKey, options)` | Bind shared material to a Renderer. |
| `UniTask<ResourceBindStatus> BindSharedMaterialAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | Async bind shared material. |
| `ResourceBindStatus BindMaterialInstance(ResourceOwner, Renderer, ResourceKey, options)` | Bind material instance (runtime copy) to a Renderer. |
| `UniTask<ResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | Async bind material instance. |
| `int GetOwnerInfos(ResourceOwnerInfo[], int, int)` | Diagnostic: batch query owner states. |
| `int GetBindingInfos(ResourceBindingInfo[], int, int)` | Diagnostic: batch query binding states. |

### ResourceBindStatus

Enum values: `Success / InvalidKey / MissingOwner / MissingTarget / StaleOwner / LoadFailed / ApplyFailed / ServiceShutdown / NotImplemented`

### ResourceBindingOptions

Flags enum: `None / KeepAliveOnRelease / SetNativeSize`

### Extension Methods (ResourceBindingExtensions)

| Extension | Description |
|-----------|-------------|
| `Image.SetSprite(string location, bool setNativeSize = false, CancellationToken)` | Set sprite on Image via binding. |
| `SpriteRenderer.SetSprite(string location, CancellationToken)` | Set sprite on SpriteRenderer via binding. |
| `Image.SetSubSprite(string location, string spriteName, bool setNativeSize = false, CancellationToken)` | Set sub-sprite from atlas on Image. |
| `Image.SetMaterial(string location, bool isAsync = false, string packageName = "")` | Set material on Image. |
| `SpriteRenderer.SetMaterial(string location, bool isAsync = false, string packageName = "")` | Set material on SpriteRenderer. |
| `MeshRenderer.SetMaterial(string location, bool needInstance = true, bool isAsync = false, string packageName = "")` | Set material on MeshRenderer (instance or shared). |
| `MeshRenderer.SetSharedMaterial(string location, bool isAsync = false, string packageName = "")` | Set shared material on MeshRenderer. |

### Async Binding Safety

Async binding methods (e.g. `BindSubSpriteAsync`, `BindImageMaterialAsync`, `BindSharedMaterialAsync`, `BindMaterialInstanceAsync`) use **version-checked binding requests** to prevent stale results:

1. Before the async load starts, a binding slot is reserved with an incremented `Version`.
2. After the async load completes, `IsBindingRequestCurrent` checks that the owner generation, target component ID, and binding version all match.
3. If the owner was destroyed, the target was replaced, or a newer binding request superseded this one, the stale result is discarded and the lease is released.

## Capacity and Expiry Properties

Configured in the `ResourceServiceSettings` (Framework settings asset) or via `IResourceService`:

| Property | Default | Description |
|----------|---------|-------------|
| `AssetRecordCapacity` | 64 | Resource record preallocation (AssetSlot pages). |
| `AssetLeaseCapacity` | 128 | Lease slot preallocation (LeaseSlot pages). |
| `BindingOwnerCapacity` | 64 | Binding owner preallocation (OwnerSlot pages). |
| `BindingSlotCapacity` | 128 | Binding slot preallocation (BindingSlot pages). |
| `RegisteredTargetCapacity` | 128 | Registered target preallocation. |
| `IdleAssetExpireTime` | 60s | Seconds before idle (refcount=0) assets are released. |
| `ExpireProcessCountPerFrame` | 16 | Max expiry items processed per frame. |
| `ExpireProcessCountWhenUnloading` | 256 | Max expiry items processed during unload. |

### WarmupResourceRecords

```csharp
void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity);
```

Preallocates internal data structures (slot pages, index maps) to avoid runtime resizing. Called automatically when capacity properties are set.

### GetAssetInfos (diagnostic)

```csharp
int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
```

Batch query for asset record states. Returns the number of entries written. Each `ResourceAssetInfo` includes package, location, type, kind, state, ref counts (direct/legacy/binding/keep-alive), and expiry info.

## Unload API

| Method | Description |
|--------|-------------|
| `void UnloadUnusedAssets()` | Release unused assets (refcount=0) that have passed idle expire time. Also clears AssetInfo cache and calls `package.UnloadUnusedAssetsAsync()`. |
| `void UnloadUnusedAssets(bool force)` | `force=true`: ignores idle expire time, immediately processes keep-alive queue and releases all unused records. |
| `void ForceUnloadAllAssets()` | Force unload all assets on all packages (not supported on WebGL — prints warning). |
| `void ForceUnloadUnusedAssets(bool performGCCollect)` | Triggers the driver's force-unload path (optionally with GC.Collect). |
| `void ProcessKeepAlive(float unscaledTime, int maxProcessCount)` | Per-frame timer-wheel expiry processing (idle + keep-alive buckets). Called by `ResourceService.DriveTick()`. |

## Configuration and Extensions

### Play Mode and Encryption

Configured on the Handler (`YooAssetHandler`) serialized fields of the `ResourceServiceSettings` asset in the editor (can also be switched via the menu `YooAsset/Editor PlayMode`; editor settings take precedence over serialized values; on device, `EditorSimulateMode` automatically falls back to `OfflinePlayMode`):

- `PlayMode`: Four play modes, determines whether `InitPackage` uses simulated build, built-in file system, cache file system, or web file system
- `EncryptionType`: `None / FileOffSet / FileStream`, the runtime creates the corresponding decryption service based on this
- `PackageName`: Default resource package name (default `DefaultPackage`); for multi-package projects, use the `packageName` parameter in each API to specify other packages
- `Milliseconds`: Maximum time slice per frame for the asynchronous system (default 30ms)
- `AutoUnloadBundleWhenUnused`: Automatically unload resource bundles when reference count reaches zero
- `DownloadingMaxNum` / `FailedTryAgain`: Download concurrency (default 10) and failure retry count (default 3)
- `UpdatableWhilePlaying`: Download while playing
- `AssetAutoReleaseInterval / AssetCapacity / AssetExpireTime / AssetPriority`: Legacy pool parameters (bridged to IdleAssetExpireTime / AssetRecordCapacity)
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`: Minimum/maximum interval for unused asset recycling (default 60s / 300s)
- `UseSystemUnloadUnusedAssets`: Whether to call `IResourceService.UnloadUnusedAssets()` during the system unload cycle

### Hot Update Process API

```csharp
IResourceService res = GameApp.Resource;

// Initialize a specified resource package (needInitMainFest: true also requests and updates the manifest, for standalone OtherPackage scenarios)
await res.InitPackage("DefaultPackage");

// Online mode: request remote version -> update manifest -> create downloader -> download
var op = res.RequestPackageVersionAsync();
res.PackageVersion = op.PackageVersion;
res.UpdatePackageManifestAsync(res.PackageVersion);
var downloader = res.CreateResourceDownloader();   // then poll the downloader

// Download size query: pending bytes for a location (for remaining-download UI; throws GameException on invalid location/package)
long downloadBytes = res.GetDownloadSize("Assets/AssetRaw/UI/logo.png");
bool needRemote = res.IsNeedDownloadFromRemote("Assets/AssetRaw/UI/logo.png");

// Remote address and cache cleanup
res.SetRemoteServicesUrl("https://cdn.example.com/res", "https://backup.example.com/res");
res.ClearCacheFilesAsync();                        // clear unused cache files
res.ClearAllBundleFiles();                         // clear sandbox path
```

### Asset Query and Handles

```csharp
HasAssetResult result = GameApp.Resource.HasAsset("Assets/AssetRaw/UI/icon.png");
bool valid = GameApp.Resource.CheckLocationValid("Assets/AssetRaw/UI/icon.png");
AssetInfo[] infos = GameApp.Resource.GetAssetInfos("Preload");   // batch get by tag

// When fine-grained control over handle lifecycle is needed (bypassing lease system)
AssetHandle handle = GameApp.Resource.LoadAssetAsyncHandle<GameObject>("path");
// ... use handle.AssetObject, then handle.Dispose() when done
```

## Notes

- **Addressables backend (experimental):** `AddressableHandler` supports only information queries and real cache maintenance; capability-missing members (lease/binding/instantiation/version/downloader) uniformly throw `GameException` (fail-fast) instead of silently returning Invalid or empty results. Use `YooAssetHandler` in production.

- **Lease API:** `ResourceAssetLease<T>` is a `struct` — always `Dispose` it (use `using` statement). After Dispose, `IsValid` returns `false` and `Asset` is `null`.
- **Binding API:** `SetSprite`/`SetMaterial` extension methods auto-add a `ResourceOwner` to the target's GameObject if not present. All bindings are released when the GameObject is destroyed.
- **Legacy API:** `LoadAsset<T>` / `LoadGameObject` return pooled shared objects; do not `Destroy` them directly. Use `UnloadAsset` to return the reference. `LoadGameObject`/`LoadGameObjectAsync` use the lease system internally and attach `ResourceOwner` for auto-cleanup.
- `LoadAssetAsync<T>` returns `null` and releases the internal handle when cancelled (via `cancellationToken`); the caller must check for null.
- The WebGL platform does not support `ForceUnloadAllAssets`; calling it will only print a warning.
- The callback-based `LoadAssetAsync(string, int, LoadAssetCallbacks, object, string)` is `async void`; exceptions are reported via `LoadAssetFailureCallback` (`LoadResourceStatus.AssetError`).
- The build-side encryption method (`FileStreamEncryption`, etc.) must match the runtime decryption side. The XOR key for `BundleStream` is a fixed constant (`KEY = 64`), intended only to prevent direct reading.
- `GetAssetInfo` caches results for the default package in a dictionary. After switching manifests (hot update completed), call `UnloadUnusedAssets()` first to get the latest information (this clears the cache).
- On low memory, the system callback `GameApp.OnLowMemory` triggers `ForceUnloadUnusedAssets(true)`, followed by `Resources.UnloadUnusedAssets` and `GC.Collect`.

---
[« Back to Main README](../../README_EN.md) · [Procedure](Procedure.md) · [Save](Save.md)