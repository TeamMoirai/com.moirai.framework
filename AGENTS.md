# Moirai Framework Plugins 仓库指令

使用中文交流、记录决策与交付结果。以当前源码、测试与实际工具读数为准，不把旧文档当 API 保证。
包简介、核心特性与生态依赖见 `README.md`。

## 工作方式
1. 先读相关实现与 `git status`，保留已有修改；不恢复用户删除的文件。
2. 按影响面选验证：小改直接实现并交付摘要；跨模块、高风险、多阶段任务先明确目标与验收，
   用一份任务记录写下决策、授权与证据（模板 `.claude/templates/task.md`）。不要求小改建档。
3. 先检索后动手：客户端提供 CodeGraph MCP 时，符号/调用链检索优先走它；没有就用本仓搜索工具。
   禁止凭空猜测项目内的函数、路径与接口定义，一切以检索到的真实代码为准。
4. 源码用代码编辑工具改；Unity 对象、序列化、资源数据库与 Editor 生命周期走本仓既有通道
   （pipeline 的 `unity` 命令/Editor 脚本），不手改 Scene/Prefab YAML、GUID 或 `.meta` 代替资源数据库操作。
5. 闭环：需求确认 → 读源码与技能 → 实现 → 验证 → 审查 → 证据交付。
6. 交付说明变更、实际执行的验证、剩余风险与阻塞。没有证据的检查不得标成通过。

## 技能路由
按下表读对应技能的 `SKILL.md`，再按需读 reference。技能未出现在客户端菜单时直接读文件，
不依赖用户级安装，也不假设客户端会自动发现 `.claude/skills/`。

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

### 自动触发的技能（Agent Skills，`.claude/skills/`）

| 技能 | 何时被选中 |
|---|---|
| `moirai-plugin-conventions` | 写/改本包代码、跑验证与发版收尾时，按 `SKILL.md` 的路由表加载对应细则块 |

## 授权边界
- 普通源码修改按用户任务实施；Unity 写入先预览具体目标与范围，再集中确认。
- 删除、覆盖、包依赖/项目设置变更、平台切换、真实构建须单独明确授权；计划获准不等于获准发布。
- 不自动安装或升级工具，不提交、推送、上传、签名或发布，不修改用户级技能配置。
- 共享文档（本档、`README.md` 等）改前先过 dirty 门；共享编辑器投单前先查 `unity command test status`。
- 禁碰路径与失效方式见 `.claude/skills/moirai-plugin-conventions/references/gates/forbidden-paths.md`。

## 验证入口
- 本包结构判据走 `/gate:policy`、文档判据走 `/gate:docs`（`.claude/commands/gate/` 内联 python 块为权威），退出码 0 才算过。
- 桥投单协议见 `.claude/skills/moirai-plugin-conventions/references/gates/bridge-protocol.md`。
- 判绿：0 失败才算通过；结论取落盘报告，不以「工具调用返回成功」为结论。
- 本包没有 CI 工作流（无 `.github/`），不跑测试、不产生覆盖率；文档不得声称相反。

## Review
优先报可复现缺陷、资源/事件生命周期、生成代码与模板漂移、序列化兼容性、授权与测试缺口。
源码与文档冲突先核源码；本轮范围内修正文档，否则在任务记录里列出具体差异，不积累无关记忆档。

## 工程事实

### 项目概述

框架包管理器消费的插件目录分支：每个顶层目录是一个可导入插件，由各自的 `<id>.plugin.json` 清单描述。插件本身是框架后端/适配器，不是独立应用。

#### 核心事实

- 15 个插件目录（2026-10-21 实测）；21 个 asmdef，其中 6 个是测试装配。
- 清单必填字段：`id`、`name`、`function`、`description`、`author`、`version`、`minUnityVersion`、`minFrameworkVersion`、`tags`、`dependencies`、`upmDependencies`。
- 插件程序集命名 `Moirai.Plugin.<名>`；多数代码直接扩展框架命名空间（`Moirai.Atropos.*`）。
- 没有根 asmdef，也没有 CI/CHANGELOG。

### 项目结构

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

### 程序集事实

| 事实 | 值 |
|---|---|
| 根 asmdef | 无（根 package.json 只描述目录分支） |
| 插件 asmdef | 21 个，分布在 15 个插件目录 |
| 测试 asmdef | 6 个：addressables、fmod、memorypack、messagepack、protobuf、wwise |
| 依赖方向 | 插件引用框架 `Moirai.Atropos.*` 与外部 SDK；插件之间不互相引用 |
| 宏门控 | 各插件 asmdef 的 `versionDefines` 决定可选依赖 |
| asmref | `primetween/PrimeTween/PrimeTween.Runtime.asmref`（vendored PrimeTween 并入插件程序集） |

### 核心系统

| 类别 | 插件 |
|---|---|
| 日志 | serilog、unity-logging、zlogger、zstring |
| 序列化 | memorypack、messagepack、protobuf、newtonsoft-json |
| 资源 | addressables |
| 音频 | fmod、wwise |
| 存档 | unity-cloud-save |
| 动画/补间 | litmotion、primetween |
| 联网 | photon-fusion |

### 依赖项

根 `package.json` 的 `dependencies` 为空；每个插件在自身 manifest 的 `upmDependencies` 声明 UPM 依赖，外部 SDK 由使用方另行安装。

## 提交纪律

提交信息两段式：subject 一行 `type(scope): 中文摘要` + 至多 3 行细则（细则落点 `.claude/skills/moirai-plugin-conventions/references/release.md`）；按显式路径暂存，提交后 `git show --name-only` 复核。

## 细则在哪

| 主题 | 权威落点 |
|---|---|
| 命名、结构、领域模板、热路径与引擎约束 | `.claude/skills/moirai-plugin-conventions/references/coding.md` |
| 测试装配与替代判据 | `.claude/skills/moirai-plugin-conventions/references/testing.md` |
| 编译、pipeline 测试通道与新鲜度口径 | `.claude/skills/moirai-plugin-conventions/references/verification.md` |
| 提交、文档、版本与发版 | `.claude/skills/moirai-plugin-conventions/references/release.md` |
| 符号真名 | `.claude/skills/moirai-plugin-conventions/references/gates/symbol-truth.md` |
| 宿主编辑器 pipeline 协议字面量 | `.claude/skills/moirai-plugin-conventions/references/gates/bridge-protocol.md` |
| 禁碰路径与静默断裂面 | `.claude/skills/moirai-plugin-conventions/references/gates/forbidden-paths.md` |
| `.meta` 成对 | `.claude/skills/moirai-plugin-conventions/references/gates/metas-pairing.md` |
