# ObjectPool Service

> Generic pool + GameObject specialization, shared kernel + dual facades in a single module.
> The shared kernel provides paged slot storage, open-addressing hashes and a min-heap maintenance scheduler; two facades serve arbitrary CLR objects and Unity GameObjects respectively.

The service is split into two independent facades; choose by pooled object type:

| Facade | Pooled Object | Key | Typical Usage |
|--------|--------------|-----|---------------|
| `ObjectPoolService` | Any `ObjectBase` derived object (data packets, connections, commands…) | `Type + pool name` | Pure C# object reuse |
| `GameObjectPoolService` | Unity GameObject (Prefab instances) | Asset location (PoolCatalog rules) | Bullets, VFX, UI popups |

> ⚠️ **Both services are opt-in**: they are NOT in the `ProcedureService` dependency chain and are not registered by default.
> When unregistered, all static facade calls silently return default values and maintenance (expiry / over-capacity / low-memory shrink) never runs.
> Enable via `GameServices.RegisterService(EServiceScopeKind.App, new ObjectPoolService())`
> (GameObjectPoolService depends on ResourceService, which is pulled up automatically by the dependency chain).

## Architecture

```
Runtime/Modules/ObjectPool/
├── Kernel/                 # Shared kernel (internal)
│   ├── PoolSlotStorage<T>      # Paged slot storage (128 slots/page + page-level free stacks)
│   ├── PoolMaintenanceScheduler # Shared min-heap maintenance scheduler (1ms frame budget)
│   ├── OpenHashMap<K> / ReferenceOpenHashMap / StringOpenHashMap  # Open-addressing zero-alloc hashes
│   └── SlotArrayPool<T>        # Bucketed array pool by length
├── ObjectPoolService.cs    # Generic pool static facade ([HandlerHost])
├── ObjectBase.cs           # Pooled object base (OnSpawn/OnDespawn/Release contract)
├── IObjectPool.cs          # Generic pool contract
└── GameObject/             # GameObject specialization
    ├── GameObjectPoolService.cs    # GO pool static facade ([HandlerHost] + ServiceDependency(Resource))
    ├── RuntimeGameObjectPool.cs    # Per-pool runtime (generation handle + policy trimming)
    ├── PoolCatalog.cs / PoolPolicy.cs / Data/  # Data-driven config and policies
    └── IPrefabLoader.cs            # Prefab loading abstraction (ResourceAssetLease-based)
```

Both pools share the same maintenance semantics: each Tick processes only due pools (min-heap, O(log n)) within a 1ms per-frame budget;
on low memory each Handler subscribes to `Application.lowMemory` and shrinks fully.

## Core Types

Namespace: `Moirai.Atropos.ObjectPool`

### Generic Pool

| Class/Interface | Description |
|-----------------|-------------|
| `ObjectPoolService` | Static facade: `GetOrCreatePool<T>` / `GetObjectPool<T>` / `HasObjectPool<T>` / `DestroyObjectPool<T>` / `Release` / `ReleaseAllUnused` |
| `ObjectPoolCreateOptions` | Creation options: `Name` / `AllowMultiSpawn` / `AutoReleaseInterval` / `Capacity` / `ExpireTime` / `Priority` |
| `IObjectPool<T>` | Per-pool contract: `Register` / `Spawn` / `Despawn` / `DespawnTarget` / `Release(count)` / `ReleaseAllUnused` |
| `ObjectBase` | Pooled object base: `OnSpawn` / `OnDespawn` / `Release(bool)` / `Locked` / `CustomCanReleaseFlag` |
| `ObjectPoolBase` | Pool metadata base: `FullName` / `ObjectType` / `Count` / `Capacity` / `ExpireTime` |
| `ObjectInfo` | Object-level debug snapshot (name / ref count / locked / releasable / last use time) |

### GameObject Pool

