# Resource 资源服务

> 基于 YooAsset 封装的资源管理系统，采用 generation 校验的 Lease/Binding 架构，具备零 GC 分页槽位数组、时间轮过期回收、引用计数、多包加载、取消控制与加密解密能力。

Resource 服务（`ResourceService`）对 [YooAsset](https://github.com/tuyoogame/YooAsset) 做了面向业务的封装。模块已全面重构为 **Lease/Binding 架构**：资源通过 generation 校验的槽位句柄（`ResourceLeaseHandle`）和类型化租约（`ResourceAssetLease<T>`）管理，UI/渲染组件可通过 `ResourceOwner` + `IResourceBindingService` 进行声明式绑定。通过 `ResourceService` 静态外观访问。

内部引擎使用**分页槽位数组**（`AssetSlot[][]`、`LeaseSlot[][]`、`BindingSlot[][]`、`OwnerSlot[][]`）配合 generation 校验、**自研零 GC 哈希映射**（`ResourceUlongIntMap`，Murmur 终结器混合；`ResourceIndexMap<TKey,TValue>`）、以及**时间轮**过期系统（idle 桶 + keep-alive 桶，每帧 O(1) 处理）。加载去重通过池化的 `LoadingOperationState` 对象实现。帧驱动编排（配置注入、时间轮推进、销毁态槽位回收、卸载调度、GC 节流、低内存响应）由 `ResourceService.Drive*` partial 随服务生命周期自动接线，编辑器下的播放模式可通过 EditorPrefs 切换。

## 核心特性

- **Lease API（推荐）：** `ResourceLeaseHandle` / `ResourceAssetLease<T>` 提供安全的、作用域化的资源引用。`ResourceAssetLease<T>` 实现 `IDisposable`，支持 `using` 语句自动释放。
- **Binding API（推荐）：** `ResourceOwner` MonoBehaviour + `IResourceBindingService` 提供声明式资源-组件绑定（Sprite、Material、PrefabSource、SubSprite），`OnDestroy` 时自动释放所有绑定。
- **扩展方法：** `Image.SetSprite(location)`、`SpriteRenderer.SetSprite(location)`、`Image.SetSubSprite(location, spriteName)`、`Image/SpriteRenderer/MeshRenderer.SetMaterial(location)`、`MeshRenderer.SetSharedMaterial(location)` —— 全部通过绑定系统自动管理生命周期。
- **异步绑定安全：** 版本校验的绑定请求防止过期异步结果覆盖较新的绑定。
- 四种播放模式：`EditorSimulateMode`（编辑器模拟）、`OfflinePlayMode`（单机）、`HostPlayMode`（联机热更）、`WebPlayMode`（WebGL，支持微信小游戏文件系统）。玩家构建里若资产仍配成 `EditorSimulate`，`ResourceServiceSettings.PlayMode` **只在读取结果上**归一为 `OfflinePlay`（并在首次读取打一条 Error），不回写那份共享设置实例——配置原值留在资产里，Inspector 与工具都还能看见它错在哪。
- **时间轮过期：** 空闲资源（引用计数 = 0）在 `IdleAssetExpireTime` 秒后被释放；`IdleAssetCapacity` 超容时按每帧限量淘汰最长空闲者。Keep-alive 租约可临时延长生命周期。`ProcessResourceMaintenance` 每帧以 O(1) 复杂度处理两座轮盘，并轮转回收销毁态所有者/绑定槽位。
- **加载去重：** 同地址并发加载共享同一个 `LoadingOperationState`（池化 `MemoryObject`），支持等待者计数与取消。
- 资源加密：`YooAssetEncryptorHandler` 的 `FileOffsetEncryptorHandler`（32 字节偏移）与 `FileStreamEncryptorHandler`（XOR 流加密），附带 Web 端解密实现
- 热更下载：请求远端清单版本、更新 Manifest、创建下载器、清理缓存文件一应俱全

## 核心类型

命名空间：`Moirai.Atropos.Resource`

### 租约 / 标识类型

| 结构体/接口 | 说明 |
|---------|------|
| `ResourceLeaseHandle` | generation 校验的槽位句柄（`readonly struct`），用于安全引用资源记录。字段：`Index`、`Generation`。静态 `Invalid` 表示无效句柄。`IsValid` 检查有效性。 |
| `ResourceAssetLease<T>` | 类型化租约（`struct`，实现 `IDisposable`），持有资源对象并在 `Dispose` 时自动释放。支持 `using` 语句。字段：`Asset`、`Handle`、`IsValid`。 |
| `ResourceKey` | `readonly struct`，描述资源位置、包名、类型和种类。工厂方法 `ResourceKey.Asset<T>(location, packageName)` 创建类型化键。`HasResolvedIds` 检查内部 ID 解析状态。 |
| `EResourceAssetKind` | 枚举：`Unknown / Asset / Sprite / Material / Prefab / SubAssets` |
| `EResourceAssetState` | 枚举：`Released / Loading / Active / KeepAlive / Idle` |
| `ResourceAssetInfo` | 诊断快照结构体：LoadKeyId、Package、Location、TypeName、Kind、State、DirectRefCount、BindingRefCount、KeepAliveRefCount、RefCountTotal、IdleExpireIn 等 |
| `ResourceBindingInfo` | 绑定诊断快照结构体：Active、BindingIndex、OwnerId、TargetComponentId、Lease、Version、SlotType、HasAppliedAsset 等 |
| `ResourceOwnerInfo` | 所有者诊断快照结构体：Active、OwnerIndex、OwnerId、GameObjectId、Generation、BindingCount |

### 服务接口与组件

| 类/接口 | 说明 |
|---------|------|
| `ResourceService` | 静态外观（`[HandlerHost]`），定义加载、租约、绑定、卸载、包操作全部 API；全部静态方法/属性经 `Handler` 属性转发（fail-fast：未就绪时按需初始化，工厂缺失时抛异常，不静默降级）。配置注入在 `OnInit` 接线，每帧驱动（时间轮推进 / 卸载调度 / GC 节流 / 销毁态回收）在 `Tick` 推进 |
| `YooAssetHandler` | 默认后端，`partial` 按职责拆分：主文件（基础属性、卸载调度、资产信息查询、预制体实例化、宿主接口接线）/ Leases（接缝上 Lease 取还的转发）/ Loading（向 YooAsset 要句柄、挂载与失败文案）/ Cache（设置注入与容量预热）/ Initialization（包与清单初始化）/ Scene（场景句柄）/ Attributes（检视器标注）。**记账已不在此**，见下面三件内核类型 |
| `ResourceRecordStore` | 资源记录内核（`internal sealed partial`，在 `Runtime/Services/Resource/Kernel/`），由后端持有：带 generation 校验的资产槽与租约槽、两条 packed key 索引表、在途加载去重、两座过期时间轮与容量淘汰。它看向后端只有 `IResourceRecordHost`：三个原生句柄算子加三个配置读数 |
| `ResourceKeyCodec` | packed key 的位域编解码与 `assetKind` / `assetType` 互推（纯静态、零状态） |
| `ResourceNameRegistry<TValue>` | 单条名称轴的登记、引用计数与 ID 回收；package / location / type 三轴各持一份 |
| `ResourceBindingService` | 绑定服务实现（`internal sealed`），`partial` 按职责拆分：主文件（所有者注册、释放、槽位快照）/ Bindings（绑定注册与组件应用）/ Async（异步绑定安全的预约与代次判定）/ Maintenance（关停、重置与销毁态回收）/ Slots（分页槽位借还） |
| `ResourceServiceHandler` | 处理器抽象基类，定义后端契约；默认实现 `YooAssetHandler`（另有实验性 `AddressableHandler`） |
| `IResourceBindingService` | 声明式资源-组件绑定服务接口，经 `ResourceService.BindingService` 访问 |
| `ResourceOwner` | MonoBehaviour 组件（`[DisallowMultipleComponent]`），`OnDestroy` 时自动释放所有绑定。提供 `ReleaseBindings()`、`EnsureFor(target, bindingService)`。单个所有者抛出只记账不截断其余绑定，末尾汇总重抛。 |
| `ResourceBindingExtension` | 静态扩展类：`Image/SpriteRenderer.SetSprite`、`Image/SpriteRenderer.SetSubSprite`、`Image/SpriteRenderer/MeshRenderer.SetMaterial`、`MeshRenderer.SetSharedMaterial` |
| `ResourceBindingService` | 绑定相关枚举与接口：`EResourceBindStatus`、`EResourceBindingOptions`、`EResourceBindingSlotType` |
| `EResourceHasAssetResult` | 资源存在性检查结果（三值语义）：`NotExist`（不存在）/ `AssetOnline`（存在但需从远端下载）/ `AssetOnDisk`（存在且已在磁盘） |
| `YooAssetEncryptorHandler` | 加密配置的抽象基类（`[SerializeReference]` 挂在 `YooAssetHandler` 上）：`CreateEncryptor()` 供打包侧、`CreateDecryptor()` 供运行侧；内置 `FileOffset*` 与 `FileStream*` 两套实现，打包侧与运行侧必须选同一套 |
| `FileStreamEncryptor` / `FileOffsetEncryptor` | 打包侧加密服务（实现 YooAsset `IEncryptionServices`） |
| `FileStreamDecryptor` / `FileOffsetDecryptor` 及 Web 变体 | 运行时解密服务（实现 `IDecryptionServices` / `IWebDecryptionServices`） |
| `RemoteService` | 远端资源地址查询服务（internal），拼接主/备 URL |
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
using var lease = ResourceService.LoadLease<Sprite>("Assets/AssetRaw/UI/icon.png");
Sprite icon = lease.Asset;

// 异步租约 + CancellationToken
var cts = new CancellationTokenSource();
using (var lease2 = await ResourceService.LoadLeaseAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token))
{
    Texture2D tex = lease2.Asset;
    // ... 使用 tex
} // lease2.Dispose() 自动调用

