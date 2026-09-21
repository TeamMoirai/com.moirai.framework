# MemoryPool 内存池

> 零 GC 页式内存池，使用非托管元数据、EWMA 自适应水位线和阶段驱动预算控制。

MemoryPool 系统为纯 C# 对象（非 GameObject）提供高性能池化。它使用 `unsafe` 指针式页元数据（`Marshal.AllocHGlobal`）在热路径上实现零 GC 压力。池通过静态 `MemoryPool` 外观或泛型 `MemoryPool<T>` 类型访问。

## 何时使用 MemoryPool vs ObjectPool

| 维度 | MemoryPool | ObjectPool |
|------|-----------|------------|
| **目标** | 纯 C# 对象（`MemoryObject`） | 带生命周期的命名对象（`ObjectBase`） |
| **GC 压力** | 零（非托管页元数据） | 托管数组（Dictionary + List） |
| **键方式** | 仅按类型（`MemoryPool<T>`） | 按字符串名称 + 类型 |
| **过期机制** | EWMA 自适应水位线 | 可配置过期时间 + 容量 |
| **适用场景** | 事件、参数、缓冲区、临时数据 | GameObject、UI 元素、业务对象 |

如果你的对象继承 `MemoryObject` 且只需简单的获取/归还语义，用 MemoryPool。如果需要命名池、过期时间、优先级或 GameObject 支持，用 ObjectPool。

## 核心概念

### 页式槽位分配

每个类型 `T` 拥有独立的 `MemoryPool<T>`，使用 32 槽位的页。页按需分配，完全空闲时回收。槽元数据（状态、代次、空闲链表）存储在非托管内存中（`Marshal.AllocHGlobal`），避免 GC 开销。页自身由两条侵入式双向链表串起（空闲页链与空槽页链），挂链/摘链发生在页内计数 0↔1 的边界，因此取用、修剪、回收腾空槽都是 O(1)。

### EWMA 自适应水位线

池通过指数加权移动平均（EWMA）跟踪获取率和突发模式。目标空闲缓存在每次 Tick 时根据以下因素调整：
- `AcquireRateEwma` — 平滑后的每帧获取率
- `BurstEwma` — 平滑后的突发大小（获取 - 归还差值）
- `PendingGrowth` — 显式 `Add()` 尚未落地的待建数量（驱动立即增长；取用未命中不再记债，改为当场构造并按在用量抬升水位）
- `IdleFrames` — 自上次活动以来的空闲帧数（驱动衰减）

### 存活上限与漏还可见性

硬容量只约束**空闲缓存**，不约束总量：`Acquire` 未命中即构造、永不失败，所以业务漏还一只就永久少一只——表现是缓慢上涨的 OOM，而不是当场报错。三条配套：

- `MemoryPoolInfo.UsingCount` 是瞬时在外量，`MaxUsingCount` 是自上次 `ResetAllStats` 以来的峰值。跨小时只增不减即说明有引用没回来；`ResetStats` 把峰值按当前在外量重起，不会把正在漏的池洗成干净。
- `MemoryPool<T>.SetLiveLimit(n)` / `MemoryPool.SetLiveLimit(type, n)`（0 表示不限制，默认不限制）给"漏还"装一个可发现的边界：越界时带池身份限流上报（默认 300 帧一条），开发期先报后抛。发布版**不拒绝发放**——拒绝会让已经开跑的演出当场断，也修不了调用方的漏还。全局默认值在 `MemoryPoolSetting` 的 Inspector 上。
- `MemoryPoolRegistry.ValidateAll()` 与 `MemoryPool<T>.ValidateStructure()` 是只读的结构自检：走查两条页链表，与页内计数、全局计数、前后指针、标志位交叉核对，返回带池身份的失配描述（健康时返回 `null`）。页链表换来 O(1) 摘挂，代价是一次漏挂/漏摘会让后续索引落到已释放内存上——那种失配平时不响。QA / 开发构建里在关键节点（关卡结束、场景卸载、加载完成）各调一次，就能把"随机崩溃"变成"当场说出哪个页的哪条链断了"。它只读但会分配字符串，别放进每帧。

### 线程守卫与故障分级

池不是线程安全的，全部结构都假定主线程独占。守卫分三档：

| 场景 | 行为 |
|------|------|
| 编辑器 / 开发构建 | 每个取还动作校验线程 id，跨线程立即抛 `InvalidOperationException`（消息带 owner/current 线程 id） |
| 正式构建（默认） | 守卫关闭，只保留一次静态布尔读；主线程 id 仍在 `SubsystemRegistration` 就固化 |
| QA / soak 构建 | `MemoryPoolSetting.VerifyMainThreadInRelease` 打开后，正式构建也常驻校验——跨线程改坏非托管元数据不会当场报错，而是几周后以随机崩溃回来，排查期值这点开销 |

