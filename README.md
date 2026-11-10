# Moirai Framework Installer

[![Version](https://img.shields.io/badge/version-1.0.0-blue)](package.json)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/license/MIT)

轻量、零依赖的 Unity 编辑器工具，为 [Moirai Framework](https://github.com/TeamMoirai/com.moirai.framework) 提供一键式引导安装流程。全部逻辑位于单一编辑器脚本 `Editor/FrameworkInstallerWindow.cs`（命名空间 `Moirai.Atropos.Installer.Editor`）。

## 目录

- [概述](#概述)
- [系统要求](#系统要求)
- [安装方式](#安装方式)
- [使用方法](#使用方法)
  - [环境检查](#环境检查)
  - [安装核心包](#安装核心包)
  - [选择模板](#选择模板)
  - [安装模板](#安装模板)
- [安装状态](#安装状态)
- [安装后项目结构](#安装后项目结构)
- [核心框架依赖](#核心框架依赖)
- [菜单参考](#菜单参考)
- [许可证](#许可证)
- [相关链接](#相关链接)

## 概述

安装器通过单一编辑器窗口自动化整个框架搭建流程：

1. **注册表引导** — 自动在项目的 `Packages/manifest.json` 中写入或补全 OpenUPM 作用域注册表，保留用户已有条目
2. **核心安装** — 通过 Unity Package Manager 安装 `com.moirai.framework` 包
3. **模板导入** — 将项目模板（普通版或热更版）复制到 `Assets/` 目录
4. **环境配置** — 应用脚本宏定义与 Player Settings

## 系统要求

- **Unity** 2022.3 或更高版本
- **网络访问**（用于 OpenUPM 与 GitHub 包解析）

## 安装方式

本包托管于 [com.moirai.framework 仓库](https://github.com/TeamMoirai/com.moirai.framework)的 `installer` 分支。在项目的 `Packages/manifest.json` 中添加以下条目：

```json
{
  "dependencies": {
    "com.moirai.framework.installer": "https://github.com/TeamMoirai/com.moirai.framework.git#installer"
  }
}
```

安装器自身没有任何 UPM 依赖（`package.json` 的 `dependencies` 为空）。

## 使用方法

通过 Unity 菜单打开安装器窗口：

```
Tools > Install Framework
```

### 环境检查

窗口会显示带有绿/红状态指示器的仪表盘，检查以下项目：

| 检查项 | 说明 |
|--------|------|
| OpenUPM 注册表 | `https://package.openupm.com` 及作用域 `com.cysharp`、`com.tuyoogame`、`com.moirai` 是否配置齐全 |
| Unity 版本 | 是否为 Unity 2022.3 或更新 |
| 核心包 | `com.moirai.framework` 是否已安装 |
| URP 包 | `com.unity.render-pipelines.universal` 是否已安装（模板安装的前置条件） |
| HybridCLR 包 | `com.code-philosophy.hybridclr` 是否已安装（仅热更模板需要） |
| 安装器状态 | 当前项目状态（未安装 / 自定义 / 普通模板 / 热更模板） |
| 状态来源 | 状态文件、兼容性回退或默认值 |
| 模板文件夹 | 框架包内 `@Requirements`、`NormalTemplate`、`HybridTemplate` 是否存在 |

首次打开窗口时会自动写入或补全 OpenUPM 作用域注册表；注册表发生变更时，安装器会等待 Package Manager 重新解析后再显示仪表盘。

### 安装核心包

点击 **Install Core** 通过 UPM 安装 `com.moirai.framework`。安装器会监控异步操作并在完成后更新界面。核心包安装完成后才能选择模板。

### 选择模板

提供两种模板可供选择：

#### 普通模板（Normal Template）

适用于标准项目的独立框架模板，复制 `@Requirements`（启动器、热更入口、配置代码等基础内容）与 `NormalTemplate`（示例场景、配置表、UI 示例）。

- Player Settings：`allowUnsafeCode = true`
- 脚本宏定义：安装器不写入额外宏

#### 热更模板（Hybrid Template）

适用于使用 [HybridCLR](https://github.com/focus-creative-games/hybridclr) 的热更新项目。

- 脚本宏定义：安装器写入 `ENABLE_HYBRIDCLR`
- Player Settings：`allowUnsafeCode = true`、IL2CPP 后端、.NET Framework API 兼容级别、禁用增量 GC（安装时弹窗确认，可选择自动应用或手动设置）
- **前置要求：** 需已安装 HybridCLR 包

已初始化普通模板的项目，在安装 HybridCLR 包后可升级为热更模板（按钮显示为 **Upgrade Template**）。模板安装不会覆盖已有文件。

### 安装模板

点击 **Install Template** 将所选模板复制到项目中：

1. 先复制 `@Requirements`（两个模板共用的基础内容），再复制所选模板目录
2. 已存在的文件会被保留 — 安装器跳过这些文件并输出警告日志
3. 完成后应用 Player Settings 与脚本宏定义、写入安装状态并刷新资源数据库

此外，操作栏还提供：

- **Use Custom** — 将项目标记为自定义（无需模板），仅在未安装状态下可用
- **Refresh Check** — 手动刷新环境检查结果

## 安装状态

安装器会将状态持久化到以下文件：

```
ProjectSettings/MoiraiFrameworkInstaller.json
```

该文件记录安装器状态、模板类型、Unity 版本、工程路径和更新时间。再次打开时，安装器会将持久化状态与实际文件系统标记（如 `Assets/Scripts/GameProto/Gen`、`Assets/HybridCLRGenerate`）校验，不一致时按「热更模板 → 普通模板 → 未安装」降级，确保状态与工程内容一致。

## 安装后项目结构

安装完成后，项目将具有以下结构（因模板而异）：

```
Assets/
├── AssetRaw/                    # YooAsset 原始资源（场景、配置表、UI 等）
├── Prefabs/                     # CameraRig / GameEntry / UIRoot
├── Resources/                   # 内置文案与加载界面
├── Scenes/                      # 启动场景 main.unity
├── Scripts/
│   ├── GameBase/                # 启动流程（Launcher / Procedure，非热更）
│   ├── GameLib/                 # 框架胶水层（Moirai.GameLib.asmdef）
│   ├── GameLogic/               # 热更逻辑入口 HotfixEntry.cs（Moirai.GameLogic.asmdef）
│   └── GameProto/               # Luban 配置代码（Moirai.GameProto.asmdef）
└── Settings/
    ├── Framework/               # 框架 AudioMixer 等设置
    └── YooAsset/                # YooAsset 收集器与扫描器配置

Packages/
└── com.moirai.framework/        # 核心框架（通过 UPM 安装）

ProjectSettings/
└── MoiraiFrameworkInstaller.json  # 安装器状态
```

## 核心框架依赖

`com.moirai.framework` 包会自动引入以下依赖（以框架包 `package.json` 为准）：

| 包名 | 版本 |
|------|------|
| `com.cysharp.unitask` | 2.5.11 |
| `com.tuyoogame.yooasset` | 3.0.6 |
| `com.unity.scriptablebuildpipeline` | 1.21.5 |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 |
| `com.unity.collections` | 2.2.1 |
| `com.unity.burst` | 1.8.9 |
| `com.unity.mathematics` | 1.3.1 |

## 菜单参考

| 菜单项 | 说明 |
|--------|------|
| `Tools > Install Framework` | 打开安装器窗口 |

## 许可证

MIT（见 `package.json` 的 `license` 字段）

## 相关链接

- [Moirai Framework 仓库](https://github.com/TeamMoirai/com.moirai.framework)
- [OpenUPM](https://openupm.com/)
- [HybridCLR](https://github.com/focus-creative-games/hybridclr)
