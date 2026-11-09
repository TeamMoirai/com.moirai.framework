---
name: gate:verify
description: 只读取证：经 pipeline 过编译门，并在有测试装配的插件上跑指定测试装配
when_to_use: 需要"这一轮代码到底编没编过"的硬证据时
argument-hint: [无参数]
arguments: []
allowed-tools: Read, Grep, Glob, Bash(unity:*), Bash(git status:*), Bash(git diff:*), Bash(git log:*)
---

## 判据

- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无。
- 投命令前必须判活：`unity status` 的 `status` 为 `ready`，`unity command editor status` 的 `compiling`、`domainReloadInProgress` 全为 false；不空闲不投。
- 编译门判绿要同时满足：`script recompile_status` 的 `failed:false` 且 `errors:[]`；`Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` 的 mtime 越过改动时刻；`unity status` 的 `codeLoadCount` 有推进。
- 产物是否覆盖本插件直接比 `Moirai.Plugin.<名>.dll` 的 mtime，不拿框架读数冒充。
- 增删过 `.cs` 就让一次 `unity recompile`（含 asset refresh）落地，不要再叠 `script recompile`。

## 流程

1. `git status --short` 记下工作树，标出别人未提交的文件。
2. `unity status --project-path 'P' --format json` 判连接，`unity command editor status --project-path 'P' --result-only --format json` 判忙碌。
3. 需要重编时跑 `unity recompile --project-path 'P' --timeout 420 --format json`。
4. 读 `unity command script recompile_status --project-path 'P' --result-only --format json` 收编译结论。
5. 重新取 `unity status` 的 `codeLoadCount` 与产物 mtime，出证据块。

## 测试门（仅 6 个有测试装配的插件）

可选装配：`Moirai.Plugin.Addressables.Tests`、`Moirai.Plugin.Fmod.Tests`、`Moirai.Plugin.MemoryPack.Tests`、`Moirai.Plugin.MessagePack.Tests`、`Moirai.Plugin.Protobuf.Tests`、`Moirai.Plugin.Wwise.Tests`。

1. 过判活门后投单：

```bash
unity command test run --project-path 'P' --mode editor --filter-type assembly --filter "<上述装配之一>" --detach --format json
unity job wait <job-id> --project-path 'P' --timeout 600 --poll-interval 5000 --format json
```

2. 结论取 `data.result.Summary` 的 `Total / Passed / Failed / Skipped`；`Failed == 0` 且 `Total` 等于预期格数才算通过，`Skipped`/`Inconclusive` 不计通过。
3. 其余插件无测试装配：不投测试请求，报告里写“该插件无测试装配”。

## 证据块

```markdown
- jobId：
- 编译结论：`recompile_status` = completed / failed / …
- 编译错误（若有）：文件名、行、列、错误号、正文
- `Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` mtime：
- codeLoadCount 前→后：
- 测试：<装配名> / Total … / Passed … / Failed … / Skipped …；或“该插件无测试装配”
- 未取证 / 本轮不声称的事：
```

## 边界

- 只读档：白名单没有写盘工具；`unity recompile` / `test run` 的副作用仅限编辑器侧编译、域重载与测试运行。
- 不把"工具调用返回成功"当编译通过。
- 编译报错可能来自兄弟包或宿主工程；先看错误归属文件。
- pipeline 不可达（Safe Mode、`STATUS_NO_INSTANCES`）时等一次真正的导入与域重载，不开第二个工程。
