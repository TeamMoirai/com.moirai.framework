# Moirai Framework Documentation

> A production-grade Unity game framework for all platforms — service-oriented architecture, high-performance async, hot-update ready.

Welcome to Moirai Framework. This documentation set covers every functional service and core utility. Each page includes core features, core types, quick start, and advanced usage.

- Chinese documentation: [`Documentation~/zh/`](../zh/Index.md)
- Project home and installation guide: [Main README](../../README_EN.md)

---

## Services

| Document | Description |
|----------|-------------|
| [Core](Core.md) | Service system foundation: `ServiceWorld`, `GameServices` registration/lookup/scopes, `[ServiceDependency]` topological init |
| [Resource](Resource.md) | YooAsset-based asset management: sync/async loading, reference counting, encryption, sub-sprites |
| [UI](UI.md) | Production-grade UI framework: stack windows, 5 layers, Widget sub-controls, binding code generation |
| [Audio](Audio.md) | Audio system: category management, AudioAgent playback, mixer, fade in/out, handle control |
| [Localization](Localization.md) | Localization: text/image/audio/Timeline multi-type injection, Google Translate integration |
| [ConfigTable](ConfigTable.md) | Luban config table integration: table loading, lazy access, export toolchain |
| [Procedure](Procedure.md) | Game flow management: startup chain, configurable procedures, self-contained state machine |
| [Input](Input.md) | Multi-platform input abstraction: Input System / Legacy Input / Mobile UI touch, button prompts |
| [Save](Save.md) | Pluggable save system: multi-block container, 4 serialization backends, AES encryption, version migration, codeless component saving, cloud sync |
| [Scene](Scene.md) | Scene management: async load/activate/unload based on YooAsset SceneHandle |
| [Timer](Timer.md) | Dual-engine timer: four-level timing wheel (seconds) + frame counter (frames) as two independent lanes, versioned lane-scoped handles, per-lane prewarming, true concurrency peak statistics |
| [ObjectPool](ObjectPool.md) | Service-level object pool: single/multi-spawn pools, GameObject pool |
| [Debugger](Debugger.md) | Runtime debugger: registerable debug windows, log replay |

## Core Utilities

| Document | Description |
|----------|-------------|
| [Singleton](Singleton.md) | Singleton system: pure C# / MonoBehaviour / registration-based singleton base classes |
| [MemoryPool](MemoryPool.md) | Zero-GC paged memory pool: unmanaged metadata, EWMA adaptive watermarks |
| [GameApp](GameApp.md) | Unity lifecycle proxy: coroutine hosting, frame update injection, engine event injection |
| [PlayerLoopDriver](PlayerLoopDriver.md) | Mono-free PlayerLoop logic driver: zero-alloc handlers, inject/restore, editor visualization |
| [StringUtility](StringUtility.md) | String formatting and building: pluggable Handler, pooled StringBuilder |
| [JsonUtility](JsonUtility.md) | JSON serialization/deserialization: pluggable Handler, byte fast path |
| [ObjectUtility](ObjectUtility.md) | Object instantiation/destruction: pluggable Handler, network-aware |
| [TweenUtility](TweenUtility.md) | Tween animation: pluggable engines (built-in/PrimeTween/LitMotion), unified ease parameter |

---

[« Back to Main README](../../README_EN.md)
