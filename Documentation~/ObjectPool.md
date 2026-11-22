# ObjectPool 服务

> GameObject 实例池：分页槽位 + 代系句柄校验 + 最小堆维护调度，PoolCatalog 数据驱动，支持 glob 通配规则与三种回收策略。

`ObjectPool` 服务按配置条目（`PoolEntry`）把 prefab 按池管理：借出 `Spawn` / 归还 `Despawn`、异步预热 `WarmupAsync`（帧预算化让帧）、`Flush` 整池清退与 prefab 卸载。地址匹配走 `PoolCatalog` 编译目录——字面路径 O(1) exact 命中，通配规则（`PoolGlobMatcher` 段级 glob）按 priority 降序逐段匹配，解析结果 memoize 回 exact 表；未命中配置的地址落 `DefaultPoolRules` 兜底档并告警一次。对外经 `ObjectPoolService.Xxx()` 静态门面访问（ProviderHandlerHost 模式：门面 + `IObjectPoolService` 契约 + `DefaultObjectPoolHandler` 后端（`: ServiceBase, IServiceTickable`）+ `ObjectPoolServiceSettings` 配置）。

## 核心特性

- 分页槽位存储（`PoolSlotStorage`）+ 代系计数：句柄 = `(generation, slotIndex)`，回收再用后旧租约自动失效（防 ABA）
- 最小堆维护调度（`PoolMaintenanceScheduler` 聚合所有池的到期时间）：无待维护池时 Tick 零开销；策略规划是纯函数（`PoolPolicyPlanner` → `PoolRecyclePlan`），Fixed / Burst / Sticky 三档保留策略 + 低内存强制收缩
- 容量三档：`minIdle`（保底）/ `softCapacity`（超限唤醒维护）/ `hardCapacity`（硬顶拒建 + 僵尸自愈清扫）
- 外部销毁自愈：绕过池直接 Destroy 的实例由 `PooledInstanceRegistry` 反向登记探测，槽位自动摘除并告警
- prefab 加载 single-flight：并发请求合并（`UniTaskCompletionSource`）+ `_loadVersion` 代际号防 Shutdown/Flush 与在途加载竞态
- 隐藏托管：根对象自身实现 `IObjectPoolable` 的实例，回收不代管 `SetActive(false)`——可见性由池件 `OnDespawn` 自管；借出激活仍归池
- 池件回调（`IObjectPoolable`：`OnSpawn` / `OnDespawn` / `OnPooledDestroy`）：重入拒绝（回调窗口内回收/租约路径被拒）、异常分级（Spawn 回滚上抛 / Despawn 隔离续走 / PooledDestroy 逐个隔离）
- 分组父节点：按 `PoolEntry.Group` 建组根 Transform，闲置实例归组停放；`FlushGroup` 按组整清
- 诊断：三级快照（总览 / 每池含 spawn/despawn/hit/miss/expand/destroy/peakActive 计数 / 每实例），调试器窗口实时可视

## 核心类型

命名空间：`Moirai.Atropos.ObjectPool`

| 类型 | 说明 |
|------|------|
| `ObjectPoolService` | 静态门面（`[ProviderHandlerHost]`）：`Spawn`（同步/异步/泛型组件）、`Despawn`（实例/租约）、`WarmupAsync`、`Flush` / `FlushAll` / `FlushGroup`、`LoadPrefab`；查询未就绪时静默降级 |
| `IObjectPoolService` | 对象池服务契约（实现类 `: ServiceBase`）；internal `IObjectPoolInternals` 为调试器与测试接缝 |
| `DefaultObjectPoolHandler` | 默认实现（`: ServiceBase, IServiceTickable, IObjectPoolService`）：池登记簿 + 调度器 + 组根管理 |
| `RuntimeObjectPool` | 单池运行时（internal）：槽位存储、活跃/闲置双向链、代系租约、维护排程 |
| `PoolEntry` | 序列化配置条目：entryName / group / pattern / policy / minIdle / softCapacity / hardCapacity / idleSeconds / unloadPrefab / priority |
| `PoolCatalog` | 编译后的规则目录：literal exact 字典 + glob 规则优先级匹配 + 解析 memoize |
| `ObjectPoolSource` | 池键（readonly struct，隐式转换 string） |
| `IObjectPoolable` | 池件契约：`OnSpawn(in ObjectPoolSpawnContext)` / `OnDespawn()` / `OnPooledDestroy()` |
| `PooledGameObject` / `Pooled<T>` | 借出包装（IDisposable；`Pooled<TComponent>` 泛型组件糖，Despawn 后访问 null-safe） |
| `ObjectPoolServiceSettings` | 框架设置（面板「[服务]对象池设置」）：`[ProviderDropdown]` 选择后端 + `m_PoolConfig` 条目清单 |
| `IPrefabLoader` | prefab 加载抽象（内置 `ResourcePrefabLoader` 走 AssetService；换资源后端实现此接口） |

## 快速上手

```csharp
// 借出 / 归还
var instance = ObjectPoolService.Spawn("Assets/AssetRaw/FX/Hit_Fire");
ObjectPoolService.Despawn(instance);

// 泛型组件糖：借出即取组件，缺失即回池
var rigid = ObjectPoolService.Spawn<Rigidbody>("Assets/AssetRaw/FX/Hit_Fire");

// 异步预热（帧预算化：每批 8 个或超 1ms 让帧）
await ObjectPoolService.WarmupAsync("Assets/AssetRaw/FX/Hit_Fire", 20);

// 池件：根或子物体挂 IObjectPoolable 实现
public sealed class HitFx : MonoBehaviour, IObjectPoolable
{
    public void OnSpawn(in ObjectPoolSpawnContext ctx) { /* 重置粒子/回放 */ }
    public void OnDespawn() { }
    public void OnPooledDestroy() { /* 释放租约 */ }
}
```

配置条目在 `ObjectPoolServiceSettings`（菜单 [框架]对象池设置）中编辑：`pattern` 支持字面路径与 glob（如 `Assets/AssetRaw/FX/**`），一条通配规则可覆盖整目录。

## 注意事项

- 回调窗口（OnSpawn / OnDespawn 执行中）内重入 `Spawn`/`Despawn`/租约操作会被拒绝（`EPoolReleaseResult.NotActive` 等），回调里不要碰池 API
- `hardCapacity` 打满即拒建（返回 null）并告警；池不会无界扩容
- 隐藏托管档（根对象自带 `IObjectPoolable`）的实例回收后**不会**被池隐藏——池件的 `OnDespawn` 必须自行处理可见性，否则闲置实例在组根下保持可见
- C# 纯类对象池不在此服务：走 Kernel 的 `MemoryPool<T>`（命名空间 `Moirai.Atropos`），两组池体系正交

---
[« 返回文档索引](Index.md) · [主 README](../README.md) · [Kernel](Kernel.md) · [MemoryPool](MemoryPool.md)
