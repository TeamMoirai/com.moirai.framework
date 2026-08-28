# ObjectPool Service

> Zero-GC GameObject pool with SoA paged slots, policy-driven recycling, min-heap maintenance scheduling, and data-driven catalog configuration.

The ObjectPool service is accessed via `GameApp.ObjectPool` and manages pooled GameObject instances by resource location. It uses a compiled `PoolCatalog` (ScriptableObject-driven) to resolve pool rules, supports three recycling policies (Fixed/Burst/Sticky), and provides async prefab loading with reference counting.

## Core Features

- **Zero-GC hot path**: SoA paged Slot storage (128 slots/page) + intrusive doubly-linked list — no Dictionary allocations during Spawn/Despawn
- **Policy-driven recycling**: `PoolPolicy.Fixed` (strict capacity), `PoolPolicy.Burst` (idle timeout), `PoolPolicy.Sticky` (no auto-trim)
- **Min-heap maintenance scheduling**: Only processes pools whose maintenance is due — O(log n) per tick, not O(n) full scan
- **Generation-based handle validation**: `ObjectPoolHandle` (MonoBehaviour) binds each instance with slot index + generation, preventing use-after-despawn
- **Async prefab loading**: `IPrefabLoader` abstraction with `ResourcePrefabLoader` (YooAsset-backed), reference-counted prefab lifecycle
- **Frame-budget warmup**: `WarmupAsync` creates instances in batches with per-frame time budget to avoid frame spikes
- **Low memory response**: `Application.lowMemory` callback triggers `FlushAll()` across all pools
- **Data-driven configuration**: `PoolConfigScriptableObject` with Glob pattern matching (`*`, `**`, `?`) for resource address rules
- **Full observability**: Per-pool snapshots with hit/miss/expand/destroy/peak metrics + instance-level inspection

## Core Types

Namespace: `Moirai.Atropos.ObjectPool`

| Class/Interface | Description |
|---------|------|
| `IObjectPoolService` | Service interface: `Spawn` / `SpawnAsync` / `Despawn` / `Flush` / `FlushGroup` / `FlushAll` / `WarmupAsync` / `LoadCatalog` |
| `ObjectPoolService` | Default implementation (`internal sealed`), `Priority = 6`, implements `IServiceTickable` for min-heap maintenance |
| `RuntimeObjectPool` | Per-location pool: SoA paged slots + intrusive linked list + generation handles |
| `ObjectPoolHandle` | MonoBehaviour attached to pooled instances; provides `TryRelease()` for safe despawn |
| `IObjectPoolable` | Interface for components on pooled prefabs: `OnSpawn(in PoolSpawnContext)` / `OnDespawn()` / `OnPooledDestroy()` |
| `PoolPolicy` | Enum: `Fixed = 0`, `Burst = 1`, `Sticky = 2` |
| `PoolEntry` | Serializable config entry: `assetPath`, `policy`, `minIdle`, `softCapacity`, `hardCapacity`, `idleSeconds`, `unloadPrefab` |
| `PoolConfigScriptableObject` | ScriptableObject holding `List<PoolEntry>`; loaded via `LoadCatalog()` |
| `PoolCompiledCatalog` | Compiled rule catalog with exact + Glob pattern matching |
| `PoolGlobMatcher` | Zero-alloc Glob pattern matcher (`*`, `**`, `?`) |
| `IPrefabLoader` | Prefab loading abstraction: `LoadPrefab` / `LoadPrefabAsync` / `UnloadPrefab` |
| `ResourcePrefabLoader` | Default `IPrefabLoader` impl using `GameApp.Resource` (YooAsset) with ref counting |
| `ObjectPoolSetting` | MonoBehaviour component for Inspector-configured pool settings + low memory/focus events |
| `ObjectPoolSnapshot` | Debug snapshot: per-pool stats (spawn/despawn/hit/miss/expand/destroy/peak) + instance list |
| `SlotArrayPool<T>` | Internal array pool (bucketed by length) for zero-GC array reuse |
| `StringOpenHashMap` | Internal open-addressing string→int HashMap |

## Quick Start

### 1. Configure pool rules

Create a `PoolConfigScriptableObject` via `Create > Moirai > PoolConfig`:

