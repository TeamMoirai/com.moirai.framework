# 自定义 Validator（读数取自装机版 Odin Validator 4.0.2.4）

本页每条形态都由**本机编译门跑绿**后写下（探针文件编译通过即删），不是文档站转述。
复现量具：`scripts/apidump`（成员级面）、`scripts/validator_env.py`（版本 / define / 配置生效值）。
官方文档站描述最新版，`ValidateInput` 一类旧式写法与新式并存——以本页与读数为准。

## 选基类（全部在 `Sirenix.OdinInspector.Editor.Validation`，除最后一行）

| 基类 | 拿得到 | 用在 |
|---|---|---|
| `ValueValidator<T>` | `Value`(get/set)、`ValueEntry`、`protected virtual Validate(ValidationResult)` | 某类型字段的值合不合法 |
| `AttributeValidator<TAttr>` | `Attribute`(get/set) | 自定义标记属性带的规则，不限值类型 |
| `AttributeValidator<TAttr,TValue>` | `Attribute`、`Value`、`ValueEntry.SmartValue` | 同上且只针对一种值类型 |
| `RootObjectValidator<T>` | `Object`、`Value`、`CanValidateObject(T)`、`CanValidateRootProperty(InspectorProperty)` | 一个资产/组件一条结论 |
| `SceneValidator` | `ValidatedScene`、`GetSceneRoots()`、`GetSceneRoot(name)`、`GetGameObjectAtPath(path,char)`、`GetComponentAtPath`、`FindComponentInSceneOfType<T>(bool)`、`FindAllComponentsInSceneOfType<T>(bool)`、`GetAllGameObjectsInScene()`、`LoadSceneIfNotLoaded(bool)`、`CanValidateScene(SceneReference)` | 场景结构约束 |
| `GlobalValidator` | 只有 `RunValidation(ValidationResult)` | 与具体资产无关（build target、PlayerSettings…） |
| `ISelfValidator`（**`Sirenix.OdinInspector`，运行时 Attributes 程序集**） | `void Validate(SelfValidationResult)` | 类型自带校验，代码留在运行时无 Editor 依赖 |

`Validator` 基类的钩子：`Property`(get/set)、`virtual RevalidationCriteria`、`Initialize(InspectorProperty)`、
`virtual CanValidateProperty(InspectorProperty)`、`virtual RunValidation(ref ValidationResult)`、
`protected virtual Initialize()`、`InitializeResult(ref ValidationResult)`。
`IValidator` 只有两样：`RevalidationCriteria` 与 `RunValidation(ref ValidationResult)`。

枚举成员全集（**别猜名字**）：
`ValidatorSeverity` = `Error | Warning | Ignore`（**没有 Info**）；
`ValidationResultType` = `IgnoreResult | Valid | Error | Warning`；
`RevalidationCriteria` = `Always | OnValueChange | OnValueChangeOrChildValueChange`。

## 注册只有两条路：assembly attribute

```csharp
// 注意 typeof 要写全限定名：assembly attribute 在 namespace 之外（见陷阱 1）
[assembly: RegisterValidator(typeof(Moirai.Editor.Val.MyValidator), Priority = 100)]
[assembly: RegisterValidationRule(typeof(Moirai.Editor.Val.MyRule),
            Name = "我的规则", Description = "说明", EnabledByDefault = false)]
```

- `RegisterValidatorAttribute` 字段：`ValidatorType`、`SpecialInstantiator`、`Priority`。
  `RegisterValidationRuleAttribute` 继承它，另加 `Name`、`Description`、`EnabledByDefault`。
- **Validator vs Rule 的取舍**（装机 XML 原文）：rule 是"被序列化出来的 Validator 实例"，
  可以在 Odin Validator 窗口的 Rules 页启停、改它的序列化字段；
  且"Only use this to register types derived from `Validator`!"。
  所以：要让策划可调 → `RegisterValidationRule` + 把参数做成公开序列化字段；
  纯逻辑校验 → `RegisterValidator`（不占规则表）。

## 报告面：`ValidationResult` / `ResultItem`

`ValidationResult` 公开成员：`Path`、`ResultValue`、`ValidationTimeMS`、`Setup`、
`Add/Remove/Count/IsMatch/CreateCopy/RerunValidation/Explode/FirstItemExists`、
以及三条 ref 追加：

```csharp
public ref ResultItem AddError(string error);
public ref ResultItem AddWarning(string warning);
public ref ResultItem Add(ValidatorSeverity severity, string message);
public ref ResultItem Add(ResultItem item);
```

