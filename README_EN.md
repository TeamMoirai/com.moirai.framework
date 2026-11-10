# Moirai Framework Installer

[![Version](https://img.shields.io/badge/version-1.0.0-blue)](package.json)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/license/MIT)
[![README](https://img.shields.io/badge/README-中文-red)](https://github.com/TeamMoirai/com.moirai.framework/blob/installer/README.md)

A lightweight, zero-dependency Unity Editor tool that provides a guided installation workflow for the [Moirai Framework](https://github.com/TeamMoirai/com.moirai.framework). All logic lives in a single editor script, `Editor/FrameworkInstallerWindow.cs` (namespace `Moirai.Atropos.Installer.Editor`).

## Table of Contents

- [Overview](#overview)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
  - [Environment Check](#environment-check)
  - [Install Core](#install-core)
  - [Choose a Template](#choose-a-template)
  - [Install a Template](#install-a-template)
- [Install State](#install-state)
- [Project Structure After Installation](#project-structure-after-installation)
- [Core Framework Dependencies](#core-framework-dependencies)
- [Menu Reference](#menu-reference)
- [License](#license)
- [Links](#links)

## Overview

The installer automates the entire framework setup process through a single editor window:

1. **Registry Bootstrap** — Writes or completes the OpenUPM scoped registry in your project's `Packages/manifest.json`, preserving existing entries
2. **Core Installation** — Installs the `com.moirai.framework` package via Unity Package Manager
3. **Template Import** — Copies a project template (Normal or Hybrid) into your `Assets/` folder
4. **Environment Configuration** — Applies scripting defines and Player Settings

## Requirements

- **Unity** 2022.3 or newer
- **Internet access** (for OpenUPM and GitHub package resolution)

## Installation

This package is hosted on the `installer` branch of the [com.moirai.framework repository](https://github.com/TeamMoirai/com.moirai.framework). Add the following entry to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.moirai.framework.installer": "https://github.com/TeamMoirai/com.moirai.framework.git#installer"
  }
}
```

The installer itself has no UPM dependencies (the `dependencies` field in its `package.json` is empty).

## Usage

Open the installer window via the Unity menu:

```
Tools > Install Framework
```

### Environment Check

The window displays a dashboard with green/red status indicators for:

| Check | Description |
|-------|-------------|
| OpenUPM Registry | `https://package.openupm.com` with scopes `com.cysharp`, `com.tuyoogame`, `com.moirai` fully configured |
| Unity Version | Unity 2022.3 or newer detected |
| Core Package | `com.moirai.framework` installed |
| URP Package | `com.unity.render-pipelines.universal` installed (required before installing templates) |
| HybridCLR Package | `com.code-philosophy.hybridclr` installed (Hybrid template only) |
| Installer State | Current project state (NotInstalled / Custom / Normal / Hybrid) |
| State Source | State file, compatibility fallback, or default |
| Template Folders | `@Requirements`, `NormalTemplate`, `HybridTemplate` present in the framework package |

The first time the window opens, it automatically writes or completes the OpenUPM scoped registry. If the registry changed, the installer waits for the Package Manager to re-resolve before showing the dashboard.

### Install Core

Click **Install Core** to install `com.moirai.framework` via UPM. The installer monitors the async operation and updates the UI on completion. Templates can only be selected after the core package is installed.

### Choose a Template

Two templates are available:

#### Normal Template

Standalone framework template for standard projects. Copies `@Requirements` (launcher, hot-update entry, config code, and other shared content) plus `NormalTemplate` (example scene, config tables, and UI examples).

- Player settings: `allowUnsafeCode = true`
- Scripting defines: the installer adds no extra defines

#### Hybrid Template

Hot-update template for projects using [HybridCLR](https://github.com/focus-creative-games/hybridclr).

- Scripting define: the installer adds `ENABLE_HYBRIDCLR`
- Player settings: `allowUnsafeCode = true`, IL2CPP backend, .NET Framework API compatibility level, incremental GC disabled (a confirmation dialog appears during installation — apply automatically or configure manually)
- **Requires:** HybridCLR package installed

A project initialized with the Normal template can be upgraded to the Hybrid template once the HybridCLR package is installed (the button then reads **Upgrade Template**). Template installation never overwrites existing files.

### Install a Template

Click **Install Template** to copy the selected template into your project:

1. `@Requirements` (shared by both templates) is copied first, followed by the selected template folder
2. Existing files are preserved — the installer skips them and logs warnings
3. Afterwards it applies Player Settings and scripting defines, saves the install state, and refreshes the AssetDatabase

The action bar also provides:

- **Use Custom** — Marks the project as custom (no template required); only available in the NotInstalled state
- **Refresh Check** — Manually refresh the environment check at any time

## Install State

The installer persists its state to:

```
ProjectSettings/MoiraiFrameworkInstaller.json
```

This file records the installer state, template type, Unity version, project path, and update timestamp. On subsequent opens, the installer validates the persisted state against actual filesystem markers (such as `Assets/Scripts/GameProto/Gen` and `Assets/HybridCLRGenerate`), falling back from Hybrid → Normal → NotInstalled on mismatch so the state always matches the project content.

## Project Structure After Installation

After installation, your project will have the following structure (varies by template):

```
Assets/
├── AssetRaw/                    # YooAsset raw assets (scenes, config tables, UI, etc.)
├── Prefabs/                     # CameraRig / GameEntry / UIRoot
├── Resources/                   # Built-in text and loading UI
├── Scenes/                      # Startup scene main.unity
├── Scripts/
│   ├── GameBase/                # Startup flow (Launcher / Procedure, non-hot-update)
│   ├── GameLib/                 # Framework glue layer (Moirai.GameLib.asmdef)
│   ├── GameLogic/               # Hot-update entry HotfixEntry.cs (Moirai.GameLogic.asmdef)
│   └── GameProto/               # Luban config code (Moirai.GameProto.asmdef)
└── Settings/
    ├── Framework/               # Framework AudioMixer and other settings
    └── YooAsset/                # YooAsset collector and scanner settings

Packages/
└── com.moirai.framework/        # Core framework (installed via UPM)

ProjectSettings/
└── MoiraiFrameworkInstaller.json  # Installer state
```

## Core Framework Dependencies

The `com.moirai.framework` package pulls in the following dependencies automatically (see the framework package's `package.json` for the authoritative list):

| Package | Version |
|---------|---------|
| `com.cysharp.unitask` | 2.5.11 |
| `com.tuyoogame.yooasset` | 3.0.6 |
| `com.unity.scriptablebuildpipeline` | 1.21.5 |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 |
| `com.unity.collections` | 2.2.1 |
| `com.unity.burst` | 1.8.9 |
| `com.unity.mathematics` | 1.3.1 |

## Menu Reference

| Menu Item | Description |
|-----------|-------------|
| `Tools > Install Framework` | Opens the installer window |

## License

MIT (see the `license` field in `package.json`)

## Links

- [Moirai Framework Repository](https://github.com/TeamMoirai/com.moirai.framework)
- [OpenUPM](https://openupm.com/)
- [HybridCLR](https://github.com/focus-creative-games/hybridclr)
