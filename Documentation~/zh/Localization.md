# Localization 服务

> 基于 Luban 配置表的多语言服务，支持文本、图片、音频与 Timeline 的自动注入和内联解析。

`Localization` 服务通过 `LocalizationService` 静态外观访问，首次访问多语言 API 时从 Luban 配置表（经 [ConfigTable](ConfigTable.md) 的 `ConfigTableService`）懒式加载全部本地化字符串，可用语言随表自报。语言按「命令行参数 → 编辑器设置 → 本地存档 → 系统语言」的优先级决定，切换语言时会重新注入所有已注册的 `LocalizerBase` 组件，随后触发 `OnLanguageChanged`。除按 ID 取文本外，`LocalizationService.Localize` 还支持在任意字符串中内联解析 `{l10n:ID}` / `{i18n:ID}` / `{g11n:ID}` 占位符。

## 核心特性

- `Language` 语言对象：携带 `Name`（枚举名）、`Code`（ISO-639-1）、`DisplayName`（本地显示名），内置 `SystemLanguage` 全量语言并支持自定义语言；内置语言与 `BuiltinLanguages` 为共享实例，不在访问时重建
- 语言检测优先级：命令行 `-force-language` → 编辑器 `LocalizationServiceSettings.EditorLanguage` → `SettingUtility` 存档 → `Application.systemLanguage`（中文未区分简繁时回落简体）；检测出的语言没进这批词条时按语言表首项兜底，不会让整套界面停留在露 key 状态
- 文本查询：`GetTextFromId`（支持 `string.Format` 参数）、`GetTextFromIdLanguage`（语言传 `null` 即当前语言）、`GetDictionaryFromId`（取全部语言）、`GetAllIds`
- 缺译即露 key：当前语言该词条为空或仅空白时直接返回 ID，不借别的语言顶上（详见「缺译即露 key」）
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
| `LocalizationServiceHandler` | 处理器抽象基类：按语言取值解析、语言切换、本地化器注册 |
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

// 按文本 ID 取本地化字符串（该语言缺译或 ID 不存在时原样返回 ID）
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

### 缺译即露 key

查询按「覆盖层 → 当前语言 → ID 原文」解析。译文为空或仅空白即视为缺译，**没有任何跨语言兜底**：直接返回 ID，并计入缺译巡检。

取向是「表必须填全，漏翻要看得见」——拿另一种语言的译文顶上一格，界面确实不露 key 了，策划与 QA 却再也不会发现这一格没翻。

- 首启语言（检测链结果）没随这批词条发行时按语言表首项兜底，避免整套界面从第一条查询起就露 key
- 按语言列模式（见「按语言列加载」）下这条取向更是必然：别的语言那一整列压根没装载，无从兜底

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
- 可用语言由配表自报：多语言按语言分份导出后，语言不再从生成代码的 bean 字段名反推，而是取自转表期生成的 `L10nLanguages.Codes`，经 `ConfigTableServiceHandler.GetLocalizationLanguageCodes()` 交给框架解析（`LocalizationService.ResolveLanguages`），不存在可回落的全局语言注册表。`ChangeLanguage` 传入未收录语言时保持原语言不变并告警（每种语言只警告一次），不抛异常
- 词条的语言列数与自报语言数不一致会被判为数据损坏：**整批数据拒载**并报错（下标错位只会表现为「显示了别的语言」，不会报错，所以宁可不加载）
- `ToLanguage(str, onlySupported)` 中 `onlySupported` 为 `true` 时，未收录进当前批的语言会回落到默认语言 English（`LocalizationService.DefaultLanguage`）；需要区分「写错了」与「就是要默认语言」时用 `TryGetBuiltInLanguage`
- 编辑器非运行模式下 `TextLocalizer.ChangeID` / `ImageLocalizer.ChangeID` 直接返回 `false`（编辑态没有后端可取资产，写进组件还会把场景标脏）；要看效果用组件 Inspector 的预览行，`LocalizationService.Localize` 在非运行模式也走同一条预览直读，取不到时才原样返回
- 数据未就绪（表未加载完）时，各 Localizer **静默推迟注入**——不按缺译刷错误日志；首次加载成功触发的语言切换会把全部已注册本地化器重注入一遍。可用 `LocalizationService.IsDataLoaded`（不触发加载）区分「未就绪」与「真缺失」
- `ImageLocalizer` / `AudioLocalizer` 的数组是按语言索引注入的，配表新增语言后需同步补齐数组元素
- 默认整批加载、全部语言列常驻内存（词条在存储层为行表 + 扁平数组，比「每词条一个 List」省下一半容器对象）。要降到「语言头 + 当前列」常驻，自定义处理器实现 `SupportsPerLanguageLoad` 三件套即可；默认的**配置表数据源已自动接好**——转表按语言分份、且游戏侧 `ConfigTableServiceHandler` 自报 `SupportsPerLanguageLocalizationLoad` 时，[ConfigTable](ConfigTable.md) 服务就切到列模式，项目侧无需再写一个本地化处理器

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
- 覆盖层先于表内译文、且只作用于被指定的那一门语言：热改了英语，改的是英语的查询结果，不替别的语言兜底
- 同名 `sourceId` 即同一层，后注册的层优先；层数与来源在调试面板「数据规模」可见
- 覆盖层**不跨服务关闭存活**，也不会被换批/重加载清空——它是叠在表数据之上的一层，不是替代品

