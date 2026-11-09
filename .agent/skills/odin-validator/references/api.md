# Odin Validator —— API 事实表（4.0.2.4）

**来源与可信度**：以下内容由反射**本机安装的** `Assets/Plugins/Sirenix/Assemblies/{Sirenix.OdinValidator.Editor, Sirenix.OdinInspector.Editor, Sirenix.OdinInspector.Attributes}.dll`（Odin Inspector & Validator **4.0.2.4**）逐类型枚举得出，并与项目内 `Odin Validator/Editor/{Config,Profiles}/*.asset` 交叉核对。**未**依赖官方站点（其为 SPA，取不到正文）。

用途：回答"某某成员/枚举到底存不存在、签名是什么"。写代码的骨架请看 [patterns.md](patterns.md)。

**加载提示**：这三份程序集含 netstandard 2.1 依赖，Windows PowerShell 5.1（.NET Framework）加载会缺 43+ 个类型（`RootObjectValidator<T>`、`SceneValidator`、`AutomationConfig`、`GlobalValidationConfig`、`RuleConfig`、`ValidationProfile` 等恰好都在其中）。要重新反射枚举，用 Unity 自带的 Mono：

```
D:\Unity\6000.3.26f1\Editor\Data\MonoBleedingEdge\bin\mcs.bat    # 编译
D:\Unity\6000.3.26f1\Editor\Data\MonoBleedingEdge\bin\mono.exe   # 运行
```

并把 `UnityEngine.CoreModule` / `UnityEditor` 的解析路径指到 `D:\Unity\6000.3.26f1\Editor\Data\Managed`（或其 `UnityEngine` 子目录）。

---

## 程序集与命名空间

| 命名空间 | 所在程序集 | 平台 |
|---|---|---|
| `Sirenix.OdinInspector.Editor.Validation` | `Sirenix.OdinInspector.Editor.dll` | 仅 Editor |
| `Sirenix.OdinInspector.Editor.Validation.Internal` | `Sirenix.OdinInspector.Editor.dll` | 仅 Editor |
| `Sirenix.OdinValidator.Editor` / `.Validators` | `Sirenix.OdinValidator.Editor.dll` | 仅 Editor |
| `Sirenix.OdinInspector`（`ISelfValidator` / `SelfValidationResult` / `SelfFix` / `ValidatorSeverity`） | `Sirenix.OdinInspector.Attributes.dll` | **运行时可用** |

`Sirenix.OdinValidator.Editor.dll` 的 PluginImporter：只有 `Editor: Editor` 为 `enabled: 1`，玩家平台全部 `Exclude: 1`；`isExplicitlyReferenced: 0`（Auto Reference）。

---

## 校验器基类

`Sirenix.OdinInspector.Editor.Validation`：

