# GameObjectPool 对象池服务

> 零 GC 游戏对象池，采用 SoA 分页 Slot 存储、策略驱动回收、最小堆维护调度和数据驱动目录配置。

通过 `GameApp.GameObjectPool` 访问游戏对象池服务。按资源地址管理池化 GameObject 实例，使用编译后的 `PoolCatalog`（ScriptableObject 驱动）解析池规则，支持三种回收策略（Fixed/Burst/Sticky），并提供异步预制体加载与引用计数管理。

## 核心特性

- **零 GC 热路径**：SoA 分页 Slot 存储（128 slots/page）+ 侵入式双向链表 — Spawn/Despawn 过程无 Dictionary 分配
- **策略驱动回收**：`PoolPolicy.Fixed`（严格容量）、`PoolPolicy.Burst`（空闲超时）、`PoolPolicy.Sticky`（不主动回收）
- **最小堆维护调度**：仅处理到期池的维护 — 每次 tick O(log n)，非 O(n) 全量扫描
- **代系校验句柄**：`GameObjectPoolHandle`（MonoBehaviour）绑定 slot index + generation，防止 use-after-despawn
- **异步预制体加载**：`IPrefabLoader` 抽象 + `ResourcePrefabLoader`（基于 YooAsset），引用计数管理预制体生命周期
- **帧预算预热**：`WarmupAsync` 按帧预算分批创建实例，避免帧尖峰
- **低内存响应**：`Application.lowMemory` 回调触发全局 `FlushAll()`
- **数据驱动配置**：`PoolConfigScriptableObject` 支持 Glob 模式匹配（`*`、`**`、`?`）
- **完整可观测性**：每池快照含 hit/miss/expand/destroy/peak 指标 + 实例级检查

## 核心类型

命名空间：`Moirai.Atropos.GameObjectPool`

| 类/接口 | 说明 |
|---------|------|
| `IGameObjectPoolService` | 服务接口：`Spawn` / `SpawnAsync` / `Despawn` / `Flush` / `FlushGroup` / `FlushAll` / `WarmupAsync` / `LoadCatalog` |
| `GameObjectPoolService` | 默认实现（`internal sealed`），`Priority = 6`，实现 `IServiceTickable` 驱动最小堆维护 |
| `RuntimeGameObjectPool` | 单池运行时：SoA 分页 Slot + 侵入式链表 + generation 句柄 |
| `GameObjectPoolHandle` | 附着在池化实例上的 MonoBehaviour；提供 `TryRelease()` 安全回收 |
| `IGameObjectPoolable` | 池化预制体组件接口：`OnSpawn(in PoolSpawnContext)` / `OnDespawn()` / `OnPooledDestroy()` |
| `PoolPolicy` | 枚举：`Fixed = 0`、`Burst = 1`、`Sticky = 2` |
| `PoolEntry` | 可序列化配置条目：`assetPath`、`policy`、`minIdle`、`softCapacity`、`hardCapacity`、`idleSeconds`、`unloadPrefab` |
| `PoolConfigScriptableObject` | 持有 `List<PoolEntry>` 的 ScriptableObject；通过 `LoadCatalog()` 加载 |
| `PoolCompiledCatalog` | 编译后的规则目录，支持精确匹配 + Glob 模式匹配 |
| `PoolGlobMatcher` | 零分配 Glob 模式匹配器（`*`、`**`、`?`） |
| `IPrefabLoader` | 预制体加载抽象：`LoadPrefab` / `LoadPrefabAsync` / `UnloadPrefab` |
| `ResourcePrefabLoader` | 默认 `IPrefabLoader` 实现，使用 `GameApp.Resource`（YooAsset）+ 引用计数 |
| `GameObjectPoolSetting` | MonoBehaviour 组件，提供 Inspector 配置 + 低内存/焦点事件注册 |
| `GameObjectPoolSnapshot` | 调试快照：每池统计（spawn/despawn/hit/miss/expand/destroy/peak）+ 实例列表 |
| `SlotArrayPool<T>` | 内部数组池（按长度分桶），零 GC 数组复用 |
| `StringOpenHashMap` | 内部开放寻址字符串→int HashMap |

## 快速开始

### 1. 配置池规则

通过 `Create > Moirai > PoolConfig` 创建 `PoolConfigScriptableObject`：

