# 禁碰路径与「为什么搬了就坏」

动手前对着本表过一遍目标路径。每条给的是失效方式——多数不是编译错误，而是编译照过、运行期或判据侧才断。

## 配置层与文档

| 路径 | 失效方式 |
|---|---|
| 技能目录 `odin-inspector`、`odin-validator` | 属别的会话的在途产物：没进版本库，`git add` 其路径会把别人没写完的内容一并提交，事后也分不出是谁的。这条规则不需要指针支撑——不得 `git add` 别人的路径 |
| `CLAUDE.md` | 上面可能挂着别人的未提交 hunk。改它之前过这道门：`git diff --numstat HEAD -- CLAUDE.md` **必须输出空**；非空即停手回报 BLOCKED 并附原文，不许手删别人的行、不许 `git add -p`、不许「先提交再说」 |
| 三份既有守卫 `Tests/EditorMode/Policy/ReflectionPolicyGuardTests.cs`、`Tests/EditorMode/Policy/CoreDependencyRatchetTests.cs`、`Tests/EditorMode/Policy/TestLogChannelPolicyGuardTests.cs` | 只读参考。为了让自己的文件过关而改别人的判据或白名单，等于把守卫拆了 |

## asmref 射程（删一下就让一片脚本失去引用）

`.asmref` 的作用域是它所在目录及其子目录里没有 `asmdef` 的脚本，靠一份 GUID 引用把外部程序集接进来。asmref 本身没有可读的程序集名，改名字、搬目录、顺手清理都会当场断。

| asmref 所在目录 | 外部程序集 | 靠它的是谁 |
|---|---|---|
| `Editor/Services/Resource/YooAsset/Internal/` | `YooAsset.Editor` | 该目录及子目录里没有 `asmdef` 的实现（`ClearBuildCache`、`CollectorInspector`、`CustomBuildPipeline`、`CustomCollectRules` 等）整个靠它取类型 |

## 按名解析的 `Resources/`

参与 Unity 导入的 `Resources/` 目录是下面这几处：`Editor/Core/Attributes/Resources/`、`Editor/Core/Events/Resources/`、`Runtime/Services/Debugger/Resources/`、`Runtime/Services/UI/Resources/`。
`~` 结尾目录（`Samples~`、`Templates~` 等）里另有同名 `Resources/`，它们不参与导入、`Resources.Load` 够不到，所以不在本表射程——按 `find -type d -name Resources` 数会得到比这里多的条目，那不是本表的口径，也别把计数钉进本层。
`Resources.Load` 只认「目录名 `Resources` + 相对路径不含扩展名」，不看程序集归属、编译期没有任何引用可查：改名或搬动 ⇒ 编译照过，运行期取到 `null`。
在盘面上对得上的几对（动一侧就断另一侧）：

- `Runtime/Services/Debugger/Resources/`：`DebuggerPanelSettings.asset` 由 `DebuggerRuntimeHost` 按常量 `"DebuggerPanelSettings"` 取，
  名字带空格的 `Debugger UI Theme.tss` 按常量 `"Debugger UI Theme"` 取。
- `Runtime/Services/UI/Resources/` 里只有 `LogUI.prefab`：`ErrorLogger` 以 `nameof(LogUI)` 作标识，经 `UGUIWindow.LoadPanel`
  的 `Resources.Load<GameObject>` 取；相对路径由 `Runtime/Services/UI/UIServiceSettings.cs` 的内置资源父目录字段拼，留空即把标识原样当相对路径。
- `Editor/Core/Events/Resources/`：`EventsDebugger.uxml` 与 `EventsDebugger.uss` 由 `EventsDebuggerWindow` 按 `"EventsDebugger"` 取。
- `Editor/Core/Attributes/Resources/`：`.png` 与 `.uss` 按 `Attributes/…` 形态的相对路径取，常量集中在 `Editor/Core/Attributes/AttributesStaticRef.cs`。

多个 `Resources/` 下同名资产还会撞车——打包时取哪一份不由路径决定。

## 不参与导入的目录

