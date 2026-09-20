# PlayerLoop Logic Driver

> Mono-free game frame logic driver: invoked directly by Unity PlayerLoop. Subscriptions survive scene changes and host destruction.

## Background

The previous `GameApp` stored `Update`/`FixedUpdate`/`LateUpdate` listeners on **instance events** of a hidden Mono host. That host could be destroyed before the initial scene load, silently dropping all subscriptions.

`Runtime/Services/Timer` (`TimerService`) is the unified timing subsystem (four-level timing wheel + frame timers), exposing `Delay` / `WaitFrame` and friends. It is advanced through `IServiceTickable` by `GameServices.Tick`, which is itself registered as an Update callback on this driver. `IUpdateHandler` targets game-side systems and DI composition roots — do not confuse the two paths.

## Architecture

| Component | Responsibility |
|------|------|
| `Moirai.Atropos.FrameLoop.PlayerLoopDriver` | Zero-alloc static registry + Drive entry points |
| `Moirai.Atropos.FrameLoop.PlayerLoopInjector` | Inject/restore Unity `PlayerLoopSystem` |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | Interface handlers (recommended, DI-friendly) |
| `IPlayerLoopPriority` | Optional drive order (lower runs first) |
| `GameApp` static APIs | Compatibility facade forwarding to Driver; holds no MonoBehaviour |
| `GameAppHost` | The single lightweight Mono host: coroutines / Gizmos / ApplicationPause, forwarding only |

Injection points:

- Start of `PlayerLoop.Update` → Framework Update
- Start of `PlayerLoop.FixedUpdate` → Framework FixedUpdate
- End of `PlayerLoop.PreLateUpdate` → Framework LateUpdate (after MonoBehaviour.LateUpdate)

Injection uses the **current** PlayerLoop so UniTask and other systems are preserved. Default loop is captured at `SubsystemRegistration` and restored on Shutdown.

Each Drive entry samples the frame clock with `GameTime.StartFrame()` before invoking handlers and then callbacks — **both subscription kinds read the same frame**.

## Quick Start

```csharp
using Moirai.Atropos.FrameLoop;

public sealed class MySystem : IUpdateHandler
{
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // No heap allocations
    }
}

PlayerLoopDriver.Initialize();
PlayerLoopDriver.Register(new MySystem());

// Compatibility API
GameApp.AddUpdateListener(OnUpdate);
```

> A type implementing several stage interfaces makes `Register(handler)` an ambiguous-overload
> compile error: use `RegisterAll(handler)` for every implemented stage, or cast to target one —
> `Register((ILateUpdateHandler)handler)`.

## Zero-Allocation Contract

`DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` and all handler implementations:

- `for` loops only; no LINQ / closures / string concat
- Register/unregister during drive is deferred into the pending buffer **of that stage**, committed when the stage finishes; cross-stage registrations never leak into each other
- A throwing subscriber cannot wedge the driving flag: `finally` resets it and commits the buffers (how the exception itself is handled is covered in the next section)
- **Thread contract**: the registries are lock-free, so register/unregister is main-thread only (fail-fast assert rather than silently dropping a subscription); background threads must hop through `MainThreadDispatcher.Post/Send`
- Profiler markers: `PlayerLoopDriver.Update`, etc.

## Exception Handling

Subscriber exceptions are graded at **compile time**, following the same contract as the kernel's `ServiceScope.RETHROW_TICK_EXCEPTIONS`:

| Build | Behavior |
|------|------|
| Editor / development | Logged at `Error` with full stack, then **rethrown** — surface the defect, never degrade silently |
| Release | Logged at `Error`, then **isolated** — one subscriber cannot truncate the rest of its stage |

Consecutive-failure tripping applies in **both** tiers: a subscriber that throws `FailureTripThreshold` times **in a row** in one stage (default 300, about 2.5 s at 120 fps) is removed from that stage and a single `Warning` is emitted — in development builds it is removed first and the exception still rethrows that frame, so the trip is observable in the editor. One success resets the counter, so an intermittent fault never accumulates into a trip; re-registering resets it completely.

Two exemptions, so that subscriber-level failures can never disable the framework itself:

