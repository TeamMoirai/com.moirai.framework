# ObjectPool 对象池服务

> 通用池 + GameObject 特化、共享内核 + 双外观的单模块对象池架构。
> 共享内核提供分页槽位存储、开放寻址哈希与最小堆维护调度；两个外观分别面向任意 CLR 对象与 Unity GameObject。

服务分为两套独立外观，按池化对象类型选择：

| 外观 | 池化对象 | 键 | 典型场景 |
|------|---------|-----|---------|
| `ObjectPoolService` | 任意 `ObjectBase` 派生对象（数据包、连接、指令…） | `Type + 池名` | 纯 C# 对象复用 |
| `GameObjectPoolService` | Unity GameObject（Prefab 实例） | 资源地址 / 外部 Prefab 引用 | 子弹、特效、UI 弹窗 |

> ⚠️ **两个服务均为 opt-in 注册**：不在 `ProcedureService` 依赖链中，组合根默认不注册。
> 外观调用一律经 `Handler` 属性转发（fail-fast，未就绪时按需初始化并自动注册——首次外观访问即完成世界注册，`Tick` 驱动的维护随之生效）。
> 也可显式启用：`GameServices.RegisterService(EServiceScopeKind.App, new ObjectPoolService())`
> （显式注册做依赖校验；GameObjectPoolService 依赖 ResourceService，须先行注册）。

## 架构

```
Runtime/Services/ObjectPool/
├── Kernel/                 # 共享内核（internal）
│   ├── PoolSlotStorage<T>      # 分页槽位存储（128 槽/页 + 页级 free stack）
│   ├── PoolMaintenanceScheduler # 共享最小堆维护调度（1ms 帧预算）
│   ├── OpenHashMap<K> / ReferenceOpenHashMap / StringOpenHashMap  # 开放寻址零分配哈希
│   └── SlotArrayPool<T>        # 按长度分桶的数组池
├── ObjectPoolService.cs    # 通用池静态外观（[HandlerHost]）
├── ObjectBase.cs           # 池化对象基类（OnSpawn/OnDespawn/Release 契约）
├── IObjectPool.cs          # 通用池契约
└── GameObject/             # GameObject 特化
    ├── GameObjectPoolService.cs    # GO 池静态外观（[HandlerHost] + ServiceDependency(Resource)）
    ├── RuntimeGameObjectPool.cs    # 单池运行时（代系句柄 + 策略裁剪；Location / External Prefab）
    ├── DefaultPoolRules.cs         # 未注册地址 / 外部 Prefab 的默认规则
    ├── PoolCatalog.cs / PoolPolicy.cs / Data/  # 数据驱动配置与策略
    ├── IPrefabLoader.cs            # 预制体加载抽象（ResourceAssetLease 租约制）
    └── Pooled/                     # IDisposable 薄包装（PooledGameObject / PooledComponent）
```

两池共用同一维护调度器语义：每帧 Tick 仅处理到期池（最小堆 O(log n)），单帧维护预算 1ms；
低内存时由各 Handler 订阅 `Application.lowMemory` 全量收缩。

## 核心类型

命名空间：`Moirai.Atropos.ObjectPool`

### 通用池

| 类/接口 | 说明 |
|---------|------|
| `ObjectPoolService` | 静态外观：`GetOrCreatePool<T>` / `GetObjectPool<T>` / `HasObjectPool<T>` / `DestroyObjectPool<T>` / `TrySpawn<T>` / `Contains<T>` / `Release` / `ReleaseAllUnused` / `FlushAll` |
| `ObjectPoolCreateOptions` | 创建选项：`Name` / `AllowMultiSpawn` / `AutoReleaseInterval` / `Capacity` / `ExpireTime` / `Priority` |
| `IObjectPool<T>` | 单池契约：`Register` / `Spawn` / `TrySpawn` / `Contains` / `Despawn` / `DespawnTarget` / `Release(count)` / `ReleaseAllUnused` / `Flush` |
| `ObjectBase` | 池化对象基类：`OnSpawn` / `OnDespawn` / `Release(bool)` / `Locked` / `CustomCanReleaseFlag` |
| `ObjectPoolBase` | 池元数据基类：`FullName` / `ObjectType` / `Count` / `Capacity` / `ExpireTime` |
| `ObjectInfo` | 对象级调试快照（名称 / 引用计数 / 锁定 / 可释放标记 / 最近使用时间） |

