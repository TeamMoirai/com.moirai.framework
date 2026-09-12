# Save

## Overview

The Save service (`SaveService`) provides AAA-grade save infrastructure: a **single-file multi-block** container, **four pluggable serialization backends** (JSON / MessagePack / MemoryPack / protobuf-net), **block-level version migration**, an **AES-encrypted pipeline** (pluggable key source: static passphrase / runtime passphrase injection / HKDF per-user derivation), **optional GZip compression**, and **no-code component saving** (Source-Generator-generated strongly-typed capturers). Namespace `Moirai.Atropos.Save`.

## Architecture

```
SaveService (static facade, silently degrades when s_Handler is null)
├── Storage pipeline ([SerializeReference] swappable)
│     PlainSaveHandler        pass-through (no crypto)
│     AesEncryptedSaveHandler AES-256-CBC + HMAC (encrypt-then-MAC), key material via ISaveKeyProvider
├── Storage backend ([SerializeReference] swappable, ISaveStorage + SaveStorageBackend)
│     FileSaveStorageBackend  local files (temp + Flush(true) + atomic replace, default)
├── Transform chain (fixed order: Serialize → Compress? → Encrypt? → CRC)
│     Compression: ICompressionProvider + SaveCompressionRegistry (GZip built-in, ID=1; unknown IDs rejected on read)
│     Keys: ISaveKeyProvider + SaveKeyProvider (Static passphrase default / Passphrase runtime-injected / HkdfPerUser per-user HKDF)
├── Serialization backends (ESaveBackend + ISaveSerializer + SaveSerializerRegistry)
│     Json (built-in, default) / MessagePack / MemoryPack / Protobuf / KeyValue (component-only)
├── Multi-block container (SaveFileContainer, hand-rolled binary: key/version/backend/bytes per block)
├── Data model ([SaveData] + SaveDataBlock.OnMigrate version migration)
└── No-code saving ([SaveField] + SaveComponent + SaveHost Source Generator capturers)
```

## File Format

```
[32B plaintext header "MRSA"][payload]
Header: [4B magic][4B format version=2][8B UTC ticks][4B payload length][4B payload CRC32][4B compression provider ID][4B flags]
Payload = Compress?(Container); encrypted handlers wrap [16B IV][AES-256-CBC][32B HMAC]
Container: [4B magic "MRSB"][4B container version=2][4B block count]
      per block [4B key length][key UTF8][4B data version][2B backend][4B byte length][4B payload CRC32][bytes]
```

