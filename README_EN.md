Moirai Framework
===

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity3d.com/)
[![openupm](https://img.shields.io/npm/v/com.moirai.framework?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.moirai.framework/)
[![Issues](https://img.shields.io/github/issues/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/issues)
[![Last Commit](https://img.shields.io/github/last-commit/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![Top Language](https://img.shields.io/github/languages/top/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![README](https://img.shields.io/badge/README-%E4%B8%AD%E6%96%87-FFA500)](https://github.com/TeamMoirai/com.moirai.framework/blob/main/README.md)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/TeamMoirai/com.moirai.framework)

---

![Alt](https://repobeats.axiom.co/api/embed/131050623959c5a3dd3ad8a5525ab0404783c98b.svg "Repobeats analytics image")

---

## Introduction

**Moirai Framework** is a simple (beginner-friendly, out-of-the-box) and powerful Unity framework for cross-platform development.

### Key Features

- **Out-of-the-Box** - Get started with the entire development workflow in 5 minutes, clean code, clear structure
- **High Performance** - UniTask-based async system, zero-GC event dispatch, strict memory management
- **High Cohesion, Low Coupling** - Modular design, easily remove or replace services you don't need
- **Hot Update Support** - Integrated HybridCLR, full-platform hot update workflow ready
- **Code Obfuscation** - Integrated Obfuz for code obfuscation and hardening, protecting core logic
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
      - [Config Table Service](#config-table-service)
    - [Quick Tips](#quick-tips)
- [Architecture](#architecture)
  - [Service System](#service-system)
  - [Subscriber/Dispatch Exception Tiering](#subscriberdispatch-exception-tiering)
  - [Startup Flow](#startup-flow)
- [Core Services](#core-services)
- [Core Tools](#core-tools)
  - [Attributes — Custom Attributes](#attributes--custom-attributes)
  - [Events — Event System](#events--event-system)
  - [MemoryPool — Memory Pool](#memorypool--memory-pool)
  - [Singleton — Singleton System](#singleton--singleton-system)
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
- [🧪 Testing Conventions](#-testing-conventions)
- [Recommended Project Structure](#recommended-project-structure)
- [Contributing & Support](#contributing--support)
  - [Ecosystem Dependencies](#ecosystem-dependencies)
  - [Contributors](#contributors)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

## 🚀 Quick Start

### Requirements

- **Unity**: 2022.3.x (recommended) or newer
- **Environment**: .NET 4.x
- **Dependency**: [Odin Inspector and Serializer](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041)
- **Platforms**: Windows, OSX, Android, iOS, WebGL, WeChat Mini Games

### Installation

#### Option 1: One-Click Installer (Recommended)

1. Install the **Framework Installer** via either method:

   - In **Window/Package Manager**, install via Git URL:

     ```bash
     https://github.com/TeamMoirai/com.moirai.framework.git#installer
     ```

     ![Package Manager install](Documentation~/.src/quick-start-1.png)

   - Or clone the `installer` branch into your project (Assets/...):

     ```bash
     git clone --branch installer --single-branch https://github.com/TeamMoirai/com.moirai.framework.git Scripts/Installer
     ```

2. Back in Unity, run the menu `Tools/Install Framework`.

3. The installer script can be safely deleted afterwards.

#### Option 2: UPM

1. In **Project Settings/Package Manager**, add a **Scoped Registry** manually:

   ```text
   // Enter the following (international registry)
   Name: Open UPM
   URL: https://package.openupm.com
   Scope(s): com.cysharp
             com.tuyoogame
             com.moirai
   ```

   ![Scoped registries](Documentation~/.src/quick-start-2-scoped-registries.png)

2. In **Window/Package Manager**, select **Moirai Framework** and click **Install**:

   ![Package detail](Documentation~/.src/quick-start-2-package-detail.png)

3. <a id="manual-import"></a>Manually copy everything under the **@Requirements** folder of `ProjectRoot/Library/PackageCache/com.moirai.framework@xxx/Templates~/` into **ProjectRoot/Assets**.

   (Optional) Pick a suitable template from the same folder as needed; **NormalTemplate** is the usual choice.

#### Option 3: Manual

1. Obtain the framework via either method:

   - In **Window/Package Manager**, install via Git URL:

     ```bash
     https://github.com/TeamMoirai/com.moirai.framework.git
     ```

   - Or download the latest **Source Code** archive from Releases.

2. Follow **[step 3 of Option 2](#manual-import)** to copy `Templates~/@Requirements` into your project.

### Initial Setup

#### Scene In Build

Add `Scenes/main.unity` (copied from the template) to the build:

- Unity 6.0+: `File -> Build Profiles -> Scene List`
- Unity 6.0-: `File -> Build Settings -> Scenes In Build`

#### Config Table Service

- In the `Tools/Framework Settings` window, select `[框架]Luban 配置` and click `生成 Config 到指定目录` (menu labels are in Chinese).
- On first generation, run **build-luban** before exporting (the script lives in the template `Config/` folder), or import Luban into your config table root yourself.
- If you move the config table folder, update `[框架]Luban 配置` in `Tools/Framework Settings` — `重定向 Config 目录` (redirect Config directory).

### Running

#### Editor Play

In `Tools/Framework Settings` → `[服务]资源设置`, set `PlayMode` to `EditorSimulate` (editor simulate mode, the default), then press `Play`.

#### Build & Run (Hot Update Workflow)

1. Run `HybridCLR/Install...` to install HybridCLR (menu provided by the HybridCLR package)
2. Run `HybridCLR/Define Symbols/Enable HybridCLR` to enable hot update
3. Run `HybridCLR/Generate/All` for required generation (menu provided by the HybridCLR package)
4. Run `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath` to build the hot update DLLs
5. Run `YooAsset/Bundle Builder` to build AssetBundles (menu provided by the YooAsset package)
6. Open Build Settings and click `Build And Run`

> 💡 **Tip**: For troubleshooting, see [HybridCLR common errors](https://hybridclr.doc.code-philosophy.com/docs/help/commonerrors)

---

## 🏗️ Architecture

```
com.moirai.framework/
├── Runtime/                    # Core framework assembly (Moirai.Atropos)
│   ├── Core/                   # Gameplay-agnostic foundations
│   │   ├── Foundation/         # Building blocks: attributes, constants, data structures, extensions, exceptions, models
│   │   ├── Infrastructure/     # Infrastructure: events, GameApp, profiler, memory pool, obfuscation, pool, singletons, tasks
│   │   └── Utilities/          # Utility set: logging, settings, time, encryption, HTTP, reflection, tween, 30+ more
│   └── Services/               # Functional services (one doc per service, see Documentation~)
│       ├── Kernel/             # Service system base (Contracts / Interception / World)
│       ├── Audio/              # Audio system (categories/agents/mixer/fade)
│       ├── ConfigTable/        # Config table management
│       ├── Debugger/           # Runtime debugger
│       ├── Input/              # Input system (keyboard/mouse/gamepad/mobile)
│       ├── Localization/       # Localization (text/image/audio/Timeline)
│       ├── Pooling/            # Object pool service (generic/GameObject pools)
│       ├── Procedure/          # Procedure management
│       ├── Resource/           # YooAsset asset management
│       ├── Save/               # Save system (multi-block container/multi-backend serialization/encryption/migration/cloud sync)
│       ├── Scene/              # Scene management
│       ├── Timer/              # Timer (time wheel + frame-tick dual engine)
│       └── UI/                 # UI framework (windows/widgets/layers)
├── Editor/                     # Editor toolset (Foundation / Services / Tools / Utilities)
├── Plugins/                    # Third-party libraries
├── Samples~/                   # Examples
├── SourceGenerators/           # Precompiled source generators (HandlerHost / SaveServiceCodegen / ServiceDependency)
├── Templates~/                 # Project initial templates (@Requirements / Config / HybridTemplate / NormalTemplate)
├── Documentation~/             # Bilingual service docs (zh / en)
└── Tests/                      # Unit tests (EditorMode / PlayMode / Player / Coverage)
```

### Service System

The framework uses a **service-oriented architecture** where all subsystems are plain C# classes inheriting `ServiceBase` (not MonoBehaviour), managed by a unified service world `ServiceWorld` for registration, lifecycle, ticking and scoping; the entry point `GameApp` drives the world tick (built-in update loop, engine callback proxy and coroutine hosting).

```csharp
// Service access — each service provides a static facade (HandlerHost generated), lazy-loaded internally
ResourceService.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
UIService.ShowUI<MainWindow>();
TimerService.Delay(1f, () => Debug.Log("1s"));

// Dynamic service lookup
var my = GameServices.GetRequiredService<MyService>();
```

**Service Lifecycle:**
- `OnInit()` — Service initialization; `Shutdown()` — Service destruction (async shutdown via `IAsyncShutdownService`)
- Dependencies are declared via the `[ServiceDependency]` attribute with two-phase construction: `RegisterService` only adds the service to the graph, then `InitializeAsync()` drives all `OnInit` in dependency-graph topological order (missing/circular dependencies fail fast; init order is independent of registration order)
- Implement `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable` interfaces to join the tick loop
- Update order controlled by `Priority` (framework built-in services are uniformly ≤ -1000; business services default to 0 and above); lifecycle scope controlled by `Scope` (App / Scene / Gameplay), auto-cleaning scene and gameplay services on scene unload
- `ServiceWorld` can be `new`-ed for isolated worlds (tests/sandboxes); the `GameServices` static facade is only a projection of the default world

> See **[Core Service System documentation](Documentation~/en/Core.md)** for details (custom services, scope shadowing, cross-service dependencies)

### Startup Chain

The full startup chain is provided by the project template and lives at `Assets/Scripts/GameBase/Procedure/` after installation:

```
ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources
→ ProcedureCreateDownloader → ProcedureDownloadFile → ProcedureDownloadOver
→ ProcedureClearCache → ProcedureLoadAssembly → ProcedurePreload → ProcedurePrepare4Entrance
```

Each stage is an independent `ProcedureBase` state, customizable via `ProcedureServiceSettings` (ScriptableObject).

> See the **[Procedure service documentation](Documentation~/en/Procedure.md)** for details.

---

## 📚 Documentation

Each service doc covers key features, core types, quick start and advanced usage, in both Chinese and English:

- English docs index: **[Documentation~/en/Index.md](Documentation~/en/Index.md)**
- 中文文档索引: **[Documentation~/zh/Index.md](Documentation~/zh/Index.md)**

### Services

| Service | Description | Doc |
|---------|-------------|-----|
| **Kernel** | Service system base: `ServiceWorld` service world, `GameServices` registration/lookup/scoping, `[ServiceDependency]` topological init | [Core.md](Documentation~/en/Core.md) |
| **Resource** | YooAsset-based asset management: sync/async loading, ref counting, encryption, sub-sprites | [Resource.md](Documentation~/en/Resource.md) |
| **UI** | Production-grade UI framework: window stack, five layers, widget sub-controls, binding codegen | [UI.md](Documentation~/en/UI.md) |
| **Audio** | Audio system: category management, AudioAgent playback, mixer, fade in/out, handle control | [Audio.md](Documentation~/en/Audio.md) |
| **Localization** | Localization: text/image/audio/Timeline injection, Google Translate integration | [Localization.md](Documentation~/en/Localization.md) |
| **ConfigTable** | Luban config table integration: table loading with lazy access, table conversion toolchain | [ConfigTable.md](Documentation~/en/ConfigTable.md) |
| **Procedure** | Game flow management: startup chain, configurable procedures, self-contained state machine | [Procedure.md](Documentation~/en/Procedure.md) |
| **Input** | Cross-platform input abstraction: Input System / legacy input / mobile UI touch, key prompts | [Input.md](Documentation~/en/Input.md) |
| **Save** | Pluggable save system: single-file multi-block container, JSON/MessagePack/MemoryPack/Protobuf backends, AES encryption + GZip compression, file-level version migration bus, codeless save components (SourceGenerator), cloud sync | [Save.md](Documentation~/en/Save.md) |
| **Scene** | Scene management: async load/activate/unload via YooAsset SceneHandle | [Scene.md](Documentation~/en/Scene.md) |
| **Timer** | Dual-engine timer: four-level time wheel (seconds) + frame ticking (frames), versioned handles, preheat, statistics | [Timer.md](Documentation~/en/Timer.md) |
| **ObjectPool** | Service-level object pools: single/multi-spawn pools, GameObject pools | [ObjectPool.md](Documentation~/en/ObjectPool.md) |
| **Debugger** | Runtime debugger: registrable debug windows, log replay | [Debugger.md](Documentation~/en/Debugger.md) |

### Core Tools

| Tool | Description | Doc |
|------|-------------|-----|
| **Attributes** | ~20 custom property drawers + Odin extensions (conditional display, inspector buttons, Layer/Tag/asset-path pickers, ProviderDropdown, etc.) | — |
| **Events** | Pooled bubbling event system ported from UIElements: zero-GC, TrickleDown → BubbleUp, `StopPropagation()` / `PreventDefault()`, visual debugger | — |
| **Singleton** | Singleton family: pure C# (volatile double-check) / MonoBehaviour (main-thread materialization) / register-style, thread rules fail fast | [Singleton.md](Documentation~/en/Singleton.md) |
| **MemoryPool** | Zero-GC page-based memory pool: unmanaged metadata, EWMA adaptive watermarks, phase budgets | [MemoryPool.md](Documentation~/en/MemoryPool.md) |
| **GameApp** | Unity lifecycle proxy: coroutine hosting, frame update injection, engine event injection, game speed/pause control | [GameApp.md](Documentation~/en/GameApp.md) |
| **PlayerLoopDriver** | Mono-free PlayerLoop logic driver: zero-allocation handlers, inject/restore, editor visualization | [PlayerLoopDriver.md](Documentation~/en/PlayerLoopDriver.md) |
| **GameLog** | Logging: runtime level filtering, pluggable backends (Default / Serilog / ZLogger / Unity Logging), T4-generated format overloads, intercepts Unity `Debug.Log` | — |
| **GameTime** | Lightweight time accessor, sampled once per frame, avoids frequent `Time.deltaTime` calls | — |
| **GameProfiler** | Conditional-compilation profiling (`PROFILER_ENABLE` macro), zero overhead when off | — |
| **GameSettings** | Framework / graphics / update settings (menu `Tools/Framework Settings`) | — |
| **GameException** | Custom game exceptions: error codes + context | — |
| **ToolRegistry** | High-performance component registry: O(1) lookup, scene-aware cleanup, zero-GC batch queries, thread-safe | — |
| **Obfuz** | [Obfuz](https://github.com/focus-creative-games/obfuz) obfuscation integration: encryption VM auto-initialized after assembly load | — |
| **DataStructure** | `IOCContainer` / `PriorityQueue<T>` / `SparseArray<T>` / `SerializableDictionary<K,V>` / `ShuffleBag<T>` and 7 more | — |
| **Extensions/R3** | R3 reactive extensions + UGUI binding: `OnClickAsObservable()`, `ReactiveProperty` two-way binding | — |
| **StringUtility** | String formatting & building: pluggable handlers, pooled StringBuilder, three usage modes | [StringUtility.md](Documentation~/en/StringUtility.md) |
| **JsonUtility** | JSON serialization: pluggable handlers, byte fast path, allow-list member filtering | [JsonUtility.md](Documentation~/en/JsonUtility.md) |
| **ObjectUtility** | Object instantiate/destroy: pluggable handlers (standalone / Photon Fusion network-aware) | [ObjectUtility.md](Documentation~/en/ObjectUtility.md) |
| **TweenUtility** | Tween system: pluggable engines (in-house / PrimeTween / LitMotion), Bezier paths, unified tween parameters | [TweenUtility.md](Documentation~/en/TweenUtility.md) |

<details>
<summary>Utility — full utility class list</summary>

| Utility | Description |
|---------|-------------|
| `AlgorithmUtility` | Algorithms |
| `AssemblyUtility` | Assembly tools |
| `ColorsUtility` | Color tools |
| `CommandLineUtility` | Command-line parsing |
| `ConverterUtility` | Type conversion |
| `CoroutineUtility` | Coroutine tools |
| `DebugDrawUtility` | Debug drawing |
| `DiagnosticsUtility` | Diagnostics |
| `EncryptionUtility` | Encryption |
| `FileUtility` | File operations |
| `HttpUtility` | HTTP requests (UniTask support) |
| `JsonUtility` | JSON serialization, [doc](Documentation~/en/JsonUtility.md) |
| `MainThreadDispatcher` | Main-thread dispatching |
| `MarshalUtility` | Unmanaged memory operations |
| `MaterialUtility` | Material tools |
| `MathsUtility` | Math tools (Unity.Mathematics integration) |
| `ObjectUtility` | Object instantiate/destroy, [doc](Documentation~/en/ObjectUtility.md) |
| `PathUtility` | Path tools |
| `ReflectionUtility` | Reflection tools |
| `StringUtility` | String formatting & building, [doc](Documentation~/en/StringUtility.md) |
| `ToolRegistry` | Component registry |
| `TweenUtility` | Tween system (Bezier paths), [doc](Documentation~/en/TweenUtility.md) |
| `UniParallel` | UniTask parallel task collector (wait for all) |
| `UnityUtility` | General Unity tools |

</details>

---

## 🛠️ Editor Tools

| Tool | Purpose |
|------|---------|
| Atlas Maker | Atlas creation, reference analysis, auto-regeneration on change (`Tools/图集工具`) |
| Custom Attributes | ~20 custom property drawers + Odin extensions |
| Define Symbols | Debug / Log / Profiler / HybridCLR / Obfuz define symbol management |
| Editor Design | Editor icon assets, GUIStyle viewer |
| Event Debugger | Visual event dispatch debugger (`Window/Event Debugger`) |
| Framework Settings | Framework settings window: audio groups, Luban, procedures, update settings (`Tools/Framework Settings`) |
| HybridCLR | Hot update define symbols & hot update DLL build commands (`HybridCLR/Define Symbols`, `HybridCLR/Build`) |
| Inspector | Custom inspectors for Asset / Core components |
| Luban Tools | Luban config table generation, open table directory (`Tools/Config`) |
| Maintenance | Clean empty folders, find missing scripts, prefab finder, group selection, lock inspector |
| PlayerLoop Debugger | PlayerLoop injection state visualization (`Window/PlayerLoop Debugger`) |
| Reference Finder | Asset dependency/reference tree view (`Tools/资产相关/查找资产引用`) |
| Release Tools | Build pipeline window, one-click build for Android / iOS / Windows / AssetBundle (`Tools/Build`) |
| Save Service | Save browser (`Tools/Moirai/Save/Save Browser`), schema snapshot export, codeless save component editor |
| Tasks Editor | Task runner editor |
| Tween | Tween property drawers |
| UI Service | UI binding code generation (`GameObject/ScriptGenerator`), component inspector |
| Input Service | Input action config editor, key icon collection editor |
| Utility | Command-line reading, shell invocation, etc. |
| YooAsset | Build cache cleanup, built-in directory/patch tools, custom build pipeline, shader variant collection |

---

## 🤝 Contributing & Support

Issues and PRs are welcome. Please read the **[contributing guide](CONTRIBUTING.md)** first — it covers the workflow, testing requirements, and repo-wide code conventions such as the subscriber/dispatch exception tiering.

### 🌟 Ecosystem

| Project | Description |
|---------|-------------|
| **[UniTask](https://github.com/Cysharp/UniTask)** | Efficient, allocation-free async/await integration for Unity. |
| **[YooAsset](https://github.com/tuyoogame/YooAsset)** | Production asset management system proven by games with millions of DAU. |
| **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** | Feature-complete, zero-cost, high-performance native C# hot update solution for all Unity platforms. |
| **[Luban](https://github.com/focus-creative-games/luban)** | The best game configuration solution. |
| **[Obfuz](https://github.com/focus-creative-games/obfuz)** | Deeply integrated Unity code obfuscation & hardening, hot-update compatible. |

### 👥 Contributors

[![Contributors](https://contrib.rocks/image?repo=TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/graphs/contributors)

## 📄 License

[MIT License](LICENSE.txt)
