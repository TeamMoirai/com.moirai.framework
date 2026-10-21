# Moirai Framework Plugins - Claude Code 配置

本包（`com.moirai.framework.plugin`）是 Moirai 系列里的 UPM 包，版本 `0.1.0`，Unity `2022.3`。本文件是本包自持的硬约束入口：只登记现行事实、摘要与指针，细则权威落点见文末「细则在哪」。

## 项目概述

框架包管理器消费的插件目录分支：每个顶层目录是一个可导入插件，由各自的 `<id>.plugin.json` 清单描述。插件本身是框架后端/适配器，不是独立应用。

### 核心事实

- 15 个插件目录（2026-10-21 实测）；21 个 asmdef，其中 6 个是测试装配。
- 清单必填字段：`id`、`name`、`function`、`description`、`author`、`version`、`minUnityVersion`、`minFrameworkVersion`、`tags`、`dependencies`、`upmDependencies`。
- 插件程序集命名 `Moirai.Plugin.<名>`；多数代码直接扩展框架命名空间（`Moirai.Atropos.*`）。
- 没有根 asmdef，也没有 CI/CHANGELOG。

## 项目结构

```
com.moirai.framework.plugin/
├── addressables/  fmod/  litmotion/  memorypack/  messagepack/
├── newtonsoft-json/  photon-fusion/  primetween/  protobuf/
├── serilog/  unity-cloud-save/  unity-logging/  wwise/
├── zlogger/  zstring/
│   ├── <id>.plugin.json      # 插件清单
│   ├── Moirai.Plugin.<名>.asmdef
│   └── Tests/                # 仅部分插件有
└── package.json             # 目录分支自身的包元数据
```

## 程序集事实

| 事实 | 值 |
|---|---|
| 根 asmdef | 无（根 package.json 只描述目录分支） |
| 插件 asmdef | 21 个，分布在 15 个插件目录 |
| 测试 asmdef | 6 个：addressables、fmod、memorypack、messagepack、protobuf、wwise |
| 依赖方向 | 插件引用框架 `Moirai.Atropos.*` 与外部 SDK；插件之间不互相引用 |
| 宏门控 | 各插件 asmdef 的 `versionDefines` 决定可选依赖 |
| asmref | `primetween/PrimeTween/PrimeTween.Runtime.asmref`（vendored PrimeTween 并入插件程序集） |

## 核心系统

| 类别 | 插件 |
|---|---|
| 日志 | serilog、unity-logging、zlogger、zstring |
| 序列化 | memorypack、messagepack、protobuf、newtonsoft-json |
| 资源 | addressables |
| 音频 | fmod、wwise |
| 存档 | unity-cloud-save |
| 动画/补间 | litmotion、primetween |
| 联网 | photon-fusion |

## 编码规范

细则在 `.claude/skills/moirai-plugin-conventions/references/coding.md`，此处只登记打回档：

- **Mandatory：** 清单字段必须与该插件的真实依赖一致（`minFrameworkVersion`、`minUnityVersion`、`upmDependencies`）；asmdef 引用与 `versionDefines` 必须匹配；不得把宿主工程设置或第三方 SDK 源码塞进插件目录；测试只放在 `*/Tests/` 的独立装配。
- **Prefer：** 插件 asmdef 用 `Moirai.Plugin.<名>`；manifest `id` 用小写 kebab-case；代码命名跟随所扩展的框架模块。
- **Reference：** 存量命名/引用差异，改动处顺手收，不单开一轮统一。

## 测试规范

本包没有统一测试装配；只有 addressables、fmod、memorypack、messagepack、protobuf、wwise 六个插件带 `*/Tests/` 测试 asmdef（2026-10-21 实测，各 1 个测试文件）。其余插件无测试装配。

## AI 测试流程

细则在 `.claude/skills/moirai-plugin-conventions/references/verification.md`，协议字面量在 `.claude/skills/moirai-plugin-conventions/references/gates/bridge-protocol.md`。

- 测试请求必须按插件选择真实存在的装配（如 `Moirai.Plugin.MessagePack.Tests`）；没有测试装配的插件不得编造装配名。
- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无
- 增删 `.cs` 后先 `refresh` 再 `recompile`；桥不可用时等一次真正的导入 + 域重载，不绕桥、不用 batchmode 开同一工程。

## Claude Code 命令

| 入口 | 用途 |
|---|---|
| `/make:new-plugin` | 新增插件目录、manifest、asmdef 与最小适配面 |
| `/make:new-plugin-test` | 为插件新增 `*/Tests/` 测试装配与用例 |
| `/check:manifest` | 只读校验插件清单字段与依赖一致性 |
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

## 自动触发的技能

| 技能 | 何时被选中 |
|---|---|
| `moirai-plugin-conventions` | 写/改本包代码、跑验证与发版收尾时，按 `SKILL.md` 的路由表加载对应细则块 |

## 开发流程

### 1. 新功能

定契约 → 定数据/资产 → 实现 → 编辑器面 → 编译门 → 测试或人工点验。

### 2. Bug 修复

先构造可复现红态，再改盘；反复红先拆「上游自证一格 + 下游断言一格」再谈归属。

### 3. 代码优化

先量再改：建测量环路，改动后同轮 A/B；跨轮时间差不算数。

### 4. 提交纪律

中文 conventional commits、按显式路径暂存、提交后 `git show --name-only` 复核；本目录没有发版工作流；根 `package.json` 与各插件 manifest 的版本各自独立，日常提交不批量改版本。

## 依赖项

根 `package.json` 的 `dependencies` 为空；每个插件在自身 manifest 的 `upmDependencies` 声明 UPM 依赖，外部 SDK 由使用方另行安装。

## 细则在哪

| 主题 | 权威落点 |
|---|---|
| 命名、结构、领域模板、热路径与引擎约束 | `.claude/skills/moirai-plugin-conventions/references/coding.md` |
| 测试装配与替代判据 | `.claude/skills/moirai-plugin-conventions/references/testing.md` |
| 编译、测试桥与新鲜度口径 | `.claude/skills/moirai-plugin-conventions/references/verification.md` |
| 提交、文档、版本与发版 | `.claude/skills/moirai-plugin-conventions/references/release.md` |
| 符号真名 | `.claude/skills/moirai-plugin-conventions/references/gates/symbol-truth.md` |
| 桥协议字面量 | `.claude/skills/moirai-plugin-conventions/references/gates/bridge-protocol.md` |
| 禁碰路径与静默断裂面 | `.claude/skills/moirai-plugin-conventions/references/gates/forbidden-paths.md` |
| `.meta` 成对 | `.claude/skills/moirai-plugin-conventions/references/gates/metas-pairing.md` |
