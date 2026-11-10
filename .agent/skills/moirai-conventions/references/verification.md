# 验证流程细则（框架包）

本块是本包该主题的唯一权威落点：`AGENTS.md` 与命令正文只留摘要与指针，同一段话在两边各写一遍即视为缺陷。

以下两段验证环路原先挂在 `## 开发流程` 下，现与本主题同处一块。

## AI 测试流程

面向代理的执行流程；规范全文见 [`Documentation~/Testing.md`](Documentation~/Testing.md)。

### 1. 何时必须写测试

**必须**：新增或修改框架对外契约（服务外观、Handler 契约、公共 API 语义）；修 bug（先构造可复现失败用例）；性能承诺（0-GC/帧预算）；状态机与生命周期；序列化/迁移/编解码。

**不必**：纯注释与文档改动；无行为变化的内部重命名；`Editor` 下一次性的手动工具脚本（除非含可复现的纯函数逻辑）。

### 2. 写作顺序（不许跳步）

1. **读被测**：读实现与相邻用例，弄清契约与既有夹具基座；确认要断的是**行为**而非实现。
2. **选层**：按上表选 L1/L2/L3；能在 EditMode 判定就 L1。
3. **先写失败用例**：新增用例必须**先红后绿**——先跑一次确认它真的能失败（否则它可能什么都没断），再改代码让它变绿。这是"用例有效"的唯一证据。
4. **实现 / 修复**。
5. **验证**：编译 0 error → 目标夹具过滤回归 → 全量回归 0 失败。
6. **同步文档**：公共 API 变更同步 `Documentation~/<模块>.md`；契约守卫基线变更同步常数与 `CHANGELOG.md`。

### 3. 选通道

连接 Unity 只走 pipeline：Unity 侧 `com.unity.pipeline` 包，命令行侧 `unity` CLI。Unity 6 装 registry 版；Unity 2022 通过 MoiraiFrameworkPackage 安装兼容版 pipeline（框架包管理器 `Tools/Moirai/Framework Package Manager`）。

| 场景 | 通道 |
|---|---|
| 连编辑器 / 判活 / 判空闲 / 判域重载 | `unity status`（见《验证：用 pipeline 连编辑器》） |
| EditMode / PlayMode 全量或过滤 | `unity command test run`（见《验证：用 pipeline 跑测试》） |
| 强制重编 / 编译结论 | `unity command script recompile` + `script recompile_status`，`console status` 复核 |
| 一次性编排（测试通道覆盖不了） | `unity command eval '<C#>'` |
| L3 玩家验收 | 玩家通道二选一：Test Runner 窗口 PlayMode 页签 → `Run all in Player`、CLI `-runTests -testPlatform <BuildTarget>`（详见《测试规范》玩家侧用例的运行方式）；不要自己 `BuildPipeline.BuildPlayer` 搭测试玩家 |

命令名与参数以 `unity command` 的现读与 `unity <命令> -h` 为准，不要凭记忆补参数。

### 4. 证据纪律

- **读回执的计数与逐格结果**：`summary.total/passed/failed/skipped/inconclusive` 与 `results[]` 才是结论；`mode` 与 `filterApplied` 要回显成本次真跑的东西。
- **过滤器不能空**：不带 `filter` 等于跑整个套件；圈错粒度会静默跑成 `total 0`，先读计数再谈结论。
- **投测前先判活**：`unity status` 的 `status` 为 `ready`、`compiling` 与 `domainReloadInProgress` 皆 false 才有接单窗口；编辑器是共享的，别的会话随时占住它。
- **以回执为准**：结论取自 `summary` 与 `results[]`，不以"工具调用返回成功"为结论。
- **超时 ≠ 失败**：`timeout` 到点只说明编辑器忙或还没跑完；用 `unity status` 的 `lastHeartbeat` / `codeLoadCount` 判活，**不要重复投单**（会叠加运行）。

### 5. 判绿门禁

