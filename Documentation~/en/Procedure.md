# Procedure Service

> Self-contained game flow management: models startup, hot update, preload, and other phases as switchable procedure states.

The Procedure service (`ProcedureService`) is a self-contained state machine — it maintains an internal state dictionary and current state without depending on any external state machine service. Each game phase (startup, checking for updates, downloading resources, loading assemblies, preloading, etc.) is a `ProcedureBase` state. Available procedures and the entry procedure are configured via `ProcedureSettings`. `GameApp.Awake` automatically reflects, instantiates, and starts them, requiring no manual bootstrap code. Access via `GameApp.Services.GetRequiredService<IProcedureService>()` (`IProcedureService`).

## Core Features

- Self-contained state machine: `ProcedureService` maintains an internal `Dictionary<Type, ProcedureBase>` state dictionary, drives its own `Tick` polling via `IServiceTickable`, and does not depend on any external FSM service
- Configuration-driven startup: `ProcedureSettings` records available procedure types and the entry procedure; `GameApp.Awake` automatically calls `ProcedureSettings.StartProcedure()` to instantiate and start
- `[ProcedureLauncher]` attribute: Only `ProcedureBase` subclasses marked with this attribute are scanned and included by `ProcedureSettings` (automatically scanned on editor Reset; defaults to the procedure whose name contains `ProcedureLaunch` as the entry)
- Dual switching entry points: Inside a procedure, use the base class method `ChangeState<T>()` (parameterless, via internal `Owner` reference); externally (e.g., from the hot update layer), use `GameApp.Services.GetRequiredService<IProcedureService>().ChangeState<T>()`
- Supports runtime reconstruction: `RestartProcedure` cleans up old states, rebuilds with a new procedure list, and starts with the first procedure

## Core Types

Namespace: `Moirai.Atropos.Procedure`

| Class/Interface | Description |
|---------|------|
| `IProcedureService` | Procedure manager interface: `Initialize` / `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` / `RestartProcedure` and `CurrentProcedure`, `CurrentProcedureTime`; accessed via `GameApp.Services.GetRequiredService<IProcedureService>()` |
| `ProcedureService` | Service implementation (`ServiceBase, IProcedureService, IServiceTickable`, `Priority = -2`), holds internal state dictionary and tick-driven polling |
| `ProcedureBase` | Procedure base class (standalone abstract class), provides `OnInit / OnEnter / OnUpdate / OnLeave / OnDestroy` parameterless lifecycle methods and `ChangeState<T>()` switching |
| `ProcedureSettings` | Framework settings (panel name "Procedure Settings"): serialized list of available procedure type names and the entry procedure type name; static `StartProcedure()` is responsible for reflecting and building the flow |
| `ProcedureLauncherAttribute` | Class-level attribute, marks `ProcedureBase` subclasses that can be included in the procedure system |
| `ProcedureEvents` / `IProcedureEvent` | Procedure-related event marker interface (`public interface IProcedureEvent { }`), for business-specific procedure event extensions |

## Quick Start

Define a procedure and mark it for inclusion (a real example from `Templates~/@Requirements/Scripts/GameBase/Procedure`):

```csharp
using Moirai.Atropos;
using Moirai.Atropos.Procedure;

// Procedure base class: must be marked with [ProcedureLauncher] to appear in ProcedureSettings' available list
[ProcedureLauncher]
public abstract class ProcedurePremainBase : ProcedureBase
{
    public abstract bool UseNativeDialog { get; }

    protected readonly IResourceService _resourceService = GameServices.Provider?.GetService<IResourceService>();
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
ProcedureBase current = GameApp.Services.GetRequiredService<IProcedureService>().CurrentProcedure;
float seconds = GameApp.Services.GetRequiredService<IProcedureService>().CurrentProcedureTime;

// Query / get a procedure instance
bool has = GameApp.Services.GetRequiredService<IProcedureService>().HasProcedure<ProcedureSplash>();
ProcedureBase proc = GameApp.Services.GetRequiredService<IProcedureService>().GetProcedure<ProcedureSplash>();

// Force a switch from outside (e.g., jump logic in hot update code)
GameApp.Services.GetRequiredService<IProcedureService>().ChangeState<ProcedurePreload>();
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
bool ok = GameApp.Services.GetRequiredService<IProcedureService>().RestartProcedure(
    new ProcedureLaunch(),
    new ProcedureInitPackage(),
    new ProcedurePreload());
```

## Notes

- `Initialize` must be called before using procedures; otherwise, `StartProcedure` / `ChangeState` etc. will throw `GameException("You must initialize procedure first.")`. In standard projects, this is done automatically by `ProcedureSettings.StartProcedure()` during `GameApp.Awake`.
- The entry procedure is selected on the editor side by the Reset logic, which picks the first type whose name contains `ProcedureLaunch`. If the entry procedure class is renamed, refresh via Reset in the `ProcedureSettings` panel.
- Procedure classes must have a parameterless constructor (`ProcedureSettings` uses `Activator.CreateInstance` for reflection-based instantiation). Do not use constructor injection in procedure classes.
- Procedure instances are held by `ProcedureService` and live for a long time; do not cache short-lived objects in them. Place per-frame logic in `OnUpdate`, and for time-consuming asynchronous operations, start them in `OnEnter` and poll for completion in `OnUpdate` (refer to the template's `_initResourcesComplete` pattern).
- `ProcedureBase.OnUpdate` has two time parameters (`elapseSeconds` / `realElapseSeconds`). Ensure the signature is consistent when overriding.
- `ChangeState<T>()` is a parameterless method — it delegates through the internal `Owner` (`IProcedureService`) reference held by `ProcedureBase`, no need to pass the service instance when calling.

---
[« Back to Main README](../../README_EN.md) · [Resource](Resource.md)