// AcquireDirect 手动管理句柄生命周期
ResourceKey key = ResourceKey.Asset<GameObject>("Assets/AssetRaw/Prefabs/Hero.prefab");
ResourceLeaseHandle handle = ResourceService.AcquireDirect(key);
if (ResourceService.TryGetLeaseAsset(handle, out Object asset))
{
    // ... 使用 asset
}
ResourceService.Release(handle);

// 异步 AcquireDirect
ResourceLeaseHandle handle2 = await ResourceService.AcquireDirectAsync(key, cts.Token);
// ... 使用 handle2
ResourceService.Release(handle2);
```

### Binding API（推荐）

通过扩展方法进行声明式绑定 —— 绑定随所属 `ResourceOwner` 一起释放，没有任何手动归还步骤：

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

两点收口值得注意：

- **销毁态兜底：** 场景卸载、退出播放等场合 `OnDestroy` 未必跑得到，此时所有者槽位连同其租约会一直占着。每帧维护入口按配额轮转查验槽位，把"组件已被引擎销毁（fake null）但槽位仍活跃"的所有者与目标已销毁的绑定强制回收。
- **关停与重置分界：** `Shutdown()` 是终态——排空后保持关闭位，之后的注册一律 `ServiceShutdown`（槽位页已整体释放，放行即写空表）；强制回收全部资源走 `Reset()`，排空同一套但完成后放行。两条路径都逐槽隔离异常，一项抛出不截断同轮其余项。

### 预制体实例化

```csharp
// 异步实例化：销毁实例即自动归还预制体源租约
GameObject hero = await ResourceService.LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// 同步实例化
GameObject go = ResourceService.LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);
```

> **说明：** `LoadGameObject` / `LoadGameObjectAsync` 返回的是**实例化副本** —— 它们内部经 `AcquirePrefabSourceLease` 取得预制体源租约，并在实例上挂载 `ResourceOwner` 绑定该租约。`Destroy` 实例即自动归还源租约；调用方不持有预制体源，因此不要销毁源预制体对象本身。需要自行掌握资源生命周期时改用 Lease API：`LoadLease<T>` / `LoadLeaseAsync<T>` 以显式所有权承担引用，不存在手工配对释放的步骤。

## 架构

### 分页槽位数组与 Generation 校验

所有内部状态通过 struct 槽位的分页数组管理：

- `AssetSlot[][]` —— 资源记录（资产、句柄、引用计数、过期队列链接、状态）
- `LeaseSlot[][]` —— 租约记录（资产 ID、种类、状态、generation）
- `BindingSlot[][]` —— 绑定记录（所有者、目标、租约、版本、槽位类型）
- `OwnerSlot[][]` —— 所有者记录（GameObject ID、generation、绑定链）

每页 256 个槽位（8 位页索引）。槽位从空闲链表栈分配，通过单调递增的 `Generation` 计数器校验。`ResourceLeaseHandle` 仅在槽位索引和 generation 同时匹配时有效，防止槽位回收后的 use-after-free。

### 64 位打包 ResourceKey

资源标识打包为单个 `ulong` 键：包 ID（12 位）+ 位置 ID（32 位）+ 类型 ID（12 位）+ 资产种类（4 位）+ 句柄种类（4 位）。字符串与 `Type` 到 ID 的映射由三条名称轴各持一份 `ResourceNameRegistry<TValue>` 完成（计引用、可回收 ID），位域排布与越界判定在 `ResourceKeyCodec`。ID 超出该轴位宽时**抛 `GameException` 而非静默截断**——截断会让两条不同资源编出同一个键。

### 自研零 GC 数据结构

- `ResourceUlongIntMap` —— 开放寻址 `ulong→int` 映射，使用 Murmur3 终结器混合键值，SoA（Structure of Arrays）布局（`_keys[]`、`_values[]`、`_states[]`），70% 负载因子，墓碑标记删除。热路径操作（TryGetValue/Set）零 GC 分配。
- `ResourceIndexMap<TKey,TValue>` —— 泛型开放寻址映射，用于 struct 键/值，绑定服务使用它进行 `OwnerSlotKey → 绑定索引` 查找。

### 时间轮过期

两个环形桶数组（各 256 个桶）驱动每帧 O(1) 过期处理：

- **Idle 桶：** 当资产引用计数归零时，进入 idle 桶，计划在 `IdleAssetExpireTime` 秒后过期。
- **Keep-alive 桶：** 当租约以 `KeepAliveOnRelease` 选项释放时，资产的 keep-alive 引用计数递增，计划在 `IdleAssetExpireTime` 秒后过期。
- **容量上限：** 空闲记录数超过 `IdleAssetCapacity` 时，挑过期刻度最早（即最长空闲）的一条释放，不必等到期；一趟最多释放 8 条，没摘完就把淘汰请求留到下一帧。把上限调小同样只是发出请求，不当场淘汰——那等于把一次 O(n) 突发挂在一次属性赋值上。淘汰排在轮盘走查之后，避免走查途中同步摘除让已捕获的 next 指针失效、整桶被跳过。

`ProcessResourceMaintenance(unscaledTime, expireBudget, destroySweepBudget)` 由外观每帧调用：先回收销毁态槽位（走查内同步摘除会破坏轮盘游标，故它排在预算判定之前），再处理两个队列中已过期的资产，收尾按 `IdleAssetCapacity` 淘汰超容的空闲记录（每趟最多 8 条）。记账实现全在 `ResourceRecordStore`，此入口只是后端接缝上的一层转发。

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
    public void Dispose(); // 调用 ResourceService.Release(Handle)
}
```

