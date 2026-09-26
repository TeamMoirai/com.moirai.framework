# PlayerLoop Logic Driver

> Mono-free game frame logic driver: invoked directly by Unity PlayerLoop. Subscriptions survive scene changes and host destruction.

## Background

The previous `GameApp` stored `Update`/`FixedUpdate`/`LateUpdate` listeners on **instance events** of a hidden Mono host. That host could be destroyed before the initial scene load, silently dropping all subscriptions.

`Runtime/Services/Timer` (`TimerService`) is the unified timing subsystem (four-level timing wheel + frame timers), exposing `Delay` / `WaitFrame` and friends. It is advanced through `IServiceTickable` by `GameServices.Tick`, which itself is installed as this driver's Update **core hook** (runs ahead of every user subscriber and is never tripped out). `IUpdateHandler` targets game-side systems and DI composition roots — do not confuse the two paths.

## Architecture

| Component | Responsibility |
|------|------|
| `internal PlayerLoopDriver` (`Moirai.Atropos`) | Zero-alloc static registry + Drive entry points; framework-internal |
| `internal PlayerLoopInjector` (`Moirai.Atropos`) | Injects/restores Unity `PlayerLoopSystem` entries — in both directions it touches only our three markers |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | Interface handlers (recommended, DI-friendly), registered via `GameApp.AddXxxHandler` |
| `IPlayerLoopPriority` | Optional drive order (lower runs first) |
| `GameApp` static facade | **The only public entry**: `AddUpdateListener` for Action hooks, `AddXxxHandler` / `AddFrameHandler` for interface handlers; holds no MonoBehaviour |
| `GameAppHost` | The single lightweight Mono host: coroutines / Gizmos / ApplicationPause, forwarding only |

Injection points:

- Start of `PlayerLoop.Update` → Framework Update
- Start of `PlayerLoop.FixedUpdate` → Framework FixedUpdate
- End of `PlayerLoop.PreLateUpdate` → Framework LateUpdate (after MonoBehaviour.LateUpdate)

Both injection and restoration touch only our three markers: injection bases on `GetCurrentPlayerLoop()` so UniTask and other systems stay in place, and `RestoreDefault()` takes our markers out item by item — it does **not** paste the engine default loop back over everything, which would also tear down third-party pumps that never re-inject themselves. `SubsystemRegistration` only resets the injected flag and caches the Drive delegates.

Each Drive entry samples the frame clock with `GameTime.StartFrame()` before invoking handlers and then callbacks — **both subscription kinds read the same frame**.

## Quick Start

```csharp
using Moirai.Atropos;

// Interface style: carries state, takes constructor-injected dependencies,
// and can pin its position in the stage via IPlayerLoopPriority
public sealed class MySystem : IUpdateHandler
{
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // No heap allocations
    }
}

// Register from game code / the composition root
GameApp.AddUpdateHandler(mySystem);
GameApp.RemoveUpdateHandler(mySystem);

// A class implementing several stage interfaces at once
GameApp.AddFrameHandler(myMultiStageSystem);

// Stateless one-off hooks
GameApp.AddUpdateListener(OnUpdate);
GameApp.RemoveUpdateListener(OnUpdate);
```

> `PlayerLoopDriver` itself is **internal**: the registries, the `Raise*` engine-event forwarders and the
> `ResetForTests` seam are not part of the public surface. Game code goes through the `GameApp` facade above.
> Assemblies inside the framework (or on its `InternalsVisibleTo` list) may call `PlayerLoopDriver.Register`
> directly — it is a three-way overload, so passing a multi-stage object fails to compile as ambiguous and
> needs `RegisterAll(handler)` or an explicit cast `Register((ILateUpdateHandler)handler)`.
> The facade's `AddXxxHandler` methods have distinct parameter types and never hit that.

## Zero-Allocation Contract

`DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` and all handler implementations:

- `for` loops only; no LINQ / closures / string concat
- Register/unregister during drive is deferred into the pending buffer **of that stage**, committed when the stage finishes; cross-stage registrations never leak into each other. Same-frame register/unregister pairs on the same object cancel with **last call winning** — including the redundant "register an already-active handler then unregister" sequence, where the removal still takes effect
- A throwing subscriber cannot wedge the driving flag: `finally` resets it and commits the buffers (how the exception itself is handled is covered in the next section)
- **Thread contract**: the registries are lock-free, so register/unregister is main-thread only (fail-fast assert rather than silently dropping a subscription); background threads must hop through `MainThreadDispatcher.Post/Send`
- Profiler markers: `PlayerLoopDriver.Update`, etc.

## Exception Handling

Subscriber exceptions are graded at **compile time**, following the same contract as the kernel's `ServiceScope.RETHROW_TICK_EXCEPTIONS`:

| Build | Behavior |
|------|------|
| Editor / development | Logged at `Error` with full stack, then **rethrown** — surface the defect, never degrade silently |
| Release | Logged at `Error`, then **isolated** — one subscriber cannot truncate the rest of its stage |

Consecutive-failure tripping applies in **both** tiers: a subscriber that throws `FailureTripThreshold` times **in a row** in one stage (default 300, about 2.5 s at 120 fps; assigned values are clamped to ≥ 1) is removed from that stage and a single `Warning` is emitted — in development builds it is removed first and the exception still rethrows that frame, so the trip is observable in the editor. One success resets the counter, so an intermittent fault never accumulates into a trip; unregistering erases the subscriber's failure record, so re-registering resets it completely.

