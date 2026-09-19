# Save Service

> Single-file multi-block container, pluggable serialization/encryption/compression, file-level migration bus, and no-code component saving.

The Save service (`SaveService`) provides AAA-grade save infrastructure: **four pluggable serialization backends** (JSON / MessagePack / MemoryPack / protobuf-net), a **file-level version migration bus** (version chains + auditing + lazy write-back) alongside **block-level version migration**, an **AES-encrypted pipeline** (pluggable key source: static passphrase / runtime passphrase injection / HKDF per-user derivation), **optional GZip compression**, and **no-code component saving** (Source-Generator-generated strongly-typed capturers). Namespace `Moirai.Atropos.Save`.

## Architecture

```
SaveService (static facade, write paths throw GameException when handler is null, reads degrade)
├── Storage pipeline ([SerializeReference] swappable)
│     PlainSaveHandler        pass-through (no crypto)
│     AESEncryptedSaveHandler AES-256-CBC + HMAC (encrypt-then-MAC), nested [SerializeReference] key provider
├── Storage backend ([SerializeReference] swappable, ISaveStorage + SaveStorageBackend)
│     FileSaveStorageBackend  local files (temp + Flush(true) + atomic replace, default)
│     CloudSaveStorageBackend local mirror + remote KV dual-write (remote plug-ins shipped: RestCloudSaveKvStore / UnityCloudSaveKvStore)
├── Transform chain (fixed order: Serialize → Compress? → Encrypt? → CRC)
│     Compression: ICompressionProvider + SaveCompressionRegistry (GZip built-in, ID=1; unknown IDs rejected on read)
│     Keys: ISaveKeyProvider + SaveKeyProvider (nested on the AES handler; Static passphrase default / Passphrase runtime-injected / HkdfPerUser per-user HKDF)
├── Serialization backends (ESaveBackend + ISaveSerializer + SaveSerializerRegistry)
│     Json (built-in, default) / MessagePack / MemoryPack / Protobuf / KeyValue (reserved for the component capture format)
│     open registration: Register(ISaveSerializer)/Unregister(ESaveBackend) (duplicate backends fail fast; KeyValue cannot be claimed)
├── Multi-block container (SaveFileContainer, hand-rolled binary: key/version/backend/bytes per block)
├── Data model ([SaveData] + SaveDataBlock.OnMigrate version migration)
├── Migration bus (SaveMigrationManager + ISaveMigrator: file-level version chain, pre-positioned on load/write pipelines)
└── No-code saving ([SaveField] + SaveComponent + SaveHost Source Generator capturers, [SaveComponentSchema] schema versions)
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
- Key sources (`ISaveKeyProvider`): static passphrase PBKDF2 (`StaticSaveKeyProvider`, default placeholder when none is configured) / runtime passphrase injection (`PassphraseSaveKeyProvider`, passphrase held in memory only — reads classify `InvalidArgument` and writes fail fast until injected) / HKDF-SHA256 per-user derivation (`HKDFPerUserSaveKeyProvider`, accounts cannot read each other's saves)
- Atomic writes: temp file `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace` (via `FileSaveStorageBackend`); orphan temp files swept in background at init
- End-to-end streaming IO: writes go through the `WriteAtomic(Action<Stream>)` delegate (placeholder header → CRC pass-through → encrypt/compress chain → container streamed segment-by-segment → real header back-filled, zero whole-file buffering); reads flow header 32B → incremental CRC → decrypt/decompress chain → 256KB segment pool → CRC validation → container parsed across a `ReadOnlySequence` (single-segment fast path hits the span parser directly); AES saves read in two streaming passes (first pass streams the HMAC pre-validation — verify before decrypt, eliminating padding oracles; after freezing and rewinding, a second bounded `[IV‖ciphertext]` decrypt chain keeps the HMAC tail outside the segment, safe to close at any moment)
- Write paths on the same file are serialized through a per-file semaphore (prevents lost updates from concurrent read-modify-write) — the gate lives in the handler orchestration layer, transparent to storage backends
- All delete operations (single slot / folder / root wipe, sync and async) hold a hierarchical gate ("root → folder → file", fixed acquisition order, deadlock-free) mutually exclusive with block IO — the exists-check and the delete complete inside the same critical section, so a concurrent write can never resurrect a deleted file; compliance-grade data erasure (`DeleteAllSaveFiles`) is reliable
- Storage contract (`ISaveStorage`): sync primitives are the contract core (`Exists`/`TryReadAllBytes`/`WriteAtomic`/`DeleteFile`/`DeleteDirectory`/`EnumerateFiles`/`CreateBackup`/`RestoreBackup`); streaming primitives sink zero-whole-buffer IO (`WriteAtomic(filePath, Action<Stream>)` delegate writes — placeholder header → transform chain → real header back-filled inside an addressable temp stream; `TryOpenRead` addressable read-only stream); `TryGetWriteTimeUtc` metadata query backs the session-level incremental guard; async wrappers default to thread-pool offload (true-async backends override and declare `Capabilities`); the capability set includes `SyncReadsAuthoritative` (whether sync reads are authoritative — false for the cloud backend, where the facade's bare-name sync reads log a one-time warning steering callers to the async APIs); read errors are classified codes, write failures throw `GameException`, deletes are idempotent; implementations must be pure .NET (callable from any thread)

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

## Version Migration Bus (file-level)

Block-level `OnMigrate` covers **single-type** schema evolution; the migration bus (`SaveMigrationManager` + `ISaveMigrator`) covers **whole-file** data versions — cross-block renames, field retypes, obsolete-block cleanup and other lateral changes. The two tracks compose; on the load pipeline the bus runs **before** `OnMigrate`.

### Enablement and version model

- Versions are monotonically increasing ints: `0` = the pre-versioning baseline; the game sets `SaveService.CurrentSaveVersion = N` at startup (default `0` = bus fully bypassed, zero overhead)
- Adoption ritual: when enabling versioning with existing saves, register the chain from version `0` (meta-less saves are treated as version `0`; if the shape is unchanged, an empty migrator bridges `0→1`)
- Version stamping: while the bus is active, every written file gets the current `SaveVersion` stamped into the `__meta` block automatically (no game-side bookkeeping; a semver triple can map to int as `major*10000+minor*100+patch` — fix the mapping rule per project)

### Migrators (ISaveMigrator)

```csharp
public sealed class SaveMigratorV1ToV2 : ISaveMigrator
{
    public int FromVersion => 1;
    public int ToVersion => 2;
    public int Priority => 0;   // migrators on the same edge run in ascending Priority order

