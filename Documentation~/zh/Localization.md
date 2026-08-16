# Localization 模块

> 基于 Luban 配置表的多语言模块，支持文本、图片、音频与 Timeline 的自动注入和内联解析。

`Localization` 模块通过 `GameModule.Localization`（`ILocalizationModule`）访问，启动时从 Luban 配置表（经 [ConfigTable](ConfigTable.md) 的 `ConfigMgr`）加载全部本地化字符串并注册可用语言。语言按「命令行参数 → 编辑器设置 → 本地存档 → 系统语言」的优先级决定，切换语言时会触发 `OnLanguageChanged` 并自动重新注入所有已注册的 `LocalizerBase` 组件。除按 ID 取文本外，`LocalizationHelper.ResolveLocalizedStrings` 还支持在任意字符串中内联解析 `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` 占位符。

## 核心特性

- `Language` 语言对象：携带 `Name`（枚举名）、`Code`（ISO-639-1）、`DisplayName`（本地显示名），内置 `SystemLanguage` 全量语言并支持自定义语言
- 语言检测优先级：命令行 `-force-language` → 编辑器 `AppSettings.EditorLanguage` → `SettingUtility` 存档 → `Application.systemLanguage`（中文未区分简繁时回落简体）
- 文本查询：`GetTextFromId`（支持 `string.Format` 参数）、`GetTextFromIdLanguage`、`GetDictionaryFromId`（取全部语言）、`GetAllIds`
- 内联解析：`ResolveLocalizedStrings` 将 `{l10n:ID}`、`{i18n:ID}`、`{g11n:ID}` 替换为本地化条目
- 组件注入：`TextLocalizer`（TextMesh / UGUI Text / TMP_Text）、`ImageLocalizer`（Image / RawImage / SpriteRenderer / Renderer 材质）、`AudioLocalizer`（AudioSource）
- 语言切换自动刷新：所有 `LocalizerBase` 在 `ChangeLanguage` 时统一重新注入
- Timeline 支持：`TextLocalizerTrack` + `TextLocalizerPlayableAsset` 在时间轴片段上切换文本 ID
- Google 翻译集成：`GoogleTranslator` 调用 Google Cloud Translation v2 API 辅助翻译配表

## 核心类型

命名空间：`Moirai.Atropos.Localization`

| 类/接口 | 说明 |
|---------|------|
| `ILocalizationModule` | 模块公开接口，`GameModule.Localization` 返回类型；含 `OnLanguageChanged` 事件 |
| `LocalizationModule` | `sealed` 实现类，负责加载配表文本、语言切换与 Localizer 管理 |
| `Language` | 语言类（`IEquatable<Language>`）：`Name`、`Code`、`DisplayName`、`BuiltinLanguages`，支持与 `SystemLanguage` 互转 |
| `LocalizationHelper` | 静态辅助类：`ResolveLocalizedStrings`、`RegisterLanguageMap`、`GetAllAvailableLanguages`、`ToLanguage` |
| `LocalizerBase` | 本地化器抽象基类（MonoBehaviour）：`Prepare` 获取目标组件引用，`Localize` 执行注入 |
| `IInjector` | 注入器接口：`Inject<T1, T2>(localizedData, localizer)` |
| `TextLocalizer` | 文本本地化器，自动发现 TextMesh / Text / TMP_Text 并注入文本 |
| `ImageLocalizer` | 图片本地化器，按语言索引切换 `sprites` / `textures` / `texture2Ds` 数组 |
| `AudioLocalizer` | 音频本地化器，按语言索引切换 `clips` 数组注入 AudioSource |
| `UITextInjector` / `TMPInjector` / `TextMeshInjector` | 文本注入器，分别写入 UGUI Text、TMP_Text、TextMesh |
| `ImageInjector` / `RawImageInjector` / `SpriteRendererInjector` / `TextureInjector` | 图片注入器，分别作用于 Image、RawImage、SpriteRenderer、Renderer 材质属性 |
| `AudioSourceInjector` | 音频注入器，作用于 AudioSource |
| `TextLocalizerTrack` / `TextLocalizerPlayableAsset` / `TextLocalizerPlayableBehaviour` | Timeline 轨道与 Playable，绑定 `TextLocalizer` 按片段切换文本 |
| `GoogleTranslator` | Google Cloud Translation v2 封装：`TranslateAsync`（协程）与 `Translate`（同步，编辑器用） |
| `GoogleTranslateRequest` / `GoogleTranslateResponse` | 翻译请求/响应数据类（`Source`、`Target`、`Text`） |
| `ComponentFinder` | 静态工具：按泛型顺序在 GameObject 上查找组件 |
| `CommandLineUtility` | 命令行解析（`-force-language`），见 `Runtime/Core/Utility` 同名类的分部定义 |