### ResourceKey

```csharp
public readonly struct ResourceKey
{
    public readonly string PackageName;
    public readonly string Location;
    public readonly Type AssetType;
    public readonly EResourceAssetKind AssetKind;
    public bool HasResolvedIds { get; }

    public ResourceKey(string location, string packageName = "", Type assetType = null,
        EResourceAssetKind assetKind = EResourceAssetKind.Unknown);

    // 工厂方法：创建类型化资源标识键
    public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object;
}
```

### ResourceService 租约方法

| 方法 | 说明 |
|--------|------|
| `ResourceLeaseHandle AcquireDirect(ResourceKey key)` | 同步获取直接租约。失败返回 `Invalid`。 |
| `UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken)` | 异步获取直接租约。 |
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

    public EResourceBindStatus ReleaseBindings(); // 释放此所有者上的所有绑定

    // 确保目标组件的 GameObject 上存在 ResourceOwner
    public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService);

    public static void WarmupReleaseBuffer(int capacity);
}
```

### IResourceBindingService

| 方法 | 说明 |
|--------|------|
| `EResourceBindStatus RegisterOwner(ResourceOwner owner)` | 注册所有者。 |
| `EResourceBindStatus ReleaseOwner(ResourceOwner owner)` | 释放所有者及其所有绑定。 |
| `EResourceBindStatus ReleaseOwner(int ownerId, uint generation)` | 按 ID + generation 释放。 |
| `void Warmup(int ownerCapacity, int bindingCapacity)` | 预分配绑定数据结构。 |
| `EResourceBindStatus BindSprite(ResourceOwner, Image, ResourceKey, options)` | 绑定精灵到 Image。 |
| `EResourceBindStatus BindSprite(ResourceOwner, SpriteRenderer, ResourceKey, options)` | 绑定精灵到 SpriteRenderer。 |
| `UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner, Image, ResourceKey atlasKey, string spriteName, options, CancellationToken)` | 异步从图集绑定子精灵。 |
| `EResourceBindStatus BindImageMaterial(ResourceOwner, Image, ResourceKey, options)` | 绑定材质到 Image。 |
| `UniTask<EResourceBindStatus> BindImageMaterialAsync(ResourceOwner, Image, ResourceKey, options, CancellationToken)` | 异步绑定材质到 Image。 |
| `EResourceBindStatus BindSharedMaterial(ResourceOwner, Renderer, ResourceKey, options)` | 绑定共享材质到 Renderer。 |
| `UniTask<EResourceBindStatus> BindSharedMaterialAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | 异步绑定共享材质。 |
| `EResourceBindStatus BindMaterialInstance(ResourceOwner, Renderer, ResourceKey, options)` | 绑定材质实例（运行时副本）到 Renderer。 |
| `UniTask<EResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner, Renderer, ResourceKey, options, CancellationToken)` | 异步绑定材质实例。 |
| `int GetOwnerInfos(ResourceOwnerInfo[], int, int)` | 诊断：批量查询所有者状态。 |
| `int GetBindingInfos(ResourceBindingInfo[], int, int)` | 诊断：批量查询绑定状态。 |

