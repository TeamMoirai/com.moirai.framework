# Save 存档服务

> 可插拔 Handler 的本地存档系统：版本化文件头 + 原子写入 + 工作线程 IO，支持 JSON 格式与 AES-CBC + HMAC-SHA256 加密。

Save 服务（`SaveService`）将存档的序列化格式与文件管线解耦：`SaveService` 静态外观负责对外 API，具体格式由 `SaveServiceHandler` 子类（`JsonSaveHandler`、`JsonEncryptedSaveHandler`）决定，可在 `SaveServiceSettings` 面板中切换。存档统一写入 `Application.persistentDataPath/Data/{folderName}/`，文件名自动追加配置的扩展名（默认 `.sav`）。文件 IO 与序列化在工作线程执行，不阻塞主线程。

## 核心特性

- 可插拔 Handler：内置 JSON / JSON 加密两种处理器，并可注入自定义 `SaveServiceHandler` 子类
- 版本化文件头：所有存档带固定 28 字节头（魔数 `MRSA` + 格式版本 + 保存时间 + 载荷长度 + 载荷 CRC32），天然支持格式演进与损坏识别
- 原子保存：先写入唯一后缀临时文件（`{文件名}.sav.tmp-{guid}`）并强制落盘（`Flush(true)`），再经 `File.Replace` 原子替换（平台不支持时回退删除+改名）；成功后无临时文件残留，服务启动时自动清扫上次中断遗留的孤儿临时文件
- 强加密：加密处理器为 AES-256-CBC + **随机 IV** + HMAC-SHA256（encrypt-then-MAC，防篡改）+ PBKDF2-SHA256 密钥派生（迭代次数可配置，默认 100000）
- 工作线程 IO：`Save`/`Load` 的文件读写与序列化在 `UniTask.RunOnThreadPool` 执行，`CancellationToken` 协作式取消贯穿全程
- 错误判别：`TryLoad` 返回 `SaveResult<T>`，区分无档/格式非法/版本不支持/损坏/解密失败/完整性失败/反序列化失败
- 目录管理：按 `folderName` 分文件夹存档，支持删除单个存档、整个文件夹或全部存档（删除带退避重试，应对云同步/杀软短时锁文件）
- 槽位枚举：`GetSaveFiles` 返回存档元数据（文件名/大小/最后写入时间，最近优先）
- 编辑器友好：`JsonSaveHandler` 在编辑器下输出带缩进的可读 JSON，真机走紧凑字节通路（框架自带 `JsonUtility.ToJsonBytes` / `ToObject<T>`，零 string 中间态）

## 核心类型

命名空间：`Moirai.Atropos.Save`

| 类/接口 | 说明 |
|---------|------|
| `SaveService` | 静态外观（`[HandlerHost]`）：同步 `Save` / `Load` / `TryLoad`，异步 `SaveAsync` / `LoadAsync` / `TryLoadAsync`，以及 `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` / `FileExists` / `GetSaveFiles` / `DetermineSavePath`；全部静态 API，经 `Handler` 属性转发（未就绪时静默降级为安全默认值） |
| `SaveServiceHandler` | 存档处理器抽象基类：完整文件管线（路径解析与校验、版本化文件头、临时文件+落盘+原子替换、删除重试、孤儿清扫、槽位枚举）；子类实现 `Serialize(object)` 与 `Deserialize<T>(byte[])` 序列化钩子（纯 .NET，工作线程调用） |
| `JsonSaveHandler` | JSON 格式处理器：编辑器下 prettyPrint、真机紧凑字节 |
| `JsonEncryptedSaveHandler` | JSON 序列化 + AES 加密（继承 `EncryptedSaveHandlerBase`） |
| `EncryptedSaveHandlerBase` | 加密处理器抽象基类：完成加密/解密流转发与错误分型；子类只需实现明文侧 `SerializeToStream` / `DeserializeFromStream<T>` |
| `SaveEncryptor` | 加密器：AES-256-CBC + 随机 IV + HMAC-SHA256 + PBKDF2-SHA256；`Key`/`Salt`/`Iterations` 可配置（默认值为占位串，上线前必须替换） |
| `SaveError` | 存档操作错误码枚举（None/FileNotFound/InvalidFormat/UnsupportedVersion/Corrupted/DecryptionFailed/IntegrityCheckFailed/SerializationFailed/IoFailed 等） |
| `SaveResult<T>` | `TryLoad` 返回值：区分成功与各错误类别 |
| `SaveFileInfo` | 存档槽位元数据：文件名（不含扩展名）、大小、最后写入时间（UTC） |
| `SaveServiceSettings` | 框架设置（面板名「存档设置」）：存档类型、加密密钥、PBKDF2 迭代次数、文件扩展名 |
| `MessagePackUtility` | MessagePack 序列化工具类（需定义 `MESSAGEPACK_INSTALLED` 宏，命名空间 `Moirai.Atropos`），可配合自定义 Handler 使用 |

