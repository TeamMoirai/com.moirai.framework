# ObjectUtility

> Framework object instantiation/destruction facade, providing a pluggable Handler architecture with support for both standalone and networked modes.

`ObjectUtility` is the static facade for creating and destroying Unity objects in the framework. The framework ships `UnityObjectHandler` (wrapping `Object.Instantiate` / `Object.Destroy`). The Photon Fusion networked backend does not live in the framework — it ships as the `photon-fusion` plugin on the `plugin` branch; once installed, instantiation/destruction goes through the networked synchronization flow.

## Core Features

- Pluggable Handler: framework-built-in `UnityObjectHandler`; the Fusion network-synchronization backend ships as the `photon-fusion` plugin
- Four `InstantiateObject` overloads: original object only, specify parent, specify position/rotation, full parameters with position/rotation and parent
- Network awareness: `playerOwned` / `allowNetworked` parameters control network registration behavior
- `DestroyObject` unified destruction, with network awareness
- Aligned signatures with `Object.Instantiate` / `Object.Destroy`; switching handlers is transparent to the caller

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `ObjectUtility` | Static facade, providing `InstantiateObject<T>` / `DestroyObject` methods |
| `ObjectHandler` | Abstract base class, defining 4 `InstantiateObject` overloads + `DestroyObject` |
| `UnityObjectHandler` | Framework-built-in default implementation, wrapping `Object.Instantiate` / `Object.Destroy` (standalone mode) |

## Quick Start

```csharp
// Instantiate a prefab
GameObject go = ObjectUtility.InstantiateObject(prefab);

// Specify parent
ObjectUtility.InstantiateObject(prefab, parentTransform);

// Specify position and rotation
ObjectUtility.InstantiateObject(prefab, position, rotation);

// Full parameters
ObjectUtility.InstantiateObject(prefab, position, rotation, parent);

// Destroy object (allowNetworked controls whether to sync to the network)
ObjectUtility.DestroyObject(go);
ObjectUtility.DestroyObject(go, allowNetworked: false);
```

## Advanced Usage

### Networked Scenario (Fusion)

The networked backend is not in the framework: first install the `photon-fusion` plugin from the `plugin` branch via `Tools/Moirai/Package Manager` (Fusion comes from Photon's own distribution channel, so after importing the SDK you must define `FUSION2` in the scripting define symbols), then select it in the "object backend" dropdown of `GameAppSettings` — that entry only exists while the plugin is present, and backends are never `new`-ed in game code. Once switched, the call sites stay the same:

```csharp
// Instantiate as a networked object (playerOwned marks ownership)
ObjectUtility.InstantiateObject(prefab, playerOwned: true, allowNetworked: true);

// Destroy networked object
ObjectUtility.DestroyObject(go, allowNetworked: true);
```

Without the plugin, `allowNetworked` / `playerOwned` carry no network semantics and the calls land on the framework-built-in `UnityObjectHandler`.

## Notes

- Setting `Handler` to null throws `ArgumentNullException`; assigning a new value automatically calls `Internal_Shutdown()` on the old handler and `Internal_Init()` on the new handler
- Generic constraint `where T : UnityEngine.Object`, supports `GameObject`, `Component` derived types, etc.
- In the default `UnityObjectHandler`, the `playerOwned` / `allowNetworked` parameters do not affect behavior (they only take effect in the network Handler)

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [ObjectPool](ObjectPool.md) · [Resource](Resource.md)