维护路径的异常按房内 `RETHROW_*` 同一约定分级：`TickAll` 是每帧边界，开发期合并报一条带失败数量的 Fatal 后上抛，发布期只上报不外溢（没有业务能接住更新派发里的异常）；池内批量路径（整批修剪、页退役）始终逐项隔离走完再上报，一个坏 `OnEvict()` 不截断其余对象。单轮最多列出 16 条回调异常，其余合并成一条汇总——无上限收集等于在"内存紧张正在修剪"的那一刻攒 GC 毛刺。

### 阶段驱动预算

`MemoryPoolRegistry.Phase` 控制每次 Tick 的增长和驱逐预算：

| 阶段 | 增长预算 | 驱逐预算 | 使用时机 |
|------|---------|---------|---------|
| `Boot` | 32 | 4 | 早期启动（闪屏） |
| `Loading` | 32 | 4 | 资源下载、程序集加载、预加载 |
| `Gameplay` | 2 | 2 | 正常游戏 |
| `Background` | 8 | 16 | 应用失去焦点 |
| `LowMemory` | 0 | 32 | 系统低内存警告 |

`LowMemory` 除放大驱逐预算外，还会把目标空闲水位直接归零，因此本轮 Tick 就能把空闲链剪空。

### 回调期限制

对象的构造函数、`Clear()` 与 `OnEvict()` 运行在"池回调"上下文里，其间：

- 不得再调用**同一类型**池的这些入口：`Acquire` / `Release` / `Add` / `Shrink` / `Compact` / `SetCapacity` / `ClearAll` / `TrimNativeMetadata` / `ResetStats`，会抛 `InvalidOperationException`；跨类型取还仍然允许（`MemoryPool<Other>.Acquire()`），只有只读的 `UnusedCount` 不受限；
- 不得调用**全局维护入口**（`MemoryPool.ClearAll` / `CompactAll` / `TrimAllNativeMetadata` / `ClearAllNativeMetadata` / `ResetAllStats` / `SetCapacityAll` / `MemoryPoolRegistry.TickAll`），同样抛 `InvalidOperationException`——这类调用会回收并重新分配正被当前归还流程以 `ref` 引用的非托管页头数组。

需要"归还时顺带清理别的对象"这类联动，请把动作排到回调之后（例如记进自己的待处理列表，或在下一帧的常规流程里消费）。

### Tombstone 页

当 `ClearAll()` 被调用时仍有对象处于租借状态，页会被标记为"tombstone"——空闲对象立即驱逐，但租借对象保留。当最后一个租借对象归还时，页存储被释放。

### Native 元数据自动修剪

在 `AutoTrimNativeMetadataFrames`（默认 18000 帧 ≈ 5 分钟）完全空闲后，池释放其非托管页元数据以最小化内存占用。

页元数据走 `Marshal.AllocHGlobal`，属进程堆，而静态字段只活在当前域里。Unity 编辑器热重载不触发 `AppDomain.DomainUnload`，所以包内另有一条 Editor 侧收口：脚本重载与编辑器退出前调用 `MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown()`。确有对象在外时它**返回 false 且不回收**（那时释放会让下一次归还往已释放内存里写），只打一句告警——这份泄漏留给本次编辑器会话，比制造野指针划算。页存储 `T[]` 与对象本身不跨域存活，因此漏的只有元数据。

## 核心类型

命名空间：`Moirai.Atropos`

| 类型 | 描述 |
|------|------|
| `MemoryPool` | 静态外观：`Acquire<T>()`、`Release<T>()`、`Add<T>()`、`CompactAll()` 等 |
| `MemoryPool<T>` | 泛型类型池：`Acquire()`、`Release()`、`Add()`、`Shrink()`、`Compact()`、`TrimNativeMetadata()` |
| `MemoryPoolRegistry` | 注册表：管理所有池句柄、`TickAll()`、`Phase`、`ClearAll()`、`CompactAll()` |
| `MemoryObject` | 池化对象抽象基类：`Clear()` 方法用于状态重置 |
| `IPoolEvictable` | 可选接口：对象被驱逐（非正常归还）时调用 `OnEvict()` |
| `MemoryPoolHandle` | 缓存句柄，用于动态类型查找：`Acquire()`、`Release()` |
| `MemoryPoolInfo` | 快照结构体：`UnusedCount`、`UsingCount`、`MaxUsingCount`、`LiveLimit`、`AcquireCount`、`MissCount`、`MissRate` 等 |
| `EMemoryPoolPhase` | 枚举：`Boot`、`Loading`、`Gameplay`、`Background`、`LowMemory` |
| `MemoryPoolSetting` | MonoBehaviour：Inspector 可配置的衰减计时器和容量限制 |