    public UniTask Migrate(SaveMigrationContext ctx)
    {
        ctx.RenameBlock("oldKey", "newKey");
        ctx.RenameField<PlayerData>("gold", "coins");                   // block key resolved from [SaveData] (explicit key also supported)
        ctx.RetypeField<int, string>("profile", "level", v => v.ToString());
        ctx.TransformBlock<PlayerData>("profile", old => new PlayerData { /* … */ });
        ctx.DeleteBlock("obsolete");
        return UniTask.CompletedTask;
    }
}
```

- Registration: implementations are scanned by the SaveHost Source Generator and self-register via a module initializer (AOT-safe; requires a concrete, non-abstract, non-privately-nested class with an accessible parameterless constructor — MIRAI302 otherwise); manual registration via `SaveService.RegisterMigrator(...)`. Invalid edges (`To <= From`) throw `ArgumentException` at registration — upgrade direction only, cycles impossible by construction
- Execution constraints: migrations run **synchronously** inside the load/write pipeline (gate held, possibly on the main thread) — `Migrate` must complete synchronously; returning an incomplete task fails fast with `MigrationFailed`; Unity main-thread APIs are forbidden
- Operations on absent blocks/fields are no-ops (return `false`, tolerating saves that never wrote that block); real exceptions (e.g. deserialize failures) abort the whole chain
- Field-level ops (`RenameField`/`RetypeField`) support JSON (requires Newtonsoft.Json) and KeyValue blocks; **binary backends do not support field-level ops** (warned and skipped — use `TransformBlock<T>` with the legacy type kept around; key-order discipline is guarded by the analyzer below)

### Chain semantics and error typing

- Equal versions short-circuit; `file version > current version` → `UnsupportedVersion` (downgrade rejected); missing/ambiguous chain (multiple edges from one version to different targets)/migrator exceptions → `MigrationFailed` (observable via the `LoadFailed` event at stage `Migrate`)
- **Write-time healing**: any read-modify-write (block save/component upsert/block delete write-back) reaching an old-version file migrates it before merging — every file on disk is always at the current version, so new-shape blocks can never land in an old file and get re-transformed by the chain
- **Write-back policy**: after a load-triggered migration, the result is lazily written back per `m_MigrationWriteBack` (default on — avoids re-running the chain on every load); when off, migration applies to memory only, a session-level cache prevents re-running for the same file in the session, and the file stays at its old version
- **Audit**: every migration step appends `"{from}->{to}|{migrator type full name}|{UTC ISO-8601}"` to `SaveMetadata.MigrationHistory` (persisted with write-back)
- **Explicit migration**: `SaveService.MigrateSave(fileName)` / `MigrateSaveAsync` — for batch-healing old saves at startup; a successful migration **forces write-back** (regardless of the write-back setting); returns `HandlerNotReady` when the handler is not ready
- Session-cache invalidation: `RestoreBackup` and delete operations invalidate the per-path (or whole) session cache automatically

### Component schema versions (no-code track)

- Mark a component class with `[SaveComponentSchema(version)]` (default 1) → the generator emits the capturer `SchemaVersion`; saves record it per component type in the KVT block's `$schemas` scope (a reserved key that can never collide with a type full name)
- On restore, when the stored version differs from the capturer's current version: if the component implements `ISaveComponentMigrator`, restore routes to `OnMigrateComponent(fromVersion, ref reader, recordCount)` (which must consume exactly recordCount records); otherwise a warning is logged and key-matching tolerant restore applies
- Legacy blocks (without `$schemas`) are treated as current (KVT key matching is naturally backward compatible)

### Binary-backend key-order freeze (analyzers MIRAI400/401)

- `ServiceDependency.dll` ships `SaveSchemaAnalyzer`: member key ordinals (`[Key]`/`[MemoryPackOrder]`/`[ProtoMember]`) of `[SaveData(Backend=MessagePack/MemoryPack/Protobuf)]` types are compared against a snapshot — MIRAI400 warns on key reordering, MIRAI401 warns on deleted members without an `OnMigrate` override (both Warning)
- The snapshot is an additional file `.SaveSchemaSnapshot` (line format `TypeFullName|member:number;…`, number -1 = MessagePack string-key mode), checked into version control and updated as the schema evolves; the analyzer stays silent while no snapshot exists
- **Snapshot generation**: menu `Tools/Moirai/Save/Export Schema Snapshot` scans all binary-backend `[SaveData]` types and writes `.SaveSchemaSnapshot` to the project root (extraction rules mirror the analyzer exactly; members sorted by name for stable diffs) — re-export to advance the baseline before changing the binary wire format
- Unity has no AdditionalFiles UI — wire it via `/additionalfile:` in `csc.rsp` or via CI `dotnet build`

## No-Code Saving (checked component fields)

1. Declare gameplay components as `partial class` and mark fields with `[SaveField]` (optional explicit key to stay stable across renames):

```csharp
public partial class Player : MonoBehaviour
{
    [SaveField] private int _hp;
    [SaveField("bag_items")] private List<int> _items;
    [SaveField] private Dictionary<string, int> _inventory;
    [SaveField] private PlayerStats _stats;          // nested [SaveData] data class
    [SaveField] private EnemyAI _target;             // scene reference (target needs SaveObjectIdentity)
    [SaveField] private Texture2D _icon;             // asset reference (must be registered in SaveAssetCatalog)
}
```

2. Attach a **Save Component** to the GameObject: add target-component bindings and check the fields to save (the block key auto-derives as `scene-namespace:path` when left empty — the namespace is the scene asset path for saved scenes (so same-name additive scenes cannot collide) or the scene name for unsaved/dynamic scenes; the path is the **full name chain** from scene root to object, and same-name siblings/roots get a `[N]` ordinal suffix for disambiguation so cross-branch key collisions cannot silently overwrite each other).
3. Trigger with `SaveService.SaveComponentsAsync(fileName)` / `LoadComponentsAsync(fileName)` — compile-time-generated strongly-typed capturers run with zero reflection, filtered by the checked mask; unknown keys are skipped and missing keys keep current values (natural forward/backward compatibility for field changes).

### Supported field types (SG v2)

- **Scalars**: primitives/enums/string/DateTime/TimeSpan/Unity math types (Vector2/3/4, Quaternion, Color, Rect, Bounds).
- **Collections**: arrays `T[]`, `List<T>`, `Queue<T>`, `Stack<T>`, `HashSet<T>`, `Dictionary<K,V>`; elements recursively support scalars and nested data classes (collections of collections included); map keys are scalar/enum only; **reference types are not supported as collection elements** (MIRAI308). Restore uses **replace semantics** (a fresh container instance); null and empty collections stay distinct; `Stack<T>` is written top-to-bottom and restored by pushing in reverse to preserve LIFO state.
- **Nested data classes**: a non-MonoBehaviour class marked `[SaveData]`; captures **all its public instance fields** (key = field name, JSON-style; the Key/Version arguments are unused in the nested context). Cycles/abstract classes/missing accessible parameterless constructors/structs report MIRAI307; `SaveDataBlock` subclasses cannot be nested field types (use the manual block API).
- **Scene object references** (GameObject/Component-derived fields): capture stores the target's `SaveObjectIdentity` **stable ID** (baked as a GUID by OnValidate in the editor, auto-assigned when empty); restore looks up the same ID in the current scene via `SaveEntityRegistry` (Component fields resolve via `GetComponent<T>`). A target without SaveObjectIdentity captures as Null with a warning; a stored ID absent from the current scene restores null with a warning. Duplicating an object (Ctrl+D) copies the ID — duplicates are first-come-first-served at runtime with a warning; clear the ID field to re-bake. Every scene-reference field emits a MIRAI305 Info reminder.
- **Asset references** (other UnityEngine.Object-derived fields such as Texture/SO/Material): capture resolves object → ResourceService location via `SaveServiceSettings.m_AssetCatalog` (a SaveAssetCatalog SO); restore resolves the location back through the same catalog — **catalog-based two-way resolution with no runtime loading** (keeps the capturer contract synchronous, zero lease burden). Referenced assets must be registered first; unregistered assets or a missing catalog capture as Null with a warning. Fields declared as the `UnityEngine.Object` base type are ambiguous between scene/asset and report MIRAI306 (skipped). Programmatic catalog edits (editor tools/importers mutating `m_Entries` directly) must call `SaveAssetCatalog.InvalidateLookup()` afterwards — the lookup table is lazily built; a stale cache both hides new entries and fails to block duplicate registration.

### Generator diagnostics

MIRAI300 unsupported field type; MIRAI301 duplicate key; MIRAI302 migrator not registrable; MIRAI303 the containing type and every level of its nesting chain must be partial classes; MIRAI304 instance field required; MIRAI305 scene-reference needs SaveObjectIdentity guidance (Info); MIRAI306 reference declared as the UnityEngine.Object base type (Warning, field skipped); MIRAI307 invalid nested data type; MIRAI308 unsupported collection-element/map-key/value type. After editing generator sources (`SourceGenerators/Source~/SaveHost/`) rebuild `SourceGenerators/SaveHost.dll` with `dotnet build -c Release -t:Rebuild` (an incremental build can skip compilation and emit a broken skeleton DLL).

### Built-in capturers (engine components)

Engine components carry no `[SaveField]` annotations — the framework ships hand-written capturers whose field lists drive the Inspector checkbox view (shown automatically when a type has no [SaveField] fields):

| Component | Fields | Notes |
|---|---|---|
| `Transform` | `localPosition` / `localRotation` / `localScale` | Local space — local coordinates stay correct after entity parent rewiring |
| `Rigidbody` | `linearVelocity` / `angularVelocity` | Physics motion persistence; restore applies only to non-kinematic bodies (kinematic velocity is driven by animation/scripts) |
| `ParticleSystem` | `time` | Playback progress; restore writes `ParticleSystem.time` directly (visible effect only while playing) |

## Dynamic Entity Persistence (prefab diffing)

Objects spawned from prefabs at runtime (monsters/drops/temporary structures) persist as a "spawn table + one diff block per entity":

1. **Register prefabs**: create a `SavePrefabRegistry` SO (Create → Moirai → Save Prefab Registry) and register each persistable prefab with a **stable key** (the save-file reference — renaming breaks saves) and a **ResourceService location** (YooAsset address), then reference it in `m_PrefabRegistry` of the save settings.
2. **Add a SaveComponent to the entity root** and check fields (include the built-in Transform capturer fields to persist position/rotation/scale).
3. Game code replaces `Instantiate`/`Destroy` with the persistent spawn/destroy pair:

```csharp
// Spawn (registered in the session spawn table; inactive-staging trick — stable ID/block key injected before activation, Awake sees the final state)
GameObject goblin = SaveService.InstantiatePersistent("goblin", pos, rot);
// Destroy (dynamic entities leave the spawn table → their blocks are cleaned on next save; scene-preset objects join the destroyed table → destroyed on restore)
SaveService.DestroyPersistent(goblin);
// Save/restore (restore = DestroyUnwanted → SpawnMissing → parent wiring → RestoreAll → activate + EntityRestored event)
await SaveService.SaveEntitiesAsync("slot1");
await SaveService.RestoreEntitiesAsync("slot1");
```

- **Template diffing**: entity capture is compared field-by-field against the prefab template baseline (one baseline KVT cached per stable key per session) and **only fields changed relative to the template are written** (nested objects diff recursively; any collection change carries the whole record — element-level diffing is v2 scope); restore = instantiate (natural template defaults) + apply the diff, minimizing save growth. When the baseline is unavailable (prefab unregistered / no root SaveComponent) capture degrades to full writes.
- **Block layout**: entity table = reserved `__entities` block (spawn records EntityId/PrefabKey/SceneName/ParentId + destroyed preset IDs); entity data = one `entity:{EntityId}` block per entity (the pipeline rewrites the entity component's block key before activation). **Component save/load APIs skip `entity:`-prefixed blocks** — full world save = `SaveEntitiesAsync` + `SaveComponentsAsync`, restore = `RestoreEntitiesAsync` + `LoadComponentsAsync` (entities first).
- **CarryForward semantics**: saving only upserts active entities; blocks of unvisited scenes and failed spawns stay untouched; entities destroyed by bypassing `DestroyPersistent` (plain `Object.Destroy`) also keep their records and blocks (explicit destroy is required for removal). After a restore, the session spawn/destroy tables are replaced wholesale with the file state.
- **Incremental saves**: session-level dirty tracking (a per-file baseline = entity-table bytes + per-entity diff payloads + the file's write time) — when nothing changed the save is skipped with zero IO and no events; with changes, a single-pass merge (read → self-healing migration → stale removal → upsert → atomic write-back) writes only dirty blocks (only changed blocks fire `BlockSaved`); when the baseline is invalidated (first save / after a restore / the file was overwritten externally — the write-time guard uses `FileInfo.Refresh`-fresh metadata to defeat NTFS cache lag) the save conservatively falls back to a full merge (orphans resolved after reading the file).
- **Thread ownership**: entity/component facade async APIs run IO on worker threads; after the read continuation returns, the facade switches back to the main thread before scene operations and field write-backs (Unity APIs always run on the main thread).
- **Parenting and scene placement**: a spawn record's ParentId (the parent must carry a SaveObjectIdentity, otherwise the link is not persisted and a warning is logged) is wired in a dedicated second pass; when the recorded SceneName is loaded the entity lands there, otherwise it lands in the active scene with a warning — **fallback placement never rewrites scene attribution** (the spawn table keeps the recorded SceneName, so the entity returns to its original scene in a later session once that scene loads — no drift). Stable-ID lookup goes through `SaveEntityRegistry` — two tables: scene scope (swept on scene unload) and global scope (DontDestroyOnLoad residents).
- **Restore timing**: spawned entities stay inactive until their diff blocks have been written back — Awake/OnEnable see the final parent and restored field values (listen to `EntityRestored` or run logic after Start when post-restore state is required). An entity whose diff block is corrupted logs an error and restores to template defaults without blocking others.
- **Degradation contract**: `InstantiatePersistent`/`DestroyPersistent` do not depend on the save handler (registry + resource service suffice); unregistered keys or load failures log an error and return `null`. `SaveEntitiesAsync` throws `GameException` when the handler is not ready; `RestoreEntitiesAsync` silently degrades to a completed task.
- **No ID baking on prefab assets**: `SaveObjectIdentity.OnValidate` skips the prefab asset itself (an ID on the asset would be shared by every instance and inevitably collide); scene instances still bake individually, and dynamic entities receive a per-instance unique ID injected by the spawn pipeline before activation.

| API (entity track) | Description |
|---|---|
| `InstantiatePersistent(prefabKey, position, rotation, parent)` | Spawn a persistent entity synchronously (template loaded via ResourceService; `null` on failure) |
| `InstantiatePersistentAsync(prefabKey, position, rotation, parent, ct)` | Async spawn (`null` on cancellation/failure) |
| `DestroyPersistent(target)` | Destroy with persistence semantics (dynamic entity removed from table / preset object marked destroyed / plain object just destroyed) |
| `SaveEntitiesAsync(fileName, folderName, ct)` | Write the entity table and all active entity diff blocks (baseline warm-up → diff capture → incremental decision → single-pass merge; zero-change saves skip all IO; `GameException` on failure) |
| `RestoreEntitiesAsync(fileName, folderName, ct)` | Rebuild all dynamic entities from the file state (DestroyUnwanted→SpawnMissing→RestoreAll; `EntityRestored` per entity) |

## Screenshot & Metadata Mirroring

Save-slot thumbnail pipeline: capture the screen at end of frame (`ScreenCapture.CaptureScreenshotAsTexture`, **playing main thread only**) → GPU Blit downsample into a small RenderTexture with small-format readback (aspect-preserving, never upscales; longest edge via `m_ScreenshotMaxDimension`, default 256 — avoids full-resolution CPU filtering and readback stalls at 4K) → single main-thread PNG encode → atomic sidecar write `{save base name}.screenshot.png` through the storage layer (next to the save file; cloud storage backends follow naturally).

- **Metadata mirroring**: on capture success the reserved `__meta` block is mirrored — `ThumbnailFileName` (sidecar file name) and `SceneName` (active scene) are filled by the pipeline; `PlayTimeTicks` (`TimeSpan` ticks) is written by the game layer under its own accounting. Corrupted existing metadata is never overwritten (a warning is logged and mirroring is skipped, preserving salvage options).
- **Save linkage**: with `m_CaptureScreenshotOnSave` on, `SaveBlockAsync` / `SaveComponentsAsync` capture automatically after success (reserved `__`-prefixed blocks are exempt — the metadata mirror write-back never recurses); linkage failures never propagate to the save result, and linkage cancellation never leaks into the caller's token.
- **Lifecycle cascade**: `DeleteSave` / `DeleteSaveAsync` cascade-delete the sidecar (stale thumbnails cannot resurrect for a same-named new slot); folder-level deletes cover it naturally.
- **Degradation contract**: `HandlerNotReady` when the handler is not ready; `NotSupported` outside play mode / in batch mode (with a warning); sidecar write failures return `IoFailed` and log an error (never thrown). A successful capture fires the `ScreenshotCaptured` event.

| API (screenshot track) | Description |
|---|---|
| `CaptureScreenshotAsync(fileName, folderName, ct)` → `SaveError` | Capture screenshot + write sidecar + mirror metadata + fire event (playing main thread only) |

## Cloud Saves (local mirror + remote KV)

`CloudSaveStorageBackend` (storage backend plug-in, configured via `m_StorageBackend`): local file mirror + remote KV dual-write, with policy-arbitrated reads. The remote KV semantics are abstracted as `CloudSaveKvStore` ([SerializeReference] plug-in) — the framework ships two concrete backends (**`RestCloudSaveKvStore`** custom REST / **`UnityCloudSaveKvStore`** UGS conditional compilation), and projects may implement their own.

- **Key spec**: cloud key = path relative to the save data root (`persistentDataPath/Data/`), `/`-separated (e.g. `Save/slot1.sav`) — no machine-local directory structure, consistent across devices.
- **Error semantics**: unreachable/failed/logged-out remote calls throw; the backend normalizes to **offline degradation** (local mirror passthrough + warning). Missing keys are not errors (read `null` / exists `false` / idempotent delete).
- **REST backend (`RestCloudSaveKvStore`)**: configures the endpoint root / key prefix (multi-tenant namespace) / auth header / timeout; the auth value resolved from a code-injected dynamic token provider takes precedence over the static config value (keeps secrets out of serialized settings assets). REST contract: `GET/HEAD/PUT/DELETE {baseUrl}/{keyPrefix}{key}` (per-segment URL escaping; 404 on read = missing key) plus `GET {baseUrl}?prefix=` enumeration push-down (JSON envelope `[{"key","size","modified","revision"}]`; returned keys must carry the key prefix — this store strips it and re-validates, defensively skipping out-of-prefix keys). Revision dual channel (`X-Save-Revision` preferred / `ETag` fallback; neither = 0, falling back to timestamp arbitration); remote timestamp `Last-Modified` → response `Date` fallback chain; timeouts normalize to `TimeoutException`, unexpected status codes to `IOException`. HttpClient pure-.NET transport (callable from any thread; **no raw sockets on WebGL — pick UGS or a UnityWebRequest-based custom backend there**).
- **UGS backend (`UnityCloudSaveKvStore`)**: activates automatically via the asmdef versionDefine (`UNITY_CLOUD_SAVE_INSTALLED`) once `com.unity.services.cloudsave` is installed. Prerequisites: `UnityServices.InitializeAsync()` + player sign-in (Authentication); not-ready states throw and degrade offline; missing keys detected via `CloudSaveException.Reason == NotFound` for null/false/idempotent. Carried by the Player Files API (quota: 200 files / 1 GB per player); WriteLock is an etag-style string (not a monotonic number) — `WriteAsync` always returns 0 and arbitration uses the `FileItem.Modified` remote-authoritative timestamp channel; the SDK takes no cancellation tokens (cooperative checks before each call; in-flight requests cannot be aborted).
- **Dual write**: the local mirror commits atomically first, the remote follows; a remote failure **never blocks the local commit** — it is queued for backfill and replayed on the next successful remote operation (pending uploads / pending key deletes / pending prefix deletes, popped in order; on failure the remainder stays queued). Mirror-only backfill has a failure cooldown (30s default; the read path stops retrying during it, and the backlog is drained by backfill replay).
- **Enumeration push-down**: the `EnumerateAsync(prefix, ct)` overload filters client-side by default after a full enumeration; backends with server-side prefix filtering override it to cut traffic (slot enumeration and prefix deletion both use this path).
- **Read arbitration** (`ESaveSyncPolicy`): `Latest` picks the newer side / `LocalWins` local authority / `CloudWins` remote authority / `Custom` delegates per key to `SaveSyncConflictResolver` (falls back to `Latest` with a warning when unconfigured; resolver entries carry both sides' sizes and revisions — the enumeration path takes remote size from the listing SizeBytes, never derived from a null payload). **De-clocked arbitration**: when the remote provides a monotonic revision (`CloudKvEntry.Version` > 0) resolution compares version numbers — the mirror's last-synced revision is recorded in a `{file}.cloudver` sidecar, and mirror dirtiness is detected by local-mtime mismatch between mirror and sidecar (same local clock; client-vs-remote clock skew never participates). Backends without version numbers fall back to timestamp comparison (downloads re-stamp the mirror with the remote authoritative timestamp). Single-sided entries self-heal the other side (remote-only → download and refresh the mirror preserving the remote timestamp and version; mirror-only → upload backfill). `WriteAsync` returns the remote-assigned revision (0 = versions unsupported).
- **Sync primitives operate on the mirror only** (sync bare-name APIs never see the remote; remote sync is driven by the async API family); single-slot backups (`.bak`) are a local concept and never cloud-sync; folder-level deletes best-effort delete the remote prefix.
- **Capability declaration**: `Capabilities.SupportsTrueAsyncIO = true` (remote network IO is truly async); `SyncReadsAuthoritative = false` (sync reads are non-authoritative — sync primitives read only the local mirror and never see the remote, and the facade's bare-name sync reads log a one-time warning on this backend, steering callers to the async APIs for arbitrated results); platforms where sync reads are unavailable (e.g. WebGL) are unaffected — sync APIs read only the local mirror and stay usable.

## Tooling (debugger & editor)

- **In-game debugger window** `Profiler/Save` (`SaveServiceDebuggerWindow`, auto-registered by `SaveService.OnInit`): pipeline state (handler/storage backend/compression/default backend/screenshot toggle), slot list and selected-slot details (block table, metadata, corrupted blocks highlighted in red, screenshot sidecar state). Folder/slot selectors stay resident; the data region rebuilds on a 1s throttle.
- **Save browser editor window** (`Window/Moirai/Save Browser`): browses folders and slots under `persistentDataPath/Data/`; block table (key/version/backend/size/per-block errors); content preview for unencrypted saves (pretty-printed raw JSON blocks / **structured tree preview** of KVT blocks — nested objects, collections and maps expanded with indentation, falling back to a hex sample when parsing fails / hex sample for other backends); backup/restore-backup/delete (with screenshot sidecar cascade)/reveal-in-finder. The editor reads through the configured save handler (including its decryption chain and key provider) plus the configured compression provider — encrypted saves preview normally; saves written with mismatched key material surface as corrupted/unreadable.
- **Asset reference collector** (`Tools/Moirai/Save/Collect Asset References into Catalog`): scans SaveComponents in open scenes and registers the project assets currently referenced by asset-reference fields into `SaveAssetCatalog` (locations derive from the file-name addressing convention — review them in projects with custom addressing; scene-object instances are only warned about) — closing the "unregistered → silently writes null" gap. Within a single scan each asset is registered at most once (local seen-set, independent of the lazily built catalog cache); on any addition the collector calls `InvalidateLookup` and saves the asset.
- **SaveComponentEditor enhancements**: field checkboxes annotate reference kinds (scene reference = GameObject/Component-derived fields; asset reference = other UnityEngine.Object fields) with Identity/Catalog configuration hints; each binding shows its schema version (SG-emitted value → `[SaveComponentSchema]` declaration → default 1).

## Public API (static facade)

### Version migration

| API | Description |
|---|---|
| `CurrentSaveVersion { get; set; }` | Current save data version (monotonically increasing int; set on the main thread at startup, default 0 = bus inactive) |
| `RegisterMigrator(ISaveMigrator)` | Manual migrator registration (complement to self-registration; invalid edges throw `ArgumentException`) |
| `MigrateSave(fileName, folderName)` / `MigrateSaveAsync(...)` | Explicitly migrate a whole save file to the current version (forces write-back on success; `FileNotFound` when absent, `HandlerNotReady` when not ready) |

### Block-level (primary)

| API | Description |
|---|---|
| `SaveBlockAsync<T>(data, fileName, key, folderName, ct)` | Read-modify-write block merge (atomic replace; per-file write serialization) |
| `LoadBlockAsync<T>(fileName, key, folderName, ct)` | Read block; returns default on failure |
| `TryLoadBlockAsync<T>(...)` → `SaveResult<T>` | Error classification (FileNotFound/Corrupted/IntegrityCheckFailed/UnsupportedVersion…) |
| `DeleteBlockAsync(fileName, key, folderName, ct)` | Delete block (removes the file when the last block goes) |
| `GetBlockInfos(fileName, folderName)` | Block metadata (key/version/backend/size/per-block error typing; corrupted blocks are listed — framing fields trustworthy only when `HasMetadata` is true and `Error != None`) |
| Sync pairs `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | Main-thread blocking variants (quit-time flushes) |

