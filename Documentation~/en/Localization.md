# Localization Service

> Multilingual service based on Luban configuration tables, supporting automatic injection and inline parsing of text, images, audio, and Timeline.

The `Localization` service is accessed via the `LocalizationService` static facade. It lazily loads all localized strings from the Luban configuration table (via `ConfigTableService` from [ConfigTable](ConfigTable.md)) and takes the available languages from the table's own self-report on the first access to any multilingual API. The language is determined by priority: "command-line argument -> editor setting -> saved setting -> system language". When switching languages, it re-injects all registered `LocalizerBase` components and only then raises `OnLanguageChanged`. In addition to retrieving text by ID, `LocalizationService.Localize` supports inline parsing of `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` placeholders in any string.

## Core Features

- `Language` object: carries `Name` (enum name), `Code` (ISO-639-1), `DisplayName` (localized display name), includes full `SystemLanguage` support and supports custom languages; built-in entries and `BuiltinLanguages` are shared instances rather than rebuilt on each access
- Language detection priority: command-line `-force-language` -> editor `LocalizationServiceSettings.EditorLanguage` -> `SettingUtility` saved setting -> `Application.systemLanguage` (falls back to Simplified Chinese when Chinese is not distinguished between Simplified/Traditional). When the detected language is not part of the loaded entries, the first loaded language takes over, so the UI never stays stuck on raw keys
- Text querying: `GetTextFromId` (supports `string.Format` parameters; missing translations expose the key), `TryGetTextFromId` (single-pass resolve: hit yields the text, miss yields `false`+`null` and feeds the missing-key tracking under the same policy — resource-mode localizers use it for their "inject if present, report if not" check instead of calling `Has` + `GetTextFromId` twice), `GetTextFromIdLanguage` (pass `null` for the current language), `GetDictionaryFromId` (retrieves all languages), `GetAllIds`
- Missing translations expose the key: an empty or whitespace-only cell in the current language returns the ID as-is, with no cross-language safety net (see "Missing Translations Expose the Key"); `TextLocalizer` display follows this policy
- Inline parsing: `LocalizationService.Localize` replaces `{l10n:ID}`, `{i18n:ID}`, `{g11n:ID}` with localized entries
- Component injection: `TextLocalizer` (TextMesh / UGUI Text / TMP_Text), `ImageLocalizer` (Image / RawImage / SpriteRenderer / Renderer material), `AudioLocalizer` (AudioSource); injectors dispatch by payload type (`string` = resource location / `int` = language index / `AudioClip` = direct asset)
- Auto-refresh on language switch: all `LocalizerBase` instances are re-injected on `ChangeLanguage` (pooled snapshot iteration with per-instance fault isolation — no resident garbage per switch even at ten-thousand-localizer scale) before the event is raised
- Timeline support: `TextLocalizerTrack` + `TextLocalizerPlayableAsset` switches text IDs on Timeline clips

## Core Types

Namespace: `Moirai.Atropos.Localization`

| Class/Interface | Description |
|----------------|-------------|
| `LocalizationService` | Static facade (`[HandlerHost]`) responsible for loading config table text, language switching, and Localizer management; the `OnLanguageChanged` event is exposed directly on the facade; `ToLanguage` / `Localize` / `ResolveLanguages` and the editor-preview API live in the same class's partial implementation (`LocalizationService.Helper`) |
| `Language` | Language class (`IEquatable<Language>`, compared by `Code`): `Name`, `Code`, `DisplayName`, `BuiltinLanguages`, supports conversion to/from `SystemLanguage`; built-in entries are shared read-only instances |
| `LocalizationServiceHandler` | Abstract handler base class: per-language text resolution, language switching, localizer registration |
| `LocalizerBase` | Abstract base class for localizers (MonoBehaviour): `Prepare` gets the target component reference, `Localize` performs injection |
| `IInjector` | Injector interface: `Inject<T1, T2>(localizedData, localizer)` |
| `TextLocalizer` | Text localizer, automatically discovers TextMesh / Text / TMP_Text and injects text |
| `ImageLocalizer` | Image localizer, switches between `sprites` / `textures` / `texture2Ds` arrays by language index |
| `AudioLocalizer` | Audio localizer, switches `clips` array by language index and injects into AudioSource |
| `UITextInjector` / `TMPInjector` / `TextMeshInjector` | Text injectors, writing to UGUI Text, TMP_Text, TextMesh respectively |
| `ImageInjector` / `RawImageInjector` / `SpriteRendererInjector` / `TextureInjector` | Image injectors, targeting Image, RawImage, SpriteRenderer, Renderer material properties respectively |
| `AudioSourceInjector` | Audio injector, targeting AudioSource |
| `TextLocalizerTrack` / `TextLocalizerPlayableAsset` / `TextLocalizerPlayableBehaviour` | Timeline track and Playable, binds `TextLocalizer` to switch text on clips |
| `ComponentFinder` | Static utility: finds components on a GameObject by generic type order |
| `CommandLineUtility` | Command-line parsing (`-force-language`), see the partial definition of the same class in `Runtime/Core/Utilities` |

