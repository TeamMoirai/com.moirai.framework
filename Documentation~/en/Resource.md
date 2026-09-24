# Resource Service

> A resource management system based on YooAsset, featuring a generation-validated Lease/Binding architecture with zero-GC paged slot arrays, timer-wheel expiry, reference counting, multi-package loading, cancellation control, and encryption/decryption capabilities.

The Resource service (`ResourceService`) provides a business-oriented wrapper around [YooAsset](https://github.com/tuyoogame/YooAsset). The module has been fully refactored to use the **Lease/Binding architecture**: resources are managed through generation-validated slot handles (`ResourceLeaseHandle`) and typed leases (`ResourceAssetLease<T>`), while UI/render components can be bound declaratively via `ResourceOwner` + `IResourceBindingService`. Access via the `ResourceService` static facade.

The internal engine uses **paged slot arrays** (`AssetSlot[][]`, `LeaseSlot[][]`, `BindingSlot[][]`, `OwnerSlot[][]`) with generation validation, **custom zero-GC hash maps** (`ResourceUlongIntMap` with Murmur finalizer, `ResourceIndexMap<TKey,TValue>`), and a **timer-wheel** expiry system (idle buckets + keep-alive buckets, O(1) per-frame processing). Loading dedup is handled via pooled `LoadingOperationState` objects. Frame-drive orchestration (config injection, timer-wheel advancement, destroyed-slot reclaim sweep, unload scheduling, GC throttling, low-memory response) is wired automatically with the service lifecycle through the `ResourceService.Drive*` partial; the editor play mode can still be switched via EditorPrefs.

## Core Features

- **Lease API (recommended):** `ResourceLeaseHandle` / `ResourceAssetLease<T>` provide safe, scoped resource references. `ResourceAssetLease<T>` implements `IDisposable`, enabling `using` statements for automatic release.
- **Binding API (recommended):** `ResourceOwner` MonoBehaviour + `IResourceBindingService` provide declarative resource-component binding (Sprite, Material, PrefabSource, SubSprite) with automatic release on `OnDestroy`.
- **Extension methods:** `Image.SetSprite(location)`, `SpriteRenderer.SetSprite(location)`, `Image.SetSubSprite(location, spriteName)`, `Image/SpriteRenderer/MeshRenderer.SetMaterial(location)`, `MeshRenderer.SetSharedMaterial(location)` — all auto-manage lifecycle via the binding system.
- **Async binding safety:** Version-checked binding requests prevent stale async results from overwriting newer bindings.
- Four play modes: `EditorSimulateMode` (editor simulation), `OfflinePlayMode` (standalone), `HostPlayMode` (online hot update), `WebPlayMode` (WebGL, supports WeChat Mini Game file system). If the asset still says `EditorSimulate` in a player build, `ResourceServiceSettings.PlayMode` normalises to `OfflinePlay` **in the value it returns only** (logging one Error on first read); it does not write back into the shared settings instance, so the misconfiguration stays visible in the asset and the inspector.
- **Timer-wheel expiry:** Idle assets (refcount = 0) are released after `IdleAssetExpireTime` seconds; when the idle record count exceeds `IdleAssetCapacity` the longest-idle records are trimmed, bounded per frame. Keep-alive leases can extend a record temporarily. `ProcessResourceMaintenance` handles both wheels in O(1) per frame and rotates the destroyed-state owner/binding slot sweep.
- **Loading dedup:** Concurrent loads of the same address share a single `LoadingOperationState` (pooled `MemoryObject`), with waiter tracking and cancellation support.
- Asset encryption: `YooAssetEncryptorHandler` 的 `FileOffsetEncryptorHandler` (32-byte offset) and `FileStreamEncryptorHandler` (XOR stream encryption), with web-side decryption implementation
- Hot update download: Request remote manifest version, update manifest, create downloader, and clear cache files, all available

## Core Types

Namespace: `Moirai.Atropos.Resource`

### Lease / Key Types

| Struct/Interface | Description |
|---------|------|
| `ResourceLeaseHandle` | Generation-validated slot handle (`readonly struct`) for safe resource references. Fields: `Index`, `Generation`. Static `Invalid` represents an invalid handle. `IsValid` checks validity. |
| `ResourceAssetLease<T>` | Typed lease (`struct`, implements `IDisposable`) that holds a resource object and auto-releases on `Dispose`. Supports `using` statements. Fields: `Asset`, `Handle`, `IsValid`. |
| `ResourceKey` | `readonly struct` describing resource location, package, type, and kind. Factory method `ResourceKey.Asset<T>(location, packageName)` creates a typed key. `HasResolvedIds` checks internal ID resolution. |
| `EResourceAssetKind` | Enum: `Unknown / Asset / Sprite / Material / Prefab / SubAssets` |
| `EResourceAssetState` | Enum: `Released / Loading / Active / KeepAlive / Idle` |
| `ResourceAssetInfo` | Diagnostic snapshot struct: LoadKeyId, Package, Location, TypeName, Kind, State, DirectRefCount, BindingRefCount, KeepAliveRefCount, RefCountTotal, IdleExpireIn, etc. |
| `ResourceBindingInfo` | Diagnostic snapshot struct for bindings: Active, BindingIndex, OwnerId, TargetComponentId, Lease, Version, SlotType, HasAppliedAsset, etc. |
| `ResourceOwnerInfo` | Diagnostic snapshot struct for owners: Active, OwnerIndex, OwnerId, GameObjectId, Generation, BindingCount. |

### Service Interfaces & Components

| Class/Interface | Description |
|---------|------|
| `ResourceService` | Static facade (`[HandlerHost]`) defining all APIs for loading, leasing, binding, unloading, and package operations; all static methods/properties forward through the `Handler` property (fail-fast: lazily initialized when not ready, throws if the default factory is missing, never silently degrades). Configuration is injected in `OnInit`; the per-frame driver (timer-wheel advancement, unload scheduling, GC throttling, destroyed-slot reclaim) runs in `Tick` |
| `YooAssetHandler` | Default backend, `partial` split by responsibility: main (base properties, unload scheduling, asset info queries, prefab instantiation, host-interface wiring) / Leases (forwards of the Lease API on the seam) / Loading (acquiring YooAsset handles, attaching them, failure text) / Cache (settings injection and capacity warmup) / Initialization (package and manifest init) / Scene (scene handles) / Attributes (inspector annotations). **Bookkeeping no longer lives here** - see the three kernel types below |
| `ResourceRecordStore` | The resource record kernel (`internal sealed partial`, under `Runtime/Services/Resource/Kernel/`), held by the backend: generation-checked asset and lease slots, two packed-key index maps, loading dedup, both expiry wheels and capacity trimming. It reaches the backend only through `IResourceRecordHost`: three raw-handle operations plus three configuration reads |
| `ResourceKeyCodec` | Bit-field packing/unpacking of the resource key and `assetKind` / `assetType` inference (pure static, no state) |
| `ResourceNameRegistry<TValue>` | Registration, refcounting and ID reclamation for one name axis; one instance per package / location / type axis |
| `ResourceBindingService` | Binding service implementation (`internal sealed`), `partial` split by responsibility: main (owner registration, release, slot snapshots) / Bindings (binding registration & component application) / Async (async binding safety, request reservation and generation checks) / Maintenance (shutdown, reset, destroyed-slot reclaim) / Slots (paged slot allocation) |
| `ResourceServiceHandler` | Handler abstract base class defining the backend contract; default implementation `YooAssetHandler` (plus experimental `AddressableHandler`) |
| `IResourceBindingService` | Declarative resource-component binding service interface, accessed via `ResourceService.BindingService` |
| `ResourceOwner` | MonoBehaviour component (`[DisallowMultipleComponent]`), auto-releases all bindings on `OnDestroy`. Provides `ReleaseBindings()` and `EnsureFor(target, bindingService)`. A single binding owner throwing is recorded without truncating the rest, rethrown aggregated at the end. |
| `ResourceBindingExtension` | Static extension class: `Image/SpriteRenderer.SetSprite`, `Image/SpriteRenderer.SetSubSprite`, `Image/SpriteRenderer/MeshRenderer.SetMaterial`, `MeshRenderer.SetSharedMaterial` |
| `ResourceBindingService` | Binding-related enums and interfaces: `EResourceBindStatus`, `EResourceBindingOptions`, `EResourceBindingSlotType` |
| `EResourceHasAssetResult` | Asset existence check result (three-value semantics): `NotExist` (not found) / `AssetOnline` (exists but needs remote download) / `AssetOnDisk` (exists and available on disk) |
| `YooAssetEncryptorHandler` | 加密配置的抽象基类（`[SerializeReference]` 挂在 `YooAssetHandler` 上）：`CreateEncryptor()` 供打包侧、`CreateDecryptor()` 供运行侧；内置 `FileOffset*` 与 `FileStream*` 两套实现，打包侧与运行侧必须选同一套 |
| `FileStreamEncryptor` / `FileOffsetEncryptor` | Build-side encryption services (implement YooAsset `IEncryptionServices`) |
| `FileStreamDecryptor` / `FileOffsetDecryptor` and Web variants | Runtime decryption services (implement `IDecryptionServices` / `IWebDecryptionServices`) |
| `RemoteService` | Remote resource address query service (internal), concatenates primary/backup URLs |
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
using var lease = ResourceService.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
Sprite icon = lease.Asset;

// Asynchronous lease with CancellationToken
var cts = new CancellationTokenSource();
using (var lease2 = await ResourceService.LoadLeaseAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token))
{
    Texture2D tex = lease2.Asset;
    // ... use tex
} // lease2.Dispose() called automatically

