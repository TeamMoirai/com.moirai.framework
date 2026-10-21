# 宿主编辑器桥协议

协议实现住在框架包的测试装配里：`../com.moirai.framework/Tests/EditorMode/EditorStateBridge.cs` 与 `../com.moirai.framework/Tests/EditorMode/TestRequestRunner.cs`。本文件只抄调用方必须逐字使用的常量与门。

## 文件

| 文件（相对宿主工程根） | 方向 | 内容 |
|---|---|---|
| `Temp/MoiraiEditorState.json` | 读 | 心跳与状态载荷 |
| `Temp/MoiraiEditorCommand.json` | 写 | `{"id":"<唯一串>","action":"focus\|refresh\|recompile"}` |
| `Temp/MoiraiEditorCommand.result.json` | 读 | 命令回执（`ok`、`message`） |
| `Temp/MoiraiTestRequest.json` | 写 | 测试请求（仅当本包有测试装配） |
| `Temp/<output>` / `.done` | 读 | 报告与配对完成标记 |

文件名前缀是 **`Moirai`**，不是 `Moriai`。宿主工程根是 `E:\Workspace\Unity\Game Dev Tools\Client`。

## 状态载荷

`domainSeq`（域重载序号）、`unix`/`utc`（心跳）、`isCompiling`/`isUpdating`/`isChangingPlayMode`（忙碌位）、`testRunActive`（-1 不可用/0 空闲/1 在跑）、`consoleErrors`、`dirtyScenes`、`domainDllUnix`、`assemblies[]`（只跟踪框架四份程序集）。

## 投单门

`testRunActive == 0`、三个忙碌位全 false、心跳新鲜、`dirtyScenes == 0`。

## 测试请求

只有 6 个插件有测试装配：`Moirai.Plugin.Addressables.Tests`、`Moirai.Plugin.Fmod.Tests`、`Moirai.Plugin.MemoryPack.Tests`、`Moirai.Plugin.MessagePack.Tests`、`Moirai.Plugin.Protobuf.Tests`、`Moirai.Plugin.Wwise.Tests`。其余插件不投测试请求。

```json
{"id":"plugin-<唯一串>","mode":"EditMode","output":"Temp/MoiraiTestResult-<唯一串>.json","assemblies":["<上述装配之一>"],"tests":[],"timeoutSeconds":600}
```

`assemblies` 与 `tests` 不能同时为空；只认内容等于本次 `id` 的 `.done`；结论取报告首行的 passed / failed / skipped，0 失败才算通过。

## 命令

- `refresh`：增删 `.cs` 后必须先投。
- `recompile`：不能在未 `refresh` 的新文件清单上直接投。
- `focus`：把编辑器带到前台。
- 回执 `ok:false` 读 `message`；常见是"正在编译脚本，未执行"。

## 并行会话

桥是单槽共享文件：用带自己前缀的 `id`/`output`；投单后只轮询自己的产物；只认内容等于本次 `id` 的 `.done`。
