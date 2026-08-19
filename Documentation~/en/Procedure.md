# Procedure Service

> FSM-based game flow management: models startup, hot update, preload, and other phases as switchable procedure states.

The Procedure service (`ProcedureService`) is built on top of the [FSM](FSM.md) finite state machine: internally, it uses `IFSMService.CreateFSM` to create a state machine whose owner is `IProcedureService`. Each game phase (startup, checking for updates, downloading resources, loading assemblies, preloading, etc.) is a `ProcedureBase` state. Available procedures and the entry procedure are configured via `ProcedureSettings`. `GameApp.Awake` automatically reflects, instantiates, and starts them, requiring no manual bootstrap code. Access via `GameApp.Procedure` (`IProcedureService`).

## Core Features

- Based on FSM: Procedures are states, reusing the full lifecycle of `FSMState<T>` and the `ChangeState` switching mechanism
- Configuration-driven startup: `ProcedureSettings` records available procedure types and the entry procedure; `GameApp.Awake` automatically calls `ProcedureSettings.StartProcedure()` to instantiate and start
- `[ProcedureLauncher]` attribute: Only `ProcedureBase` subclasses marked with this attribute are scanned and included by `ProcedureSettings` (automatically scanned on editor Reset; defaults to the procedure whose name contains `ProcedureLaunch` as the entry)
- Dual switching entry points: Inside a procedure, use the base class method `ChangeState<T>(procedureOwner)`; externally (e.g., from the hot update layer), use `GameApp.Procedure.ChangeState<T>()`
- Supports runtime reconstruction: `RestartProcedure` destroys the old state machine, rebuilds it with a new procedure list, and starts with the first procedure

## Core Types

Namespace: `Moirai.Atropos.Procedure`

| Class/Interface | Description |
|---------|------|
| `IProcedureService` | Procedure manager interface: `Initialize` / `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` / `RestartProcedure` and `CurrentProcedure`, `CurrentProcedureTime`; accessed via `GameApp.Procedure` |
| `ProcedureService` | Service implementation (`Service, IProcedureService`, `Priority = -2`), holds an internal `IFSM<IProcedureService>` state machine |
| `ProcedureBase` | Procedure base class, inherits `FSMState<IProcedureService>`, provides lifecycle methods `OnInit / OnEnter / OnUpdate / OnExit / OnDestroy` |
| `ProcedureSettings` | Framework settings (panel name "Procedure Settings"): serialized list of available procedure type names and the entry procedure type name; static `StartProcedure()` is responsible for reflecting and building the flow |
| `ProcedureLauncherAttribute` | Class-level attribute, marks `ProcedureBase` subclasses that can be included in the procedure system |
| `ProcedureEvents` / `IProcedureEvent` | Procedure-related event marker interface (`public interface IProcedureEvent { }`), for business-specific procedure event extensions |

Dependent FSM types (namespace `Moirai.Atropos.FSM`): `FSMState<T>` (state base class and `ChangeState` switching), `IFSM<T>` / `IFSMService` (state machine and state machine manager interfaces, the latter accessed via `GameApp.FSM`).

## Quick Start

Define a procedure and mark it for inclusion (a real example from `Templates~/@Requirements/Scripts/GameBase/Procedure`):

```csharp
using Moirai.Atropos;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Procedure;

// Procedure base class: must be marked with [ProcedureLauncher] to appear in ProcedureSettings' available list
[ProcedureLauncher]
public abstract class ProcedurePremainBase : ProcedureBase
{
    public abstract bool UseNativeDialog { get; }

    protected readonly IResourceService _resourceService = ServiceSystem.GetService<IResourceService>();
}

// Concrete procedure
public class ProcedureLaunch : ProcedurePremainBase
{
    public override bool UseNativeDialog => true;

    protected override void OnEnter(IFSM<IProcedureService> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        // Startup phase initialization (in the template, this initializes the hot update UI: LauncherMgr.Initialize())
    }

    protected override void OnUpdate(IFSM<IProcedureService> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        // Switch to the next phase within the procedure (protected method provided by FSMState<T>)
        ChangeState<ProcedureInitPackage>(procedureOwner);
    }
}
```

Querying and switching from outside a procedure:

```csharp
// Current procedure and elapsed time
ProcedureBase current = GameApp.Procedure.CurrentProcedure;
float seconds = GameApp.Procedure.CurrentProcedureTime;

// Query / get a procedure instance
bool has = GameApp.Procedure.HasProcedure<ProcedureSplash>();
ProcedureBase proc = GameApp.Procedure.GetProcedure<ProcedureSplash>();

// Force a switch from outside (e.g., jump logic in hot update code)
GameApp.Procedure.ChangeState<ProcedurePreload>();
```

## Configuration and Extensions

### Relationship with FSM

`ProcedureService.Initialize(IFSMService fsmService, params ProcedureBase[] procedures)` internally calls `fsmService.CreateFSM(this, procedures)` to create a single procedure state machine; `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` delegate to the state machine's `Start` / `HasState` / `ChangeState` / `GetState` respectively. The procedure lifecycle is the state lifecycle:

| Procedure Callback | Signature | Description |
|----------|------|------|
| `OnInit` | `(IFSM<IProcedureService>)` | Called once after the state machine is created |
| `OnEnter` | `(IFSM<IProcedureService>)` | Called when entering the procedure |
| `OnUpdate` | `(IFSM<IProcedureService>, float elapseSeconds, float realElapseSeconds)` | Polled every frame (logic/real elapsed time) |
| `OnExit` | `(IFSM<IProcedureService>, bool isShutdown)` | Called when leaving the procedure (includes state machine shutdown flag) |
| `OnDestroy` | `(IFSM<IProcedureService>)` | Called when the state is destroyed |

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
// Destroy the current state machine, rebuild with a new procedure list, and start with the first procedure (returns success status)
bool ok = GameApp.Procedure.RestartProcedure(
    new ProcedureLaunch(),
    new ProcedureInitPackage(),
    new ProcedurePreload());
```

## Notes

- `Initialize` must be called before using procedures; otherwise, `StartProcedure` / `ChangeState` etc. will throw `GameException("You must initialize procedure first.")`. In standard projects, this is done automatically by `ProcedureSettings.StartProcedure()` during `GameApp.Awake`.
- The entry procedure is selected on the editor side by the Reset logic, which picks the first type whose name contains `ProcedureLaunch`. If the entry procedure class is renamed, refresh via Reset in the `ProcedureSettings` panel.
- Procedure classes must have a parameterless constructor (`ProcedureSettings` uses `Activator.CreateInstance` for reflection-based instantiation). Do not use constructor injection in procedure classes.
- Procedure instances are held by the state machine and live for a long time; do not cache short-lived objects in them. Place per-frame logic in `OnUpdate`, and for time-consuming asynchronous operations, start them in `OnEnter` and poll for completion in `OnUpdate` (refer to the template's `_initResourcesComplete` pattern).
- `ProcedureBase.OnUpdate` has two time parameters (`elapseSeconds` / `realElapseSeconds`). Ensure the signature is consistent when overriding.

---
[« Back to Main README](../../README_EN.md) · [FSM](FSM.md) · [Resource](Resource.md)