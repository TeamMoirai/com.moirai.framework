# ObjectPool 对象池服务

> 通用池 + GameObject 特化、共享内核 + 双门面的单模块对象池架构。
> 共享内核提供分页槽位存储、开放寻址哈希与最小堆维护调度；两个门面分别面向任意 CLR 对象与 Unity GameObject。

服务分为两套独立门面，按池化对象类型选择：

| 门面 | 池化对象 | 键 | 典型场景 |
|------|---------|-----|---------|
| `ObjectPoolService` | 任意 `ObjectBase` 派生对象（数据包、连接、指令…） | `Type + 池名` | 纯 C# 对象复用 |
| `GameObjectPoolService` | Unity GameObject（Prefab 实例） | 资源地址（PoolCatalog 规则） | 子弹、特效、UI 弹窗 |

> ⚠️ **两个服务均为 opt-in 注册**：不在 `ProcedureService` 依赖链中，默认不注册。
> 未注册时静态门面所有调用静默返回默认值，维护（过期/超容/低内存收缩）不生效。
> 启用方式：`GameServices.RegisterService(EServiceScopeKind.App, new ObjectPoolService())`
> （GameObjectPoolService 依赖 ResourceService，注册时自动递归拉起）。

## 架构

```
Runtime/Modules/ObjectPool/
├── Kernel/                 # 共享内核（internal）
│   ├── PoolSlotStorage<T>      # 分页槽位存储（128 槽/页 + 页级 free stack）
│   ├── PoolMaintenanceScheduler # 共享最小堆维护调度（1ms 帧预算）
│   ├── OpenHashMap<K> / ReferenceOpenHashMap / StringOpenHashMap  # 开放寻址零分配哈希
│   └── SlotArrayPool<T>        # 按长度分桶的数组池
├── ObjectPoolService.cs    # 通用池静态门面（[HandlerHost]）
├── ObjectBase.cs           # 池化对象基类（OnSpawn/OnDespawn/Release 契约）
├── IObjectPool.cs          # 通用池契约
└── GameObject/             # GameObject 特化
    ├── GameObjectPoolService.cs    # GO 池静态门面（[HandlerHost] + ServiceDependency(Resource)）
    ├── RuntimeGameObjectPool.cs    # 单池运行时（代系句柄 + 策略裁剪）
    ├── PoolCatalog.cs / PoolPolicy.cs / Data/  # 数据驱动配置与策略
    └── IPrefabLoader.cs            # 预制体加载抽象（ResourceAssetLease 租约制）
```

两池共用同一维护调度器语义：每帧 Tick 仅处理到期池（最小堆 O(log n)），单帧维护预算 1ms；
低内存时由各 Handler 订阅 `Application.lowMemory` 全量收缩。

## 核心类型

命名空间：`Moirai.Atropos.ObjectPool`

### 通用池

| 类/接口 | 说明 |
|---------|------|
| `ObjectPoolService` | 静态门面：`GetOrCreatePool<T>` / `GetObjectPool<T>` / `HasObjectPool<T>` / `DestroyObjectPool<T>` / `Release` / `ReleaseAllUnused` |
| `ObjectPoolCreateOptions` | 创建选项：`Name` / `AllowMultiSpawn` / `AutoReleaseInterval` / `Capacity` / `ExpireTime` / `Priority` |
| `IObjectPool<T>` | 单池契约：`Register` / `Spawn` / `Despawn` / `DespawnTarget` / `Release(count)` / `ReleaseAllUnused` |
| `ObjectBase` | 池化对象基类：`OnSpawn` / `OnDespawn` / `Release(bool)` / `Locked` / `CustomCanReleaseFlag` |
| `ObjectPoolBase` | 池元数据基类：`FullName` / `ObjectType` / `Count` / `Capacity` / `ExpireTime` |
| `ObjectInfo` | 对象级调试快照（名称 / 引用计数 / 锁定 / 可释放标记 / 最近使用时间） |

### GameObject 池