### EResourceBindStatus

枚举值：`Success / InvalidKey / MissingOwner / MissingTarget / StaleOwner / Cancelled / LoadFailed / ApplyFailed / ServiceShutdown`

### EResourceBindingOptions

标志枚举：`None / KeepAliveOnRelease / SetNativeSize`

### 扩展方法（ResourceBindingExtension）

| 扩展方法 | 说明 |
|-----------|------|
| `Image.SetSprite(string location, bool setNativeSize = false, CancellationToken, string packageName = "")`
| `SpriteRenderer.SetSprite(string location, CancellationToken, string packageName = "")`
| `Image.SetSubSprite(string location, string spriteName, bool setNativeSize = false, CancellationToken, string packageName = "")`
| `SpriteRenderer.SetSubSprite(string location, string spriteName, CancellationToken, string packageName = "")` | 从图集中设置 SpriteRenderer 子精灵。 |
| `Image.SetMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 Image 材质。 |
| `SpriteRenderer.SetMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 SpriteRenderer 材质。 |
| `MeshRenderer.SetMaterial(string location, bool needInstance = true, bool isAsync = false, string packageName = "")` | 设置 MeshRenderer 材质（实例或共享）。 |
| `MeshRenderer.SetSharedMaterial(string location, bool isAsync = false, string packageName = "")` | 设置 MeshRenderer 共享材质。 |

