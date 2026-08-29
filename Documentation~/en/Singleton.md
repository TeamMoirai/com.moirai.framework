# Singleton System

> Thread-safe singleton base family: pure C# singletons (volatile double-checked locking), MonoBehaviour singletons (scene lookup + main-thread materialization), and register-style singletons.

Namespace: `Moirai.Atropos` (`Runtime/Core/Singleton/`)

## Type Overview

| Type | Target | Thread-safe | Lifecycle callbacks | Use case |
|------|--------|-------------|---------------------|----------|
| `Singleton<T>` | Pure C# class | ✅ always | `OnInit()` / `OnShutdown()` | Global objects without Unity dependencies |
| `SingletonMono<T>` | MonoBehaviour | ✅ (after materialization) | `OnInit()` / `OnShutdown()` | Scene-component managers |
| `SingletonMono_Persistent<T>` | MonoBehaviour | ✅ (after materialization) | Same + forced `DontDestroyOnLoad` | Cross-scene global scripts |
| `SingletonRegister<T>` | Any `new()` type | ✅ always | None | Existing types you cannot re-base |
| `SingletonRegisterMono<T>` | Any MonoBehaviour | ✅ (after materialization) | None | Inheritance-free Mono singletons |
| `ReferencedScriptableObject<T>` | ScriptableObject | — (main thread) | `OnReferenced()` / `OnDisposed()` | Weak-reference registry of live instances |

## Singleton\<T\> — Pure C# Singleton

```csharp
public class UIConfigManager : Singleton<UIConfigManager>
{
    public int CurrentThemeIndex { get; set; }

    protected override void OnInit() { /* called once after first Instance access */ }
    protected override void OnShutdown() { /* called once before Dispose completes */ }
}

// Any thread:
UIConfigManager.Instance.CurrentThemeIndex = 2;
if (UIConfigManager.IsValid) { /* ... */ }
UIConfigManager.Instance.Dispose(); // release; next access re-creates and re-initializes
```

### Thread Model

- **Fast path**: once materialized, a single volatile read — lock-free, allocation-free, safe from background threads.
- **Lazy creation**: volatile read + Double-Checked Locking; concurrent first access creates exactly one instance and initializes it exactly once.
- **`new()` constraint**: the constraint requires a public constructor; constructing a derived type directly with `new` in the editor immediately logs a `LogUtility.Error` (editor-only guard, zero runtime cost).

### Initialization Contract (publish, then initialize)

The instance is written to the static field **before** `OnInit()` runs:

- Recursive `Instance` access *inside* `OnInit()` (same thread) returns **the same instance being initialized** (no deadlock, no double creation);
- Concurrent first access from other threads blocks until `OnInit()` completes.

`OnInit()` / `OnShutdown()` both execute under the creation lock — keep them lightweight.

### Disposal Contract

`Dispose()` (implements `IDisposable`) is idempotent with a stale-instance guard:

- Only the current live instance (`s_Instance == this`) triggers `OnShutdown()` and clears the static reference;
- Calling it on a released/replaced **stale instance** is a no-op — it **cannot kill the live instance**;
- Accessing `Instance` after disposal creates a fresh instance and re-initializes it;
- During `OnShutdown()`, `Instance` still returns the instance being shut down (no surrogate is created).

## SingletonMono\<T\> — MonoBehaviour Singleton

```csharp
public class AudioManager : SingletonMono<AudioManager>
{
    protected override void OnInit() { /* called from the winning instance's Awake */ }
    protected override void OnShutdown() { /* called before the instance is destroyed */ }
}

// Main thread:
AudioManager.Instance.PlayBgm("main");
if (AudioManager.IsValid) { /* ... */ }
AudioManager.TryGetInstance()?.PlayBgm("main"); // safe null when no instance
```

### Materialization & Thread Model

| Stage | Behavior |
|-------|----------|
| Play mode · main thread · no instance | Finds an existing scene instance → creates a `[TypeName]_AutoCreated` GameObject if none |
| Play mode · background thread · materialized | Volatile-read atomic fast path (no Unity API calls) |
| Play mode · background thread · not materialized | Throws `GameException` (fail-fast) — background threads may only access after main-thread warm-up |
| Edit mode · main thread | **Find only, never creates** (avoids writing transient objects into the scene); returns null when absent |
| Shutdown window (app quit / play stopped) | `Instance` returns null and refuses re-creation; `IsValid` / `TryGetInstance()` reflect the state |

Derived classes accessed from background threads should warm up once on the main thread during startup (see the `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` pattern in `MainThreadDispatcher.BootstrapOnPlay`).

### Multi-instance Policy

| Inspector option | Behavior |
|------------------|----------|
| `m_Persistent` | Winner is marked `DontDestroyOnLoad`, survives scene changes |
| `m_Replaceable` | The most recently created instance wins and destroys the old one (eg: background music); default is first-come-first-served, latecomers are destroyed |

Extend initialization/shutdown by overriding `OnInit()` / `OnShutdown()`; `Awake()` / `OnDestroy()` are non-virtual lifecycle skeletons and must not be overridden.

### Shutdown Flag & Domain Reload

`OnDestroy` keeps the shutdown flag set during app quit to prevent resurrection mid-teardown; regular destruction during play (scene switches) resets the flag so the next access re-materializes. With Domain Reload disabled in the editor, static state survives across play sessions — derived classes needing cross-session resets should provide their own `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hook (see `MainThreadDispatcher.ResetStatics()`).

## SingletonRegister / SingletonRegisterMono — Register-style Singletons

```csharp
// No inheritance required: register any type directly
SingletonRegister<LegacyConfig>.Instance.Load();

// Inheritance-free Mono singleton (no scene lookup / multi-instance policy / lifecycle callbacks)
SingletonRegisterMono<FxPlayer>.Instance.Play("explosion");
```

`SingletonRegisterMono<T>` materialization is likewise main-thread only (off-thread access throws `GameException`). Use `SingletonMono<T>` when you need lookup, multi-instance resolution, or lifecycle callbacks.

---
[« Back to Main README](../../README_EN.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
