# 验证流程细则（框架包）

本块是本包该主题的唯一权威落点：`CLAUDE.md` 与命令正文只留摘要与指针，同一段话在两边各写一遍即视为缺陷。

以下两段验证环路原先挂在 `## 开发流程` 下，现与本主题同处一块。

## AI 测试流程

面向代理的执行流程；规范全文见 [`Documentation~/zh/Testing.md`](Documentation~/zh/Testing.md)。

### 1. 何时必须写测试

**必须**：新增或修改框架对外契约（服务外观、Handler 契约、公共 API 语义）；修 bug（先构造可复现失败用例）；性能承诺（0-GC/帧预算）；状态机与生命周期；序列化/迁移/编解码。

**不必**：纯注释与文档改动；无行为变化的内部重命名；`Editor` 下一次性的手动工具脚本（除非含可复现的纯函数逻辑）。

### 2. 写作顺序（不许跳步）

1. **读被测**：读实现与相邻用例，弄清契约与既有夹具基座；确认要断的是**行为**而非实现。
2. **选层**：按上表选 L1/L2/L3/L4；能在 EditMode 判定就 L1。
3. **先写失败用例**：新增用例必须**先红后绿**——先跑一次确认它真的能失败（否则它可能什么都没断），再改代码让它变绿。这是"用例有效"的唯一证据。
4. **实现 / 修复**。
5. **验证**：编译 0 error → 目标夹具过滤回归 → 全量回归 0 失败。
6. **同步文档**：公共 API 变更同步 `Documentation~/zh|en`；契约守卫基线变更同步常数与 `CHANGELOG.md`。

### 3. 选通道

| 场景 | 通道 |
|---|---|
| EditMode / PlayMode 全量或过滤 | **测试桥**（`Temp/MoiraiTestRequest.json` 文件协议，见《验证：让开着的编辑器自己跑测试》） |
| 桥不可用（编辑器刚重载、桥未进域） | `TestRunnerApi` 直跑（`exec_editor_script` + `ICallbacks` 宿主） |
| 判编辑器是否空闲 / dll 是否新鲜 | **状态桥**（`Temp/MoriaiEditorState.json`，见《验证：编辑器状态桥》） |
| L3 玩家验收 | 玩家通道三选一：Test Runner 窗口 PlayMode 页签 → `Run all in Player`、本包 `Window/General/Test Player Runner` 窗口、CLI `-runTests -testPlatform <BuildTarget>`（详见《测试规范》玩家侧用例的运行方式）；不要自己 `BuildPipeline.BuildPlayer` 搭测试玩家 |

### 4. 证据纪律

- **唯一 `id` + 只认配对的 `.done`**：否则会把上一轮旧报告当本轮结论。
- **`assemblies` 与 `tests` 不能都为空**：空过滤器会重跑"上一次窗口选择集"，看似成功实则文不对题。
- **投单前先查状态桥**：`testRunActive` 为 0 且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 false 才接单；编辑器是共享的，别的会话随时占住它。
- **以落盘报告为准**：结论取自 `report.txt`（含 ABORTED 的 `collected passed/failed/skipped`），不以"工具调用返回成功"为结论。
- **客户端超时 ≠ 失败**：`exec_editor_script`/编译管线的超时只说明编辑器忙；用状态桥心跳与 `Library/ScriptAssemblies/*.dll` 时间戳判活，**不要重复提交**（会叠加运行）。

### 5. 判绿门禁

**0 失败才算通过。** 新增用例必须"先红后绿"；既有失败必须归因——先 `git diff` 排除并行改动（本项目用户会与代理并行编辑，也会 rebase/amend），确认是本次引入才动手修。

**不允许**：用 `Assert.Ignore` 掩盖 flaky；直接改契约守卫常数让它变绿；把"测不出"当成"没问题"（0-GC 计量走 `GC.Alloc` 采样、编辑器同样有牙；但发布出口只能 L3 玩家验收，编辑器跑绿不替代玩家报告）。

### 6. 忙碌期与域重载处置

- 编辑器编译/域重载期间测试桥与状态桥都会停摆——**请求文件留着，空闲后自动消费**，不要重复投单。
- 域重载后桥脚本可能引用旧程序集（报 CS1061/CS0117 而成员确实存在）——改用反射调用，或等一次真正的重载。
- 收尾前确认编辑器空闲（状态桥 `unix` 心跳新鲜、`isCompiling` 为 false）。

### 验证：让开着的编辑器自己跑测试