### 异步绑定安全

> **空目标与已销毁目标的口径：** 扩展层每个入口都先验目标组件与其 `gameObject`，命中即**静默不做**（不抛、不打日志）；绑定服务对空 owner / 空目标回 `EResourceBindStatus.MissingOwner` / `MissingTarget`，由调用方决定是否记录。关停期撞上已销毁对象是常态，这里刻意不用异常表达"没什么可做"。

异步绑定方法（如 `BindSubSpriteAsync`、`BindImageMaterialAsync`、`BindSharedMaterialAsync`、`BindMaterialInstanceAsync`）使用**版本校验的绑定请求**防止过期结果：

1. 异步加载开始前，预留绑定槽位并递增 `Version`。
2. 异步加载完成后，`IsBindingRequestCurrent` 检查所有者 generation、目标组件 ID 和绑定版本是否全部匹配。
3. 如果所有者已销毁、目标已替换、或有更新的绑定请求取代了当前请求，则丢弃过期结果并释放租约。

## 容量与过期属性

在 `ResourceServiceSettings`（Framework 设置资产）或通过 `ResourceService` 配置：

| 属性 | 默认值 | 说明 |
|----------|---------|------|
| `AssetRecordCapacity` | 64 | 资源记录预热容量（AssetSlot 页）。 |
| `AssetLeaseCapacity` | 128 | 租约槽位预热容量（LeaseSlot 页）。 |
| `BindingOwnerCapacity` | 64 | 绑定所有者预热容量（OwnerSlot 页）。 |
| `BindingSlotCapacity` | 128 | 绑定槽位预热容量（BindingSlot 页）。 |
| `IdleAssetExpireTime` | 60s | 无引用资源句柄空闲过期秒数。 |
| `IdleAssetCapacity` | 256 | 空闲资源记录容量上限；超出即淘汰最长空闲者，0 表示不留空闲记录。 |
| `ExpireProcessCountPerFrame` | 16 | 每帧过期处理最大数量。 |
| `ExpireProcessCountWhenUnloading` | 256 | 卸载时过期处理最大数量。 |

