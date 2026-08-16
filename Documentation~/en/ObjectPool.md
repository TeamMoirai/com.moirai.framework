# ObjectPool Module

> Modular object pool: An object pool system with capacity, expiration, priority, locking, and auto-release strategies, built around `ObjectBase`.

The ObjectPool module is accessed via `GameModule.ObjectPool` and manages any number of named object pools. Each pool holds a set of `ObjectBase`-derived objects, supporting both "single-spawn" (an object can only be checked out by one consumer at a time) and "multi-spawn" modes. It supports configurable capacity limits, object expiration seconds, auto-release intervals, and priority. The `Support` subdirectory provides ready-to-use GameObject pooling components on top of this module.

Note the distinction from the general-purpose object pool in `Runtime/Core/Pool` (namespace `Moirai.Atropos.Pool`): the latter is a lightweight internal utility -- `_ObjectPool<T>` (internal stack-based generic pool) and `GameObjectPoolManager` (GameObject pool keyed by `PoolKey`, with `Get`/`Release`/`ReleasePool`/`ReleaseAll`), with no module lifecycle or expiration strategy. This module is a full framework module (implements `IUpdateModule`, driven by [Core](Core.md)'s `ModuleSystem`), suitable for business-layer objects that require reference counting and release strategies.

## Core Features

- Pooled object abstraction: Target objects are wrapped as `ObjectBase`-derived classes, with unified `OnSpawn` / `OnDespawn` / `Release` events
- Two spawn modes: `CreateSingleSpawnObjectPool` (single-spawn) and `CreateMultiSpawnObjectPool` (allows multiple spawns)
- Comprehensive release strategies: Capacity (`Capacity`), expiration time (`ExpireTime`), auto-release interval (`AutoReleaseInterval`), object locking (`SetLocked`), and custom release flag (`CustomCanReleaseFlag`)
- Custom release filtering: `Release(ReleaseObjectFilterCallback<T>)` allows selecting which objects to release on demand
- GameObject support: The `Support` directory provides `PoolObject`, `GameObjectPoolMgr`, and `Object4PoolManager` for direct pooling of prefab instances
- Low memory integration: `GameModule.OnLowMemory` automatically calls `ReleaseAllUnused()` to release all unused objects

## Core Types

Namespace: `Moirai.Atropos.ObjectPool`

| Class/Interface | Description |
|---------|------|
| `IObjectPoolModule` | Manager interface: create/destroy/query object pools, `Release()` / `ReleaseAllUnused()`, accessed via `GameModule.ObjectPool` |
| `ObjectPoolModule` | Default implementation (`internal sealed`), `Priority = 6`, implements `IUpdateModule` to drive expiration and auto-release for each pool |
| `IObjectPool<T>` | Single object pool interface: `Register` / `CanSpawn` / `Spawn` / `Despawn` / `SetLocked` / `SetPriority` / `ReleaseObject` / `Release` / `ReleaseAllUnused` |
| `ObjectPoolBase` | Abstract base class for object pools: `Name` / `FullName` / `ObjectType` / `Count` / `CanReleaseCount` / `AllowMultiSpawn` and `GetAllObjectInfos()` |
| `ObjectBase` | Abstract base class for pooled objects (implements `IMemory`): `Initialize` overloads, `OnSpawn` / `OnDespawn` / `Release(bool isShutdown)` |
| `ObjectInfo` | Object information struct: `Name` / `Locked` / `Priority` / `LastUseTime` / `SpawnCount` / `IsInUse` |
| `ReleaseObjectFilterCallback<T>` | Release filter delegate: `(List<T> candidateObjects, int toReleaseCount, DateTime expireTime) -> List<T>` |
| `PoolObject` | Support: GameObject wrapper for `ObjectBase`, created via `Create(string, GameObject)`, restores initial transform and activates on Spawn |
| `GameObjectPoolMgr` | Support: `SingletonMono_Persistent<GameObjectPoolMgr>`, a manager for `Spawn` / `Despawn` using template GameObjects as pool keys |
| `Object4PoolManager` | Support: A MonoBehaviour attached to prefabs, providing `Spawn<T>()` / `Despawn()` and optional lifecycle auto-reclaim |

## Quick Start

Custom pooled object and creating a single-spawn object pool:

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
        // Clean up the target resource when released outside of shutdown; isShutdown = true means closing with the pool
    }
}

