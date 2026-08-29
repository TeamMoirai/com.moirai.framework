# Procedure Service

> Self-contained game flow management: models startup, hot update, preload, and other phases as switchable procedure states.

The Procedure service (`ProcedureService`) is a self-contained state machine — it maintains an internal state dictionary and current state without depending on any external state machine service. Each game phase (startup, checking for updates, downloading resources, loading assemblies, preloading, etc.) is a `ProcedureBase` state. Available procedures and the entry procedure are configured via `ProcedureServiceSettings`. `GameApp.Awake` automatically reflects, instantiates, and starts them, requiring no manual bootstrap code. Access via the `ProcedureService` static facade.

## Core Features

- Self-contained state machine: `ProcedureService` maintains an internal `Dictionary<Type, ProcedureBase>` state dictionary, drives its own `Tick` polling via `IServiceTickable`, and does not depend on any external FSM service
- Configuration-driven startup: `ProcedureServiceSettings` records available procedure types and the entry procedure; `GameApp.Awake` automatically calls `ProcedureServiceSettings.StartProcedure()` to instantiate and start
- `[ProcedureLauncher]` attribute: Only `ProcedureBase` subclasses marked with this attribute are scanned and included by `ProcedureServiceSettings` (automatically scanned on editor Reset; defaults to the procedure whose name contains `ProcedureLaunch` as the entry)
- Dual switching entry points: Inside a procedure, use the base class method `ChangeState<T>()` (parameterless, via internal `Owner` reference); externally (e.g., from the hot update layer), use `ProcedureService.ChangeState<T>()`
- Supports runtime reconstruction: `RestartProcedure` cleans up old states, rebuilds with a new procedure list, and starts with the first procedure

## Core Types

Namespace: `Moirai.Atropos.Procedure`

| Class/Interface | Description |
|---------|------|
| `ProcedureService` | Static facade (`[HandlerHost]`, `IServiceTickable`): `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` / `RestartProcedure` and `CurrentProcedure`, `CurrentProcedureTime`; all static APIs forward through the `Handler` property (fail-fast: lazily initialized when not ready, throws if the default factory is missing, never silently degrades) |
| `ProcedureServiceHandler` | Handler abstract base class defining the procedure state-machine backend contract; `ProcedureBase` subclasses call back into this handler via the internal `Owner` reference |
| `DefaultProcedureHandler` | Default implementation, holds the internal state dictionary and tick-driven polling |
| `ProcedureBase` | Procedure base class (standalone abstract class), provides `OnInit / OnEnter / OnUpdate / OnLeave / OnDestroy` parameterless lifecycle methods and `ChangeState<T>()` switching |
| `ProcedureServiceSettings` | Framework settings (panel name "Procedure Settings"): serialized list of available procedure type names and the entry procedure type name; static `StartProcedure()` is responsible for reflecting and building the flow |
| `ProcedureLauncherAttribute` | Class-level attribute, marks `ProcedureBase` subclasses that can be included in the procedure system |
| `ProcedureEvents` / `IProcedureEvent` | Procedure-related event marker interface (`public interface IProcedureEvent { }`), for business-specific procedure event extensions |

## Quick Start

Define a procedure and mark it for inclusion (a real example from `Templates~/@Requirements/Scripts/GameBase/Procedure`):

```csharp
using Moirai.Atropos;
using Moirai.Atropos.Procedure;

// Procedure base class: must be marked with [ProcedureLauncher] to appear in ProcedureServiceSettings' available list
[ProcedureLauncher]
public abstract class ProcedurePremainBase : ProcedureBase
{
    public abstract bool UseNativeDialog { get; }
}

// Concrete procedure
public class ProcedureLaunch : ProcedurePremainBase
{
    public override bool UseNativeDialog => true;

    protected override void OnEnter()
    {
        base.OnEnter();
        // Startup phase initialization (in the template, this initializes the hot update UI: LauncherMgr.Initialize())
    }

    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        // Switch to the next phase within the procedure (protected method, parameterless)
        ChangeState<ProcedureInitPackage>();
    }
}
```

