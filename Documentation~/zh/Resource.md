# Resource 资源服务

> 基于 YooAsset 封装的资源管理系统，采用 generation 校验的 Lease/Binding 架构，具备零 GC 分页槽位数组、时间轮过期回收、引用计数、多包加载、取消控制与加密解密能力。

Resource 服务（`ResourceService`）对 [YooAsset](https://github.com/tuyoogame/YooAsset) 做了面向业务的封装。模块已全面重构为 **Lease/Binding 架构**：资源通过 generation 校验的槽位句柄（`ResourceLeaseHandle`）和类型化租约（`ResourceAssetLease<T>`）管理，UI/渲染组件可通过 `ResourceOwner` + `IResourceBindingService` 进行声明式绑定。通过 `GameApp.Resource`（`IResourceService`）访问。

内部引擎使用**分页槽位数组**（`AssetSlot[][]`、`LeaseSlot[][]`、`BindingSlot[][]`、`OwnerSlot[][]`）配合 generation 校验、**自研零 GC 哈希映射**（`ResourceUlongIntMap`，Murmur 终结器混合；`ResourceIndexMap<TKey,TValue>`）、以及**时间轮**过期系统（idle 桶 + keep-alive 桶，每帧 O(1) 处理）。加载去重通过池化的 `LoadingOperationState` 对象实现。编辑器下的播放模式由 `ResourceServiceDriver` 组件驱动并可通过 EditorPrefs 切换。

## 核心特性

- **Lease API（推荐）：** `ResourceLeaseHandle` / `ResourceAssetLease<T>` 提供安全的、作用域化的资源引用。`ResourceAssetLease<T>` 实现 `IDisposable`，支持 `using` 语句自动释放。
- **Binding API（推荐）：** `ResourceOwner` MonoBehaviour + `IResourceBindingService` 提供声明式资源-组件绑定（Sprite、Material、PrefabSource、SubSprite），`OnDestroy` 时自动释放所有绑定。
- **扩展方法：** `Image.SetSprite(location)`、`SpriteRenderer.SetSprite(location)`、`Image.SetSubSprite(location, spriteName)`、`Image/SpriteRenderer/MeshRenderer.SetMaterial(location)`、`MeshRenderer.SetSharedMaterial(location)` —— 全部通过绑定系统自动管理生命周期。
- **异步绑定安全：** 版本校验的绑定请求防止过期异步结果覆盖较新的绑定。
- 四种播放模式：`EditorSimulateMode`（编辑器模拟）、`OfflinePlayMode`（单机）、`HostPlayMode`（联机热更）、`WebPlayMode`（WebGL，支持微信小游戏文件系统）
- **时间轮过期：** 空闲资源（引用计数 = 0）在 `IdleAssetExpireTime` 秒后被释放。Keep-alive 租约可临时延长生命周期。`ProcessKeepAlive` 每帧以 O(1) 复杂度处理两个队列。
- **加载去重：** 同地址并发加载共享同一个 `LoadingOperationState`（池化 `MemoryObject`），支持等待者计数与取消。
- 资源加密：`EncryptionType.FileOffSet`（32 字节偏移）与 `EncryptionType.FileStream`（XOR 流加密），附带 Web 端解密实现
- 热更下载：请求远端清单版本、更新 Manifest、创建下载器、清理缓存文件一应俱全
- **遗留 API（仍可用）：** `LoadAsset<T>` / `LoadAssetAsync<T>` / `UnloadAsset` / 回调式 `LoadAssetAsync` 保留并内部桥接到租约系统（通过遗留直接引用计数）。标记为 `[Obsolete]`。

## 核心类型

命名空间：`Moirai.Atropos.Resource`

### 租约 / 标识类型

| 结构体/接口 | 说明 |
|---------|------|
| `ResourceLeaseHandle` | generation 校验的槽位句柄（`readonly struct`），用于安全引用资源记录。字段：`Index`、`Generation`。静态 `Invalid` 表示无效句柄。`IsValid` 检查有效性。 |
| `ResourceAssetLease<T>` | 类型化租约（`struct`，实现 `IDisposable`），持有资源对象并在 `Dispose` 时自动释放。支持 `using` 语句。字段：`Asset`、`Handle`、`IsValid`。 |
| `ResourceKey` | `readonly struct`，描述资源位置、包名、类型和种类。工厂方法 `ResourceKey.Asset<T>(location, packageName)` 创建类型化键。`HasResolvedIds` 检查内部 ID 解析状态。 |
| `ResourceAssetKind` | 枚举：`Unknown / Asset / Sprite / Material / Prefab / SubAssets` |
| `ResourceAssetState` | 枚举：`Released / Loading / Active / KeepAlive / Idle` |
| `ResourceAssetInfo` | 诊断快照结构体：LoadKeyId、Package、Location、TypeName、Kind、State、DirectRefCount、LegacyDirectRefCount、BindingRefCount、KeepAliveRefCount、RefCountTotal、IdleExpireIn 等 |
| `ResourceBindingInfo` | 绑定诊断快照结构体：Active、BindingIndex、OwnerId、TargetComponentId、Lease、Version、SlotType、HasAppliedAsset 等 |
| `ResourceOwnerInfo` | 所有者诊断快照结构体：Active、OwnerIndex、OwnerId、GameObjectId、Generation、BindingCount、RegisteredTargetCount |

### 服务接口与组件

| 类/接口 | 说明 |
|---------|------|
| `IResourceService` | 资源管理器接口，定义加载、租约、绑定、卸载、包操作全部 API；经 `GameApp.Resource` 访问 |
| `IResourceBindingService` | 声明式资源-组件绑定服务接口，经 `IResourceService.BindingService` 访问 |
| `ResourceService` | 内部实现（`internal sealed partial class`，拆分为：主逻辑 / Records（槽位系统 + 时间轮）/ Cache（容量与遗留桥接）/ Services） |
| `ResourceServiceDriver` | MonoBehaviour 驱动组件，Inspector 配置播放模式、加密类型、下载参数、容量/过期参数，并周期执行 `UnloadUnusedAssets` + `ProcessKeepAlive` |
| `ResourceOwner` | MonoBehaviour 组件（`[DisallowMultipleComponent]`），`OnDestroy` 时自动释放所有绑定。提供 `ReleaseBindings()`、`ReleaseBindingsInHierarchy(root)`、`EnsureFor(target, bindingService)`。 |
| `ResourceBindingExtensions` | 静态扩展类：`Image/SpriteRenderer.SetSprite`、`Image/SpriteRenderer.SetSubSprite`、`Image/SpriteRenderer/MeshRenderer.SetMaterial`、`MeshRenderer.SetSharedMaterial` |
| `ResourceBindingTypes` | 绑定相关枚举与接口：`ResourceBindStatus`、`ResourceBindingOptions`、`ResourceBindingSlotType` |
| `LoadAssetCallbacks` | 回调式加载的回调函数集，组合成功/失败/进度三个委托 |
| `LoadAssetSuccessCallback` 等委托 | `(string assetName, object asset, float duration, object userData)` 等签名，另有场景加载/卸载回调组 |
| `LoadResourceStatus` | 加载结果状态枚举：`Success / NotExist / NotReady / DependencyError / TypeError / AssetError` |
| `HasAssetResult` | 资源存在性检查结果：`NotExist / AssetOnline / AssetOnDisk / AssetOnFileSystem / BinaryOnDisk / BinaryOnFileSystem / Valid` |
| `EncryptionType` | 加密方式枚举：`None / FileOffSet / FileStream` |
| `FileStreamEncryption` / `FileOffsetEncryption` | 打包侧加密服务（实现 YooAsset `IEncryptionServices`） |
| `FileStreamDecryption` / `FileOffsetDecryption` 及 Web 变体 | 运行时解密服务（实现 `IDecryptionServices` / `IWebDecryptionServices`） |
| `RemoteServices` | 远端资源地址查询服务（internal），拼接主/备 URL |
| `ResourceUlongIntMap` | 自研开放寻址 `ulong→int` 哈希映射，Murmur3 终结器混合键值，SoA 布局，热路径零 GC（internal） |
| `ResourceIndexMap<TKey,TValue>` | 泛型开放寻址哈希映射，struct 键/值，热路径零 GC（internal） |
| `LoadingOperationState` | 池化 `MemoryObject`，用于异步加载去重：跟踪 AssetHandle/SubAssetsHandle、等待者计数、完成状态（internal） |

## 快速上手

### Lease API（推荐）

Lease API 通过 `IDisposable` 提供显式所有权与自动释放：

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using UnityEngine;

// 同步租约 + using 语句（作用域结束时自动释放）
using var lease = GameApp.Resource.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
Sprite icon = lease.Asset;

// 异步租约 + CancellationToken
var cts = new CancellationTokenSource();
using (var lease2 = await GameApp.Resource.LoadLeaseAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token))
{
    Texture2D tex = lease2.Asset;
    // ... 使用 tex
} // lease2.Dispose() 自动调用

