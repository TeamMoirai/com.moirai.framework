# Timer Module

> High-performance timer module based on a four-level timing wheel, no full scan, suitable for large-scale timed scenarios such as skill cooldowns, heartbeat packets, and delayed tasks.

The `Timer` module provides the ability to add, pause, resume, restart, and remove timers. The implementation class `TimerModule` uses a four-level timing wheel algorithm (256 slots per level, 1 ms precision, advancing at most 64 ticks per frame), combined with paged slot reuse and versioned handles, maintaining zero GC and O(1) operation cost even with hundreds of thousands of timers. The module maintains two independent timing wheels: scaled (affected by `Time.timeScale`) and unscaled. Access via `GameModule.Timer`.

Note: This module is a separate facility from the Scheduler (`Scheduler.Delay`, `Scheduler.WaitFrame`, etc.) under `Runtime/Core/Schedulers`. The Scheduler is a zero-allocation general-purpose scheduler, while the Timer module is a timing wheel implementation designed for massive timed tasks. Choose based on your needs.

## Core Features

- Four-level timing wheel: 4 levels x 256 buckets, 1 ms tick precision, no full scan on expiration
- Versioned handles: handles are `(version << 32) | (slot + 1)`, old handles automatically invalidate after slot reuse (ABA prevention)
- Dual timing wheels: scaled (`Time.timeAsDouble`) and unscaled (`Time.unscaledTimeAsDouble`) advance independently
- Three callback forms: `TimerHandler` (object[] parameters), `Action` (no parameters), `Action<T>` (generic single parameter, avoids closures)
- Exception isolation: exceptions thrown by individual callbacks are only logged and do not affect other timers or timing wheel advancement
- Reentrancy safe: callbacks can safely call `RemoveTimer` / `Stop` / `Restart` on themselves or other timers
- Paged storage and prewarming: prewarms 1024 slots by default, expands in pages of 256, with a maximum capacity of approximately 1 million slots

## Core Types

Namespace: `Moirai.Atropos.Timer`

| Class/Interface | Description |
|---------|------|
| `ITimerModule` | Public module interface: `AddTimer` three overloads, `Stop` / `Resume` / `Restart` / `RemoveTimer`, `Prewarm`, `GetStatistics`, `GetAllTimers` |
| `TimerModule` | `internal sealed` implementation class, inherits `Module` and implements `IUpdateModule`, driven by the module system every frame |
| `TimerHandler` | Delegate `void TimerHandler(object[] args)`, traditional object[] parameter callback |
| `TimerDebugInfo` | Debug info struct: `timerHandle`, `leftTime`, `duration`, `age`, `flags` |
| `TimerDebugFlags` | Debug flag constants: `RUNNING`, `LOOP`, `UNSCALED` |

## Quick Start

```csharp
// Access the module
ITimerModule timer = GameModule.Timer;

// 1. Delayed execution (no-parameter Action)
ulong id1 = timer.AddTimer(() => Debug.Log("Executed after 3 seconds"), 3f);

// 2. Loop timer (affected by timeScale)
ulong id2 = timer.AddTimer(OnHeartbeat, 1f, isLoop: true);

// 3. Generic single-parameter callback, avoids closure allocation (T constrained to class)
ulong id3 = timer.AddTimer<Entity>(OnSkillCdEnd, target, 5f);

// 4. Traditional object[] parameter (compatible with legacy code)
ulong id4 = timer.AddTimer(OnArgsCallback, 2f, false, false, 100, "hello");
void OnArgsCallback(object[] args) { /* args[0]=100, args[1]="hello" */ }

// Pause / Resume / Restart / Remove
timer.Stop(id2);       // Pause and record remaining time
timer.Resume(id2);     // Resume from remaining time
timer.Restart(id2);    // Reset to full duration and restart
timer.RemoveTimer(id2);// Remove completely and reclaim slot
```

## Advanced Usage

### Unscaled Time

```csharp
// isUnscaled: true means not affected by Time.timeScale (pause menus, UI countdowns, etc.)
ulong id = timer.AddTimer(OnCountdown, 1f, isLoop: true, isUnscaled: true);
```

### Loop Timer Scheduling Rules

After a loop timer triggers, it is rescheduled based on "last trigger time + duration" to maintain phase stability. If frame drops cause the scheduled time to fall behind the current time, it aligns to "current time + duration" to avoid consecutive catch-up dispatches.

### Prewarming and Statistics

```csharp
// Prewarm slots before combat to avoid runtime page expansion (max 4096 pages x 256 slots)
timer.Prewarm(4096);

// Runtime statistics: active count, pool capacity, peak active count, free count
timer.GetStatistics(out int activeCount, out int poolCapacity,
                    out int peakActiveCount, out int freeCount);

// Debug snapshot: fills the caller-provided array, returns the actual number written
var results = new TimerDebugInfo[activeCount];
int count = timer.GetAllTimers(results);
for (int i = 0; i < count; i++)
{
    bool isRunning = (results[i].flags & TimerDebugFlags.RUNNING) != 0;
    Debug.Log($"{results[i].timerHandle} remaining {results[i].leftTime:F2}s");
}
```

### Implementation Highlights

- Data is stored in pages of 256 slots across multiple parallel arrays (`TimerPage`), avoiding LOH pressure from large arrays
- Each frame's `Update` advances both timing wheels independently, with a budget of at most 64 ticks per wheel per frame to prevent snowballing after long hitches
- High-level bucket expiration cascades down to lower levels; lookup is purely slot index arithmetic
- Module `Shutdown` clears all timers and wheel structures

## Notes

- `AddTimer` returns `0UL` on failure (null callback or slot exhaustion); valid handles are never 0.
- Slot reuse includes versioning: calling `Stop` / `RemoveTimer` etc. on an invalid handle is a safe no-op.
- `RemoveTimer` is equivalent to a one-time natural expiration; both recycle the slot. Loop timers must be manually removed, otherwise they continue to trigger.
- Callbacks execute synchronously on the main thread (in the module's `Update`); do not perform blocking operations inside callbacks.
- Time scaling only affects timers with `isUnscaled: false`; choose the appropriate callback form when modifying `Time.timeScale`.

---
[« Back to Main README](../README_EN.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)