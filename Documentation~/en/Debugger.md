# Debugger Service

> Runtime debugger: an in-game UI Toolkit panel — virtualized console log stream, runtime environment info, memory & object pool profiling, an always-on stats HUD, and a one-line fluent debug panel builder.

The Debugger service exposes window registration and polling through the static `DebuggerService` facade; the handler lazily spawns a `DebuggerRuntimeHost` (`DontDestroyOnLoad`) on its first Tick — **no scene components or assets need to be placed manually**. At runtime a floating FPS entry appears in the top-left corner (draggable, edge-snapping, colored by worst log level); clicking it expands the full window with a sidebar tree navigation + content area, search filtering, header dragging and bottom-right resizing. All panels (`PanelSettings` / `UIDocument`) are constructed at runtime, depending only on the in-package `Resources/DebuggerPanelSettings.asset` (with the default theme reference embedded).

## Core Features

- **UI Toolkit rendering**: no IMGUI per-frame redraw — the console uses a virtualized `ListView` (makeItem/bindItem render visible rows only); info windows rebuild on a 0.25s throttle
- **Console**: severity filter chips (incremental counters, zero-scan refresh), log search, scroll lock, stack detail with one-click copy
- **Info windows**: System / Environment / Screen / Graphics / Input (Input System devices & sensors) / Scene / Time / Quality / Path
- **Profiler windows**: Summary, Memory Summary, Memory details (All / Texture / Mesh / Material / Shader / AnimationClip / AudioClip / Font / TextAsset / ScriptableObject), Object Pool / GameObject Pool / Memory Pool, Service System (service container diagnostics)
- **Service debug panels**: Timer / Resource / Audio / Procedure / Localization — per-module debug views auto-registered by each service's OnInit (see the "Service Debug Panels" section)
- **Game app settings**: frame rate / game speed live controls and the local settings key-value list (`Other/Game Settings`, integrating the former GameAppEditor)
- **Stats HUD**: FPS / Tris / Batches / DrawCall / SetPass / Mono / Alloc / GfxDrv (`ProfilerRecorder` started on demand + 0.25s throttle)
- **Operations**: GameObject pool flush, asset unloading / GC, Time Scale slider, framework shutdown (None / Restart / Quit)
- **Fluent panel builder**: `RegisterPanel` registers a custom debug panel in one line (sliders / toggles / buttons / foldouts / read-only fields / progress bars, bound via Getter/Setter closures with 200ms polling refresh)
- **Thread-safe log capture**: `logMessageReceivedThreaded` enqueues from any thread; a pooled ring buffer drains on the main thread
- **Configurable activation policy**: always open / development builds only / editor only / always closed; the `-showdebugger` command line forces it on
- **Persistent layout**: entry & window position, size and scale remembered via `SettingUtility`; resolution-adaptive (1920×1080 reference)

## Core Types

Namespace: `Moirai.Atropos.Debugger`