```csharp
// 抽象底座（不建议直接继承）
public abstract class Validator
{
    public InspectorProperty Property { get; }
    public RevalidationCriteria RevalidationCriteria { get; }
    protected virtual void Initialize();
    public bool CanValidateProperty(InspectorProperty property);
    public virtual void RunValidation(ref ValidationResult result);
    public void InitializeResult(ref ValidationResult result);
}

// 值校验
public abstract class ValueValidator<T> : Validator
{
    protected abstract void Validate(ValidationResult result);
    public IPropertyValueEntry<T> ValueEntry { get; }
    public object Value { get; }
}

// 根对象校验（资产上的对象）
public abstract class RootObjectValidator<TValue> : ValueValidator<TValue>
{
    protected abstract void Validate(ValidationResult result);
    protected virtual bool CanValidateRootProperty(InspectorProperty rootProperty);
    protected virtual bool CanValidateObject(TValue obj);
    public TValue Object { get; }
    public TValue Value { get; }
}

// 场景校验
public abstract class SceneValidator
{
    protected abstract void Validate(ValidationResult result);
    public SceneReference ValidatedScene { get; }
    public void Initialize(SceneReference scene);
    public bool CanValidateScene(SceneReference scene);
    public List<GameObject> GetSceneRoots();
    public GameObject GetSceneRoot(string name);
    public T FindComponentInSceneOfType<T>(bool includeInactive);
    public IEnumerable<T> FindAllComponentsInSceneOfType<T>(bool includeInactive);
    public IEnumerable<GameObject> GetAllGameObjectsInScene();
    public GameObject GetGameObjectAtPath(string path, char pathSeparator);
    public GameObject GetGameObjectAtPath(IList<string> path);
    public T GetComponentAtPath<T>(string path, char pathSeparator);
    public T GetComponentAtPath<T>(IList<string> path);
    public void LoadSceneIfNotLoaded(bool askToSave);
    public void RunValidation(ref ValidationResult result);
}

// 特性驱动
public abstract class AttributeValidator<TAttribute> : Validator
{
    protected abstract void Validate(ValidationResult result);
    public TAttribute Attribute { get; }
}
public abstract class AttributeValidator<TAttribute, TValue> : AttributeValidator<TAttribute>
{
    protected abstract void Validate(ValidationResult result);
    public IPropertyValueEntry<TValue> ValueEntry { get; }
    public TValue Value { get; }
}

// 项目级全局校验
public abstract class GlobalValidator
{
    public abstract IEnumerable RunValidation(ValidationResult result);   // ← 注意返回 IEnumerable
}

// ISelfValidator 的桥接（非抽象，Odin 内部使用）
public class SelfValidator<T> : ValueValidator<T> { protected override void Validate(ValidationResult result); }
```

其它公开类型：`IValidator`（`RevalidationCriteria` + `RunValidation(ref ValidationResult)`）、`IValidatorLocator`、`IMemberSelector`、`DefaultValidatorLocator`（含公开嵌套 `BrokenAttributeValidator : Validator`）、`DefaultValidationMemberSelector`、`SerializationPolicyMemberSelector`、`ValidationDrawer<T> : OdinValueDrawer<T>`、`ValidationEvents`、`ValidationStateChangeInfo`、`ValidationPathStep`、`ValidationSetup`（字段 `Validator`/`Value`/`ParentInstance`/`Root`）。

## 结果模型

```csharp
public class ValidationResult
{
    public string Path;
    public object ResultValue;
    public double ValidationTimeMS;
    public ValidationSetup Setup;
    public int Count { get; }
    public ref ResultItem Item { get; }
    public ref string Message { get; }
    public ref ValidationResultType ResultType { get; }

    public ref ResultItem AddError(string error);
    public ref ResultItem AddWarning(string warning);
    public ref ResultItem Add(ResultItem item);
    public ref ResultItem Add(ValidatorSeverity severity, string message);
    public void RerunValidation();
    public ValidationResult CreateCopy();
    public void Explode(List<ValidationResult> results, double validationTimeMS);
    public bool IsMatch(ValidationResult other);
    public bool FirstItemExists();
}

public struct ResultItem
{
    public string Message;
    public ValidationResultType ResultType;
    public ResultItemMetaData[] MetaData;
    public Fix Fix;
    public Action OnContextClick;
    public Action OnSceneGUI;
    public UnityEngine.Object SelectionObject;
    public bool RichText;

    public static ResultItem Error(string errorMessage);
    public static ResultItem Warning(string warningMessage);
}

public struct ResultItemMetaData { public string Name; public object Value; public Attribute[] Attributes; }

public class Fix
{
    public bool OfferInInspector;
    public string Title;
    public Delegate Action;
    public Type ArgType;
    public static Fix Create(Action action, bool offerInInspector);
    public static Fix Create(Action<T> fix, bool offerInInspector);
    public static Fix Create(string title, Action action, bool offerInInspector);
    public static Fix Create(string title, Action<T> fix, bool offerInInspector);
}
public class NoFixArgs { }
```

`ResultItemExtensions`（静态扩展，全部 `ref ResultItem` 前导参数）：