## 快速上手

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

[System.Serializable]
public class PlayerData
{
    public int Level;
    public int Coin;
}

// 异步保存：写入 persistentDataPath/Data/Save/player_data.sav（IO 在工作线程）
await SaveService.SaveAsync(new PlayerData { Level = 10, Coin = 999 }, "player_data");

// 异步加载：文件不存在或加载失败时返回 default（失败已记录错误日志）
if (SaveService.FileExists("player_data"))
{
    PlayerData data = await SaveService.LoadAsync<PlayerData>("player_data");
}

// 需要错误判别时使用 TryLoadAsync（区分无档/损坏/解密失败等）
SaveResult<PlayerData> result = await SaveService.TryLoadAsync<PlayerData>("player_data");
if (result.IsSuccess)
{
    Debug.Log($"Level: {result.Data.Level}");
}
else if (result.Error == SaveError.Corrupted)
{
    // 存档损坏——进入恢复/重建流程
}

// 同步 API（裸名，阻塞调用线程，仅限主线程）：退出前落盘、启动期设置加载等必须同步完成的场景
SaveService.Save(new PlayerData { Level = 11, Coin = 1000 }, "player_data");
PlayerData synced = SaveService.Load<PlayerData>("player_data");
SaveResult<PlayerData> syncResult = SaveService.TryLoad<PlayerData>("player_data");

// 枚举存档槽位（最近优先）
foreach (SaveFileInfo info in SaveService.GetSaveFiles())
{
    Debug.Log($"{info.FileName} {info.SizeBytes}B {info.LastWriteTimeUtc}");
}

// 分文件夹存档（persistentDataPath/Data/Settings/）
await SaveService.SaveAsync(settingsObject, "audio", "Settings");

// 删除
SaveService.DeleteSave("player_data");            // 删除单个存档
SaveService.DeleteSaveFolder("Settings");         // 删除整个存档文件夹
SaveService.DeleteAllSaveFiles();                 // 删除 Data/ 下所有存档

// 查询实际存档路径
string path = SaveService.DetermineSavePath();    // persistentDataPath\Data\Save\（分隔符随平台）
```

## 存档文件格式

```
[4B  魔数 "MRSA"]
[4B  格式版本（小端，当前为 1）]
[8B  保存时间 UTC ticks（小端）]
[4B  载荷长度（小端）]
[4B  载荷 CRC32（小端）]
[载荷]
```

- 文件头始终为明文：元数据（保存时间等）无需解密即可读，魔数/版本/长度自洽性与 CRC32 在反序列化前完成校验
- 未加密处理器载荷 = JSON 字节；加密处理器载荷 = `[16B 随机 IV][AES-CBC 密文][32B HMAC-SHA256(IV‖密文)]`
- 读档流程：魔数 → 版本 → 长度自洽 → CRC32 →（加密档）HMAC 验证 → 解密 → 反序列化，任一环节失败返回对应 `SaveError`

## 配置与扩展

### 存档设置

`SaveServiceSettings`（框架设置菜单「存档设置」）提供四项配置：

- 存档类型：`Json` / `JsonEncrypted`（选择加密类型时显示密钥与迭代次数字段；BinaryFormatter 系处理器因反序列化 RCE 风险已移除）
- 加密密钥：默认值为 `CHANGE_ME_BEFORE_SHIPPING` 占位串，上线前必须改为项目专属密钥
- PBKDF2 迭代次数：默认 100000（每次存/读档的密钥派生耗时与之线性相关，可按目标平台预算调整）
- 存档文件扩展名：默认 `.sav`，保存时会取 `fileName` 去扩展名部分再拼接（如 `player_data`、`player_data.json` 最终均为 `player_data.sav`）

### 自定义 Handler

继承 `SaveServiceHandler` 实现序列化钩子（输入输出均为字节载荷，在工作线程调用，必须为纯 .NET 逻辑、禁止触达 Unity 主线程 API），并在服务初始化前注入：

```csharp
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;