| Class/Interface | Description |
|-----------------|-------------|
| `DebuggerService` | Static facade (`[HandlerHost]`, `IServiceTickable`): `ActiveWindow` / `ShowFullWindow` / `ActiveWindowType` / `WindowRegistry` / `LogCapture`; `RegisterDebuggerWindow` / `UnregisterDebuggerWindow` / `GetDebuggerWindow` / `SelectDebuggerWindow` / `RegisterPanel` / `RegisterDebugView` / `GetRecentLogs`. Forwards via `s_Handler` (silently degrades when unregistered — only explicit registration enables the service) |
| `DebuggerServiceHandler` | Abstract handler (contract): `ActiveWindow` / `ShowFullWindow` / `WindowRegistry` / `LogCapture` / `Tick` / the four registration methods; configuration carried by the `DebuggerServiceHandlerConfig` pure-data class |
| `DefaultDebuggerHandler` | Built-in backend: owns the window registry and log capture, resolves visibility by activation policy, lazily spawns the runtime host on first Tick |
| `DefaultDebuggerHandlerConfig` | Backend config (`[SerializeReference]` stored in the settings asset): `ConsoleCapacity` (ring buffer size, default 256) / `FpsUpdateInterval` / `StatsOverlayVisible` / `WindowOpacity` |
| `DebuggerServiceSettings` | Settings asset (`[FrameworkSetting]`): `ActiveWindowType` activation policy + `m_HandlerConfig` handler config |
| `IDebuggerWindow` | Window interface: `Initialize(params object[])` / `Shutdown()` / `OnEnter()` / `OnLeave()` / `OnUpdate(float, float)` / **`CreateView()` → `VisualElement`** |
| `DebuggerWindowRegistry` | Window registry (pure data): flat dictionary with O(1) lookup + path-tree navigation model (`DebuggerWindowNode`); a structural version number drives sidebar rebuilds |
| `DebuggerLogCapture` | Log capture: thread-safe enqueue + main-thread `Drain()` into a pooled ring buffer; incremental severity counters + content version |
| `LogNode` | Pooled log node: `LogTime` / `LogFrameCount` / `LogType` / `LogMessage` / `StackTrack` |
| `DebuggerRuntimeHost` | Runtime host (MonoBehaviour): constructs PanelSettings/UIDocument at runtime, floating FPS entry, main window chrome, layout persistence, OS fallback font (CJK-capable); singleton `Instance` |
| `DebuggerStatsOverlay` | Always-on stats HUD (`ProfilerRecorder` + StringBuilder reuse, zero steady-state allocation) |
| `DebugPanelBuilder` | Fluent panel builder: `AddLabel` / `AddSection` / `AddFoldout` / `AddButton` / `AddToggle` / `AddSlider` / `AddIntSlider` / `AddReadOnlyField` / `AddProgressBar` |
| `ScrollableDebuggerWindowBase` | Scrollable window base (UI Toolkit); `PollingDebuggerWindowBase` throttled-rebuild base |
| `DebuggerActiveWindowType` | Activation policy enum: `AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose` |
| `Constant.Debug` | Setting key constants for layout and console filters |
| `CommandLineUtility` | Static utility: `GetShowDebugger()` reads the `-showdebugger` force-on argument |
| `ServiceDebugView` | IMGUI debug view abstract base (implements `IDebuggerWindow`): `Title` / `IsReady` / `OnDrawContent()` (GUILayout) + default `CreateView()` (wraps the content in an `IMGUIContainer`) — a compat extension path for quick game-side IMGUI views (all framework built-in panels are native UI Toolkit) |
| `Windows/*` | Built-in windows: `ConsoleWindow`, `*InformationWindow`, `RuntimeMemorySummaryWindow`, `RuntimeMemoryInformationWindow<T>`, `*PoolInformationWindow`, `ServiceSystemInformationWindow`, `OperationsWindow`, `SettingsWindow`, etc. |

## Quick Start

No scene setup required — once the composition root registers `DebuggerService` (the `GameEntry` prefab already does), a floating FPS entry appears at runtime; click it to expand the full window. Configure the activation policy in `Assets/Settings/Framework/Resources/DebuggerServiceSettings.asset`.

Controlling from code:

```csharp
using Moirai.Atropos.Debugger;

DebuggerService.ActiveWindow = true;          // floating entry visibility
DebuggerService.ShowFullWindow = true;        // full window <-> floating entry
DebuggerService.SelectDebuggerWindow("Profiler/Memory/Texture");  // select a window
IDebuggerWindow window = DebuggerService.GetDebuggerWindow("Console");
```

Retrieving recorded logs:

```csharp
using System.Collections.Generic;
using Moirai.Atropos.Debugger;

var logs = new List<LogNode>();
DebuggerService.GetRecentLogs(logs);       // all (within the ring buffer)
DebuggerService.GetRecentLogs(logs, 100);  // the most recent 100

foreach (LogNode node in logs)
{
    UnityEngine.LogType type = node.LogType;
    string message = node.LogMessage;
    string stack = node.StackTrack;
}
```

## Fluent Debug Panels (Recommended Extension)

Custom debug panels require no hand-written UI Toolkit views — register in one line and declare controls through the builder:

```csharp
using Moirai.Atropos.Debugger;

DebuggerService.RegisterPanel("Game/Player", builder => builder
    .AddLabel("Player runtime tweaks")
    .AddSlider("Move Speed", 0f, 20f, () => player.MoveSpeed, v => player.MoveSpeed = v)
    .AddToggle("God Mode", () => player.Invulnerable, v => player.Invulnerable = v)
    .AddIntSlider("Max Health", 1, 999, () => player.MaxHealth, v => player.MaxHealth = v)
    .AddReadOnlyField("Current Position", () => player.transform.position)
    .AddProgressBar("Stamina", 0f, 100f, () => player.Stamina)
    .AddFoldout("Combat", b => b
        .AddButton("Kill All Enemies", player.KillAll)
        .AddReadOnlyField("Damage", () => player.Damage, "{0:F1}"))
    .AddSection("Danger Zone")
    .AddButton("Respawn", player.Respawn));
```