- **Core hooks** (`SetCoreUpdateCallback`, …): `GameServices.Tick/FixedTick/LateTick` live here and run ahead of every user subscriber, and they are **never tripped out**. If the framework's own heartbeat could be removed by tripping, one project subscriber's repeated throws would stall the whole service layer with no way back. Core hooks still follow the build tier above (rethrow in development, isolated in release).
- **Quit / destroy broadcasts** (`AddApplicationQuitCallback`, `AddDestroyCallback`): invoked item by item (`GetInvocationList` allocates, so this is reserved for one-shot broadcasts). A throwing item cannot block the rest, and these do **not** rethrow even in development builds — their only job is cleanup, so truncation means every later release action is silently skipped.

The remaining Unity-event tables (`focusChanged`, pause, gizmos) are still plain multicast invokes: a throwing subscriber truncates the later subscribers of that one broadcast.

An `IUpdateHandler` implemented on a destroyed `MonoBehaviour` throws `MissingReferenceException` on invocation: `Drive`'s null check only covers emptied slots, not Unity's fake-null. Unregister explicitly from `OnDestroy`.

## Lifecycle

| Moment | Behavior |
|------|------|
| `SubsystemRegistration` | Capture default PlayerLoop; Driver marked Shutdown; `GameAppHost` clears its shutdown flag |
| `GameApp.Initialize` (`BeforeSceneLoad`) | `PlayerLoopDriver.Initialize()` injects + installs the built-in core hooks, then materializes `GameAppHost`; injection is verified against the actual loop — if any marker is missing the injected flag stays false and a warning is logged |
| `AfterSceneLoad` | Self-heal: if a third party rebuilt the loop from default at or before `BeforeSceneLoad` (including same-phase but later than this framework) and wiped the markers, re-inserts them based on the live loop and logs a warning. **The phase must not move earlier than the injection point** — injection happens at `BeforeSceneLoad`, and before it `s_Injected` is always false, so the guard's first line returns and the check never runs |
| `GameApp.Shutdown` / exit Play | Broadcast Destroy → clear registry → restore default PlayerLoop → destroy host |
| After ECS resets PlayerLoop | Rebuilds up to `AfterSceneLoad` are re-inserted by the self-heal; later ones (e.g. at the end of a custom bootstrap) need `PlayerLoopInjector.Reinject()` after they complete |

## DI (VContainer etc.)

Register handler implementations as services; after composition root `InitializeAsync`:

```csharp
var system = container.Resolve<MySystem>();
PlayerLoopDriver.Register(system);
```

## Compatibility

- **UniTask**: injection bases on current loop and does not overwrite UniTask systems; restoring default on Play exit lets UniTask re-init next Play. **Note**: `RestoreDefault` restores the engine default loop, which also removes UniTask's injection — with DisableDomainReload, exiting Play triggers no domain reload, so edit-mode UniTask stalls until the next reload or Play entry.
- **ECS/DOTS**: Entities may reset the PlayerLoop at `BeforeSceneLoad` — rebuilds happening before/early relative to the self-heal check are re-inserted automatically; if the reset runs later (e.g. a custom bootstrap), call `PlayerLoopInjector.Reinject()` after initialization completes.
- **ApplicationPause**: Unity exposes no pure C# event — `GameAppHost.OnApplicationPause` forwards to `PlayerLoopDriver.RaiseApplicationPause`. Subscriptions live in the static table, so rebuilding the host restores dispatch.
- **Gizmos**: same pattern — `GameAppHost.OnDrawGizmos(Selected)` forwards to `PlayerLoopDriver.RaiseDrawGizmos(Selected)`. Only the editor has a dispatcher; in builds the table is never raised.
- **Coroutines**: `GameApp.StartCoroutine` goes through `GameAppHost.Instance`; destroying the host only kills running coroutines, never subscriptions.

## Editor Debugging

Menu **Window → PlayerLoop Debugger**:

- Recursive dump of the current PlayerLoop with Moirai markers `<< Moirai`
- Handler/callback counts per stage
- Buttons: Ensure Injected / Reinject / Restore Default

---
[« Documentation Index](Index.md) · [GameApp](GameApp.md)