| Class/Interface | Description |
|-----------------|-------------|
| `GameObjectPoolService` | Static facade: `Spawn` / `SpawnAsync` / `TrySpawn` / `Despawn` / `WarmupAsync` / `LoadPrefab(Async)` / `Flush` / `FlushGroup` / `FlushAll` / `LoadCatalog` |
| `RuntimeGameObjectPool` | Per-pool runtime: paged slots + intrusive inactive list + generation handles |
| `GameObjectPoolHandle` | MonoBehaviour attached to pooled instances; generation validation prevents use-after-despawn |
| `IGameObjectPoolable` | Pooled component interface: `OnSpawn(in GameObjectPoolSpawnContext)` / `OnDespawn` / `OnPooledDestroy` |
| `EPoolPolicy` | Recycle policy: `Fixed` (trim on excess) / `Burst` (trim after idle timeout) / `Sticky` (no proactive trim) |
| `PoolEntry` / `PoolConfigScriptableObject` | Serializable config entries and config asset (supports Glob: `*`, `**`, `?`) |
| `PoolCompiledCatalog` | Compiled rule catalog: exact + Glob matching |
| `IPrefabLoader` | Prefab loading abstraction; default `ResourcePrefabLoader` uses `ResourceService.LoadLease` leases for ref-counting |

### Debugging

| Class/Interface | Description |
|-----------------|-------------|
| `GameObjectPoolSummarySnapshot` / `GameObjectPoolSnapshot` | GO pool statistics snapshots (spawn/despawn/hit/miss/expand/destroy/peak + instance list) |
| `GetAllObjectPools(bool sort, ObjectPoolBase[])` / `GetAllObjectInfos(ObjectInfo[])` | Generic pool debug export |
| Debugger windows | `Profiler/Object Pool` (generic), `Profiler/GameObject Pool` (GO pool) |

## Quick Start

### 1. Generic Pool (Pure C# Objects)

```csharp
// Define a pooled object: derive ObjectBase, implement Release, reset state in Clear
public sealed class BuffData : ObjectBase
{
    public Buff Owner { get; private set; }

    public void Init(Buff owner)
    {
        Initialize(owner);          // target is the identity & lookup key
    }

    protected internal override void Release(bool isShutdown)
    {
        // permanently removed: release underlying resources
    }

    public override void Clear()
    {
        Owner = null;               // reset before returning to MemoryPool
        base.Clear();
    }
}

// Get or create a pool (key = typeof(BuffData) + optional pool name)
IObjectPool<BuffData> pool = ObjectPoolService.GetOrCreatePool<BuffData>(
    new ObjectPoolCreateOptions(capacity: 256, expireTime: 30f));

// Spawn / despawn
BuffData buff = pool.Spawn();
pool.Despawn(buff);

// Reference-counted mode: one object can be spawned by multiple parties
var sharedPool = ObjectPoolService.GetOrCreatePool<SharedFx>(
    new ObjectPoolCreateOptions(allowMultiSpawn: true));
SharedFx fx = sharedPool.Spawn();   // SpawnCount++
sharedPool.Despawn(fx);             // SpawnCount--; reusable again when zero
```

### 2. GameObject Pool

Configure a `PoolConfigScriptableObject` (Create > Moirai > PoolConfig):

```csharp
new PoolEntry
{
    entryName = "Bullet",
    group = "Combat",
    assetPath = "Assets/Bundles/Prefabs/Bullet",   // Glob also supported: Assets/Bundles/UI/*
    policy = EPoolPolicy.Fixed,
    minIdle = 10,
    softCapacity = 50,
    hardCapacity = 100,
    idleSeconds = 15f,
    unloadPrefab = true,
    priority = 10
};
```

> Config can be provided via `GameObjectPoolServiceSettings` (assign the PoolConfig asset in the Inspector; auto-loaded on service init),
> or at runtime via `GameObjectPoolService.LoadCatalog(config)` / `LoadCatalog(location)` for hot swap (rebuilds all pools).

```csharp
// Synchronous spawn (prefab must be loaded)
GameObject bullet = GameObjectPoolService.Spawn("Assets/Bundles/Prefabs/Bullet", parent);

// Async spawn (auto loads prefab, deduplicated)
GameObject popup = await GameObjectPoolService.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, cancellationToken);

// Spawn and fetch a component directly
var renderer = await GameObjectPoolService.SpawnAsync<MeshRenderer>("Assets/Bundles/Props/Rock", parent);

// Despawn (return to pool)
GameObjectPoolService.Despawn(bullet);

// Or via handle
if (bullet.TryGetComponent(out GameObjectPoolHandle handle))
{
    GameObjectPoolService.Despawn(handle);
}
```

### 3. Poolable Components & Warmup

