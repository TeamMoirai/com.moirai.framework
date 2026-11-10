# Moirai Framework Installer - AI 协作配置

本包（`com.moirai.framework.installer`）是 Moirai 系列里的 UPM 包，版本 `1.0.0`，Unity `2022.3`。本文件是本包自持的硬约束入口：只登记现行事实、摘要与指针，细则权威落点见文末「细则在哪」。

## 项目概述

框架安装器：检查环境与 OpenUPM 作用域注册表、安装核心包 `com.moirai.framework`、导入 Normal/Hybrid 模板，并写入安装状态。

### 核心事实

- 全包只有一个脚本 `Editor/FrameworkInstallerWindow.cs`，命名空间 `Moirai.Atropos.Installer.Editor`，主类 `FrameworkInstallerWindow : EditorWindow`（`Editor/FrameworkInstallerWindow.cs:14`）。
- 菜单入口 `Tools/Install Framework`（`MENU_PATH`，`:16`）。
- 模板路径指向框架包 `Packages/com.moirai.framework/Templates~` 的 `@Requirements`、`NormalTemplate`、`HybridTemplate`（`:17-20`）。
- 安装状态写 `ProjectSettings/MoiraiFrameworkInstaller.json`（`INSTALL_STATE_PATH`，`:34`）。
- 包级配置：OpenUPM 作用域 `com.cysharp`、`com.tuyoogame`、`com.moirai`；可选 URP 与 HybridCLR（`ENABLE_HYBRIDCLR`）。
- 没有 asmdef、测试装配、CI 或 CHANGELOG。

## 项目结构

```
com.moirai.framework.installer/
├── Editor/FrameworkInstallerWindow.cs   # 唯一脚本（#if UNITY_EDITOR）
├── README.md
└── package.json
```

## 程序集事实

| 事实 | 值 |
|---|---|
| asmdef | 无 |
| 编译归属 | `Editor/` 下的脚本由宿主工程编入预定义编辑器程序集（`Assembly-CSharp-Editor` 一类），不是独立包 DLL |
| 运行期 | 无；安装器只在编辑器里运行 |
| asmref | 无 |
| 测试装配 | 无 |

## 核心系统

| 系统 | 放什么 |
|---|---|
| 安装窗口 | `OnGUI` 分阶段界面：环境检查、核心安装、模板选择、动作 |
| 注册表 | `EnsureRequiredScopedRegistry` / `AddOrUpdateScopedRegistry` |
| 核心安装 | `InstallCorePackage` / `MonitorCoreInstall`（Package Manager Request） |
| 模板导入 | `CopyTemplateDirectory`、`ApplyScriptingDefineSymbols`、`ApplyPlayerSettings` |
| 安装状态 | `SaveInstallState` / `ReadInstallState` / `ValidatePersistedInstallState` |

## 编码规范

细则在 `.agent/skills/moirai-framework-installer-conventions/references/coding.md`，此处只登记打回档：

- **Mandatory：** 编辑器脚本整文件 `#if UNITY_EDITOR` 守卫；不新增 asmdef（安装器靠宿主预定义编辑器程序集）；写 `Packages/manifest.json` 前先读再改，保留用户已有条目；模板复制不覆盖用户改动；安装状态读写保持向后兼容。
- **Prefer：** 私有字段 `_camelCase`（如 `_registryResolveRequest`）；常量 `UPPER_SNAKE`；菜单路径与状态文件路径集中在类顶常量；UI 文案跟随 README 中英双语。
- **Reference：** 存量命名/引用差异，改动处顺手收，不单开一轮统一。

## 测试规范

本包没有测试装配：无 `Tests/`、无 asmdef、无 PlayMode。没有 CI 工作流。

## AI 测试流程

细则在 `.agent/skills/moirai-framework-installer-conventions/references/verification.md`，协议字面量在 `.agent/skills/moirai-framework-installer-conventions/references/gates/bridge-protocol.md`。连接 Unity 只走 pipeline（Unity 6 装 registry 版 `com.unity.pipeline`；Unity 2022 通过 MoiraiFrameworkPackage 安装兼容版）。

- 安装器的行为依赖宿主工程的 Package Manager 与 ProjectSettings；编译门只能证明脚本编过，不能证明安装流程正确。
- 本包无测试装配
- 增删 `.cs` 后让一次 `unity recompile`（含 asset refresh）落地即可；pipeline 不可用时等一次真正的导入 + 域重载，不绕道、不用 batchmode 开同一工程。

## AI 命令

| 入口 | 用途 |
|---|---|
| `/change:fix-bug` | 定位并修复缺陷：先构造可复现红态再改盘 |
| `/change:refactor` | 结构性改造，序列化字段与公共面不动 |
| `/change:optimize` | 热路径与分配优化，同轮 A/B |
| `/change:migrate` | 公共 API、命名空间与序列化字段迁移 |
| `/check:review` | 按 Mandatory/Prefer/Reference 三档评审，只读 |
| `/check:explain` | 读实现体后解释契约与行为，只读 |
| `/gate:verify` | 编译门与结构判据取证，只读 |
| `/gate:policy` | meta 成对、asmdef/asmref 与禁碰面自查，只读 |
| `/gate:docs` | 文档、包版本与结构事实对齐，只读 |
| `/gate:release` | 发版前本地预检，只读 |

本包是单一安装器窗口，没有仓库专属的 `make:*` 创建命令；改动集中在 `Editor/FrameworkInstallerWindow.cs`。

## 自动触发的技能

| 技能 | 何时被选中 |
|---|---|
| `moirai-framework-installer-conventions` | 写/改本包代码、跑验证与发版收尾时，按 `SKILL.md` 的路由表加载对应细则块 |

## 开发流程

### 1. 新功能

定契约 → 定数据/资产 → 实现 → 编辑器面 → 编译门 → 测试或人工点验。

### 2. Bug 修复

先构造可复现红态，再改盘；反复红先拆「上游自证一格 + 下游断言一格」再谈归属。

### 3. 代码优化

先量再改：建测量环路，改动后同轮 A/B；跨轮时间差不算数。

### 4. 提交纪律

中文 conventional commits、按显式路径暂存、提交后 `git show --name-only` 复核；本包没有发版工作流；`package.json` 版本 1.0.0 由发布动作维护，日常提交不碰 `version`。

## 依赖项

`package.json` 的 `dependencies` 为空；安装器安装的是框架包 `com.moirai.framework`，并可选 URP/HybridCLR。

## 细则在哪

| 主题 | 权威落点 |
|---|---|
| 命名、结构、领域模板、热路径与引擎约束 | `.agent/skills/moirai-framework-installer-conventions/references/coding.md` |
| 测试装配与替代判据 | `.agent/skills/moirai-framework-installer-conventions/references/testing.md` |
| 编译、pipeline 协议与新鲜度口径 | `.agent/skills/moirai-framework-installer-conventions/references/verification.md` |
| 提交、文档、版本与发版 | `.agent/skills/moirai-framework-installer-conventions/references/release.md` |
| 符号真名 | `.agent/skills/moirai-framework-installer-conventions/references/gates/symbol-truth.md` |
| 宿主编辑器 pipeline 协议字面量 | `.agent/skills/moirai-framework-installer-conventions/references/gates/bridge-protocol.md` |
| 禁碰路径与静默断裂面 | `.agent/skills/moirai-framework-installer-conventions/references/gates/forbidden-paths.md` |
| `.meta` 成对 | `.agent/skills/moirai-framework-installer-conventions/references/gates/metas-pairing.md` |