```csharp
// 或在 GameObjectPoolSetting 组件的 Inspector 中指定
var config = ScriptableObject.CreateInstance<PoolConfigScriptableObject>();
config.entries = new List<PoolEntry>
{
    new PoolEntry
    {
        entryName = "子弹",
        group = "战斗",
        assetPath = "Assets/Bundles/Prefabs/Bullet",
        policy = PoolPolicy.Fixed,
        minIdle = 10,
        softCapacity = 50,
        hardCapacity = 100,
        idleSeconds = 15f,
        unloadPrefab = true,
        priority = 10
    },
    new PoolEntry
    {
        entryName = "UI弹窗",
        group = "UI",
        assetPath = "Assets/Bundles/UI/*",  // Glob 模式
        policy = PoolPolicy.Burst,
        minIdle = 2,
        softCapacity = 8,
        hardCapacity = 32,
        idleSeconds = 30f
    }
};
```

### 2. 获取和回收

```csharp
// 同步获取（需预制体已加载）
GameObject bullet = GameApp.GameObjectPool.Spawn("Assets/Bundles/Prefabs/Bullet", parent);

// 异步获取（自动加载预制体）
GameObject popup = await GameApp.GameObjectPool.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, cancellationToken);

// 直接获取组件
var renderer = await GameApp.GameObjectPool.SpawnAsync<MeshRenderer>("Assets/Bundles/Props/Rock", parent);

// 回收（归还池中）
GameApp.GameObjectPool.Despawn(bullet);

// 或通过句柄回收（附着在 GameObject 上）
if (bullet.TryGetComponent(out GameObjectPoolHandle handle))
{
    GameApp.GameObjectPool.Despawn(handle);
}
```

### 3. 预热

```csharp
// 预创建 20 个实例，帧预算控制不卡帧
await GameApp.GameObjectPool.WarmupAsync("Assets/Bundles/Prefabs/Bullet", 20, cancellationToken);
```

### 4. 可池化组件

在池化预制体的组件上实现 `IGameObjectPoolable`：

```csharp
public class BulletController : MonoBehaviour, IGameObjectPoolable
{
    public void OnSpawn(in PoolSpawnContext context)
    {
        // 从池中取出时调用 — 重置状态、启动移动等
        transform.SetPositionAndRotation(context.Parent.position, Quaternion.identity);
    }

    public void OnDespawn()
    {
        // 归还池中时调用 — 停止移动、清理引用等
    }

    public void OnPooledDestroy()
    {
        // 实例被永久销毁时调用（容量裁剪、关闭池）
    }
}
```

## 高级用法

### 刷新操作

```csharp
// 刷新单个池
GameApp.GameObjectPool.Flush("Assets/Bundles/Prefabs/Bullet");

// 刷新指定分组的所有池
GameApp.GameObjectPool.FlushGroup("战斗");

// 刷新所有池（等同于低内存响应）
GameApp.GameObjectPool.FlushAll();
```

### 策略参考

| 策略 | 回收行为 | 适用场景 |
|------|---------|----------|
| `Fixed` | 超出 softCapacity → 立即裁剪 | 子弹、粒子（严格限流） |
| `Burst` | 空闲超过 idleSeconds → 裁剪 | UI 窗口、通用道具 |
| `Sticky` | 不主动裁剪；仅手动 Flush | 高频复用对象 |

### 调试检查

```csharp
if (GameApp.GameObjectPool is GameObjectPoolService service)
{
    var summary = service.GetDebugSummary();
    // summary.PoolCount, summary.ActiveInstanceCount, summary.InactiveInstanceCount 等

    GameObjectPoolSnapshot[] snapshots = new GameObjectPoolSnapshot[64];
    int count = service.GetDebugSnapshots(snapshots);
    for (int i = 0; i < count; i++)
    {
        // snapshots[i].hitCount, snapshots[i].missCount, snapshots[i].peakActive 等
        MemoryPool.Release(snapshots[i]); // 归还快照到 MemoryPool
    }
}
```

Debugger 窗口（`Profiler > GameObject Pool`）提供所有池统计的实时视图。

## 注意事项

- 生成前必须通过 `PoolConfigScriptableObject` 注册池规则。未注册的地址会记录错误并返回 null。
- `Spawn()`（同步）在预制体未加载时返回 null。首次加载请使用 `SpawnAsync()`。
- `Despawn()` 对非池化 GameObject 安全 — 会 fallback 到 `Object.Destroy()` 并发出警告。
- `GameObjectPoolHandle` 在实例创建时自动添加。外部 `Destroy()` 池化对象会触发 `NotifyHandleDestroyed` 进行清理。
- 服务由 `IServiceTickable.Tick()` 通过 `GameServices.Tick` 驱动 — 无独立 MonoBehaviour Update 循环。
- `GameObjectPoolSetting` 组件在 GameEntry 上提供 Inspector 配置默认值 + 低内存/焦点事件注册（与 `MemoryPoolSetting` 相同模式）。

---
[« 返回主 README](../../README.md)
