# Timer Service

> High-performance timer service based on a four-level timing wheel, no full scan, suitable for large-scale timed scenarios such as skill cooldowns, heartbeat packets, and delayed tasks.

The `Timer` service provides the ability to add, pause, resume, restart, and remove timers. The default implementation `DefaultTimerHandler` uses a four-level timing wheel algorithm (256 slots per level, 1 ms precision, advancing at most 64 ticks per frame), combined with paged slot reuse and versioned handles, maintaining zero GC and O(1) operation cost even with hundreds of thousands of timers. The service maintains two independent timing wheels: scaled (affected by `Time.timeScale`) and unscaled. Access via the `TimerService.Xxx()` static facade (HandlerHost pattern: `TimerService` static facade + `TimerServiceHandler` abstract base class + `DefaultTimerHandler` timing wheel backend + `TimerServiceSettings` configuration).

Note: This service is a separate facility from the Scheduler (`Scheduler.Delay`, `Scheduler.WaitFrame`, etc.) under `Runtime/Core/Schedulers`. The Scheduler is a zero-allocation general-purpose scheduler, while the Timer service is a timing wheel implementation designed for massive timed tasks. Choose based on your needs.

## Core Features

- Four-level timing wheel: 4 levels x 256 buckets, 1 ms tick precision, no full scan on expiration
- Versioned handles: handles are `(version << 32) | (slot + 1)`, old handles automatically invalidate after slot reuse (ABA prevention)
- Dual timing wheels: scaled (`Time.timeAsDouble`) and unscaled (`Time.unscaledTimeAsDouble`) advance independently
- Two callback forms: `Action` (no parameters), `Action<T>` (generic single parameter; use cached delegates or static method groups on hot paths to avoid closure allocation)
- Handle queries: `IsRunning` checks the running state and `GetLeftTime` returns the remaining time, both safely access via versioned handles
- Exception isolation: exceptions thrown by individual callbacks are only logged (Fatal level) and do not affect other timers or timing wheel advancement
- Reentrancy safe: callbacks can safely call `RemoveTimer` / `Stop` / `Restart` on themselves or other timers
- Paged storage and prewarming: initial capacity is configured via `TimerServiceSettings` (default 1024, minimum 256, maximum 16384), expands in pages of 256, with a maximum capacity of approximately 1 million slots

## Core Types

Namespace: `Moirai.Atropos.Timer`

| Class/Interface | Description |
|---------|------|
| `TimerService` | Static facade (`[HandlerHost]`): `AddTimer` two overloads, `Stop` / `Resume` / `Restart` / `RemoveTimer`, `IsRunning` / `GetLeftTime`; debug APIs (`GetStatistics` / `GetAllTimers` / `GetStaleOneShotTimers`) live in partial `TimerService.Debug` |
| `TimerServiceHandler` | Timing wheel backend handler abstract base class (inherits `FrameworkHandler`, contract members are `internal`), defines the backend contract invoked by the facade |
| `DefaultTimerHandler` | Default implementation (four-level timing wheel algorithm, located under `Handler/`), carries the core timing wheel logic; initial capacity is configured by its serialized field `m_InitialCapacity` |
| `TimerServiceSettings` | Framework settings, selects the timer backend implementation via `[ProviderDropdown]` |
| `TimerDebugInfo` | Debug info struct: `TimerHandle`, `LeftTime`, `Duration`, `Age`, `Flags` |
| `TimerDebugFlags` | Debug flag constants: `RUNNING`, `LOOP`, `UNSCALED` |
| `TimerServiceDebugger` | Timer debug component (add-as-needed): attach to any scene object to inspect runtime statistics and debug info in the Inspector, zero runtime logic overhead |

## Quick Start

