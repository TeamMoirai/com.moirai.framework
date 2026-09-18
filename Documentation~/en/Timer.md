# Timer Service

> High-performance timer service based on a four-level timing wheel, no full scan, suitable for large-scale timed scenarios such as skill cooldowns, heartbeat packets, and delayed tasks.

The `Timer` service provides the ability to delay, wait for frames, pause, resume, restart, and cancel timers. The default implementation `DefaultTimerHandler` is a **composite façade over two independent engines**: `WheelTimerEngine` (`Delay`, a four-level timing wheel — 256 slots per level, 1 ms precision, at most 64 ticks per frame) and `FrameTimerEngine` (`WaitFrame`, per-frame decrement). Each occupies its own lane with a private paged slot pool and handle namespace, so they never reference or branch on each other; `DefaultTimerHandler` only routes creation to the right engine, dispatches handle operations by the lane bits embedded in the handle, and fans out/aggregates phase ticks and statistics across engines. The wheel engine maintains two independent wheels, scaled (affected by `Time.timeScale`) and unscaled. Access via the `TimerService.Xxx()` static facade (HandlerHost pattern: `TimerService` static facade + `TimerServiceHandler` abstract base class + `DefaultTimerHandler` composite backend + `TimerServiceSettings` configuration).

The public API is driven by `ulong` handles: `Delay` / `WaitFrame` / `Cancel` / `Pause` / `Resume` / `IsDone`. Each handle exposes the `TimerHandleExtensions` methods (`handle.Cancel()` / `handle.Pause()` / `handle.Resume()` / `handle.IsDone()`), so no separate handle type is required.

## Core Features

- Four-level timing wheel: 4 levels x 256 buckets, 1 ms tick precision, no full scan on expiration
- Versioned handles: handles are `(version << 32) | (lane << 21) | (slot + 1)`, old handles automatically invalidate after slot reuse (ABA prevention); the embedded lane id gives the timing-wheel and frame engines separate, non-colliding handle namespaces; dispatch iterates over handle snapshots, so it is reentrancy-safe
- Dual timing wheels: scaled (`Time.timeAsDouble`) and unscaled (`Time.unscaledTimeAsDouble`) advance independently
- Two timer forms: **time timers** (`Delay` family, run on the wheel by seconds) and **frame timers** (`WaitFrame` family, decrement frame by frame and never enter the wheel)
- Trigger phase: `TimerPhase.Update / FixedUpdate / LateUpdate` — time timers expiring in Fixed/Late are deferred to the matching tick; frame timers advance within their own tick
- Multiple callback forms: `Action` (no parameters), `Action<T>` (generic single parameter), `TimerUnsafeBinding` (`delegate*` function pointer, zero-allocation), and progress callbacks `Action<float>` (time 0..1) / `Action<int>` (frame count, 1-based)
- Handle extensions: `handle.Cancel()` / `.Pause()` / `.Resume()` / `.IsDone()`, plus `await handle.WaitAsync()` (UniTask that polls until done)
- Bulk operations: `PauseAll` / `ResumeAll` / `CancelAll`
- Exception isolation: exceptions thrown by individual callbacks are only logged (Fatal level) and do not affect other timers or timing wheel advancement
- Reentrancy safe: callbacks can safely call `Cancel` / `Pause` / `Restart` on themselves or other timers
- Paged storage and prewarming: initial capacity is configured via `TimerServiceSettings` (default 1024, minimum 256, maximum 16384), expands in pages of 256, with a maximum capacity of approximately 1 million slots

## Core Types

Namespace: `Moirai.Atropos.Timer`

| Class/Interface | Description |
|---------|------|
| `TimerService` | Static facade (`[HandlerHost]`): `Delay` (four overloads), `DelayUnsafe`, `WaitFrame` (two overloads), `WaitFrameUnsafe`, `Pause` / `Resume` / `PauseAll` / `ResumeAll` / `Restart` / `Cancel` / `CancelAll`, `IsRunning` / `IsDone` / `GetLeftTime` / `GetElapsed` / `GetDuration`; debug APIs (`GetStatistics` / `GetAllTimers` / `GetStaleOneShotTimers`) live in partial `TimerService.Debug` |
| `TimerServiceHandler` | Timer backend handler abstract base class (inherits `FrameworkHandler`, contract members are `internal`), defines the backend contract invoked by the facade |
| `DefaultTimerHandler` | Default implementation (four-level timing wheel + frame timing + phase dispatch, located under `Handler/`); initial capacity is configured by its serialized field `m_InitialCapacity` |
| `TimerServiceSettings` | Framework settings, selects the timer backend implementation via `[ProviderDropdown]` |
| `TimerPhase` | Trigger phase enum: `Update` (default) / `FixedUpdate` / `LateUpdate` |
| `TimerUnsafeBinding` | Zero-allocation callback binding struct (function pointer preferred, `Action` compatible); used with `DelayUnsafe` / `WaitFrameUnsafe` |
| `TimerHandleExtensions` | `ulong` handle extensions: `Cancel` / `Pause` / `Resume` / `IsDone` / `WaitAsync` (UniTask) |
| `TimerDebugInfo` | Debug info struct: `TimerHandle`, `LeftTime`, `Duration`, `Age`, `Flags` |
| `TimerDebugFlags` | Debug flag constants: `RUNNING`, `LOOP`, `UNSCALED` |
| `TimerServiceDebuggerWindow` | Timer debug view (native UI Toolkit, inherits `PollingDebuggerWindowBase`): carries the debug content (statistics, timer sample, stale detection); auto-registered into the in-game debugger as "Profiler/Timer" by `TimerService.OnInit` |

