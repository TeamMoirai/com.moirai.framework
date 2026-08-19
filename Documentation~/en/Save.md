# Save Module

> A pluggable Handler-based local save system supporting JSON/binary formats and AES encryption, with atomic file replacement on write.

The Save module (`SaveModule`) decouples the serialization format from the file read/write process: `SaveModule` is responsible for path assembly, directory creation, atomic writes, and deletion/cleanup, while the specific format is determined by `ISaveHandler` implementations (`JsonSaveHandler`, encrypted version, and binary version), which can be switched in the `SaveSettings` panel. Saves are written to `Application.persistentDataPath/Data/{folderName}/`, with filenames automatically appended with the configured extension (default `.sav`). Access via `GameModule.Save` (`ISaveModule`).

## Core Features

- Pluggable Handlers: Four built-in handlers (JSON / JSON Encrypted / Binary / Binary Encrypted), with support for custom `ISaveHandler` injection
- Atomic save: First writes to `{filename}.tmp`, then on success deletes the old save and renames the temp file; on failure, automatically cleans up the temp file, preventing corruption from interrupted writes
- AES encryption: Encrypted handlers are based on `SaveEncryptor`, using AES + `Rfc2898DeriveBytes` (key and salt derivation); on decryption failure, returns `default` without throwing an exception
- Directory management: Saves are organized into folders by `folderName`, supporting deletion of individual saves, entire folders, or all saves
- Editor-friendly: `JsonSaveHandler` outputs indented, readable JSON in the editor; on device, it uses a compact byte path (the framework's built-in `JsonUtility.ToJsonBytes` / `ToObject<T>`, with zero string intermediate state)

## Core Types

Namespace: `Moirai.Atropos.Save`

| Class/Interface | Description |
|---------|------|
| `ISaveModule` | Save module interface: `Save` / `Load` / `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` / `FileExists` / `DetermineSavePath`; accessed via `GameModule.Save` |
| `SaveModule` | Module implementation (`Module, ISaveModule`), reads the Handler from `SaveSettings` on `OnInit` and injects the encryption key |
| `ISaveHandler` | Serialization handler interface: `Task Save(object objectToSave, FileStream saveFile)` and `Task<T> Load<T>(FileStream saveFile)` |
| `JsonSaveHandler` | JSON format handler, prettyPrint in editor, compact bytes on device |
| `JsonEncryptedSaveHandler` | JSON serialization + AES encryption (inherits `EncryptedSaveHandlerBase`) |
| `BinarySaveHandler` | Binary format (`BinaryFormatter`), marked `[System.Obsolete]`, carries deserialization RCE risk, not recommended |
| `BinaryEncryptedSaveHandler` | Binary + AES encryption, also marked `[System.Obsolete]` |
| `EncryptedSaveHandlerBase` | Abstract base class for encrypted handlers: handles encryption/decryption stream forwarding and exception fallback; subclasses only need to implement `SerializeToStream` / `DeserializeFromStream` |
| `SaveEncryptor` | Encryption base class: `Key` / `Salt` virtual properties (default values are placeholder strings, must be replaced before shipping) and AES `Encrypt` / `Decrypt` |
| `SaveSettings` | Framework settings (`FrameworkSettings<SaveSettings>`, panel name "Save Settings"): save type, encryption key, file extension; exposes static `SaveHandler` and `SaveFileExtension` |
| `MessagePackUtility` | MessagePack serialization utility class (requires defining `MESSAGEPACK_INSTALLED` macro, namespace `Moirai.Atropos`), can be used with custom Handlers |

## Quick Start

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

// Save: writes to persistentDataPath/Data/Save/player_data.sav
await GameModule.Save.Save(new PlayerData { Level = 10, Coin = 999 }, "player_data");

// Load: returns default when file does not exist or decryption fails
if (GameModule.Save.FileExists("player_data"))
{
    PlayerData data = await GameModule.Save.Load<PlayerData>("player_data");
}

// Save to a subfolder (persistentDataPath/Data/Settings/)
await GameModule.Save.Save(settingsObject, "audio", "Settings");

// Deletion
GameModule.Save.DeleteSave("player_data");            // delete a single save
GameModule.Save.DeleteSaveFolder("Settings");         // delete an entire save folder
GameModule.Save.DeleteAllSaveFiles();                 // delete all saves under Data/

// Query the actual save path
string path = GameModule.Save.DetermineSavePath();    // persistentDataPath/Data/Save/
```

## Configuration and Extensions

### Save Settings

`SaveSettings` (framework settings menu "Save Settings") provides three configuration items:

- Save type: `Binary` / `BinaryEncrypted` / `Json` / `JsonEncrypted` (selecting an encrypted type reveals the key field)
- Encryption key: Default value is the placeholder string `CHANGE_ME_BEFORE_SHIPPING`; must be changed to a project-specific key before shipping
- Save file extension: Default `.sav`; at save time, the extension part of `fileName` is stripped and then re-appended (e.g., `player_data` and `player_data.json` both result in `player_data.sav`)

### Custom Handler

Implement `ISaveHandler` and inject it before module initialization (e.g., at the very start of the launch process):

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

// Inject (must be done before SaveModule.OnInit, otherwise the panel configuration is used)
SaveSettings.SaveHandler = new MessagePackSaveHandler();
```

## Notes

- The `Save` parameter order is "object first, filename second": `Save(object saveObject, string fileName, string folderName = "Save")`.
- The Handler is read and cached during `SaveModule.OnInit`; modifying `SaveSettings.SaveHandler` at runtime does not affect an already initialized module.
- The `Key` for encrypted handlers comes from `SaveSettings.EncryptionKey`; the `Salt` still uses the `SaveEncryptor` default. Changing the key will make old saves undecryptable (`Load` returns `default`).
- The binary handler is based on `BinaryFormatter` (deprecated and carries deserialization attack risk, removed in .NET 9+). New projects should use `JsonSaveHandler` or `JsonEncryptedSaveHandler`.
- The JSON handler relies on the framework's built-in `JsonUtility` (`Moirai.Atropos`'s `Core/Utility/Json`), not `UnityEngine.JsonUtility`, and can directly serialize `byte[]`, dictionaries, and other types.
- Atomic replacement depends on `File.Delete` + `File.Move`; on certain platforms (e.g., WebGL virtual file system), behavior is limited by the underlying implementation. It is recommended to verify on the target device.

---
[« Back to Main README](../../README_EN.md) · [Resource](Resource.md) · [Procedure](Procedure.md)