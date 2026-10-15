# 包管理器

> 把一个可配的 git 分支当包源来浏览与装卸框架自定义插件包：未勾选的包既不物化也不落盘，外部 UPM 依赖按归属台账写进工程 `manifest.json`。

菜单入口 `Tools/Moirai/Package Manager`。窗口是 Odin 菜单树：叶子按清单的 `function` 分组，右侧详情面就是选中条目本身；工具栏三个动作依次是 `Refresh`（扫分支）、`Update All (n)`（一趟更新全部可更新项）、`Settings`（跳到就地可编辑的源设置）。

## 包源与落点

| 项 | 值 |
|---|---|
| 远端与分支 | 窗口内 `分支包源` 节点编辑，默认分支 `plugin` |
| 落点目录 | `Packages/com.moirai.framework.plugin/` |
| 状态账本 | `ProjectSettings/MoiraiPackageManager.json` |
| git 可执行文件 | 默认 `git`，走进程外调用 |

落点目录就是分支的工作树。工具按 `--filter=blob:none --no-checkout --single-branch` 建检出，再用 cone 模式稀疏检出，因此**未勾选的插件目录不会出现在磁盘上**，而 `git ls-tree` 仍能列出整棵树——浏览面靠树、取文靠按需读 blob。

## 插件清单

每个插件住在 `<id>/` 下，清单文件名固定 `<id>.plugin.json`，只由本工具解析（Unity 读的是分支根的 `package.json`，工具对它**只读校验、永不写**）。

| 字段 | 含义 |
|---|---|
| `id` | 小写 slug，字符集 `[a-z0-9._-]` 且不以点开头；须同时等于目录名与清单文件名前缀 |
| `name` | 展示名 |
| `function` | 菜单分组名 |
| `description` / `author` | 说明与作者 |
| `version` | 三段十进制版本号 |
| `minUnityVersion` | 最低引擎版本 |
| `tags` | 安装时添加、卸载时移除的脚本宏 |
| `dependencies` | 包间依赖：`id` + `version` + `versionOperator`（`>=` `>` `=` `<=` `<`） |
| `upmDependencies` | 外部 UPM 包依赖：`id` + `version` |

## 装、卸、更新

`安装` 走这条时序：建检出 → fetch → 摆正分支 → 列树 → 建目录 → 规划 → 冲突则整批中止 → 勾稀疏集 → 逐个校验 `.asmdef`（须存在，且装配名不得与工程内任何 `.asmdef` 重名）→ 写工程 `manifest.json` 的外部依赖 → 加脚本宏 → **状态账本先落盘** → 只 `AssetDatabase.Refresh()` 一次。

规划层是纯函数，判出这几类冲突之一就整批不装：缺依赖、依赖成环、版本要求不满足、引擎地板不够。多包必须一趟装完——分批会在每包后各触发一次域重载，第二趟起没人接着跑。

`卸载` 的依据全部来自账本，不靠扫描工作树反推：按 `owners` 计数回收外部依赖（`owners` 清空且该条由工具写入才删那一行；用户本就手写的条目即使再无使用者也原样留着），脚本宏只撤掉没有别的使用者的那几枚，然后把稀疏集收到余下的已装项。仍有插件依赖它时先弹窗，三个选项各有真实分支：连带卸载、中止、只卸它。

`可更新` 判定比对两处的 tree 哈希：账本里安装时那条 `commit` 下的 `<id>/<id>.plugin.json`，与 `refs/remotes/origin/<branch>` 下的同一份。

## 已知边界

- cone 模式永远物化分支根的散文件（如根 `package.json`），这是它的既定形状。
- 卸载或清空勾选集后，插件目录本身可能作为空目录留在落点：git 撤文件不撤目录，Unity 会连带清掉它的 `.meta`。
- 本地 `file://` 源（离线自测常用）不做过滤协商，git 会报 `filtering not recognized by server` 并回全量对象；"少下载"只在真远端成立，离线侧能判的是"勾选外的目录不物化"与"过滤器已登记进 `remote.origin.partialclonefilter`"。
- 首批动作面是单包安装、单包卸载与 `Update All`，不做多选批量队列。

## 判定面

装卸规则的可判部分全部收在 L1（EditMode）：版本值类型与运算符、清单校验、工程 `manifest.json` 的行级增删、状态账本与归属计数、依赖闭包与拓扑、树路径到目录的发现、以及 git 层的克隆/摆正/稀疏物化（跑在临时 `file://` 夹具上）。

```bash
# 复现当前读数：83 个用例名 / 128 格通过 / 0 失败
bash "$LOCALAPPDATA/pm-names.sh" Tests/EditorMode/Editor/Tools/PackageManager/*/*Tests.cs > names.txt
bash "$LOCALAPPDATA/pm-gate.sh" pm refresh
bash "$LOCALAPPDATA/pm-test.sh" pm names.txt
```