Two exemptions, so that subscriber-level failures can never disable the framework itself:

- **Core hooks** (`SetCoreUpdateCallback`, …): `GameServices.Tick/FixedTick/LateTick` live here and run ahead of every user subscriber, and they are **never tripped out**. If the framework's own heartbeat could be removed by tripping, one project subscriber's repeated throws would stall the whole service layer with no way back. Core hooks still follow the build tier above (rethrow in development, isolated in release).
- **Quit / destroy and lifecycle broadcasts** (`AddApplicationQuitCallback`, `AddDestroyCallback`, `focusChanged`, `ApplicationPause`): invoked item by item (`GetInvocationList` allocates, so this is reserved for low-frequency events). A throwing item cannot block the rest, and these do **not** rethrow even in development builds — truncating cleanup or background-save responses means every later responder is silently skipped.

The remaining Unity-event tables (gizmos) are still plain multicast invokes: editor-only dispatch, and a throwing subscriber truncates the later subscribers of that one broadcast.

An `IUpdateHandler` implemented on a destroyed `MonoBehaviour` throws `MissingReferenceException` on invocation: `Drive`'s null check only covers emptied slots, not Unity's fake-null. Unregister explicitly from `OnDestroy`.

## Lifecycle

| Moment | Behavior |
|------|------|
| `SubsystemRegistration` | Resets the injected flag and caches the Drive delegates; Driver marked Shutdown; `GameAppHost` clears its shutdown flag |
| `GameApp.Initialize` (`BeforeSceneLoad`) | `PlayerLoopDriver.Initialize()` injects + installs the built-in core hooks, then materializes `GameAppHost`; injection is verified against the actual loop — if any marker is missing the injected flag stays false and a warning is logged |
| `AfterSceneLoad` | Self-heal: if a third party rebuilt the loop from default at or before `BeforeSceneLoad` (including same-phase but later than this framework) and wiped the markers, re-inserts them based on the live loop and logs a warning. **The phase must not move earlier than the injection point** — injection happens at `BeforeSceneLoad`, and before it `s_Injected` is always false, so the guard's first line returns and the check never runs |
| `GameApp.Shutdown` / exit Play | Broadcast Destroy → clear registry → `RestoreDefault()` takes our three markers out item by item (third-party injections stay as they were) → destroy host (except on the application-quit path: the engine tears the host down with the scene, so the explicit Destroy is skipped) |
| After ECS resets PlayerLoop | Rebuilds up to `AfterSceneLoad` are re-inserted by the self-heal; later ones (e.g. at the end of a custom bootstrap) need `PlayerLoopInjector.Reinject()` after they complete |

## DI (VContainer etc.)

Register handler implementations as services; after composition root `InitializeAsync`:

```csharp
var system = container.Resolve<MySystem>();
GameApp.AddUpdateHandler(system);
```

Unregister with `GameApp.RemoveUpdateHandler(system)`, or `GameApp.AddFrameHandler` /
`RemoveFrameHandler` for a class implementing several stage interfaces.

## Compatibility

- **UniTask**: injection bases on the current loop and never overwrites UniTask's systems. Shutdown goes through `RestoreDefault()`, so UniTask's injection **survives intact** and its `await` continuations still resume after the framework closes — the exit path's async save flush and the debug window's `Shutdown (Restart)` (which calls `LoadScene` right after `GameApp.Shutdown()`) both depend on this. A project that wants the engine default loop back pastes it itself with `PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop())`, keeping in mind that UniTask's pump does not come back along with it.
- **ECS/DOTS**: Entities may reset the PlayerLoop at `BeforeSceneLoad` — rebuilds happening before/early relative to the self-heal check are re-inserted automatically; if the reset runs later (e.g. a custom bootstrap), call `PlayerLoopInjector.Reinject()` after initialization completes.
- **ApplicationPause**: Unity exposes no pure C# event — `GameAppHost.OnApplicationPause` forwards to `PlayerLoopDriver.RaiseApplicationPause`. Subscriptions live in the static table, so rebuilding the host restores dispatch.
- **Gizmos**: same pattern — `GameAppHost.OnDrawGizmos(Selected)` forwards to `PlayerLoopDriver.RaiseDrawGizmos(Selected)`. Only the editor has a dispatcher; in builds the table is never raised.
- **Coroutines**: `GameApp.StartCoroutine` goes through `GameAppHost.Instance`; destroying the host only kills running coroutines, never subscriptions.

## Editor Debugging

Menu **Window → PlayerLoop Debugger** (an Odin window):

- **Status**: markers found vs `IsInjected`, whether the driver is `Shutdown` or `Driving`, and `GameTime` next to the engine `Time` — if the frame numbers stop tracking each other, the driver is not running
- **Stage table**: whether each of Update / FixedUpdate / LateUpdate still has its marker in the loop (including its index inside the parent phase list), plus the handler and callback counts
- **Loop tree**: collapsible, filterable by type name, and a markers-only view. Moirai markers render green; a third-party pump (has a delegate, is not ours — UniTask and friends) renders blue, since keeping those is exactly what the shutdown path is for. `Locate Moirai` expands the three injection points in one click
- **Buttons**: Refresh / Ensure Injected / Reinject / Restore Default, mapping one-to-one onto `PlayerLoopInjector`; `Restore Default` is the same call the shutdown path makes (our markers only)
- **Auto refresh**: re-scans the loop on a configurable interval

---
[« Documentation Index](Index.md) · [GameApp](GameApp.md)
