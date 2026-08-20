# Debugger Service

> Runtime debugger: An IMGUI-based in-game debug panel providing console, runtime environment information, memory and object pool profiling windows, etc.

The Debugger service consists of a pure C# `DebuggerService` (accessed via `GameApp.Services.GetRequiredService<IDebuggerService>()`) responsible for registering and polling the window tree, and a scene component `DebuggerComp` responsible for rendering. At runtime, it appears as a floating box in the top-left corner; clicking it expands the full window. Window layout (position, size, scale) is persisted via `SettingUtility`. All windows are based on the `IDebuggerWindow` interface, and business code can register its own debug windows. This service is a pure runtime IMGUI implementation with no editor-specific code (the Editor directory's Events / Scheduler debug windows belong to other services).

## Core Features

- Runtime panel: Console log window (Info/Warning/Error/Fatal filtering, lock scroll), FPS counter
- Information windows: System / Environment / Screen / Graphics / Scene / Path / Time / Quality
- Input information: Summary / Touch / Location / Acceleration / Gyroscope / Compass
- Profiler windows: Summary, Memory (All / Texture / Mesh / Material / Shader / AnimationClip / AudioClip / Font / TextAsset / ScriptableObject), Object Pool, Reference Pool
- Configurable activation strategy: Always open / Development builds only / Editor only / Always closed, with command-line argument to force enable
- Window tree architecture: Path-based registration (e.g., `"Profiler/Memory/Texture"`), supports custom windows and window groups
- Layout persistence: The floating box and window positions, sizes, and scales are saved in local settings

## Core Types

Namespace: `Moirai.Atropos.Debugger`

| Class/Interface | Description |
|---------|------|
| `IDebuggerService` | Debugger manager interface: `ActiveWindow`, `DebuggerWindowRoot`, `RegisterDebuggerWindow` / `UnregisterDebuggerWindow` / `GetDebuggerWindow` / `SelectDebuggerWindow` |
| `DebuggerService` | Default implementation (`internal sealed`), `Priority = -1`, implements `IUpdateService`, polls the window tree only when a window is active |
| `IDebuggerWindow` | Debugger window interface: `Initialize(params object[] args)` / `Shutdown()` / `OnEnter()` / `OnLeave()` / `OnUpdate(float, float)` / `OnDraw()` |
| `IDebuggerWindowGroup` | Window group interface (inherits `IDebuggerWindow`): `DebuggerWindowCount` / `SelectedIndex` / `SelectedWindow` / `GetDebuggerWindowNames()` / `RegisterDebuggerWindow(string, IDebuggerWindow)` |
| `DebuggerComp` | Debugger component (`public sealed partial`, MonoBehaviour), renders all panels when attached to a scene, singleton `DebuggerComp.Instance` |
| `DebuggerActiveWindowType` | Activation strategy enum: `AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose` |
| `DebuggerComp.LogNode` | Log node: `LogTime` / `LogFrameCount` / `LogType` / `LogMessage` / `StackTrack` |
| `Constant.Debug` | Setting key constants for layout and console filtering (e.g., `WINDOW_SCALE`, `LOCK_SCROLL`) |
| `CommandLineUtility` | Static utility class: `GetShowDebugger()` reads the command-line argument to force enable the debugger |
| `Component/*` | Implementation of various information windows, such as `ConsoleWindow`, `ProfilerInformationWindow`, `RuntimeMemoryInformationWindow<T>`, `ObjectPoolInformationWindow`, `ScrollableDebuggerWindowBase`, etc. |

## Quick Start

Place a GameObject with `DebuggerComp` attached in the scene (the Inspector allows configuring `GUISkin`, activation strategy `m_ActiveWindow`, `m_ShowFullWindow`). At runtime, click the floating box in the top-left corner to expand the debugger.

Controlling the debugger and selecting windows in code:

```csharp
using Moirai.Atropos;

GameApp.Services.GetRequiredService<IDebuggerService>().ActiveWindow = true;        // Open/close the debugger window
bool active = GameApp.Services.GetRequiredService<IDebuggerService>().ActiveWindow;

// Equivalent and extended control via DebuggerComp
DebuggerComp.Instance.ActiveWindow = true;      // Start/stop the component as well
DebuggerComp.Instance.ShowFullWindow = true;    // Full window <-> floating box
DebuggerComp.Instance.ResetLayout();            // Restore default layout (position/size/scale)

// Select a specific window (path comes from the registration string)
GameApp.Services.GetRequiredService<IDebuggerService>().SelectDebuggerWindow("Profiler/Memory/Texture");
IDebuggerWindow window = GameApp.Services.GetRequiredService<IDebuggerService>().GetDebuggerWindow("Console");
```

Registering a custom debug window:

```csharp
using Moirai.Atropos.Debugger;
using UnityEngine;

public class MyWindow : IDebuggerWindow
{
    public void Initialize(params object[] args) { }
    public void Shutdown() { }
    public void OnEnter() { }
    public void OnLeave() { }
    public void OnUpdate(float elapseSeconds, float realElapseSeconds) { }

    public void OnDraw()
    {
        GUILayout.Label("Hello Debugger");
    }
}

// Register via DebuggerComp or the service interface; paths use "/" as separator and are automatically grouped
DebuggerComp.Instance.RegisterDebuggerWindow("Other/My", new MyWindow());
// Alternatively, use the service interface directly: GameApp.Services.GetRequiredService<IDebuggerService>().RegisterDebuggerWindow("Other/My", new MyWindow());
```

Retrieving logs recorded at runtime:

```csharp
using System.Collections.Generic;
using Moirai.Atropos.Debugger;

var logs = new List<DebuggerComp.LogNode>();
DebuggerComp.Instance.GetRecentLogs(logs);     // All logs
DebuggerComp.Instance.GetRecentLogs(logs, 100); // Most recent 100 logs

foreach (DebuggerComp.LogNode node in logs)
{
    UnityEngine.LogType type = node.LogType;
    string message = node.LogMessage;
    string stack = node.StackTrack;
}
```

## Configuration and Extension

- Activation strategy (`m_ActiveWindow` in the `DebuggerComp` Inspector):
  - `AlwaysOpen`: Unconditionally open (default)
  - `OnlyOpenWhenDevelopment`: Open when `UnityEngine.Debug.isDebugBuild` is true
  - `OnlyOpenInEditor`: Open when `Application.isEditor` is true
  - `AlwaysClose`: Closed by default
  - Except for `AlwaysOpen`, all of the above can be force-enabled via the startup parameter corresponding to `CommandLineUtility.GetShowDebugger()`
- Layout-related properties: `IconRect` (floating box area), `WindowRect` (window area), `WindowScale` (scale, default 1.5); changes are persisted via setting keys in `Constant.Debug`
- Console filter state (Info/Warning/Error/Fatal, lock scroll) is also persisted; keys are found in `Constant.Debug.INFO_FILTER`, etc.
- To extend information windows, inherit from `ScrollableDebuggerWindowBase` in the `Component` directory to get scrollable drawing capability, then register under the `"Information/..."` path
- `DebuggerService.Update` only calls the window tree's `OnUpdate` when `ActiveWindow == true`; no polling overhead when closed

## Notes

- `DebuggerComp.Start` registers all built-in windows at once (Console, Information/..., Profiler/..., Other/Settings, Other/Operations); register custom windows after that
- The `path` parameter of `RegisterDebuggerWindow` cannot be empty, and `debuggerWindow` cannot be null, otherwise `GameException` is thrown; `args` is passed through to the window's `Initialize`
- Toggling `ShowFullWindow` also enables/disables the `"UIRoot/EventSystem"` event system object in the scene
- The FPS counter (`FpsCounter`) is an internal type of `DebuggerComp`, calculated at a fixed interval (0.5 seconds), not exposed externally
- Window drawing is based on `OnGUI`/`GUILayout`; `DebuggerComp.OnDestroy` calls `SettingUtility.Save()` to persist layout settings

---
[« Back to Main README](../../README_EN.md)