### WarmupResourceRecords

```csharp
void WarmupResourceRecords(int assetCapacity, int leaseCapacity);
```

预分配内部数据结构（槽位页、索引映射），避免运行时扩容。设置容量属性时自动调用。

### GetAssetInfos（诊断）

```csharp
int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);
```

批量查询资源记录状态。返回实际写入数量。每个 `ResourceAssetInfo` 包含包名、位置、类型、种类、状态、引用计数（直接/绑定/keep-alive）及过期信息。

## 卸载 API

| 方法 | 说明 |
|--------|------|
| `void UnloadUnusedAssets()` | 释放已过空闲过期时间的无用资源（引用计数=0）。同时清空 AssetInfo 缓存并调用 `package.UnloadUnusedAssetsAsync()`。 |
| `void UnloadUnusedAssets(bool force)` | `force=true`：忽略空闲过期时间，立即处理 keep-alive 队列并释放所有无用记录。 |
| `void ForceUnloadAllAssets()` | 强制卸载所有包上的所有资产（WebGL 不支持 —— 仅打印警告）。 |
| `void ForceUnloadUnusedAssets(bool performGCCollect)` | 触发驱动器的强制卸载路径（可选 GC.Collect）。 |
| `void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)` | 每帧资源维护：时间轮过期处理（idle + keep-alive 桶）+ 空闲容量淘汰 + 销毁态槽位回收。**internal**，由 `ResourceService.Tick()` 内部调用。 |

## 配置与扩展

### 播放模式与加密

编辑器中在 `ResourceServiceSettings` 资产的 Handler（YooAssetHandler）序列化字段上配置，也可用菜单 `YooAsset/Editor PlayMode` 切换（编辑器设置优先于序列化值；真机下 `EditorSimulateMode` 自动降级为 `OfflinePlayMode`）：

- `PlayMode`：四种播放模式，决定 `InitializePackageAsync` 走模拟构建、内置文件系统、缓存文件系统还是 Web 文件系统
- 加密方式由 `YooAssetHandler.EncryptorHandler` 这个 `[SerializeReference]` 配置决定（不配即为无加密），运行时据此创建对应解密服务
- `PackageName`：默认资源包名（默认 `DefaultPackage`），多包项目通过各 API 的 `packageName` 参数指定其它包

以下运行时配置属性已上移至抽象契约，外观与 Handler 均可读写（Handler 序列化字段为默认值来源）：

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `Milliseconds` | 30ms | 异步系统每帧最大时间切片；外观 setter 即时应用 `SetAsyncOperationMaxTimeSlice`，负值抛 `GameException` |
| `AutoUnloadBundleWhenUnused` | false | 引用计数为零时自动卸载资源包（初始化与卸载决策时读取） |
| `DownloadingMaxNum` | 10 | 下载并发数（创建下载器时传入 `ResourceDownloaderOptions`） |
| `FailedTryAgain` | 3 | 下载失败重试次数（创建下载器时传入 `ResourceDownloaderOptions`） |

其余配置：