// AcquireDirect 手动管理句柄生命周期
ResourceKey key = ResourceKey.Asset<GameObject>("Assets/AssetRaw/Prefabs/Hero.prefab");
ResourceLeaseHandle handle = GameApp.Resource.AcquireDirect(key);
if (GameApp.Resource.TryGetLeaseAsset(handle, out Object asset))
{
    // ... 使用 asset
}
GameApp.Resource.Release(handle);

// 异步 AcquireDirect
ResourceLeaseHandle handle2 = await GameApp.Resource.AcquireDirectAsync(key, cts.Token);
// ... 使用 handle2
GameApp.Resource.Release(handle2);
```

### Binding API（推荐）

通过扩展方法进行声明式绑定 —— 无需手动 `UnloadAsset`：

```csharp
// 为 Image 设置精灵（自动管理：释放旧绑定，绑定新资源）
image.SetSprite("Assets/AssetRaw/UI/icon.png", setNativeSize: true);

// 为 SpriteRenderer 设置精灵
spriteRenderer.SetSprite("Assets/AssetRaw/UI/icon.png");

// 设置图集内的子精灵
image.SetSubSprite("Assets/AssetRaw/UI/atlas.png", "btn_ok");

// 设置共享材质
meshRenderer.SetSharedMaterial("Assets/AssetRaw/Mat/skin.mat");