| 方法 | 重载 |
|---|---|
| `WithFix` | `(string title, Action fix, bool offerInInspector)`、`(string title, Action<T> fix, bool offerInInspector)`、`(Action fix, bool)`、`(Action<T> fix, bool)`、`(Fix fix)` |
| `WithContextClick` | `(Action<T> onContextClick)`、`(string path, Action onClick)`、`(string path, bool on, Action onClick)` |
| `SetSelectionObject` | `(UnityEngine.Object obj)` |
| `WithSceneGUI` | `(Action onSceneGUI)` |
| `WithMetaData` | `(string name, object value, Attribute[] attributes)`、`(object value, Attribute[] attributes)` |
| `WithButton` | `(string name, Action onClick)` |
| `EnableRichText` | `()` |

`SelfValidationResultItemExtensions` 形态基本一致（`WithContextClick` 多一个 `ContextMenuItem` 重载、委托重载为 `Func<T>`、`WithFix` 接受 `SelfFix`）。

## 注册特性

```csharp
// Sirenix.OdinInspector.Editor.Validation（Sirenix.OdinInspector.Editor.dll）
public class RegisterValidatorAttribute : Attribute
{
    public Type ValidatorType;
    public Type SpecialInstantiator;
    public int Priority;
}

// Sirenix.OdinInspector.Editor.Validation（Sirenix.OdinValidator.Editor.dll）
public class RegisterValidationRuleAttribute : RegisterValidatorAttribute
{
    public string Name;
    public string Description;
    public bool EnabledByDefault;
}

public class NoValidationInInspectorAttribute : Attribute   // 抑制 Inspector 内联校验显示
```

## 运行时可用的自校验（`Sirenix.OdinInspector`，Attributes 程序集）

```csharp
public interface ISelfValidator { void Validate(SelfValidationResult result); }

public class SelfValidationResult
{
    public int Count { get; }
    public ref ResultItem Item { get; }
    public ref ResultItem AddError(string error);
    public ref ResultItem AddWarning(string warning);
    public ref ResultItem Add(ValidatorSeverity severity, string message);
    public ref ResultItem Add(ResultItem item);
}

public struct SelfFix
{
    public string Title;
    public Delegate Action;
    public bool OfferInInspector;
    public static SelfFix Create(Action action, bool offerInInspector);
    public static SelfFix Create(string title, Action action, bool offerInInspector);
    public static SelfFix Create(Action<T> action, bool offerInInspector);
    public static SelfFix Create(string title, Action<T> action, bool offerInInspector);
}
```

`SelfValidationResultItemExtensions`：`WithFix`、`WithContextClick`、`WithSceneGUI`、`WithButton`、`WithMetaData`、`EnableRichText`、`SetSelectionObject`。

## 枚举值（全部实测）

| 枚举 | 值 |
|---|---|
| `ValidationResultType` | `IgnoreResult=0`, `Valid=1`, `Error=2`, `Warning=3` |
| `ValidatorSeverity` | `Error=0`, `Warning=1`, `Ignore=2` |
| `SelfValidationResult.ResultType` | `Error=0`, `Warning=1`, `Valid=2` |
| `RevalidationCriteria` | `Always=0`, `OnValueChange=1`, `OnValueChangeOrChildValueChange=2` |
| `ConfigSourceType`（位标志） | `None=0`, `Default=1`, `Project=2`, `Local=4`, `All=7` |
| `ValidationItem.ValidationItemType` | `Asset=0`, `Object=1`, `Scene=2`, `AssetBundle=3`, `AddressableGroup=4` |
| `ValidationItem.SceneIncludeType` | `ScenesInFolder=0`, `ScenesInBuildOptions=1`, `OpenScenes=2`, `SceneGuid=3` |
| `AutomationConfig.OnPlayModeActions`（位标志） | `None=0`, `OpenValidator=1`, `StopPlayMode=2`, `LogToConsole=4` |
| `AutomationConfig.OnBuildActions`（位标志） | `None=0`, `OpenValidator=1`, `StopBuild=2`, `LogToConsole=4` |
| `AutomationConfig.OnProjectStartupActions` | `None=0`, `OpenValidator=1`, `LogToConsole=2` |
| `AutomationConfig.ValidationSetup.RunKind` | `GlobalValidation=0`, `Custom=1` |
| `PersistenceDataType` | `Validator=0`, `InspectorProperty=1`, `ValueEntry=2`, `BaseValueEntry=3`, `PropertyTree=4`, `Root=5`, `Value=6`, `UnityObjectReference=7` |
| `ValidationSession.ValidationSessionResult.ValidationSessionResultType` | `Ignore=0`, `ResultAddedOrChanged=1`, `ObjectDeleted=2` |

