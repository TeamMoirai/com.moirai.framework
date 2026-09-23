# ObjectPool Service

> Generic pool + GameObject specialization, shared kernel + dual facades in a single module.
> The shared kernel provides paged slot storage, open-addressing hashes and a min-heap maintenance scheduler; two facades serve arbitrary CLR objects and Unity GameObjects respectively.

The service is split into two independent facades; choose by pooled object type:

| Facade | Pooled Object | Key | Typical Usage |
|--------|--------------|-----|---------------|
| `ObjectPoolService` | Any `ObjectBase` derived object (data packets, connections, commands…) | `Type + pool name` | Pure C# object reuse |
| `GameObjectPoolService` | Unity GameObject (Prefab instances) | Asset location / external Prefab reference | Bullets, VFX, UI popups |

> Both services are built-in App services: marked `[AutoRegisterService]` and auto-registered by the composition root via the generated list
> (`GameObjectPoolService` depends on `ResourceService` via `[ServiceDependency]`; topological initialization guarantees the ordering).

## Architecture

```
Runtime/Services/ObjectPool/
├── Kernel/                 # Shared kernel (internal)
│   ├── PoolSlotStorage<T>      # Paged slot storage (128 slots/page + page-level free stacks)
│   ├── PoolMaintenanceScheduler # Shared min-heap maintenance scheduler (1ms frame budget)
│   ├── OpenHashMap<K> / ReferenceOpenHashMap / StringOpenHashMap  # Open-addressing zero-alloc hashes
│   └── SlotArrayPool<T>        # Bucketed array pool by length
├── ObjectPool/             # Generic pool
│   ├── ObjectPoolService.cs    # Generic pool static facade ([HandlerHost])
│   ├── ObjectBase.cs           # Pooled object base (OnSpawn/OnDespawn/Release contract)
│   └── IObjectPool.cs          # Generic pool contract
└── GameObject/             # GameObject specialization
    ├── GameObjectPoolService.cs    # GO pool static facade ([HandlerHost] + ServiceDependency(Resource))
    ├── RuntimeGameObjectPool.cs    # Per-pool runtime (generation handle + policy; Location / External Prefab sources)
    ├── DefaultPoolRules.cs         # Default rules for unregistered locations / external prefabs
    ├── PoolCatalog.cs / PoolPolicy.cs / Data/  # Data-driven config and policies
    ├── IPrefabLoader.cs            # Prefab loading abstraction (ResourceAssetLease-based)
    └── Pooled/                     # IDisposable thin wrappers (PooledGameObject / PooledComponent)
```

Both pools share the same maintenance semantics: each Tick processes only due pools (min-heap, O(log n)) within a 1ms per-frame budget; maintenance runs in two phases — collect, then dispatch — so **each pool is maintained at most once per frame**, items re-scheduled during a wake-up wait for the next Tick, and any residue left by the budget stays in the work set and is dispatched next Tick rather than dropped;
on low memory each Handler subscribes to `Application.lowMemory` and shrinks fully.

## Core Types

Namespace: `Moirai.Atropos.ObjectPool`

### Generic Pool

| Class/Interface | Description |
|-----------------|-------------|
| `ObjectPoolService` | Static facade: `GetOrCreatePool<T>` / `GetObjectPool<T>` / `HasObjectPool<T>` / `DestroyObjectPool<T>` / `TrySpawn<T>` / `Contains<T>` / `Release` / `ReleaseAllUnused` / `FlushAll` |
| `ObjectPoolCreateOptions` | Creation options: `Name` / `AllowMultiSpawn` / `AutoReleaseInterval` / `Capacity` / `ExpireTime` / `Priority` |
| `IObjectPool<T>` | Per-pool contract: `Register` / `Spawn` / `Despawn` / `DespawnTarget` / `Release(count)` / `ReleaseAllUnused` |
| `ObjectBase` | Pooled object base: `OnSpawn` / `OnDespawn` / `Release(bool)` / `Locked` / `CustomCanReleaseFlag` |
| `ObjectPoolBase` | Pool metadata base: `FullName` / `ObjectType` / `Count` / `Capacity` / `ExpireTime` |
| `ObjectInfo` | Object-level debug snapshot (name / ref count / locked / releasable / last use time) |

### GameObject Pool