## Quick Start

```csharp
// Access the service (static facade, call static methods directly)
LocalizationService.ChangeLanguage("English");

// Localized data is lazily loaded: it is automatically loaded from config tables on the first call
// to any query/switch API — no manual initialization required

// Get localized string by text ID (the ID is returned as-is when this language is
// untranslated or the ID does not exist at all)
string title = LocalizationService.GetTextFromId("main_title");

// With string.Format parameters (a malformed placeholder in the table degrades to the raw text
// instead of throwing)
string welcome = LocalizationService.GetTextFromId("welcome_player", "Moirai");

// Get text for a specific language / get all language translations for an ID (null = current language)
string english = LocalizationService.GetTextFromIdLanguage("main_title", Language.English);
Dictionary<string, string> all = LocalizationService.GetDictionaryFromId("main_title");

// ID check and enumeration
bool has = LocalizationService.Has("main_title");
List<string> ids = LocalizationService.GetAllIds();

// Switch language (three methods, Name and Code are case-insensitive)
LocalizationService.ChangeLanguage(Language.ChineseSimplified);
LocalizationService.ChangeLanguage("zh-Hans");
LocalizationService.ChangeLanguage(0);                 // By loaded language index

// Cycle through languages (debug use)
string next = LocalizationService.ActivateNextLanguage();
string prev = LocalizationService.ActivatePreviousLanguage();
```

## Advanced Usage

### Inline Placeholder Parsing

Markers like `{l10n:ID}`, `{i18n:ID}`, `{g11n:ID}` in any string will be replaced with the corresponding localized text, suitable for config table text composition:

```csharp
string hint = LocalizationService.Localize("Press {l10n:btn_confirm} to continue");
```

### Missing Translations Expose the Key

Queries resolve as "overlay -> current language -> ID". An entry that is empty or whitespace-only counts as untranslated, and **there is no cross-language safety net**: the ID is returned and the miss is recorded in the missing-key tracking.

The stance is "the table must be complete, and a missing translation must be visible" — patching a blank cell with another language's text keeps the UI free of raw keys, but nobody, designer or QA, ever learns that the cell was never translated.

- When the detected startup language is not shipped with these entries, the first language of the header is used instead, so the UI never starts out covered in keys
- Under per-language column loading this stance is also inevitable: the other languages' columns are not in memory at all, so there is nothing to fall back to

### Subscribing to Language Switching

```csharp
LocalizationService.OnLanguageChanged += language =>
{
    Debug.Log($"Language switched: {language.DisplayName}");
    // Raised after every LocalizerBase has been re-injected and the current language is updated,
    // so queries here already return the new language; refresh non-LocalizerBase content here
    titleText.text = LocalizationService.GetTextFromId("main_title");
};
```

### Component Injection

- Text: Add `TextLocalizer` to objects with `TextMesh`, UGUI `Text`, or `TMP_Text`, fill in `m_TextId` in the Inspector; at runtime, call `ChangeID(string textId)` to dynamically change text, `Clear()` to clear
- Image: `ImageLocalizer` acts on Image / RawImage / SpriteRenderer / Renderer in discovery order; `sprites` / `textures` / `texture2Ds` array elements must match the table's self-reported language column order (indexed by `CurrentLanguageIndex`), `Renderer` uses material properties (default `_MainTex`, can be specified via `propertyName`)
- Audio: `AudioLocalizer` injects `clips[CurrentLanguageIndex]` into AudioSource

### Timeline Localization