// 设置材质实例（创建运行时副本）
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", needInstance: true);

// 异步材质绑定
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", isAsync: true);
```

当首次对某组件调用 `SetSprite`/`SetMaterial` 扩展方法时，会自动在 GameObject 上添加 `ResourceOwner`（如未存在）并注册到绑定服务。`OnDestroy` 时 `ResourceOwner` 自动释放所有绑定。

### 遗留 API（仍可用，标记 `[Obsolete]`）

```csharp
// 同步加载（内部通过遗留直接引用计数桥接到租约系统）
Sprite icon = GameApp.Resource.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 异步加载（UniTask，支持 CancellationToken 取消）
var cts = new CancellationTokenSource();
Texture2D tex = await GameApp.Resource.LoadAssetAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token);

// 异步实例化：Destroy 时自动卸载引用
GameObject hero = await GameApp.Resource.LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// 同步实例化
GameObject go = GameApp.Resource.LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);

// 卸载手动加载的资源（递减遗留直接引用计数）
GameApp.Resource.UnloadAsset(icon);

// 回调式异步（async void；异常经 LoadAssetFailureCallback 上报）
GameApp.Resource.LoadAssetAsync(
    "Assets/AssetRaw/Audio/bgm.mp3", 0,
    new LoadAssetCallbacks(
        (assetName, asset, duration, userData) => { /* 成功 */ },
        (assetName, status, errorMessage, userData) => { /* 失败 */ },
        (assetName, progress, userData) => { /* 进度 0~1 */ }),
    null);