- `UpdatableWhilePlaying`：边玩边下载
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`：无用资源回收的最小/最大间隔（默认 60s / 300s）
- `UseSystemUnloadUnusedAssets`：是否在系统卸载周期中调用 `ResourceService.UnloadUnusedAssets()`

### 热更流程相关 API

```csharp
// 初始化指定资源包（needInitMainFest: true 时顺带请求并更新清单，单机 OtherPackage 场景）
await ResourceService.InitializePackageAsync("DefaultPackage", needInitManifest: true);

// 布尔薄壳：可写远程地址，不更新清单，收成成败布尔；
// 并发去重/幂等语义与 InitializePackageAsync 一致；
// hostServerURL/fallbackHostServerURL 非空时写入 HostServerURL/FallbackHostServerURL；
// HostPlay/WebPlay 模式下两属性均为空时抛 GameException（fail-fast）。
bool succeed = await ResourceService.TryInitializePackageAsync();
bool succeed2 = await ResourceService.TryInitializePackageAsync("OtherPackage", "https://cdn.example.com/res");

// 联机模式：请求远端版本 -> 更新清单 -> 创建下载器 -> 下载
var op = await ResourceService.RequestPackageVersion();
ResourceService.PackageVersion = op.PackageVersion;
await ResourceService.LoadPackageManifestAsync(ResourceService.PackageVersion);
var downloader = ResourceService.CreateResourceDownloader();   // 之后轮询 downloader

// 下载量查询：定位地址待下载字节数（用于更新 UI 剩余下载量展示；定位/包无效抛 GameException）
long downloadBytes = ResourceService.GetDownloadSize("Assets/AssetRaw/UI/logo.png");
bool needRemote = ResourceService.IsNeedDownloadFromRemote("Assets/AssetRaw/UI/logo.png");

// 远端地址与缓存清理
ResourceService.SetRemoteServicesUrl("https://cdn.example.com/res", "https://backup.example.com/res");
ResourceService.StartClearCache(EResourceClearMode.ClearUnusedBundleFiles);            // 清理未使用的缓存文件
ResourceService.ClearAllBundleFiles();             // 清空沙盒路径
```

### 资源查询与句柄

```csharp
EResourceHasAssetResult result = ResourceService.HasAsset("Assets/AssetRaw/UI/icon.png");
// NotExist：定位无效或清单中不存在；AssetOnline：存在但需远端下载；AssetOnDisk：已可用
bool valid = ResourceService.IsLocationValid("Assets/AssetRaw/UI/icon.png");
ResourceAssetInfoEntry[] infos = ResourceService.GetAssetInfos("Preload");   // 按标签批量获取

