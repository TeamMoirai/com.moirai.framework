# ObjectUtility

> Framework object instantiation/destruction facade, providing a pluggable Handler architecture with support for both standalone and networked modes.

`ObjectUtility` is the static facade for creating and destroying Unity objects in the framework. By default, it uses `UnityObjectHandler` (wrapping `Object.Instantiate` / `Object.Destroy`). When integrating with network libraries such as Photon Fusion, it can be switched to `PhotonFusionObjectHandler`, so that instantiation/destruction automatically follows the network synchronization flow.

## Core Features

- Pluggable Handler: `UnityObjectHandler` (default) / `PhotonFusionObjectHandler` (Fusion network synchronization)
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
| `UnityObjectHandler` | Default implementation, wrapping `Object.Instantiate` / `Object.Destroy` (standalone mode) |
| `PhotonFusionObjectHandler` | Networked implementation, wrapping Fusion network instantiation/destruction |

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

```csharp
// Switch to Fusion network Handler
ObjectUtility.Handler = new PhotonFusionObjectHandler();

// Instantiate as a networked object (playerOwned marks ownership)
ObjectUtility.InstantiateObject(prefab, playerOwned: true, allowNetworked: true);

// Destroy networked object
ObjectUtility.DestroyObject(go, allowNetworked: true);
```

## Notes

- Setting `Handler` to null is ignored and does not reset; assigning a new value automatically calls `Shutdown()` on the old handler and `OnInit()` on the new handler
- Generic constraint `where T : UnityEngine.Object`, supports `GameObject`, `Component` derived types, etc.
- In the default `UnityObjectHandler`, the `playerOwned` / `allowNetworked` parameters do not affect behavior (they only take effect in the network Handler)

---
[« Back to Main README](../../README_EN.md)