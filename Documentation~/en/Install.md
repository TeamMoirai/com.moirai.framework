# Installation and Running

> From requirements to the first run: the three installation options, initial setup, and the editor / build-and-run workflows.

---

## Requirements

- **Unity**: 2022.3.x (recommended) or newer
- **Environment**: .NET 4.x
- **Dependency**: [Odin Inspector and Serializer](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041)
- **Platforms**: Windows, OSX, Android, iOS, WebGL, WeChat Mini Games

---

## Installation

### Option 1: One-Click Installer (Recommended)

1. Install the **Framework Installer** via either method:

   - In **Window/Package Manager**, install via Git URL:

     ```bash
     https://github.com/TeamMoirai/com.moirai.framework.git#installer
     ```

     ![Package Manager install](../.src/Install/installer-git-url.png)

   - Or clone the `installer` branch into your project (Assets/...):

     ```bash
     git clone --branch installer --single-branch https://github.com/TeamMoirai/com.moirai.framework.git Scripts/Installer
     ```

2. Back in Unity, run the menu `Tools/Install Framework`.

3. The installer script can be safely deleted afterwards.

### Option 2: UPM

1. In **Project Settings/Package Manager**, add a **Scoped Registry** manually:

   ```text
   // Enter the following (international registry)
   Name: Open UPM
   URL: https://package.openupm.com
   Scope(s): com.cysharp
             com.tuyoogame
             com.moirai
   ```

   ![Scoped registries](../.src/Install/upm-scoped-registries.png)

2. In **Window/Package Manager**, select **Moirai Framework** and click **Install**:

   ![Package detail](../.src/Install/upm-package-detail.png)

3. <a id="manual-import"></a>Manually copy everything under the **@Requirements** folder of `ProjectRoot/Library/PackageCache/com.moirai.framework@xxx/Templates~/` into **ProjectRoot/Assets**.

   (Optional) Pick a suitable template from the same folder as needed; **NormalTemplate** is the usual choice.

### Option 3: Manual

1. Obtain the framework via either method:

   - In **Window/Package Manager**, install via Git URL:

     ```bash
     https://github.com/TeamMoirai/com.moirai.framework.git
     ```

   - Or download the latest **Source Code** archive from Releases.

2. Follow **[step 3 of Option 2](#manual-import)** to copy `Templates~/@Requirements` into your project.

---

## Initial Setup

### Scene In Build

Add `Scenes/main.unity` (copied from the template) to the build:

- Unity 6.0+: `File -> Build Profiles -> Scene List`
- Unity 6.0-: `File -> Build Settings -> Scenes In Build`

### Config Table Service

- In the `Tools/Framework Settings` window, select `[框架]Luban 配置` and click `生成 Config 到指定目录` (menu labels are in Chinese).
- On first generation, run **build-luban** before exporting (the script lives in the template `Config/` folder), or import Luban into your config table root yourself.
- If you move the config table folder, update `[框架]Luban 配置` in `Tools/Framework Settings` — `重定向 Config 目录` (redirect Config directory).

---

## Running

### Editor Play

In `Tools/Framework Settings` → `[服务]资源设置`, set `PlayMode` to `EditorSimulate` (editor simulate mode, the default), then press `Play`.

### Build & Run (Hot Update Workflow)

1. Run `HybridCLR/Install...` to install HybridCLR (menu provided by the HybridCLR package)
2. Run `HybridCLR/Define Symbols/Enable HybridCLR` to enable hot update
3. Run `HybridCLR/Generate/All` for required generation (menu provided by the HybridCLR package)
4. Run `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath` to build the hot update DLLs
5. Run `YooAsset/Bundle Builder` to build AssetBundles (menu provided by the YooAsset package)
6. Open Build Settings and click `Build And Run`

> 💡 **Tip**: For troubleshooting, see [HybridCLR common errors](https://hybridclr.doc.code-philosophy.com/docs/help/commonerrors)

The full startup chain and each stage's configurable options are covered in the [Procedure service doc](Procedure.md).

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [中文](../zh/Install.md)