`Client/Temp/UnityLockfile` 在时 batchmode 打不开同一工程，而"改完要证据"不该每次都等人去点 Test Runner。
`Tests/EditorMode/TestRequestRunner.cs`（测试程序集内的调试桥）轮询 `Client/Temp/MoiraiTestRequest.json`：
出现请求就按过滤器执行一轮，把逐格进度与结果回写。协议是单向文件，调用方只轮询：

```json
{"id":"<唯一串>","mode":"EditMode","output":"<绝对路径>/report.txt","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],"tests":["<命名空间.类名.方法名>", "..."]}
```

产物：`report.txt`（`run <id> | passed N | failed N | skipped N | 耗时`，后附逐格失败详情）、`report.txt.progress`
（正在跑的用例全名，可判卡死；收口时删除）、`report.txt.done`（内容是请求里的 `id`）。**必须自带唯一 `id` 并只认配对的
`.done`**，否则会把上一轮的旧报告当成这次的结论。前提是该程序集已编译过一次且编辑器有过一次 `update`
（焦点切过去即可，通常在几秒内）；正在编译、正在导入、正在切 PlayMode 或编辑器里有任意 run 在跑（含窗口手动发起）时不接新单——请求文件留着，空闲后自动消费。`mode` 支持 `EditMode`/`PlayMode`；PlayMode 进出场的域重载由驱动落盘 `Temp/MoiraiTestRunState.json` 自动续跑。可选 `timeoutSeconds` 是墙钟上限（秒，`0`/缺省不限时，编译、导入与域重载的等待计入），超时按 ABORTED 收口并尽力取消 Test Runner 作业；ABORTED 报告（超时/孤儿单/执行失败）附带已收集的 `collected passed/failed/skipped` 与墙钟时长，已跑完的格子不白跑；`assemblies` 与 `tests` 均为空的请求会被直接拒绝收口——空过滤器会让 Test Runner 重跑上一次的选择集。

**取消在途单**：往 `Client/Temp/MoiraiTestRequest.cancel.json` 写要取消的请求 `id`（裸文本或 `{"id":"..."}` 均可），
驱动匹配在途单即删除文件并经 `TestRunnerApi.CancelTestRun` 取消作业；UTF 取消后不再送达 RunFinished，
受理即由驱动收口（ABORTED 格式，附已收集计数）；拒绝受理才等 RunFinished 自然收口。
域重载后驱动按作业 guid 精确判活（不认窗口手动跑），判活不可用也有强制收口宽限——调用方永不会等不到 `.done`。

**玩家验收用例走玩家通道**：`Tests/Player` 自 2026-09-28 起编辑器可见——编辑器 PlayMode 套件会真跑其 0-GC 计量格
（`GC.Alloc` 采样编辑器同样有牙，日常回归可在编辑器做），但**发布出口的 L3 验收以玩家侧报告为准**。发起通道三选一：
Test Runner 窗口 PlayMode 页签 → `Run all in Player`、本包 `Window/General/Test Player Runner` 窗口（参数化一键发起、
护栏与测试桥同款）、CLI `-runTests -testPlatform <BuildTarget>`（详见《测试规范》玩家侧用例的运行方式）。
桥单跑的是编辑器内回归（L1/L2 程序集过滤口径），玩家验收不要混进桥单。

**玩家侧测试为什么不能手搓构建发起**（2026-09-22/09-28 实测；三条正规通道见《测试规范》玩家侧用例的运行方式——
Test Runner 窗口 `Run all in Player`、Test Player Runner 窗口、CLI `-runTests -testPlatform <BuildTarget>`）：
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

### 验证：编辑器状态桥（不用人按 Ctrl+R）

`Tests/EditorMode/EditorStateBridge.cs` 把编辑器此刻的状态每 ~1s 覆写到 `Client/Temp/MoiraiEditorState.json`：
`pid`、`domainSeq`、`unix`（心跳 UTC 秒，与 `stat -c %Y` 同量纲）、`isCompiling` / `isUpdating` / `isPlaying` / `isPaused` /
`isChangingPlayMode` / `isFocused` / `isActive`、`activeScenePath`、`dirtyScenes`、`consoleErrors` / `consoleWarnings`、
`testRequestPending` / `testRunActive`（-1 探针不可用、0 空闲、1 有 run 在跑）、`domainDllUnix`（本域加载时
`Moirai.Atropos.Tests.EditorMode.dll` 的 mtime），以及 `assemblies[{name,unix}]`——
`Library/ScriptAssemblies/` 下四份 Moirai 产物（Runtime/Editor/Tests.EditorMode/Tests.PlayMode）的 mtime；
`Tests.Player` 与 `Editor.Testing` 不在跟踪列表——判这两处的新鲜度直接比对对应 dll 的 mtime。`testRunActive` 走 `TestRunnerApi.IsRunActive()` 反射探针而不是
看 `Temp/MoiraiTestRunState.json` 在不在：实测有一单超时收口（报告与 `.done` 都落了、状态文件也删了）之后，
旧域拆走时又把运行态写回了磁盘，残留文件会把空闲报成在跑。