### GameObject 池

| 类/接口 | 说明 |
|---------|------|
| `GameObjectPoolSource` | 统一来源键：资源地址 / 外部 Prefab；`string` / `GameObject` 隐式转换；`Group` 仅 Prefab 源建池生效 |
| `GameObjectPoolService` | 静态外观（唯一入口）：`Spawn` / `SpawnAsync` / `SpawnPooled` / `SpawnPooledAsync` / `Despawn` / `WarmupAsync` / `LoadPrefab(Async)` / `Flush` / `FlushGroup` / `FlushAll` / `LoadCatalog` |
| `PooledGameObject` | 纯 C# 租约（非 MonoBehaviour）：owner/slot/租期代系；`Spawn` / `SpawnAsync` / `Wrap` / `Dispose`；仅 Active 可包装 |
| `Pooled<TComponent>` | 通用组件租约（服务 `SpawnPooled<T>` 的返回类型） |
| `PooledComponent<T,TComponent>` | CRTP 组件租约基类，供 `PooledShot` 等自定义子类使用 |
| `RuntimeGameObjectPool` | 单池运行时：分页 Slot（UserData）+ 侵入式 inactive 链 + 代系；Location / External Prefab |
| `PooledInstanceRegistry` | 实例 → (pool,slot) 零分配反向映射；代系由 Slot 独占 |
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
    pattern = "Assets/Bundles/Prefabs/Bullet",   // 也支持 Glob：Assets/Bundles/UI/*
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
// —— 原始实例（手动 Despawn；string / GameObject 均隐式转为 GameObjectPoolSource）——
GameObject bullet = GameObjectPoolService.Spawn("Assets/Bundles/Prefabs/Bullet", parent);
GameObject fx = GameObjectPoolService.Spawn(vfxPrefab, parent);
GameObject popup = await GameObjectPoolService.SpawnAsync("Assets/Bundles/UI/SettingsPopup", parent, ct);
GameObject posed = GameObjectPoolService.Spawn(vfxPrefab, position, rotation, parent, useLocalPosition: false);
GameObjectPoolService.Despawn(bullet);

// —— 池化租约（using 自动回收，同一套 Spawn 动词）——
using (PooledGameObject lease = GameObjectPoolService.SpawnPooled("Assets/Bundles/Prefabs/Bullet", parent))
{
    // lease.GameObject / lease.Transform
}

using (Pooled<ParticleSystem> ps = GameObjectPoolService.SpawnPooled<ParticleSystem>(vfxPrefab, parent))
{
    ps.Component.Play();
}

// Prefab 源异步与地址源同一签名
PooledGameObject pooled = await PooledGameObject.SpawnAsync(vfxPrefab, parent, ct);
pooled.Dispose(); // 等价 GameObjectPoolService.Despawn(pooled)
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
await GameObjectPoolService.WarmupAsync(vfxPrefab, 8, cancellationToken);
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
GameObjectPoolService.Flush(vfxPrefab);                        // 刷新外部 Prefab 池
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
| `ObjectPoolService`（GO 池语义） | `GameObjectPoolService` | 外观更名，GO 池全部 API 保持 |
| `GameApp.ObjectPool` | `GameObjectPoolService` 静态外观 | 不再经 GameApp 访问 |
| `IObjectPoolable` / `PoolSpawnContext` / `ObjectPoolHandle` | `IGameObjectPoolable` / `GameObjectPoolSpawnContext` / `GameObjectPoolHandle` | 类型改名 |
| `IObjectPoolable.OnPooledDestroy` 等 | 同名，接口命名空间不变 | 组件代码只需改接口名 |
| `ObjectPoolSetting` 组件 | `GameObjectPoolServiceSettings`（PoolConfig 字段） | 配置单源化到 Settings 资产 |
| — | `ObjectPoolService` | 新增通用池外观（原为 AlicizaX 参考架构能力） |
| `GameObjectPoolManager.Get/Release` | `GameObjectPoolService.Spawn/Despawn` | Core 层 Manager 已删除，统一走服务 |
| `GameObjectPoolManager.ReleasePool(key)` | `GameObjectPoolService.Flush(source)` | 按 `GameObjectPoolSource` 刷新 |
| `Moirai.Atropos.Pool.PooledGameObject/PooledComponent` | `Moirai.Atropos.ObjectPool.PooledGameObject/PooledComponent` | 命名空间迁移，后端改为服务 |
| `PooledGameObject.Get` / `PooledComponent.Instantiate` | `Spawn` / `SpawnAsync`（或服务 `SpawnPooled`） | 动词与服务对齐 |
| `PoolKey` / `IPooledMetadata` | `GameObjectPoolSource` / `Slot.UserData` | 键与元数据机制替换 |
| `Spawn(location)` / `Spawn(prefab)` 双重载 | 统一 `Spawn(GameObjectPoolSource)` | `string` / `GameObject` 隐式转换 |