**0 失败才算通过。** 新增用例必须"先红后绿"；既有失败必须归因——先 `git diff` 排除并行改动（本项目用户会与代理并行编辑，也会 rebase/amend），确认是本次引入才动手修。

**不允许**：用 `Assert.Ignore` 掩盖 flaky；直接改契约守卫常数让它变绿；把"测不出"当成"没问题"（0-GC 计量走 `GC.Alloc` 采样、编辑器同样有牙；但发布出口只能 L3 玩家验收，编辑器跑绿不替代玩家报告）。

### 6. 忙碌期与域重载处置

- 编辑器编译/域重载期间 pipeline 的命令会排队或报忙——**不要重复投单**，等 `unity status` 的 `compiling` / `domainReloadInProgress` 落回 false 再投。
- 域重载后 `unity command` 的旧连接可能指向旧域——用 `unity status` 的 `codeLoadCount` 确认新域已加载，必要时重连。
- 收尾前确认编辑器空闲（`status` 为 `ready`、`compiling` 为 false、`test status` 为 `no_tests`）。

### 验证：用 pipeline 连编辑器

`unity status` 是连接与判活的唯一入口，取代过去每 ~1 秒覆写状态文件的做法：

```bash
unity status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
unity status --until-ready --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
```

- **连接**：`status` 为 `ready`。`STATUS_NO_INSTANCES` = 没有可连的编辑器；`STATUS_PIPELINE_LOAD_PENDING` = 包刚装、编辑器还没刷新。
- **Safe Mode**：工程有编译错误时编辑器以 Safe Mode 启动，pipeline 包不加载，所有 `unity` 命令都连不上。用 `unity pipeline list` 确认后修编译错误并重启 Unity。
- **判活**：`lastHeartbeat` 陈旧几秒即主线程没在跑 `update`——导入中、域重载中、被原生模态框挡住（`dialog` 非空），或 Interaction Mode 限流。真原因去 `Editor.log`（Windows `%LOCALAPPDATA%\Unity\Editor\Editor.log`；macOS `~/Library/Logs/Unity/Editor.log`）。
- **判新域**：`codeLoadCount` 递增就是脚本重载真发生过；它跨域不清零，能区分"重载"与"重启"（重启后回到 1）。
- **判空闲**：`compiling`、`domainReloadInProgress` 皆 false，且 `unity command test status` 返回 `no_tests`。
- **多个编辑器**：`projectPath` 就是 `--project-path` 该给的值；匹配不上会报 `AMBIGUOUS_EDITOR`。

### 验证：用 pipeline 跑测试

```bash
unity command test run --mode editor --filter "<命名空间.类名.方法名>" --filter-type testName --timeout 180 --format json
unity command test run --mode playmode --filter "<程序集名>" --filter-type assembly --async-tests --format json
unity command test status --format json
unity command test cancel
```

- `mode` 取 `all` / `editor` / `playmode`；`filter-type` 取 `testName` / `assembly` / `category`；`filter` 是大小写不敏感的**部分匹配**，不是正则。
- `[Explicit]` 的基准要跑必须显式带 `include-explicit`。
- 同步档（默认）直接返回；`async-tests` 立即返回，靠 `test status` 轮询。PlayMode 会触发域重载，走异步档。
- 回执字段：`summary.total/passed/failed/skipped/inconclusive`、`results[]`（逐格消息与堆栈）、`duration`、`mode`、`filterApplied`。**先读 `total`**——圈漏了会是 `total 0` 而不是报错。
- 一次只跑一单；`test status` 报在跑时不要再投。取消用 `test cancel`。
- 超时按 `timeout` 收口，已跑完的格子仍在 `summary` 里，不白跑。

