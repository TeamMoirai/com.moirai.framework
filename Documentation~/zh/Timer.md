# Timer 服务

> 基于四级时间轮的高性能计时器服务，无全量扫描，适合技能 CD、心跳包、延时任务等大规模定时场景。

`Timer` 服务提供添加、暂停、恢复、重启、移除计时器的能力。默认实现 `DefaultTimerHandler` 采用四级时间轮算法（每级 256 槽、1 毫秒精度、每帧最多推进 64 个 tick），配合分页槽位复用与版本化句柄，在十万级计时器规模下仍保持零 GC、O(1) 级操作成本。服务同时维护缩放（受 `Time.timeScale` 影响）与非缩放两条独立时间轮。通过 `TimerService.Xxx()` 静态外观访问（HandlerHost 模式：`TimerService` 静态外观 + `TimerServiceHandler` 抽象基类 + `DefaultTimerHandler` 时间轮后端 + `TimerServiceSettings` 配置）。

注意：本服务与 `Runtime/Core/Schedulers` 下的 Scheduler 调度器（`Scheduler.Delay`、`Scheduler.WaitFrame` 等）是两套独立设施——Scheduler 是零分配的通用调度器，Timer 服务是面向海量定时任务的时间轮实现，按需选用。

## 核心特性

- 四级时间轮：4 级 x 256 桶，1ms tick 精度，到期派发无需全量扫描
- 版本化句柄：句柄为 `(版本号 << 32) | (槽位 + 1)`，槽位复用后旧句柄自动失效（防 ABA）
- 双时间轮：缩放（`Time.timeAsDouble`）与非缩放（`Time.unscaledTimeAsDouble`）独立推进
- 两种回调形态：`Action`（无参）、`Action<T>`（泛型单参，配合缓存委托或静态方法组避免闭包分配）
- 句柄查询：`IsRunning` 查询运行状态、`GetLeftTime` 查询剩余时间，均基于版本化句柄安全访问
- 异常隔离：单个回调抛出的异常仅记录日志（Fatal 级），不影响其他计时器与时间轮推进
- 重入安全：回调内部可安全调用 `RemoveTimer` / `Stop` / `Restart` 操作自身或其他计时器
- 分页存储与预热：初始容量经 `TimerServiceSettings` 配置（默认 1024、最小 256、上限 16384），按 256/页扩展，上限约 100 万槽位

## 核心类型

命名空间：`Moirai.Atropos.Timer`

| 类/接口 | 说明 |
|---------|------|
| `TimerService` | 静态外观（`[HandlerHost]`）：`AddTimer` 两个重载、`Stop` / `Resume` / `Restart` / `RemoveTimer`、`IsRunning` / `GetLeftTime`；调试 API（`GetStatistics` / `GetAllTimers` / `GetStaleOneShotTimers`）位于 partial `TimerService.Debug` |
| `TimerServiceHandler` | 时间轮后端处理器抽象基类（继承 `FrameworkHandler`，契约成员为 `internal`），定义外观调用的后端契约 |
| `DefaultTimerHandler` | 默认实现（四级时间轮算法，位于 `Handler/` 目录），承载时间轮核心逻辑；初始容量由自身序列化字段 `m_InitialCapacity` 配置 |
| `TimerServiceSettings` | 框架设置，`[ProviderDropdown]` 选择计时器后端实现 |
| `TimerDebugInfo` | 调试信息结构体：`TimerHandle`、`LeftTime`、`Duration`、`Age`、`Flags` |
| `TimerDebugFlags` | 调试标志位常量：`RUNNING`、`LOOP`、`UNSCALED` |
| `TimerServiceDebugView` | 计时器调试视图（原生 UI Toolkit，实现 `IDebuggerWindow`）：承载调试内容（统计、采样列表、僵尸检测）；经 `TimerService.OnInit` 自动注册进游戏内调试器 "Profiler/Timer" |

## 快速上手

```csharp
// 1. 延时执行（无参 Action）
ulong id1 = TimerService.AddTimer(() => Debug.Log("3 秒后执行"), 3f);

// 2. 循环计时器（受 timeScale 影响）
ulong id2 = TimerService.AddTimer(OnHeartbeat, 1f, isLoop: true);

// 3. 泛型单参回调，避免闭包分配（T 约束为 class；热路径请使用缓存委托或静态方法组）
ulong id3 = TimerService.AddTimer<Entity>(OnSkillCdEnd, target, 5f);

// 暂停 / 恢复 / 重启 / 移除
TimerService.Stop(id2);       // 暂停并记录剩余时间
TimerService.Resume(id2);     // 从剩余时间继续
TimerService.Restart(id2);    // 重置为完整时长重新计时
TimerService.RemoveTimer(id2);// 彻底移除并回收槽位

// 句柄查询
bool running = TimerService.IsRunning(id2);
float leftTime = TimerService.GetLeftTime(id2);
```

## 进阶用法

### 非缩放时间

```csharp
// isUnscaled: true 时不受 Time.timeScale 影响（暂停菜单、UI 倒计时等场景）
ulong id = TimerService.AddTimer(OnCountdown, 1f, isLoop: true, isUnscaled: true);
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

自定义宿主亦可独立持有视图实例（`new TimerServiceDebugView()`，实现 `IDebuggerWindow` 契约）。

### 实现要点

- 数据按 256 槽分页存放于多个并行数组（`TimerPage`），避免大数组 LOH 压力；
- 每帧 `Update` 中分两条时间轮各推进，单帧每轮最多消耗 64 个 tick 预算，防止长卡顿后雪崩；
- 高层级桶到期后逐级级联（cascade）到低层级，查找仅为槽位索引运算；
- 服务 `Shutdown` 时清理全部计时器与轮结构。

## 注意事项

- 外观方法一律经 `Handler` 属性转发：服务未就绪时按需从 `TimerServiceSettings` 初始化；设置资产不可用或默认工厂缺失时抛出异常（fail-fast），不静默返回默认值。`Shutdown` 后调用外观同样按需重建。
- `AddTimer` 返回 `0UL` 表示失败（回调为 null 或槽位耗尽），有效句柄不会为 0；编辑器下失败会输出 `LogUtility.Warning` 警告日志，运行时不产生日志开销。
- 槽位复用带版本号：对已失效句柄调用 `Stop` / `RemoveTimer` / `IsRunning` 等均为安全的空操作或返回默认值。
- `RemoveTimer` 与一次性的自然到期等价，均会回收槽位；循环计时器必须手动移除，否则持续触发。
- 回调在主线程（服务 `Update`）中同步执行，不要在回调中做耗时阻塞操作。
- 时间缩放只影响 `isUnscaled: false` 的计时器；修改 `Time.timeScale` 前请按需选择回调形态。
- 热路径注册计时器请使用缓存委托或静态方法组，避免捕获 lambda / 闭包引入分配。

---
[« 返回主 README](../../README.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
