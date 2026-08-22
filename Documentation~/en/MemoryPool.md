# MemoryPool

> Zero-GC page-based memory pool with unmanaged metadata, EWMA adaptive watermarks, and phase-driven budget control.

The MemoryPool system provides high-performance pooling for plain C# objects (not GameObjects). It uses `unsafe` pointer-based page metadata (`Marshal.AllocHGlobal`) to achieve zero GC pressure on the hot path. The pool is accessed via the static `MemoryPool` facade or the generic `MemoryPool<T>` type.

## When to Use MemoryPool vs ObjectPool

| Aspect | MemoryPool | ObjectPool |
|--------|-----------|------------|
| **Target** | Plain C# objects (`MemoryObject`) | Named objects with lifecycle (`ObjectBase`) |
| **GC Pressure** | Zero (unmanaged page metadata) | Managed arrays (Dictionary + List) |
| **Keying** | By type only (`MemoryPool<T>`) | By string name + type |
| **Expiration** | EWMA-based adaptive watermarks | Configurable expire time + capacity |
| **Use Case** | Events, args, buffers, temporary data | GameObjects, UI elements, business objects |

If your object inherits `MemoryObject` and needs simple acquire/release semantics, use MemoryPool. If you need named pools, expiration, priority, or GameObject support, use ObjectPool.

## Core Concepts

### Page-Based Slot Allocation

Each type `T` gets its own `MemoryPool<T>` with 32-slot pages. Pages are allocated on demand and recycled when fully empty. Slot metadata (state, generation, free-list links) is stored in unmanaged memory (`Marshal.AllocHGlobal`), avoiding GC overhead.

### EWMA Adaptive Watermarks

The pool tracks acquire rate and burst patterns using Exponentially Weighted Moving Average (EWMA). The target free reserve is adjusted each tick based on:
- `AcquireRateEwma` — smoothed acquire rate per frame
- `BurstEwma` — smoothed burst size (acquire - release delta)
- `MissDebt` — outstanding miss count (drives immediate growth)
- `IdleFrames` — frames since last activity (drives decay)

### Phase-Driven Budgets

The `MemoryPoolRegistry.Phase` controls per-tick growth and eviction budgets:

| Phase | Growth Budget | Evict Budget | Use During |
|-------|--------------|-------------|------------|
| `Boot` | 32 | 4 | Early startup (splash screen) |
| `Loading` | 32 | 4 | Resource download, assembly loading, preload |
| `Gameplay` | 2 | 2 | Normal gameplay |
| `Background` | 8 | 16 | App lost focus |
| `LowMemory` | 0 | 32 | System low-memory warning |

### Tombstone Pages

When `ClearAll()` is called while objects are still leased, pages are marked as "tombstone" — free objects are evicted immediately, but leased objects are preserved. When the last leased object is released, the page storage is freed.

### Native Metadata Auto-Trim

After `AutoTrimNativeMetadataFrames` (default 18000 frames ≈ 5 minutes) of complete idleness, the pool releases its unmanaged page metadata to minimize memory footprint.

## Core Types

Namespace: `Moirai.Atropos`

| Type | Description |
|------|-------------|
| `MemoryPool` | Static facade: `Acquire<T>()`, `Release<T>()`, `Add<T>()`, `CompactAll()`, etc. |
| `MemoryPool<T>` | Generic typed pool: `Acquire()`, `Release()`, `Add()`, `Shrink()`, `Compact()`, `TrimNativeMetadata()` |
| `MemoryPoolRegistry` | Registry: manages all pool handles, `TickAll()`, `Phase`, `ClearAll()`, `CompactAll()` |
| `MemoryObject` | Abstract base class for pooled objects: `Clear()` method for state reset |
| `IPoolEvictable` | Optional interface: `OnEvict()` called when an object is evicted (not normal release) |
| `MemoryPoolHandle` | Cached handle for dynamic type lookup: `Acquire()`, `Release()` |
| `MemoryPoolInfo` | Snapshot struct: `UnusedCount`, `UsingCount`, `AcquireCount`, `MissCount`, `MissRate`, etc. |
| `EMemoryPoolPhase` | Enum: `Boot`, `Loading`, `Gameplay`, `Background`, `LowMemory` |
| `MemoryPoolSetting` | MonoBehaviour: Inspector-configurable decay timers and capacity limits |

