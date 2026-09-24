# Localization 服务

> 基于 Luban 配置表的多语言服务，支持文本、图片、音频与 Timeline 的自动注入和内联解析。

`Localization` 服务通过 `LocalizationService` 静态外观访问，首次访问多语言 API 时从 Luban 配置表（经 [ConfigTable](ConfigTable.md) 的 `ConfigTableService`）懒式加载全部本地化字符串，可用语言随表自报。语言按「命令行参数 → 编辑器设置 → 本地存档 → 系统语言」的优先级决定，切换语言时会重新注入所有已注册的 `LocalizerBase` 组件，随后触发 `OnLanguageChanged`。除按 ID 取文本外，`LocalizationService.Localize` 还支持在任意字符串中内联解析 `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` 占位符。

## 核心特性

- `Language` 语言对象：携带 `Name`（枚举名）、`Code`（ISO-639-1）、`DisplayName`（本地显示名），内置 `SystemLanguage` 全量语言并支持自定义语言；内置语言与 `BuiltinLanguages` 为共享实例，不在访问时重建
- 语言检测优先级：命令行 `-force-language` → 编辑器 `LocalizationServiceSettings.EditorLanguage` → `SettingUtility` 存档 → `Application.systemLanguage`（中文未区分简繁时回落简体）；检测出的语言没进这批词条时，按回退链、再按语言表首项兜底，不会让整套界面停留在露 key 状态
- 文本查询：`GetTextFromId`（支持 `string.Format` 参数）、`GetTextFromIdLanguage`（语言传 `null` 即当前语言）、`GetDictionaryFromId`（取全部语言）、`GetAllIds`
- 缺译回退链：当前语言该词条为空或仅空白时，按 `FallbackLanguageCodes` 配置的顺序继续取译文，全链缺译才返回 ID（详见「缺译回退」）
- 内联解析：`LocalizationService.Localize` 将 `{l10n:ID}`、`{i18n:ID}`、`{g11n:ID}` 替换为本地化条目
- 组件注入：`TextLocalizer`（TextMesh / UGUI Text / TMP_Text）、`ImageLocalizer`（Image / RawImage / SpriteRenderer / Renderer 材质）、`AudioLocalizer`（AudioSource）
- 语言切换自动刷新：所有 `LocalizerBase` 在 `ChangeLanguage` 时统一重新注入（快照遍历 + 单个失败隔离），注入完成后才抛事件
- Timeline 支持：`TextLocalizerTrack` + `TextLocalizerPlayableAsset` 在时间轴片段上切换文本 ID
- Google 翻译集成：`GoogleTranslator` 调用 Google Cloud Translation v2 API 辅助翻译配表

## 核心类型

命名空间：`Moirai.Atropos.Localization`

| 类/接口 | 说明 |
|---------|------|
| `LocalizationService` | 静态外观（`[HandlerHost]`），负责加载配表文本、语言切换与 Localizer 管理；`OnLanguageChanged` 事件由外观直接暴露；`ToLanguage` / `Localize` / `ResolveLanguages` 与编辑器预览 API 住在同一类的分部实现（`LocalizationService.Helper`）中 |
| `Language` | 语言类（`IEquatable<Language>`，按 `Code` 比较）：`Name`、`Code`、`DisplayName`、`BuiltinLanguages`，支持与 `SystemLanguage` 互转；内置条目为共享只读实例 |
| `LocalizationServiceHandler` | 处理器抽象基类：查询与回退链解析、语言切换、本地化器注册；`FallbackLanguageCodes` 配置回退顺序 |
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
| `CommandLineUtility` | 命令行解析（`-force-language`），见 `Runtime/Core/Utilities` 同名类的分部定义 |

## 快速上手

```csharp
// 访问服务（静态外观，直接调用静态方法）
LocalizationService.ChangeLanguage("English");

// 本地化数据为懒式加载：首次调用任一查询/切换 API 时自动从配置表加载，无需手动初始化

// 按文本 ID 取本地化字符串（该语言缺译时按回退链取；全链缺译或 ID 不存在才原样返回 ID）
string title = LocalizationService.GetTextFromId("main_title");

// 带 string.Format 参数（表内占位符写坏时退化为未格式化原文，不会抛出）
string welcome = LocalizationService.GetTextFromId("welcome_player", "Moirai");

// 指定语言取文本 / 取某 ID 的所有语言译文（language 传 null 表示当前语言）
string english = LocalizationService.GetTextFromIdLanguage("main_title", Language.English);
Dictionary<string, string> all = LocalizationService.GetDictionaryFromId("main_title");

// ID 检查与枚举
bool has = LocalizationService.Has("main_title");
List<string> ids = LocalizationService.GetAllIds();

// 切换语言（三种方式，Name 与 Code 均不区分大小写）
LocalizationService.ChangeLanguage(Language.ChineseSimplified);
LocalizationService.ChangeLanguage("zh-Hans");
LocalizationService.ChangeLanguage(0);                 // 按已加载语言索引

// 循环切换（调试用）
string next = LocalizationService.ActivateNextLanguage();
string prev = LocalizationService.ActivatePreviousLanguage();
```