```

> **注意：** `LoadGameObject` / `LoadGameObjectAsync` **未**被标记为过时 —— 它们内部使用新租约系统（通过 `AcquirePrefabSourceLease`），并在实例上挂载 `ResourceOwner` 实现自动清理。

## 架构

### 分页槽位数组与 Generation 校验

所有内部状态通过 struct 槽位的分页数组管理：

- `AssetSlot[][]` —— 资源记录（资产、句柄、引用计数、过期队列链接、状态）
- `LeaseSlot[][]` —— 租约记录（资产 ID、种类、状态、generation）
- `BindingSlot[][]` —— 绑定记录（所有者、目标、租约、版本、槽位类型）
- `OwnerSlot[][]` —— 所有者记录（GameObject ID、generation、绑定链）

每页 256 个槽位（8 位页索引）。槽位从空闲链表栈分配，通过单调递增的 `Generation` 计数器校验。`ResourceLeaseHandle` 仅在槽位索引和 generation 同时匹配时有效，防止槽位回收后的 use-after-free。

### 64 位打包 ResourceKey

资源标识打包为单个 `ulong` 键：包 ID（16 位）+ 位置 ID（28 位）+ 类型 ID（16 位）+ 资产种类（2 位）+ 句柄种类（2 位）。字符串到 ID 的映射通过引用计数注册表（`_resourcePackagesById`、`_resourceLocationsById`、`_resourceTypesById`）管理，支持空闲链表回收。

### 自研零 GC 数据结构

- `ResourceUlongIntMap` —— 开放寻址 `ulong→int` 映射，使用 Murmur3 终结器混合键值，SoA（Structure of Arrays）布局（`_keys[]`、`_values[]`、`_states[]`），70% 负载因子，墓碑标记删除。热路径操作（TryGetValue/Set）零 GC 分配。
- `ResourceIndexMap<TKey,TValue>` —— 泛型开放寻址映射，用于 struct 键/值，绑定服务使用它进行 `OwnerSlotKey → 绑定索引` 查找。

### 时间轮过期

两个环形桶数组（各 256 个桶）驱动每帧 O(1) 过期处理：

- **Idle 桶：** 当资产引用计数归零时，进入 idle 桶，计划在 `IdleAssetExpireTime` 秒后过期。
- **Keep-alive 桶：** 当租约以 `KeepAliveOnRelease` 选项释放时，资产的 keep-alive 引用计数递增，计划在 `IdleAssetExpireTime` 秒后过期。

`ProcessKeepAlive(unscaledTime, maxProcessCount)` 由 `ResourceServiceDriver` 每帧调用，处理两个队列中已过期的资产。

### 加载去重

同一资源的并发加载共享单个 `LoadingOperationState`（池化 `MemoryObject`）。首个调用者通过 `TryBeginLoading`（返回 `true`）发起加载，后续调用者获得 `false` 并通过 `WaitForLoadingAsync` 等待。加载完成/失败时，`CompleteLoading`/`FailLoading` 通知所有等待者并释放池化对象。

## Lease API 参考

### ResourceLeaseHandle

```csharp
public readonly struct ResourceLeaseHandle
{
    public static readonly ResourceLeaseHandle Invalid;
    public readonly int Index;
    public readonly uint Generation;
    public bool IsValid => Index >= 0 && Generation != 0;
}
```

### ResourceAssetLease\<T\>

```csharp
public struct ResourceAssetLease<T> : IDisposable where T : UnityEngine.Object
{
    public T Asset { get; }
    public ResourceLeaseHandle Handle { get; }
    public bool IsValid { get; }
    public void Dispose(); // 调用 IResourceService.Release(Handle)
}
```

### ResourceKey

```csharp
public readonly struct ResourceKey
{
    public readonly string PackageName;
    public readonly string Location;
    public readonly Type AssetType;
    public readonly ResourceAssetKind AssetKind;
    public bool HasResolvedIds { get; }

    public ResourceKey(string location, string packageName = "", Type assetType = null,
        ResourceAssetKind assetKind = ResourceAssetKind.Unknown);

    // 工厂方法：创建类型化资源标识键
    public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object;
}
```

### IResourceService 租约方法

| 方法 | 说明 |
|--------|------|
| `ResourceLeaseHandle AcquireDirect(ResourceKey key)` | 同步获取直接租约。失败返回 `Invalid`。 |
| `UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken)` | 异步获取直接租约。 |
| `bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)` | 尝试获取变体。 |
| `void Release(ResourceLeaseHandle handle)` | 释放租约（递减引用计数）。 |
| `ResourceAssetLease<T> LoadLease<T>(ResourceKey key)` | 同步加载并返回类型化租约。 |
| `ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "")` | 按地址同步加载并返回类型化租约。 |
| `UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken)` | 异步加载并返回类型化租约。 |
| `UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken, string packageName)` | 按地址异步加载并返回类型化租约。 |
| `bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)` | 从租约句柄读取 Unity 资产对象。 |
| `IResourceBindingService BindingService { get; }` | 访问绑定服务。 |

## Binding API 参考

### ResourceOwner

```csharp
[DisallowMultipleComponent]
public sealed class ResourceOwner : MonoBehaviour
{
    public int OwnerId { get; }
    public ulong GameObjectId { get; }
    public uint Generation { get; }
    public bool IsRegistered { get; }

