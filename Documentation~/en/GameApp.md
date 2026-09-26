# GameApp Frame Driver and Host

> Unity lifecycle proxy for non-MonoBehaviour code: coroutine hosting, frame update injection, and Unity event injection.

## Architecture Change (Important)

Frame subscriptions moved from a MonoBehaviour host to **`PlayerLoopDriver`** (`Runtime/Core/GameApp/PlayerLoop`, namespace `Moirai.Atropos`, type is `internal` — game code reaches it through the `GameApp` facade):

- Subscriptions live in a **static registry**, not on any GameObject
- Scene loads / unexpected host destruction **do not lose** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy`/`Gizmos`/`Pause` listeners
- Prior bug: listeners lived on a hidden Mono host instance events, which could be destroyed before the initial scene load, dropping every subscription

`GameApp` itself now holds **no MonoBehaviour at all** (no nested host, no GameObject field). The Unity messages that only dispatch on a MonoBehaviour — coroutines, `OnDrawGizmos(Selected)`, `OnApplicationPause` — are collected in a single `SingletonMono_Persistent` host, `GameAppHost` (`Runtime/Core/GameApp/GameAppHost.cs`). It only **forwards**; the subscriptions stay in the Driver's static tables, so destroying or rebuilding the host loses nothing.

See [PlayerLoopDriver](PlayerLoopDriver.md) for details.

## Core Features

- Coroutine hosting: `GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- Frame updates: `GameApp.AddUpdateListener` (Action) plus `GameApp.AddUpdateHandler` / `AddFrameHandler` (interface handlers, `IPlayerLoopPriority` aware) all write **synchronously** into the driver's registries (no `UniTask.Yield` deferral)
- Unity events: `AddDestroyListener` (broadcast on Shutdown), `AddOnApplicationPauseListener`, Gizmos APIs
- Runtime switches: `FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` carry live engine state (`GameAppSettings` is only the boot default); pausing is reference counted via `PauseGame` / `ResumeGame` — see [Pause And Speed Semantics](#pause-and-speed-semantics)
- Clean shutdown: `GameApp.Shutdown` clears the Driver registry, removes this framework's PlayerLoop systems (UniTask and other third-party injections stay), unwinds any unmatched pause and releases the host (except on the application-quit path: the engine tears the host down with the scene, so the explicit Destroy is skipped)

## Core Types

| Class/Interface | Description |
|---------|------|
| `GameApp` | Framework entry static facade: lifecycle, coroutines, subscription APIs (no Mono members) |
| `PlayerLoopDriver` | Mono-free logic driver (static) |
| `IUpdateHandler` etc. | Interface handlers — preferred for new code |
| `GameAppHost` | Coroutine / Gizmos / Pause host (`SingletonMono_Persistent`) |

## Quick Start

```csharp
// Callback style (compatible API)
GameApp.AddUpdateListener(OnUpdate);
GameApp.AddFixedUpdateListener(OnFixedUpdate);
GameApp.AddLateUpdateListener(OnLateUpdate);
GameApp.RemoveUpdateListener(OnUpdate);

// Interface style (recommended): carries state, DI-injected, orderable via IPlayerLoopPriority
GameApp.AddUpdateHandler(myUpdateHandler);
GameApp.RemoveUpdateHandler(myUpdateHandler);

// A class implementing several stage interfaces, registered in one call
GameApp.AddFrameHandler(myMultiStageSystem);
GameApp.RemoveFrameHandler(myMultiStageSystem);

// Coroutines
Coroutine co = GameApp.StartCoroutine(SomeRoutine());
GameApp.StopCoroutine(co);

// Destroy (fired on GameApp.Shutdown)
GameApp.AddDestroyListener(OnShutdown);
```

## Registration Timing

`Add*Listener` / `Add*Handler` / `AddFrameHandler` write **synchronously** into static tables and are safe at any init stage (including `SubsystemRegistration`). Driving starts after PlayerLoop injection.

## Runtime State Is Not Config State

`m_FrameRate` / `m_GameSpeed` / `m_RunInBackground` / `m_NeverSleep` on `GameAppSettings` are **boot defaults only**:

1. `GameAppSettings.Initiation` (`BeforeSceneLoad`) pushes the asset values into the engine once
2. `GameApp.Initialize` immediately reads the **live engine state** back (`SeedRuntimeFromEngine`) into `GameApp`'s own static fields
3. From then on `FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` touch only those fields and the engine — `GameApp` never dereferences **or writes back** the config asset

So `GameApp.FrameRate = 60` in the editor no longer leaves the shared ScriptableObject under `Resources/` dirty across Play sessions, and predicates read live state rather than configured intent.

## Pause And Speed Semantics

`GameSpeed` is the single source of truth for the **expected** speed (it maps to `Time.timeScale`); `PauseGame` is a **reference count**. The two are decoupled:

| Call | Behavior |
|------|----------|
| `PauseGame()` | Count +1; presses `Time.timeScale` to 0 only on the 0→1 transition |
| `ResumeGame()` | Count -1; replays the current `GameSpeed` into `Time.timeScale` **only when it reaches 0**. At count 0 it is a **no-op** — it never pulls back some stored default |
| `GameSpeed = v` (while paused) | Updates the resume target only; `timeScale` stays 0 — **pause outranks speed**; negative values clamp to 0 |
| `ResetGameSpeed()` | Sets the expected speed to 1; calling it while paused does **not** resume |

A popup, a background switch and a cutscene may each `PauseGame`; each must `ResumeGame`, and only the last one restores speed.

**Read what you actually mean** (in 1.0.2 `IsGamePaused` was `GameSpeed <= 0`; they are now decoupled):

| Question | Read |
|----------|------|
| Did anyone ask to pause? | `GameApp.IsGamePaused` (count non-zero; **a 0x freeze is not a pause**) |
| Is time actually stopped? | `Time.timeScale <= 0f` (or `GameApp.GameSpeed <= 0f`) |
| How deep is the pause stack? | `GameApp.PauseDepth` (framework-internal; the `Other/Game Settings` debug panel shows it as the depth) |

## Runtime State Contract After Shutdown

`GameApp.Shutdown` (idempotent) **unwinds any unmatched pause**: the count goes to 0 and `GameSpeed` is replayed. Leaving it running would freeze the frames that still have to run after shutdown (scene restart / async flush on exit) at `timeScale = 0`, and the next `Initialize` would seed `GameSpeed = 0` from that frozen engine state — with `ResumeGame` a no-op at count 0, no API could recover the speed afterwards.

After shutdown:

- **Runtime properties keep working**: `FrameRate` / `GameSpeed` / `RunInBackground` / `NeverSleep` / `IsGamePaused` / `PauseGame` / `ResumeGame` do not check `IsShutdown` and never throw — they are a facade over engine state, not framework state. Writes take effect on the engine immediately and become the baseline re-seeded by the next `Initialize`.
- **Frame subscriptions and coroutines make no promise**: the registries are cleared and the host released, so `Add*Listener` / `Add*Handler` registrations are never driven, and `StartCoroutine` may fail to reach a host (returns `null` and warns).

## Notes

- Listeners hold strong references — always unregister; `Shutdown` clears the Driver. Prefer keeping the `GameApp.Subscription` returned by `Add*Listener` and calling `Dispose()`: `Remove*Listener(Action)` compares by delegate equality, so **a freshly written lambda with the same body will not remove it** (that is a new delegate instance) and the subscription — closure captures included — survives until `Shutdown`.
- Gizmos APIs only have a dispatcher in the editor; subscriptions still go to the Driver's static table, so registering in a build is harmless.
- Do not manually destroy `[GameAppHost]`; it is lazily rebuilt through `Instance`, which restores dispatch only — subscriptions were never lost.
- Calling `GameApp.StartCoroutine` from a background thread throws via `SingletonMono<T>.Instance`: the host must be materialized on the main thread first (`GameApp.Initialize` guarantees this).
- Exiting Play removes only the three PlayerLoop systems this framework injected; UniTask and other third-party injections are left intact. Re-entering Play re-injects via `GameApp.Initialize`.

---
[« Documentation Index](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
