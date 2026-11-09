# pipeline 连接的操作形状

连接体是 pipeline：Unity 侧装 `com.unity.pipeline` 包，命令行侧是 `unity` CLI。
Unity 6 直接装 registry 版；**Unity 2022 通过 MoiraiFrameworkPackage 安装兼容版 pipeline**（框架包管理器 `Tools/Moirai/Framework Package Manager`）。
本块只给可粘贴的形状：连接、投测、轮询与读法。协议语义、档位与「为什么」的叙述不在这里复述。
命令名与参数以 `unity command` 的现读和 `unity <命令> -h` 为准；本文件抄的形状会随后者漂移。

所有命令都接受 `--project-path <路径>` 与 `--format json`。同时开着多个编辑器时必须给 `--project-path`，否则目标跟着 shell 的 cwd 漂。

## 连接与判活

```bash
unity status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
unity status --until-ready --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
```

`unity status` 的读数直接替代旧的状态快照：

| 字段 | 读法 |
|---|---|
| `status` | `ready` 才是连上了；`domainReloadInProgress` / `compiling` 为 true 时在等，不是掉线 |
| `lastHeartbeat` | 主线程心跳；陈旧几秒即主线程没在跑 `update` |
| `codeLoadCount` | 本会话脚本加载次数；两次读之间变化 = 中间发生了域重载 |
| `autoRefresh` | 不是 `enabled` 时磁盘改动不会自己编译，要显式 `script recompile` |
| `playMode` / `frameCount` / `playerLoopTicking` | PlayMode 态与「帧在不在走」；暂停时 `playerLoopTicking` 为 false |
| `dialog` | 非空即被原生模态框挡住（保存场景？一类），先处理对话框 |
| `projectPath` / `unityVersion` | 确认打到的是哪个工程、哪个编辑器 |

- **投测前先判活**：`status` 为 `ready` 且 `compiling`、`domainReloadInProgress` 皆 false 才有接单窗口；编辑器是共享的，别的会话随时可能占住它。
- **连接不上先排 Safe Mode**：工程有编译错误时编辑器以 Safe Mode 启动，pipeline 包不加载，`unity status` / `unity command` / `unity recompile` 一律连不上。用 `unity pipeline list` 确认，再修编译错误并重启 Unity；不要因为连不上就回去手改资产文件。
- **多个编辑器**：`unity status` 的 `projectPath` 就是 `--project-path` 该给的值；匹配不上会报 `AMBIGUOUS_EDITOR` 并列出候选。
- **沙箱里看到 `STATUS_NO_INSTANCES`**：受限沙箱可能挡住真实在跑的编辑器，不能只凭这一条断定编辑器没开。

## 投测：粒度与参数

```bash
unity command test list --mode editor --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
unity command test run --mode editor --filter "<命名空间.类名.方法名>" --filter-type testName --timeout 180 --format json
unity command test run --mode editor --filter "Moirai.Atropos.Tests.EditorMode" --filter-type assembly --format json
unity command test run --mode playmode --filter "<命名空间.类名>" --filter-type testName --async-tests --format json
```

| 参数 | 取值 |
|---|---|
| `mode` | `all` / `editor` / `playmode`（缺省 `all`） |
| `filter` | 名字片段，大小写不敏感的部分匹配；不是正则 |
| `filter-type` | `testName` / `assembly` / `category`（缺省 `testName`） |
| `include-explicit` | 基准这类 `[Explicit]` 用例必须显式带上才跑 |
| `async-tests` | 立即返回，靠 `test status` 轮询；PlayMode 会触发域重载，用这一档 |
| `timeout` | 秒，缺省 600；编译、导入与域重载的等待计入 |

选粒度：

| 目标 | 写法 |
|---|---|
| 一格 | `--filter "<命名空间.类名.方法名>" --filter-type testName` |
| 一类 / 一程序集 | `--filter "<命名空间.类名 或 程序集名>" --filter-type testName\|assembly` |
| 按 Category 圈 | `--filter "<Category 名>" --filter-type category` |
| 整程序集 | `--filter "<程序集名>" --filter-type assembly` |