- **判活**：`now - unix` 大到几秒即主线程没在跑 `update`——导入中、域重载中、被原生模态框挡住（`dirtyScenes` 大于 0 时刷新/重编译
  就可能撞上"保存场景？"对话框，本桥不代存），或者 Interaction Mode 不是 `No Throttling`（那时是走得慢而不是不动）。
  真原因去 Editor.log 取（Windows `%LOCALAPPDATA%\Unity\Editor\Editor.log`；macOS `~/Library/Logs/Unity/Editor.log`）。
- **心跳陈旧时整份快照都作废**：里的位是"进阻塞之前"的读数，实测编辑器已经 `Compiling Scripts (busy for 09:51)`
  （进程 CPU 几乎为 0、日志不涨＝编译在等待而非在跑）时，快照里仍是 `isCompiling: false`——把它读成"没在编译"就反了。
  这时唯一可信的是 `unix` 与 `domainSeq` 本身，外加窗口标题/日志这类外部信号。
- **判新域**：`domainSeq` 递增就是域重载真发生过（`SessionState` 计数：跨域保留、随编辑器退出清空，配 `pid` 可区分重载与重启）。
- **判新鲜度**：逐源树比它归属的那份 `assemblies[].unix` 有没有越过自己的改动时刻（`Runtime/**` → `Moirai.Atropos`、
  `Tests/EditorMode/**` → `.Tests.EditorMode`），别一律比测试 dll。还要比 `assemblies[]` 与 `domainDllUnix`：
  **不相等就是"dll 已被后台代编换掉、而这个域还没重载"**，此时跑的还是旧代码——只比 `assemblies[].unix` 和自己的
  改动时刻会把这种状态误判成"已进判据"（实测踩过：dll 14:35:30 出炉、域停在 seq=5，而"无资产改动"的 `refresh`
  不会触发重载，得发 `recompile`）。`consoleErrors` 是 Console 当前条数
  （实测一轮重编译会把它清归零），刷新后 dll 没越过改动时刻且它有增量 = 编译失败；错误正文本桥不代报，仍去 `Editor.log`。
  磁盘上没东西可编时 Bee 不重写 dll，"dll 没变新"单独不构成失败判据。
- **动作**：往 `Client/Temp/MoiraiEditorCommand.json` 投单（同样**必须**先写临时名再 `mv` 原子改名），
  `{"id":"<唯一串>","action":"focus|refresh|recompile"}`。`focus` 把编辑器顶到系统前台（Win32 `AttachThreadInput` +
  `SetForegroundWindow`，只实现了 Windows）；`refresh` 执行 `AssetDatabase.Refresh()`，磁盘上有改动的 `.cs` 时它自己就会起编译——
  这种时候**别再叠 `recompile`**，那会重入编译管线、和 Bee 抢同一份在途构建；`recompile` 才是要跳过磁盘检测强制重编时用。
  正在编译或导入时 `refresh`/`recompile` 直接拒（回执给原因，空闲后重投），播放中不接受 `recompile`。
- **回执**：`Temp/MoiraiEditorCommand.result.json` 配 `.done`（内容是请求 `id`）。回执只答「命令有没有被执行」，
  效果一律回状态文件读——包括 `focus` 之后 `isActive` 到底变了没有。**只认与本次 `id` 配对的 `.done`**：
  命令在编译期间会排在下一拍才消费，直接读回执文件会读到上一单的。
  `refresh`/`recompile` 的回执会写两遍——受理一遍、结论覆写一遍：动作本身可能就当场把本域拆走（编译与域重载就是这次调用发起的），
  先不落地这一单就永远没有配对回执了。读到"已受理"停在原地，是动作已发出、本域被拆走的正常形态，去看状态文件。
- **投测试单前**：`testRunActive` 为 0 且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 false 才是接单窗口
  （与测试桥自己的接单门同源）；这个编辑器是共享的，别的会话随时可能占住它。
- 桥与测试桥同住在测试程序集（`UNITY_INCLUDE_TESTS` 门控），关掉 Test Tools 包就没有心跳。**心跳只在桥进域之后才有**：
  新落的桥文件要等一次真正的导入 + 域重载（本机后台 `AssetImportWorker` 会代跑，本次约二十分钟后自己起来了，时长不可控），
  在那之前这条环路仍然是空的——第一次可能还得有人按一次 `Ctrl+R`。