Querying and switching from outside a procedure:

```csharp
// Current procedure and elapsed time
ProcedureBase current = ProcedureService.CurrentProcedure;
float seconds = ProcedureService.CurrentProcedureTime;

// Query / get a procedure instance
bool has = ProcedureService.HasProcedure<ProcedureSplash>();
ProcedureBase proc = ProcedureService.GetProcedure<ProcedureSplash>();

// Force a switch from outside (e.g., jump logic in hot update code)
ProcedureService.ChangeState<ProcedurePreload>();
```

## Configuration and Extensions

### Procedure Lifecycle

`ProcedureService.Initialize(params ProcedureBase[] procedures)` injects the `Owner` reference into each state and calls `OnInit()`; `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` operate directly on the internal dictionary. The procedure lifecycle:

| Procedure Callback | Signature | Description |
|----------|------|------|
| `OnInit` | `()` | Called once per state during `Initialize` |
| `OnEnter` | `()` | Called when entering the procedure |
| `OnUpdate` | `(float elapseSeconds, float realElapseSeconds)` | Polled every frame (logic/real elapsed time), driven by `IServiceTickable.Tick` |
| `OnLeave` | `(bool isShutdown)` | Called when leaving the procedure (`isShutdown` = `true` indicates exit due to service shutdown) |
| `OnDestroy` | `()` | Called when the state is destroyed |

### Startup Chain Reference

`Templates~/@Requirements/Scripts/GameBase/Procedure` provides a complete startup procedure template, with a typical chain of:

```
ProcedureLaunch -> ProcedureSplash -> ProcedureInitPackage -> ProcedureInitResources
-> ProcedureCreateDownloader -> ProcedureDownloadFile -> ProcedureDownloadOver
-> ProcedureClearCache -> ProcedureLoadAssembly -> ProcedurePreload -> ProcedurePrepare4Entrance
```

`ProcedureInitResources` demonstrates integration with the Resource service: it calls `_resourceService.RequestPackageVersionAsync()` to get the remote manifest version, `UpdatePackageManifestAsync(packageVersion)` to update the manifest, and then decides whether to proceed with the download flow or directly preload based on the play mode (`EPlayMode.HostPlayMode` / `WebPlayMode`, whether `UpdatableWhilePlaying` is enabled).

### Restarting Procedures

```csharp
// Clean up old states, rebuild with a new procedure list, and start with the first procedure (returns success status)
bool ok = ProcedureService.RestartProcedure(
    new ProcedureLaunch(),
    new ProcedureInitPackage(),
    new ProcedurePreload());
```

## Notes

- `Initialize` must be called before using procedures; otherwise, `StartProcedure` / `ChangeState` etc. will throw `GameException("You must initialize procedure first.")`. In standard projects, this is done automatically by `ProcedureServiceSettings.StartProcedure()` during `GameApp.Awake`.
- The entry procedure is selected on the editor side by the Reset logic, which picks the first type whose name contains `ProcedureLaunch`. If the entry procedure class is renamed, refresh via Reset in the `ProcedureServiceSettings` panel.
- Procedure classes must have a parameterless constructor (`ProcedureServiceSettings` uses `Activator.CreateInstance` for reflection-based instantiation). Do not use constructor injection in procedure classes.
- Procedure instances are held by `ProcedureService` and live for a long time; do not cache short-lived objects in them. Place per-frame logic in `OnUpdate`, and for time-consuming asynchronous operations, start them in `OnEnter` and poll for completion in `OnUpdate` (refer to the template's `_initResourcesComplete` pattern).
- `ProcedureBase.OnUpdate` has two time parameters (`elapseSeconds` / `realElapseSeconds`). Ensure the signature is consistent when overriding.
- `ChangeState<T>()` is a parameterless method — it delegates through the internal `Owner` (`ProcedureServiceHandler`) reference held by `ProcedureBase`, no need to pass the service instance when calling.

---
[« Back to Main README](../../README_EN.md) · [Resource](Resource.md)