## 缺译巡检（Missing Keys）

覆盖层与当前语言都给不出译文的 key 会被逐个记录并告警一次（每个 key 一条 Warning），供 QA 巡检与线上漏翻排查：

```csharp
int distinct = LocalizationService.MissingKeyCount;        // 去重后的缺译 key 数
int events   = LocalizationService.MissingKeyEventCount;   // 缺译事件总数（含同一 key 重复命中）
string[] keys = LocalizationService.GetMissingKeys();      // 有序快照
LocalizationService.ClearMissingKeys();                    // 巡检回合之间重置
```

- 数据未加载期间的「查不到」不算缺译，不记录
- 覆盖层给出的译文不算缺译（覆盖层先于表内译文被查到）
- 记录容量上限 256 个去重 key：超上限后事件计数照走、逐 key 记录与告警停摆（防异常配置刷爆内存与日志），并告警一次
- 记录不跨服务关闭存活；游戏内调试器 `Profiler/Localization` 的「MISSING KEYS」区实时可见

## 异步预加载

启动期推荐先 `await LocalizationService.PreloadAsync()`——把「首查询承担整表展开」挪到可等待的启动窗口：

```csharp
// 启动流程早期（如 ProcedurePreload）
await LocalizationService.PreloadAsync();
```

- 幂等 + 在途去重：并发调用共享同一任务；已加载立即完成
- 在途期间同步查询按「未就绪」降级（返回 ID 原文、不计缺译、不重复取源），完成后自动重注入全部本地化器
- 默认实现让出一帧后走同步批；大数据源（远程词库等）自定义处理器覆写 `LoadLocalizedTextBatchAsync` 即得道真异步
- 加载在途时服务关服，未完成的结果会被丢弃，不会写进下一次会话

## 按语言列加载（可选契约）

整批常驻对绝大多数项目够用。词条量级到了按列常驻更合理时（`ResidentChars` 量化判据），自定义处理器声明三件套即启用；配置表这条默认数据源不需要你动处理器——它按语言分份导出后由 `ConfigTableServiceHandler` 自报开关，桥处理器自动转列模式：

```csharp
public sealed class RemoteLocalizationHandler : LocalizationServiceHandler
{
    protected override bool SupportsPerLanguageLoad => true;
    protected override IReadOnlyList<Language> LoadLanguageHeader() => ...;          // 语言头（可用语言与列序）
    protected override Dictionary<string, string> LoadLanguageColumn(Language language) => ...; // key → 译文
}
```

- 常驻 = 语言头 + 当前语言列；切换语言只装载目标列——目标列取不到源**拒绝切换并保持当前语言**
- 空列（语言在头内但暂无词条）只装载一次；返回 `null` 视为可重试的缺源
- `GetDictionaryFromId` 会按需装齐全列（「全语言」语义的必要代价，热路径请勿使用）；`ReloadTexts` 重取语言头与列缓存，覆盖层不清空
- 批（整列拒载）与列（缺列保当前）的损坏语义一致：宁可停在旧可用状态，不把坏数据混进运行态

## RTL 与按语言字体