## 快速上手

```csharp
// 访问模块
ILocalizationModule localization = GameModule.Localization;

// 初始化语言配置（依赖 ConfigTable 与 Resource 模块，需在配表就绪后手动调用一次）
localization.InitLanguageSettings();

// 按文本 ID 取本地化字符串（未翻译或 ID 不存在时原样返回 ID）
string title = localization.GetTextFromId("main_title");

// 带 string.Format 参数
string welcome = localization.GetTextFromId("welcome_player", "Moirai");

// 指定语言取文本 / 取某 ID 的所有语言译文
string english = localization.GetTextFromIdLanguage("main_title", Language.English);
Dictionary<string, string> all = localization.GetDictionaryFromId("main_title");

// ID 检查与枚举
bool has = localization.Has("main_title");
List<string> ids = localization.GetAllIds();

// 切换语言（三种方式，Name 与 Code 均不区分大小写）
localization.ChangeLanguage(Language.ChineseSimplified);
localization.ChangeLanguage("zh-Hans");
localization.ChangeLanguage(0);                 // 按已加载语言索引

// 循环切换（调试用）
string next = localization.ActivateNextLanguage();
string prev = localization.ActivatePreviousLanguage();
```

## 进阶用法

### 内联占位符解析

任意字符串中的 `{l10n:ID}`、`{i18n:ID}`、`{g11n:ID}` 标记都会被替换为对应本地化文本，适合配表文案组合：

```csharp
string hint = LocalizationHelper.ResolveLocalizedStrings("按 {l10n:btn_confirm} 继续");
```

### 订阅语言切换

```csharp
GameModule.Localization.OnLanguageChanged += language =>
{
    Debug.Log($"语言已切换: {language.DisplayName}");
    // 自行刷新非 LocalizerBase 管理的内容
};
```

### 组件注入

- 文本：在挂有 `TextMesh`、UGUI `Text` 或 `TMP_Text` 的物体上添加 `TextLocalizer`， Inspector 中填写 `m_TextId`；运行中可调用 `ChangeID(string textId)` 动态换文案，`Clear()` 清空
- 图片：`ImageLocalizer` 按发现顺序作用于 Image / RawImage / SpriteRenderer / Renderer；`sprites` / `textures` / `texture2Ds` 数组元素须与语言注册顺序一致（按 `CurrentLanguageIndex` 索引），`Renderer` 走材质属性（默认 `_MainTex`，可用 `propertyName` 指定）
- 音频：`AudioLocalizer` 将 `clips[CurrentLanguageIndex]` 注入 AudioSource

### Timeline 本地化

安装 Timeline 包（`TIMELINE_INSTALLED` 宏）后，创建 `TextLocalizerTrack` 轨道并绑定场景中的 `TextLocalizer`，每个 `TextLocalizerPlayableAsset` 片段设置 `textId`，播放到该片段时自动切换文本，离开片段时清空。

### Google 翻译辅助

```csharp
var translator = new GoogleTranslator(authFile); // authFile 为含 API Key 的 TextAsset
var request = new GoogleTranslateRequest(Language.English, Language.ChineseSimplified, "Hello");
IEnumerator routine = translator.TranslateAsync(request,
    onCompleted: e => Debug.Log(e.Responses[0].TranslatedText),
    onError:   e => Debug.Log(e.Message));
```

## 注意事项

- 本地化数据来自 Luban 配置表：必须先在 `Tools/Settings/ConfigTableSettings` 中生成并转表，否则加载失败并提示 "Failed to load localized text, generate config first!"
- `InitLanguageSettings` 依赖 `Resource` 模块加载配表资源，不要在 `OnInit` 阶段（资源未就绪）调用
- 可用语言列表来自配表中 `LocalizedBean` 的字段注册（`LocalizationHelper.RegisterLanguageMap`），`ChangeLanguage` 传入未注册语言会抛 `KeyNotFoundException`
- `ToLanguage(str, onlySupported)` 中 `onlySupported` 为 `true` 时，未注册语言会回落到默认语言 English（`LocalizationHelper.defaultLanguage`）
- 编辑器非运行模式下 `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` 直接返回 `false`（Timeline 预览待实现），`ResolveLocalizedStrings` 也会原样返回
- `ImageLocalizer` / `AudioLocalizer` 的数组是按语言索引注入的，配表新增语言后需同步补齐数组元素

---
[« 返回主 README](../README.md) · [ConfigTable](ConfigTable.md)