## Tool 层（`Sirenix.OdinValidator.Editor`）

```csharp
// Profile：Include/Exclude 是 ValidationItem 列表
public class ValidationProfile : ScriptableObject
{
    public List<ValidationItem> Include;
    public List<ValidationItem> Exclude;
    public static ValidationProfile MainValidationProfile { get; }
    public SdfIconType Icon { get; }
    public SessionConfigDataType Type { get; }
    public ValidationSessionEditor OpenValidatorWindow();
    public ValidationSessionAssetHandle ClaimSessionHandle();
    public IEnumerable<ValidationProfile> FindAll();
    public void SaveChanges();
}
public class MainValidationProfile : ValidationProfile { public static MainValidationProfile Instance { get; } }
public class MainLocalValidationProfile { public static MainLocalValidationProfile Instance { get; } /* 仅本机 */ }

// 校验条目（Include/Exclude 的单元）
public struct ValidationItem
{
    public bool Enabled;
    public ValidationItemType Type;
    public AssetToValidate Asset;      // { string Path; string Filter; }
    public UnityEngine.Object Object;
    public SceneToValidate Scene;      // { SceneIncludeType Type; string Value; bool IncludeAssetDependencies; }
    public string AssetBundle;
    public string AddressableGroup;

    public static ValidationItem FromSceneGuid(string guid, bool includeAssetDeps);
    public static ValidationItem FromSceneFolderPath(string path, bool includeAssetDeps);
    public static ValidationItem FromScenePath(string path, bool includeAssetDeps);
    public static ValidationItem FromOpenScenes(bool includeAssetDeps);
    public static ValidationItem FromScenesInBuildOptions(bool includeAssetDeps);
    public static ValidationItem FromAssetPath(string path);
    public static ValidationItem FromAssetPath(string path, string filter);
    public static ValidationItem FromAssetBundle(string assetBundle);
    public static ValidationItem FromAddressableGroup(string addressableGroup);
    // + FromSelection()
}

// 会话配置
public class SessionConfig
{
    public bool IsDirty;
    public IEnumerable<ValidationItem> Include { get; }
    public IEnumerable<ValidationItem> Exclude { get; }
    public HashSet<string> GetAssetsToValidate();
    public HashSet<string> GetSceneGuidsToValidate();
    public bool ShouldValidateScene(string guid);
    public bool ShouldValidateUnityObjectReference(int instanceID);
    public void UpdateAll();
    public void MarkDirty();
    public void SaveChanges();
}

// 规则配置（项目级 RuleConfig.asset / 本机 local rules）
public class RuleConfig : GlobalConfig<RuleConfig>
{
    public static bool IsValidatorEnabled(Type validatorType);
    public static IValidator InstantiateValidator(Type validatorType);
    public static List<SerializedRule> GetDefaultRules();
    public static List<SerializedRule> InstantiateDefaultRules();
    public RuleDataWrapper GetRuleDataWrapper();
    public T GetRuleData<T>(ConfigSourceType source);
    public void SetAndSaveRuleData<T>(T data, ConfigSourceType source);
    public void SetProjectRules(List<SerializedRule> rules);
    public void SetLocalRules(List<SerializedRule> rules);
    public void LoadRules();
    public void SaveRules();
}
```

