# ObjectPool 模块

> 模块化对象池：以 `ObjectBase` 为载体，提供容量、过期、优先级、锁定与自动释放策略的对象池体系。

ObjectPool 模块通过 `GameModule.ObjectPool` 访问，管理任意数量的命名对象池。每个池持有一组 `ObjectBase` 派生对象，支持"单次获取"（同一对象同时只能被取出一处）与"多次获取"两种模式，并可配置容量上限、对象过期秒数、自动释放间隔与优先级。`Support` 子目录在此之上提供了面向 `GameObject` 的开箱即用池化组件。

注意与 `Runtime/Core/Pool`（命名空间 `Moirai.Atropos.Pool`）的通用对象池区分：后者是框架内部使用的轻量工具——`_ObjectPool<T>`（internal 栈式泛型池）与 `GameObjectPoolManager`（按 `PoolKey` 的 GameObject 池，`Get`/`Release`/`ReleasePool`/`ReleaseAll`），无模块生命周期与过期策略；本模块则是完整的框架模块（实现 `IUpdateModule`，由 [Core](Core.md) 的 `ModuleSystem` 驱动轮询），适合业务层管理需要引用计数与释放策略的对象。

## 核心特性

- 池化载体抽象：目标对象包装为 `ObjectBase` 派生类，统一 `OnSpawn` / `OnDespawn` / `Release` 事件
- 两种获取模式：`CreateSingleSpawnObjectPool`（单次获取）与 `CreateMultiSpawnObjectPool`（允许多次获取）
- 释放策略齐全：容量（`Capacity`）、过期秒数（`ExpireTime`）、自动释放间隔（`AutoReleaseInterval`）、对象锁定（`SetLocked`）与自定义释放标记（`CustomCanReleaseFlag`）
- 自定义释放筛选：`Release(ReleaseObjectFilterCallback<T>)` 可按需挑选要释放的对象
- GameObject 支持：`Support` 目录提供 `PoolObject`、`GameObjectPoolMgr` 与 `Object4PoolManager`，直接池化预制体实例
- 低内存联动：`GameModule.OnLowMemory` 会自动调用 `ReleaseAllUnused()` 释放所有未使用对象

## 核心类型

命名空间：`Moirai.Atropos.ObjectPool`

| 类/接口 | 说明 |
|---------|------|
| `IObjectPoolModule` | 管理器接口：创建/销毁/查询对象池、`Release()` / `ReleaseAllUnused()`，经 `GameModule.ObjectPool` 访问 |
| `ObjectPoolModule` | 默认实现（`internal sealed`），`Priority = 6`，实现 `IUpdateModule` 驱动各池过期与自动释放 |
| `IObjectPool<T>` | 单个对象池接口：`Register` / `CanSpawn` / `Spawn` / `Despawn` / `SetLocked` / `SetPriority` / `ReleaseObject` / `Release` / `ReleaseAllUnused` |
| `ObjectPoolBase` | 对象池抽象基类：`Name` / `FullName` / `ObjectType` / `Count` / `CanReleaseCount` / `AllowMultiSpawn` 等属性与 `GetAllObjectInfos()` |
| `ObjectBase` | 池化对象抽象基类（实现 `IMemory`）：`Initialize` 重载、`OnSpawn` / `OnDespawn` / `Release(bool isShutdown)` |
| `ObjectInfo` | 对象信息结构体：`Name` / `Locked` / `Priority` / `LastUseTime` / `SpawnCount` / `IsInUse` |
| `ReleaseObjectFilterCallback<T>` | 释放筛选委托：`(List<T> candidateObjects, int toReleaseCount, DateTime expireTime) -> List<T>` |
| `PoolObject` | Support：`ObjectBase` 的 GameObject 包装，`Create(string, GameObject)` 创建，Spawn 时恢复初始变换并激活 |
| `GameObjectPoolMgr` | Support：`SingletonMono_Persistent<GameObjectPoolMgr>`，按模板 GameObject 建池的 `Spawn` / `Despawn` 管理器 |
| `Object4PoolManager` | Support：挂在预制体上的 MonoBehaviour，提供 `Spawn<T>()` / `Despawn()` 与可选生命周期自动回收 |

## 快速上手

自定义池化对象并创建单次获取对象池：