## Quick Start

Define a pooled object:

```csharp
using Moirai.Atropos;

public class DamageEvent : MemoryObject, IPoolEvictable
{
    public int TargetId;
    public float Amount;

    public override void Clear()
    {
        TargetId = 0;
        Amount = 0f;
    }

    public void OnEvict()
    {
        // Called when the object is evicted due to hard capacity overflow
    }
}
```

Acquire and release:

```csharp
// Generic API (fastest, compile-time typed)
var evt = MemoryPool.Acquire<DamageEvent>();
evt.TargetId = entityId;
evt.Amount = 50f;
// ... use evt ...
MemoryPool.Release(evt);

// Dynamic type API (use when type is unknown at compile time)
MemoryPoolHandle handle = MemoryPool.GetHandle(typeof(DamageEvent));
MemoryObject obj = handle.Acquire();
handle.Release(obj);
```

Pre-warm a pool:

```csharp
MemoryPool.Add<DamageEvent>(64);
MemoryPoolRegistry.TickAll(Time.frameCount); // Process growth budget
```

Configure capacity:

```csharp
MemoryPool.SetCapacity<DamageEvent>(softCapacity: 128, hardCapacity: 512);
```

## Phase Integration

The `MemoryPoolSetting` MonoBehaviour drives `MemoryPoolRegistry.TickAll()` every frame and handles system events:

- `Application.lowMemory` → switches to `LowMemory` phase, calls `CompactAll()`, restores previous phase
- `Application.focusChanged` → switches to `Background` phase when unfocused, restores when refocused

The Procedure flow chain sets the phase at each stage:
- `ProcedureLaunch` / `ProcedureSplash` → `Boot`
- `ProcedureInitPackage` through `ProcedurePreload` → `Loading`
- `ProcedurePrepare4Entrance` → `Gameplay`

## Statistics and Debugging

Get pool info without allocation:

```csharp
MemoryPoolInfo[] buffer = new MemoryPoolInfo[MemoryPool.Count];
int actual = MemoryPool.GetAllMemoryPoolInfos(buffer);
for (int i = 0; i < actual; i++)
{
    Debug.Log($"{buffer[i].Type.Name}: unused={buffer[i].UnusedCount}, miss={buffer[i].MissCount}, missRate={buffer[i].MissRate:P1}");
}
```

Subscribe to per-frame stats updates (zero cost when unsubscribed):

```csharp
MemoryPoolRegistry.PoolStatsUpdated += infos =>
{
    foreach (var info in infos)
    {
        if (info.MissRate > 0.1f)
            Debug.LogWarning($"High miss rate for {info.Type.Name}: {info.MissRate:P1}");
    }
};
```

The Debugger window (if enabled) shows all pools with columns: Unused, Using, Acquire, Release, Miss, Reserve, Idle, Pages, Util%.

## Inspector Settings

The `MemoryPoolSetting` component exposes:

| Field | Default | Description |
|-------|---------|-------------|
| `m_ShortDecayStartFrames` | 1800 | Frames before target reserve starts decaying (~30s @60fps) |
| `m_LongDecayStartFrames` | 7200 | Frames before accelerated decay (~2min @60fps) |
| `m_UnscheduleIdleFrames` | 18000 | Frames before tick scheduling stops (~5min @60fps) |
| `m_ZeroFreeReserveStartFrames` | 7200 | Frames before target reserve can drop to 0 (~2min @60fps) |
| `m_AutoTrimNativeMetadataFrames` | 18000 | Frames before native metadata is auto-released (~5min @60fps) |
| `m_SoftFreeReserveLimit` | 128 | Default soft capacity limit |
| `m_HardFreeReserveLimit` | 512 | Default hard capacity limit (overflow triggers eviction) |