## Quick Start

```csharp
// 1. Delayed execution (no-parameter Action) — fires after 3 seconds
ulong id1 = TimerService.Delay(3f, () => Debug.Log("Executed after 3 seconds"));

// 2. Loop timer (affected by timeScale)
ulong id2 = TimerService.Delay(1f, OnHeartbeat, isLooped: true);

// 3. Generic single-parameter callback, avoids closure allocation (T constrained to class; use cached method groups on hot paths)
ulong id3 = TimerService.Delay<Entity>(5f, OnSkillCdEnd, target);

// 4. Frame timer — fires after 30 frames
ulong id4 = TimerService.WaitFrame(30, OnAfterFrames);

// Pause / Resume / Restart / Cancel (via handle extensions, or TimerService.Pause(id) ...)
id2.Pause();      // Pause and record remaining time
id2.Resume();     // Resume from remaining time
id2.Cancel();     // Cancel and reclaim slot (equivalent to TimerService.Cancel(id2))
TimerService.Restart(id2);  // Reset to full duration and restart

// Handle queries
bool running  = TimerService.IsRunning(id2);
bool done     = id2.IsDone();          // true once completed / cancelled / invalidated
float left    = TimerService.GetLeftTime(id2);   // remaining seconds (frame timers return 0)
float elapsed = TimerService.GetElapsed(id2);    // seconds for time timers, frames for frame timers
```

## Advanced Usage

### Unscaled Time

```csharp
// ignoreTimeScale: true means not affected by Time.timeScale (pause menus, UI countdowns, etc.)
ulong id = TimerService.Delay(1f, OnCountdown, isLooped: true, ignoreTimeScale: true);
```

### Zero-Allocation Function Pointer Binding (hot path)

```csharp
unsafe {
    // delegate* binding: zero allocation for both registration and dispatch
    ulong id = TimerService.DelayUnsafe(1f, new TimerUnsafeBinding(target, &OnCdEnd));
    ulong frame = TimerService.WaitFrameUnsafe(10, new TimerUnsafeBinding(&OnFrames));
}
// A cached Action also converts implicitly
TimerUnsafeBinding binding = someCachedAction;
```

### Progress Callbacks

```csharp
// Time timer: reports 0..1 every frame before expiration (onComplete may be null)
ulong id = TimerService.Delay(2f, OnDone, progress => bar.value = progress);
// Frame timer: reports the accumulated frame count (1-based) every frame
ulong frame = TimerService.WaitFrame(60, frameCount => text.text = $"{frameCount}/60");
```

### Trigger Phase

```csharp
// Dispatch in FixedUpdate / LateUpdate; expiring time timers are deferred to the matching tick
ulong id = TimerService.Delay(1f, OnLogic, phase: TimerPhase.FixedUpdate);
```

### Awaiting in async code

```csharp
// UniTask: await until the timer completes (polls IsDone internally); accepts a CancellationToken
await TimerService.Delay(3f, OnDone).WaitAsync(cancellationToken);
```

### Loop Timer Scheduling Rules

After a loop timer triggers, it is rescheduled based on "last trigger time + duration" to maintain phase stability. If frame drops cause the scheduled time to fall behind the current time, it aligns to "current time + duration" to avoid consecutive catch-up dispatches.

### Capacity Configuration and Statistics

The initial capacity is configured in the `TimerServiceSettings` asset (`DefaultTimerHandler.m_InitialCapacity`, default 1024, minimum 256, aligned by 256). It only takes effect when the service initializes; there is no runtime capacity reconfiguration.

```csharp
// Runtime statistics: active count, pool capacity, peak active count, free count
TimerService.GetStatistics(out int activeCount, out int poolCapacity,
                           out int peakActiveCount, out int freeCount);

// Debug snapshot: fills the caller-provided array, returns the actual number written
var results = new TimerDebugInfo[activeCount];
int count = TimerService.GetAllTimers(results);
for (int i = 0; i < count; i++)
{
    bool isRunning = (results[i].Flags & TimerDebugFlags.RUNNING) != 0;
    Debug.Log($"{results[i].TimerHandle} remaining {results[i].LeftTime:F2}s");
}

#if UNITY_EDITOR
// Stale timer detection: one-shot timers alive for over 300 seconds (possibly leaked due to logic errors)
var staleResults = new TimerDebugInfo[32];
int staleCount = TimerService.GetStaleOneShotTimers(staleResults);
#endif
```