| 类/接口 | 说明 |
|---------|------|
| `GameObjectPoolService` | 静态门面：`Spawn` / `SpawnAsync` / `TrySpawn` / `Despawn` / `WarmupAsync` / `LoadPrefab(Async)` / `Flush` / `FlushGroup` / `FlushAll` / `LoadCatalog` |
| `RuntimeGameObjectPool` | 单池运行时：分页槽位 + 侵入式 inactive 链 + 代系句柄 |
| `GameObjectPoolHandle` | 附着在池化实例上的 MonoBehaviour；代系校验防 use-after-despawn |
| `IGameObjectPoolable` | 池化组件接口：`OnSpawn(in GameObjectPoolSpawnContext)` / `OnDespawn` / `OnPooledDestroy` |
| `EPoolPolicy` | 回收策略：`Fixed`（超限即裁剪）/ `Burst`（空闲超时裁剪）/ `Sticky`（不主动回收） |
| `PoolEntry` / `PoolConfigScriptableObject` | 可序列化配置条目与配置资产（支持 Glob：`*`、`**`、`?`） |
| `PoolCompiledCatalog` | 编译后规则目录：精确匹配 + Glob 匹配 |
| `IPrefabLoader` | 预制体加载抽象；默认 `ResourcePrefabLoader` 基于 `ResourceService.LoadLease` 租约制引用计数 |

### 调试

| 类/接口 | 说明 |
|---------|------|
| `GameObjectPoolSummarySnapshot` / `GameObjectPoolSnapshot` | GO 池统计快照（spawn/despawn/hit/miss/expand/destroy/peak + 实例列表） |
| `GetAllObjectPools(bool sort, ObjectPoolBase[])` / `GetAllObjectInfos(ObjectInfo[])` | 通用池调试导出 |
| Debugger 窗口 | `Profiler/Object Pool`（通用池）、`Profiler/GameObject Pool`（GO 池） |

## 快速开始

### 1. 通用池（纯 C# 对象）

```csharp
// 定义池化对象：继承 ObjectBase，实现 Release，重置逻辑放 Clear
public sealed class BuffData : ObjectBase
{
    public Buff Owner { get; private set; }

    public void Init(Buff owner)
    {
        Initialize(owner);          // target 是判等与反查键
    }

    protected internal override void Release(bool isShutdown)
    {
        // 永久移除回调：归还底层资源
    }

    public override void Clear()
    {
        Owner = null;               // 归还 MemoryPool 前重置状态
        base.Clear();
    }
}

// 取池（键 = typeof(BuffData) + 可选池名）
IObjectPool<BuffData> pool = ObjectPoolService.GetOrCreatePool<BuffData>(
    new ObjectPoolCreateOptions(capacity: 256, expireTime: 30f));

// 取用 / 归还
BuffData buff = pool.Spawn();
pool.Despawn(buff);

// 引用计数模式：同一对象可被多方同时取用
var sharedPool = ObjectPoolService.GetOrCreatePool<SharedFx>(
    new ObjectPoolCreateOptions(allowMultiSpawn: true));
SharedFx fx = sharedPool.Spawn();   // SpawnCount++
sharedPool.Despawn(fx);             // SpawnCount--，归零后回到可复用链
```

### 2. GameObject 池

配置 `PoolConfigScriptableObject`（Create > Moirai > PoolConfig）：

```csharp
new PoolEntry
{
    entryName = "子弹",
    group = "战斗",
    assetPath = "Assets/Bundles/Prefabs/Bullet",   // 也支持 Glob：Assets/Bundles/UI/*
    policy = EPoolPolicy.Fixed,
    minIdle = 10,
    softCapacity = 50,
    hardCapacity = 100,
    idleSeconds = 15f,
    unloadPrefab = true,
    priority = 10
};
```

> 配置可走 `GameObjectPoolServiceSettings`（Inspector 指定 PoolConfig 资产，服务初始化时自动加载），
> 或运行时 `GameObjectPoolService.LoadCatalog(config)` / `LoadCatalog(资源地址)` 热切换（重建全部池）。

```csharp
// 同步获取（需预制体已加载）
GameObject bullet = GameObjectPoolService.Spawn("Assets/Bundles/Prefabs/Bullet", parent);

// 异步获取（自动加载预制体，合流去重）
GameObject popup = await GameObjectPoolService.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, cancellationToken);

// 直接获取组件
var renderer = await GameObjectPoolService.SpawnAsync<MeshRenderer>("Assets/Bundles/Props/Rock", parent);

// 回收（归还池中）
GameObjectPoolService.Despawn(bullet);

// 或通过句柄回收
if (bullet.TryGetComponent(out GameObjectPoolHandle handle))
{
    GameObjectPoolService.Despawn(handle);
}
```

### 3. 可池化组件与预热

```csharp
public class BulletController : MonoBehaviour, IGameObjectPoolable
{
    public void OnSpawn(in GameObjectPoolSpawnContext context)
    {
        // 从池中取出时调用 — context.Location/Group/Parent/SpawnFrame
    }

    public void OnDespawn()
    {
        // 归还池中时调用
    }

    public void OnPooledDestroy()
    {
        // 实例被永久销毁时调用（容量裁剪、低内存收缩、关闭池）
    }
}

// 预创建 20 个实例，帧预算分帧不卡顿
await GameObjectPoolService.WarmupAsync("Assets/Bundles/Prefabs/Bullet", 20, cancellationToken);
```

