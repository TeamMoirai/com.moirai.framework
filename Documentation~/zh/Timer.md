# Timer 服务

> 基于四级时间轮的高性能计时器服务，无全量扫描，适合技能 CD、心跳包、延时任务等大规模定时场景。

`Timer` 服务提供延时、按帧等待、暂停、恢复、重启、取消计时器的能力。默认实现 `DefaultTimerHandler` 是**两套独立引擎的复合外观（Composite）**：时间轮引擎 `WheelTimerEngine`（`Delay`，四级时间轮，每级 256 槽、1 毫秒精度、每帧最多推进 64 个 tick）与帧计时引擎 `FrameTimerEngine`（`WaitFrame`，按帧递减），各占一条泳道、自持独立的分页槽位池与句柄命名空间，互不知晓、互不糅合；`DefaultTimerHandler` 只做「创建落到对应引擎、句柄按内嵌泳道号路由、阶段推进与统计跨引擎扇出/聚合」。时间轮引擎同时维护缩放（受 `Time.timeScale` 影响）与非缩放两条轮。对外经 `TimerService.Xxx()` 静态外观访问（HandlerHost 模式：`TimerService` 静态外观 + `TimerServiceHandler` 抽象基类 + `DefaultTimerHandler` 复合后端 + `TimerServiceSettings` 配置）。

公开 API 以 `ulong` 句柄驱动：`Delay` / `WaitFrame` / `Cancel` / `Pause` / `Resume` / `IsDone`。句柄可直接调用 `TimerHandleExtensions` 上的扩展方法（`handle.Cancel()` / `handle.Pause()` / `handle.Resume()` / `handle.IsDone()`），无需额外句柄类型。

## 核心特性

- 四级时间轮：4 级 x 256 桶，1ms tick 精度，到期派发无需全量扫描
- 版本化句柄：句柄为 `(版本号 << 32) | (泳道 << 21) | (槽位 + 1)`，槽位复用后旧句柄自动失效（防 ABA）；泳道号让时间轮与帧计时各用独立句柄命名空间且互不串台；回调内以句柄快照迭代，重入安全
- 双时间轮：缩放（`Time.timeAsDouble`）与非缩放（`Time.unscaledTimeAsDouble`）独立推进
- 两种计时形态：**时间计时器**（`Delay` 系列，按秒走时间轮）与**帧计时器**（`WaitFrame` 系列，按帧逐帧递减，不进入时间轮）
- 触发阶段：`TimerPhase.Update / FixedUpdate / LateUpdate`——时间计时器在 Fixed/Late 阶段到期会延后到对应 Tick 派发，帧计时器在各自 Tick 内推进
- 多种回调形态：`Action`（无参）、`Action<T>`（泛型单参）、`TimerUnsafeBinding`（`delegate*` 函数指针零分配）、以及进度回调 `Action<float>`（时间 0..1）/ `Action<int>`（帧数，1 起）
- 句柄扩展：`handle.Cancel()` / `.Pause()` / `.Resume()` / `.IsDone()`，以及 `await handle.WaitAsync()`（UniTask 轮询等待完成）
- 批量操作：`PauseAll` / `ResumeAll` / `CancelAll`
- 异常隔离：单个回调抛出的异常仅记录日志（Fatal 级），不影响其他计时器与时间轮推进
- 重入安全：回调内部可安全调用 `Cancel` / `Pause` / `Restart` 操作自身或其他计时器
- 分页存储与预热：初始容量经 `TimerServiceSettings` 配置（默认 1024、最小 256、上限 16384），按 256/页扩展，上限约 100 万槽位

## 核心类型

命名空间：`Moirai.Atropos.Timer`

| 类/接口 | 说明 |
|---------|------|
| `TimerService` | 静态外观（`[HandlerHost]`）：`Delay`（四个重载）、`DelayUnsafe`、`WaitFrame`（两个重载）、`WaitFrameUnsafe`、`Pause` / `Resume` / `PauseAll` / `ResumeAll` / `Restart` / `Cancel` / `CancelAll`、`IsRunning` / `IsDone` / `GetLeftTime` / `GetElapsed` / `GetDuration`；调试 API（`GetStatistics` / `GetAllTimers` / `GetStaleOneShotTimers`）位于 partial `TimerService.Debug` |
| `TimerServiceHandler` | 计时器后端处理器抽象基类（继承 `FrameworkHandler`，契约成员为 `internal`），定义外观调用的后端契约 |
| `DefaultTimerHandler` | 默认实现（四级时间轮 + 帧计时 + 阶段触发，位于 `Handler/` 目录）；初始容量由自身序列化字段 `m_InitialCapacity` 配置 |
| `TimerServiceSettings` | 框架设置，`[ProviderDropdown]` 选择计时器后端实现 |
| `TimerPhase` | 触发阶段枚举：`Update`（默认）/ `FixedUpdate` / `LateUpdate` |
| `TimerUnsafeBinding` | 零分配回调绑定结构（函数指针优先，兼容 `Action`）；配 `DelayUnsafe` / `WaitFrameUnsafe` |
| `TimerHandleExtensions` | `ulong` 句柄扩展：`Cancel` / `Pause` / `Resume` / `IsDone` / `WaitAsync`（UniTask） |
| `TimerDebugInfo` | 调试信息结构体：`TimerHandle`、`LeftTime`、`Duration`、`Age`、`Flags` |
| `TimerDebugFlags` | 调试标志位常量：`RUNNING`、`LOOP`、`UNSCALED` |
| `TimerServiceDebuggerWindow` | 计时器调试视图（原生 UI Toolkit，继承 `PollingDebuggerWindowBase`）：承载调试内容（统计、采样列表、僵尸检测）；经 `TimerService.OnInit` 自动注册进游戏内调试器 "Profiler/Timer" |