### Debug Panel (In-Game Debugger)

The timer service's debug info is integrated into the in-game debugger's **Profiler/Timer** panel — auto-registered by `TimerService.OnInit` (native UI Toolkit implementation, themed with the framework), with no scene components required. Double-click the floating FPS entry to expand the debugger and select it in the sidebar:

- **Runtime Statistics**: active count / pool capacity / peak active / free slot statistics with usage progress bars (click a value to copy it).
- **Active Timer Sample**: first 32 timers with handle, form (loop/once), scaling mode, running state, remaining and total duration.
- **Stale One-shot Timers**: warning list of one-shot timers alive for over 300 seconds, helping locate leaks (editor builds only).

The panel rebuilds on a 0.5s throttle; a hint is shown while the service is not ready. To edit the initial capacity, modify the `TimerServiceSettings` asset directly (read-only at runtime; changes take effect on next service initialization).

Custom hosts can also hold independent view instances (`new TimerServiceDebuggerWindow()`, inherits `PollingDebuggerWindowBase`).

### Implementation Highlights

- Data is stored in pages of 256 slots across multiple parallel arrays (`TimerPage`), avoiding LOH pressure from large arrays
- Each frame's `Update` advances both timing wheels independently, with a budget of at most 64 ticks per wheel per frame to prevent snowballing after long hitches
- High-level bucket expiration cascades down to lower levels; lookup is purely slot index arithmetic
- Frame timers live outside the timing wheel in per-phase parallel lists, using a per-slot position table for O(1) swap-remove; dispatch iterates over **handle snapshots**, avoiding the reentrancy risk of in-callback releases and slot reuse
- Service `Shutdown` clears all timers and wheel structures

## Notes

- Facade methods always read the `s_Handler` static field (source-generated) directly — they do **not** trigger the `Handler` property's lazy-load. When the service is unregistered / not yet initialized, or after `OnShutdown` has cleared the handler, every API silently degrades to a safe default: scheduling calls (`Delay` / `WaitFrame` / `DelayUnsafe` / `WaitFrameUnsafe`) return `0UL`; queries return by semantics (`IsRunning` / `IsPaused` → `false`, `IsDone` → `true`, `GetLeftTime` / `GetElapsed` / `GetDuration` → `0f`, statistics → all zeros / empty); control calls (`Pause` / `Resume` / `Restart` / `Cancel`, etc.) are no-ops. The degradation path emits no logs. This contract matches the rest of the framework (UI / ObjectPool / Procedure / Debugger facades likewise go through `s_Handler?.`).
- The `Handler` property is exercised once by `OnInit` to perform assembly (`GetHandlerFromSettings() ?? CreateDefaultHandler()`; throws `InvalidOperationException` when both return null — this is assembly-time fail-fast and does not affect the facade degradation contract). Callers who need "fail on not-ready" semantics for write paths should read `Handler` explicitly (which triggers lazy-load) or check `TimerService.IsValid` first; after `OnShutdown` the service must be re-registered and re-initialized before the handler is rebuilt.
- `Delay` / `WaitFrame` etc. return `0UL` to signal "not registered": the service is not ready (silent, see above), the callback is null, or the slot pool is exhausted (the latter two log a `LogUtility.Warning` from the engine, Editor-only; no logging overhead at runtime). Valid handles are never 0.
- Slot reuse includes versioning: calling `Cancel` / `Pause` / `IsRunning` etc. on an invalid handle is a safe no-op or returns the default value.
- `Cancel` is equivalent to a one-time natural expiration; both recycle the slot. Loop timers must be cancelled manually, otherwise they continue to trigger.
- Frame timers always return `0` from `GetLeftTime`; read their progress via `GetElapsed` / `GetDuration` (in frames).
- Callbacks execute synchronously on the main thread (in the matching phase `Tick`); do not perform blocking operations inside callbacks.
- Time scaling only affects timers with `ignoreTimeScale: false`; choose the appropriate form when modifying `Time.timeScale`.
- On hot paths, register timers with `DelayUnsafe` / `WaitFrameUnsafe` (function pointers) or cached method groups; avoid captured lambdas / closures that introduce allocations.
- The legacy names `AddTimer` / `AddTimerUnsafe` / `Stop` / `RemoveTimer` are kept as `[Obsolete]` aliases (forwarding to `Delay` / `DelayUnsafe` / `Pause` / `Cancel` respectively) only for existing call sites; new code should use the `Delay` family names directly.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
