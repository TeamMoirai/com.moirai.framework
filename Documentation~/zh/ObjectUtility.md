# ObjectUtility

> 框架的对象实例化/销毁外观，提供可插拔的 Handler 架构，支持单机与联网两种模式。

`ObjectUtility` 是框架的 Unity 对象创建与销毁的静态外观。框架内置 `UnityObjectHandler`（封装 `Object.Instantiate` / `Object.Destroy`）。Photon Fusion 的联网后端不在框架里，由 `plugin` 分支的 `photon-fusion` 插件提供——装上后实例化/销毁才会走网络同步流程。

## 核心特性

- 可插拔 Handler：框架内置 `UnityObjectHandler`；Fusion 网络同步后端由 `photon-fusion` 插件提供
- 四种 `InstantiateObject` 重载：仅原始对象、指定父级、指定位置旋转、位置旋转与父级全量
- 网络感知：`playerOwned` / `allowNetworked` 参数控制网络注册行为
- `DestroyObject` 统一销毁，支持网络感知
- 与 `Object.Instantiate` / `Object.Destroy` 签名对齐，切换 Handler 时调用方无感

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `ObjectUtility` | 静态外观，提供 `InstantiateObject<T>` / `DestroyObject` 方法 |
| `ObjectHandler` | 抽象基类，定义 `InstantiateObject` 4 重载 + `DestroyObject` |
| `UnityObjectHandler` | 框架内置默认实现，封装 `Object.Instantiate` / `Object.Destroy`（单机模式） |

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

联网后端不在框架里：先用 `Tools/Moirai/Package Manager` 装上 `plugin` 分支的 `photon-fusion` 插件（Fusion SDK 走 Photon 自己的发行渠道，接入后需在脚本后端宏里定义 `FUSION2`），再到 `GameAppSettings` 的「对象后端」下拉里选中它——下拉里那一列只在插件在场时出现，后端一律不在游戏代码里 `new`。切换完成后调用方写法不变：

```csharp
// 实例化为网络对象（playerOwned 标记归属）
ObjectUtility.InstantiateObject(prefab, playerOwned: true, allowNetworked: true);

// 销毁网络对象
ObjectUtility.DestroyObject(go, allowNetworked: true);
```

未装插件时 `allowNetworked` / `playerOwned` 不产生网络语义，调用落到框架内置的 `UnityObjectHandler`。

## 注意事项

- `Handler` 赋 null 抛出 `ArgumentNullException`；赋新值时自动调用旧 handler 的 `Internal_Shutdown()` 和新 handler 的 `Internal_Init()`
- 泛型约束 `where T : UnityEngine.Object`，支持 `GameObject`、`Component` 派生等类型
- 默认 `UnityObjectHandler` 中 `playerOwned` / `allowNetworked` 参数不影响行为（仅在网络 Handler 中生效）

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [ObjectPool](ObjectPool.md) · [Resource](Resource.md)