- Value controls bind through Getter/Setter closures (one-time allocation at build); a `schedule` polls refresh every 200ms — scheduling auto-pauses when detached from the panel
- The window title is the last path segment ("Player" above)
- `AddSlider` suppresses external write-back while being dragged (avoids fighting user input)

## Custom Windows & IMGUI Debug Views

Implement `IDebuggerWindow` (UI Toolkit view) to register a custom window:

```csharp
using UnityEngine.UIElements;

public class MyWindow : IDebuggerWindow
{
    public void Initialize(params object[] args) { }
    public void Shutdown() { }
    public void OnEnter() { }
    public void OnLeave() { }
    public void OnUpdate(float elapseSeconds, float realElapseSeconds) { }

    public VisualElement CreateView()
    {
        var root = new VisualElement();
        root.Add(DebuggerUI.CreateSectionTitle("Hello Debugger"));
        root.Add(DebuggerUI.CreateRow("Answer", "42"));
        return root;
    }
}

DebuggerService.RegisterDebuggerWindow("Other/My", new MyWindow());
```

Existing IMGUI debug views (`ServiceDebugView` derivatives) integrate unchanged — the default `CreateView()` wraps the `OnDraw()` GUILayout content in an `IMGUIContainer`:

```csharp
// Convenience registration (adapted via IMGUIDebuggerWindow internally)
DebuggerService.RegisterDebugView("Profiler/Timer Service", new TimerServiceDebugView());
```

