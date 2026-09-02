# Save Service

> A pluggable Handler-based local save system supporting JSON/binary formats and AES encryption, with atomic file replacement on write.

The Save service (`SaveService`) decouples the serialization format from the file read/write process: the `SaveService` static facade exposes the public API, while the specific format is determined by `SaveServiceHandler` subclasses (`JsonSaveHandler`, encrypted version, and binary version), which can be switched in the `SaveServiceSettings` panel. Saves are written to `Application.persistentDataPath/Data/{folderName}/`, with filenames automatically appended with the configured extension (default `.sav`). Access via the `SaveService` static facade.

## Core Features

- Pluggable Handlers: Four built-in handlers (JSON / JSON Encrypted / Binary / Binary Encrypted), with support for custom `SaveServiceHandler` subclass injection
- Atomic save: First writes to `{filename}.tmp`, then on success deletes the old save and renames the temp file; on failure, automatically cleans up the temp file, preventing corruption from interrupted writes
- AES encryption: Encrypted handlers are based on `SaveEncryptor`, using AES + `Rfc2898DeriveBytes` (key and salt derivation); on decryption failure, returns `default` without throwing an exception
- Directory management: Saves are organized into folders by `folderName`, supporting deletion of individual saves, entire folders, or all saves
- Editor-friendly: `JsonSaveHandler` outputs indented, readable JSON in the editor; on device, it uses a compact byte path (the framework's built-in `JsonUtility.ToJsonBytes` / `ToObject<T>`, with zero string intermediate state)

## Core Types

Namespace: `Moirai.Atropos.Save`

| Class/Interface | Description |
|---------|------|
| `SaveService` | Save service static facade (`[HandlerHost]`): `Save` / `Load` / `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` / `FileExists` / `DetermineSavePath`; all static APIs forward through the `Handler` property (fail-fast: lazily initialized when not ready, throws if the default factory is missing, never silently degrades) |
| `SaveServiceHandler` | Serialization handler abstract base class: high-level `Save(object, fileName, folderName)` handles path assembly / atomic writes; subclasses implement the `SerializeAsync(object, FileStream)` and `DeserializeAsync<T>(FileStream)` serialization hooks |
| `JsonSaveHandler` | JSON format handler, prettyPrint in editor, compact bytes on device |
| `JsonEncryptedSaveHandler` | JSON serialization + AES encryption (inherits `EncryptedSaveHandlerBase`) |
| `BinarySaveHandler` | Binary format (`BinaryFormatter`), marked `[System.Obsolete]`, carries deserialization RCE risk, not recommended |
| `BinaryEncryptedSaveHandler` | Binary + AES encryption, also marked `[System.Obsolete]` |
| `EncryptedSaveHandlerBase` | Abstract base class for encrypted handlers: handles encryption/decryption stream forwarding and exception fallback; subclasses only need to implement `SerializeToStream` / `DeserializeFromStream` |
| `SaveEncryptor` | Encryption base class: `Key` / `Salt` virtual properties (default values are placeholder strings, must be replaced before shipping) and AES `Encrypt` / `Decrypt` |
| `SaveServiceSettings` | Framework settings (`FrameworkSettings<SaveServiceSettings>`, panel name "Save Settings"): save type, encryption key, file extension; exposes static `SaveServiceHandler` and `SaveFileExtension` |
| `MessagePackUtility` | MessagePack serialization utility class (requires defining `MESSAGEPACK_INSTALLED` macro, namespace `Moirai.Atropos`), can be used with custom Handlers |

## Quick Start

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

// Save: writes to persistentDataPath/Data/Save/player_data.sav
await SaveService.Save(new PlayerData { Level = 10, Coin = 999 }, "player_data");

// Load: returns default when file does not exist or decryption fails
if (SaveService.FileExists("player_data"))
{
    PlayerData data = await SaveService.Load<PlayerData>("player_data");
}

// Save to a subfolder (persistentDataPath/Data/Settings/)
await SaveService.Save(settingsObject, "audio", "Settings");

// Deletion
SaveService.DeleteSave("player_data");            // delete a single save
SaveService.DeleteSaveFolder("Settings");         // delete an entire save folder
SaveService.DeleteAllSaveFiles();                 // delete all saves under Data/

// Query the actual save path
string path = SaveService.DetermineSavePath();    // persistentDataPath/Data/Save/
```

## Configuration and Extensions

### Save Settings

`SaveServiceSettings` (framework settings menu "Save Settings") provides three configuration items:

- Save type: `Binary` / `BinaryEncrypted` / `Json` / `JsonEncrypted` (selecting an encrypted type reveals the key field)
- Encryption key: Default value is the placeholder string `CHANGE_ME_BEFORE_SHIPPING`; must be changed to a project-specific key before shipping
- Save file extension: Default `.sav`; at save time, the extension part of `fileName` is stripped and then re-appended (e.g., `player_data` and `player_data.json` both result in `player_data.sav`)

### Custom Handler

Inherit `SaveServiceHandler` to implement the serialization hooks and inject it before service initialization (e.g., at the very start of the launch process):

```csharp
using System.IO;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;

public class MessagePackSaveServiceHandler : SaveServiceHandler
{
    protected internal override UniTask SerializeAsync(object saveObject, FileStream saveFile)
    {
        byte[] bytes = MessagePackUtility.Serialize(saveObject);
        saveFile.Write(bytes, 0, bytes.Length);
        saveFile.Close();
        return UniTask.CompletedTask;
    }

    protected internal override UniTask<T> DeserializeAsync<T>(FileStream saveFile)
    {
        using var ms = new MemoryStream();
        saveFile.CopyTo(ms);
        saveFile.Close();
        return UniTask.FromResult(MessagePackUtility.Deserialize<T>(ms.ToArray()));
    }
}

// Inject (must be done before SaveService.OnInit, otherwise the panel configuration is used)
SaveService.Handler = new MessagePackSaveServiceHandler();
```

## Notes

- The `Save` parameter order is "object first, filename second": `Save(object saveObject, string fileName, string folderName = "Save")`.
- The Handler is read and cached during `SaveService.OnInit`; switching the handler via the settings panel takes effect on the next service initialization.
- The `Key` for encrypted handlers comes from `SaveServiceSettings.EncryptionKey`; the `Salt` still uses the `SaveEncryptor` default. Changing the key will make old saves undecryptable (`Load` returns `default`).
- The binary handler is based on `BinaryFormatter` (deprecated and carries deserialization attack risk, removed in .NET 9+). New projects should use `JsonSaveHandler` or `JsonEncryptedSaveHandler`.
- The JSON handler relies on the framework's built-in `JsonUtility` (`Moirai.Atropos`'s `Core/Utility/Json`), not `UnityEngine.JsonUtility`, and can directly serialize `byte[]`, dictionaries, and other types.
- Atomic replacement depends on `File.Delete` + `File.Move`; on certain platforms (e.g., WebGL virtual file system), behavior is limited by the underlying implementation. It is recommended to verify on the target device.

---
[« Back to Main README](../../README_EN.md) · [Resource](Resource.md) · [Procedure](Procedure.md)