## 快速上手

```csharp
// 1. 延时执行（无参 Action）——3 秒后触发
ulong id1 = TimerService.Delay(3f, () => Debug.Log("3 秒后执行"));

// 2. 循环计时器（受 timeScale 影响）
ulong id2 = TimerService.Delay(1f, OnHeartbeat, isLooped: true);

// 3. 泛型单参回调，避免闭包分配（T 约束为 class；热路径请使用缓存方法组）
ulong id3 = TimerService.Delay<Entity>(5f, OnSkillCdEnd, target);

// 4. 帧计时器——等待 30 帧后回调
ulong id4 = TimerService.WaitFrame(30, OnAfterFrames);

// 暂停 / 恢复 / 重启 / 取消（经句柄扩展，或直接 TimerService.Pause(id) ...）
id2.Pause();      // 暂停并记录剩余时间
id2.Resume();     // 从剩余时间继续
id2.Cancel();     // 取消并回收槽位（等价 TimerService.Cancel(id2)）
TimerService.Restart(id2);  // 重置为完整时长重新计时

// 句柄查询
bool running = TimerService.IsRunning(id2);
bool done    = id2.IsDone();          // 完成 / 取消 / 失效均为 true
float left   = TimerService.GetLeftTime(id2);   // 剩余秒数（帧计时器返回 0）
float elapsed = TimerService.GetElapsed(id2);   // 时间计时器为秒，帧计时器为帧
```

## 进阶用法

### 非缩放时间

```csharp
// ignoreTimeScale: true 时不受 Time.timeScale 影响（暂停菜单、UI 倒计时等场景）
ulong id = TimerService.Delay(1f, OnCountdown, isLooped: true, ignoreTimeScale: true);
```

### 零分配函数指针绑定（热路径）

```csharp
unsafe {
    // delegate* 绑定，注册与触发全程零分配
    ulong id = TimerService.DelayUnsafe(1f, new TimerUnsafeBinding(target, &OnCdEnd));
    ulong frame = TimerService.WaitFrameUnsafe(10, new TimerUnsafeBinding(&OnFrames));
}
// 也可直接传缓存的 Action（隐式转换）
TimerUnsafeBinding binding = someCachedAction;
```

### 进度回调

```csharp
// 时间计时器：到期前每帧上报 0..1（onComplete 可为 null）
ulong id = TimerService.Delay(2f, OnDone, progress => bar.value = progress);
// 帧计时器：每帧回调累计帧数（1 起）
ulong frame = TimerService.WaitFrame(60, frameCount => text.text = $"{frameCount}/60");
```

### 触发阶段

```csharp
// 在 FixedUpdate / LateUpdate 阶段派发；时间计时器到期落到对应 Tick 延后触发
ulong id = TimerService.Delay(1f, OnLogic, phase: TimerPhase.FixedUpdate);
```

### 在协程 / async 中等待

```csharp
// UniTask：等待计时器完成（内部轮询 IsDone），可传 CancellationToken
await TimerService.Delay(3f, OnDone).WaitAsync(cancellationToken);
```

### 循环计时器的排程规则

循环计时器触发后按「上次触发时间 + 时长」排程以保持相位稳定；若因掉帧导致排程时间落后于当前时间，则对齐为「当前时间 + 时长」，避免连续补发。

### 容量配置与统计

初始容量在 `TimerServiceSettings` 资产中配置（`DefaultTimerHandler.m_InitialCapacity`，默认 1024，最小 256，按 256 对齐），仅在服务初始化时生效，运行中不扩容配置。

```csharp
// 运行时统计：活跃数、池容量、峰值活跃数、空闲数
TimerService.GetStatistics(out int activeCount, out int poolCapacity,
                           out int peakActiveCount, out int freeCount);

// 调试快照：填充调用方提供的数组，返回实际写入数量
var results = new TimerDebugInfo[activeCount];
int count = TimerService.GetAllTimers(results);
for (int i = 0; i < count; i++)
{
    bool isRunning = (results[i].Flags & TimerDebugFlags.RUNNING) != 0;
    Debug.Log($"{results[i].TimerHandle} 剩余 {results[i].LeftTime:F2}s");
}

#if UNITY_EDITOR
// 僵尸计时器检测：存活超过 300 秒的一次性计时器（可能因逻辑错误未释放）
var staleResults = new TimerDebugInfo[32];
int staleCount = TimerService.GetStaleOneShotTimers(staleResults);
#endif
```

