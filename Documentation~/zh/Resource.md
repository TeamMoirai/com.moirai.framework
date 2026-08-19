# Resource 资源模块

> 基于 YooAsset 封装的资源管理系统，提供引用计数、多包加载、取消控制与加密解密能力。

Resource 模块（`ResourceModule`）对 [YooAsset](https://github.com/tuyoogame/YooAsset) 做了面向业务的封装：统一同步/异步加载入口、基于对象池的引用计数与自动回收、多资源包（Package）支持，以及 FileOffset / FileStream 两种 Bundle 加密解密方案。通过 `GameModule.Resource`（`IResourceModule`）访问，加载结果缓存在 `AssetObject` 对象池中，重复加载同一资源零开销。编辑器下的播放模式由 `ResourceModuleDriver` 组件驱动并可通过 EditorPrefs 切换。

## 核心特性

- 四种播放模式：`EditorSimulateMode`（编辑器模拟）、`OfflinePlayMode`（单机）、`HostPlayMode`（联机热更）、`WebPlayMode`（WebGL，支持微信小游戏文件系统）
- 引用计数：加载结果注册到 `AssetObject` 多重生对象池，`UnloadAsset` 后计数归零即可被回收
- 取消与超时：`LoadAssetAsync<T>` 支持 `CancellationToken`；同地址并发加载自动等待，编辑器下 60 秒超时告警
- 实例化自动卸载：`LoadGameObject` / `LoadGameObjectAsync` 生成的实例挂载 `AssetsReference`，Destroy 时自动归还引用
- 资源加密：`EncryptionType.FileOffSet`（32 字节偏移）与 `EncryptionType.FileStream`（XOR 流加密），附带 Web 端解密实现
- 热更下载：请求远端清单版本、更新 Manifest、创建下载器、清理缓存文件一应俱全
- UI 扩展：`SetSprite` / `SetSubSprite`（子精灵/图集散图） / `SetMaterial` 扩展方法，自动管理生命周期与引用释放

## 核心类型

命名空间：`Moirai.Atropos.Resource`

| 类/接口 | 说明 |
|---------|------|
| `IResourceModule` | 资源管理器接口，定义加载、卸载、包操作全部 API；经 `GameModule.Resource` 访问 |
| `ResourceModule` | 内部实现（`internal sealed partial class`，按 Partial 拆分为主逻辑 / Pool / AssetObject / Services 四部分） |
| `ResourceModuleDriver` | MonoBehaviour 驱动组件，Inspector 配置播放模式、加密类型、下载参数与资源池参数，并周期执行 `UnloadUnusedAssets` |
| `ResourceExtComponent` | 资源扩展组件（internal），维护散图对象池 `AssetItemObject` 与分帧回收，供 `SetSprite` 系列扩展使用 |
| `AssetsReference` | MonoBehaviour 引用组件，记录实例与源资源的关系，`OnDestroy` 时自动 `UnloadAsset` |
| `AssetsSetHelper` | 静态扩展类：`Image/SpriteRenderer/MeshRenderer.SetMaterial`、`SetSharedMaterial`（同步/异步） |
| `SetSpriteExtensions` | 静态扩展类：`Image/SpriteRenderer.SetSprite`、`SetSubSprite` |
| `SetSpriteObject` / `AssetItemObject` / `LoadAssetObject` / `ISetAssetObject` | 散图加载的对象池条目与内存池协议实现 |
| `SubSpriteReference` | 子精灵引用计数组件，GameObject 销毁时自动释放对应 `SubAssetsHandle` |
| `LoadAssetCallbacks` | 回调式加载的回调函数集，组合成功/失败/进度三个委托 |
| `LoadAssetSuccessCallback` 等委托 | `(string assetName, object asset, float duration, object userData)` 等签名，另有场景加载/卸载回调组 |
| `LoadResourceStatus` | 加载结果状态枚举：`Success / NotExist / NotReady / DependencyError / TypeError / AssetError` |
| `HasAssetResult` | 资源存在性检查结果：`NotExist / AssetOnline / AssetOnDisk / AssetOnFileSystem / BinaryOnDisk / BinaryOnFileSystem / Valid` |
| `EncryptionType` | 加密方式枚举：`None / FileOffSet / FileStream` |
| `FileStreamEncryption` / `FileOffsetEncryption` | 打包侧加密服务（实现 YooAsset `IEncryptionServices`） |
| `FileStreamDecryption` / `FileOffsetDecryption` 及 Web 变体 | 运行时解密服务（实现 `IDecryptionServices` / `IWebDecryptionServices`） |
| `RemoteServices` | 远端资源地址查询服务（internal），拼接主/备 URL |

## 快速上手

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

// 同步加载
Sprite icon = GameModule.Resource.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 异步加载（UniTask，支持 CancellationToken 取消）
var cts = new CancellationTokenSource();
Texture2D tex = await GameModule.Resource.LoadAssetAsync<Texture2D>(
    "Assets/AssetRaw/UI/atlas.png", cts.Token);

// 异步实例化到场景：Destroy 时自动卸载引用，无需手动 UnloadAsset
GameObject hero = await GameModule.Resource.LoadGameObjectAsync(
    "Assets/AssetRaw/Prefabs/Hero.prefab", parent);

// 异步回调式（成功 / 失败 / 进度）
GameModule.Resource.LoadAssetAsync(
    "Assets/AssetRaw/Audio/bgm.mp3", 0,
    new LoadAssetCallbacks(
        (assetName, asset, duration, userData) => { /* 成功 */ },
        (assetName, status, errorMessage, userData) => { /* 失败，status 为 LoadResourceStatus */ },
        (assetName, progress, userData) => { /* 进度 0~1 */ }),
    null);

// 同步实例化
GameObject go = GameModule.Resource.LoadGameObject("Assets/AssetRaw/Prefabs/Item.prefab", parent);

// 卸载手动加载的资源（LoadAsset 系列；引用计数归零后由对象池回收）
GameModule.Resource.UnloadAsset(icon);
```

## 配置与扩展

### 播放模式与加密

编辑器中在场景的 `ResourceModuleDriver` 组件上配置（也可用菜单 `YooAsset/Editor PlayMode` 切换，编辑器设置优先于序列化值；真机下 `EditorSimulateMode` 自动降级为 `OfflinePlayMode`）：

- `PlayMode`：四种播放模式，决定 `InitPackage` 走模拟构建、内置文件系统、缓存文件系统还是 Web 文件系统
- `EncryptionType`：`None / FileOffSet / FileStream`，运行时据此创建对应解密服务
- `PackageName`：默认资源包名（默认 `DefaultPackage`），多包项目通过各 API 的 `packageName` 参数指定其它包
- `Milliseconds`：异步系统每帧最大时间切片（默认 30ms）
- `AutoUnloadBundleWhenUnused`：引用计数为零时自动卸载资源包
- `DownloadingMaxNum` / `FailedTryAgain`：下载并发数（默认 10）与失败重试次数（默认 3）
- `UpdatableWhilePlaying`：边玩边下载
- `AssetAutoReleaseInterval / AssetCapacity / AssetExpireTime / AssetPriority`：资源对象池参数
- `MinUnloadUnusedAssetsInterval / MaxUnloadUnusedAssetsInterval`：无用资源回收的最小/最大间隔（默认 60s / 300s）

### 热更流程相关 API

```csharp
IResourceModule res = GameModule.Resource;

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

### UI / 渲染组件扩展

```csharp
// 为 Image 设置散图（自动缓存、对象被销毁或换图后自动回收）
image.SetSprite("Assets/AssetRaw/UI/icon.png", setNativeSize: true);

// 设置图集内的子精灵（SubAssets，按引用计数共享 SubAssetsHandle）
image.SetSubSprite("Assets/AssetRaw/UI/atlas.png", "btn_ok");

// 设置材质
meshRenderer.SetMaterial("Assets/AssetRaw/Mat/skin.mat", needInstance: true);
```

### 资源查询与句柄

```csharp
HasAssetResult result = GameModule.Resource.HasAsset("Assets/AssetRaw/UI/icon.png");
bool valid = GameModule.Resource.CheckLocationValid("Assets/AssetRaw/UI/icon.png");
AssetInfo[] infos = GameModule.Resource.GetAssetInfos("Preload");   // 按标签批量获取

// 需要精细控制句柄生命周期时（不经过对象池计数）
AssetHandle handle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>("path");
// ... 使用 handle.AssetObject，用完 handle.Dispose()
```

## 注意事项

- `LoadAsset<T>` / `LoadGameObject` 返回的是池化共享对象，不要直接 `Destroy`；需要销毁请用 `UnloadAsset` 归还引用，需要实例请用 `LoadGameObject` 系列。
- `LoadAssetAsync<T>` 被取消（`cancellationToken` 触发）时返回 `null` 并释放内部句柄，调用方需判空。
- WebGL 平台不支持 `ForceUnloadAllAssets`，调用只会打印警告。
- 回调式 `LoadAssetAsync(string, int, LoadAssetCallbacks, object, string)` 为 `async void`，异常经 `LoadAssetFailureCallback`（`LoadResourceStatus.AssetError`）上报。
- 加密方式的打包侧（`FileStreamEncryption` 等）与运行时解密侧需一致，`BundleStream` 的 XOR 密钥为固定常量（`KEY = 64`），仅作防直读用途。
- `GetAssetInfo` 对默认包结果做了字典缓存，切换清单（热更完成）后如需最新信息请先调用 `UnloadUnusedAssets()`（会清空缓存）。
- 低内存时系统回调 `GameModule.OnLowMemory` 会触发 `ForceUnloadUnusedAssets(true)`，随后执行 `Resources.UnloadUnusedAssets` 与 `GC.Collect`。

---
[« 返回主 README](../../README.md) · [Procedure](Procedure.md) · [Save](Save.md)