- The header is always plaintext (saved time readable without decryption); the header CRC guards whole-file storage corruption, HMAC guards tampering (verify MAC before decrypting)
- Container v2 per-block CRC32 self-validation (second, fine-grained layer after the header CRC gate): a block whose payload fails validation is skipped and logged with a warning while the remaining healthy blocks load normally (**partial recovery**); structural damage to block framing (length/key fields out of bounds) preserves the parsed prefix and stops, since later block boundaries are unknowable. `TryLoadBlock` returns `Corrupted` for a corrupted key (distinct from `FileNotFound` for a truly absent one); corrupted blocks are dropped on the next write-back (healthy blocks are preserved)
- Container v1 files are hard-cut: reads classify as `UnsupportedVersion` with no dual-format compatibility
- Transform chain order is fixed: Serialize → **Compress (optional, before encryption)** → Encrypt → CRC; the read side reverses it (decrypt → decompress via header ID registry lookup) — uncompressed legacy files pass through unchanged (magic/flags sniffing is idempotent, old and new files coexist)
- Header offset 24-27 holds the compression provider ID (0 = uncompressed); unknown IDs are classified `UnsupportedVersion`, flag/ID inconsistency is classified `Corrupted`
- Key sources (`ISaveKeyProvider`): static passphrase PBKDF2 (`StaticSaveKeyProvider`, default, parameter-identical to V2) / runtime passphrase injection (`PassphraseSaveKeyProvider`, passphrase held in memory only — reads classify `InvalidArgument` and writes fail fast until injected) / HKDF-SHA256 per-user derivation (`HkdfPerUserSaveKeyProvider`, accounts cannot read each other's saves)
- Atomic writes: temp file `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace` (via `FileSaveStorageBackend`); orphan temp files swept in background at init
- Write paths on the same file are serialized through a per-file semaphore (prevents lost updates from concurrent read-modify-write) — the gate lives in the handler orchestration layer, transparent to storage backends
- Storage contract (`ISaveStorage`): sync primitives are the contract core (`Exists`/`TryReadAllBytes`/`WriteAtomic`/`DeleteFile`/`DeleteDirectory`/`EnumerateFiles`/`CreateBackup`/`RestoreBackup`); async wrappers default to thread-pool offload (true-async backends override and declare `Capabilities`); read errors are classified codes, write failures throw `GameException`, deletes are idempotent; implementations must be pure .NET (callable from any thread)

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

2. Attach a **Save Component** to the GameObject: add target-component bindings and check the fields to save (the block key auto-derives as `scene:path` when left empty; the path is the **full name chain** from scene root to object, and same-name siblings/roots get a `[N]` ordinal suffix for disambiguation so cross-branch key collisions cannot silently overwrite each other).
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
| `GetBlockInfos(fileName, folderName)` | Block metadata (key/version/backend/size/per-block error typing; corrupted blocks are listed — framing fields trustworthy only when `HasMetadata` is true and `Error != None`) |
| Sync pairs `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | Main-thread blocking variants (quit-time flushes) |

### Legacy (single-object APIs mapped to the reserved `__main__` block)

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad` — signatures unchanged from the v1 version.

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

### Events (SaveService.Events)

Static events (default zero-overhead channel) + `EventManager` bridge events (`SaveSlotChangedEvent` etc. — subscribers pick either channel). All events dispatch on the main thread: operations triggered on the main thread dispatch inline; async operations complete on worker threads and are queued via `MainThreadDispatcher`. `OnShutdown` does not clear subscribers — subscribers must unsubscribe themselves. Args are readonly value types (≤32B).

| Static event | Bridge event | When |
|---|---|---|
| `SlotChanged` | `SaveSlotChangedEvent` | Slot write (`Saved` merges create/update)/delete/backup create/backup restore; `FileName` is null for folder-level bulk deletes |
| `BlockSaved` / `BlockDeleted` | `SaveBlockChangedEvent` | Block save/delete completed (fileName+key+backend+size); idempotent no-op deletes never fire |
| `SaveProgress` / `LoadProgress` | `SaveProgressEvent` | Component capture/restore reported in batches (every 8 + always the final one; `ShouldReportProgress`) |
| `SaveFailed` / `LoadFailed` | `SaveFailedEvent` | Failures (`ESaveFailureStage` stage + `SaveError`); write failures also fail-fast with `GameException`; missing file/block (`FileNotFound`) never fires |
| `EntityRestored` | `SaveEntityRestoredEvent` | Pre-defined; wired by dynamic entity persistence |
| `ScreenshotCaptured` | `SaveScreenshotEvent` | Pre-defined; wired by the screenshot pipeline |

## Configuration (SaveServiceSettings)

| Field | Description |
|---|---|
| `m_SaveServiceHandler` | Storage pipeline handler (PlainSaveHandler / AesEncryptedSaveHandler) |
| `m_StorageBackend` | Storage backend (IO sink, default FileSaveStorageBackend; empty falls back to the file backend; cloud backends plug in by deriving `SaveStorageBackend`) |
| `m_CompressionProvider` | Compression provider (empty = no compression; built-in GZipCompressionProvider) |
| `m_KeyProvider` | Key provider (empty = static key; alternatives: PassphraseSaveKeyProvider / HkdfPerUserSaveKeyProvider) |
| `m_DefaultBackend` | Default serialization backend (blocks without `[SaveData]`) |
| `m_EncryptionKey` / `m_Pbkdf2Iterations` | Static-key parameters (**SECURITY: replace the placeholder key before shipping**; effective only when no key provider is configured; derived keys are cached per instance) |
| `m_SaveFileExtension` | Save file extension (default `.sav`) |

## Dependencies

MessagePack 3.1.8, protobuf-net 3.3.8 (+Core with embedded BuildTools SG), MemoryPack 1.21.4 (via NuGetForUnity; runtime DLLs are auto-referenced; analyzer DLLs need the RoslynAnalyzer label). Missing DLLs fail fast in `SaveSerializerRegistry.GetRequired`.

## Tests

`Tests/EditorMode/Save/`: container layout and v2 per-block validation (`SaveFileContainerTests`: round-trips/corrupted-block skip/structural prefix preservation/v1 hard-cut, `SaveContainerV2Tests`: partial recovery past a repatched header CRC/whole-file rejection/corrupted-block listing/write-back salvage), events API (`SaveEventTests`: trigger timing/count/args, failure-stage typing, background dispatch to main thread, progress batching), composer, handler pipeline (atomic writes/sweep/corruption classification/argument validation), storage backend contract (`FileSaveStorageBackendTests`: atomic writes/idempotent deletes/exact-filter listing/backup-restore/capabilities), compression transform chain (`SaveCompressionTests`: GZip round-trips/compress+encrypt combos/legacy uncompressed reads/header classification/registry), key providers (`SaveKeyProviderTests`: static equivalence/passphrase injection/HKDF per-user isolation), full crypto chain, four-backend round-trips, migration cascades, component capturers (generated code).