- `Language.IsRightToLeft` 按 Code 白名单识别阿拉伯语（ar）与希伯来语（he）两枚，不从语族或文字系统推断；外观 `LocalizationService.IsCurrentLanguageRightToLeft` 读取当前语言方向。`TextLocalizer` 在目标为 TMP 时自动把该值写入 `isRightToLeftText`
- `TextLocalizer` 两个可选数组按「当前语言列下标」换字体：`m_TmpFontAssets`（TMP_FontAsset[]）与 `m_UguiFonts`（Font[]）——与 `ImageLocalizer`/`AudioLocalizer` 数组同一约定，越界或空元素保持原字体
- UGUI Text 与 TextMesh 无 RTL 排版能力（只有 TMP 这条路）

## 复数词条（CLDR cardinal）

```csharp
// 表内：quest.items#one / quest.items#few / quest.items#other（按语言配需要的形态）
string text = LocalizationService.GetPluralTextFromId("quest.items", count);
string detail = LocalizationService.GetPluralTextFromId("quest.items", count, playerName); // {0}=数量 {1}=playerName
```

- 词条约定：基础 ID + 类别后缀 `id#zero|one|two|few|many|other`，回落 `id#other` → 裸 key；全链落空按基础 key 计入缺译巡检
- 规则内置：中/日/韩/越/泰/印尼无形态，英/德/西/意/荷/葡/挪/瑞/丹/芬/希/两形态（one 当且仅当 n==1），法/印地/波斯/阿塞拜疆 0..1 为 one，斯拉夫族（ru/uk/be/hr/bs/sr）、波兰、捷克/斯洛伐克、犹太（he）、罗马尼亚、立陶宛、拉脱维亚、阿拉伯六形态；未收录语言一律「仅 other」
- 占位符约定：`{0}` 自动放数量，调用方参数从 `{1}` 起；格式化文化跟随当前语言

## 构建期渠道默认语言

多渠道出包各带默认语言（首启未改语言的玩家落在渠道语言）：

- 检测链顺序：命令行 → 编辑器语言 → 本地存档 → **烘焙渠道语言** → 系统语言；玩家改过语言后存档仍优先
- CI：出包参数加 `localizationLanguage=xx`（如 `-CustomArgs:platform=Android;localizationLanguage=en`），构建钩子自动烘焙 `Assets/Resources/LocalizationBuildConfig.asset`；未给参数则不动产物
- 手动：`Tools/Config/烘焙渠道默认语言` 窗口烘焙/清除；语言填 Name 或 Code（非法值直接抛异常不让坏值进包）
- 仅播放器消费该资产；编辑器与 Play 预览按编辑器设置链走，不受烘焙影响

## 编辑器内预览

`TextLocalizer` / `ImageLocalizer` / `AudioLocalizer` 的 Inspector 在 ID 字段下方显示一行「译文预览 [Preview]」，解析路径与运行期同源，只是数据源按状态分两条：

- **播放态**读已注册的服务：语言、译文与注入器已经取到的资产都是真值
- **非播放态**读配置表的编辑器预览入口（`ConfigTableService.GetAllLocalizedStringsForEditor`：运行期没注册处理器时，经 Settings 里配置的那份实例取数），资源模式那条地址再经 `ResourceService.LoadAssetForEditor` 解析成资产，不需要进 Play
- 文本类显示译文；取不到时按 `EPreviewResolveStatus` 分档点明「表内无此 ID」或「该语言留空」，不拿 key 冒充译文。解析入口是 `LocalizationService.ResolvePreviewText`（唯一）
- 图/音的资源模式显示 `ID → 地址 → 资产类型 '名字'`，并点名三种在编辑器里就能看出来的错：地址指向的资产取不到、类型不符（注入器会拒绝）、类型可自动转换（运行期会为此告警一次）
- 图/音的索引模式显示预览语言、将要取用的数组下标以及该下标上的元素（`缺项` / `空引用` / 资源名）——「新增语言后数组没补齐」这类错位在这里当场能看见，不必等运行时
- 类型判据始终向注入器要（接缝是 `IInjectorAssetPreview`；非播放态没有 `Awake`，预览会临时补建注入器，只建对象、不碰目标组件），预览侧不留第二份类型对照表
- 语言取 Inspector 里的「编辑器语言」；未设置或该语言不在表内时取英语列，再退到首列
- 预览**不写回**目标组件（不标脏场景、不留「忘了还原」的错文案）；某格缺译时预览直接露 ID，那正是策划要看见的信息
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