```csharp
// Or assign in Inspector on ObjectPoolSetting component
var config = ScriptableObject.CreateInstance<PoolConfigScriptableObject>();
config.entries = new List<PoolEntry>
{
    new PoolEntry
    {
        entryName = "Bullets",
        group = "Combat",
        assetPath = "Assets/Bundles/Prefabs/Bullet",
        policy = PoolPolicy.Fixed,
        minIdle = 10,
        softCapacity = 50,
        hardCapacity = 100,
        idleSeconds = 15f,
        unloadPrefab = true,
        priority = 10
    },
    new PoolEntry
    {
        entryName = "UIPopups",
        group = "UI",
        assetPath = "Assets/Bundles/UI/*",  // Glob pattern
        policy = PoolPolicy.Burst,
        minIdle = 2,
        softCapacity = 8,
        hardCapacity = 32,
        idleSeconds = 30f
    }
};
```

### 2. Spawn and despawn

```csharp
// Sync spawn (requires prefab already loaded)
GameObject bullet = GameApp.ObjectPool.Spawn("Assets/Bundles/Prefabs/Bullet", parent);

// Async spawn (loads prefab if needed)
GameObject popup = await GameApp.ObjectPool.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, cancellationToken);

// Get component directly
var renderer = await GameApp.ObjectPool.SpawnAsync<MeshRenderer>("Assets/Bundles/Props/Rock", parent);

// Despawn (returns to pool)
GameApp.ObjectPool.Despawn(bullet);

// Or despawn via handle (attached to the GameObject)
if (bullet.TryGetComponent(out ObjectPoolHandle handle))
{
    GameApp.ObjectPool.Despawn(handle);
}
```

### 3. Warmup

```csharp
// Pre-create 20 instances with frame budget (won't spike)
await GameApp.ObjectPool.WarmupAsync("Assets/Bundles/Prefabs/Bullet", 20, cancellationToken);
```

### 4. Poolable components

Implement `IObjectPoolable` on components attached to pooled prefabs:

```csharp
public class BulletController : MonoBehaviour, IObjectPoolable
{
    public void OnSpawn(in PoolSpawnContext context)
    {
        // Called when taken from pool — reset state, start movement, etc.
        transform.SetPositionAndRotation(context.Parent.position, Quaternion.identity);
    }

    public void OnDespawn()
    {
        // Called when returned to pool — stop movement, clear references, etc.
    }

    public void OnPooledDestroy()
    {
        // Called when the instance is permanently destroyed (capacity trim, shutdown)
    }
}
```

## Advanced Usage

### Flush operations

```csharp
// Flush a single pool
GameApp.ObjectPool.Flush("Assets/Bundles/Prefabs/Bullet");

// Flush all pools in a group
GameApp.ObjectPool.FlushGroup("Combat");

// Flush all pools (equivalent to low-memory response)
GameApp.ObjectPool.FlushAll();
```

### Policy reference

| Policy | Trim behavior | Use case |
|--------|---------------|----------|
| `Fixed` | Exceeds softCapacity → immediate trim | Bullets, particles (strict limit) |
| `Burst` | Idle > idleSeconds → trim | UI windows, general props |
| `Sticky` | Never auto-trim; manual Flush only | Frequently reused objects |

### Debug inspection

```csharp
if (GameApp.ObjectPool is ObjectPoolService service)
{
    var summary = service.GetDebugSummary();
    // summary.PoolCount, summary.ActiveInstanceCount, summary.InactiveInstanceCount, etc.

    ObjectPoolSnapshot[] snapshots = new ObjectPoolSnapshot[64];
    int count = service.GetDebugSnapshots(snapshots);
    for (int i = 0; i < count; i++)
    {
        // snapshots[i].hitCount, snapshots[i].missCount, snapshots[i].peakActive, etc.
        MemoryPool.Release(snapshots[i]); // Return snapshot to MemoryPool
    }
}
```

The Debugger window (`Profiler > GameObject Pool`) provides a live view of all pool stats.

## Notes

- Pool rules must be registered via `PoolConfigScriptableObject` before spawning. Unregistered locations will log an error and return null.
- `Spawn()` (sync) returns null if the prefab hasn't been loaded yet. Use `SpawnAsync()` for first-time loads.
- `Despawn()` is safe to call on non-pooled GameObjects — it will fall back to `Object.Destroy()` with a warning.
- `ObjectPoolHandle` is automatically added to instances on creation. External `Destroy()` of a pooled object triggers `NotifyHandleDestroyed` for cleanup.
- The service is driven by `IServiceTickable.Tick()` via `GameServices.Tick` — no separate MonoBehaviour Update loop.
- `ObjectPoolSetting` component on GameEntry provides Inspector-configured defaults and low-memory/focus event registration (same pattern as `MemoryPoolSetting`).

---
[« Back to Main README](../../README_EN.md)