```csharp
using Moirai.Atropos;
using Moirai.Atropos.ObjectPool;
using UnityEngine;

public class TextureObject : ObjectBase
{
    public Texture2D TargetTexture => (Texture2D)Target;

    public static TextureObject Create(string name, Texture2D target)
    {
        TextureObject obj = MemoryPool.Acquire<TextureObject>();
        obj.Initialize(name, target);
        return obj;
    }

    protected internal override void Release(bool isShutdown)
    {
        // 非关闭期间被释放时清理目标资源；isShutdown 为 true 表示随池一起关闭
    }
}

// 创建对象池：(name, autoReleaseInterval, capacity, expireTime, priority)
// 未指定时的默认值：capacity = int.MaxValue，expireTime = float.MaxValue，priority = 0
IObjectPool<TextureObject> pool = GameModule.ObjectPool
    .CreateSingleSpawnObjectPool<TextureObject>("textures", 60f, 16, 300f, 0);

// 注册一个已生成的对象（spawned: false 表示注册后留在池中待取，可用于预热）
pool.Register(TextureObject.Create("hero", LoadTexture("hero")), false);

// 获取与回收
if (pool.CanSpawn("hero"))
{
    TextureObject obj = pool.Spawn("hero");
    // ... 使用 obj.TargetTexture ...
    pool.Despawn(obj);
}

// 销毁对象池
GameModule.ObjectPool.DestroyObjectPool(pool);
```

Support 层的 GameObject 池（内部基于 `GameModule.ObjectPool` 建池）：

```csharp
// 按模板实例化并池化；同一模板复用同一个池
PoolObject po = GameObjectPoolMgr.Instance.Spawn(templateGo, parent);
GameObject go = po.TargetGameObject;
// ... 使用完毕后回收（自动停用并挂回池根节点）...
GameObjectPoolMgr.Instance.Despawn(po);

// 预制体方案：在预制体根节点挂 Object4PoolManager 派生组件
// Bullet : Object4PoolManager，Inspector 中可将 m_LifeTime 设为 > 0 实现到期自动回收
Bullet bullet = templateBullet.Spawn<Bullet>();
bullet.Despawn();
```

## 进阶用法

释放策略与筛选：

```csharp
pool.AutoReleaseInterval = 30f; // 每 30 秒自动释放一次可释放对象
pool.ExpireTime = 120f;         // 未使用超过 120 秒的对象视为过期
pool.Capacity = 64;             // 容量上限，超限时按优先级与最近使用时间淘汰
pool.Priority = 10;             // 池优先级，用于 GetAllObjectPools(true) 排序

// 锁定/解锁单个对象（锁定对象不会被释放）
pool.SetLocked(obj, true);

// 立即释放可释放对象，或用筛选回调挑选候选
pool.Release();
pool.Release(4); // 尝试释放 4 个
pool.Release((List<TextureObject> candidates, int toReleaseCount, DateTime expireTime) =>
{
    // 返回实际要释放的子集
    return candidates.FindAll(o => o.LastUseTime < expireTime);
});

// 全局操作：释放所有池的可释放对象 / 未使用对象
GameModule.ObjectPool.Release();
GameModule.ObjectPool.ReleaseAllUnused();
```

查询池与对象信息：

```csharp
bool has = GameModule.ObjectPool.HasObjectPool<TextureObject>("textures");
IObjectPool<TextureObject> p = GameModule.ObjectPool.GetObjectPool<TextureObject>("textures");
ObjectInfo[] infos = ((ObjectPoolBase)p).GetAllObjectInfos(); // 供调试器等遍历
foreach (ObjectInfo info in infos)
{
    bool inUse = info.IsInUse; int spawnCount = info.SpawnCount;
}
```

## 注意事项

- 池化对象必须继承 `ObjectBase`，在工厂方法中先 `MemoryPool.Acquire<T>()` 再调用 `Initialize(name, target, ...)`；`Initialize` 要求 `target` 非空
- `ObjectBase` 的 `Release(bool isShutdown)` 是必须实现的抽象方法，用于真正销毁目标资源
- 单次获取池中已被取出的对象不可再次 `Spawn`；需要共享同一对象请使用 `CreateMultiSpawnObjectPool`
- `Despawn` 前对象应处于已取出状态；`OnSpawn` / `OnDespawn` 为 `protected internal virtual`，可在子类中重写以挂接激活/停用逻辑
- `PoolObject.OnSpawn` 会把对象恢复到 `PoolObject.Create` 时记录的位置、旋转、缩放并 `SetActive(true)`；`Release` 时通过 `ObjectUtility.DestroyObject` 销毁 GameObject
- `GameObjectPoolMgr` 为模板自动创建的池参数为 `(300f, 100, 60f, 0)`，即 60 秒自动释放、容量 100、60 秒过期
- 收到系统低内存回调时框架会自动 `ReleaseAllUnused()`，无需手动响应

---
[« 返回主 README](../../README.md)
