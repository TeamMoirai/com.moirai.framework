# ObjectUtility

> 框架的对象实例化/销毁门面，提供可插拔的 Handler 架构，支持单机与联网两种模式。

`ObjectUtility` 是框架的 Unity 对象创建与销毁的静态门面。默认使用 `UnityObjectHandler`（封装 `Object.Instantiate` / `Object.Destroy`）。当集成 Photon Fusion 等网络库时，可切换为 `PhotonFusionObjectHandler`，使实例化/销毁自动走网络同步流程。

## 核心特性

- 可插拔 Handler：`UnityObjectHandler`（默认）/ `PhotonFusionObjectHandler`（Fusion 网络同步）
- 四种 `InstantiateObject` 重载：仅原始对象、指定父级、指定位置旋转、位置旋转与父级全量
- 网络感知：`playerOwned` / `allowNetworked` 参数控制网络注册行为
- `DestroyObject` 统一销毁，支持网络感知
- 与 `Object.Instantiate` / `Object.Destroy` 签名对齐，切换 Handler 时调用方无感

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `ObjectUtility` | 静态门面，提供 `InstantiateObject<T>` / `DestroyObject` 方法 |
| `ObjectHandler` | 抽象基类，定义 `InstantiateObject` 4 重载 + `DestroyObject` |
| `UnityObjectHandler` | 默认实现，封装 `Object.Instantiate` / `Object.Destroy`（单机模式） |
| `PhotonFusionObjectHandler` | 联网实现，封装 Fusion 网络实例化/销毁 |

## 快速上手

```csharp
// 实例化预制体
GameObject go = ObjectUtility.InstantiateObject(prefab);

// 指定父级
ObjectUtility.InstantiateObject(prefab, parentTransform);

// 指定位置与旋转
ObjectUtility.InstantiateObject(prefab, position, rotation);

// 全量参数
ObjectUtility.InstantiateObject(prefab, position, rotation, parent);

// 销毁对象（allowNetworked 控制是否同步到网络）
ObjectUtility.DestroyObject(go);
ObjectUtility.DestroyObject(go, allowNetworked: false);
```

## 进阶用法

### 联网场景（Fusion）

```csharp
// 切换为 Fusion 网络 Handler
ObjectUtility.Handler = new PhotonFusionObjectHandler();

// 实例化为网络对象（playerOwned 标记归属）
ObjectUtility.InstantiateObject(prefab, playerOwned: true, allowNetworked: true);

// 销毁网络对象
ObjectUtility.DestroyObject(go, allowNetworked: true);
```

## 注意事项

- `Handler` 赋 null 抛出 `ArgumentNullException`；赋新值时自动调用旧 handler 的 `Internal_Shutdown()` 和新 handler 的 `Internal_Init()`
- 泛型约束 `where T : UnityEngine.Object`，支持 `GameObject`、`Component` 派生等类型
- 默认 `UnityObjectHandler` 中 `playerOwned` / `allowNetworked` 参数不影响行为（仅在网络 Handler 中生效）

---
[« 返回主 README](../../README.md)