After installing the Timeline package (`TIMELINE_INSTALLED` macro), create a `TextLocalizerTrack` track and bind it to a `TextLocalizer` in the scene. Each `TextLocalizerPlayableAsset` clip sets a `textId`. When playback reaches that clip, the text automatically switches; when leaving the clip, it clears.

## Notes

- Localization data comes from Luban configuration tables: the Config project must first be generated and exported from the `LubanSettings` entry ("[框架]Luban 配置") under `Tools/Framework Settings`, otherwise loading fails with "Failed to load localized text, generate config first!" (logged once; while not ready every query returns the raw ID)
- Localized data is lazily initialized: no resources are loaded during service registration (`OnInit`); data is loaded from config tables on the first access to any multilingual API (query/switch) — by then the `Resource` service is guaranteed to be ready
- The list of available languages is self-reported by the config table: once data is exported split by language, languages are no longer inferred from the generated bean's field names — they come from the export-time constant `L10nLanguages.Codes`, handed to the framework through `ConfigTableServiceHandler.GetLocalizationLanguageCodes()` and resolved via `LocalizationService.ResolveLanguages`. There is no global language registry to fall back to. Calling `ChangeLanguage` with a language that is not part of the header keeps the current language and warns once per language rather than throwing
- A mismatch between an entry's language column count and the self-reported language count marks the dataset corrupt: **the whole batch is refused** and an error is logged (a shifted index only shows up as "the wrong language is displayed", never as an error, which is exactly why nothing is loaded)
- In `ToLanguage(str, onlySupported)`, when `onlySupported` is `true`, a language outside the loaded batch falls back to the default language English (`LocalizationService.DefaultLanguage`); use `TryGetBuiltInLanguage` to tell "typo" apart from "I do want the default"
- In the editor's non-play mode, `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` return `false` (there is no backend runtime to load from, and writing into the component would dirty the scene); use the component's inspector preview row instead, and `LocalizationService.Localize` resolves through that same editor-side read, returning the input as-is only when it is unavailable
- While the localization data is not ready (tables still loading), localizers **defer injection silently** instead of logging per-component missing-key errors; the language switch raised by the first successful load re-injects every registered localizer. Use `LocalizationService.IsDataLoaded` (does not trigger a load) to tell "not ready" apart from "genuinely missing"
- The arrays of `ImageLocalizer` / `AudioLocalizer` are injected by language index; after adding a new language to the config table, array elements must be supplemented accordingly
- By default the whole batch loads eagerly and every language column stays resident (the store keeps entries as a flat row-index + cell array, halving container objects vs. a list per entry). To drop residency to "header + current column", implement the `SupportsPerLanguageLoad` trio on a custom handler — and make that call from `ResidentChars` evidence (the "DATA FOOTPRINT" card in `Profiler/Localization`, or `LocalizationService.EntryCount` / `LoadedLanguageCount` / `ResidentChars`), not gut feeling. The default config-table source needs no handler work: once the export splits data by language and the game-side handler self-reports `SupportsPerLanguageLocalizationLoad`, the bridge switches into column mode on its own

## Runtime Overlay (live text patching)

Override entries for one language without touching the table or shipping a new build (ops fixing a mistranslation, QA forcing a string, remote patch):

```csharp
LocalizationService.SetStringOverlay("remote-ops", Language.English, new[]
{
    new KeyValuePair<string, string>("UI.Shop.Title", "Market"),
});
LocalizationService.ClearStringOverlay("remote-ops");   // drops this source only
```

- Additive: only the given keys of the given language are replaced, everything else still comes from the table; an empty/whitespace value means "not an override"
- The overlay is consulted before the table text for the same language — patching English changes what an English query returns, and nothing else
- The same `sourceId` is the same layer, and the last registered layer wins; layer count and sources show up in the debugger panel
- An overlay never survives a service shutdown, and is not cleared by a table reload — it sits on top of the table data rather than replacing it

## Missing-Key Watch

A key that neither the overlay nor its own language column can serve is recorded and warned about once per key, for QA sweeps and live mistranslation hunting:

```csharp
int distinct = LocalizationService.MissingKeyCount;        // distinct missing keys
int events   = LocalizationService.MissingKeyEventCount;   // total miss events (repeats included)
string[] keys = LocalizationService.GetMissingKeys();      // ordered snapshot
LocalizationService.ClearMissingKeys();                    // reset between QA passes
```