```csharp
// 会话：可构造，驱动队列并收集结果
public class ValidationSession : IDisposable
{
    public ValidationSession(ValidationProfile profile);
    public ValidationSession(string name, IValidationProfile[] profiles);
    public ValidationSession(string sessionName, ValidationItem[] include);
    public ValidationSession(string sessionName, IList<ValidationItem> include, IList<ValidationItem> exclude);
    public ValidationSession(string sessionName, SessionConfig config);

    public static List<ValidationSession> ActiveValidationSessions;
    public static ValidationSession MainValidationSession { get; }

    public string Name;
    public OdinValidationRunner Runner;      // 公开字段
    public ValidationWorkItemQueue WorkQueue;
    public HashSet<ValidationWorkItem> WorkDone;
    public SessionConfig Config;
    public bool IsValidatingInBackground { get; set; }
    public bool IsWatching { get; set; }
    public int CurrentErrorCount { get; }
    public int CurrentWarningCount { get; }
    public int CurrentValidCount { get; }

    public void PopulateQueue(bool clearCurrentQueue, bool populateUnloadedScenes, bool queueScenes, bool queueAssets, bool queueGlobalValidators);
    public void StartSession(bool startWatching, bool startBackgroundValidation);
    public void StopSession(bool stopWatching, bool stopBackgroundValidation);
    public void RestartBackgroundValidation();
    public IEnumerable ValidateEverythingEnumerator(bool openClosedScenes, bool showProgressBar);
    public IEnumerable ValidateEverythingEnumeratorBatched(bool openClosedScenes, bool showProgressBar);
    public IEnumerable ValidateCurrentWorkQueueEnumerator(bool populateResults, bool openClosedScenes);
    public void ValidateQueuedUpWorkNow(bool showProgressBar, bool processResultQueue);
    public PersistentValidationResult[] GetCurrentResults(bool applyFilters);
    public float CalculateCurrentValidationProgress();
    public ValidationSessionEditor OpenEditor();
    public void Clear(bool clearResults, bool clearQueue);
    public void Dispose();
}
public class ValidationSessionAssetHandle { public ValidationSession Session { get; } public void AquireSession(); public void Dispose(); }
```

```csharp
// 运行器：可直接 new，务必 Dispose（有泄漏检测）
public class OdinValidationRunner : IDisposable
{
    public OdinValidationRunner(OdinValidationPolicy policy, ValidationRunnerConfig config);
    public static EditorPrefBool EnableLeakDetection;

    public OdinValidationPolicy Policy;
    public ValidationRunnerConfig Config;

    public IEnumerable ValidateObject(object value);
    public IEnumerable ValidateObject(object value, object parentContext);
    public IEnumerable ValidateObject(object value, ObjectAddress objectAddress);
    public IEnumerable ValidateObject(object value, object parentContext, ObjectAddress objectAddress);
    public IEnumerable ValidateAllAssetsAtPath(string path);
    public IEnumerable ValidateScene(SceneReference scene);
    public IEnumerable ValidateSceneContent(SceneReference scene);
    public IEnumerable ValidateSceneValidators(SceneReference scene);
    public IEnumerable ValidateUnityObjectRecursively(object value);
    public void Dispose();
}

public class ValidationRunnerConfig { public bool DeepValidation; public static ValidationRunnerConfig Default; }
public class OdinValidationPolicy
{
    public static OdinValidationPolicy Default;
    public bool ShouldValidate(OdinValidationRunner runner, object value);
    public bool ShouldDeeplyValidate(OdinValidationRunner runner, object value);
    public bool CustomValidatorFilter(OdinValidationRunner runner, Type type);
}
```

```csharp
// 报告导出：静态方法
public abstract class ValidationReport
{
    public static SessionReport CreateSessionReport(string name, IList<PersistentValidationResult> results);
    public static string ToJson(ValidationSession session, bool prettyPrint);
    public static string ToHtml(ValidationSession session);
}
```

## 场景引用

```csharp
public struct SceneReference
{
    public static SceneReference Invalid;
    public bool IsValid;
    public string GUID;
    public string Name { get; }
    public string Path { get; }
    public bool IsLoaded { get; }
    public bool IsActive { get; }
    public static SceneReference FromPath(string path);
    public static SceneReference FromAsset(SceneAsset asset);
    public bool IsInBuildSettings(bool mustBeEnabled);
    public SceneAsset GetSceneAsset();
    public bool TryOpenScene(OpenSceneMode mode, out Scene scene);   // 注意有 out 参数
    public bool TryGetScene(out Scene scene);                        // 注意有 out 参数
}

// 零散助手
public static class SceneExtensions  { public static SceneReference ToSceneReference(this Scene scene); }
public static class SceneUtilities
{
    public static int GetLoadedSceneCount();
    public static IEnumerable<GameObject> GetSceneRoots(Scene scene);
}
```