`ResultItem` 是 **struct**（`ValueType`），字段：`Message`、`ResultType`、`MetaData[]`、`Fix`、
`OnContextClick`、`OnSceneGUI`、`SelectionObject`、`RichText`；静态工厂 `ResultItem.Error(string)` / `.Warning(string)`。

装饰一律是 **`ref` 进 `ref` 出**的扩展方法（`ResultItemExtensions`），全集：
`WithFix`(5 重载：`Action`/`Action<T>`/带 title/`Fix`)、`WithContextClick`(4)、`WithSceneGUI`、
`SetSelectionObject(UnityEngine.Object)`、`EnableRichText`、`WithMetaData`(2)、`WithButton(string, Action)`。
运行时侧 `Sirenix.OdinInspector.SelfValidationResultItemExtensions` 同名同形（配 `SelfValidationResult`，
它也有 `AddError/AddWarning/Add(severity,msg)/Add(item)`）。

正写（本机编绿）：

```csharp
protected override void Validate(ValidationResult result)
{
    if (string.IsNullOrEmpty(this.Value))
    {
        ref var item = ref result.AddError("值为空");
        item.Message = "值为空（重写文案）";           // struct 字段，改的是原条目
        item.RichText = true;
        item.SetSelectionObject(target);                 // target: UnityEngine.Object（本机以 null 验过调用形态）
        item.WithButton("打开来源", () => { /* ... */ });
        item.WithFix("trim", () => this.Value = this.Value?.Trim() ?? string.Empty, true);
    }
}
```

## 实测陷阱（本会话编译门换来的）

1. **assembly attribute 里的 `typeof` 必须写全限定名**。attribute 落在 namespace 块之外，
   裸类名解析不到 → `CS0246`。同一个坑我在两次不同写法里各踩一次。
2. **`Validation` 命名空间不含 `InspectorProperty`**。重写 `CanValidateProperty(InspectorProperty)`
   要额外 `using Sirenix.OdinInspector.Editor;`，否则 `CS0246`。
3. **`GlobalValidator.RunValidation` 返回非泛型 `System.Collections.IEnumerable`**。
   只 `using System.Collections.Generic;` 会把 `IEnumerable` 认成泛型那个 → `CS0305`。
   必须 `using System.Collections;`。
4. **CS8176：迭代器体里不能有 `ref` 局部**。带 `yield return null` 分帧的 GlobalValidator
   与 `ref var it = ref result.AddError(...)` **不能写在同一个方法体里**；
   要装饰就抽一个非迭代的私有方法调用它。装机 dll 里自带的官方 GlobalValidator 模板
   正是把两者放在同一个方法里（`ref var error = ref result.AddError(...)` + 注释说可以 yield），
   照抄必红——模板原文不能当可编译证据。
5. **`ref` 返回赋给普通局部就是拷贝**：`ResultItem copy = result.AddError("x");` 之后的
   `copy.WithButton(...)` 只作用在副本上（签名如此；本机未做反例验证，所以按"永远写
   `ref var x = ref result.Add…`"来防守）。
6. `[NoValidationInInspector]`（贴在 Validator 类上）只挡检视器里的那次运行，
   **项目扫描照跑**；`[DontValidate]` 与 `[SuppressInvalidAttributeError]` 是另外两条通路（贴在字段/属性上），
   三者不要混用成一个"关掉校验"的口子。

## 存在性怎么再核一遍

在**宿主工程根目录**下跑（路径都是相对工程根的实测形式）：

```
python .claude/skills/odin-inspector/scripts/odin_api_probe.py ValueValidator AbstractValidator ValidatorSeverity
dotnet run --project .claude/skills/odin-validator/scripts/apidump -- "Assets/Plugins/Sirenix/Assemblies" "Validation.ValueValidator"
```

`dotnet run` 传参只认 `--project <目录> -- <参数...>`；把 `-v q`、`--nologo` 之类放在 `--` 之前会**漏进程序的 args**（本机踩过：`DirectoryNotFoundException: …\apidump\--nologo`，看着像工具坏了其实是调用姿势）。这条命令会在 `apidump/` 下生成 `bin/`、`obj/`，收尾删掉或靠自带的 `.gitignore` 挡住。

本机读数：`AbstractValidator`、`StringValidatorBase`、`ValidatorGroup`、`ValidationSeverity`、`ErrorLevel`、
`ApplyFix` 全部 **ABSENT**（文档站/旧版/直觉里的名字）；`ValidatorSeverity.Info` 也不存在。
apidump 的已知缺陷：属性行的"类型"列打印的是签名对象，要看属性类型请读它的 getter 方法行。