public class MessagePackSaveServiceHandler : SaveServiceHandler
{
    protected internal override byte[] Serialize(object saveObject)
    {
        return MessagePackUtility.Serialize(saveObject);
    }

    protected internal override T Deserialize<T>(byte[] payload)
    {
        return MessagePackUtility.Deserialize<T>(payload);
    }
}

// 注入（需在 SaveService.OnInit 之前，否则沿用面板配置）
SaveService.Handler = new MessagePackSaveServiceHandler();
```

## 注意事项

- 读写对命名遵循「同步裸名 / 异步 Async 后缀」（对齐 `ResourceService` 惯例）：异步为 `SaveAsync`/`LoadAsync`/`TryLoadAsync`（`SaveAsync<T>(T saveObject, string fileName, string folderName = "Save", CancellationToken cancellationToken = default)`），同步为 `Save`/`Load`/`TryLoad`。
- **同步 API（`Save`/`Load`/`TryLoad`）在调用线程阻塞执行完整管线**：仅限主线程调用，适用于退出前落盘、启动期设置加载等必须同步完成的场景；大数据量或常规路径请用异步 API（工作线程 IO，不阻塞）。
- **旧格式存档已作废**：新版本写入带版本化文件头的格式，无文件头的旧档读取时返回 `SaveError.InvalidFormat`（用户裁定，发布前无历史档负担）。
- **损坏兜底统一**：`Load` 在缺档时返回 `default`（既有契约）；损坏/解密失败/反序列化失败现在也记录错误日志后返回 `default`（旧版明文 JSON 损坏会抛异常），需要精确判别时使用 `TryLoad`。
- 写入失败（序列化异常、IO 异常）抛出 `GameException`（含路径上下文）；`CancellationToken` 为协作式取消（序列化前后与替换前检查，无法中断进行中的单次磁盘写入）。
- 序列化钩子在工作线程执行：自定义 Handler 禁止调用 Unity 主线程 API（`Application.persistentDataPath`、`PlayerPrefs` 等）；框架 `JsonUtility` 线程安全（ThreadStatic 缓冲）。
- 加密处理器的 `Key`/`Iterations` 来自 `SaveServiceSettings`，`Salt` 为 `SaveEncryptor` 默认值；修改任一项会导致旧档无法解密（`TryLoad` 返回 `IntegrityCheckFailed`/`DecryptionFailed`）。
- JSON 处理器依赖框架自带 `JsonUtility`（`Moirai.Atropos` 的 `Core/Utilities/Json`），而非 `UnityEngine.JsonUtility`，可直接序列化 `byte[]`、字典等类型。
- 路径拼装改用 `Path.Combine`：`DetermineSavePath` 返回的分隔符随平台（Windows 为 `\`，旧版恒为 `/`）；不要对路径字符串做解析依赖。
- 原子替换优先 `File.Replace`（NTFS 元数据级原子）；个别平台（如 WebGL 虚拟文件系统）不支持时自动回退删除+改名，建议真机验证。

---
[« 返回主 README](../../README.md) · [Resource](Resource.md) · [Procedure](Procedure.md)