// Create an object pool: (name, autoReleaseInterval, capacity, expireTime, priority)
// Default values when not specified: capacity = int.MaxValue, expireTime = float.MaxValue, priority = 0
IObjectPool<TextureObject> pool = GameModule.ObjectPool
    .CreateSingleSpawnObjectPool<TextureObject>("textures", 60f, 16, 300f, 0);

// Register a spawned object (spawned: false means it stays in the pool for later retrieval, useful for pre-warming)
pool.Register(TextureObject.Create("hero", LoadTexture("hero")), false);

// Spawn and despawn
if (pool.CanSpawn("hero"))
{
    TextureObject obj = pool.Spawn("hero");
    // ... use obj.TargetTexture ...
    pool.Despawn(obj);
}

// Destroy the object pool
GameModule.ObjectPool.DestroyObjectPool(pool);
```

GameObject pooling at the Support layer (internally builds pools on `GameModule.ObjectPool`):

```csharp
// Instantiate from a template and pool; the same template reuses the same pool
PoolObject po = GameObjectPoolMgr.Instance.Spawn(templateGo, parent);
GameObject go = po.TargetGameObject;
// ... use and then despawn (automatically deactivates and re-parents to the pool root) ...
GameObjectPoolMgr.Instance.Despawn(po);

// Prefab approach: attach an Object4PoolManager-derived component to the prefab root
// Bullet : Object4PoolManager, set m_LifeTime > 0 in the Inspector for auto-reclaim on expiry
Bullet bullet = templateBullet.Spawn<Bullet>();
bullet.Despawn();
```

## Advanced Usage

Release strategies and filtering:

```csharp
pool.AutoReleaseInterval = 30f; // Auto-release releasable objects every 30 seconds
pool.ExpireTime = 120f;         // Objects unused for more than 120 seconds are considered expired
pool.Capacity = 64;             // Capacity limit; objects are evicted by priority and last use time when exceeded
pool.Priority = 10;             // Pool priority, used for sorting in GetAllObjectPools(true)

// Lock/unlock a single object (locked objects will not be released)
pool.SetLocked(obj, true);

// Immediately release releasable objects, or use a filter callback to select candidates
pool.Release();
pool.Release(4); // Attempt to release 4 objects
pool.Release((List<TextureObject> candidates, int toReleaseCount, DateTime expireTime) =>
{
    // Return the actual subset to release
    return candidates.FindAll(o => o.LastUseTime < expireTime);
});

// Global operations: release all releasable objects / unused objects across all pools
GameModule.ObjectPool.Release();
GameModule.ObjectPool.ReleaseAllUnused();
```

Querying pools and object information:

```csharp
bool has = GameModule.ObjectPool.HasObjectPool<TextureObject>("textures");
IObjectPool<TextureObject> p = GameModule.ObjectPool.GetObjectPool<TextureObject>("textures");
ObjectInfo[] infos = ((ObjectPoolBase)p).GetAllObjectInfos(); // For debugger traversal, etc.
foreach (ObjectInfo info in infos)
{
    bool inUse = info.IsInUse; int spawnCount = info.SpawnCount;
}
```

## Notes

- Pooled objects must inherit from `ObjectBase`. In the factory method, first call `MemoryPool.Acquire<T>()` and then `Initialize(name, target, ...)`; `Initialize` requires `target` to be non-null
- `ObjectBase`'s `Release(bool isShutdown)` is an abstract method that must be implemented to actually destroy the target resource
- In a single-spawn pool, an object that has been checked out cannot be spawned again; use `CreateMultiSpawnObjectPool` if the same object needs to be shared
- An object should be in a spawned state before calling `Despawn`; `OnSpawn` / `OnDespawn` are `protected internal virtual` and can be overridden in subclasses to hook activation/deactivation logic
- `PoolObject.OnSpawn` restores the object's position, rotation, and scale as recorded at `PoolObject.Create` time and calls `SetActive(true)`; `Release` destroys the GameObject via `ObjectUtility.DestroyObject`
- `GameObjectPoolMgr` creates pools for templates with default parameters `(300f, 100, 60f, 0)`, i.e., 60-second auto-release interval, capacity 100, 60-second expiration
- When the system sends a low-memory callback, the framework automatically calls `ReleaseAllUnused()`; no manual response is needed

---
[« Back to Main README](../README_EN.md)