## 快速上手

定义池化对象：

```csharp
using Moirai.Atropos;

public class DamageEvent : MemoryObject, IPoolEvictable
{
    public int TargetId;
    public float Amount;

    public override void Clear()
    {
        TargetId = 0;
        Amount = 0f;
    }

    public void OnEvict()
    {
        // 当对象因硬上限溢出被驱逐时调用
    }
}
```

获取和归还：

```csharp
// 泛型 API（最快，编译时类型确定）
var evt = MemoryPool.Acquire<DamageEvent>();
evt.TargetId = entityId;
evt.Amount = 50f;
// ... 使用 evt ...
MemoryPool.Release(evt);

// 动态类型 API（编译时类型未知时使用）
MemoryPoolHandle handle = MemoryPool.GetHandle(typeof(DamageEvent));
MemoryObject obj = handle.Acquire();
handle.Release(obj);
```

预热池：

```csharp
MemoryPool.Add<DamageEvent>(64);
MemoryPoolRegistry.TickAll(Time.frameCount); // 处理增长预算
```

配置容量：

```csharp
MemoryPool.SetCapacity<DamageEvent>(softCapacity: 128, hardCapacity: 512);
```

## 阶段集成

`MemoryPoolSetting` MonoBehaviour 每帧驱动 `MemoryPoolRegistry.TickAll()` 并处理系统事件：

- `Application.lowMemory` → 切换到 `LowMemory` 阶段，调用 `CompactAll()`，恢复原阶段
- `Application.focusChanged` → 失焦时切换到 `Background` 阶段，获焦时恢复

Procedure 流程链在每个阶段设置 Phase：
- `ProcedureLaunch` / `ProcedureSplash` → `Boot`
- `ProcedureInitPackage` 到 `ProcedurePreload` → `Loading`
- `ProcedurePrepare4Entrance` → `Gameplay`

## 统计与调试

零分配获取池信息：

```csharp
MemoryPoolInfo[] buffer = new MemoryPoolInfo[MemoryPool.Count];
int actual = MemoryPool.GetAllMemoryPoolInfos(buffer);
for (int i = 0; i < actual; i++)
{
    Debug.Log($"{buffer[i].Type.Name}: unused={buffer[i].UnusedCount}, miss={buffer[i].MissCount}, missRate={buffer[i].MissRate:P1}");
}
```

订阅每帧统计更新（未订阅时零开销）：

```csharp
MemoryPoolRegistry.PoolStatsUpdated += infos =>
{
    foreach (var info in infos)
    {
        if (info.MissRate > 0.1f)
            Debug.LogWarning($"高未命中率: {info.Type.Name}: {info.MissRate:P1}");
    }
};
```

Debugger 窗口（如已启用）显示所有池的列：Unused、Using（含 max 高水位）、Acquire、Release、Miss、Reserve、Idle、Pages、Util%，配了 `LiveLimit` 时追加 Limit 一栏；顶到上限或高未命中率会标红。

## Inspector 设置

`MemoryPoolSetting` 组件暴露以下配置：

| 字段 | 默认值 | 描述 |
|------|--------|------|
| `m_ShortDecayStartFrames` | 1800 | 空闲多少帧后开始衰减目标空闲水位（@60fps ≈ 30秒） |
| `m_LongDecayStartFrames` | 7200 | 空闲多少帧后加速衰减（@60fps ≈ 2分钟） |
| `m_UnscheduleIdleFrames` | 18000 | 空闲多少帧后停止调度 Tick（@60fps ≈ 5分钟） |
| `m_ZeroFreeReserveStartFrames` | 7200 | 空闲多少帧后允许目标空闲缓存降为 0（@60fps ≈ 2分钟） |
| `m_AutoTrimNativeMetadataFrames` | 18000 | 空闲多少帧后自动释放 Native 元数据（@60fps ≈ 5分钟） |
| `m_SoftFreeReserveLimit` | 128 | 默认空闲缓存软上限 |
| `m_HardFreeReserveLimit` | 512 | 默认空闲缓存硬上限（超限触发驱逐） |
| `m_VerifyMainThreadInRelease` | false | 正式构建也常驻主线程守卫（QA / soak 包打开） |
| `m_DefaultLiveLimit` | 0 | 存活（在外）对象数量上限的全局默认值，0 表示不限制 |

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [ObjectPool](ObjectPool.md) · [Core](Core.md)
