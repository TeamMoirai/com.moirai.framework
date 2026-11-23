Moirai Framework
===

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity3d.com/)
[![openupm](https://img.shields.io/npm/v/com.moirai.framework?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.moirai.framework/)
[![Issues](https://img.shields.io/github/issues/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/issues)
[![Last Commit](https://img.shields.io/github/last-commit/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![Top Language](https://img.shields.io/github/languages/top/TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/TeamMoirai/com.moirai.framework)

---

![Alt](https://repobeats.axiom.co/api/embed/131050623959c5a3dd3ad8a5525ab0404783c98b.svg "Repobeats analytics image")

---

## 📖 简介

**Moirai Framework** 是一个以「服务契约 + 编译期生成」为骨架的 Unity 客户端框架。每个子系统收敛为唯一契约接口，接线与样板代码由源生成器产出，测试与验收口径以可执行守卫落地。它面向需要长期迭代、多人协作的项目：替换后端不动消费方，接口漂移在编译期暴露，每笔提交留下可复现的判绿证据。三方兼容实现不堆进框架本体，而是外置为按宏门控的插件包。

### ✨ 核心特性

- **契约化服务架构** — 每个服务以唯一 `I*Service` 接口对外，后端直接实现契约并注册进服务世界（绑定即注册，拓扑驱动生命周期）；空壳抽象 Handler 基类已删除，有真实共享实现的家族基座保留。静态门面缓存容器实例并提供高层封装；设置与测试均按接口类型持有，换后端不改消费方。默认语义走接口默认方法（DIM），因此 API Compatibility Level 为 .NET Standard 2.1。
- **源生成器接线** — 5 个预编译生成器分担重复代码：`ProviderHandlerHost` 生成门面懒加载与 Provider 下拉绑定，`ServiceDependency` 生成依赖注册，`SaveServiceCodegen` 生成存档 schema 与迁移骨架，`UIWindowCodegen` 生成 UI 绑定，`HotPathAnalyzer` 在编译期标注热路径。
- **服务世界与作用域** — `ServiceWorld` 可构造隔离世界供测试/沙盒使用；`App / Scene / Gameplay` 三级作用域决定生命周期，场景卸载时自动清理对应服务；依赖以特性声明，两阶段初始化按依赖图拓扑驱动 `OnInit`。
- **零 GC 内存池** — 页式内存池配非托管元数据，按在用量抬升空闲水位线；空闲超时后分 Gameplay / Background / LowMemory 三档预算驱逐，兼顾峰值吞吐与常驻占用。
- **异常分级与故障隔离** — 订阅、派发、任务、服务 Tick、池回调统一按构建分级：Editor / Development 上抛 fail-fast 暴露缺陷，发布构建记录后隔离续跑；生命周期广播等例外无条件隔离，避免漏掉释放与存档。
- **插件化三方后端** — 日志、字符串、补间、网络对象、资源、音频、存档的可选实现不在框架内，各发行一支 `Moirai.Plugin.*`；装了插件才出现在 settings 的 Provider 下拉，未装时框架照常编译。
- **可执行的验证纪律** — 测试分层、反射政策、日志通道都钉成可执行守卫；结论取 `summary.total/failed` 计数，0 失败才算通过；诊断要点写进 commit message，CHANGELOG 只保留净结果。
- **配套工具链** — 包内自带分支作包源的插件包管理器、Luban 转表、图集变动重生成、事件与 PlayerLoop 调试器、发布流水线窗口；AI 协作按 `.agent/skills/` 规范层与 pipeline 验证闭环执行。

---

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
## 📚 目录

- [🚀 快速开始](#-%E5%BF%AB%E9%80%9F%E5%BC%80%E5%A7%8B)
- [🏗️ 架构](#-%E6%9E%B6%E6%9E%84)
  - [服务系统](#%E6%9C%8D%E5%8A%A1%E7%B3%BB%E7%BB%9F)
  - [启动流程](#%E5%90%AF%E5%8A%A8%E6%B5%81%E7%A8%8B)
- [📚 文档](#-%E6%96%87%E6%A1%A3)
- [🛠️ 编辑器工具](#-%E7%BC%96%E8%BE%91%E5%99%A8%E5%B7%A5%E5%85%B7)
- [🤝 贡献与支持](#-%E8%B4%A1%E7%8C%AE%E4%B8%8E%E6%94%AF%E6%8C%81)
  - [🌟 生态依赖](#-%E7%94%9F%E6%80%81%E4%BE%9D%E8%B5%96)
  - [👥 贡献者](#-%E8%B4%A1%E7%8C%AE%E8%80%85)
- [📄 许可证](#-%E8%AE%B8%E5%8F%AF%E8%AF%81)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

## 🚀 快速开始

**环境要求**：Unity 2022.3.x（推荐）或更高、**API Compatibility Level 必须为 .NET Standard 2.1**（*Project Settings → Player → Other Settings → API Compatibility Level*；框架的服务契约接口依赖接口默认方法，.NET Framework 4.x profile 不支持），依赖 [Odin Inspector and Serializer](https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041)；支持 Windows、OSX、Android、iOS、WebGL、微信小游戏。

**安装方式三选一**：

1. **一键安装（推荐）** —— 在 Package Manager 用 Git URL `https://github.com/TeamMoirai/com.moirai.framework.git#installer` 安装，或克隆 `installer` 分支进工程，再执行菜单 `Tools/Install Framework`
2. **UPM 安装** —— 在 Project Settings/Package Manager 添加 Open UPM 的 Scoped Registry，再对 Moirai Framework 点 Install
3. **手动安装** —— 用 Git URL 安装，或下载最新 Release 的 Source Code 压缩包

三种方式的逐步说明与截图、装完后的初始设置（模板复制、场景打包列表、Luban Config 生成）与编辑器/打包运行流程，见 **[安装与运行](Documentation~/Install.md)**。

---

## 🏗️ 架构

```
com.moirai.framework/
├── Runtime/                    # 核心框架程序集 (Moirai.Atropos)
│   ├── Kernel/                 # 内核：服务注册与生命周期（GameServices / ServiceScope / ServiceBase）+ 框架运行底座
│   │   ├── GameApp/            # 启动、帧驱动（PlayerLoop/）、运行期开关
│   │   ├── MemoryPool/         # C# 内存池与通用池件
│   │   ├── GameException/      # 框架异常约定
│   │   ├── FrameworkSettings/  # 设置基座（FrameworkSettings<T> / FrameworkHandler / ProviderHandlerHost）
│   │   ├── GameSettings/       # 游戏设置资产
│   │   ├── Singleton/          # 单例模式
│   │   ├── Tasks/              # 任务/序列系统
│   │   ├── GameProfiler/       # 性能采样
│   │   └── Events/             # 事件总线（EventBus / EventChannel / GameEvent 编译期校验）
│   ├── Foundation/             # 与具体玩法无关的基础件，按功能分组平铺（无层级壳）
│   │   ├── <功能组>/           # Attributes / Constant / DataStructure / Extensions / Obfuz
│   │   └── Utilities/          # 工具集：日志、时间、加密、HTTP、反射、缓动等，一个子目录一个关注点
│   └── Services/               # 功能服务（每个服务一份文档，见 Documentation~），模块内按封闭角色档归位：
│       #  角色档：模块根平铺门面四件套 + Handler/ Models/ Objects/ Internal/ Support/（按需 Extension/ Views/）
│       ├── Audio/              # 音频系统（分类/代理/混音/淡入淡出）
│       ├── ConfigTable/        # 配置表管理
│       ├── Debugger/           # 运行时调试器
│       ├── Input/              # 输入系统（键鼠/手柄/移动端）
│       ├── Localization/       # 本地化（文本/图片/音频/Timeline）
│       ├── Pooling/            # GameObject 池服务（C# 对象复用走 Kernel/MemoryPool）
│       ├── Procedure/          # 流程管理
│       ├── Resource/           # YooAsset 资源管理
│       ├── Save/               # 存档系统（多块容器/多后端序列化/加密/迁移/云同步）
│       ├── Scene/              # 场景管理
│       ├── Timer/              # 计时器（时间轮 + 帧计时双引擎）
│       └── UI/                 # UI 框架（窗口/控件/层）
├── Editor/                     # 编辑器工具集（Kernel / Services / Tools / Utilities；Services 按服务分目录，模块内按需分 Drawers / Windows / BuildPipeline / Support 等档）
├── Plugins/                    # 第三方库
├── Samples~/                   # 示例
├── SourceGenerators/           # 预编译源生成器与分析器（ProviderHandlerHost / SaveServiceCodegen / ServiceDependency / UIWindowCodegen / HotPathAnalyzer）
├── Templates~/                 # 项目初始模板（@Requirements / Config / Sample）
├── Documentation~/             # 服务文档（一个服务一份）
└── Tests/                      # 单元测试（EditorMode / PlayMode / Player）
```

### 服务系统

框架采用**服务化架构**：各子系统的后端 handler 为继承 `ServiceBase` 的普通 C# 类（非 MonoBehaviour），注册进统一服务世界 `ServiceWorld` 实现契约，由世界管理生命周期、轮询与作用域；静态门面（`ProviderHandlerHost` 源生成）是容器实例的公共缓存与高层封装。入口 `GameApp` 驱动世界轮询（内建更新循环、引擎回调代理与协程托管）。

```csharp
// 服务访问 — 静态门面（ProviderHandlerHost 源生成）：容器实例缓存 + 高层封装，懒链兜底注册
AssetService.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
UIService.ShowUI<MainWindow>("MainWindow");
TimerService.Delay(1f, () => Debug.Log("1s"));

// 动态服务查找
var my = GameServices.GetRequiredService<MyService>();
```

**服务生命周期：**

- `OnInit()` — 服务初始化；`Shutdown()` — 服务销毁（支持 `IAsyncShutdownService` 异步关闭）
- 依赖通过 `[ServiceDependency]` 特性声明，两阶段构建：`RegisterService` 仅入图，`InitializeAsync()` 按依赖图拓扑排序统一驱动 OnInit（缺失/循环依赖 fail-fast，初始化顺序与注册顺序无关）
- 实现 `IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable` 接口注册进轮询循环
- 通过 `Priority` 属性控制轮询顺序（框架内置服务统一 ≤ -1000，业务服务默认 0 及以上），通过 `Scope`（App / Scene / Gameplay）控制生命周期范围，场景卸载时自动清理场景与玩法级服务
- `ServiceWorld` 可 `new` 构造隔离世界（测试/沙盒），`GameServices` 静态外观仅是默认世界的投影

> 📖 详细用法（自定义服务、作用域遮蔽、跨服务依赖）见 **[Kernel 服务系统文档](Documentation~/Kernel.md)**

### 启动流程

完整的启动链由模板提供，安装后位于工程 `Assets/Scripts/GameBase/Procedure/`：

```
ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources
→ ProcedureCreateDownloader → ProcedureDownloadFile → ProcedureDownloadOver
→ ProcedureClearCache → ProcedureLoadAssembly → ProcedurePreload → ProcedurePrepare4Entrance
```

每个阶段均为独立的 `ProcedureBase` 状态，可通过 `ProcedureServiceSettings`（ScriptableObject）自定义。

> 📖 详见 **[Procedure 服务文档](Documentation~/Procedure.md)**

---

## 📚 文档

完整文档目录（安装、功能服务、核心工具、工程规范）见 **[Documentation~/Index.md](Documentation~/Index.md)**。

---

## 🛠️ 编辑器工具

| 工具 | 用途 |
|------|------|
| Atlas Maker | 图集创建、引用分析、变动自动重生成、配置面板（`Tools/图集工具`） |
| Custom Attributes | ~20 个自定义属性绘制器 + Odin 扩展 |
| Define Symbols | Debug / Log / Profiler / HybridCLR / Obfuz 宏定义管理 |
| Editor Design | 编辑器图标资源、GUIStyle 查看器 |
| Event Debugger | 可视化事件派发调试窗口（`Window/Event Debugger`） |
| Framework Settings | 框架设置窗口：音频组、Luban、流程、更新设置（`Tools/Framework Settings`） |
| HybridCLR | 热更新宏定义与热更 DLL 构建命令（`HybridCLR/Define Symbols`、`HybridCLR/Build`） |
| Inspector | Asset / Kernel 组件自定义 Inspector |
| Input Service | 输入动作配置编辑器、按键图标集合编辑器 |
| Luban Tools | Luban 配置表生成、表格目录直达（`Tools/Config`） |
| Maintenance | 清理空文件夹、查找丢失脚本、预制体查找器、分组选择、锁定 Inspector |
| Package Manager | 分支作包源的插件包管理器：按目录稀疏检出装卸、依赖闭包规划（`Tools/Moirai/Framework Package Manager`） |
| PlayerLoop Debugger | PlayerLoop 注入状态可视化（`Window/PlayerLoop Debugger`） |
| Reference Finder | 资源依赖/引用树视图（`Tools/资产相关/查找资产引用`） |
| Release Tools | 构建流水线窗口、一键打包 Android / iOS / Windows / AssetBundle（`Tools/Build`） |
| Save Service | 存档浏览器（`Tools/Moirai/Save/Save Browser`）、Schema 快照导出、无代码保存组件编辑器 |
| Tasks Editor | 任务运行器编辑器 |
| Tween | 缓动属性绘制器 |
| UI Service | UI 绑定代码自动生成（`GameObject/ScriptGenerator`）、组件 Inspector |
| Utility | 命令行读取、Shell 调用等 |
| YooAsset | 构建缓存清理、内置目录/补丁包工具、自定义构建管线、Shader 变体收集 |

---

## 🤝 贡献与支持

欢迎 Issue 与 PR。贡献前请阅读 **[贡献指南](CONTRIBUTING.md)**——包含开发流程、测试要求，以及订阅/派发异常分级等全仓一致的代码约定。

### 🌟 生态依赖

| 项目 | 描述 |
|------|------|
| **[UniTask](https://github.com/Cysharp/UniTask)** | 为 Unity 提供高效、无分配的异步/等待集成。 |
| **[YooAsset](https://github.com/tuyoogame/YooAsset)** | 商业级经历百万 DAU 游戏验证的资源管理系统 |
| **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** | 特性完整、零成本、高性能、低内存的近乎完美的 Unity 全平台原生 C# 热更方案。 |
| **[Luban](https://github.com/focus-creative-games/luban)** | 最佳游戏配置解决方案 |
| **[Obfuz](https://github.com/focus-creative-games/obfuz)** | 深度集成 Unity 工作流的代码混淆加固方案，兼容热更新。 |

### 👥 贡献者

[![Contributors](https://contrib.rocks/image?repo=TeamMoirai/com.moirai.framework)](https://github.com/TeamMoirai/com.moirai.framework/graphs/contributors)

---

## 📄 许可证

[MIT License](LICENSE.txt)
