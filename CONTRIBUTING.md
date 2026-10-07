# 贡献指南

感谢参与 Moirai Framework 的开发。本文收录贡献代码前需要了解的流程与全仓一致的代码约定。

## 开发流程

1. Fork / 拉取仓库，从 `master` 切出功能分支。
2. 开发时保持双语文档同步：`Documentation~/zh/` 与 `Documentation~/en/` 为平行翻译，改动任一侧必须同步另一侧（仓库存有标题骨架一致性检查的惯例，见提交历史 `docs(...)` 前缀的提交）。
3. 变更记入 [CHANGELOG.md](CHANGELOG.md) 的 `[Unreleased]` 段——格式遵循 Keep a Changelog，只记尚未发行的净结果，具体排版规则见该文件头部说明。
4. 提交信息格式为 `类型(范围): 摘要`，如 `feat(save): ...`、`fix(json): ...`、`docs(ui): ...`、`refactor(resource): ...`。
5. 提交前跑测试（见 [Testing 文档](Documentation~/zh/Testing.md)），确认既有用例不红。

## 测试约定

**内部状态走 internal，不走反射。** 测试要读或写被测对象的内部状态时，**不用反射取字段/属性**，而是把该成员的访问级别从 `private` 改成 `internal`。本包 `Runtime/AssemblyInfo.cs` 已给 `Moirai.Atropos.Editor` 与三个测试程序集（`.Tests.EditorMode` / `.Tests.PlayMode` / `.Tests.Player`）声明了 `InternalsVisibleTo`，`internal` 成员对测试天然可见。

```csharp
// ✗ 反射写私有序列化字段：字段改名不会编译报错，测试要到运行期 GetField 返回 null 才炸
typeof(AudioGroupConfig)
    .GetField("m_MaxChannelCeiling", BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(config, 4096);

// ✓ 成员开一档可见性，测试按普通字段读写
[SerializeField, Min(1)] internal int m_MaxChannelCeiling = HARD_CHANNEL_CEILING_DEFAULT;
// ...
config.m_MaxChannelCeiling = 4096;
```

- **事件命名走 `onMainSceneChanged`／`onProcedureChanged` 这类形状**（`on` + PascalCase，与 `Client.sln.DotSettings` 的 Events 规则同口径）。生命周期覆写点是另一档：`protected virtual On*`（`OnInit`/`OnShutdown`），别混用。
- **序列化字段同样适用**：`internal` 不影响 Unity 序列化（`[SerializeField]` 不要求 `private`），命名前缀仍按 `m_` / `s_` / `_` 的私有家族口径走。
- **已有窄接缝的不放开字段**：换入/换出服务处理器一律走 `HandlerHostGenerator` 生成的 `XxxService.Internal_PeekHandler()` / `Internal_UseHandler(next)`（用法见 `Tests/PlayMode/Service/Audio/AudioServiceTestHost.cs`），`s_Handler` 保持 `private`。
- **`UIService` 没有换入接缝**：两支后端各一枚具体类型的处理器槽（`s_UGUIHandler`/`s_UITKHandler`，`private`），只读走 `Internal_PeekUGUIHandler()`/`Internal_PeekUITKHandler()`；后端实现固定，测试不往槽里注入替身，回到干净域状态用 `Internal_ResetHandlerSlots()`。
- **反射仍用于两件事**：遍历 API 形状、断成员标注来做契约守卫（`ResourceSeamShapeGuardTests`、`ResourceMethodSetContractTests`、`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`），以及唤起 Unity 生命周期回调（`Awake` / `OnEnable` / `OnInit`）。这两类都不是读写某个具体私有成员。

完整的分层归属、用例规范与覆盖率门禁见 **[Testing 文档](Documentation~/zh/Testing.md)**。

## 订阅/派发异常分级约定

热路径上「订户 / 回调 / 派发」抛异常时，框架采用**同一套分级策略**（各处 `const` 独立声明、**需同步修改**，因程序集边界不互相引用）：

| 构建 | 行为 |
|------|------|
| `UNITY_EDITOR` / `DEVELOPMENT_BUILD` | `LogUtility.Fatal` 记录后**上抛**（fail-fast，缺陷第一时间暴露） |
| 发布构建 | `Fatal` 记录后**隔离续跑**（单订户/单回调/单事件不拖垮同轮其余项） |

声明位置：

| 常量 | 文件 | 范围 |
|------|------|------|
| `PlayerLoopDriver.RETHROW_SUBSCRIBER_EXCEPTIONS` | `Runtime/Core/Infrastructure/GameApp/PlayerLoop/PlayerLoopDriver.cs` | 帧订阅 Handler / 核心钩子旁路 |
| `ServiceScope.RETHROW_TICK_EXCEPTIONS` | `Runtime/Services/Kernel/World/ServiceScope.cs` | 服务 Tick / 拦截器（否决通道 `OnServiceRegistering` 除外：抛出即拒绝注册，不被隔离） |
| `EventDispatcher.RETHROW_DISPATCH_EXCEPTIONS` | `Runtime/Core/Infrastructure/Events/Models/EventDispatcher.cs` | 事件回调、`ProcessEvent`、协调器排空 |
| `TaskRunner.RETHROW_TASK_EXCEPTIONS` | `Runtime/Core/Infrastructure/Tasks/Components/TaskRunner.cs` | 任务/序列系统回调 |
| `MemoryPoolRegistry.RETHROW_POOL_EXCEPTIONS` | `Runtime/Core/Infrastructure/MemoryPool/MemoryPoolRegistry.cs` | 池回调 |

**与分级无关的硬性卫生（`finally` 无条件执行）：**

- `PlayerLoopDriver` 的 `s_IsDriving`、事件注册表的 `m_IsInvoking` — 异常不得泄漏标记，否则注册/派发会静默失效
- 事件队列 / 协调器队列的 `Acquire`/`Dispose` 配对与池回收入 — 异常中断后残留事件必须逐个 `Dispose` 再清空队列回池
- `ServiceScope` 的 `_isIterating` 与 pending 变更冲刷

**明确例外（无条件隔离，不参与 RETHROW）：**

- `PlayerLoopDriver.InvokeAllQuarantined`：`ApplicationQuit` / `Destroy` / Focus / Pause 等低频生命周期广播 — 截断等于漏掉后续释放/存档
- 事件队列 `ProcessEventQueue` 的 leftover `Dispose`：失败后的资源卫生，不按业务异常分级

改其中任一常量的语义或级别时，**五处常量 + 本节约定 + CHANGELOG** 一并更新。