**玩家验收用例走玩家通道**：`Tests/Player` 自 2026-09-28 起编辑器可见——编辑器 PlayMode 套件会真跑其 0-GC 计量格
（`GC.Alloc` 采样编辑器同样有牙，日常回归可在编辑器做），但**发布出口的 L3 验收以玩家侧报告为准**。发起通道二选一：
Test Runner 窗口 PlayMode 页签 → `Run all in Player`、CLI `-runTests -testPlatform <BuildTarget>`（详见《测试规范》玩家侧用例的运行方式）。
`test run` 单跑的是编辑器内回归（L1/L2 程序集过滤口径），玩家验收不要混进来。

**玩家侧测试为什么不能手搓构建发起**（2026-09-22/09-28 实测；两条正规通道见《测试规范》玩家侧用例的运行方式——Test Runner 窗口 `Run all in Player`、CLI `-runTests -testPlatform <BuildTarget>`）：
① 玩家里的测试入口不是 `-runTests` 参数，而是**构建期注入的引导场景**——编辑器侧 `CreateBootstrapSceneTask` 建一个挂着
`PlaymodeTestsController`（internal，`Code-based tests runner`）的 `Assets/InitTestScene<guid>.unity` 并把它作为构建场景，
控制器在 `Start()` 里跑测试；自己 `BuildPipeline.BuildPlayer` 的玩家没有这个场景，`-runTests` 什么也不会发生。
② 玩家**不写结果 XML**——结果经 `RemoteTestResultSender` 走 PlayerConnection 回传编辑器，由编辑器落盘（CLI
`-runTests` 模式由 UTF 把结果写入 `-testResults`）；所以「**手搓构建的**独立玩家 + 命令行」这条路不存在，正规 CLI
走的是同一 PlayerLauncher 机制、有引导场景。③ 玩家默认自动启动框架（`GameApp.AutoBoot` 默认 true →
`GameAppSettings.Initiation` 里 `if (GameApp.AutoBoot) GameApp.Boot()`），测试玩家跑的是空场景，启动链会停在
`UGUIHandler.OnInit` 的「UI 根尚未绑定」（实测：带不带 `-runTests` 都停在同一行，测试运行永远轮不到）——
故 `Tests/Player/PlayerTestBootstrap.cs` 在 `AfterAssembliesLoaded` 把 `GameApp.AutoBoot` 置 false，且**仅玩家域生效
（`#if !UNITY_EDITOR`）**：编辑器 PlayMode 测试域依赖自动启动链（L2 门禁前提），编辑器里绝不能掐。

### 验证：用 pipeline 驱动编译（不用人按 Ctrl+R）

```bash
unity command script recompile --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
unity command script recompile_status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
unity command console status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
unity command editor focus --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
```

- **何时要显式重编**：`unity status` 的 `autoRefresh` 不是 `enabled` 时，磁盘上的 `.cs` 改动不会自己编译；缺资产改动也不会触发重载。
- **增删或移动 `.cs` 之后**：先让 AssetDatabase 刷新（`autoRefresh` 为 `enabled` 时它自己会起编译），**不要再叠 `recompile`**——那会重入编译管线、和 Bee 抢同一份在途构建。
- **回执语义**：`script recompile` 成功会当场触发域重载，发起它的请求等不到返回是正常形态；结论一律去 `script recompile_status` 读（`status`、`failed`、`compilationFailed`、`errors`）。
- **编译失败**：`console status` 的 `groundTruth.compilationFailed` 为 true，或 `console get --level error` 读正文；`groundTruth.consoleErrors` 是 Console 当前条数（一轮重编译会把它清归零）。
- **判新鲜度**：编译成功后 `unity status` 的 `codeLoadCount` 必须推进过，否则跑的还是旧代码。
- **聚焦不等于编译**：`editor focus` 只把窗口顶到前台，不要拿它当触发编译的手段。

### pipeline 不可用时

`unity status` 连不上、Safe Mode、或心跳陈旧 ⇒ 停手报 BLOCKED，附 `unity status` 与 `unity pipeline list` 的原文读数。不要改走手搓文件协议，也不要因为连不上就手改 `.unity` / `.prefab` / `.asset`。