// AcquireDirect for manual handle lifecycle control
ResourceKey key = ResourceKey.Asset<GameObject>("Assets/AssetRaw/Prefabs/Hero.prefab");
ResourceLeaseHandle handle = ResourceService.AcquireDirect(key);
if (ResourceService.TryGetLeaseAsset(handle, out Object asset))
{
    // ... use asset
}
ResourceService.Release(handle);

// Async AcquireDirect
ResourceLeaseHandle handle2 = await ResourceService.AcquireDirectAsync(key, cts.Token);
// ... use handle2
ResourceService.Release(handle2);
```

### Binding API (recommended)

Declarative binding via extension methods — bindings are released together with their owning `ResourceOwner`, with no manual release step at all:

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

Two boundaries are worth knowing:

- **Destroyed-state backstop:** scene teardown and play-mode exit can skip `OnDestroy`, leaving the owner slot and its leases pinned. The per-frame maintenance entry rotates a budgeted sweep that force-reclaims slots whose component is fake-null (destroyed on the engine side, still referenced in C#).
- **Shutdown vs reset:** `Shutdown()` is terminal — after draining it stays closed and every later registration returns `ServiceShutdown` (the slot pages are gone, so admitting writes would target a null table). Force-unloading all assets uses `Reset()`, which drains the same way and then reopens the instance. Both paths isolate exceptions per slot so one failure cannot truncate the round.

### Prefab Instantiation

```csharp
// Asynchronous instantiation: destroying the instance returns the prefab source lease automatically
GameObject hero = await ResourceService.LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// Synchronous instantiation
GameObject go = ResourceService.LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);
```

> **Note:** `LoadGameObject` / `LoadGameObjectAsync` return an **instantiated copy** — internally they take a prefab source lease via `AcquirePrefabSourceLease` and bind it to a `ResourceOwner` attached to the instance. `Destroy`-ing the instance releases that lease; the caller does not own the prefab source, so do not destroy the source prefab object itself. When you need to control the resource lifetime yourself, use the Lease API instead: `LoadLease<T>` / `LoadLeaseAsync<T>` carry the reference through explicit ownership, with no manual pairing step.

## Architecture

### Paged Slot Arrays with Generation Validation

All internal state is managed through paged arrays of struct slots:

- `AssetSlot[][]` — resource records (asset, handle, ref counts, expiry queue links, state)
- `LeaseSlot[][]` — lease records (asset ID, kind, state, generation)
- `BindingSlot[][]` — binding records (owner, target, lease, version, slot type)
- `OwnerSlot[][]` — owner records (GameObject ID, generation, binding chain)

Each page is 256 slots (8-bit page index). Slots are allocated from a free-list stack and validated via a monotonically increasing `Generation` counter. A `ResourceLeaseHandle` is only valid if both the slot index and generation match, preventing use-after-free when a slot is recycled.

### Packed 64-bit ResourceKey

Resource identity is packed into a single `ulong` key encoding: package ID (12 bits) + location ID (32 bits) + type ID (12 bits) + asset kind (4 bits) + handle kind (4 bits). String and `Type` to ID mapping is owned by three `ResourceNameRegistry<TValue>` axes (refcounted, IDs reclaimed), while the bit layout and its range checks live in `ResourceKeyCodec`. An ID exceeding its axis **throws `GameException` instead of silently truncating** - truncation would encode two different resources into the same key.

### Custom Zero-GC Data Structures

- `ResourceUlongIntMap` — open-addressing `ulong→int` map using Murmur3 finalizer for key mixing, SoA (Structure of Arrays) layout (`_keys[]`, `_values[]`, `_states[]`), 70% load factor, tombstone-based deletion. Hot-path operations (TryGetValue/Set) allocate zero GC.
- `ResourceIndexMap<TKey,TValue>` — generic open-addressing map for struct keys/values, used by the binding service for `OwnerSlotKey → binding index` lookup.

### Timer-Wheel Expiry

Two circular bucket arrays (256 buckets each) drive O(1) per-frame expiry:

- **Idle buckets:** When an asset's refcount reaches zero, it enters an idle bucket scheduled to expire after `IdleAssetExpireTime` seconds.
- **Keep-alive buckets:** When a lease is released with `KeepAliveOnRelease` option, the asset's keep-alive refcount is incremented and scheduled to expire after `IdleAssetExpireTime` seconds.
- **Capacity cap:** When idle records exceed `IdleAssetCapacity`, the one with the earliest expiry tick (i.e. idle the longest) is released without waiting for expiry, at most 8 per pass; whatever is left over stays requested for the next frame. Lowering the cap only files that request too - it does not trim inline, because that would hang an O(n) burst on a property assignment. Capacity trimming runs after the wheel sweep, since removing nodes mid-sweep invalidates captured next pointers and skips whole buckets.

`ProcessResourceMaintenance(unscaledTime, expireBudget, destroySweepBudget)` is called every frame by the facade: the destroyed-slot sweep goes first (removing nodes mid-sweep would corrupt wheel cursors), then both queues are processed, and `IdleAssetCapacity` trimming runs last, bounded to 8 releases per pass. All of the bookkeeping lives in `ResourceRecordStore`; this entry point is only a forwarding layer on the backend seam.

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
    public void Dispose(); // calls ResourceService.Release(Handle)
}
```