    public ResourceBindStatus ReleaseBindings(); // 释放此所有者上的所有绑定

    // 释放层级中所有 ResourceOwner 的绑定
    public static int ReleaseBindingsInHierarchy(GameObject root);

    // 确保目标组件的 GameObject 上存在 ResourceOwner
    public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService);

    public static void WarmupReleaseBuffer(int capacity);
}
```

### IResourceBindingService

| 方法 | 说明 |
|--------|------|
| `ResourceBindStatus RegisterOwner(ResourceOwner owner)` | 注册所有者。 |
| `ResourceBindStatus ReleaseOwner(ResourceOwner owner)` | 释放所有者及其所有绑定。 |
| `ResourceBindStatus ReleaseOwner(int ownerId, uint generation)` | 按 ID + generation 释放。 |
| `void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity)` | 预分配绑定数据结构。 |
| `ResourceBindStatus RegisterTarget(ResourceOwner, Component)` | 注册目标组件用于跟踪。 |
| `ResourceBindStatus UnregisterTarget(ResourceOwner, Component)` | 注销目标组件。 |
| `ResourceBindStatus BindSprite(ResourceOwner, Image, ResourceKey, options)` | 绑定精灵到 Image。 |
| `ResourceBindStatus BindSprite(ResourceOwner, SpriteRenderer, ResourceKey, options)` | 绑定精灵到 SpriteRenderer。 |
| `UniTask<ResourceBindStatus> BindSubSpriteAsync(ResourceOwner, Image, ResourceKey atlasKey, string spriteName, options, CancellationToken)` | 异步从图集绑定子精灵。 |
| `ResourceBindStatus BindImageMaterial(ResourceOwner, Image, ResourceKey, options)` | 绑定材质到 Image。 |
| `UniTask<ResourceBindStatus> BindImageMaterialAsync(ResourceOwner, Image, ResourceKey, options, CancellationToken)` | 异步绑定材质到 Image。 |
| `ResourceBindStatus BindSharedMaterial(ResourceOwner, Renderer, ResourceKey, options)` | 绑定共享材质到 Renderer。 |
| `UniTask<ResourceBindStatus> BindSharedMaterialAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | 异步绑定共享材质。 |
| `ResourceBindStatus BindMaterialInstance(ResourceOwner, Renderer, ResourceKey, options)` | 绑定材质实例（运行时副本）到 Renderer。 |
| `UniTask<ResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | 异步绑定材质实例。 |
| `int GetOwnerInfos(ResourceOwnerInfo[], int, int)` | 诊断：批量查询所有者状态。 |
| `int GetBindingInfos(ResourceBindingInfo[], int, int)` | 诊断：批量查询绑定状态。 |

### ResourceBindStatus

枚举值：`Success / InvalidKey / MissingOwner / MissingTarget / StaleOwner / LoadFailed / ApplyFailed / ServiceShutdown / NotImplemented`

### ResourceBindingOptions

标志枚举：`None / KeepAliveOnRelease / SetNativeSize`

### 扩展方法（ResourceBindingExtensions）

| 扩展方法 | 说明 |
|-----------|------|
| `Image.SetSprite(string location, bool setNativeSize = false, CancellationToken)` | 通过绑定设置 Image 精灵。 |
| `SpriteRenderer.SetSprite(string location, CancellationToken)` | 通过绑定设置 SpriteRenderer 精灵。 |
| `Image.SetSubSprite(string location, string spriteName, bool setNativeSize = false, CancellationToken)` | 从图集设置 Image 子精灵。 |
| `Image.SetMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 Image 材质。 |
| `SpriteRenderer.SetMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 SpriteRenderer 材质。 |
| `MeshRenderer.SetMaterial(string location, bool needInstance = true, bool isAsync = false, string packageName = "")` | 设置 MeshRenderer 材质（实例或共享）。 |
| `MeshRenderer.SetSharedMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 MeshRenderer 共享材质。 |

