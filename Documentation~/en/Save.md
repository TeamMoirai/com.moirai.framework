# Save Service

> Pluggable Handler-based local save system: versioned file headers + atomic writes + worker-thread IO, with JSON format and AES-CBC + HMAC-SHA256 encryption.

The Save service (`SaveService`) decouples the serialization format from the file pipeline: the `SaveService` static facade exposes the public API, while the specific format is determined by `SaveServiceHandler` subclasses (`JsonSaveHandler`, `JsonEncryptedSaveHandler`), which can be switched in the `SaveServiceSettings` panel. Saves are written to `Application.persistentDataPath/Data/{folderName}/`, with filenames automatically appended with the configured extension (default `.sav`). File IO and serialization run on a worker thread and never block the main thread.

## Core Features

- Pluggable Handlers: Built-in JSON / JSON-encrypted handlers, with support for custom `SaveServiceHandler` subclass injection
- Versioned file headers: Every save carries a fixed 28-byte header (magic `MRSA` + format version + save time + payload length + payload CRC32), enabling format evolution and corruption detection
- Atomic save: First writes to a uniquely-suffixed temp file (`{filename}.sav.tmp-{guid}`) and forces it to disk (`Flush(true)`), then atomically replaces via `File.Replace` (falls back to delete+rename when unsupported); no temp file residue on success, and orphan temp files left by interrupted writes are swept automatically at service startup
- Strong encryption: Encrypted handlers use AES-256-CBC + **random IV** + HMAC-SHA256 (encrypt-then-MAC, tamper-proof) + PBKDF2-SHA256 key derivation (configurable iterations, default 100000)
- Worker-thread IO: File read/write and serialization for `Save`/`Load` run via `UniTask.RunOnThreadPool`, with cooperative `CancellationToken` throughout
- Error discrimination: `TryLoad` returns `SaveResult<T>`, distinguishing missing file / invalid format / unsupported version / corrupted / decryption failure / integrity failure / deserialization failure
- Directory management: Saves are organized into folders by `folderName`, supporting deletion of individual saves, entire folders, or all saves (deletions retry with backoff against cloud-sync/antivirus file locks)
- Slot enumeration: `GetSaveFiles` returns save metadata (name/size/last write time, newest first)
- Editor-friendly: `JsonSaveHandler` outputs indented, readable JSON in the editor; on device, it uses a compact byte path (the framework's built-in `JsonUtility.ToJsonBytes` / `ToObject<T>`, with zero string intermediate state)

## Core Types

Namespace: `Moirai.Atropos.Save`

| Class/Interface | Description |
|---------|------|
| `SaveService` | Save service static facade (`[HandlerHost]`): sync `Save` / `Load` / `TryLoad`, async `SaveAsync` / `LoadAsync` / `TryLoadAsync`, plus `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` / `FileExists` / `GetSaveFiles` / `DetermineSavePath`; all static APIs forward through the `Handler` property (silently degrade to safe defaults when not ready) |
| `SaveServiceHandler` | Save handler abstract base class: full file pipeline (path resolution & validation, versioned header, temp file + disk flush + atomic replace, deletion retry, orphan sweep, slot enumeration); subclasses implement the `Serialize(object)` and `Deserialize<T>(byte[])` serialization hooks (pure .NET, invoked on a worker thread) |
| `JsonSaveHandler` | JSON format handler, prettyPrint in editor, compact bytes on device |
| `JsonEncryptedSaveHandler` | JSON serialization + AES encryption (inherits `EncryptedSaveHandlerBase`) |
| `EncryptedSaveHandlerBase` | Abstract base class for encrypted handlers: encryption/decryption stream forwarding and error classification; subclasses only need to implement the plaintext-side `SerializeToStream` / `DeserializeFromStream<T>` |
| `SaveEncryptor` | Encryptor: AES-256-CBC + random IV + HMAC-SHA256 + PBKDF2-SHA256; `Key`/`Salt`/`Iterations` configurable (defaults are placeholder strings, must be replaced before shipping) |
| `SaveError` | Save operation error code enum (None/FileNotFound/InvalidFormat/UnsupportedVersion/Corrupted/DecryptionFailed/IntegrityCheckFailed/SerializationFailed/IoFailed, etc.) |
| `SaveResult<T>` | `TryLoad` return value: distinguishes success from each error category |
| `SaveFileInfo` | Save slot metadata: file name (without extension), size, last write time (UTC) |
| `SaveServiceSettings` | Framework settings (panel "Save Settings"): save type, encryption key, PBKDF2 iterations, file extension |
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

// Async save: writes to persistentDataPath/Data/Save/player_data.sav (IO on a worker thread)
await SaveService.SaveAsync(new PlayerData { Level = 10, Coin = 999 }, "player_data");

// Async load: returns default when the file does not exist or loading fails (failures are logged)
if (SaveService.FileExists("player_data"))
{
    PlayerData data = await SaveService.LoadAsync<PlayerData>("player_data");
}

// Use TryLoadAsync when error discrimination is needed (missing/corrupted/decryption failure, etc.)
SaveResult<PlayerData> result = await SaveService.TryLoadAsync<PlayerData>("player_data");
if (result.IsSuccess)
{
    Debug.Log($"Level: {result.Data.Level}");
}
else if (result.Error == SaveError.Corrupted)
{
    // Save corrupted — enter recovery/rebuild flow
}

// Sync API (bare names; blocks the calling thread, main thread only): quit-time flushes, boot-time settings loads
SaveService.Save(new PlayerData { Level = 11, Coin = 1000 }, "player_data");
PlayerData synced = SaveService.Load<PlayerData>("player_data");
SaveResult<PlayerData> syncResult = SaveService.TryLoad<PlayerData>("player_data");

// Enumerate save slots (newest first)
foreach (SaveFileInfo info in SaveService.GetSaveFiles())
{
    Debug.Log($"{info.FileName} {info.SizeBytes}B {info.LastWriteTimeUtc}");
}

// Save to a subfolder (persistentDataPath/Data/Settings/)
await SaveService.SaveAsync(settingsObject, "audio", "Settings");

// Deletion
SaveService.DeleteSave("player_data");            // delete a single save
SaveService.DeleteSaveFolder("Settings");         // delete an entire save folder
SaveService.DeleteAllSaveFiles();                 // delete all saves under Data/

// Query the actual save path
string path = SaveService.DetermineSavePath();    // persistentDataPath\Data\Save\ (separator is platform-specific)
```

## Save File Format

```
[4B  magic "MRSA"]
[4B  format version (little-endian, currently 1)]
[8B  save time UTC ticks (little-endian)]
[4B  payload length (little-endian)]
[4B  payload CRC32 (little-endian)]
[payload]
```

- The header is always plaintext: metadata (save time, etc.) can be read without decryption; magic/version/length consistency and CRC32 are verified before deserialization
- Unencrypted handler payload = JSON bytes; encrypted handler payload = `[16B random IV][AES-CBC ciphertext][32B HMAC-SHA256(IV‖ciphertext)]`
- Load flow: magic → version → length consistency → CRC32 → (encrypted) HMAC verification → decrypt → deserialize; any failure returns the corresponding `SaveError`

## Configuration and Extensions

### Save Settings

`SaveServiceSettings` (framework settings menu "Save Settings") provides four configuration items:

- Save type: `Json` / `JsonEncrypted` (selecting the encrypted type reveals the key and iteration fields; BinaryFormatter-based handlers were removed due to deserialization RCE risk)
- Encryption key: Default value is the placeholder string `CHANGE_ME_BEFORE_SHIPPING`; must be changed to a project-specific key before shipping
- PBKDF2 iterations: Default 100000 (per-save/read key derivation time scales linearly; adjust to the target platform budget)
- Save file extension: Default `.sav`; at save time, the extension part of `fileName` is stripped and then re-appended (e.g., `player_data` and `player_data.json` both result in `player_data.sav`)

### Custom Handler

Inherit `SaveServiceHandler` and implement the serialization hooks (input/output are byte payloads; invoked on a worker thread; must be pure .NET logic and never touch Unity main-thread APIs), then inject it before service initialization:

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

// Inject (must be done before SaveService.OnInit, otherwise the panel configuration is used)
SaveService.Handler = new MessagePackSaveServiceHandler();
```

## Notes

- The read/write pair naming follows "bare name = sync, Async suffix = async" (aligned with the `ResourceService` convention): async are `SaveAsync`/`LoadAsync`/`TryLoadAsync` (`SaveAsync<T>(T saveObject, string fileName, string folderName = "Save", CancellationToken cancellationToken = default)`), sync are `Save`/`Load`/`TryLoad`.
- **Sync APIs (`Save`/`Load`/`TryLoad`) run the full pipeline on the calling thread and block until done**: main thread only; intended for quit-time flushes, boot-time settings loads, and other must-be-synchronous scenarios. For large data or regular paths prefer the async APIs (worker-thread IO, non-blocking).
- **Legacy saves are discarded**: This version writes the versioned-header format; header-less legacy saves return `SaveError.InvalidFormat` on load (by decision — no historical save burden before release).
- **Unified corruption fallback**: `Load` returns `default` on a missing file (existing contract); corruption/decryption failure/deserialization failure now also log an error and return `default` (the old version threw on plain-JSON corruption). Use `TryLoad` when precise discrimination is needed.
- Write failures (serialization exceptions, IO exceptions) throw `GameException` (with path context); the `CancellationToken` is cooperative (checked before/after serialization and before replacement — an in-flight single disk write cannot be aborted).
- Serialization hooks run on a worker thread: custom Handlers must never call Unity main-thread APIs (`Application.persistentDataPath`, `PlayerPrefs`, etc.); the framework `JsonUtility` is thread-safe (ThreadStatic buffers).
- The encrypted handler's `Key`/`Iterations` come from `SaveServiceSettings`, and the `Salt` is the `SaveEncryptor` default; changing any of them makes old saves undecryptable (`TryLoad` returns `IntegrityCheckFailed`/`DecryptionFailed`).
- The JSON handler relies on the framework's built-in `JsonUtility` (`Moirai.Atropos`'s `Core/Utilities/Json`), not `UnityEngine.JsonUtility`, and can directly serialize `byte[]`, dictionaries, and other types.
- Path assembly now uses `Path.Combine`: the separator returned by `DetermineSavePath` is platform-specific (`\` on Windows, previously always `/`); do not depend on parsing path strings.
- Atomic replacement prefers `File.Replace` (atomic at the NTFS metadata level); on platforms without support (e.g., the WebGL virtual file system) it automatically falls back to delete+rename — verify on target devices.

---
[« Back to Main README](../../README_EN.md) · [Resource](Resource.md) · [Procedure](Procedure.md)