### ResourceKey

```csharp
public readonly struct ResourceKey
{
    public readonly string PackageName;
    public readonly string Location;
    public readonly Type AssetType;
    public readonly EResourceAssetKind AssetKind;
    public bool HasResolvedIds { get; }

    public ResourceKey(string location, string packageName = "", Type assetType = null,
        EResourceAssetKind assetKind = EResourceAssetKind.Unknown);

    // Factory: creates a typed key for asset loading
    public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object;
}
```

### ResourceService Lease Methods

| Method | Description |
|--------|-------------|
| `ResourceLeaseHandle AcquireDirect(ResourceKey key)` | Synchronously acquire a direct lease. Returns `Invalid` on failure. |
| `UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken)` | Asynchronously acquire a direct lease. |
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

    public EResourceBindStatus ReleaseBindings(); // Release all bindings on this owner

    // Ensure a ResourceOwner exists on the target component's GameObject
    public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService);

    public static void WarmupReleaseBuffer(int capacity);
}
```

### IResourceBindingService

| Method | Description |
|--------|-------------|
| `EResourceBindStatus RegisterOwner(ResourceOwner owner)` | Register an owner. |
| `EResourceBindStatus ReleaseOwner(ResourceOwner owner)` | Release an owner and all its bindings. |
| `EResourceBindStatus ReleaseOwner(int ownerId, uint generation)` | Release by ID + generation. |
| `void Warmup(int ownerCapacity, int bindingCapacity)` | Preallocate binding data structures. |
| `EResourceBindStatus BindSprite(ResourceOwner, Image, ResourceKey, options)` | Bind a sprite to an Image. |
| `EResourceBindStatus BindSprite(ResourceOwner, SpriteRenderer, ResourceKey, options)` | Bind a sprite to a SpriteRenderer. |
| `UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner, Image, ResourceKey atlasKey, string spriteName, options, CancellationToken)` | Async bind a sub-sprite from an atlas. |
| `EResourceBindStatus BindImageMaterial(ResourceOwner, Image, ResourceKey, options)` | Bind a material to an Image. |
| `UniTask<EResourceBindStatus> BindImageMaterialAsync(ResourceOwner, Image, ResourceKey, options, CancellationToken)` | Async bind a material to an Image. |
| `EResourceBindStatus BindSharedMaterial(ResourceOwner, Renderer, ResourceKey, options)` | Bind shared material to a Renderer. |
| `UniTask<EResourceBindStatus> BindSharedMaterialAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | Async bind shared material. |
| `EResourceBindStatus BindMaterialInstance(ResourceOwner, Renderer, ResourceKey, options)` | Bind material instance (runtime copy) to a Renderer. |
| `UniTask<EResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | Async bind material instance. |
| `int GetOwnerInfos(ResourceOwnerInfo[], int, int)` | Diagnostic: batch query owner states. |
| `int GetBindingInfos(ResourceBindingInfo[], int, int)` | Diagnostic: batch query binding states. |

### EResourceBindStatus

Enum values: `Success / InvalidKey / MissingOwner / MissingTarget / StaleOwner / Cancelled / LoadFailed / ApplyFailed / ServiceShutdown`

### EResourceBindingOptions

Flags enum: `None / KeepAliveOnRelease / SetNativeSize`

### Extension Methods (ResourceBindingExtension)

| Extension | Description |
|-----------|-------------|
| `Image.SetSprite(string location, bool setNativeSize = false, CancellationToken, string packageName = "")`
| `SpriteRenderer.SetSprite(string location, CancellationToken, string packageName = "")`
| `Image.SetSubSprite(string location, string spriteName, bool setNativeSize = false, CancellationToken, string packageName = "")`
| `SpriteRenderer.SetSubSprite(string location, string spriteName, CancellationToken, string packageName = "")` | Sets a sub-sprite on a SpriteRenderer from an atlas. |
| `Image.SetMaterial(string location, bool isAsync = false, string packageName = "")` | Set material on Image. |
| `SpriteRenderer.SetMaterial(string location, bool isAsync = false, string packageName = "")` | Set material on SpriteRenderer. |
| `MeshRenderer.SetMaterial(string location, bool needInstance = true, bool isAsync = false, string packageName = "")` | Set material on MeshRenderer (instance or shared). |
| `MeshRenderer.SetSharedMaterial(string location, bool isAsync = false, string packageName = "")` | Set shared material on MeshRenderer. |

### Async Binding Safety

> **Null and destroyed targets:** every extension entry first checks the target component and its `gameObject`, and does **nothing silently** when either is gone (no throw, no log); the binding service answers an empty owner or target with `EResourceBindStatus.MissingOwner` / `MissingTarget`, leaving it to the caller to record. Hitting a destroyed object during shutdown is routine, so "nothing to do" is deliberately not an exception.

Async binding methods (e.g. `BindSubSpriteAsync`, `BindImageMaterialAsync`, `BindSharedMaterialAsync`, `BindMaterialInstanceAsync`) use **version-checked binding requests** to prevent stale results:

1. Before the async load starts, a binding slot is reserved with an incremented `Version`.
2. After the async load completes, `IsBindingRequestCurrent` checks that the owner generation, target component ID, and binding version all match.
3. If the owner was destroyed, the target was replaced, or a newer binding request superseded this one, the stale result is discarded and the lease is released.

## Capacity and Expiry Properties

Configured in the `ResourceServiceSettings` (Framework settings asset) or via `ResourceService`:

| Property | Default | Description |
|----------|---------|-------------|
| `AssetRecordCapacity` | 64 | Resource record preallocation (AssetSlot pages). |
| `AssetLeaseCapacity` | 128 | Lease slot preallocation (LeaseSlot pages). |
| `BindingOwnerCapacity` | 64 | Binding owner preallocation (OwnerSlot pages). |
| `BindingSlotCapacity` | 128 | Binding slot preallocation (BindingSlot pages). |
| `IdleAssetExpireTime` | 60s | Seconds before idle (refcount=0) assets are released. |
| `IdleAssetCapacity` | 256 | Max idle asset records kept; over the cap the longest-idle record is released immediately, 0 keeps none. |
| `ExpireProcessCountPerFrame` | 16 | Max expiry items processed per frame. |
| `ExpireProcessCountWhenUnloading` | 256 | Max expiry items processed during unload. |

### WarmupResourceRecords

```csharp
void WarmupResourceRecords(int assetCapacity, int leaseCapacity);
```

Preallocates internal data structures (slot pages, index maps) to avoid runtime resizing. Called automatically when capacity properties are set.

### GetAssetInfos (diagnostic)

```csharp
int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
```

Batch query for asset record states. Returns the number of entries written. Each `ResourceAssetInfo` includes package, location, type, kind, state, ref counts (direct/binding/keep-alive), and expiry info.

## Unload API

| Method | Description |
|--------|-------------|
| `void UnloadUnusedAssets()` | Release unused assets (refcount=0) that have passed idle expire time. Also clears AssetInfo cache and calls `package.UnloadUnusedAssetsAsync()`. |
| `void UnloadUnusedAssets(bool force)` | `force=true`: ignores idle expire time, immediately processes keep-alive queue and releases all unused records. |
| `void ForceUnloadAllAssets()` | Force unload all assets on all packages (not supported on WebGL — prints warning). |
| `void ForceUnloadUnusedAssets(bool performGCCollect)` | Triggers the driver's force-unload path (optionally with GC.Collect). |
| `void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)` | Per-frame resource maintenance: timer-wheel expiry (idle + keep-alive buckets), idle capacity eviction, destroyed-slot reclaim sweep. **internal**, called by `ResourceService.Tick()`. |

## Configuration and Extensions

### Play Mode and Encryption

Configured on the Handler (`YooAssetHandler`) serialized fields of the `ResourceServiceSettings` asset in the editor (can also be switched via the menu `YooAsset/Editor PlayMode`; editor settings take precedence over serialized values; on device, `EditorSimulateMode` automatically falls back to `OfflinePlayMode`):

- `PlayMode`: Four play modes, determines whether `InitializePackageAsync` uses simulated build, built-in file system, cache file system, or web file system
- Encryption is decided by the `[SerializeReference]` `YooAssetHandler.EncryptorHandler` setting (no encryption when unset); the runtime creates the matching decryptor from it. The build-side and runtime-side handlers must be the same pair.
- `PackageName`: Default resource package name (default `DefaultPackage`); for multi-package projects, use the `packageName` parameter in each API to specify other packages

The following runtime configuration properties are promoted to the abstract contract, readable and writable on both the facade and the handler (the handler's serialized fields are the default source):

| Property | Default | Description |
|----------|---------|-------------|
| `Milliseconds` | 30ms | Max time slice per frame for the async system; the facade setter applies `SetAsyncOperationMaxTimeSlice` immediately, negative values throw `GameException` |
| `AutoUnloadBundleWhenUnused` | false | Automatically unload resource bundles when reference count reaches zero (read during init/unload decisions) |
| `DownloadingMaxNum` | 10 | Download concurrency (passed to `ResourceDownloaderOptions` at downloader creation) |
| `FailedTryAgain` | 3 | Download failure retry count (passed to `ResourceDownloaderOptions` at downloader creation) |

Other settings:

- `UpdatableWhilePlaying`: Download while playing
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`: Minimum/maximum interval for unused asset recycling (default 60s / 300s)
- `UseSystemUnloadUnusedAssets`: Whether to call `ResourceService.UnloadUnusedAssets()` during the system unload cycle