### 异步绑定安全

异步绑定方法（如 `BindSubSpriteAsync`、`BindImageMaterialAsync`、`BindSharedMaterialAsync`、`BindMaterialInstanceAsync`）使用**版本校验的绑定请求**防止过期结果：

1. 异步加载开始前，预留绑定槽位并递增 `Version`。
2. 异步加载完成后，`IsBindingRequestCurrent` 检查所有者 generation、目标组件 ID 和绑定版本是否全部匹配。
3. 如果所有者已销毁、目标已替换、或有更新的绑定请求取代了当前请求，则丢弃过期结果并释放租约。

## 容量与过期属性

在 `ResourceServiceDriver`（Inspector）或通过 `IResourceService` 配置：

| 属性 | 默认值 | 说明 |
|----------|---------|------|
| `AssetRecordCapacity` | 64 | 资源记录预热容量（AssetSlot 页）。 |
| `AssetLeaseCapacity` | 128 | 租约槽位预热容量（LeaseSlot 页）。 |
| `BindingOwnerCapacity` | 64 | 绑定所有者预热容量（OwnerSlot 页）。 |
| `BindingSlotCapacity` | 128 | 绑定槽位预热容量（BindingSlot 页）。 |
| `RegisteredTargetCapacity` | 128 | 已注册目标预热容量。 |
| `IdleAssetExpireTime` | 60s | 无引用资源句柄空闲过期秒数。 |
| `ExpireProcessCountPerFrame` | 16 | 每帧过期处理最大数量。 |
| `ExpireProcessCountWhenUnloading` | 256 | 卸载时过期处理最大数量。 |

### WarmupResourceRecords

```csharp
void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity);
```

预分配内部数据结构（槽位页、索引映射），避免运行时扩容。设置容量属性时自动调用。

### GetAssetInfos（诊断）

```csharp
int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
```

批量查询资源记录状态。返回实际写入数量。每个 `ResourceAssetInfo` 包含包名、位置、类型、种类、状态、引用计数（直接/遗留/绑定/keep-alive）及过期信息。

## 卸载 API

| 方法 | 说明 |
|--------|------|
| `void UnloadUnusedAssets()` | 释放已过空闲过期时间的无用资源（引用计数=0）。同时清空 AssetInfo 缓存并调用 `package.UnloadUnusedAssetsAsync()`。 |
| `void UnloadUnusedAssets(bool force)` | `force=true`：忽略空闲过期时间，立即处理 keep-alive 队列并释放所有无用记录。 |
| `void ForceUnloadAllAssets()` | 强制卸载所有包上的所有资产（WebGL 不支持 —— 仅打印警告）。 |
| `void ForceUnloadUnusedAssets(bool performGCCollect)` | 触发驱动器的强制卸载路径（可选 GC.Collect）。 |
| `void ProcessKeepAlive(float unscaledTime, int maxProcessCount)` | 每帧时间轮过期处理（idle + keep-alive 桶）。由 `ResourceServiceDriver.Update()` 调用。 |

## 配置与扩展

### 播放模式与加密

编辑器中在场景的 `ResourceServiceDriver` 组件上配置（也可用菜单 `YooAsset/Editor PlayMode` 切换，编辑器设置优先于序列化值；真机下 `EditorSimulateMode` 自动降级为 `OfflinePlayMode`）：

