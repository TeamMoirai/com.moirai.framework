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

每个类型 `T` 拥有独立的 `MemoryPool<T>`，使用 32 槽位的页。页按需分配，完全空闲时回收。槽元数据（状态、代次、空闲链表）存储在非托管内存中（`Marshal.AllocHGlobal`），避免 GC 开销。

### EWMA 自适应水位线

池通过指数加权移动平均（EWMA）跟踪获取率和突发模式。目标空闲缓存在每次 Tick 时根据以下因素调整：
- `AcquireRateEwma` — 平滑后的每帧获取率
- `BurstEwma` — 平滑后的突发大小（获取 - 归还差值）
- `MissDebt` — 未偿还的未命中计数（驱动立即增长）
- `IdleFrames` — 自上次活动以来的空闲帧数（驱动衰减）

### 阶段驱动预算

`MemoryPoolRegistry.Phase` 控制每次 Tick 的增长和驱逐预算：

| 阶段 | 增长预算 | 驱逐预算 | 使用时机 |
|------|---------|---------|---------|
| `Boot` | 32 | 4 | 早期启动（闪屏） |
| `Loading` | 32 | 4 | 资源下载、程序集加载、预加载 |
| `Gameplay` | 2 | 2 | 正常游戏 |
| `Background` | 8 | 16 | 应用失去焦点 |
| `LowMemory` | 0 | 32 | 系统低内存警告 |

### Tombstone 页

当 `ClearAll()` 被调用时仍有对象处于租借状态，页会被标记为"tombstone"——空闲对象立即驱逐，但租借对象保留。当最后一个租借对象归还时，页存储被释放。

### Native 元数据自动修剪

在 `AutoTrimNativeMetadataFrames`（默认 18000 帧 ≈ 5 分钟）完全空闲后，池释放其非托管页元数据以最小化内存占用。

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
| `MemoryPoolInfo` | 快照结构体：`UnusedCount`、`UsingCount`、`AcquireCount`、`MissCount`、`MissRate` 等 |
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

Debugger 窗口（如已启用）显示所有池的列：Unused、Using、Acquire、Release、Miss、Reserve、Idle、Pages、Util%。

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