## 进阶用法

### 内联占位符解析

任意字符串中的 `{l10n:ID}`、`{i18n:ID}`、`{g11n:ID}` 标记都会被替换为对应本地化文本，适合配表文案组合：

```csharp
string hint = LocalizationService.Localize("按 {l10n:btn_confirm} 继续");
```

### 缺译回退

查询按「当前语言 → 回退链 → ID 原文」解析。译文为空或仅空白即视为缺译，因此表里留空就是「交给回退链」，不需要程序侧再判一次。

回退顺序配在处理器上（`Tools/Framework Settings` 的「[服务]本地化设置」条目，或代码赋值），填语言 `Code`：

```csharp
// 默认 { "en" }；置空即关闭回退——缺译直接露 key
LocalizationServiceSettings.LocalizationServiceHandler.FallbackLanguageCodes = new[] { "en", "zh-Hans" };
```

- 配置里认不出、或没随这批词条发行的语言会被剔除并告警一次，不会静默折成默认语言
- 首启语言（检测链结果）没随词条发行时，同样按回退链、再按语言表首项兜底，避免整套界面露 key
- 查看当前生效的回退链：`LocalizationService.FallbackChain`，或游戏内调试器 `Profiler/Localization`

### 订阅语言切换

```csharp
LocalizationService.OnLanguageChanged += language =>
{
    Debug.Log($"语言已切换: {language.DisplayName}");
    // 事件在所有 LocalizerBase 重注入完成、且当前语言已更新之后触发，
    // 这里取到的文本已经是新语言；非 LocalizerBase 管理的内容在此刷新
    titleText.text = LocalizationService.GetTextFromId("main_title");
};
```

### 组件注入

- 文本：在挂有 `TextMesh`、UGUI `Text` 或 `TMP_Text` 的物体上添加 `TextLocalizer`， Inspector 中填写 `m_TextId`；运行中可调用 `ChangeID(string textId)` 动态换文案，`Clear()` 清空
- 图片：`ImageLocalizer` 按发现顺序作用于 Image / RawImage / SpriteRenderer / Renderer；`sprites` / `textures` / `texture2Ds` 数组元素须与表自报的语言列序一致（按 `CurrentLanguageIndex` 索引），`Renderer` 走材质属性（默认 `_MainTex`，可用 `propertyName` 指定）
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

- 本地化数据来自 Luban 配置表：必须先在 `Tools/Framework Settings` 的 `LubanSettings`（「[框架]Luban 配置」）中生成并转表，否则加载失败并提示 "Failed to load localized text, generate config first!"（该错误只打一次，未就绪期间每次查询都返回 ID 原文）
- 本地化数据为懒式初始化：服务注册期（`OnInit`）不加载任何资源，首次访问多语言 API（查询/切换）时才从配置表加载——届时 `Resource` 服务必然已就绪
- 可用语言由配表自报：生成侧 `LubanHandler` 反射 `LocalizationBean` 的语言列，经 `ConfigTableServiceHandler.GetLocalizationLanguageCodes()` 交给框架解析（`LocalizationService.ResolveLanguages`），不存在可回落的全局语言注册表。`ChangeLanguage` 传入未收录语言时保持原语言不变并告警（每种语言只警告一次），不抛异常
- 词条的语言列数与自报语言数不一致会被判为数据损坏：**整批数据拒载**并报错（下标错位只会表现为「显示了别的语言」，不会报错，所以宁可不加载）
- `ToLanguage(str, onlySupported)` 中 `onlySupported` 为 `true` 时，未收录进当前批的语言会回落到默认语言 English（`LocalizationService.DefaultLanguage`）；需要区分「写错了」与「就是要默认语言」时用 `TryGetBuiltInLanguage`
- 编辑器非运行模式下 `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` 直接返回 `false`（Timeline 预览待实现）；`LocalizationService.Localize` 在非运行模式走编辑器预览直读，取不到预览数据时才原样返回
- 数据未就绪（表未加载完）时，各 Localizer **静默推迟注入**——不按缺译刷错误日志；首次加载成功触发的语言切换会把全部已注册本地化器重注入一遍。可用 `LocalizationService.IsDataLoaded`（不触发加载）区分「未就绪」与「真缺失」
- `ImageLocalizer` / `AudioLocalizer` 的数组是按语言索引注入的，配表新增语言后需同步补齐数组元素
- 全部语言列常驻内存。是否到了必须按语言拆包的程度不要凭感觉：看游戏内调试器 `Profiler/Localization` 的「数据规模」一栏（词条数、语言数、译文总字符数即常驻下限），或读 `LocalizationService.EntryCount` / `LoadedLanguageCount` / `ResidentChars`

## 运行时覆盖（热改文案）

