# Localization Service

> Multilingual service based on Luban configuration tables, supporting automatic injection and inline parsing of text, images, audio, and Timeline.

The `Localization` service is accessed via the `LocalizationService` static facade. It lazily loads all localized strings from the Luban configuration table (via `ConfigTableService` from [ConfigTable](ConfigTable.md)) and registers available languages on the first access to any multilingual API. The language is determined by priority: "command-line argument -> editor setting -> saved setting -> system language". When switching languages, it re-injects all registered `LocalizerBase` components and only then raises `OnLanguageChanged`. In addition to retrieving text by ID, `LocalizationService.Localize` supports inline parsing of `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` placeholders in any string.

## Core Features

- `Language` object: carries `Name` (enum name), `Code` (ISO-639-1), `DisplayName` (localized display name), includes full `SystemLanguage` support and supports custom languages; built-in entries and `BuiltinLanguages` are shared instances rather than rebuilt on each access
- Language detection priority: command-line `-force-language` -> editor `AppSettings.EditorLanguage` -> `SettingUtility` saved setting -> `Application.systemLanguage` (falls back to Simplified Chinese when Chinese is not distinguished between Simplified/Traditional). When the detected language is not part of the loaded entries, the fallback chain and then the first loaded language take over, so the UI never stays stuck on raw keys
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
| `LocalizationService` | Static facade (`[HandlerHost]`) responsible for loading config table text, language switching, and Localizer management; the `OnLanguageChanged` event is exposed directly on the facade; `RegisterLanguageMap` / `GetAllAvailableLanguages` / `ToLanguage` / `Localize` live on the same class's partial implementation |
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
| `CommandLineUtility` | Command-line parsing (`-force-language`), see the partial definition of the same class in `Runtime/Core/Utility` |

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

The order is configured on the handler (the Localization entry under `Tools/Settings`, or assigned in code) and takes language `Code` values:

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
- Image: `ImageLocalizer` acts on Image / RawImage / SpriteRenderer / Renderer in discovery order; `sprites` / `textures` / `texture2Ds` array elements must match the language registration order (indexed by `CurrentLanguageIndex`), `Renderer` uses material properties (default `_MainTex`, can be specified via `propertyName`)
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

- Localization data comes from Luban configuration tables: must generate and export tables in `Tools/Settings/ConfigTableSettings` first, otherwise loading fails with "Failed to load localized text, generate config first!" (logged once; while not ready every query returns the raw ID)
- Localized data is lazily initialized: no resources are loaded during service registration (`OnInit`); data is loaded from config tables on the first access to any multilingual API (query/switch) — by then the `Resource` service is guaranteed to be ready
- The list of available languages comes from field registration of `LocalizationBean` in the config table (`LocalizationService.RegisterLanguageMap`); calling `ChangeLanguage` with an unregistered language keeps the current language and warns once per language rather than throwing
- A mismatch between an entry's language column count and the registered language count marks the dataset corrupt: **the whole batch is refused** and an error is logged (a shifted index only shows up as "the wrong language is displayed", never as an error, which is exactly why nothing is loaded)
- In `ToLanguage(str, onlySupported)`, when `onlySupported` is `true`, unregistered languages fall back to the default language English (`LocalizationService.defaultLanguage`); use `TryGetBuiltInLanguage` to tell "typo" apart from "I do want the default"
- In the editor's non-play mode, `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` directly return `false` (Timeline preview pending implementation), and `LocalizationService.Localize` also returns the input as-is
- The arrays of `ImageLocalizer` / `AudioLocalizer` are injected by language index; after adding a new language to the config table, array elements must be supplemented accordingly
- All language columns stay resident in memory. Don't guess whether it is time to split packs per language: read the "DATA FOOTPRINT" section of the in-game debugger (`Profiler/Localization`) — entry count, language count and total text length (a lower bound on the resident size) — or `LocalizationService.EntryCount` / `LoadedLanguageCount` / `TotalTextLength`

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [ConfigTable](ConfigTable.md)
