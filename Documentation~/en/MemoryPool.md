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

Each type `T` gets its own `MemoryPool<T>` with 32-slot pages. Pages are allocated on demand and recycled when fully empty. Slot metadata (state, generation, free-list links) is stored in unmanaged memory (`Marshal.AllocHGlobal`), avoiding GC overhead. Pages themselves are threaded on two intrusive doubly-linked lists (free-page list and empty-slot page list); linking and unlinking happen at the 0↔1 boundaries of a page's counters, so acquiring, trimming and recycling an emptied slot are all O(1).

### EWMA Adaptive Watermarks

The pool tracks acquire rate and burst patterns using Exponentially Weighted Moving Average (EWMA). The target free reserve is adjusted each tick based on:
- `AcquireRateEwma` — smoothed acquire rate per frame
- `BurstEwma` — smoothed burst size (acquire - release delta)
- `PendingGrowth` — objects requested by an explicit `Add()` that have not been built yet (drives immediate growth; a miss on `Acquire` no longer accrues debt — the object is built on the spot and the watermark follows the in-use count)
- `IdleFrames` — frames since last activity (drives decay)

### Live Lease Limits And Leak Visibility

Hard capacity bounds the **free cache**, not the total: `Acquire` constructs on a miss and never fails, so one lost reference is one object gone forever — the symptom is a slowly climbing OOM, not an error at the point of loss. Three things back that up:

- `MemoryPoolInfo.UsingCount` is the instantaneous count outside the pool, `MaxUsingCount` is its peak since the last `ResetAllStats`. A peak that only grows across hours is the leak evidence; `ResetStats` re-bases the peak at the current in-use count so a leaking pool cannot be washed clean.
- `MemoryPool<T>.SetLiveLimit(n)` / `MemoryPool.SetLiveLimit(type, n)` (0 means unlimited, the default) puts a discoverable edge on the leak: crossing it reports with the pool identity, rate-limited (one entry per 300 frames by default), and in development builds it reports then throws. Release builds **still hand the object out** — refusing would break a scene already in motion and would not fix the caller that never returned anything. The global default lives on `MemoryPoolSetting`'s Inspector.
- `MemoryPoolRegistry.ValidateAll()` and `MemoryPool<T>.ValidateStructure()` are read-only structure checks: they walk both page lists and cross-check them against page counters, the global counters, forward/back pointers and flag bits, returning a pool-identified description (`null` when consistent). The intrusive page lists buy O(1) link/unlink at the cost of one class of bug: a missed unlink makes later indexes read freed memory, and that stays silent until some path trips over it. Calling these at meaningful checkpoints (level end, scene unload, load finished) in QA / development builds turns "random crash" into "page 7's free list is broken". It allocates strings while reporting, so keep it out of per-frame code.

### Thread Guard And Fault Severity

The pool is not thread safe; every structure assumes exclusive main-thread access. Three settings:

| Context | Behaviour |
|---|---|
| Editor / development build | Every acquire/release validates the thread id and throws `InvalidOperationException` immediately (message carries owner and current thread ids). |
| Release build (default) | Guard off — one static boolean read per operation; the main thread id is still pinned at `SubsystemRegistration`. |
| QA / soak build | Turn on `MemoryPoolSetting.VerifyMainThreadInRelease` to keep validating in a release build. Cross-thread damage to unmanaged page metadata does not fail at the time it happens — it comes back weeks later as a random crash, which is worth the check while you are hunting. |

Maintenance faults follow the framework-wide `RETHROW_*` convention: `TickAll` is the per-frame boundary, so development builds log one merged Fatal with the failure count and rethrow, while release builds log and contain it (nothing in game code can catch an exception escaping update dispatch). Inside a pool, batch paths (whole-batch trim, page retirement) always finish the batch item by item and report at the end — one bad `OnEvict()` cannot truncate the rest. A single round lists at most 16 callback exceptions and merges the remainder into one summary entry, because unbounded collection allocates exactly while memory pressure is being relieved.

### Phase-Driven Budgets

The `MemoryPoolRegistry.Phase` controls per-tick growth and eviction budgets:

| Phase | Growth Budget | Evict Budget | Use During |
|-------|--------------|-------------|------------|
| `Boot` | 32 | 4 | Early startup (splash screen) |
| `Loading` | 32 | 4 | Resource download, assembly loading, preload |
| `Gameplay` | 2 | 2 | Normal gameplay |
| `Background` | 8 | 16 | App lost focus |
| `LowMemory` | 0 | 32 | System low-memory warning |

Besides widening the evict budget, `LowMemory` also zeroes the target free reserve outright, so the very next tick drains the free list.

### Restrictions During Callbacks

An object's constructor, `Clear()` and `OnEvict()` run in a "pool callback" context. While inside one:

- you may not call these entries of the **same type's** pool: `Acquire` / `Release` / `Add` / `Shrink` / `Compact` / `SetCapacity` / `ClearAll` / `TrimNativeMetadata` / `ResetStats` — they throw `InvalidOperationException`. Cross-type acquire/release is still allowed (`MemoryPool<Other>.Acquire()`); only the read-only `UnusedCount` is unrestricted;
- you may not call the **global maintenance entries** (`MemoryPool.ClearAll` / `CompactAll` / `TrimAllNativeMetadata` / `ClearAllNativeMetadata` / `ResetAllStats` / `SetCapacityAll` / `MemoryPoolRegistry.TickAll`) — they also throw, because they can reallocate the very unmanaged page-header array the in-flight release is holding by `ref`.

If a callback needs to trigger follow-up work, queue it and consume it after the callback returns.

### Tombstone Pages

When `ClearAll()` is called while objects are still leased, pages are marked as "tombstone" — free objects are evicted immediately, but leased objects are preserved. When the last leased object is released, the page storage is freed.

### Native Metadata Auto-Trim

After `AutoTrimNativeMetadataFrames` (default 18000 frames ≈ 5 minutes) of complete idleness, the pool releases its unmanaged page metadata to minimize memory footprint.

Page metadata lives in `Marshal.AllocHGlobal`, i.e. the process heap, while static fields only live inside the current domain. Unity's editor script reload does not raise `AppDomain.DomainUnload`, so the package adds an editor-side sweep: `MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown()` runs on `AssemblyReloadEvents.beforeAssemblyReload` and on `EditorApplication.quitting`. If anything is still leased it **returns false and frees nothing** (releasing metadata then would make the next return write into freed memory) and logs one actionable warning — that leak stays with the editor session, which is a better trade than a dangling pointer. Page arrays and the objects themselves do not survive a domain reload, so metadata is all that can leak.

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
| `MemoryPoolInfo` | Snapshot struct: `UnusedCount`, `UsingCount`, `MaxUsingCount`, `LiveLimit`, `AcquireCount`, `MissCount`, `MissRate`, etc. |
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

The Debugger window (if enabled) shows all pools with columns: Unused, Using (with the max high-water mark), Acquire, Release, Miss, Reserve, Idle, Pages, Util%, plus Limit when a `LiveLimit` is set; a pool sitting on its limit or with a high miss rate is highlighted as an error.

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
| `m_VerifyMainThreadInRelease` | false | Keep the main-thread guard active in release builds (for QA / soak builds) |
| `m_DefaultLiveLimit` | 0 | Global default for the live (leased) object limit; 0 means unlimited |

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [ObjectPool](ObjectPool.md) · [Core](Core.md)