- Queries made before the data finishes loading do not count as misses
- An entry the overlay serves is not a miss either — the overlay is consulted before the table
- The tracker holds at most 256 distinct keys: beyond that, events keep counting but per-key recording and warnings stop (a broken config must not flood memory or the log), with a single saturation warning
- Records do not survive a service shutdown; the in-game debugger shows them live under `Profiler/Localization` → "MISSING KEYS"

## Async Preloading

Call `await LocalizationService.PreloadAsync()` during startup so the whole-table expansion lands in a window you can wait on instead of the first UI query:

```csharp
// Early in launch (e.g. ProcedurePreload)
await LocalizationService.PreloadAsync();
```

- Idempotent with in-flight deduplication: concurrent callers share one task; an already-loaded service completes immediately
- Synchronous queries during the flight degrade to "not ready" (raw IDs, no miss tracking, no duplicate source reads); once done, every registered localizer is re-injected
- The default handler yields a frame then does the sync batch; remote or very large sources override `LoadLocalizedTextBatchAsync` for a genuinely async pipeline
- A load in flight at shutdown is discarded — partial results never leak into the next session's store

## Per-Language Column Loading (opt-in)

Eager full-table residency is the default. To drop residency to "header + current column", declare the contract trio on a custom handler — and note the built-in **config-table source is already wired**: when the export splits data by language and the game-side `ConfigTableServiceHandler` self-reports `SupportsPerLanguageLocalizationLoad`, the [ConfigTable](ConfigTable.md) service switches the bridge into column mode, so a project does not need to write its own localization handler.

```csharp
public sealed class RemoteLocalizationHandler : LocalizationServiceHandler
{
    protected override bool SupportsPerLanguageLoad => true;
    protected override IReadOnlyList<Language> LoadLanguageHeader() => ...;          // language header (codes + column order)
    protected override Dictionary<string, string> LoadLanguageColumn(Language language) => ...; // key → text
}
```

- Residency = header + current column; switching fetches only the target column — if it cannot be fetched, **the switch is refused and the current language stays**
- An "empty but loaded" column (language in the header with zero entries) is fetched only once; `null` means a retryable source miss
- `GetDictionaryFromId` fetches every column on demand (the necessary cost of the "all languages" semantic — keep it off hot paths); `ReloadTexts` re-reads the header and all column caches without touching overlays
- Corruption semantics mirror the batch path (reject-batch / keep-current) — a broken payload never dislodges a working snapshot

## RTL and Per-Language Fonts

- `Language.IsRightToLeft` recognizes Arabic (ar) and Hebrew (he) through a Code allowlist — it infers nothing from language family or script; `LocalizationService.IsCurrentLanguageRightToLeft` reports the current direction. On TMP targets `TextLocalizer` applies it to `isRightToLeftText` automatically
- Two optional arrays on `TextLocalizer` swap fonts by current language column index: `m_TmpFontAssets` (`TMP_FontAsset[]`) and `m_UguiFonts` (`Font[]`) — same convention as the image/audio localizer arrays; out-of-range or empty slots keep the existing font
- UGUI `Text` and `TextMesh` have no RTL layout support (TMP only)

## Plural Entries (CLDR cardinal)

```csharp
// Table keys: quest.items#one / quest.items#few / quest.items#other (per language as needed)
string text = LocalizationService.GetPluralTextFromId("quest.items", count);
string detail = LocalizationService.GetPluralTextFromId("quest.items", count, playerName); // {0}=count {1}=playerName
```

- Key convention: base id + category suffix `id#zero|one|two|few|many|other`, falling back to `id#other` then the bare key; a full-chain miss is recorded under the base key in the missing-key watch
- Built-in rules: zh/ja/ko/vi/th/id formless; en/de/es/it/nl/pt/no/nb/sv/da/fi/el/et/bg/ca/eu/af two-form (one iff n==1); fr/hi/fa/az one for 0..1; Slavic (ru/uk/be/hr/bs/sr), Polish, Czech/Slovak, Hebrew, Romanian, Lithuanian, Latvian and the six Arabic categories; unlisted languages always resolve to `other`
- Placeholders: `{0}` is the count automatically; caller arguments start at `{1}`; the formatting culture follows the current game language

