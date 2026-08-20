# Localization Service

> Multilingual service based on Luban configuration tables, supporting automatic injection and inline parsing of text, images, audio, and Timeline.

The `Localization` service is accessed via `GameApp.Localization` (`ILocalizationService`). On startup, it loads all localized strings from the Luban configuration table (via `ConfigMgr` from [ConfigTable](ConfigTable.md)) and registers available languages. The language is determined by priority: "command-line argument -> editor setting -> local存档 -> system language". When switching languages, it triggers `OnLanguageChanged` and automatically re-injects all registered `LocalizerBase` components. In addition to retrieving text by ID, `LocalizationHelper.ResolveLocalizedStrings` supports inline parsing of `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` placeholders in any string.

## Core Features

- `Language` object: carries `Name` (enum name), `Code` (ISO-639-1), `DisplayName` (localized display name), includes full `SystemLanguage` support and supports custom languages
- Language detection priority: command-line `-force-language` -> editor `AppSettings.EditorLanguage` -> `SettingUtility`存档 -> `Application.systemLanguage` (falls back to Simplified Chinese when Chinese is not distinguished between Simplified/Traditional)
- Text querying: `GetTextFromId` (supports `string.Format` parameters), `GetTextFromIdLanguage`, `GetDictionaryFromId` (retrieves all languages), `GetAllIds`
- Inline parsing: `ResolveLocalizedStrings` replaces `{l10n:ID}`, `{i18n:ID}`, `{g11n:ID}` with localized entries
- Component injection: `TextLocalizer` (TextMesh / UGUI Text / TMP_Text), `ImageLocalizer` (Image / RawImage / SpriteRenderer / Renderer material), `AudioLocalizer` (AudioSource)
- Auto-refresh on language switch: All `LocalizerBase` instances are uniformly re-injected when `ChangeLanguage` is called
- Timeline support: `TextLocalizerTrack` + `TextLocalizerPlayableAsset` switches text IDs on Timeline clips
- Google Translate integration: `GoogleTranslator` calls Google Cloud Translation v2 API to assist with translating configuration tables

## Core Types

Namespace: `Moirai.Atropos.Localization`

| Class/Interface | Description |
|----------------|-------------|
| `ILocalizationService` | Service public interface, return type of `GameApp.Localization`; includes `OnLanguageChanged` event |
| `LocalizationService` | `sealed` implementation class, responsible for loading config table text, language switching, and Localizer management |
| `Language` | Language class (`IEquatable<Language>`): `Name`, `Code`, `DisplayName`, `BuiltinLanguages`, supports conversion to/from `SystemLanguage` |
| `LocalizationHelper` | Static helper class: `ResolveLocalizedStrings`, `RegisterLanguageMap`, `GetAllAvailableLanguages`, `ToLanguage` |
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
// Access the service
ILocalizationService localization = GameApp.Localization;

// Initialize language configuration (depends on ConfigTable and Resource services, must be called manually once after config tables are ready)
localization.InitLanguageSettings();

// Get localized string by text ID (returns the ID as-is if untranslated or ID does not exist)
string title = localization.GetTextFromId("main_title");

// With string.Format parameters
string welcome = localization.GetTextFromId("welcome_player", "Moirai");

// Get text for a specific language / get all language translations for an ID
string english = localization.GetTextFromIdLanguage("main_title", Language.English);
Dictionary<string, string> all = localization.GetDictionaryFromId("main_title");

// ID check and enumeration
bool has = localization.Has("main_title");
List<string> ids = localization.GetAllIds();

// Switch language (three methods, Name and Code are case-insensitive)
localization.ChangeLanguage(Language.ChineseSimplified);
localization.ChangeLanguage("zh-Hans");
localization.ChangeLanguage(0);                 // By loaded language index

// Cycle through languages (debug use)
string next = localization.ActivateNextLanguage();
string prev = localization.ActivatePreviousLanguage();
```

## Advanced Usage

### Inline Placeholder Parsing

Markers like `{l10n:ID}`, `{i18n:ID}`, `{g11n:ID}` in any string will be replaced with the corresponding localized text, suitable for config table text composition:

```csharp
string hint = LocalizationHelper.ResolveLocalizedStrings("Press {l10n:btn_confirm} to continue");
```

### Subscribing to Language Switching

```csharp
GameApp.Localization.OnLanguageChanged += language =>
{
    Debug.Log($"Language switched: {language.DisplayName}");
    // Manually refresh content not managed by LocalizerBase
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

- Localization data comes from Luban configuration tables: must generate and export tables in `Tools/Settings/ConfigTableSettings` first, otherwise loading fails with "Failed to load localized text, generate config first!"
- `InitLanguageSettings` depends on the `Resource` service to load config table assets; do not call it during the `OnInit` phase (resources are not ready)
- The list of available languages comes from field registration of `LocalizedBean` in the config table (`LocalizationHelper.RegisterLanguageMap`); calling `ChangeLanguage` with an unregistered language will throw `KeyNotFoundException`
- In `ToLanguage(str, onlySupported)`, when `onlySupported` is `true`, unregistered languages fall back to the default language English (`LocalizationHelper.defaultLanguage`)
- In the editor's non-play mode, `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` directly return `false` (Timeline preview pending implementation), and `ResolveLocalizedStrings` also returns the input as-is
- The arrays of `ImageLocalizer` / `AudioLocalizer` are injected by language index; after adding a new language to the config table, array elements must be supplemented accordingly

---
[« Back to Main README](../../README_EN.md) · [ConfigTable](ConfigTable.md)