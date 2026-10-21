---
name: gate:verify
description: 只读取证：经宿主编辑器桥过编译门，并在有测试装配的插件上跑指定测试装配
when_to_use: 需要"这一轮代码到底编没编过"的硬证据时
argument-hint: [无参数]
arguments: []
allowed-tools: Read, Grep, Glob, Bash(python:*), Bash(git status:*), Bash(git diff:*), Bash(git log:*)
---

## 判据

- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无。
- 投命令前必须读状态桥判空闲：`isCompiling`、`isUpdating`、`isChangingPlayMode` 全为 false；不空闲不投。
- 编译门判绿要同时满足：`consoleErrors == 0`；`Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` 的 mtime 越过改动时刻；`domainSeq` 有推进。
- 新鲜度第三段（`domainDllUnix` 对本次所投程序集）本包量不出来——状态桥只跟踪框架程序集，按"未取证"写。
- 增删过 `.cs` 就先投 `refresh` 再投 `recompile`。

## 流程

1. `git status --short` 记下工作树，标出别人未提交的文件。
2. `Read` 宿主 `Client/Temp/MoiraiEditorState.json` 取忙碌位与读数。
3. 需要重编时写 `Client/Temp/MoiraiEditorCommand.json`：`{"id":"plugin-<唯一串>","action":"recompile"}`。
4. 轮询 `MoiraiEditorCommand.result.json`，只认本次 `id` 的回执。
5. 重新取状态桥与产物 mtime，出证据块。

## 测试门（仅 6 个有测试装配的插件）

可选装配：`Moirai.Plugin.Addressables.Tests`、`Moirai.Plugin.Fmod.Tests`、`Moirai.Plugin.MemoryPack.Tests`、`Moirai.Plugin.MessagePack.Tests`、`Moirai.Plugin.Protobuf.Tests`、`Moirai.Plugin.Wwise.Tests`。

1. 过空闲门后写 `Client/Temp/MoiraiTestRequest.json`（UTF-8 无 BOM）：

```json
{"id":"plugin-<唯一串>","mode":"EditMode","output":"Temp/MoiraiTestResult-<唯一串>.json","assemblies":["<上述装配之一>"],"tests":[],"timeoutSeconds":600}
```

2. 投单后只轮询 `output`、`output.done`、`output.progress`；不要再读请求文件。只认内容等于本次 `id` 的 `.done`。
3. 读 `output` 首行：`passed / failed / skipped`；`failed == 0` 才算通过，`skipped` 不计通过，`ABORTED` 按未收口处理。
4. 其余插件无测试装配：不投测试请求，报告里写“该插件无测试装配”。

## 证据块

```markdown
- id：
- consoleErrors / consoleWarnings：
- `Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` mtime：
- domainSeq 前→后：
- domainDllUnix：未取证（状态桥只跟踪框架程序集）
- 测试：<装配名> / passed N / failed N / skipped N；或“该插件无测试装配”
- 未取证 / 本轮不声称的事：
```

## 边界

- 只读档：白名单没有写盘工具；唯一落盘是宿主 `Temp/` 下的命令与回执。
- 不把"工具调用返回成功"当编译通过。
- 编译报错可能来自兄弟包或宿主工程；先看错误归属文件。
- 桥不可用时等一次真正的导入与域重载，不开第二个工程。
