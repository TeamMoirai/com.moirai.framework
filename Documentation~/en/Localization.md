# Localization Service

> Multilingual service based on Luban configuration tables, supporting automatic injection and inline parsing of text, images, audio, and Timeline.

The `Localization` service is accessed via the `LocalizationService` static facade. It lazily loads all localized strings from the Luban configuration table (via `ConfigTableService` from [ConfigTable](ConfigTable.md)) and takes the available languages from the table's own self-report on the first access to any multilingual API. The language is determined by priority: "command-line argument -> editor setting -> saved setting -> system language". When switching languages, it re-injects all registered `LocalizerBase` components and only then raises `OnLanguageChanged`. In addition to retrieving text by ID, `LocalizationService.Localize` supports inline parsing of `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` placeholders in any string.

## Core Features

- `Language` object: carries `Name` (enum name), `Code` (ISO-639-1), `DisplayName` (localized display name), includes full `SystemLanguage` support and supports custom languages; built-in entries and `BuiltinLanguages` are shared instances rather than rebuilt on each access
- Language detection priority: command-line `-force-language` -> editor `LocalizationServiceSettings.EditorLanguage` -> `SettingUtility` saved setting -> `Application.systemLanguage` (falls back to Simplified Chinese when Chinese is not distinguished between Simplified/Traditional). When the detected language is not part of the loaded entries, the fallback chain and then the first loaded language take over, so the UI never stays stuck on raw keys
- Text querying: `GetTextFromId` (supports `string.Format` parameters), `GetTextFromIdLanguage` (pass `null` for the current language), `GetDictionaryFromId` (retrieves all languages), `GetAllIds`
- Missing-translation fallback: when the current language's entry is empty or whitespace-only, text is taken in `FallbackLanguageCodes` order; the ID is returned only when the whole chain is empty (see "Missing-Translation Fallback")
- Inline parsing: `LocalizationService.Localize` replaces `{l10n:ID}`, `{i18n:ID}`, `{g11n:ID}` with localized entries
- Component injection: `TextLocalizer` (TextMesh / UGUI Text / TMP_Text), `ImageLocalizer` (Image / RawImage / SpriteRenderer / Renderer material), `AudioLocalizer` (AudioSource)
- Auto-refresh on language switch: all `LocalizerBase` instances are re-injected on `ChangeLanguage` (snapshot iteration with per-instance fault isolation) before the event is raised
- Timeline support: `TextLocalizerTrack` + `TextLocalizerPlayableAsset` switches text IDs on Timeline clips
- Google Translate integration: `GoogleTranslator` calls Google Cloud Translation v2 API to assist with translating configuration tables

## Core Types

Namespace: `Moirai.Atropos.Localization`