### Quick (single-object shortcuts mapped to the reserved `__main__` block)

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad` — single-object save/load convenience entry points.

### Metadata / Slots / Backup

| API | Description |
|---|---|
| `SaveMetadataAsync` / `TryLoadMetadataAsync` (+sync pairs) | Slot metadata (reserved `__meta` block, JSON backend) |
| `GetSaveFiles(folderName)` / `GetSaveFilesAsync` | Slot enumeration (newest first) |
| `FileExists` / `DetermineSavePath` | Queries and paths |
| `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles` (+Async pairs) | Deletion (backoff retries; single-slot deletes cascade the screenshot sidecar) |
| `TryDeleteSave` / `TryDeleteSaveFolder` / `TryDeleteAllSaveFiles` (+Async pairs) | Existence-checked deletion (returns bool — `true` = existed and deleted; `false` = target did not exist or handler not ready, nothing was deleted; event behavior matches the void family) |
| `CreateBackup` / `RestoreBackup` | Single-slot `.bak` backup/restore (atomic replace) |

### Degradation contract (handler not ready)

Write paths (`SaveBlock`/`SaveBlockAsync`/`SaveComponentsAsync`/`SaveEntitiesAsync`/`SaveMetadata`/`SaveMetadataAsync`) **throw `GameException`** — never silently drop player progress; reads return default; `TryLoad*` returns `Failure(HandlerNotReady)`; deletes no-op; enumerations return empty arrays; the `TryDelete*` family returns `false`.

### Events (SaveService.Events)

Static events (default zero-overhead channel) + `EventManager` bridge events (`SaveSlotChangedEvent` etc. — subscribers pick either channel). All events dispatch on the main thread: operations triggered on the main thread dispatch inline (zero closure); async operations complete on worker threads and are queued via `MainThreadDispatcher.Post<TState>` (pooled work items + cached static lambdas — zero steady-state allocation). `OnShutdown` does not clear subscribers — subscribers must unsubscribe themselves; debug helpers may call `SaveService.UnsubscribeAll()`. Args are readonly value types (≤32B).

| Static event | Bridge event | When |
|---|---|---|
| `SlotChanged` | `SaveSlotChangedEvent` | Slot write (`Saved` merges create/update)/delete/backup create/backup restore; `FileName` is null for folder-level bulk deletes |
| `BlockSaved` / `BlockDeleted` | `SaveBlockChangedEvent` | Block save/delete completed (fileName+key+backend+size); idempotent no-op deletes never fire |
| `SaveProgress` / `LoadProgress` | `SaveProgressEvent` | Component capture/restore reported in batches (every 8 + always the final one; `ShouldReportProgress`) |
| `SaveFailed` / `LoadFailed` | `SaveFailedEvent` | Failures (`ESaveFailureStage` stage + `SaveError`); write failures also fail-fast with `GameException`; missing file/block (`FileNotFound`) never fires |
| `EntityRestored` | `SaveEntityRestoredEvent` | Fired per entity by the `RestoreEntitiesAsync` pipeline (after activation; args = entity ID + prefab key + instance) |
| `ScreenshotCaptured` | `SaveScreenshotEvent` | Screenshot pipeline completed (fileName + sidecar name + thumbnail size) |

## Configuration (SaveServiceSettings)

| Field | Description |
|---|---|
| `m_SaveServiceHandler` | Storage pipeline handler (PlainSaveHandler / AESEncryptedSaveHandler; the key provider is nested on the AES handler — empty falls back to `StaticSaveKeyProvider.Default` placeholders; alternatives: StaticSaveKeyProvider / PassphraseSaveKeyProvider / HKDFPerUserSaveKeyProvider) |
| `m_StorageBackend` | Storage backend (IO sink, default FileSaveStorageBackend; empty falls back to the file backend; for cloud saves pick `CloudSaveStorageBackend` — composes the remote KV plug-in (built-in `RestCloudSaveKvStore` custom REST / `UnityCloudSaveKvStore` UGS conditional compilation) + sync policy + custom resolver) |
| `m_CompressionProvider` | Compression provider (empty = no compression; built-in GZipCompressionProvider) |
| `m_DefaultBackend` | Default serialization backend (blocks without `[SaveData]`) |
| `m_SaveFileExtension` | Save file extension (default `.sav`) |
| `m_MigrationWriteBack` | Migration write-back (default on): lazily persists load-triggered migrations; when off, migration applies to in-memory data of that load only |
| `m_AssetCatalog` | Asset reference catalog (SaveAssetCatalog SO): no-code asset reference fields resolve locations two-way through the catalog; empty = asset reference fields always capture Null |
| `m_PrefabRegistry` | Prefab registry (SavePrefabRegistry SO): persistable dynamic entities (stable key → ResourceService location); empty = `InstantiatePersistent` unavailable and saved spawn records skip as unregistered |
| `m_CaptureScreenshotOnSave` | Screenshot on save (default off): `SaveBlockAsync`/`SaveComponentsAsync` capture automatically after success and mirror the sidecar + metadata (playing main thread only) |
| `m_ScreenshotMaxDimension` | Screenshot thumbnail longest edge (pixels, aspect-preserving, never upscales; default 256) |

## Dependencies

| Package | Version | Notes |
|---|---|---|
| MessagePack | 3.1.8 | Via NuGetForUnity; runtime DLLs auto-referenced; analyzer DLLs need the `RoslynAnalyzer` label |
| protobuf-net | 3.3.8 | Same (+Core with embedded BuildTools SG) |
| MemoryPack | 1.21.4 | Same |
| Unity Cloud Save | optional | Installing `com.unity.services.cloudsave` activates `UnityCloudSaveKvStore` via the versionDefine (`UNITY_CLOUD_SAVE_INSTALLED`) |
| LZ4 (K4os.Compression.LZ4) | reserved | versionDefine slot `LZ4_INSTALLED` reserved (activates on installing `org.nuget.k4os.compression.lz4`); the compression provider implementation comes later |

Missing DLLs fail fast in `SaveSerializerRegistry.GetRequired`.

## Tests

Directory: `Tests/EditorMode/Service/Save/`

| Test class | Coverage |
|---|---|
| `SaveFileContainerTests` | Container round-trips, corrupted-block skip, structural prefix preservation, v1 hard-cut |
| `SaveContainerV2Tests` | Partial recovery past a repatched header CRC, whole-file rejection, corrupted-block listing, write-back salvage |
| `SaveEventTests` | Trigger timing/count/args, failure-stage typing, background dispatch to main thread, progress batching |
| `SaveServiceHandlerTests` | Atomic writes, orphan sweep, corruption classification, argument validation, quick-map & degradation, RawBlocks round-trip |
| `FileSaveStorageBackendTests` | Atomic writes, idempotent deletes, exact-filter listing, backup/restore, capabilities |
| `SaveCompressionTests` | GZip round-trips, compress+encrypt combos, uncompressed reads, header classification, registry |
| `SaveKeyProviderTests` | Static derivation, passphrase injection, HKDF per-user isolation |
| `AesEncryptedSaveHandlerTests` | Full crypto chain |
| `SaveEncryptorTests` | AES/HMAC machinery |
| `SaveMigrationAndBackendTests` | Four-backend round-trips, block-level migration cascades |
| `SaveMigrationBusTests` | Version chains, rename/retype, whole-block transform, write-back toggle, audit, write-time healing, explicit migration |
| `SaveCapturerTests` / `SaveCapturerV2Tests` | Component capturers: field add/remove, collection/nested/scene/asset reference matrices |
| `SaveSerializerRegistryTests` | Registration validation, duplicate fail-fast, reserved backend, unregister |
| `SaveKeyValueElementTests` | KVT elements: sequence/map/nested, null, type-mismatch, buffer bounds |
| `SaveObjectIdentityTests` | Register/unregister, empty ID, duplicate-ID first-wins, destroy-invalidation, Resolve |
| `SaveAssetCatalogTests` | Two-way lookup, type mismatch, duplicate first-wins, cache invalidation, InvalidateLookup programmatic contract |
| `SaveKvDifferTests` | Template diff: scalar/nested/collection/add/type-drift/size-shrink/corrupt |
| `SaveEntityTableTests` | Entity table round-trip, empty table, nullable fields, unknown-record tolerance |
| `SaveEntityPersistenceTests` | Dynamic entity loop, diff, destroy marks, parent wiring, EntityRestored |
| `SaveEntityIncrementalTests` | Incremental entity save loop: zero-change skip, dirty-block-only writes, destroy cleanup, external-overwrite/after-restore full merges |
| `RestCloudSaveKvStoreTests` | REST backend: read/write round-trips (revision/timestamp), existence probes, idempotent deletes, prefix enumeration, auth-header precedence, remote-failure/timeout normalization, ETag fallback |
| `SaveBuiltInCapturerTests` | Transform / Rigidbody / ParticleSystem built-in capturers |
| `SaveScreenshotTests` | Sidecar, thumbnail, PNG encode, metadata mirror, cascade delete, non-playing degrade |
| `SaveCloudStorageBackendTests` | Dual-write, sync policy matrix, single-side align, offline degrade, backfill replay |
| `SaveBlockComposerTests` | Block composer |
| `SaveV3HardeningTests` | Hardening path regressions |

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Resource](Resource.md) · [Core](Core.md)