### Hot Update Process API

```csharp
// Initialize a specified resource package (needInitManifest: true also requests and updates the manifest, for standalone OtherPackage scenarios)
await ResourceService.InitializePackageAsync("DefaultPackage", needInitManifest: true);

// Boolean thin shell: may write remote URLs, does not update the manifest, folds to success/failure;
// dedup/idempotency semantics identical to InitializePackageAsync.
// Non-empty hostServerURL/fallbackHostServerURL are written to
// HostServerURL/FallbackHostServerURL; in HostPlay/WebPlay modes with both empty, GameException is
// thrown (fail-fast).
bool succeed = await ResourceService.TryInitializePackageAsync();
bool succeed2 = await ResourceService.TryInitializePackageAsync("OtherPackage", "https://cdn.example.com/res");

// Online mode: request remote version -> update manifest -> create downloader -> download
var op = await ResourceService.RequestPackageVersion();
ResourceService.PackageVersion = op.PackageVersion;
await ResourceService.LoadPackageManifestAsync(ResourceService.PackageVersion);
var downloader = ResourceService.CreateResourceDownloader();   // then poll the downloader

// Download size query: pending bytes for a location (for remaining-download UI; throws GameException on invalid location/package)
long downloadBytes = ResourceService.GetDownloadSize("Assets/AssetRaw/UI/logo.png");
bool needRemote = ResourceService.IsNeedDownloadFromRemote("Assets/AssetRaw/UI/logo.png");

// Remote address and cache cleanup
ResourceService.SetRemoteServicesUrl("https://cdn.example.com/res", "https://backup.example.com/res");
ResourceService.StartClearCache(EResourceClearMode.ClearUnusedBundleFiles);            // clear unused cache files
ResourceService.ClearAllBundleFiles();             // clear sandbox path
```