```csharp
// 1. Delayed execution (no-parameter Action)
ulong id1 = TimerService.AddTimer(() => Debug.Log("Executed after 3 seconds"), 3f);

// 2. Loop timer (affected by timeScale)
ulong id2 = TimerService.AddTimer(OnHeartbeat, 1f, isLoop: true);

// 3. Generic single-parameter callback, avoids closure allocation (T constrained to class; use cached delegates or static method groups on hot paths)
ulong id3 = TimerService.AddTimer<Entity>(OnSkillCdEnd, target, 5f);

// Pause / Resume / Restart / Remove
TimerService.Stop(id2);       // Pause and record remaining time
TimerService.Resume(id2);     // Resume from remaining time
TimerService.Restart(id2);    // Reset to full duration and restart
TimerService.RemoveTimer(id2);// Remove completely and reclaim slot

// Handle queries
bool running = TimerService.IsRunning(id2);
float leftTime = TimerService.GetLeftTime(id2);
```

## Advanced Usage

### Unscaled Time

```csharp
// isUnscaled: true means not affected by Time.timeScale (pause menus, UI countdowns, etc.)
ulong id = TimerService.AddTimer(OnCountdown, 1f, isLoop: true, isUnscaled: true);
```

### Loop Timer Scheduling Rules

After a loop timer triggers, it is rescheduled based on "last trigger time + duration" to maintain phase stability. If frame drops cause the scheduled time to fall behind the current time, it aligns to "current time + duration" to avoid consecutive catch-up dispatches.

### Capacity Configuration and Statistics

The initial capacity is configured in the `TimerServiceSettings` asset (`DefaultTimerHandler.m_InitialCapacity`, default 1024, minimum 256, aligned by 256). It only takes effect when the service initializes; there is no runtime capacity reconfiguration. You can also adjust it visually via the `TimerServiceDebugger` component's Inspector slider (read-only during play mode).

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

### Debug Component (TimerServiceDebugger)

`TimerServiceDebugger` is an add-as-needed empty MonoBehaviour (menu `Moirai/Timer Service Debugger`). Once attached, its Inspector provides:

- **Configuration**: edits the initial capacity of the `TimerServiceSettings` asset (slider + int field, aligned and clamped by 256; read-only during play mode; changes take effect on next service initialization).
- **Runtime Debug**: active count / pool capacity / peak active / free slot statistics with usage progress bars.
- **Active Timer Sample**: first 32 timers with handle, form (loop/once), scaling mode, running state, remaining and total duration.
- **Stale One-shot Timers**: warning list of one-shot timers alive for over 300 seconds, helping locate leaks.

### Implementation Highlights

- Data is stored in pages of 256 slots across multiple parallel arrays (`TimerPage`), avoiding LOH pressure from large arrays
- Each frame's `Update` advances both timing wheels independently, with a budget of at most 64 ticks per wheel per frame to prevent snowballing after long hitches
- High-level bucket expiration cascades down to lower levels; lookup is purely slot index arithmetic
- Service `Shutdown` clears all timers and wheel structures

## Notes

- Facade methods always forward through the `Handler` property: when the service is not ready it is lazily initialized from `TimerServiceSettings`; if the settings asset is unavailable or the default factory is missing, an exception is thrown (fail-fast) instead of silently returning default values. Calls after `Shutdown` likewise rebuild on demand.
- `AddTimer` returns `0UL` on failure (null callback or slot exhaustion); valid handles are never 0. Failures log a `LogUtility.Warning` in the Editor; no logging overhead at runtime.
- Slot reuse includes versioning: calling `Stop` / `RemoveTimer` / `IsRunning` etc. on an invalid handle is a safe no-op or returns the default value.
- `RemoveTimer` is equivalent to a one-time natural expiration; both recycle the slot. Loop timers must be manually removed, otherwise they continue to trigger.
- Callbacks execute synchronously on the main thread (in the service's `Update`); do not perform blocking operations inside callbacks.
- Time scaling only affects timers with `isUnscaled: false`; choose the appropriate callback form when modifying `Time.timeScale`.
- On hot paths, register timers with cached delegates or static method groups; avoid captured lambdas / closures that introduce allocations.

---
[« Back to Main README](../../README_EN.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