## Build-Time Channel Default Language

Give each channel package its own default language for first launch:

- Detection order: command line → editor language → saved setting → **baked channel language** → system language; a player-chosen language still wins
- CI: pass `localizationLanguage=xx` (e.g. `-CustomArgs:platform=Android;localizationLanguage=en`); the build hook bakes `Assets/Resources/LocalizationBuildConfig.asset` before packaging, and leaves the product untouched when the argument is absent
- Manual: the `Tools/Config/烘焙渠道默认语言` window bakes/clears it; names or codes are validated (an unknown value throws instead of slipping into the build)
- Only players consume the baked asset; the editor and Play-in-editor follow the editor detection chain

## Editor Preview

`TextLocalizer` / `ImageLocalizer` / `AudioLocalizer` show a "Preview" row under the ID field in the inspector. Resolution follows the same path as at runtime; only the data source depends on the state:

- **In play mode** it reads the registered service: language, translation and whatever asset the injector already holds are the real ones
- **Outside play mode** it reads the config table through the editor preview entry (`ConfigTableService.GetAllLocalizedStringsForEditor`: with no handler registered it still gets data from the instance configured in Settings), and a resource-mode address is turned into an asset via `ResourceService.LoadAssetForEditor` — no Play required
- Text localizers show the translation, or name the gap via `EPreviewResolveStatus` — "no such ID in the table" or "this language left it blank"; the key is never passed off as a translation. Resolution goes through the single entry `LocalizationService.ResolvePreviewText`
- Resource-mode image/audio localizers show `ID -> address -> asset type 'name'` and name the three mistakes that are visible right here: no asset behind the address, wrong type (the injector will refuse it), convertible type (which costs one runtime warning)
- Indexed image/audio localizers show the preview language, the array index that would be used and what sits at it (`missing` / `null reference` / asset name) — which is exactly how "the arrays were not extended after adding a language" gets caught before runtime
- Type judgements always come from the injector (the seam is `IInjectorAssetPreview`; outside play mode there is no `Awake`, so the preview builds one temporarily — it only constructs the injector and never touches the target component); the preview keeps no second type table
- Language is the inspector's editor language; when unset or not shipped it falls back to the English column, then the first one
- The preview is **not** written back into the target component (no dirty scenes, no forgotten restores); a blank cell showing its ID in the editor is exactly the information the designer wants
- The preview cache invalidates automatically on any project asset change (an `EditorApplication.projectChanged` hook, covering table re-exports) and on editor language change; call `LocalizationService.InvalidateEditorPreview()` to drop it manually

## Formatted Queries (boxing-free path)

```csharp
string price = LocalizationService.GetTextFromId("UI.Common.CreditPrice", 120);            // one arg
string line  = LocalizationService.GetTextFromId("Log.Buy.Confirmed", item, count, total);  // three args
```

- Arity 1–4 have dedicated overloads backed by `StringUtility.Format<T…>`: with ZString installed (`ZSTRING_INSTALLED`) they allocate no `object[]` and box no value types
- Without ZString, `StringUtility` falls back to `StringBuilder.AppendFormat`, **which still boxes** — "boxing-free" is conditional on ZString being installed
- Beyond four arguments use `GetTextFromId(id, params object[])` and consider splitting that entry into two keys
- A malformed placeholder in the table degrades to the unformatted source text and logs one Error instead of throwing out of the query
- **The formatting culture follows the game language** (`params` overload): a German device running the English build still prints `1.5`, not `1,5`; `GetTextFromIdLanguage` follows the queried language. The typed overloads go through ZString's fast path: primitive numbers format with invariant rules (never culture-sensitive), while custom `IFormattable` arguments use their default culture — use the `params` overload when strict culture awareness (dates/currencies) matters

## Handle-based Language Subscription

```csharp
private IDisposable _subscription;

private void OnEnable() => _subscription = LocalizationService.SubscribeLanguageChanged(Refresh);
private void OnDisable() => _subscription?.Dispose();
```

Dispatched in the same pass as the static `OnLanguageChanged` (same ordering contract: after every Localizer has been re-injected), but `Dispose` removes it immediately and **a service shutdown invalidates every handle** — the static event path has no such cleanup, so a forgotten `-=` keeps firing across shutdowns and sessions.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [ConfigTable](ConfigTable.md)
