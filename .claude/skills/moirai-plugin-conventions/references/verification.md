# 验证细则（本包）

## 验证面

编译发生在宿主工程的活动编辑器里；插件是否被编译还取决于宿主是否安装了对应外部 SDK。

## 编译门

1. 读宿主 `Client/Temp/MoiraiEditorState.json`，确认 `isCompiling`、`isUpdating`、`isChangingPlayMode` 全为 false。
2. 需要重编时写 `Client/Temp/MoiraiEditorCommand.json`：`{"id":"<唯一串>","action":"recompile"}`，回执读 `MoiraiEditorCommand.result.json`；`action` 白名单只有 `focus` / `refresh` / `recompile`。
3. 判绿要同时满足：`consoleErrors == 0`；`Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` 的 mtime 越过本次改动时刻；`domainSeq` 有推进。
4. 增删 `.cs` 时先 `refresh` 再 `recompile`。
5. 测试请求必须按插件选择真实存在的装配（如 `Moirai.Plugin.MessagePack.Tests`）；没有测试装配的插件不得编造装配名。

## 测试门

只有 6 个插件有测试装配：`Moirai.Plugin.Addressables.Tests`、`Moirai.Plugin.Fmod.Tests`、`Moirai.Plugin.MemoryPack.Tests`、`Moirai.Plugin.MessagePack.Tests`、`Moirai.Plugin.Protobuf.Tests`、`Moirai.Plugin.Wwise.Tests`。其余插件不投测试请求。

请求示例：

```json
{"id":"plugin-<唯一串>","mode":"EditMode","output":"Temp/MoiraiTestResult-<唯一串>.json","assemblies":["Moirai.Plugin.MessagePack.Tests"],"tests":[],"timeoutSeconds":600}
```

- 投单后只轮询 `output`、`output.done`、`output.progress`；不要再读 `MoiraiTestRequest.json`。
- 只认内容等于本次 `id` 的 `.done`；结论取 `output` 首行，`failed == 0` 才算通过，`skipped` 不计通过，`ABORTED` 按未收口处理。
- 并行会话用带 `plugin` 前缀的唯一 `id`，不复用别人的 `tag`/输出路径。

## 新鲜度三段

| 段 | 本包能不能取到 | 写法 |
|---|---|---|
| 源树 mtime → 归属产物 mtime | 能 | 比源文件与 `Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` |
| `domainSeq` 是否推进 | 能 | 投命令前后各读一次状态桥 |
| `domainDllUnix` 是否等于本次所投程序集产物 mtime | **不能** | 状态桥只跟踪框架四份程序集；按"未取证"写 |

## 不许做的验证捷径

- 不用 `-batchmode` 另开同一工程（会与活动编辑器争锁）。
- 不把"工具调用返回成功"当编译/测试通过。
- 不在编译失败时用"上次的绿"当本轮结论。
- 不向桥请求不存在的测试程序集。
- 不把人工点验写成回归。