| Class/Interface | Description |
|----------------|-------------|
| `LocalizationService` | Static facade (`[HandlerHost]`) responsible for loading config table text, language switching, and Localizer management; the `OnLanguageChanged` event is exposed directly on the facade; `ToLanguage` / `Localize` / `ResolveLanguages` and the editor-preview API live in the same class's partial implementation (`LocalizationService.Helper`) |
| `Language` | Language class (`IEquatable<Language>`, compared by `Code`): `Name`, `Code`, `DisplayName`, `BuiltinLanguages`, supports conversion to/from `SystemLanguage`; built-in entries are shared read-only instances |
| `LocalizationServiceHandler` | Abstract handler base class: querying with fallback resolution, language switching, localizer registration; `FallbackLanguageCodes` configures the fallback order |
| `LocalizerBase` | Abstract base class for localizers (MonoBehaviour): `Prepare` gets the target component reference, `Localize` performs injection |
| `IInjector` | Injector interface: `Inject<T1, T2>(localizedData, localizer)` |
| `TextLocalizer` | Text localizer, automatically discovers TextMesh / Text / TMP_Text and injects text |
| `ImageLocalizer` | Image localizer, switches between `sprites` / `textures` / `texture2Ds` arrays by language index |
| `AudioLocalizer` | Audio localizer, switches `clips` array by language index and injects into AudioSource |
| `UITextInjector` / `TMPInjector` / `TextMeshInjector` | Text injectors, writing to UGUI Text, TMP_Text, TextMesh respectively |
| `ImageInjector` / `RawImageInjector` / `SpriteRendererInjector` / `TextureInjector` | Image injectors, targeting Image, RawImage, SpriteRenderer, Renderer material properties respectively |
| `AudioSourceInjector` | Audio injector, targeting AudioSource |
| `TextLocalizerTrack` / `TextLocalizerPlayableAsset` / `TextLocalizerPlayableBehaviour` | Timeline track and Playable, binds `TextLocalizer` to switch text on clips |
| `GoogleTranslator` | Google Cloud Translation v2 wrapper: `TranslateAsync` (coroutine) and `Translate` (synchronous, editor use) |
| `GoogleTranslateRequest` / `GoogleTranslateResponse` | Translation request/response data classes (`Source`, `Target`, `Text`) |
| `ComponentFinder` | Static utility: finds components on a GameObject by generic type order |
| `CommandLineUtility` | Command-line parsing (`-force-language`), see the partial definition of the same class in `Runtime/Core/Utilities` |

## Quick Start

```csharp
// Access the service (static facade, call static methods directly)
LocalizationService.ChangeLanguage("English");

// Localized data is lazily loaded: it is automatically loaded from config tables on the first call
// to any query/switch API — no manual initialization required

// Get localized string by text ID (falls back along the chain when this language is untranslated;
// the ID is returned as-is only when the whole chain is empty or the ID does not exist)
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

### Missing-Translation Fallback

Queries resolve as "current language -> fallback chain -> ID". An entry that is empty or whitespace-only counts as untranslated, so leaving a cell blank in the table means "hand it to the chain" — no extra check on the code side.

The order is configured on the handler (the localization entry under `Tools/Framework Settings`, i.e. `LocalizationServiceSettings`, or assigned in code) and takes language `Code` values:

```csharp
// Defaults to { "en" }; set it empty to disable fallback — missing translations then expose the key
LocalizationServiceSettings.LocalizationServiceHandler.FallbackLanguageCodes = new[] { "en", "zh-Hans" };
```

- Codes that cannot be resolved, or that are not part of the loaded entries, are dropped with a warning instead of silently becoming the default language
- The first language picked at startup follows the same chain when the detected language is not shipped with these entries
- Inspect the effective chain via `LocalizationService.FallbackChain`, or in the in-game debugger under `Profiler/Localization`

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

### Google Translate Assistance

```csharp
var translator = new GoogleTranslator(authFile); // authFile is a TextAsset containing the API Key
var request = new GoogleTranslateRequest(Language.English, Language.ChineseSimplified, "Hello");
IEnumerator routine = translator.TranslateAsync(request,
    onCompleted: e => Debug.Log(e.Responses[0].TranslatedText),
    onError:   e => Debug.Log(e.Message));