- `PlayMode`：四种播放模式，决定 `InitPackage` 走模拟构建、内置文件系统、缓存文件系统还是 Web 文件系统
- `EncryptionType`：`None / FileOffSet / FileStream`，运行时据此创建对应解密服务
- `PackageName`：默认资源包名（默认 `DefaultPackage`），多包项目通过各 API 的 `packageName` 参数指定其它包
- `Milliseconds`：异步系统每帧最大时间切片（默认 30ms）
- `AutoUnloadBundleWhenUnused`：引用计数为零时自动卸载资源包
- `DownloadingMaxNum` / `FailedTryAgain`：下载并发数（默认 10）与失败重试次数（默认 3）
- `UpdatableWhilePlaying`：边玩边下载
- `AssetAutoReleaseInterval / AssetCapacity / AssetExpireTime / AssetPriority`：遗留池参数（桥接到 IdleAssetExpireTime / AssetRecordCapacity）
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`：无用资源回收的最小/最大间隔（默认 60s / 300s）
- `UseSystemUnloadUnusedAssets`：是否在系统卸载周期中调用 `IResourceService.UnloadUnusedAssets()`

### 热更流程相关 API

```csharp
IResourceService res = GameApp.Resource;

// 初始化指定资源包（needInitMainFest: true 时顺带请求并更新清单，单机 OtherPackage 场景）
await res.InitPackage("DefaultPackage");

// 联机模式：请求远端版本 -> 更新清单 -> 创建下载器 -> 下载
var op = res.RequestPackageVersionAsync();
res.PackageVersion = op.PackageVersion;
res.UpdatePackageManifestAsync(res.PackageVersion);
var downloader = res.CreateResourceDownloader();   // 之后轮询 downloader

// 远端地址与缓存清理
res.SetRemoteServicesUrl("https://cdn.example.com/res", "https://backup.example.com/res");
res.ClearCacheFilesAsync();                        // 清理未使用的缓存文件
res.ClearAllBundleFiles();                         // 清空沙盒路径
```

### 资源查询与句柄

```csharp
HasAssetResult result = GameApp.Resource.HasAsset("Assets/AssetRaw/UI/icon.png");
bool valid = GameApp.Resource.CheckLocationValid("Assets/AssetRaw/UI/icon.png");
AssetInfo[] infos = GameApp.Resource.GetAssetInfos("Preload");   // 按标签批量获取

// 需要精细控制句柄生命周期时（不经过租约系统）
AssetHandle handle = GameApp.Resource.LoadAssetAsyncHandle<GameObject>("path");
// ... 使用 handle.AssetObject，用完 handle.Dispose()
```

## 注意事项

- **Lease API：** `ResourceAssetLease<T>` 是 `struct` —— 务必调用 `Dispose`（使用 `using` 语句）。Dispose 后 `IsValid` 返回 `false`，`Asset` 为 `null`。
- **Binding API：** `SetSprite`/`SetMaterial` 扩展方法在目标 GameObject 上不存在 `ResourceOwner` 时自动添加。GameObject 销毁时所有绑定自动释放。
- **遗留 API：** `LoadAsset<T>` / `LoadGameObject` 返回的是池化共享对象，不要直接 `Destroy`；需要销毁请用 `UnloadAsset` 归还引用。`LoadGameObject`/`LoadGameObjectAsync` 内部使用租约系统并挂载 `ResourceOwner` 实现自动清理。
- `LoadAssetAsync<T>` 被取消（`cancellationToken` 触发）时返回 `null` 并释放内部句柄，调用方需判空。
- WebGL 平台不支持 `ForceUnloadAllAssets`，调用只会打印警告。
- 回调式 `LoadAssetAsync(string, int, LoadAssetCallbacks, object, string)` 为 `async void`，异常经 `LoadAssetFailureCallback`（`LoadResourceStatus.AssetError`）上报。
- 加密方式的打包侧（`FileStreamEncryption` 等）与运行时解密侧需一致，`BundleStream` 的 XOR 密钥为固定常量（`KEY = 64`），仅作防直读用途。
- `GetAssetInfo` 对默认包结果做了字典缓存，切换清单（热更完成）后如需最新信息请先调用 `UnloadUnusedAssets()`（会清空缓存）。
- 低内存时系统回调 `GameApp.OnLowMemory` 会触发 `ForceUnloadUnusedAssets(true)`，随后执行 `Resources.UnloadUnusedAssets` 与 `GC.Collect`。

---
[« 返回主 README](../../README.md) · [Procedure](Procedure.md) · [Save](Save.md)