Style helpers are centralized in `DebuggerUI` (structure building and USS class assignment only): `CreateSection` / `CreateCard` / `CreateRow` (value area copies on click, 2/3-wide row overload) / `CreateActionButton` / `CreateToggle` / `CreateFilterChip` / `CreateSlider` / `CreateReadOnlyMultilineText` / `StyleScrollView`, etc.; visual styles (palette / dimensions / interaction states) are defined in the shared style library `Runtime/Modules/Debugger/Resources/Debugger UI.uss` (mounted to `DebuggerPanelSettings.themeStyleSheet` via `Debugger UI Theme.tss`, with hover/pressed/checked driven by USS pseudo-classes) — the theme structure is shared with [DebugUI](https://github.com/annulusgames/DebugUI); sidebar group nodes use the built-in `Foldout` (rotating arrow and content collapsing out of the box).

## Service Debug Panels (Framework Built-in)

Each framework service module holds a native UI Toolkit debug view (implementing `IDebuggerWindow`) in its own directory, **auto-registered by the service's own `OnInit` via `DebuggerService.RegisterDebuggerWindow`** — the composition root registers the debugger first (ordering contract), so panels attach as services initialize, with no scene components. Sidebar paths and content:

| Path | View (module folder) | Content |
|------|----------------------|---------|
| `Profiler/Timer` | `TimerServiceDebugView` (Timer module) | active/capacity/peak statistics with usage bars, active timer sample, stale one-shot detection (0.5s throttle) |
| `Profiler/Resource` | `ResourceServiceDebugView` (Resource module) | play mode, loaded asset snapshot (state/ref counts, 0.5s throttle) |
| `Profiler/Audio` | `AudioServiceDebugView` (Audio module) | master volume and Sfx/UI/Music/Voice track volume/mute live controls |
| `Profiler/Procedure` | `ProcedureServiceDebugView` (Procedure module) | current procedure state and elapsed time (0.5s throttle) |
| `Profiler/Localization` | `LocalizationServiceDebugView` (Localization module) | current language display and one-click switching (1s throttle) |
| `Other/Game Settings` | `GameAppInformationWindow` (Debugger built-in) | frame rate / game speed live controls (0x-8x presets), local settings key-value list with save/clear |

The fixed pattern for adding a service debug panel:

```csharp
// 1) Place the view class in the service module's own folder (e.g. Runtime/Modules/Audio/AudioServiceDebugView.cs),
//    inheriting PollingDebuggerWindowBase (data-driven) or ScrollableDebuggerWindowBase (control-driven), content themed via DebuggerUI helpers;
// 2) Register at the end of the service's OnInit (the composition root guarantees DebuggerService is registered first — silently skipped when the facade isn't ready):
public override void OnInit()
{
    _ = Handler;
    DebuggerService.RegisterDebuggerWindow("Profiler/<Service>", new XxxServiceDebugView());
}
```

The registry's structural version drives automatic sidebar rebuilds in the host — registrations that arrive after the host exists take effect immediately too.

### IMGUI Debug View Adapter (Compat Extension Path)

Game-side debug views that already exist or prefer GUILayout (`ServiceDebugView` derivatives) integrate unchanged — the default `CreateView()` wraps via `IMGUIContainer` (skin text colors are temporarily lifted for dark-background readability):

```csharp
DebuggerService.RegisterDebugView("My/IMGUI View", new MyIMGUIDebugView());
```

Custom popups and any OnGUI context can also call `view.OnDraw()` directly.

## Configuration & Extension

- **Activation policy** (`DebuggerServiceSettings.ActiveWindowType`):
  - `AlwaysOpen`: always visible
  - `OnlyOpenWhenDevelopment`: visible when `Debug.isDebugBuild` (default)
  - `OnlyOpenInEditor`: visible when `Application.isEditor`
  - `AlwaysClose`: hidden by default
  - Any policy other than `AlwaysOpen` can be forced on with the `-showdebugger` launch argument
- **Backend config** (`DefaultDebuggerHandlerConfig`): `ConsoleCapacity` / `FpsUpdateInterval` / `StatsOverlayVisible` / `WindowOpacity`
- **Extending info windows**: inherit `PollingDebuggerWindowBase` (throttled rebuild) or `ScrollableDebuggerWindowBase` and register under an `"Information/..."` path
- **Custom backend**: inherit `DebuggerServiceHandler` + a paired `DebuggerServiceHandlerConfig`, then swap it in the settings asset
- `DebuggerService.Tick` only polls the visible window while `ShowFullWindow` is expanded; log-capture draining always runs (capture does not depend on UI state)

## Notes

- The 28 built-in windows are registered by `DefaultDebuggerHandler.OnInit`; register custom windows after service initialization. `RegisterDebuggerWindow` paths must be non-empty and must not collide with registered windows or directories, otherwise a `GameException` is thrown
- The runtime panel **must carry a theme**: the host clones the in-package `Resources/DebuggerPanelSettings.asset` (with `UnityDefaultRuntimeTheme` embedded) — `ScriptableObject.CreateInstance<PanelSettings>()` yields a null `themeStyleSheet` in Play Mode, leaving all built-in controls without base USS (completely broken layout)
- Never create `VisualElement`s in MonoBehaviour field initializers (UnityException) — always create them inside build methods
- The floating entry snaps to the nearest screen edge after a drag; layout persists via `SettingUtility`, and the header Reset button restores defaults
- Console filter state (severities + scroll lock) also persists; see `Constant.Debug` for the keys
- `LogNode`s returned by `GetRecentLogs` are pooled and owned by the capture — read-only, do not retain (nodes are recycled after ring eviction)
- The service is opt-in (the composition root registers `DebuggerService` manually); facade calls silently degrade when unregistered (log queries return empty, registrations are no-ops)

## Migrating from the Old IMGUI DebuggerComp

- The `DebuggerComp` component was removed from scenes/prefabs (the `GameEntry.prefab` no longer contains a Debugger node) — the host is spawned by the service at runtime
- **`ServiceDebuggerComponent` (the Inspector host component) is deprecated and removed**: derived components (e.g. `TimerServiceDebugger`) and the generic Inspector are deleted — service debug views now auto-register into the in-game debugger from OnInit (native UI Toolkit, located in each module's folder)
- **`GameAppEditor` (the GameApp Inspector) is deleted**: its debug info (frame rate / game speed / local settings list) is integrated into the built-in `Other/Game Settings` window (GameObject pool content was already covered by `Profiler/GameObject Pool`)
- `DebuggerComp.Instance.GetRecentLogs(...)` → `DebuggerService.GetRecentLogs(...)`
- `DebuggerComp.LogNode` → `Moirai.Atropos.Debugger.LogNode` (top-level type)
- `IDebuggerWindow.OnDraw()` (IMGUI) → `CreateView()` (UI Toolkit); IMGUI content integrates via `RegisterDebugView` / `IMGUIDebuggerWindow`
- `IDebuggerWindowGroup` / `DebuggerWindowRoot` (nested window-group toolbars) → `DebuggerWindowRegistry` (the path tree is navigation data only)
- The activation policy moved from the `DebuggerComp` Inspector field to the `DebuggerServiceSettings` asset (`ActiveWindowType`); `UGUIHandler`'s error-log switch now reads `DebuggerService.ActiveWindowType`
- The input info window was rewritten from the legacy `UnityEngine.Input` API (which throws under an Input System-only build) to Input System device model reads

---
[« Back to main README](../../README.md)