### Asset Query and Handles

```csharp
EResourceHasAssetResult result = ResourceService.HasAsset("Assets/AssetRaw/UI/icon.png");
// NotExist: invalid location or missing from manifest; AssetOnline: exists but needs remote download; AssetOnDisk: available
bool valid = ResourceService.IsLocationValid("Assets/AssetRaw/UI/icon.png");
ResourceAssetInfoEntry[] infos = ResourceService.GetAssetInfos("Preload");   // batch get by tag

// When fine-grained control over handle lifecycle is needed, use the lease API (not auto-managed by ResourceOwner)
using var lease = ResourceService.LoadLeaseAsync<GameObject>("path").GetAwaiter().GetResult();
// ... use lease.Asset; released automatically at the end of the using scope
```

### Configuration Self-Check

Settings follow a **report, never rewrite** policy: values that are individually legal but inconsistent with each other each raise one Warning during `ResourceService.OnInit`; at build time `ResourceSettingsBuildValidator` re-runs **the exact same rules**, and it also only warns by default — set `MOIRAI_RESOURCE_SETTINGS_STRICT=1` to fail the build (this package is consumed by others, so stopping somebody else's build over one setting is a ticket, not a reminder).

Deliberately no runtime clamping: clamping rewrites a configuration mistake into a value that looks fine, which is the same reasoning behind `PlayMode` normalising only its return value and never writing back to the asset.

| Setting | Triggered when | What goes wrong |
|---|---|---|
| `ExpireProcessCountWhenUnloading` | below `ExpireProcessCountPerFrame` | the frame drive keeps the larger value, so the unload budget never applies |
| `ExpireProcessCountPerFrame` | ≤ 0 | the expiry wheels advance only on a positive budget, so idle/keep-alive records never expire |
| `DestroySweepBudget` | ≤ 0 | the destroyed-target sweep never runs, so leases left by a truncated `OnDestroy` are never reclaimed |
| `IdleAssetExpireTime` | > 255 | expiry ticks land in a 256-bucket wheel one tick per second; a value past a full lap is skipped until the wheel wraps, delaying release by up to a whole lap |
| `MaxUnloadUnusedAssetsInterval` | ≤ 0 | the trigger compares elapsed time against it, so a non-positive value unloads every frame |
| `MinUnloadUnusedAssetsInterval` | above the max interval | preordered unloads are already fired by the periodic trigger, so the minimum decides nothing |
| `MinGCCollectInterval` | < 0 | throttling never applies and every collect request runs `GC.Collect` |

## Notes

- **Addressables backend (experimental):** `AddressableHandler` shares the same record kernel as `YooAssetHandler` (`ResourceRecordStore`); the async lease / binding / prefab instantiation / atlas sub-sprite / scene loading / cache maintenance and low-memory release families are all at parity. When `com.unity.addressables` is not installed the whole layer drops out via the asmdef `versionDefines` macro `ADDRESSABLES_INSTALLED` plus file-level `#if` — it is deliberately **not** split into its own assembly, because the kernel types are `internal` to `Moirai.Atropos` and a satellite assembly would only buy a line of `InternalsVisibleTo`.
- **Addressables sync and download families throw:** `LoadLease<T>` / `AcquireDirect` / `AcquireBinding` / `AcquirePrefabSourceLease` / the synchronous `LoadGameObject` all raise `GameException` — Addressables has no synchronous load API, and returning `null` would just move the failure to the next null dereference. Same for `RequestPackageVersion` / `LoadPackageManifestAsync` / `CreateResourceDownloader` / `GetDownloadSize`: its update flow is a two-step Check→Update, and `DownloadStatus` has no counterpart for `TotalDownloadCount` or `FailedFiles`.
- **Addressables degraded queries:** `IsNeedDownloadFromRemote` always returns `false`, `GetPackageVersion` always returns an empty string, `GetAssetInfo` always returns `default`, and the tag-based `GetAssetInfos` always returns an empty array (the matching capability is async-only, or `IResourceLocation` simply carries no tags or size). `HasAsset` reports `AssetOnDisk` for any locatable address and cannot tell "cached" from "needs remote download" (`AssetOnline` never occurs); atlas sub-sprites only support addresses that resolve to a `SpriteAtlas`. The non-forced `UnloadUnusedAssets()` does nothing on this backend (on YooAsset that overload drives bundle unload operations, which have no counterpart), while the forced overload and the low-memory callback go through record release. Use `YooAssetHandler` in production.

- **Lease API:** `ResourceAssetLease<T>` is a `struct` — always `Dispose` it (use `using` statement). After Dispose, `IsValid` returns `false` and `Asset` is `null`.
- **Binding API:** `SetSprite`/`SetMaterial` extension methods auto-add a `ResourceOwner` to the target's GameObject if not present. All bindings are released when the GameObject is destroyed.
- **Prefab instantiation:** `LoadGameObject` / `LoadGameObjectAsync` return an instantiated copy whose prefab source lease is held by the instance's `ResourceOwner`; `Destroy`-ing the instance releases the lease. Do not destroy the source prefab object itself, and do not treat the instance as a shared resource you own.
- **Async cancellation:** `LoadLeaseAsync<T>` returns an invalid lease (`IsValid` is `false`, `Asset` is `null`) and releases its internal handle when cancelled (via `cancellationToken`); the caller must check for it. `LoadGameObjectAsync` likewise returns `null` when cancelled.
- The WebGL platform does not support `ForceUnloadAllAssets`; calling it will only print a warning.
- The build-side encryption method (`FileStreamEncryptor`, etc.) must match the runtime decryption side. The XOR key for `BundleStream` is a fixed constant (`KEY = 64`), intended only to prevent direct reading.
- `GetAssetInfo` caches results for the default package in a dictionary. After switching manifests (hot update completed), call `UnloadUnusedAssets()` first to get the latest information (this clears the cache).
- On low memory, the system callback `GameApp.OnLowMemory` triggers `ForceUnloadUnusedAssets(true)`, followed by `Resources.UnloadUnusedAssets` and `GC.Collect`.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Procedure](Procedure.md) · [Save](Save.md) · [ObjectPool](ObjectPool.md)