## 对象寻址

```csharp
public class ObjectAddress
{
    public static ObjectAddress Unknown;
    public bool IsBroken;
    public AddressType Type;
    public string AssetPath;
    public SubAssetAddress SubAsset;
    public HierarchyAddress Hierarchy;
    public ComponentAddress Component;
    public string Name;
    public UnitySerializableType ObjectType;

    public static bool TryCreateObjectAddress(object obj, out ObjectAddress finalResult, out string errorMessage);
    public static ObjectAddress Parse(string json);
    public bool TryGetObjectReference(bool openSceneIfNeeded, bool autoSaveIfOpenScene, out UnityEngine.Object result, out string errorMessage);
    public bool TryGetObjectReference(bool openSceneIfNeeded, bool autoSaveIfOpenScene, out UnityEngine.Object result, out string errorMessage, out UnityEngine.Object closestObject);
    public bool IsSceneAsset();
    public string ToString(bool prettyPrint);
}
```

## 自动化配置

```csharp
public class AutomationConfig : GlobalConfig<AutomationConfig>
{
    // OnPlayMode                          // OnBuild                        // OnProjectStartup
    public bool OnPlayMode;                public bool OnBuild;              public bool OnProjectStartup;
    public OnPlayModeActions OnPlayModeIfWarnings;   public OnBuildActions OnBuildIfWarnings;   public OnProjectStartupActions OnProjectStartupIfWarnings;
    public OnPlayModeActions OnPlayModeIfErrors;     public OnBuildActions OnBuildIfErrors;     public OnProjectStartupActions OnProjectStartupIfErrors;
    public bool OnPlayModeAlwaysCompleteValidationFully; public bool OnBuildAlwaysCompleteValidationFully; public bool OnProjectStartupAlwaysCompleteValidationFully;
    public bool OnPlayModeFlashScreen;     public bool OnBuildFlashScreen;    public bool OnProjectStartupFlashScreen;
    public ValidationSetup OnPlayModeSetup; public ValidationSetup OnBuildSetup; public ValidationSetup OnProjectStartupSetup;

    public bool HasReloadedBeforeThisSession { get; }
    public void TriggerOnBuild();
}
// ValidationSetup { RunKind Profile (GlobalValidation=0 / Custom=1); ScriptableObject ProfileAsset; }
public class BuildEventHookTrigger : IPreprocessBuildWithReport { public void OnPreprocessBuild(BuildReport report); public int callbackOrder { get; } }
```

`GlobalValidationConfig : ProjectSettingsGlobalConfig<GlobalValidationConfig>` 的 `ProjectSettingBool` 字段（24 项，项目资产中当前全为 0）：
`ValidateScenesOnSceneLoad`、`ValidateMainProfileOnLoad`、`QueueAssetsOnLoad`、`QueueScenesOnLoad`、`PopulateQueueOnAssetDeleted`、`PopulateQueueOnGameObjectDeleted`、`RunMainValidationSessionOnLoad`、`DeepValidation`、`ContinuouslyValidateVisibleIssues`、`PauseValidationWhileWorkingInSceneView`、`PingOnDoubleClick`、`FocusObjectOnDoubleClick`、`FrameSelection`、`SelectNextIssueOnFix`、`SkipFirstSample`、`ShowWidget`、`DebugMode`、`ShowWidgetOnlyWhenErrorOrWarnings`、`EnableLeakDetection`、`SupressAssetLoadErrorsFromUnityLogger`、`OpenComponentInInspectorAndCloseOthers`、`WatchForChanges`、`ValidateInBackground`、`KeepMainValidationSessionAliveInBackground`；另有 `bool HasShownValidationConfig` 与静态显示项 `WidgetAnchor` / `WidgetOffsetX` / `WidgetOffsetY`。