## 注意事项

- **opt-in 注册**：两服务默认不在依赖链；首次外观访问经懒加载路径自动注册（`Tick` 驱动的维护随之生效），也可显式 `RegisterService`（依赖校验更严格，见顶部说明）。
- **Main Thread Only**：整个 ObjectPool 模块（含通用池、GameObject 池、包装租约与 Kernel）仅限主线程调用，无锁设计。
- 通用池对象由外部构造并 `Register` 入池；经 `MemoryPool.Acquire` 创建的对象会被池回收复用，外部 `new` 的对象释放时交由 GC。
- **未注册地址**：自动用默认规则建池（Burst / soft 8 / hard 64，Editor/DevBuild 告警一次）。建议生产地址仍写入 PoolConfig 以便调参。
- **外部 Prefab 池**：按引用身份映射自动建池（零字符串热路径）；池**不**加载/卸载该预制体（`unloadPrefab` 被来源门控忽略）。PoolConfig 支持 `Prefab:` 前缀模式为外部预制体定制容量/分组/策略（如 `pattern = "Prefab:Bullet*"` ——合成键带 instanceID，**必须用通配**，字面量无法预知；未命中回落默认规则 soft 8 / hard 64，catalog 规则优先于 `FromPrefab` 的 `group` 参数）。
- **姿态**：两种来源复用时均重置到 Prefab 局部 TRS（与 `Object.Instantiate(prefab, parent)` 对齐）。`OnSpawn` 回调读到的是已重置姿态。
- **租期代系**：每次激活递增；旧租约在槽位复用后 `TryRelease`/`IsValid` 必然失败。`Wrap` 仅包装 Active 实例。
- **Despawn 三分支**：未注册 → Destroy（外来对象）；已注册非 Active → 安全 no-op（不 Destroy）；Active → 回收入池。重复 Despawn 不会销毁仍在池中的实例。`ReleaseByInstance` 返回 `NotOwned` 时仅告警 no-op。
- `Spawn()`（同步）在资源地址预制体未加载时返回 null；首次加载请使用 `SpawnAsync()`。
- **UserData 单消费者**：`Slot.UserData` 被异种类型占用时组件缓存降级为非驻留（不覆盖原数据，dev 告警）。
- `default(GameObjectPoolSource)` 为无效源；不要写 `Spawn(null)`（两个隐式算子歧义，编译失败）。空源请用 `default`。
- **僵尸槽位自愈**：Spawn 撞硬容量时先清扫外部销毁的槽位再重试分配；仍有实例但无自发到期维护的池（Sticky / 全活跃）按 30s 周期兜底清扫并告警，外部 Destroy 的回收有上界，不再依赖 Flush / 低内存。
- 维护由 `GameServices.Tick` 驱动（最小堆到期唤醒，单帧 1ms 预算）— 无独立 MonoBehaviour Update 循环。Sticky 池不排维护时，外部 Destroy 的槽位在下次 Spawn 惰性清扫。
- 低内存：两池 Handler 各自订阅 `Application.lowMemory` 全量收缩；`GameApp.OnLowMemory` 仅驱动资源层卸载。

---
[« 返回主 README](../../README.md)
