---
name: odin-validator
description: 在 Unity 工程里安全地写/改 Odin Validator 校验：选自定义基类（ValueValidator/AttributeValidator/RootObjectValidator/SceneValidator/GlobalValidator/ISelfValidator）、RegisterValidator 与 RegisterValidationRule 的取舍、ValidationResult/ResultItem 的 ref 报告与 WithFix/WithButton/SetSelectionObject、属性层校验被项目扫描拾取的清单、Odin Validator 窗口与 Profile/Rule/Automation 配置生效值、CI/流水线里驱动 OdinValidationRunner。排查"校验不跑/规则不在窗口里/AddError 写了没反应/CS8176 迭代器/CS0305 IEnumerable/AbstractValidator 或 ValidatorSeverity.Info 不存在/进 Play 或构建没触发校验/批量修复不出现"都先用它。凡涉及 Sirenix 校验程序集 API 是否存在、rule 序列化配置归属、验证结论能不能自证，都按本手册先量再写。
---

# Odin Validator 作业手册

## Overview

本手册的顺序是：**先量现场 → 属性优先 → 按签名写 → 编译门 + 自证读数 → 交互面交回用户目测**。

## 铁律

1. **先量再写。** `scripts/validator_env.py --project <宿主工程根>` 一次给全：版本、各平台 `ODIN_VALIDATOR*` 符号、
   关键成员在各 dll 的整名命中数（带正/反对照自检）、配置资产生效值。缺哪一段，对应结论就只能标"未证"。
2. **能用属性就别写 validator。** `[ValidateInput]`/`[Required]`/`[Range]`/`[InfoBox]` 这一批都有内置
   validator 兜住，项目扫描会自动拾取（清单见 `references/run-and-config.md`）。自定义只在属性表达不了时用。
3. **注册只有 assembly attribute 一条路**，且 `typeof` 必须全限定名。
   要给人调 → `RegisterValidationRule`（rule = 序列化出来的 Validator 实例，窗口 Rules 页可启停/改值，
   参数写成公开序列化字段）；纯逻辑 → `RegisterValidator`（不进规则表）。
4. **报告一律走 ref**：`ref var it = ref result.AddError(...)`，之后 `it.Message/ResultType/RichText/SelectionObject`
   与 `it.WithButton(...)`/`it.WithFix(title, Action, offerInInspector)`/`it.SetSelectionObject(obj)` 才作用在真条目上。
   带 `yield` 分帧的 `GlobalValidator` 里**不能有 ref 局部**（CS8176）→ 装饰抽进非迭代私有方法。
5. **生效行为在配置资产里，不在 C# 初值。** 自动化（Play/Build/Startup）、全局开关、rule 表、Profile 各归一份 .asset；
   改完重跑 env 脚本用读数自证（`projectRules entries=` 这类）。
6. **交互面不能凭读数下结论。** 窗口里错误条目显不显示、按钮点不点得动、批量修对不对，只能标"待用户目测"。

## 工作流

1. 判性质：单字段值合不合法 / 带标记属性的规则 / 整个资产或组件 / 场景结构 / 与资产无关的全局 / 类型自带。
   → 按 `references/validators.md` 的基类表选，别背签名。
2. 拿不准成员名就用成员级量具：
   在宿主工程根下：`dotnet run --project .claude/skills/odin-validator/scripts/apidump -- "Assets/Plugins/Sirenix/Assemblies" "Validation.ValueValidator"`
   （参数只能跟在 `--` 之后，`-v q`/`--nologo` 漏进 args 会报成 `DirectoryNotFoundException`）
   （它给类型/字段/方法含返回与参数类型；属性行的"类型"列有已知缺陷，看 getter 方法行）。
3. 写：`#if UNITY_EDITOR && ODIN_VALIDATOR` 包住；`using` 至少要 `System.Collections`（GlobalValidator）、
   `Sirenix.OdinInspector.Editor`（`InspectorProperty`）、`Sirenix.OdinInspector.Editor.Validation`。
4. 验证：编译门（协议在 `references/run-and-config.md` 末节，与 sibling `odin-inspector` 的桥协议同源；桥是本仓 `Tests/EditorMode/EditorStateBridge.cs`）
   → env 脚本读数 → 目测清单交回用户。错误正文要整文件搜再 `sort -u`，别截尾。
5. 结论分层汇报：编译过 / 跑过读数 / 只是文档这么说——三者不能混着写。

## 常见症状 → 先看这条

| 症状 | 成因/入口 |
|---|---|
| `CS0305`：`IEnumerable` 需要 1 个类型参数 | `GlobalValidator.RunValidation` 返回**非泛型** `System.Collections.IEnumerable`，缺 `using System.Collections;` |
| `CS8176`：迭代器不能有 by-reference 局部 | 同一方法体里既有 `yield` 又有 `ref var`（`validators.md` 陷阱 4） |
| `CS0246`：attribute 里的类找不到 | assembly attribute 在 namespace 外，`typeof` 要写全限定名 |
| `CS0246`：`InspectorProperty` 找不到 | `Validation` 命名空间不含它，补 `using Sirenix.OdinInspector.Editor;` |
| `ValidatorSeverity.Info` / `AbstractValidator` / `StringValidatorBase` 不存在 | 文档站或旧版的名字；本机枚举全集是 `Error|Warning|Ignore`（`validators.md` 末节复现命令） |
| AddError 写了但窗口里没这条 | 三层门顺查：define 符号 → 程序集是否编进来 → Automation/Global 配置生效值（`run-and-config.md`） |
| 规则不在 Validator 窗口的 Rules 页 | 用了 `RegisterValidator` 而不是 `RegisterValidationRule`；或 `RuleConfig.asset` 里 `projectRules` 还没落表 |
| 进 Play / 构建 / 打开工程都没触发校验 | `AutomationConfig.asset` 三项读数是否为 0（本机实测全 0） |
| 字段级 `[ValidateInput]` 的红字没进项目扫描 | 它**有**内置 validator 会拾取（`ValidateInputAttributeValidator`）；先排 define / 配置生效值 / 该属性是否被 `[DontValidate]` 关掉，别怀疑属性层本身 |
| 照 dll 自带模板抄就编译红 | 模板原文有自相矛盾处（ref 局部 + yield 同体），以 `validators.md` 的编绿写法为准 |

## Resources

- `references/validators.md` — 基类表、注册两型、`ValidationResult`/`ResultItem` 报告面与扩展方法全集、6 条实测陷阱、存在性复现命令
- `references/run-and-config.md` — 三层门（define/程序集/配置）、属性拾取清单、Runner/Session/报告形态、本机验证协议
- `scripts/validator_env.py` — 现场体检（版本/define/API 整名命中/配置生效值，自带正反映对照，全 ASCII 输出）
- `scripts/apidump/` — 成员级面（工程根下 `dotnet run --project .claude/skills/odin-validator/scripts/apidump -- "Assets/Plugins/Sirenix/Assemblies" <全名子串>`；属性类型列有已知缺陷）
