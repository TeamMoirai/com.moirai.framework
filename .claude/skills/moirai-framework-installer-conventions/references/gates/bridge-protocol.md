# 宿主编辑器 pipeline 协议

连接 Unity 只走 pipeline：Unity 侧 `com.unity.pipeline` 包，命令行侧 `unity` CLI。Unity 6 直接装 registry 版；**Unity 2022 通过 MoiraiFrameworkPackage 安装兼容版 pipeline**（框架包管理器 `Tools/Moirai/Framework Package Manager`）。宿主工程根是 `E:\Workspace\Unity\Game Dev Tools\Client`。

本文件只给本包用得到的可粘贴形状：可达性、跑测、计数口径、编译门、Console 读数与不可达处置。命令名与参数以 `unity command` 的现读和 `unity <命令> -h` 为准，本块不抄第二份实现。

## 通道与命令

| 用途 | 命令 |
|---|---|
| 有没有可接的编辑器 | `unity status --format json` / `unity editors running --format json` |
| 编辑器此刻在干什么 | `unity command editor status` |
| 触发编译并取错误正文 | `unity recompile`（含 asset refresh）；单读用 `unity command script recompile_status` |
| Console 读数 | `unity command console status` / `unity command console get --tail N --level Error` |
| 长单不占住调用方 | `unity command test run --detach` + `unity job wait/status/cancel <job-id>` |
| 任意 C# 探针 | `unity command script eval` |

所有命令都吃 `--format json`；`--project-path` 按 `unity status` 回读的 `project` 原样传。

## 前置：可达性

| 读数 | 含义与动作 |
|---|---|
| `instances[].state` 为 `ready` | 可投单 |
| `editors running` 的 `hasPipeline: true` 且 `reachable: true` | `unity command` 通道可用 |
| `STATUS_PIPELINE_LOAD_PENDING` | 编辑器开着但 pipeline 没起来（导入中、域重载中）——等，别改走别的通道 |
| `PIPELINE_UNREACHABLE` | 有 pipeline 包的编辑器连不上服务器，同上 |
| `AMBIGUOUS_EDITOR` | 多于一台，用 `--project-path` 指定 |
| 开机就带着编译错误（Safe Mode） | pipeline 包不加载，整条通道连不上；先修编译错误并重启 Unity |

## 投单前：判活与接单窗口

```bash
unity command test status --project-path 'P' --result-only --format json    # 要 no_tests
unity command editor status --project-path 'P' --result-only --format json  # compiling 与 domainReloadInProgress 皆 false
```

| 读什么 | 形状 |
|---|---|
| 跑测占用 | `test status` 空闲时回 `{"status":"no_tests"}` |
| 编译与域重载 | `editor status` 的 `compiling`、`domainReloadInProgress` |
| 新域有没有推进 | `editor status` 的 `codeLoadCount`（改动前后各取一次比推进） |
| 心跳 | `editor status` 的 `lastHeartbeat`；拉到几秒即主线程没在推进 |
| 播放态与刷新档 | `editor status` 的 `playMode`、`autoRefresh`、`runInBackground` |

这台编辑器是共享的，别的会话随时占住它；一趟只允许一单在飞。

## 测试段

**本包无测试装配，不适用。** 不投 `test run`、不填程序集名、不跑 PlayMode；报告里固定写"测试段：不适用"。本包的验证只走编译门与人工点验。

## 计数口径（有测试装配的仓库才用）

`data.result` 的形状是 `{ Summary: { Total, Passed, Failed, Skipped, Inconclusive }, Results: [...], Mode, FilterApplied }`。

- 判绿：`Summary.Failed` 为 0，且 `Summary.Total` 等于预期格数。
- `Mode` 与 `FilterApplied` 是请求回显，不是执行证据。
- `Results` 不保证只含本单的格子；`Summary.Total` 与预期对不上就作废重投。
- 圈空了不报错：`Total 0` 与 `Passed 0` 并排，先读计数再谈结论。

## 编译门（本包的主判据）

```bash
unity recompile --project-path 'P' --timeout 420 --format json; echo "exit=$?"
unity command script recompile_status --project-path 'P' --result-only --format json
```

| 项 | 形状 |
|---|---|
| 语义 | 触发 asset refresh 加脚本重编译，轮询到完成，逐条报诊断（文件、行、列、错误号、正文） |
| 退出码 | `0` 编过（或本无可编）；`6` 编译报错；`7` 连不到编辑器，结论未知 |
| 判编译结论 | 以 `script recompile_status` 的 `{status, failed, errors[], compilationFailed}` 为准，`exit=6` 单独不构成结论 |
| 产物新鲜度 | 比 `Library/ScriptAssemblies/Moirai.Framework.Installer*.dll` 的 mtime 有没有越过自己的改动时刻 |

## Console 读数

`console status` 给两套数：`counts.{error,warn,log}`（会话累计）与 `groundTruth.{consoleErrors,consoleWarnings,compiling,compilationFailed,ageMs}`（当场采样）。`groundTruth.consoleErrors` 是 Console 当前条数，属全局粘性量，读不了"本次改动有没有引入错误"；归因要靠 `recompile_status.errors` 的正文与行号。

## 并行会话

- 一趟只允许一单在飞；投单前先读 `test status` 为 `no_tests`。
- 并发时先看 `git status`，别人的未提交改动不纳入本轮结论。
- 不删除别人的产物；自己的失败产物可用于取证，交付时说明路径。

## 不可达时

`unity status` 报不出 `ready` 实例，或 `lastHeartbeat` 陈旧到无法判活 ⇒ 停手报 BLOCKED，附 `unity status` 与 `unity editors running` 的原文读数。不要退回任何落盘的 `Temp/` 文件协议（没有消费方会读它），也不与活动编辑器抢工程锁去另起一台 batchmode。
