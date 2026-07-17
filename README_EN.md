Moirai Framework
===

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity3d.com/)
[![openupm](https://img.shields.io/npm/v/com.moirai.framework?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.moirai.framework/)
[![License](https://img.shields.io/github/license/TeamMoirai/com.moirai.framework)](LICENSE)
[![Issues](https://img.shields.io/github/issues/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/issues)
[![Last Commit](https://img.shields.io/github/last-commit/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![Top Language](https://img.shields.io/github/languages/top/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![README](https://img.shields.io/badge/README-中文-FFA500)](https://github.com/TeamMoirai/com.moirai.framework/blob/main/README.md)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/TeamMoirai/com.moirai.framework)

## Introduction

**Moirai Framework** is a simple (beginner-friendly, out-of-the-box) and powerful Unity framework for cross-platform development.

### Key Features

- **Out-of-the-Box** - Get started with the entire development workflow in 5 minutes, clean code, clear structure
- **High Performance** - UniTask-based async system, zero-GC event dispatch, strict memory management
- **High Cohesion, Low Coupling** - Modular design, easily remove or replace modules you don't need
- **Hot Update Support** - Integrated HybridCLR, full-platform hot update workflow ready
- **Code Obfuscation** - Integrated Obfuz for code obfuscation and hardening
- **Asset Management** - Integrated YooAsset, supports LRU and ARC cache strategies, automatic asset release
- **Config Table System** - Integrated Luban, supports lazy loading, async loading, and sync loading
- **UI Framework** - Production-grade UI development workflow, supports code auto-generation
- **Full Platform Support** - Windows, Android, iOS, WebGL, WeChat Mini Games, and more

---

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
## 📚 Table of Contents

- [Quick Start](#quick-start)
  - [Requirements](#requirements)
  - [Getting Started](#getting-started)
    - [Installation](#installation)
      - [Option 1: One-Click Install (Recommended)](#option-1-one-click-install-recommended)
      - [Option 2: UPM Install](#option-2-upm-install)
      - [Option 3: Manual Install](#option-3-manual-install)
    - [Initial Setup](#initial-setup)
      - [Scene Building](#scene-building)
      - [Config Table Module](#config-table-module)
    - [Quick Tips](#quick-tips)
- [Architecture](#architecture)
  - [Module System](#module-system)
  - [Startup Flow](#startup-flow)
- [Core Modules](#core-modules)
  - [Resource — Asset Management](#resource--asset-management)
  - [UI — UI Framework](#ui--ui-framework)
  - [Audio — Audio System](#audio--audio-system)
  - [Localization — Localization](#localization--localization)
  - [Events — Event System](#events--event-system)
  - [FSM — Finite State Machine](#fsm--finite-state-machine)
  - [Procedure — Procedure Management](#procedure--procedure-management)
  - [Input — Input System](#input--input-system)
  - [Save — Save System](#save--save-system)
  - [Scheduler — Scheduler](#scheduler--scheduler)
- [Core Utilities](#core-utilities)
  - [Attributes — Custom Attributes](#attributes--custom-attributes)
  - [ObjectPool — Object Pool](#objectpool--object-pool)
  - [MemoryPool — Memory Pool](#memorypool--memory-pool)
  - [Singleton — Singleton System](#singleton--singleton-system)
  - [GameConfig — Config Management](#gameconfig--config-management)
  - [GameLog — Logging System](#gamelog--logging-system)
  - [GameTime — Game Time](#gametime--game-time)
  - [GameProfiler — Profiler](#gameprofiler--profiler)
  - [GameSettings — Game Settings](#gamesettings--game-settings)
  - [GameException — Exception System](#gameexception--exception-system)
  - [ToolRegistry — Component Registry](#toolregistry--component-registry)
  - [Obfuz — Code Obfuscation](#obfuz--code-obfuscation)
  - [DataStructure — Data Structures](#datastructure--data-structures)
  - [Extensions/R3 — Reactive Extensions](#extensionsr3--reactive-extensions)
  - [Utility — Utilities](#utility--utilities)
- [Editor Tools](#editor-tools)
- [Contributing & Support](#contributing--support)
  - [Ecosystem Dependencies](#ecosystem-dependencies)
  - [Contributors](#contributors)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

## Quick Start

### Requirements

- **Unity Version**: 2022.3.x (recommended) or higher
- **Development Environment**: .NET 4.x
- **Dependencies**：[Odin Inspector and Serializer](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041)
- **Supported Platforms**: Windows, OSX, Android, iOS, WebGL

### Getting Started

#### Installation

##### Option 1: One-Click Install (Recommended)

1. Install **Framework Installer** via either method:

   - In **Window/Package Manager**, install via Git URL:

      ```bash
      https://github.com/TeamMoirai/com.moirai.framework.git#installer
      ```
      <img src="Documentation~\src\quick-start-1.png" alt="quick-start-scoped-registries" />

   - Clone the `install` branch to your project directory (Assets/...):

      ```bash
      git clone --branch installer --single-branch https://github.com/TeamMoirai/com.moirai.framework.git Scripts/Installer
      ```

2. Return to Unity and manually run menu `Tools/Install Framework`

3. After installation completes, you can safely delete the installer script

##### Option 2: UPM Install

1. In **Project Settings/Unity Package Manager**, manually add **Scoped Registry**:

   ```text
   Name: Open UPM
   URL: https://package.openupm.com
   Scope(s): com.cysharp
             com.tuyoogame
             com.moirai
   ```

   <img src="Documentation~\src\quick-start-2-scoped-registries.png" alt="quick-start-scoped-registries" />

2. In **Window/Package Manager**, select **Moirai Framework** and click **Install**:

    <img src="Documentation~\src\quick-start-2-package-detail.png" alt="quick-start-package-detail" />

3. <a id="manual-import"></a>Manually copy all contents from the **@Requirements** folder under `ProjectRoot/Library/PackageCach/com.moirai.framework@xxx/Templates~/` to the **ProjectRoot/Asset**s directory.

    (Optional) Copy an appropriate template from the same directory into the project as needed; generally, choose **NormalTemplate**.

##### Option 3: Manual Install

1. Download the **Source Code** archive from the latest release on the Releases page.

2. Refer to **[Quick Start - Getting Started - Installation - Option 2: UPM Install - 3](#manual-import)**.

---

#### Initial Setup

##### Scene Building

Add `Scenes/main.unity` to the build:

- Unity 6.0+: `File -> Build Profiles -> Scene List`

- Unity 6.0-:` File -> Build Settings -> Scene In Build`

#####  Config Table Module
   - Select `Tools/Settings/ConfigTableSettings`, click `生成 Config 到指定目录`.

   - When generating for the first time, before exporting, first run the **build-luban** compilation or manually import Luban to the configuration table root directory.

   - If the config table directory is moved, you need to manually update it in `Tools/Settings/ConfigTableSettings` — Redirect Config Directory

#### Quick Tips

1. **Editor Play Mode**
   - Select `YooAsset/Editor PlayMode` from the top menu bar for editor simulation mode
   - Click `Play` to start running

2. **Build & Run** (Hot Update Workflow)

   - Run menu `HybridCLR/Install...` to install HybridCLR
   - Run menu `HybridCLR/Define Symbols/Enable HybridCLR` to enable hot updates
   - Run menu `HybridCLR/Generate/All` for necessary code generation
   - Run menu `HybridCLR/Build/BuildAssets And CopyTo AssemblyPath` to build the hot update DLL
   - Run menu `YooAsset/AssetBundle Builder` to build AssetBundles
   - Open Build Settings and click Build And Run

> **Tip**: For issues, see [HybridCLR Common Errors](https://hybridclr.doc.code-philosophy.com/docs/help/commonerrors)

---

## Architecture

```
com.moirai.framework/
├── Runtime/              # Core Framework Assembly (Moirai.Atropos)
│   ├── Core/             # Base utilities and data structures
│   │   ├── Attributes/   #   Custom attributes (~20 + Odin extensions)
│   │   ├── Constant/     #   Constants (RuntimeId, etc.)
│   │   ├── DataStructure/#   Data structures (IoC container, priority queue, sparse array, etc.)
│   │   ├── Events/       #   Event system (pooled, bubbling propagation)
│   │   ├── Extensions/   #   Extension methods (R3 reactive, UGUI, etc.)
│   │   ├── GameException/#   Game exception system
│   │   ├── GameProfiler/ #   Performance profiler
│   │   ├── GameSettings/ #   Framework and graphics settings
│   │   ├── GameTime/     #   Game time
│   │   ├── MemoryPool/   #   Memory pool
│   │   ├── Models/       #   Data models
│   │   ├── Obfuz/        #   Code obfuscation initialization
│   │   ├── Pool/         #   Object pool (generic/UniTask/GameObject)
│   │   ├── Schedulers/   #   Zero-allocation scheduler (timer/frame counter)
│   │   ├── Singleton/    #   Singleton system (pure C# / MonoBehaviour)
│   │   ├── Tasks/        #   Task/sequence system
│   │   └── Utility/      #   Utilities (logging, encryption, HTTP, reflection, tween, etc.)
│   └── Modules/          #   Functional modules
│       ├── Audio/        #   Audio system (categories/agents/fade)
│       ├── ConfigTable/  #   Config table management
│       ├── Debugger/     #   Runtime debugger
│       ├── FSM/          #   Finite State Machine
│       ├── Input/        #   Input system (keyboard/mouse/gamepad/mobile)
│       ├── Localization/ #   Localization (text/image/audio/Google Translate)
│       ├── ObjectPool/   #   Object pool module
│       ├── Procedure/    #   Procedure management
│       ├── Resource/     #   YooAsset asset management
│       ├── Save/         #   Save system (JSON/binary/encrypted)
│       ├── Scene/        #   Scene management
│       ├── Timer/        #   Timer
│       ├── UI/           #   UI framework (windows/widgets/layers)
│       └── UpdateDriver/ #   Update loop driver
├── Editor/               # Editor toolset
├── Plugins/              # Third-party libraries
├── Samples~/             # Examples
├── Templates~/           # Project initial templates
└── Tests/                # Unit tests
```

### Module System

The framework uses a **modular architecture** where all subsystems are plain C# classes (not MonoBehaviour), registered and managed via `ModuleSystem`. The entry point is `GameModule` (MonoBehaviour), which provides static accessors for all modules.

```csharp
// Module access - lazy loaded, auto-created on first access
var resource = GameModule.Resource;
var ui = GameModule.UI;
var audio = GameModule.Audio;
var localization = GameModule.Localization;
```

**Module Lifecycle:**
- `OnInit()` - Module initialization
- `Shutdown()` - Module destruction
- Supports `IUpdateModule`, `IFixedUpdateModule`, `ILateUpdateModule` interfaces for update loop registration
- Update order controlled by `Priority` property

### Startup Flow

`Main/Procedure/` defines the complete startup chain:

```
ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources
→ ProcedureCreateDownloader → ProcedureDownloadFile → ProcedureDownloadOver
→ ProcedureClearCache → ProcedureLoadAssembly → ProcedurePreload → ProcedurePrepare4Main
```

Each stage is an independent `ProcedureBase` state, customizable via `ProcedureSettings` (ScriptableObject).

---

## Core Modules

### Resource — Asset Management

Wraps YooAsset with sync/async loading APIs.

| Feature | Description |
|---------|-------------|
| Play Mode | EditorSimulate, Offline, Host, Web |
| Reference Counting | `AssetObject` pool auto-managed |
| Load Cancellation | Supports `CancellationToken` |
| Asset Encryption | FileOffset, FileStream |
| Sprite Loading | `ResourceExtComponent` supports sub-sprites |

```csharp
// Async loading
var handle = GameModule.Resource.LoadAssetAsync<GameObject>("Assets/Prefabs/Hero.prefab");
await handle.ToUniTask();
var prefab = handle.AssetObject;

// Sync loading
var sprite = GameModule.Resource.LoadAsset<Sprite>("Assets/UI/icon.png");
```

### UI — UI Framework

Production-grade UI development workflow with stack-based window management.

| Layer | Purpose |
|-------|---------|
| Background | Background layer |
| UI | Main interface |
| Popup | Popup layer |
| System | System layer |
| Top | Topmost layer |

```csharp
// Open window
GameModule.UI.ShowWindow<MainWindow>();

// Close window
GameModule.UI.CloseWindow<MainWindow>();

// Widget sub-control
public class MainWindow : UIWindow
{
    protected override void OnCreate() { /* Initialize */ }
    protected override void OnRefresh(object userData) { /* Refresh data */ }
    protected override void OnClose() { /* Cleanup on close */ }
}
```

**Editor Support:** `ScriptAutoGenerator` auto-generates UI binding code.

### Audio — Audio System

Category management, agent-based playback, event-driven.

```csharp
// Play sound effect
GameModule.Audio.Play("BGM_Main", AudioGroup.BGM);

// Fade in/out
GameModule.Audio.FadeIn("BGM_Battle", 2.0f);
GameModule.Audio.FadeOut("BGM_Main", 1.5f);
```

- **AudioCategory** - Each AudioGroupConfig corresponds to one category
- **AudioAgent** - Agent-based playback, auto-manages AudioSource lifecycle
- **AudioMixer** - Unity AudioMixer integration
- **Event-Driven** - `AudioPlayEvent`, `AudioControlEvent`, `AudioTrackEvent`, `AudioFadeEvent`

### Localization — Localization

Supports automatic injection for multiple content types.

| Type | Localizer |
|------|-----------|
| UGUI Text | `LocalizerText` |
| TextMeshPro | `LocalizerTMPText` |
| SpriteRenderer | `LocalizerSprite` |
| RawImage / Texture | `LocalizerRawImage` |
| AudioSource | `LocalizerAudio` |
| Timeline | `LocalizerTimeline` |

- Loads localization strings from Luban config tables
- Supports Google Translate integration (`GoogleTranslator`)
- Language detection: Command line → Editor settings → Saved settings → System language

### Events — Event System

Pooled bubbling event system ported from Unity UIElements.

```csharp
// Register event
EventManager.RegisterCallback<GameStartEvent>(OnGameStart);

// Send event (supports bubbling/capture propagation)
EventManager.SendEvent(new GameStartEvent());

// Unregister
EventManager.UnregisterCallback<GameStartEvent>(OnGameStart);
```

- **Zero GC Allocation** - Independent object pool per event type
- **Propagation** - TrickleDown (capture) → BubbleUp (bubble)
- **Propagation Control** - `StopPropagation()`, `StopImmediatePropagation()`, `PreventDefault()`
- **Editor Debug** - Visual event dispatch debug window

### FSM — Finite State Machine

```csharp
var fsm = GameModule.Fsm.CreateFsm("GameFlow", state1, state2, state3);
fsm.Start<State1>();

// State transition
fsm.ChangeState<State2>();

// State interface
public class State1 : FsmState
{
    protected override void OnInit(IFsm fsm) { }
    protected override void OnEnter(IFsm fsm) { }
    protected override void OnUpdate(IFsm fsm, float elapseSeconds) { }
    protected override void OnLeave(IFsm fsm, bool isShutdown) { }
    protected override void OnDestroy(IFsm fsm) { }
}
```

### Procedure — Procedure Management

FSM-based game flow management where each stage is an independent state.

```csharp
public class ProcedurePreload : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureModule> fsm)
    {
        // Preload resources, initialize game data
    }

    protected override void OnUpdate(IFsm<IProcedureModule> fsm, float elapseSeconds)
    {
        // Check loading completion, transition to next stage
    }
}
```

### Input — Input System

Abstracted input layer supporting multiple platforms.

| Handler | Description |
|---------|-------------|
| `UnityInputSystemHandler` | New Input System |
| `UnityInputManagerHandler` | Legacy Input Manager |
| `UIMobileInputHandler` | Mobile UI touch input |

- UI modal detection - Automatically blocks player input when popups are open
- Button prompt system - Supports PS4/PS5/Xbox/Switch/SteamDeck/keyboard-mouse icons

### Save — Save System

Pluggable save strategies.

```csharp
// Save
GameModule.Save.Save("player_data", playerData);

// Load
var data = GameModule.Save.Load<PlayerData>("player_data");
```

| Handler | Description |
|---------|-------------|
| `JsonSaveHandler` | JSON format |
| `BinarySaveHandler` | Binary format |
| `JsonEncryptedSaveHandler` | JSON encrypted |
| `BinaryEncryptedSaveHandler` | Binary encrypted |

- **Atomic Save** - Writes to `.tmp` first then renames, prevents data corruption

### Scheduler — Scheduler

Zero-allocation timer/frame scheduling system.

```csharp
// Delayed execution
Scheduler.Delay(2.0f, () => Debug.Log("Executes after 2 seconds"));

// Wait frames
Scheduler.WaitFrame(3, () => Debug.Log("Executes after 3 frames"));

// Cancel scheduling
var handle = Scheduler.Delay(5.0f, () => { });
handle.Cancel();
```

- Supports Update / FixedUpdate / LateUpdate frames
- Supports looping, ignores TimeScale
- `SchedulerUnsafeBinding` - Unsafe struct for zero-allocation function pointer dispatch

---

## Core Utilities

### Attributes — Custom Attributes

~20 custom property drawers + Odin Inspector extensions for enhanced editor UX.

| Attribute | Description |
|-----------|-------------|
| `BooleanButton` | Boolean button drawing |
| `BreakVector2/3` | Vector split drawing |
| `ConditionAttribute` | Conditional display control |
| `DisableAttribute` | Disabled field drawing |
| `EnumConditionAttribute` | Enum condition control |
| `ExpandAttribute` | Expandable property |
| `InspectorButton` | Inspector button |
| `InspectorButtonBar` | Button bar |
| `LayerAttribute` | Layer selector |
| `TagAttribute` | Tag selector |
| `ResourcePathAttribute` | Resource path selector |
| `ReferenceDropdownAttribute` | Reference dropdown |
| `OdinExtends/*` | Odin extensions (condition groups, help info, inline buttons, etc.) |

### ObjectPool — Object Pool

```csharp
// Generic object pool
var pool = new ObjectPool<MyClass>(() => new MyClass(), 32);
var obj = pool.Get();
pool.Release(obj);

// GameObject pool
var go = GameObjectPoolManager.Get("Particle");
GameObjectPoolManager.Release(go);
```

### MemoryPool — Memory Pool

Generic memory pool with collection management, reduces GC pressure.

### Singleton — Singleton System

| Type | Description |
|------|-------------|
| `Singleton<T>` | Pure C# singleton, double-checked locking |
| `SingletonMono<T>` | MonoBehaviour singleton, supports persistence/replacement strategies |
| `SingletonRegister<T>` | Register-based singleton, supports lookup by type |
| `ReferencedScriptableObject` | ScriptableObject reference base class |

`SingletonSystem` centrally manages all singleton lifecycles, attached to UpdateDriver to drive IUpdate/IFixedUpdate/ILateUpdate.

### GameConfig — Config Management

Integrated Luban config table system.

```csharp
ConfigMgr.LoadTables();           // Sync loading
await ConfigMgr.LoadTablesAsync(); // Async loading
var cfg = ConfigMgr.Tables.TbItem.Get(itemId); // Lazy loading access
```

### GameLog — Logging System

```csharp
Log.Info("Player logged in: {0}", playerName);
Log.Warning("Asset load failed: {0}", path);
Log.Error("Critical error!");
```

- Conditional compilation: `LOG_DEBUG_ENABLE`, `LOG_ALL`, `LOG_INFO_ENABLE`, etc.
- Pluggable `ILogHelper` for custom log output

### GameTime — Game Time

Lightweight time accessor, sampled once per frame to avoid frequent `Time.deltaTime` calls.

```csharp
float dt = GameTime.deltaTime;       // Frame interval
float time = GameTime.time;          // Current time
float unscaledDt = GameTime.unscaledDeltaTime; // Unscaled frame interval
```

### GameProfiler — Profiler

Conditional compilation performance sampling tool, active only when `PROFILER_ENABLE` macro is enabled.

```csharp
GameProfiler.BeginSample("MyOperation");
// ... code to profile
GameProfiler.EndSample();
```

- Supports level-based sampling (`SetProfileLevel`) to control sampling depth
- Zero overhead: completely removed when macro is off

### GameSettings — Game Settings

Graphics settings management: resolution, fullscreen, VSync, window mode, etc.
Also includes framework settings (`FrameworkSettings`) and update settings (`UpdateSettings`).

### GameException — Exception System

Custom game exception type with error code and context information.

### ToolRegistry — Component Registry

High-performance component registration/lookup system, replacing `FindObject` with O(1)-level lookups.

```csharp
// Register
ToolRegistry.RegisterComponent(myService, "GameService");

// Lookup
var service = ToolRegistry.GetComponent<IMyService>("GameService");

// Type-based lookup
var player = ToolRegistry.GetComponent<PlayerController>();
```

- Scene-aware: auto-cleans non-persistent registrations on scene unload
- Zero-GC batch queries: `GetComponents<T>(List<T>)`
- Thread-safe design

### Obfuz — Code Obfuscation

Integrated with the [Obfuz](https://github.com/nicenightcc/Obfuz) code obfuscation framework, auto-initializes encryption virtual machine after assembly load.

- Conditional compilation: requires both `OBFUZ_INSTALLED` and `ENABLE_OBFUZ` macros
- Supports static key encryption (`StaticEncryptionScope`)
- Auto-loads key resource (`Resources/Obfuz/defaultStaticSecretKey`)

### DataStructure — Data Structures

| Data Structure | Description |
|----------------|-------------|
| `IOCContainer` | Inversion of Control container |
| `PriorityQueue<T>` | Priority queue |
| `RandomList<T>` | Random list |
| `SerializableDictionary<K,V>` | Serializable dictionary |
| `SparseArray<T>` | Sparse array |
| `ShuffleBag<T>` | Shuffle bag (non-repeating random) |
| `GameDictionary<K,V>` | Game dictionary (with traversal support) |
| `GameLinkedList<T>` | Game linked list |
| `GameMultiDictionary<K,V>` | Multi-value dictionary |
| `TypeNamePair` | Type-name pair (for type-based registration) |
| `ArrayUtils` | Array utility methods |
| `GameSerializer` | Game serialization utilities |

### Extensions/R3 — Reactive Extensions

Reactive programming support based on R3 (Reactive Extensions), with UGUI bindings.

```csharp
// Observable extensions
myButton.OnClickAsObservable()
    .Subscribe(_ => Debug.Log("Button clicked"));

// ReactiveProperty ↔ UGUI two-way binding
var hp = new ReactiveProperty<int>(100);
hp.BindTo(hpSlider);  // Slider auto-syncs
```

### Utility — Utilities

| Utility | Description |
|---------|-------------|
| `AlgorithmUtility` | Algorithm utilities |
| `AssemblyUtility` | Assembly utilities |
| `ColorsUtility` | Color utilities |
| `CommandLineUtility` | Command-line parsing |
| `ConverterUtility` | Type conversion |
| `CoroutineUtility` | Coroutine utilities |
| `DebugDrawHelper` | Debug drawing |
| `DiagnosticsUtility` | Diagnostics utilities |
| `EncryptionUtility` | Encryption utilities |
| `FileUtility` | File operations |
| `HttpUtility` | HTTP requests (with UniTask support) |
| `MainThreadDispatcher` | Main thread dispatching |
| `MarshalUtility` | Unmanaged memory operations |
| `MaterialUtility` | Material utilities |
| `MathsUtility` | Math utilities (with Unity.Mathematics integration) |
| `NetUtility` | Network utilities |
| `PathUtility` | Path utilities |
| `ProgramUtility` | Program utilities |
| `ReflectionUtility` | Reflection utilities (with serialized field traversal) |
| `TimeUtility` | Time utilities |
| `ToolRegistry` | Component registry |
| `Tween/*` | Easing system (with Bezier paths) |
| `UniTaskUtils` | UniTask utilities |
| `UnityUtility` | Unity common utilities |
| `XmlUtility` | XML utilities |
| `ZipWrapper` | Compression/decompression wrapper |

---

## Editor Tools

| Tool | Purpose |
|------|---------|
| Atlas Maker | Sprite Atlas creation |
| Custom Attributes | ~20 custom property drawers + Odin extensions |
| Define Symbols | Debug/Log/Profiler/Obfuz macro definition management |
| Design Tool | Comprehensive probability calculator |
| Editor Design | Editor icon resources, GUIStyle viewer |
| Event Debugger | Visual event dispatch debug window |
| Game Settings | Audio group, procedure settings, update settings editor |
| HybridCLR | Hot update DLL build commands |
| Inspector | Asset/Core component custom inspectors |
| Luban Tools | Luban config table generation |
| Maintenance | Clean empty folders, find missing scripts, group selection, lock Inspector |
| Reference Finder | Asset dependency/reference tree view |
| Release Tools | Build pipeline window, build configuration |
| Scheduler Debugger | Visual scheduler/timer debugger |
| Tasks Editor | Task runner editor |
| Tween | Easing property drawer |
| UI Module | UI binding code auto-generation, component Inspector |
| Input Module | Input action config editor, button icon collection editor |
| Utility | Command-line reader, log redirection, EditorScriptableSingleton, Shell helper, etc. |
| YooAsset | Build cache cleanup, built-in directory, custom build pipeline, Shader variant collection |

---

## Contributing & Support

### Ecosystem Dependencies

| Project | Description |
|---------|-------------|
| **[TEngine](https://github.com/Alex-Rachel/TEngine)** | Unity production-grade development framework |
| **[YooAsset](https://github.com/tuyoogame/YooAsset)** | Production-grade asset management system verified with millions of DAU games |
| **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** | Feature-complete, zero-cost, high-performance, low-memory near-perfect Unity full-platform native C# hot update solution |
| **[Luban](https://github.com/focus-creative-games/luban)** | Best game configuration solution |

### Contributors
[![Contributors](https://contrib.rocks/image?repo=TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/graphs/contributors)