| 项目资产实际值 | |
|---|---|
| `AfterBuildAlwaysCompleteValidationFully` 系列 | 全 `0` |
| `OnPlayMode` / `OnBuild` / `OnProjectStartup`（总开关） | 全 `0`（未启用） |
| `OnPlayModeIfErrors` | `3`（OpenValidator\|StopPlayMode） |
| `OnBuildIfErrors` | `3`（OpenValidator\|StopBuild） |
| `OnProjectStartupIfErrors` | `1`（OpenValidator） |
| `RuleConfig.projectRules` | `[]` |

## 内置校验器（公开可枚举部分）

特性驱动（`Sirenix.OdinInspector.Editor.Validation`）：`RequiredValidator<T>`、`RequiredInValidator<T>`、`MaxValueValidator<T>`、`MinValueValidator<T>`、`MinMaxSliderValidator<T>`、`PropertyRangeValidator<T>`、`MinMaxValueCompositeValidator<T>`、`ValidateInputAttributeValidator<T>`、`RequiredListLengthValidator`、`FilePathValidator`、`FolderPathValidator`、`InfoBoxValidator`、`DetailedInfoBoxValidator`、`DisallowModificationsInAttributeValidator`、`TypeRegistryIllegalInstanceValidator<T>`。

项目/资产级规则（`Sirenix.OdinValidator.Editor.Validators`）：`ListNullElementValidator`、`NoEmptyStringsValidator`、`ReferenceRequiredByDefaultValidator`、`SerializeReferenceValidator`、`MaterialValidator`、`SceneNotInBuildSettingsValidator`、`WithRuleConfig`（提供 `WithModifyRuleDataContextClick(ref ResultItem, string path, Action<...>)`，另有同名 `RuleConfigExtensions.WithModifyRuleDataContextClick`）。

> 内置校验器有相当一部分是 internal，不在这份公开清单内（例如"缺失引用/缺失脚本/重复组件/失效渲染器"一类）。需要确认某个具体检查是否存在时，按下面的字符串探针办法搜程序集，别凭营销页的措辞断言。

## 校验相关特性（`Sirenix.OdinInspector.Attributes`，均已实测存在）

`Required`、`RequiredIn`、`RequiredListLength`、`ValidateInput`、`DontValidate`、`SuppressInvalidAttributeError`、`FilePath`、`FolderPath`、`MinValue`、`MaxValue`、`PropertyRange`、`MinMaxSlider`、`AssetsOnly`、`SceneObjectsOnly`、`ChildGameObjectsOnly`、`DisallowModificationsIn`、`InfoBox`、`DetailedInfoBox`、`Obsolete`。

## 脚本模板字段（`ValidatorScriptTemplates`，全为 `public static string`）

`Standard_` / `Rule_` × `RootObjectValidator` / `ValueValidatorFileTemplate_Class` / `ValueValidatorFileTemplate_Struct` / `SceneValidator` / `AttributeValidator`，外加 `Standard_GlobalValidatorFileTemplate`（**无 Rule 版**）。

静态方法：`CreateTemplateScriptFile(filePath, template, defaultTargetName)`、`AddValidatorTemplateScriptCreationToGenericMenu(GenericMenu, string prependMenuPath)`、`AddRuleTemplateScriptCreationToGenericMenu(...)`、`AddRuleAndValidatorTemplateScriptCreationToGenericMenu(...)`，以及 `CreateStandard*` / `CreateRule*` 各一个（接受 `saveFilePanelFolderPath`）。

## 快速自查手段

- **成员是否存在**：`vfs_grep` 搜不到 DLL 内部；直接按上面的 Mono 反射法，或对 DLL 做字节探针——类型名/方法名在 `#Strings` 堆是 **UTF-8**，`ISO-8859-1` 解码后 `Contains` 即可；C# **字符串字面量**在 `#US` 堆是 **UTF-16LE**，必须按「逐字符 + `\0` 交错」模式搜，否则假阴性。菜单路径这类字面量就用后者（本技能里的 `Tools/Odin/Validator`、`Assets/Odin Validator/…` 即由此获得）。