```csharp
public class BulletController : MonoBehaviour, IGameObjectPoolable
{
    public void OnSpawn(in GameObjectPoolSpawnContext context)
    {
        // taken from the pool — context.Location/Group/Parent/SpawnFrame
    }

    public void OnDespawn()
    {
        // returned to the pool
    }

    public void OnPooledDestroy()
    {
        // instance permanently destroyed (trim, low-memory shrink, pool shutdown)
    }
}

// Pre-create 20 instances with per-frame budget (no frame spikes)
await GameObjectPoolService.WarmupAsync("Assets/Bundles/Prefabs/Bullet", 20, cancellationToken);
```

## Advanced

### GameObject Pool Policies

| Policy | Behavior | Use Case |
|--------|----------|----------|
| `Fixed` | Exceeds retain target → trim immediately | Bullets, particles (strict limits) |
| `Burst` | Idle beyond idleSeconds → trim | UI windows, common props |
| `Sticky` | No proactive trim; manual Flush / low-memory only | High-frequency reuse |

### Generic Pool Capacity & Expiry

| Option | Behavior |
|--------|----------|
| `Capacity` | On register overflow, tries releasing releasable idle objects first; refuses the object if still full |
| `ExpireTime` | Unused objects past idle duration → released in budgeted batches (8 per wake) |
| `AutoReleaseInterval` | Sustained over-capacity for the interval marks the excess for release |
| `Locked` / `CustomCanReleaseFlag` | Per-object veto against automatic release |

### Flush Operations (GO Pool)

```csharp
GameObjectPoolService.Flush("Assets/Bundles/Prefabs/Bullet");  // single pool
GameObjectPoolService.FlushGroup("Combat");                    // by group
GameObjectPoolService.FlushAll();                               // all (same as low-memory response)
```

### Debug Inspection

```csharp
// GO pool
GameObjectPoolSummarySnapshot summary = GameObjectPoolService.GetDebugSummary();
GameObjectPoolSnapshot[] snapshots = new GameObjectPoolSnapshot[64];
int count = GameObjectPoolService.GetDebugSnapshots(snapshots);
for (int i = 0; i < count; i++)
{
    MemoryPool.Release(snapshots[i]);   // return snapshot to MemoryPool
}

// Generic pool
ObjectPoolBase[] pools = new ObjectPoolBase[64];
int poolCount = ObjectPoolService.GetAllObjectPools(true, pools);   // true = sort by priority
```

Debugger windows: `Profiler/Object Pool` (generic), `Profiler/GameObject Pool` (GO pool, with hit/miss/peak metrics).

## Migration From Old API

| Old (≤ before 126df59) | New | Notes |
|------------------------|-----|-------|
| `ObjectPoolService` (GO pool semantics) | `GameObjectPoolService` | Facade renamed; GO pool APIs unchanged |
| `GameApp.ObjectPool` | `GameObjectPoolService` static facade | No longer accessed via GameApp |
| `IObjectPoolable` / `PoolSpawnContext` / `ObjectPoolHandle` | `IGameObjectPoolable` / `GameObjectPoolSpawnContext` / `GameObjectPoolHandle` | Type renames |
| `IObjectPoolable.OnPooledDestroy` etc. | Same names | Component code only changes the interface name |
| `ObjectPoolSetting` component | `GameObjectPoolServiceSettings` (PoolConfig field) | Config single-sourced into the Settings asset |
| — | `ObjectPoolService` | New generic pool facade (port of the AlicizaX reference capability) |

## Notes

- **Opt-in registration**: neither service is in the dependency chain by default; when unregistered, facade calls silently no-op and maintenance never runs (see top).
- Generic pool objects are created externally and `Register`ed; objects created via `MemoryPool.Acquire` are recycled by the pool, externally `new`ed ones go to GC on release.
- GO pool spawns require rules registered via `PoolConfigScriptableObject`; unregistered locations log an error and return null.
- `Spawn()` (sync) returns null when the prefab is not loaded; use `SpawnAsync()` for the first load.
- `Despawn()` is safe on non-pooled GameObjects — falls back to `Destroy` (immediate in EditMode) with a warning.
- `GameObjectPoolHandle` is added automatically on creation; external `Destroy` of a pooled instance triggers generation-validated cleanup with a warning.
- Maintenance is driven by `GameServices.Tick` (min-heap due wakeups, 1ms per-frame budget) — no standalone MonoBehaviour Update loop.
- Low memory: both pool Handlers subscribe to `Application.lowMemory` and shrink fully; `GameApp.OnLowMemory` only drives the resource layer unload.

---
[« Back to main README](../../README.md)