```

## Notes

- Localization data comes from Luban configuration tables: the Config project must first be generated and exported from the `LubanSettings` entry ("[框架]Luban 配置") under `Tools/Framework Settings`, otherwise loading fails with "Failed to load localized text, generate config first!" (logged once; while not ready every query returns the raw ID)
- Localized data is lazily initialized: no resources are loaded during service registration (`OnInit`); data is loaded from config tables on the first access to any multilingual API (query/switch) — by then the `Resource` service is guaranteed to be ready
- The list of available languages is self-reported by the config table: the generated `LubanHandler` reflects `LocalizationBean`'s language columns and hands them to the framework through `ConfigTableServiceHandler.GetLocalizationLanguageCodes()`, which resolves them via `LocalizationService.ResolveLanguages` — there is no global language registry to fall back to. Calling `ChangeLanguage` with a language that is not part of the batch keeps the current language and warns once per language rather than throwing
- A mismatch between an entry's language column count and the self-reported language count marks the dataset corrupt: **the whole batch is refused** and an error is logged (a shifted index only shows up as "the wrong language is displayed", never as an error, which is exactly why nothing is loaded)
- In `ToLanguage(str, onlySupported)`, when `onlySupported` is `true`, a language outside the loaded batch falls back to the default language English (`LocalizationService.defaultLanguage`); use `TryGetBuiltInLanguage` to tell "typo" apart from "I do want the default"
- In the editor's non-play mode, `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` directly return `false` (Timeline preview pending implementation); `LocalizationService.Localize` resolves through the editor preview there and only returns the input as-is when preview data is unavailable
- The arrays of `ImageLocalizer` / `AudioLocalizer` are injected by language index; after adding a new language to the config table, array elements must be supplemented accordingly
- All language columns stay resident in memory. Don't guess whether it is time to split packs per language: read the "DATA FOOTPRINT" section of the in-game debugger (`Profiler/Localization`) — entry count, language count and total text length (a lower bound on the resident size) — or `LocalizationService.EntryCount` / `LoadedLanguageCount` / `ResidentChars`

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
- The overlay participates in **every** language attempt, fallback included — patching English also changes what a missing French entry resolves to via the fallback chain
- The same `sourceId` is the same layer, and the last registered layer wins; layer count and sources show up in the debugger panel
- An overlay never survives a service shutdown, and is not cleared by a table reload — it sits on top of the table data rather than replacing it

## Editor Preview (no Play required)

`TextLocalizer` / `ImageLocalizer` / `AudioLocalizer` show a "Preview" row under the ID field in the inspector, fed by the config table's **direct editor read** (`ConfigTableServiceHandler.GetLocalizedStringsForEditorPreview`) — no resource system, no play mode:

- Text localizers show the resolved translation, or "no such ID in table"
- Image/audio localizers show the preview language, the array index that would be used and what sits at it (`missing` / `null reference` / asset name) — which is exactly how "the arrays were not extended after adding a language" gets caught before runtime
- Language is the inspector's editor language; when unset or not shipped it falls back to the English column, then the first one
- The preview is **not** written back into the target component (no dirty scenes, no forgotten restores) and deliberately skips the fallback chain: a blank cell showing its ID in the editor is information for the designer
- Cache invalidates when the editor language changes; call `LocalizationService.InvalidateEditorPreview()` after a re-export

## Formatted Queries (boxing-free path)

```csharp
string price = LocalizationService.GetTextFromId("UI.Common.CreditPrice", 120);            // one arg
string line  = LocalizationService.GetTextFromId("Log.Buy.Confirmed", item, count, total);  // three args
```

- Arity 1–4 have dedicated overloads backed by `StringUtility.Format<T…>`: with ZString installed (`ZSTRING_INSTALLED`) they allocate no `object[]` and box no value types
- Without ZString, `StringUtility` falls back to `StringBuilder.AppendFormat`, **which still boxes** — "boxing-free" is conditional on ZString being installed
- Beyond four arguments use `GetTextFromId(id, params object[])` and consider splitting that entry into two keys
- A malformed placeholder in the table degrades to the unformatted source text and logs one Error instead of throwing out of the query

## Handle-based Language Subscription

```csharp
private IDisposable _subscription;

private void OnEnable() => _subscription = LocalizationService.SubscribeLanguageChanged(Refresh);
private void OnDisable() => _subscription?.Dispose();
```

Dispatched in the same pass as the static `OnLanguageChanged` (same ordering contract: after every Localizer has been re-injected), but `Dispose` removes it immediately and **a service shutdown invalidates every handle** — the static event path has no such cleanup, so a forgotten `-=` keeps firing across shutdowns and sessions.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [ConfigTable](ConfigTable.md)