## 高级用法

### GameObject 池策略参考

| 策略 | 回收行为 | 适用场景 |
|------|---------|----------|
| `Fixed` | 超出保留目标 → 立即裁剪 | 子弹、粒子（严格限流） |
| `Burst` | 空闲超过 idleSeconds → 裁剪 | UI 窗口、通用道具 |
| `Sticky` | 不主动裁剪；仅手动 Flush / 低内存收缩 | 高频复用对象 |

### 通用池容量与过期

| 选项 | 行为 |
|------|------|
| `Capacity` | 注册超限时先尝试释放可释放空闲对象，仍满则拒绝并回收该对象 |
| `ExpireTime` | 未使用对象超过空闲时长 → 按唤醒预算（每次 8 个）分帧释放 |
| `AutoReleaseInterval` | 持续超容达到间隔后标记超出部分待释放 |
| `Locked` / `CustomCanReleaseFlag` | 对象级否决自动释放 |

### 刷新操作（GO 池）

```csharp
GameObjectPoolService.Flush("Assets/Bundles/Prefabs/Bullet");  // 刷新单个池
GameObjectPoolService.FlushGroup("战斗");                       // 刷新分组
GameObjectPoolService.FlushAll();                               // 刷新全部（等同低内存响应）
```

### 调试检查

```csharp
// GO 池
GameObjectPoolSummarySnapshot summary = GameObjectPoolService.GetDebugSummary();
GameObjectPoolSnapshot[] snapshots = new GameObjectPoolSnapshot[64];
int count = GameObjectPoolService.GetDebugSnapshots(snapshots);
for (int i = 0; i < count; i++)
{
    MemoryPool.Release(snapshots[i]);   // 快照归还 MemoryPool
}

// 通用池
ObjectPoolBase[] pools = new ObjectPoolBase[64];
int poolCount = ObjectPoolService.GetAllObjectPools(true, pools);   // true = 按优先级排序
```

Debugger 窗口：`Profiler/Object Pool`（通用池）、`Profiler/GameObject Pool`（GO 池，含 hit/miss/peak 指标）。

## 从旧 API 迁移

| 旧（≤ 126df59 前） | 新 | 说明 |
|--------------------|-----|------|
| `ObjectPoolService`（GO 池语义） | `GameObjectPoolService` | 门面更名，GO 池全部 API 保持 |
| `GameApp.ObjectPool` | `GameObjectPoolService` 静态门面 | 不再经 GameApp 访问 |
| `IObjectPoolable` / `PoolSpawnContext` / `ObjectPoolHandle` | `IGameObjectPoolable` / `GameObjectPoolSpawnContext` / `GameObjectPoolHandle` | 类型改名 |
| `IObjectPoolable.OnPooledDestroy` 等 | 同名，接口命名空间不变 | 组件代码只需改接口名 |
| `ObjectPoolSetting` 组件 | `GameObjectPoolServiceSettings`（PoolConfig 字段） | 配置单源化到 Settings 资产 |
| — | `ObjectPoolService` | 新增通用池门面（原为 AlicizaX 参考架构能力） |

## 注意事项

- **opt-in 注册**：两服务默认不在依赖链；未注册时门面调用静默无效，维护不生效（见顶部说明）。
- 通用池对象由外部构造并 `Register` 入池；经 `MemoryPool.Acquire` 创建的对象会被池回收复用，外部 `new` 的对象释放时交由 GC。
- GO 池生成前必须通过 `PoolConfigScriptableObject` 注册池规则；未注册地址记录错误并返回 null。
- `Spawn()`（同步）在预制体未加载时返回 null；首次加载请使用 `SpawnAsync()`。
- `Despawn()` 对非池化 GameObject 安全 — fallback 到 `Destroy`（EditMode 为立即销毁）并告警。
- `GameObjectPoolHandle` 在实例创建时自动添加；外部 `Destroy` 池化实例会触发代系校验清理并告警。
- 维护由 `GameServices.Tick` 驱动（最小堆到期唤醒，单帧 1ms 预算）— 无独立 MonoBehaviour Update 循环。
- 低内存：两池 Handler 各自订阅 `Application.lowMemory` 全量收缩；`GameApp.OnLowMemory` 仅驱动资源层卸载。

---
[« 返回主 README](../../README.md)