圈漏了不会报错：`summary.total` 为 0 与 `summary.passed` 为 0 并排，先读计数再谈结论。过滤是部分匹配，写前缀会连同类名一起圈进来——要精确到一格就把全名写全。

## 等待与读法

同步档（默认）直接返回 `TestExecutionResponse`；异步档返回 `statusPath`，用 `test status` 轮询：

```bash
unity command test status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
```

| 字段 | 读法 |
|---|---|
| `summary.total` / `passed` / `failed` / `skipped` / `inconclusive` | 计数并排读；`inconclusive` 是跑了但没给出判决 |
| `results[]` | 逐格结果；失败条目带消息与堆栈 |
| `duration` | 本次墙钟秒数 |
| `mode` / `filterApplied` | 回显本次实际跑的套件与过滤条件，确认没文不对题 |
| `status` | `test status` 的 `no_tests` 表示当前没有在跑的作业 |

- **一次只跑一单**：`test status` 报在跑时不要再投，会叠加。
- **取消在途单**：`unity command test cancel`。
- **超时 ≠ 失败**：`timeout` 到点只说明还没跑完或编辑器忙，读 `summary` 已收集的计数；不要靠重复投单等结果。
- **以回执计数为准**：结论取自 `summary` 与 `results[]`，不以「命令调用返回成功」为结论。

## 编译与新鲜度

```bash
unity command script recompile --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
unity command script recompile_status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
unity command console status --project-path "E:/Workspace/Unity/Game Dev Tools/Client" --format json
```

| 项 | 形状 |
|---|---|
| 增删或移动 `.cs` 之后 | 先让 AssetDatabase 刷新（编辑器 `autoRefresh` 为 `enabled` 时自己就会起编译），不要叠 `recompile` |
| 需要强制重编 | `script recompile`，再轮询 `script recompile_status` 到 `completed` / `up_to_date` |
| 编译结论 | `script recompile_status` 的 `failed`、`compilationFailed`、`errors`；`console status` 的 `groundTruth.compilationFailed` 同源 |
| 判新域 | `unity status` 的 `codeLoadCount` 比上一次读大 = 域重载真发生过 |
| 判新鲜度 | 编译成功后 `codeLoadCount` 必须推进过；没有推进就是还在旧域里跑旧代码 |
| 编译失败正文 | `console get --level error`，或去 `Editor.log`（Windows `%LOCALAPPDATA%\Unity\Editor\Editor.log`） |

- **一次走完（含 asset refresh）**：`unity recompile --project-path <路径>`，退出码 `0` 编过 / `6` 编译报错 / `7` 连不到编辑器（结论未知）；编译结论仍以 `script recompile_status` 为准。
- `script recompile` 成功会触发域重载，发起它的请求等不到返回是正常形态；改用 `script recompile_status` 收口。
- 磁盘上没东西可编时编译是空转，「`codeLoadCount` 没变」单独不构成失败判据；要配合 `console status` 的 `groundTruth.consoleErrors` 增量看。

## 编辑器动作

```bash
unity command editor focus --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
unity command editor.playmode start|stop|pause --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
unity command eval '<C# 语句>' --project-path "E:/Workspace/Unity/Game Dev Tools/Client"
```

- `editor focus` 只把窗口顶到前台，**不等同于编译**；要编译用 `script recompile`。
- `eval` 是任取 C# 的通道，`test run` 覆盖不了的一次性编排走它；能用类型化命令就别用 eval。
- `editor.playmode start` 之后编辑器窗口失焦时玩家循环会停在第一帧，`unity status` 仍报 `playMode`——要看 `frameCount` / `playerLoopTicking` 才知道真的在走。

## pipeline 不可用时

`unity status` 报 `STATUS_NO_INSTANCES`、`STATUS_PIPELINE_LOAD_PENDING`、Safe Mode，或心跳陈旧 ⇒ 停手报 BLOCKED，附 `unity status` 与 `unity pipeline list` 的原文读数；不要改走手搓文件协议或手改资产。