// 需要精细控制句柄生命周期时，使用租约 API（不经 ResourceOwner 自动管理）
using var lease = ResourceService.LoadLeaseAsync<GameObject>("path").GetAwaiter().GetResult();
// ... 使用 lease.Asset，using 结束自动释放
```

### 配置自检

设置项按**只报不改**处理：值单看都合法、只是相互关系不成立的那几类，会在 `ResourceService.OnInit` 各打一条 Warning；构建期由 `ResourceSettingsBuildValidator` 拿**同一份判据**再走一遍，默认也只告警——设环境变量 `MOIRAI_RESOURCE_SETTINGS_STRICT=1` 才会把构建拦停（本包被他人消费，因一项配置拦停别人的构建是工单，不是提醒）。

刻意不做运行期夹取：夹取会把配置错误洗成"看起来本来就对"的值，这与 `PlayMode` 读取时"只归一返回值、不回写资产"是同一个取向。

| 设置项 | 触发条件 | 不修会怎样 |
|---|---|---|
| `ExpireProcessCountWhenUnloading` | 小于 `ExpireProcessCountPerFrame` | 帧驱动取二者较大值，卸载档预算永远顶不上去 |
| `ExpireProcessCountPerFrame` | ≤ 0 | 时间轮只在正预算下推进，空闲/保活记录永不过期 |
| `DestroySweepBudget` | ≤ 0 | 销毁态轮转一帧都不跑，被 `OnDestroy` 截断留下的租约永不回收 |
| `IdleAssetExpireTime` | > 255 | 过期刻度按一秒一格落进 256 格轮盘，超过一圈的值会被跳过直到轮盘绕回，释放最多晚一整圈 |
| `MaxUnloadUnusedAssetsInterval` | ≤ 0 | 调度判据是"距上次卸载 ≥ 该值"，非正数使其恒真，卸载变成每帧一次 |
| `MinUnloadUnusedAssetsInterval` | 大于上限 | 预约卸载先被周期触发抢掉，下限形同废弃 |
| `MinGCCollectInterval` | < 0 | 节流失效，每次收集请求都真跑 `GC.Collect` |

## 注意事项

- **Addressables 后端（实验性）：** `AddressableHandler` 与 `YooAssetHandler` 共用同一套记录内核（`ResourceRecordStore`），异步租约 / 绑定 / 预制体实例化 / 图集子精灵 / 场景加载 / 缓存维护与低内存回收均已对齐。未安装 `com.unity.addressables` 时整层由 asmdef 的 `versionDefines`（宏 `ADDRESSABLES_INSTALLED`）连同文件级 `#if` 一起剔除，**不拆独立程序集**——内核类型是 `Moirai.Atropos` 的 `internal`，拆出去只会逼出一行 `InternalsVisibleTo`，换不到任何东西。
- **Addressables 的同步族与下载族抛错：** `LoadLease<T>` / `AcquireDirect` / `AcquireBinding` / `AcquirePrefabSourceLease` / 同步 `LoadGameObject` 一律抛 `GameException`——Addressables 没有同步加载 API，返回 `null` 只会把错误推到后面的空引用上。`RequestPackageVersion` / `LoadPackageManifestAsync` / `CreateResourceDownloader` / `GetDownloadSize` 同理：它的更新流程是 Check→Update 两步式，`DownloadStatus` 也没有 `TotalDownloadCount` 与 `FailedFiles` 的对应项。
- **Addressables 的降级查询：** `IsNeedDownloadFromRemote` 恒为 `false`、`GetPackageVersion` 恒为空串、`GetAssetInfo` 恒为 `default`、按标签的 `GetAssetInfos` 恒为空数组（对应能力只有异步版本，或 `IResourceLocation` 根本不带标签与体积）。`HasAsset` 只要地址可定位就报 `AssetOnDisk`，区分不出"需远端下载"（`AssetOnline` 永不出现）；图集子精灵仅支持 `SpriteAtlas` 形态的地址。非强制档的 `UnloadUnusedAssets()` 在这一后端不做任何事（YooAsset 那一档推进的是 bundle 卸载操作，没有对应物），强制档与低内存回收走的是记录释放。生产环境请使用 `YooAssetHandler`。
- **Lease API：** `ResourceAssetLease<T>` 是 `struct` —— 务必调用 `Dispose`（使用 `using` 语句）。Dispose 后 `IsValid` 返回 `false`，`Asset` 为 `null`。
- **Binding API：** `SetSprite`/`SetMaterial` 扩展方法在目标 GameObject 上不存在 `ResourceOwner` 时自动添加。GameObject 销毁时所有绑定自动释放。
- **预制体实例化：** `LoadGameObject` / `LoadGameObjectAsync` 返回的是实例化副本，预制体源租约挂在实例的 `ResourceOwner` 上；`Destroy` 实例即归还租约，不要销毁源预制体对象本身，也不要把实例当成自己持有的共享资源。
- **异步取消：** `LoadLeaseAsync<T>` 被取消（`cancellationToken` 触发）时返回无效租约（`IsValid` 为 `false`、`Asset` 为 `null`）并释放内部句柄，调用方需判空；`LoadGameObjectAsync` 被取消时同样返回 `null`。
- WebGL 平台不支持 `ForceUnloadAllAssets`，调用只会打印警告。
- 加密方式的打包侧（`FileStreamEncryptor` 等）与运行时解密侧需一致，`BundleStream` 的 XOR 密钥为固定常量（`KEY = 64`），仅作防直读用途。
- `GetAssetInfo` 对默认包结果做了字典缓存，切换清单（热更完成）后如需最新信息请先调用 `UnloadUnusedAssets()`（会清空缓存）。
- 低内存时系统回调 `GameApp.OnLowMemory` 会触发 `ForceUnloadUnusedAssets(true)`，随后执行 `Resources.UnloadUnusedAssets` 与 `GC.Collect`。

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Procedure](Procedure.md) · [Save](Save.md) · [ObjectPool](ObjectPool.md)
