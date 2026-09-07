# Save

## Overview

The Save service (`SaveService`) provides AAA-grade save infrastructure: a **single-file multi-block** container, **four pluggable serialization backends** (JSON / MessagePack / MemoryPack / protobuf-net), **block-level version migration**, an **AES-encrypted pipeline**, and **no-code component saving** (Source-Generator-generated strongly-typed capturers). Namespace `Moirai.Atropos.Save`.

## Architecture

```
SaveService (static facade, silently degrades when s_Handler is null)
├── Storage pipeline ([SerializeReference] swappable)
│     PlainSaveHandler        pass-through (no crypto)
│     AesEncryptedSaveHandler AES-256-CBC + HMAC (encrypt-then-MAC) + PBKDF2
├── Serialization backends (ESaveBackend + ISaveSerializer + SaveSerializerRegistry)
│     Json (built-in, default) / MessagePack / MemoryPack / Protobuf / KeyValue (component-only)
├── Multi-block container (SaveFileContainer, hand-rolled binary: key/version/backend/bytes per block)
├── Data model ([SaveData] + SaveDataBlock.OnMigrate version migration)
└── No-code saving ([SaveField] + SaveComponent + SaveHost Source Generator capturers)
```

## File Format v2

```
[32B plaintext header "MRSA"][payload]
Header: [4B magic][4B format version=2][8B UTC ticks][4B payload length][4B payload CRC32][4B flags]
Payload = Compress?(Container); encrypted handlers wrap [16B IV][AES-256-CBC][32B HMAC]
Container: [4B magic "MRSB"][4B container version][4B block count]
      per block [4B key length][key UTF8][4B data version][2B backend][4B byte length][bytes]
```

- The header is always plaintext (saved time readable without decryption); CRC guards storage corruption, HMAC guards tampering (verify MAC before decrypting)
- v1 files (28B header, single-block legacy format) are rejected with `UnsupportedVersion` (pre-launch decision, no dual-format reads)
- Atomic writes: temp file `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace`; orphan temp files swept in background at init
- Write paths on the same file are serialized through a per-file semaphore (prevents lost updates from concurrent read-modify-write)

## Save Paths

`Application.persistentDataPath/Data/{folderName}/{fileName}{extension}`; the extension defaults to `.sav` (configured in `SaveServiceSettings`; passed file names are stripped and re-appended).

## Manual Save Data Scripts (with version migration)

```csharp
[SaveData("PlayerStats", version = 3, Backend = ESaveBackend.MessagePack)]
public sealed class PlayerStatsData : SaveDataBlock
{
    public int Level;
    public long Gold;

    protected internal override void OnMigrate(int fromVersion)
    {
        // switch-cascade convention: v1→v2→v3 fallthrough, fix fields in place
        switch (fromVersion)
        {
            case 1: Gold = 0; goto case 2;
            case 2: Level = 1; break;
        }
    }
}
```

- Writes record the declared version; on load, a stored version lower than declared runs the cascade (in-memory fix, persisted on the next save); higher → rejected with `UnsupportedVersion`
- Binary backends require their own AOT annotations: MessagePack `[MessagePackObject]`+SG, MemoryPack `[MemoryPackable]` partial+SG, protobuf-net `[ProtoContract]`+BuildTools SG; unannotated types are unsupported on IL2CPP

## No-Code Saving (checked component fields)

1. Declare gameplay components as `partial class` and mark fields with `[SaveField]` (optional explicit key to stay stable across renames):

```csharp
public partial class Player : MonoBehaviour
{
    [SaveField] private int _hp;
    [SaveField("bag_items")] private List<int> _items;   // collections/nested classes are a future generator extension; MIRAI300 today
}
```

2. Attach a **Save Component** to the GameObject: add target-component bindings and check the fields to save (the block key auto-derives as `scene:path` when left empty).
3. Trigger with `SaveService.SaveComponentsAsync(fileName)` / `LoadComponentsAsync(fileName)` — compile-time-generated strongly-typed capturers run with zero reflection, filtered by the checked mask; unknown keys are skipped and missing keys keep current values (natural forward/backward compatibility for field changes).

Generator diagnostics: MIRAI300 unsupported type, MIRAI301 duplicate key, MIRAI303 partial class required, MIRAI304 instance field required. After editing generator sources (`SourceGenerators/Source~/SaveHost/`) rebuild `SourceGenerators/SaveHost.dll` with `dotnet build -c Release`.

## Public API (static facade)

### Block-level (primary)

| API | Description |
|---|---|
| `SaveBlockAsync<T>(data, fileName, key, folderName, ct)` | Read-modify-write block merge (atomic replace; per-file write serialization) |
| `LoadBlockAsync<T>(fileName, key, folderName, ct)` | Read block; returns default on failure |
| `TryLoadBlockAsync<T>(...)` → `SaveResult<T>` | Error classification (FileNotFound/Corrupted/IntegrityCheckFailed/UnsupportedVersion…) |
| `DeleteBlockAsync(fileName, key, folderName, ct)` | Delete block (removes the file when the last block goes) |
| `GetBlockInfos(fileName, folderName)` | Block metadata (key/version/backend/size) |
| Sync pairs `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | Main-thread blocking variants (quit-time flushes) |

### Legacy (single-object APIs mapped to the reserved `__main__` block)

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad` — signatures unchanged from the A+ version.

### Metadata / Slots / Backup

| API | Description |
|---|---|
| `SaveMetadataAsync` / `TryLoadMetadataAsync` (+sync pairs) | Slot metadata (reserved `__meta` block, JSON backend) |
| `GetSaveFiles(folderName)` / `GetSaveFilesAsync` | Slot enumeration (newest first) |
| `FileExists` / `DetermineSavePath` | Queries and paths |
| `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` (+Async pairs) | Deletion (backoff retries) |
| `CreateBackup` / `RestoreBackup` | Single-slot `.bak` backup/restore (atomic replace) |

### Degradation contract (handler not ready)

Writes/deletes no-op; reads return default; `TryLoad*` returns `Failure(HandlerNotReady)`; enumerations return empty arrays.

## Configuration (SaveServiceSettings)

| Field | Description |
|---|---|
| `m_SaveServiceHandler` | Storage pipeline handler (PlainSaveHandler / AesEncryptedSaveHandler) |
| `m_DefaultBackend` | Default serialization backend (blocks without `[SaveData]`) |
| `m_EncryptionKey` / `m_Pbkdf2Iterations` | Crypto parameters (**SECURITY: replace the placeholder key before shipping**; derived keys are cached per instance) |
| `m_SaveFileExtension` | Save file extension (default `.sav`) |

## Dependencies

MessagePack 3.1.8, protobuf-net 3.3.8 (+Core with embedded BuildTools SG), MemoryPack 1.21.4 (via NuGetForUnity; runtime DLLs are auto-referenced; analyzer DLLs need the RoslynAnalyzer label). Missing DLLs fail fast in `SaveSerializerRegistry.GetRequired`.

## Tests

`Tests/EditorMode/Save/`: container layout, composer, handler pipeline (atomic writes/sweep/corruption classification/argument validation), full crypto chain, four-backend round-trips, migration cascades, component capturers (generated code).