不改表、不重出包的前提下覆盖某语言的若干词条（运营改错译、QA 强改、远程补丁走同一条路）：

```csharp
LocalizationService.SetStringOverlay("remote-ops", Language.English, new[]
{
    new KeyValuePair<string, string>("UI.Shop.Title", "Market"),
});
LocalizationService.ClearStringOverlay("remote-ops");   // 按来源撤销，不动其它来源
```

- 叠加语义：只替换指定语言下的指定 key，**未覆盖的词条照旧取表内译文**；值为空或仅空白等同于「不覆盖」
- 覆盖层参与**每一次**语言尝试（含回退链）：热改了英语，缺译回退到英语时拿到的也是改后那版
- 同名 `sourceId` 即同一层，后注册的层优先；层数与来源在调试面板「数据规模」可见
- 覆盖层**不跨服务关闭存活**，也不会被换批/重加载清空——它是叠在表数据之上的一层，不是替代品

## 缺译巡检（Missing Keys）

全链缺译（覆盖层 → 当前语言 → 回退链全部落空）的 key 会被逐个记录并告警一次（每个 key 一条 Warning），供 QA 巡检与线上漏翻排查：

```csharp
int distinct = LocalizationService.MissingKeyCount;        // 去重后的缺译 key 数
int events   = LocalizationService.MissingKeyEventCount;   // 缺译事件总数（含同一 key 重复命中）
string[] keys = LocalizationService.GetMissingKeys();      // 有序快照
LocalizationService.ClearMissingKeys();                    // 巡检回合之间重置
```

- 数据未加载期间的「查不到」不算缺译，不记录
- 回退链命中的不算缺译（最终有译文显示）
- 记录容量上限 256 个去重 key：超上限后事件计数照走、逐 key 记录与告警停摆（防异常配置刷爆内存与日志），并告警一次
- 记录不跨服务关闭存活；游戏内调试器 `Profiler/Localization` 的「MISSING KEYS」区实时可见

## 编辑器内预览（不进 Play）

`TextLocalizer` / `ImageLocalizer` / `AudioLocalizer` 的 Inspector 在 ID 字段下方显示「译文预览」一行，数据来自配置表在编辑器下的**直读**路径（`ConfigTableServiceHandler.GetLocalizedStringsForEditorPreview`），不经资源系统、不需要进 Play：

- 文本类显示解析后的译文；表内没有该 ID 时点明「表内无此 ID」
- 图/音类显示预览语言、将要取用的数组下标，以及该下标上的元素（`缺项` / `空引用` / 资源名）——「新增语言后数组没补齐」这类错位在这里当场能看见，不必等运行时
- 语言取 Inspector 里的「编辑器语言」；未设置或该语言不在表内时取英语列，再退到首列
- 预览**不写回**目标组件（不标脏场景、不留「忘了还原」的错文案），也不套用回退链：某格缺译时预览直接露 ID，那正是策划要看见的信息
- 预览缓存随项目资产变更自动失效（`EditorApplication.projectChanged` 钩子，含转表回写与编辑器语言切换），也可手动调 `LocalizationService.InvalidateEditorPreview()`

## 带参取文（不装箱路径）

```csharp
string price = LocalizationService.GetTextFromId("UI.Common.CreditPrice", 120);   // 单参，走 StringUtility.Format
string line  = LocalizationService.GetTextFromId("Log.Buy.Confirmed", item, count, total);  // 三参
```

- arity 1~4 有专用重载，底层是 `StringUtility.Format<T…>`：装了 ZString（`ZSTRING_INSTALLED`）时不建 `object[]`、不装箱值类型
- 未装 ZString 时 `StringUtility` 退化到 `StringBuilder.AppendFormat`，**那条路径仍会装箱**——「不装箱」是以装了 ZString 为前提的
- 参数超过 4 个请改用 `GetTextFromId(id, params object[])`，并把那条文案考虑拆成两条 key
- 表内占位符与参数不匹配时退化为未格式化原文并只报一次 Error，不会把异常抛到查询上
- **格式化文化跟随游戏语言**（`params` 重载）：德语设备跑英语包时数字仍显示 `1.5` 而非 `1,5`；`GetTextFromIdLanguage` 跟随被查询的语言。泛型重载经 ZString 快路径：基元数字按不变规则格式化（本就不随文化漂移），自定义 `IFormattable` 实参按其默认文化——需要严格文化感知（日期/货币）时用 `params` 重载

## 句柄式订阅语言变更

```csharp
private IDisposable _subscription;

private void OnEnable() => _subscription = LocalizationService.SubscribeLanguageChanged(Refresh);
private void OnDisable() => _subscription?.Dispose();
```

与静态 `OnLanguageChanged` 在同一次派发里触发（时序契约一致：全部 Localizer 重注入之后），区别是句柄 `Dispose` 即摘除、且**服务关闭时框架统一作废**——静态事件那条路上忘了注销的订阅者会跨关服、跨会话继续被调用。

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [ConfigTable](ConfigTable.md)