### 调试面板（游戏内调试器）

计时器服务的调试信息整合于游戏内调试器的 **Profiler/Timer** 面板——由 `TimerService.OnInit` 自动注册（原生 UI Toolkit 实现，随框架主题渲染），无需在场景中挂载任何组件。双击悬浮 FPS 入口展开调试器后在侧边栏选择即可查看：

- **运行时统计 [RUNTIME STATISTICS]**：活跃数 / 池容量 / 峰值活跃 / 空闲槽位统计与占用率进度条（点击取值可复制）。
- **活跃计时器采样 [ACTIVE TIMER SAMPLE]**：前 32 个计时器的句柄、形态（循环/单次）、缩放模式、运行状态、剩余与周期时长。
- **僵尸一次性计时器 [STALE ONE-SHOT TIMERS]**：存活超过 300 秒的一次性计时器警告列表，帮助定位泄漏（仅编辑器包含）。

面板按 0.5 秒节流重建；服务未就绪时显示提示信息。初始容量的编辑请直接修改 `TimerServiceSettings` 资产（运行中只读，修改在下次服务初始化时生效）。

自定义宿主亦可独立持有视图实例（`new TimerServiceDebuggerWindow()`，继承 `PollingDebuggerWindowBase`）。

### 实现要点

- 数据按 256 槽分页存放于多个并行数组（`TimerPage`），避免大数组 LOH 压力；
- 每帧 `Update` 中分两条时间轮各推进，单帧每轮最多消耗 64 个 tick 预算，防止长卡顿后雪崩；
- 高层级桶到期后逐级级联（cascade）到低层级，查找仅为槽位索引运算；
- 帧计时器独立于时间轮，按阶段存放于并行帧列表，借助每槽位置表实现 O(1) 交换删除；派发以**句柄快照**迭代，规避回调内释放/槽位复用的重入风险；
- 服务 `Shutdown` 时清理全部计时器与轮结构。

## 注意事项

- 外观方法一律直接读取 `s_Handler` 静态字段（源生成器生成），不触发 `Handler` 属性的懒加载：服务未注册 / 未初始化 / `OnShutdown` 已回收处理器时，全部 API 静默降级为安全默认值——调度类（`Delay` / `WaitFrame` / `DelayUnsafe` / `WaitFrameUnsafe`）返回 `0UL`，查询类按语义返回（`IsRunning` / `IsPaused` → `false`，`IsDone` → `true`，`GetLeftTime` / `GetElapsed` / `GetDuration` → `0f`，统计类 → 全零 / 空），控制类（`Pause` / `Resume` / `Restart` / `Cancel` 等）为空操作。降级路径不产生任何日志。这一契约与全框架统一（UI / ObjectPool / Procedure / Debugger 等外观同样走 `s_Handler?.`）。
- `Handler` 属性由 `OnInit` 触发一次装配（`GetHandlerFromSettings() ?? CreateDefaultHandler()`；两厂皆返回 null 时抛 `InvalidOperationException`——这是装配期 fail-fast，不影响外观降级契约）。调用方若需要"未就绪即失败"的写路径语义，请显式访问 `Handler`（触发懒加载）或先判 `TimerService.IsValid`；`OnShutdown` 后需重新注册并初始化服务才会再次装配。
- `Delay` / `WaitFrame` 等返回 `0UL` 表示未登记成功：服务未就绪（见上，静默）、回调为 null 或槽位耗尽（后两者由引擎记录 `LogUtility.Warning`，仅编辑器输出，运行时不产生日志开销）。有效句柄不会为 0。
- 槽位复用带版本号：对已失效句柄调用 `Cancel` / `Pause` / `IsRunning` 等均为安全的空操作或返回默认值。
- `Cancel` 与一次性的自然到期等价，均会回收槽位；循环计时器必须手动取消，否则持续触发。
- 帧计时器的 `GetLeftTime` 恒返回 `0`；其剩余/已用信息请用 `GetElapsed` / `GetDuration`（单位为帧）读取。
- 回调在主线程（对应阶段的 `Tick`）中同步执行，不要在回调中做耗时阻塞操作。
- 时间缩放只影响 `ignoreTimeScale: false` 的计时器；修改 `Time.timeScale` 前请按需选择形态。
- 热路径注册计时器请使用 `DelayUnsafe` / `WaitFrameUnsafe`（函数指针）或缓存方法组，避免捕获 lambda / 闭包引入分配。
- 旧命名 `AddTimer` / `AddTimerUnsafe` / `Stop` / `RemoveTimer` 仍保留为 `[Obsolete]` 别名（分别转发到 `Delay` / `DelayUnsafe` / `Pause` / `Cancel`），仅为兼容存量调用点，新代码请直接使用 `Delay` 系列命名。

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Core](Core.md) · [GameApp](GameApp.md)
