# Save 存档模块

> 可插拔 Handler 的本地存档系统，支持 JSON / 二进制格式与 AES 加密，写入采用临时文件原子替换。

Save 模块（`SaveModule`）将存档的序列化格式与文件读写流程解耦：`SaveModule` 负责路径拼装、目录创建、原子写入与删除清理，具体格式由 `ISaveHandler` 实现（`JsonSaveHandler`、加密版及二进制版）决定，可在 `SaveSettings` 面板中切换。存档统一写入 `Application.persistentDataPath/Data/{folderName}/`，文件名自动追加配置的扩展名（默认 `.sav`）。通过 `GameModule.Save`（`ISaveModule`）访问。

## 核心特性

- 可插拔 Handler：四种内置处理器（JSON / JSON 加密 / 二进制 / 二进制加密），并可注入自定义 `ISaveHandler`
- 原子保存：先写入 `{文件名}.tmp`，成功后删除旧档并将临时文件改名，失败自动清理临时文件，避免写入中断损坏存档
- AES 加密：加密处理器基于 `SaveEncryptor`，使用 AES + `Rfc2898DeriveBytes`（密钥与盐派生），解密失败返回 `default` 而不抛异常
- 目录管理：按 `folderName` 分文件夹存档，支持删除单个存档、整个文件夹或全部存档
- 编辑器友好：`JsonSaveHandler` 在编辑器下输出带缩进的可读 JSON，真机走紧凑字节通路（框架自带 `JsonUtility.ToJsonBytes` / `ToObject<T>`，零 string 中间态）

## 核心类型

命名空间：`Moirai.Atropos.Save`

| 类/接口 | 说明 |
|---------|------|
| `ISaveModule` | 存档模块接口：`Save` / `Load` / `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` / `FileExists` / `DetermineSavePath`；经 `GameModule.Save` 访问 |
| `SaveModule` | 模块实现（`Module, ISaveModule`），`OnInit` 时从 `SaveSettings` 读取 Handler 并注入加密密钥 |
| `ISaveHandler` | 序列化处理器接口：`Task Save(object objectToSave, FileStream saveFile)` 与 `Task<T> Load<T>(FileStream saveFile)` |
| `JsonSaveHandler` | JSON 格式处理器，编辑器下 prettyPrint、真机紧凑字节 |
| `JsonEncryptedSaveHandler` | JSON 序列化 + AES 加密（继承 `EncryptedSaveHandlerBase`） |
| `BinarySaveHandler` | 二进制格式（`BinaryFormatter`），已标记 `[System.Obsolete]`，存在反序列化 RCE 风险，不建议使用 |
| `BinaryEncryptedSaveHandler` | 二进制 + AES 加密，同样已标记 `[System.Obsolete]` |
| `EncryptedSaveHandlerBase` | 加密处理器抽象基类：完成加密/解密流转发与异常兜底，子类只需实现 `SerializeToStream` / `DeserializeFromStream` |
| `SaveEncryptor` | 加密基类：`Key` / `Salt` 虚属性（默认值为占位串，上线前必须替换）与 AES `Encrypt` / `Decrypt` |
| `SaveSettings` | 框架设置（`FrameworkSettings<SaveSettings>`，面板名「存档设置」）：存档类型、加密密钥、文件扩展名，暴露静态 `SaveHandler` 与 `SaveFileExtension` |
| `MessagePackUtility` | MessagePack 序列化工具类（需定义 `MESSAGEPACK_INSTALLED` 宏，命名空间 `Moirai.Atropos`），可配合自定义 Handler 使用 |

## 快速上手

```csharp
using System.Threading.Tasks;
using Moirai.Atropos;
using UnityEngine;

[System.Serializable]
public class PlayerData
{
    public int Level;
    public int Coin;
}

// 保存：写入 persistentDataPath/Data/Save/player_data.sav
await GameModule.Save.Save(new PlayerData { Level = 10, Coin = 999 }, "player_data");

// 加载：文件不存在或解密失败时返回 default
if (GameModule.Save.FileExists("player_data"))
{
    PlayerData data = await GameModule.Save.Load<PlayerData>("player_data");
}

// 分文件夹存档（persistentDataPath/Data/Settings/）
await GameModule.Save.Save(settingsObject, "audio", "Settings");

// 删除
GameModule.Save.DeleteSave("player_data");            // 删除单个存档
GameModule.Save.DeleteSaveFolder("Settings");         // 删除整个存档文件夹
GameModule.Save.DeleteAllSaveFiles();                 // 删除 Data/ 下所有存档

// 查询实际存档路径
string path = GameModule.Save.DetermineSavePath();    // persistentDataPath/Data/Save/
```

## 配置与扩展

### 存档设置

`SaveSettings`（菜单中的框架设置「存档设置」）提供三项配置：

- 存档类型：`Binary` / `BinaryEncrypted` / `Json` / `JsonEncrypted`（选择加密类型时显示密钥字段）
- 加密密钥：默认值为 `CHANGE_ME_BEFORE_SHIPPING` 占位串，上线前必须改为项目专属密钥
- 存档文件扩展名：默认 `.sav`，保存时会取 `fileName` 去扩展名部分再拼接（如 `player_data`、`player_data.json` 最终均为 `player_data.sav`）

### 自定义 Handler

实现 `ISaveHandler` 并在模块初始化前（如启动流程最开始）注入即可：

```csharp
using System.IO;
using System.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;

public class MessagePackSaveHandler : ISaveHandler
{
    public Task Save(object objectToSave, FileStream saveFile)
    {
        byte[] bytes = MessagePackUtility.Serialize(objectToSave);
        saveFile.Write(bytes, 0, bytes.Length);
        saveFile.Close();
        return Task.CompletedTask;
    }

    public Task<T> Load<T>(FileStream saveFile)
    {
        using var ms = new MemoryStream();
        saveFile.CopyTo(ms);
        saveFile.Close();
        return Task.FromResult(MessagePackUtility.Deserialize<T>(ms.ToArray()));
    }
}

// 注入（需在 SaveModule.OnInit 之前，否则沿用面板配置）
SaveSettings.SaveHandler = new MessagePackSaveHandler();
```

## 注意事项

- `Save` 的参数顺序是「先对象、后文件名」：`Save(object saveObject, string fileName, string folderName = "Save")`。
- Handler 在 `SaveModule.OnInit` 时读取并缓存，运行期修改 `SaveSettings.SaveHandler` 不会影响已初始化的模块。
- 加密处理器的 `Key` 来自 `SaveSettings.EncryptionKey`，`Salt` 仍为 `SaveEncryptor` 默认值；修改密钥会导致旧档无法解密（`Load` 返回 `default`）。
- 二进制处理器基于 `BinaryFormatter`（已过时且有反序列化攻击风险，.NET 9+ 已移除），新项目请使用 `JsonSaveHandler` 或 `JsonEncryptedSaveHandler`。
- JSON 处理器依赖框架自带 `JsonUtility`（`Moirai.Atropos` 的 `Core/Utility/Json`），而非 `UnityEngine.JsonUtility`，可直接序列化 `byte[]`、字典等类型。
- 原子替换依赖 `File.Delete` + `File.Move`，在个别平台（如 WebGL 虚拟文件系统）上行为受底层实现限制，建议真机验证。

---
[« 返回主 README](../../README.md) · [Resource](Resource.md) · [Procedure](Procedure.md)
