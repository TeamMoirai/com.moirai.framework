# 包管理器

> 把一个可配的 git 分支当包源来浏览与装卸框架自定义插件包：未勾选的包既不物化也不落盘，外部 UPM 依赖按归属台账写进工程 `manifest.json`。

菜单入口 `Tools/Moirai/Package Manager`。窗口是 Odin 菜单树：叶子按清单的 `function` 分组，**行名前带实心圆状态图标并上色**（✓ 绿=已装、↑ 蓝=可升级、! 黄=超出支持范围、✕ 红=其他拦路；未装且无拦路不画图标），右侧详情面就是选中条目本身；工具栏依次是 `Refresh`（扫分支）、文本筛选框、**筛选下拉（全部 / 未安装 / 可升级）**、`Update All (n)`（一趟更新全部可更新项；**没有可更新项时不出现**）、`Settings`（进独立设置面）；装卸成功后按本机账本立即重算列表，不必等域重载或手动刷新。**开窗即可见内容**：先用上次扫分支的缓存铺列表，随后自动在后台重扫（扫分支跑在工作线程，界面不卡；只有首次开窗没有缓存时才显示装载遮罩）；文本筛选按名字/标识/分组/说明/标签不区分大小写匹配，并与筛选下拉叠加，任一项生效时右侧读数变成命中/总数；源不可用时的错误行带 `重试`。详情面按 `概况 / 标签 / 依赖 / 操作` 分组：**清单里的说明**用只读说明框折行展示（没写就不占地方），`状态` 直接写出版本对照（`已装 0.1.0　↑ 可更新到 0.2.0`），依赖逐条附已装状态；动作按状态取舍——未装只给 `安装`，可更新给 `更新到分支版本`（旁边可勾 `更新前先清目录`：先删掉插件目录再按分支头重新物化，清掉新版本不再产出的文件），已装给 `重装` 与 `卸载`；`Update All` 会先把每条的 `已装 → 分支` 摆出来问一次再动手。操作区还有 `安装版本` 下拉：默认分支头那一条，展开时才去问一遍 git，列出该插件清单历史上出现过的版本（同一版本只留最新那次修订，已装的那条标 `（已装）`）；选中历史版本后出现 `安装所选版本`——先清目录再按那次修订物化，因此**可以降级**，账本记的是该修订（随时可用 `更新到分支版本` 回到头）。**源设置住在设置面里**：默认视图的菜单树只列插件，进设置面后树里只列 `分支包源`、工具栏同一位置变成 `返回列表`。顶栏读数同样分面：常规面报 `上次刷新` 时刻、分支与已装数量，源、HEAD 与检出目录这些现场细节只在设置面报（`上次刷新` 与目录缓存由每次扫分支成功时写进 `Library/MoiraiCache/` 下的本机缓存，不写进 `ProjectSettings/` 的状态账本）；源不可用时的失败原因在两面都报。

## 包源与落点

| 项 | 值 |
|---|---|
| 远端与分支 | 设置面的 `分支包源` 节点编辑；远端默认 `https://github.com/TeamMoirai/com.moirai.framework.git`，分支默认 `plugin` |
| 检出目录 | `Library/MoiraiCache/com.moirai.framework.plugin/`（临时工作树，可随时删除重建） |
| 安装目录 | `Packages/com.moirai.framework.plugin.<id>/`（每个插件一个嵌入包） |
| 状态账本 | `ProjectSettings/MoiraiFrameworkPackage.json`（只含设置项、已装记录与归属台账；随配置或装卸变化才写） |
| 本机缓存 | `Library/MoiraiCache/com.moirai.framework.plugin.cache.json`（上次刷新时刻与目录缓存；每次刷新都会重写，不进版本控制） |
| git 可执行文件 | 默认 `git`，走进程外调用 |

检出目录就是分支的工作树，只作刷新与取内容的中间层。工具按 `--filter=blob:none --no-checkout --single-branch` 建检出，再用按目录的稀疏检出（非 cone 模式）：**未勾选的插件目录不会出现在磁盘上**，分支根的开发说明与 `.meta` 也不物化；浏览面只列一次树，把根文件与各份 `<id>.plugin.json` 合成一次预取取回、一条进程读齐，插件正文只在安装时才物化，随后复制进 `Packages/`。刷新拿到的清单目录只落进本机缓存的 `catalogCache`，不再依赖检出常驻。

## 插件清单

每个插件住在 `<id>/` 下，清单文件名固定 `<id>.plugin.json`，只由本工具解析（Unity 读的是分支根的 `package.json`，工具对它**只读校验、永不写**）。