| Class/Interface | Description |
|-----------------|-------------|
| `GameObjectPoolSource` | Unified source key: location or external prefab; implicit from `string`/`GameObject`; `Group` applies only when a prefab pool is first created |
| `GameObjectPoolService` | Static facade (single entry): `Spawn` / `SpawnAsync` / `SpawnPooled` / `SpawnPooledAsync` / `Despawn` / `WarmupAsync` / `LoadPrefab(Async)` / `Flush` / `FlushGroup` / `FlushAll` |
| `PooledGameObject` | Pure C# lease (not MonoBehaviour): owner/slot/generation; `Spawn` / `SpawnAsync` / `Wrap` / `Dispose` / `Get(OrAdd)UserData` / `SetUserData` / `IsValid`; only Active instances can be wrapped |
| `Pooled<TComponent>` | Generic component lease (return type of service `SpawnPooled<T>`) |
| `PooledComponent<T,TComponent>` | CRTP component lease base for custom subclasses |
| `RuntimeGameObjectPool` | Per-pool runtime: paged Slot (UserData) + intrusive inactive list + generation; Location / External Prefab |
| `PooledInstanceRegistry` | Zero-alloc instance → (pool,slot) reverse map; generation lives on Slot |
| `IGameObjectPoolable` | Pooled component interface: `OnSpawn(in GameObjectPoolSpawnContext)` / `OnDespawn` / `OnPooledDestroy` |
| `EPoolPolicy` | Recycle policy: `Fixed` (trim on excess) / `Burst` (trim after idle timeout) / `Sticky` (no proactive trim) |
| `PoolEntry` | Serializable config entry (listed in the default handler's pool config field; supports Glob: `*`, `**`, `?`) |
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

Pool entries live in the default handler's pool config list (`Tools > Framework Settings > [服务]游戏对象池设置`):

```csharp
new PoolEntry
{
    entryName = "Bullet",
    group = "Combat",
    pattern = "Assets/Bundles/Prefabs/Bullet",   // Glob also supported: Assets/Bundles/UI/*
    policy = EPoolPolicy.Fixed,
    minIdle = 10,
    softCapacity = 50,
    hardCapacity = 100,
    idleSeconds = 15f,
    unloadPrefab = true,
    priority = 10
};
```

> Config is serialized on the pool handler chosen in `GameObjectPoolServiceSettings` (its pool config list),
> and the handler compiles it into a `PoolCompiledCatalog` and rebuilds all pools on init.

```csharp
// string / GameObject implicitly convert to GameObjectPoolSource
GameObject bullet = GameObjectPoolService.Spawn("Assets/Bundles/Prefabs/Bullet", parent);
GameObject fx = GameObjectPoolService.Spawn(vfxPrefab, parent);
GameObject popup = await GameObjectPoolService.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, cancellationToken);
GameObject posed = GameObjectPoolService.Spawn(vfxPrefab, position, rotation, parent, useLocalPosition: false);

// Raw instance — manual despawn
GameObjectPoolService.Despawn(bullet);

// Pooled lease — Dispose auto-returns (using recommended)
using (PooledGameObject lease = GameObjectPoolService.SpawnPooled("Assets/Bundles/Prefabs/Bullet", parent))
{
    // lease.GameObject / lease.Transform / lease.IsValid
}

using (Pooled<ParticleSystem> ps = GameObjectPoolService.SpawnPooled<ParticleSystem>(vfxPrefab, parent))
{
    ps.Component.Play();
}

// Equivalent: spawn from the lease type itself
PooledGameObject direct = PooledGameObject.Spawn(vfxPrefab, parent);
direct.Dispose();

// Async — same signature for location and prefab sources
PooledGameObject pooled = await PooledGameObject.SpawnAsync(vfxPrefab, parent, ct);
pooled.Dispose();   // == GameObjectPoolService.Despawn(pooled)
```

Full lease usage (UserData, delayed release, custom subclasses): see [Leases: PooledGameObject / PooledComponent](#leases-pooledgameobject--pooledcomponent).

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

### Leases: PooledGameObject / PooledComponent

A lease is a pure C# object (not a MonoBehaviour) holding `(pool, slot, generation)` identity.
`Dispose` returns the instance after a generation check; once the slot is reused, the old lease
is guaranteed stale (`IsValid == false`, and `Dispose` becomes a silent no-op).

#### Acquire & release

```csharp
// ① Service facade (SpawnPooled / SpawnPooledAsync / SpawnPooled<T>)
using (PooledGameObject lease = GameObjectPoolService.SpawnPooled(source, parent))
{
    GameObject go = lease.GameObject;
    Transform tf = lease.Transform;
    bool alive = lease.IsValid;     // generation check
}                                   // auto-Dispose at end of scope

// ② Spawn from the lease type (equivalent to ①)
using (PooledGameObject lease = PooledGameObject.Spawn(source, parent)) { /* ... */ }
using (Pooled<ParticleSystem> ps = Pooled<ParticleSystem>.Spawn(vfxPrefab, parent))
{
    ps.Component.Play();            // component comes from Slot.UserData resident cache
}

// ③ Wrap an already-spawned Active instance (does not spawn again)
GameObject raw = GameObjectPoolService.Spawn(source, parent);
PooledGameObject wrapped = PooledGameObject.Wrap(raw);
if (wrapped != null) wrapped.Dispose();   // Wrap returns null if not pooled / not Active

// ④ Manual Dispose — fine on linear paths; use using / try-finally when
//    exceptions, early returns, or await cancellation are possible
var lease2 = PooledGameObject.Spawn(source, parent);
try { /* ... */ }
finally { lease2.Dispose(); }             // Dispose is idempotent
```

> **Performance**: wrappers are pooled via `Internal_ObjectPool` (steady-state 0-Alloc).
> `Pooled<TComponent>.Component` is resolved once via `GetOrAddComponent` and then resident in
> `Slot.UserData`; re-spawning the same GameObject slot costs zero `GetComponent` lookups.
> `UserData` is a single-consumer slot: alien occupancy degrades the component cache to
> non-resident (no overwrite; dev warning).

#### UserData (data attached across reuse)

```csharp
using (var lease = Pooled<BulletView>.Spawn(source, parent))
{
    // Survives Spawn/Despawn on the same slot until the slot is destroyed
    BulletRuntimeState state = lease.GetOrAddUserData<BulletRuntimeState>();
    state.Reset(damage);

    BulletRuntimeState existing = lease.GetUserData<BulletRuntimeState>(); // may be null
    lease.SetUserData(state);   // overwrite; avoid occupying ComponentCache's type
}
```

#### Custom subclasses (CRTP + delayed release)

`Pooled<TComponent>` covers the no-custom-logic case. For custom `Init`, component resolution,
or timed release, derive from `PooledComponent<T, TComponent>`:

```csharp
public sealed class BulletLease : PooledComponent<BulletLease, BulletView>
{
    protected override void Init()
    {
        base.Init();                // runs ResolveComponent, builds the component cache
        Component.Reset();
    }

    protected override void OnDispose()
    {
        // pre-return cleanup (do not touch a stale GameObject here)
    }

    /// <summary>Auto-return 2s after hit (scheduler-driven, frame-end safe).</summary>
    public void AutoRelease() => Destroy(2f);   // Destroy is protected
}

// Usage matches Pooled<T>
using (var bullet = BulletLease.Spawn("Assets/Prefabs/Bullet", firePoint))
{
    bullet.Component.Fire();
    bullet.AutoRelease();           // delayed 2s return; early Dispose cancels the scheduled handle
}
```

When relying on `AutoRelease` without `using`, do not `Dispose` the lease early — that cancels the delayed handle.

When to use which entry:

| Need | Entry |
|------|-------|
| Raw instance, external lifecycle | `GameObjectPoolService.Spawn` / `Despawn` |
| Scope-based auto-return | `SpawnPooled` / `PooledGameObject.Spawn` |
| Component + scope return + component cache | `SpawnPooled<T>` / `Pooled<T>.Spawn` |
| Custom Init / resolution / delayed release | Derive `PooledComponent<T, TComponent>` |
| `TrySpawn` / warmup / Flush | `GameObjectPoolService` only |

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

## Notes

- **Opt-in registration**: neither service is in the dependency chain by default; the first facade access auto-registers it via the lazy path (`Tick`-driven maintenance takes effect immediately), or register explicitly via `RegisterService` (stricter dependency validation, see top).
- **Main Thread Only**: the entire ObjectPool module (generic pool, GameObject pool, leases, Kernel) is main-thread only by design; no locking.
- Generic pool objects are created externally and `Register`ed; objects created via `MemoryPool.Acquire` are recycled by the pool, externally `new`ed ones go to GC on release.
- **Unregistered locations**: auto-create a pool with the default rule (Burst / soft 8 / hard 64; one warning in Editor/DevBuild). Prefer registering production addresses in PoolConfig for tuning.
- **External prefab pools**: identity-mapped by reference (zero string alloc on the hot path); the pool does **not** load/unload that prefab (`unloadPrefab` is gated away by the source). PoolConfig supports `Prefab:` prefix patterns to customize capacity/group/policy for external prefabs (e.g. `pattern = "Prefab:Bullet*"` — the synthetic key embeds an instanceID, so a **wildcard is required**; literals cannot match; unmatched prefabs fall back to the default rule soft 8 / hard 64; a catalog rule takes precedence over the `group` argument of `FromPrefab`).
- **Pose**: both sources reset local TRS from the prefab on reuse (aligned with `Object.Instantiate(prefab, parent)`). `OnSpawn` observes the reset pose.
- **Lease generation**: bumps on every activate; stale leases fail `TryRelease`/`IsValid`. `Wrap` only binds Active instances.
- **Despawn branches**: unregistered → `Destroy` (foreign); registered but not Active → safe no-op; Active → return to pool.
- `Spawn()` (sync) returns null when a location-based prefab is not loaded; use `SpawnAsync()` for the first load.
- **UserData single consumer**: alien occupancy degrades the component cache to non-resident (no overwrite; dev warning).
- `default(GameObjectPoolSource)` is an invalid source; do not write `Spawn(null)` (ambiguous implicits — compile error).
- **Zombie-slot self-heal**: hitting the hard capacity sweeps externally destroyed slots before retrying the allocation; pools with live instances but no due maintenance (Sticky / all-active) run a fallback sweep every 30s with a warning, so reclaiming externally destroyed instances is bounded and no longer depends on Flush / low memory.
- Maintenance is driven by `GameServices.Tick` (min-heap due wakeups, 1ms per-frame budget). Sticky pools lazily reclaim externally destroyed slots on the next Spawn.
- Low memory: both pool Handlers subscribe to `Application.lowMemory` and shrink fully; the resource layer unload is driven by `ResourceService`'s own `OnLowMemory`.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [MemoryPool](MemoryPool.md) · [Resource](Resource.md)
