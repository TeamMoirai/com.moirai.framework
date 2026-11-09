# 验证细则（本包）

## 研发闭环与任务记录

本节是研发闭环、按影响面选验证与证据交付纪律的权威落点；入口《工作方式》只留摘要与指针。

### 六步闭环
需求确认 → 读源码与技能 → 实现 → 验证 → 审查 → 证据交付。动手前先读相关实现与 `git status`，
保留已有修改；不恢复用户删除的文件。

### 按影响面选验证
小修改直接实施并交付摘要，不要求建任务记录；跨模块、高风险或多阶段任务先明确目标与验收，
用 `.claude/templates/task.md` 建一份任务记录，写下决策、授权与证据。**一份任务只留一份记录**——
该任务若已有 plan/spec，那份就是记录，不再建第二份。记录落 .claude/tasks/（tracked，随包发行）。

### 证据交付格式
交付四件：变更内容、实际执行的验证（命令与退出码/读数行原文）、剩余风险与阻塞、未执行的检查及原因。
**没有证据的检查不得标成通过**；「工具调用返回成功」不等于结论，结论取回执的 `Summary` 与 `Results[]`（见下文 pipeline 测试通道与判绿门禁）。

### 清退纪律
工具、通道或入口档退出使用时，同轮清掉它们的引用面与场景（TEngine 工作流同款口径：
清除退出使用的旧资料）；不自动积累无关记忆文件。

## 验证面

编译发生在宿主工程的活动编辑器里；插件是否被编译还取决于宿主是否安装了对应外部 SDK。

## 编译门

1. 先判活：`unity status` 的 `status` 为 `ready`，`unity command editor status` 的 `compiling`、`domainReloadInProgress` 全为 false。
2. 触发编译：`unity recompile --project-path 'P' --timeout 420 --format json`（含 asset refresh）；退出码 `0` 编过 / `6` 编译报错 / `7` 连不到编辑器。结论以 `unity command script recompile_status` 的 `{status, failed, errors[], compilationFailed}` 为准，`exit=6` 单独不构成结论。
3. 判绿要同时满足：`script recompile_status` 的 `failed:false` 且 `errors:[]`；`Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` 的 mtime 越过本次改动时刻；`unity status` 的 `codeLoadCount` 有推进。
4. 不要再叠发 `script recompile`：`unity recompile` 已含刷新与编译。
5. 测试必须按插件选择真实存在的装配（如 `Moirai.Plugin.MessagePack.Tests`）；没有测试装配的插件不得编造装配名。

## 测试门

只有 6 个插件有测试装配：`Moirai.Plugin.Addressables.Tests`、`Moirai.Plugin.Fmod.Tests`、`Moirai.Plugin.MemoryPack.Tests`、`Moirai.Plugin.MessagePack.Tests`、`Moirai.Plugin.Protobuf.Tests`、`Moirai.Plugin.Wwise.Tests`。其余插件不投测试。

```bash
unity command test run --project-path 'P' --mode editor --filter-type assembly --filter "Moirai.Plugin.MessagePack.Tests" --detach --format json
unity job wait <job-id> --project-path 'P' --timeout 600 --poll-interval 5000 --format json
```

- 结论取 `data.result.Summary` 与 `Results[]`：`Failed == 0` 且 `Total` 等于预期格数才算通过。
- `Skipped`/`Inconclusive` 不计通过；`Total 0` 不报错但也不是绿——先读计数再谈结论。
- 长单用 `--detach` 回 `jobId`，再 `unity job wait` 收口；前台直跑会被 CLI 的 30 秒命令超时打断。

## 新鲜度三段

| 段 | 本包能不能取到 | 写法 |
|---|---|---|
| 源树 mtime → 归属产物 mtime | 能 | 比源文件与 `Library/ScriptAssemblies/Moirai.Plugin.<名>.dll` |
| `codeLoadCount` 是否推进 | 能 | 改动前后各读一次 `unity status` |
| 产物是否覆盖本插件 | 能 | 直接比 `Moirai.Plugin.<名>.dll` 的 mtime；不拿框架读数冒充 |

## 不许做的验证捷径

- 不用 `-batchmode` 另开同一工程（会与活动编辑器争锁）。
- 不把"工具调用返回成功"当编译/测试通过。
- 不在编译失败时用"上次的绿"当本轮结论。
- 不向 pipeline 请求不存在的测试程序集。
- 不把人工点验写成回归。
