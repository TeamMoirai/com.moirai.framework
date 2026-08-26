# Timer 服务

> 基于四级时间轮的高性能计时器服务，无全量扫描，适合技能 CD、心跳包、延时任务等大规模定时场景。

`Timer` 服务提供添加、暂停、恢复、重启、移除计时器的能力。实现类 `TimerHandler` 采用四级时间轮算法（每级 256 槽、1 毫秒精度、每帧最多推进 64 个 tick），配合分页槽位复用与版本化句柄，在十万级计时器规模下仍保持零 GC、O(1) 级操作成本。服务同时维护缩放（受 `Time.timeScale` 影响）与非缩放两条独立时间轮。通过 `TimerService.Xxx()` 静态门面访问（HandlerHost 模式：`TimerService` 静态门面 + `TimerHandler` 时间轮后端 + `TimerSettings` 配置）。

注意：本服务与 `Runtime/Core/Schedulers` 下的 Scheduler 调度器（`Scheduler.Delay`、`Scheduler.WaitFrame` 等）是两套独立设施——Scheduler 是零分配的通用调度器，Timer 服务是面向海量定时任务的时间轮实现，按需选用。

## 核心特性

- 四级时间轮：4 级 x 256 桶，1ms tick 精度，到期派发无需全量扫描
- 版本化句柄：句柄为 `(版本号 << 32) | (槽位 + 1)`，槽位复用后旧句柄自动失效（防 ABA）
- 双时间轮：缩放（`Time.timeAsDouble`）与非缩放（`Time.unscaledTimeAsDouble`）独立推进
- 三种回调形态：`TimerHandler`（object[] 传参）、`Action`（无参）、`Action<T>`（泛型单参，避免闭包）
- 异常隔离：单个回调抛出的异常仅记录日志，不影响其他计时器与时间轮推进
- 重入安全：回调内部可安全调用 `RemoveTimer` / `Stop` / `Restart` 操作自身或其他计时器
- 分页存储与预热：默认预热 1024 槽位，按 256/页扩展，上限约 100 万槽位

## 核心类型

命名空间：`Moirai.Atropos.Timer`

| 类/接口 | 说明 |
|---------|------|
| `TimerService` | 静态门面（`[HandlerHost]`）：`AddTimer` 三个重载、`Stop` / `Resume` / `Restart` / `RemoveTimer`、`Prewarm`、`GetStatistics`、`GetAllTimers` 全部静态 API |
| `TimerHandler` | 时间轮后端处理器（继承 `FrameworkHandler`），承载四级时间轮核心逻辑 |
| `TimerSettings` | 框架设置，`[ProviderDropdown]` 选择计时器后端实现 |
| `TimerCallback` | 委托 `void TimerCallback(object[] args)`，传统 object[] 传参回调 |
| `TimerDebugInfo` | 调试信息结构体：`timerHandle`、`leftTime`、`duration`、`age`、`flags` |
| `TimerDebugFlags` | 调试标志位常量：`RUNNING`、`LOOP`、`UNSCALED` |

## 快速上手

```csharp
// 1. 延时执行（无参 Action）
ulong id1 = TimerService.AddTimer(() => Debug.Log("3 秒后执行"), 3f);

// 2. 循环计时器（受 timeScale 影响）
ulong id2 = TimerService.AddTimer(OnHeartbeat, 1f, isLoop: true);

// 3. 泛型单参回调，避免闭包分配（T 约束为 class）
ulong id3 = TimerService.AddTimer<Entity>(OnSkillCdEnd, target, 5f);

// 4. 传统 object[] 传参（兼容旧代码）
ulong id4 = TimerService.AddTimer(OnArgsCallback, 2f, false, false, 100, "hello");
void OnArgsCallback(object[] args) { /* args[0]=100, args[1]="hello" */ }

// 暂停 / 恢复 / 重启 / 移除
TimerService.Stop(id2);       // 暂停并记录剩余时间
TimerService.Resume(id2);     // 从剩余时间继续
TimerService.Restart(id2);    // 重置为完整时长重新计时
TimerService.RemoveTimer(id2);// 彻底移除并回收槽位
```

## 进阶用法

### 非缩放时间

```csharp
// isUnscaled: true 时不受 Time.timeScale 影响（暂停菜单、UI 倒计时等场景）
ulong id = TimerService.AddTimer(OnCountdown, 1f, isLoop: true, isUnscaled: true);
```

### 循环计时器的排程规则

循环计时器触发后按「上次触发时间 + 时长」排程以保持相位稳定；若因掉帧导致排程时间落后于当前时间，则对齐为「当前时间 + 时长」，避免连续补发。

### 预热与统计

```csharp
// 战斗前预热槽位，避免运行中扩页（上限 4096 页 x 256 槽）
TimerService.Prewarm(4096);

// 运行时统计：活跃数、池容量、峰值活跃数、空闲数
TimerService.GetStatistics(out int activeCount, out int poolCapacity,
                           out int peakActiveCount, out int freeCount);

// 调试快照：填充调用方提供的数组，返回实际写入数量
var results = new TimerDebugInfo[activeCount];
int count = TimerService.GetAllTimers(results);
for (int i = 0; i < count; i++)
{
    bool isRunning = (results[i].flags & TimerDebugFlags.RUNNING) != 0;
    Debug.Log($"{results[i].timerHandle} 剩余 {results[i].leftTime:F2}s");
}
```

### 实现要点

- 数据按 256 槽分页存放于多个并行数组（`TimerPage`），避免大数组 LOH 压力；
- 每帧 `Update` 中分两条时间轮各推进，单帧每轮最多消耗 64 个 tick 预算，防止长卡顿后雪崩；
- 高层级桶到期后逐级级联（cascade）到低层级，查找仅为槽位索引运算；
- 服务 `Shutdown` 时清理全部计时器与轮结构。

## 注意事项

- `AddTimer` 返回 `0UL` 表示失败（回调为 null 或槽位耗尽），有效句柄不会为 0。
- 槽位复用带版本号：对已失效句柄调用 `Stop` / `RemoveTimer` 等均为安全的空操作。
- `RemoveTimer` 与一次性的自然到期等价，均会回收槽位；循环计时器必须手动移除，否则持续触发。
- 回调在主线程（服务 `Update`）中同步执行，不要在回调中做耗时阻塞操作。
- 时间缩放只影响 `isUnscaled: false` 的计时器；修改 `Time.timeScale` 前请按需选择回调形态。

---
[« 返回主 README](../../README.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