| 路径 | 失效方式 |
|---|---|
| `Samples~`、`Templates~`、`Documentation~` | 尾带 `~` 的目录被 Unity 与包管理器排除在导入与编译校验之外：里面写坏不报错，判据也看不见。搬动会断示例/模板的解析入口（`Samples~/Moirai.Atropos.Samples.asmref`、`Templates~/@Requirements/`） |
| `Plugins` | 走特殊导入管线，程序集与平台面按目录约定装配；搬动影响的是原生/第三方依赖的可见性——没有判据会替你报错，只能自己核平台面 |
| `SourceGenerators` | 随包的是带 `RoslynAnalyzer` 标签的编译产物（`SourceGenerators/ProviderHandlerHost.dll` 起头的一批，dll 与工程一一对应），工程源码在 `Source~/` 形态里、不参与 Unity 导入。产物正是手写代码引用的成员与注册表：`ProviderHandlerHostGenerator` 给标了 `[ProviderHandlerHost(typeof(THandler))]` 的 partial class 发射 `s_Handler` 字段、`IsValid` 与 `Handler` 属性和 `RequireHandler` 方法（消费点如 `LogUtility.Handler`、`GameTime.Handler`），`SaveServiceCodegen` 发射存档字段的 capture/restore 与 serializer/migrator 注册，`UIWindowCodegen` 与 `ServiceDependency` 发射窗口注册表与内建服务注册。摘掉或搬走 dll ⇒ 这些成员当场查无，是编译失败而不是静默降级；改生成器本体要在 Unity 外用 dotnet 编好 dll 再放回，标签跟着 `.meta` 走 |

## `Tests/`：白名单键是按路径写的

`ReflectionPolicyGuardTests` 与 `TestLogChannelPolicyGuardTests` 的白名单键是**相对 `Tests/` 的正斜杠路径**（如 `EditorMode/Service/Save/SaveAssetCatalogTests.cs`）。
括号里那串是**键的形状，不是文档指针**：它按 `Tests/` 起基座，不参与「跨文件指针一律按包根」那条口径。
给它补上 `Tests/` 前缀两头都不认——包根写法在这里不是键，键去掉前缀在那里不是路径，照它换键注册即落空、反向断言当场打红。
两份名单都双向断言：未登记的不得出现该模式，已登记的必须仍存在且仍命中。于是：

- 重命名或移动一个已登记的测试文件 ⇒ 该键落空，反向断言当场打红（名单腐烂）。要搬就在同一个提交里换键。
- 配置层登记表 `frontmatter`/`symbols` 段的键是**文件名**（`symbols` 的键是 `文件名|符号`），所以分层目录后命令文件名必须保持唯一，否则两个同名文件共用一条登记，豁免面宽于判据面。

## 这条边界要说清：档位判据挡不住写

`.claude/skills/moirai-conventions/scripts/check_config_surface.py` 的 B 段判「只读档位不该声明写权限」时，
只认量具 `WRITE_TOOLS` 里的工具名整词（`Edit`、`Write`、`MultiEdit`、`NotebookEdit`）——值先按空白与逗号切词，
切出的 chunk 再剥掉反引号、引号与 `[]`/`{}`，所以 `allowed-tools` 的**一行式逗号**与 **YAML 清单**两种已知书写都切得出来，
值为空按缺键算。不属于这两种书写的值形态（YAML flow sequence `allowed-tools: [Read, Edit]`、折叠/字面标量
`allowed-tools: >` 这类切不出工具名的写法）自己就是一处违例，在登记串里单列 `allowed-tools-shape` 一段上报——
这种形态的后果恰好是让上面那句「切得出来」变成切不出，当成非空值放行就是不报缺键、也不报写权限的静默绿。
**`Bash` 的写能力不在它的射程内**——
只读档命令即使一条写工具都不声明，只要给了宽口子 `Bash`，仍然能改盘。

因此仓内写保护由三处分担，缺一处就漏：包内权限配置的 `permissions.deny`、命令正文里写死的「本命令不写盘」、以及本清单。
包内权限配置尚未落位时，只有后两处生效——别把「档位判据是绿的」读成「只读档位写不了盘」。