| 字段 | 含义 |
|---|---|
| `id` | 小写 slug，字符集 `[a-z0-9._-]` 且不以点开头；须同时等于目录名与清单文件名前缀 |
| `name` | 展示名 |
| `function` | 菜单分组名；不许含 `/` `\` 或控制字符、长度 ≤ 40（分组名要当树路径的一节用，带 `/` 会多撑一层） |
| `description` / `author` | 说明与作者 |
| `version` | 三段十进制版本号 |
| `minUnityVersion` | 最低引擎版本 |
| `minFrameworkVersion` | 最低框架版本：与 `minUnityVersion` 同款硬性要求，不满足即拒绝安装。填已发或已排期的框架版本——框架 `package.json` 的 `version` 由发布自动化抬，插件不得靠改它来满足自己的地板 |
| `maxUnityVersion` | 可选上限：Unity 侧**不再提供的第一个版本**，空或缺省=无限制。区间按 `[min, max)` 判，等于 max 即已停止提供。写**编辑器内部版本号**（Unity 6.3 = `6000.3.0`），与 `minUnityVersion` 同源同形 |
| `maxFrameworkVersion` | 可选上限：框架侧不再提供的第一个版本，空或缺省=无限制。上限不高于同侧地板时整条清单判非——那是空区间，谁都装不上，静默留着只会让人误判成"版本不够" |
| `tags` | 安装时添加、卸载时移除的脚本宏 |
| `dependencies` | 包间依赖：`id` + `version` + `versionOperator`（`>=` `>` `=` `<=` `<`） |
| `upmDependencies` | 外部 UPM 包依赖：`id` + `version` |

**安装前置写在 `description` 里**（如「先用 NuGetForUnity 装好 ZLogger 再装本插件」）：详情面的说明框原样展示它，工具不把它解析成结构化步骤。

## 装、卸、更新

`安装` 走这条时序：建检出 → fetch → 摆正分支 → 列树 → 建目录 → 规划 → 冲突或目标目录归属不明则整批中止 → 勾稀疏集 → 逐个校验 `.asmdef`（须存在，且装配名不得与工程内任何 `.asmdef` 重名）→ 从检出复制到 `Packages/<检出目录名>.<id>/` 并补一份最小 `package.json` → 写工程 `manifest.json` 的外部依赖 → 加脚本宏 → **状态账本先落盘** → 只 `AssetDatabase.Refresh()` 一次。`Packages/` 下已有同名目录但账本里没有该插件时，工具拒绝覆盖。

装卸全程同步占住编辑器，窗口在此期间挂 `EditorUtility.DisplayProgressBar`：编排层按上面的阶段回报 `0..1` 与阶段文案（检查检出 → 扫分支与取目录 → 规划依赖闭包 → 清理与物化 → 复制到 `Packages` → 写 manifest 与脚本宏 → 刷新资源），动作返回后无论成败都 `ClearProgressBar`。单包安装、`Update All` 与按历史版本安装走同一套。动作在途时整窗禁用输入（进度弹窗仍会派发点击，不锁就可能叠第二趟），动作结束后再锁半秒收掉期间排队的点击；账本落盘后、`AssetDatabase.Refresh()` 之前先把列表图标与详情的本机读数就地换掉，重编译期间窗口不会停在"未安装"的样子。

规划层是纯函数，判出这几类冲突之一就整批不装：缺依赖、依赖成环、版本要求不满足、引擎地板不够、框架地板不够（`minFrameworkVersion`）、引擎已过上限、框架已过上限（`maxUnityVersion` / `maxFrameworkVersion`）。多包必须一趟装完——分批会在每包后各触发一次域重载，第二趟起没人接着跑。读不出框架包版本的那一趟把框架的地板与上限一起跳过，并在报告里说明判过了什么。上限的存在让"有限生命周期的插件"有处可写：官方弃用的包（如 `unity-logging` 之于 Unity 6.3）可以在清单里自己标出停止提供的那一档，不必让框架去记谁在哪天失效。

`卸载` 的依据全部来自账本，不靠扫描工作树反推：按 `owners` 计数回收外部依赖（`owners` 清空且该条由工具写入才删那一行；用户本就手写的条目即使再无使用者也原样留着；归属记录里本来没有这个插件时判 `NotFound`，既不撤账也不删行），脚本宏只撤掉没有别的使用者的那枚，然后删掉 `Packages/<检出目录名>.<id>/` 并把稀疏集收到余下的已装项。仍有插件依赖它时先弹窗，三个选项各有真实分支：`连带卸载` 把点名的与全部在场依赖方收进同一批（一次稀疏集、一次 manifest 写、一次刷新），`中止` 什么都不改，`只卸它` 只收点名的一个并在结束后列出仍缺它的那批。

`可更新` 判定比对两处的 tree 哈希：账本里安装时那条 `commit` 下的 `<id>/<id>.plugin.json`，与 `refs/remotes/origin/<branch>` 下的同一份。

## 复用：另一条分支的包管理器

窗口的交互与装卸编排收在 `PackageManagerWindowBase` 里，派生窗只需给出一份 `PackageManagerWindowBase.PackageManagerProfile`：

| Profile 项 | 作用 |
|---|---|
| `WindowTitle` | 窗口标题 |
| `StateFileName` | 状态账本文件名，落在 `ProjectSettings/` 下；与检出目录一起把两套管理器隔开 |
| `WorkspaceFolderName` | 检出目录名，落在 `Library/MoiraiCache/` 下；须与分支根 `package.json` 的 `name` 相同。刷新缓存取同一名字（`Library/MoiraiCache/<WorkspaceFolderName>.cache.json`），安装目录取它的前缀（`Packages/<WorkspaceFolderName>.<id>/`） |
| `DefaultRepoUrl` / `DefaultBranch` / `DefaultGitPath` | 源设置的初值 |
| `HostPackageType` | `minFrameworkVersion` / `maxFrameworkVersion` 拿哪个包的版本来判；空引用表示不判这段区间 |
| `EntryNoun` / `BranchEntryNoun` / `SourceNodeName` / `UngroupedNodeName` / `HostLabel` | 界面文案（插件/模块、分支包/分支模块、框架/本体等） |

派生窗覆写 `CreateProfile()` 并自备 `[MenuItem]`；菜单树、筛选、详情面、版本历史下拉、`Update All` 与装卸动作全部继承。框架插件源自己的配置在 `FrameworkPackageProfile.Instance`，状态账本 `ProjectSettings/MoiraiFrameworkPackage.json` 与检出目录 `Library/MoiraiCache/com.moirai.framework.plugin/` 保持原样。同一工程里并存多套管理器时，只要 `StateFileName` 与 `WorkspaceFolderName` 不重名，彼此的账本、检出与宿主版本判定互不影响；安装目录也因前缀不同而互不相撞。

## 已知边界

- 检出只是远端分支的缓存：摆正分支一律强制对齐 `origin`，**始终以远端为准**。本地未推送的提交与未提交改动会被直接重置丢弃（冲突时就地覆盖，不弹窗询问），窗口也不再显示这类本地状态。
- 稀疏集按目录给模式，分支根的散文件（README、`.meta`、根 `package.json`）不进工作树；包名校验读的是 blob，不依赖盘上那份。旧检出若是 cone 模式，建检出时按现有勾选就地换成按目录模式。
- 离线时 fetch 判为"没到远端"：仍按本地已有的分支头列包并允许安装（blob 真取不到时由该份清单判非并说明），"上次刷新"时刻不写。
- 清单解析判非的份数会在顶栏按文件点名，不静默少一个包。
- 卸载或清空勾选集后，检出里的插件目录本身可能作为空目录留在 `Library/MoiraiCache/`：git 撤文件不撤目录，Unity 会连带清掉它的 `.meta`；`Packages/` 下的安装目录由工具整目录删除。
- 分支名与源地址在进 git 前过形状守卫：分支只许字母数字与 `. _ - /`（不得以 `- . /` 开头、不得含 `..`），地址不得以 `-` 开头也不得含引号与控制字符。
- 详情面的 `概况` 有清单在分支树里的来源路径，`依赖` 有外部依赖的归属台账（谁写入的、要求到的版本、当前哪几个插件在用）。状态徽标走的是与安装同一条判据（试装一次规划器），能报未装 / 已装 / 可更新 / 缺依赖 / 依赖成环 / 版本不符 / Unity 版本不符 / 框架版本不符 / 超出 Unity 支持范围 / 框架侧停止提供。
- `操作` 面：安装类按钮是大号按钮，并按同一条判据置灰——判据就是徽标那条 blocker，所以"为什么不能装"与"为什么装不了"必然同源。置灰时按钮下方出一行 `✕ 原因`（能装时整行不画）。**`卸载` 不接这条限制**：版本过档了也得让人把已装的拆掉。`Update All` 同样按这条判据把装不了的剔掉并在结果里逐条点名，不静默少一个。
- 本地 `file://` 源（离线自测常用）不做过滤协商，git 会报 `filtering not recognized by server` 并回全量对象；"少下载"只在真远端成立，离线侧能判的是"勾选外的目录不物化"与"过滤器已登记进 `remote.origin.partialclonefilter`"。
- 首批动作面是单包安装、单包卸载与 `Update All`，不做多选批量队列。

## 判定面

装卸规则的可判部分全部收在 L1（EditMode）：版本值类型与运算符、清单校验、工程 `manifest.json` 的行级增删、状态账本与归属计数、依赖闭包与拓扑、树路径到目录的发现、以及 git 层的克隆/摆正/稀疏物化与清单历史（列历史、按修订取文、按修订物化；跑在临时 `file://` 夹具上）。

读数按属性就能数出来，再由编辑器实证。编辑器实测 `failed 0 | skipped 0`（先过 `GATE OK` 的空闲与逐装配新鲜度门）。

```bash
# 逐文件抽取再合并：pm-names.sh 一次只吃一个文件，多传（如直接喂 glob）会当场判非而不是静默少抽
for f in Tests/EditorMode/Editor/Tools/PackageManager/*/*Tests.cs; do bash "$LOCALAPPDATA/pm-names.sh" "$f"; done | sort -u > names.txt
bash "$LOCALAPPDATA/pm-gate.sh" pm refresh
bash "$LOCALAPPDATA/pm-test.sh" pm names.txt   # 首行应为 failed 0
```
