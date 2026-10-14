# 两条桥的操作形状

执行体是 `Tests/EditorMode/TestRequestRunner.cs`（测试桥）与 `Tests/EditorMode/EditorStateBridge.cs`（状态桥与命令桥），
两者都在 `Moirai.Atropos.Tests.EditorMode` 程序集里（`UNITY_INCLUDE_TESTS` 门控）。
本块只给可粘贴的形状：通道、字段名、片段、轮询与读法。协议语义、档位与「为什么」的叙述不在这里复述。
通道表里的路径一律相对外层工程根 `Client/`；片段里给的是同一批路径的绝对写法。

## 通道与落盘文件

| 用途 | 路径 |
|---|---|
| 测试请求 | `Temp/MoiraiTestRequest.json` |
| 请求被取走后的留痕 | `Temp/MoiraiTestRequest.consuming.json` |
| 取消在途单 | `Temp/MoiraiTestRequest.cancel.json` |
| PlayMode 续跑认领 | `Temp/MoiraiTestRunState.json` |
| 编辑器状态快照（约每 1 秒覆写） | `Temp/MoiraiEditorState.json` |
| 编辑器命令请求 | `Temp/MoiraiEditorCommand.json`，取走留痕 `.taken.json` |
| 编辑器命令回执 | `Temp/MoiraiEditorCommand.result.json` + `.done` |

## 投单前：判活与接单窗口

```bash
cat "E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiEditorState.json"
python -c "import os;print('request path is file:', os.path.isfile(r'E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiTestRequest.json'))"
```

| 读什么 | 形状 |
|---|---|
| 心跳 | `now - unix` 的秒数；到几秒即主线程没在跑 `update` |
| 接单窗口 | `testRunActive` 为 `0`，且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 `false` |
| 请求路径 | 必须是普通文件——同名目录让 `File.Exists` 恒假 |
| 读取时机 | 每次投单前自己读一遍，不复用别人的空闲读数 |

## 请求字段全集

```json
{"id":"<唯一串>","mode":"EditMode","output":"<绝对路径>/report.txt","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],"tests":["<命名空间.类名.方法名>"]}
```

| 字段 | 必填 | 取值形状 |
|---|---|---|
| `id` | 是 | 每单唯一的串；回执 `.done` 的内容就是它 |
| `mode` | 是 | `EditMode` / `PlayMode` |
| `output` | 是 | 报告绝对路径；同名旧产物与 `.done`、`.error` 在开跑前清掉 |
| `timeoutSeconds` | 否 | 墙钟秒数，`0` 或缺省为不限时；编译、导入与域重载的等待计入 |
| `assemblies` | 与 `tests` 至少一个非空 | 程序集名数组 |
| `tests` | 与 `assemblies` 至少一个非空 | 用例全名数组 |

## 选粒度：一格 / 一类 / 整程序集

| 目标 | 写法 |
|---|---|
| 一格 | `"tests":["Policy.ReflectionPolicyGuardTests.<方法名>"]` |
| 一类 | 把该类每一格的全名逐个列进 `tests`：数组内是「或」，`!` 前缀是排除 |
| 一类，图省事 | 只填 `assemblies`、`tests` 留空 ⇒ 整个程序集跑 |
| 参数化用例 | 全名带括号里的参数，形如 `MyTestClass2.MyTestWithMultipleValues(1)` |

`tests` 走 UTF 的 `Filter.testNames`，匹配是整名相等（`命名空间.类名.方法名`），不是前缀也不是正则——
请求 DTO 只转 `assemblies` 与 `tests`，带正则的那档到不了这条桥。圈漏了不会报错：按首行的计数形状，没圈到东西就是
`passed 0` 与 `failed 0` 并排，先读计数再谈结论。

配置层判据不走这条桥：`.claude/commands` 的装配存在性、frontmatter 与档位写权限、符号真名、命令表对账与文档指针
由本地脚本 `.claude/skills/moirai-conventions/scripts/check_config_surface.py` 一趟出退出码，不需要开着的编辑器，
形状见 `.claude/skills/moirai-conventions/references/gates/symbol-truth.md` 的《复现》。

## 投递、等待与读法

```bash
REQ_ID="gate-$(date +%s)"
OUT="E:/Workspace/Unity/Game Dev Tools/Client/Temp/${REQ_ID}.txt"
TMP="E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiTestRequest.json.tmp"
cat > "$TMP" <<JSON
{"id":"$REQ_ID","mode":"EditMode","output":"$OUT","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],
 "tests":["<命名空间.类名.方法名>"]}
JSON
mv "$TMP" "E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiTestRequest.json"
for _ in $(seq 1 60); do [ -f "$OUT.done" ] && break; sleep 3; done
cat "$OUT.done"; cat "$OUT"
```

- 先写 `.tmp` 再 `mv` 原子改名：直接写目标名会让驱动读到半份 JSON。
- `report.txt` 首行形状：`run <id> | passed N | failed N | skipped N | <秒>s`；以 `ABORTED` 收口的首行形状：
  `run <id> | ABORTED: <原因> | collected passed N | failed N | skipped N | wall <秒>s`。
- 失败正文逐格附在首行之后，**每条最多 6 行**（缩进占行）⇒ 一趟红最多稳定露出 5 条违例。要全量名单就把它设计成收集式断言或分批投。
- `{output}.progress` 是在跑的用例全名，判卡死用；收口时删除。
- `{output}.done` 的内容是本次 `id`，只认配对的这份。

## 取消在途单

```bash
echo "$REQ_ID" > "E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiTestRequest.cancel.json"
```

写要取消的请求 `id`，裸文本或 `{"id":"..."}` 均可。

## 新鲜度三段（缺一不算判绿）

| 段 | 比什么 |
|---|---|
| 1 | 逐源树比它归属的那份产物 mtime 有没有越过自己的改动时刻：`Runtime/` → `Moirai.Atropos`、`Tests/EditorMode/` → `Moirai.Atropos.Tests.EditorMode` |
| 2 | `domainSeq` 有推进 |
| 3 | `domainDllUnix` 等于本次所投程序集在 `assemblies[{name,unix}]` 里的 mtime |

| 字段 | 读法 |
|---|---|
| `consoleErrors` | Console 当前条数；刷新后 dll 没越过改动时刻且它有增量 = 编译失败 |
| dll 没变新 | 磁盘上没东西可编时 Bee 不重写 dll，单独不构成失败判据 |

## 命令桥：refresh 与 recompile 的次序

```bash
CMD_TMP="E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiEditorCommand.json.tmp"
printf '{"id":"%s","action":"refresh"}' "cmd-$(date +%s)" > "$CMD_TMP"
mv "$CMD_TMP" "E:/Workspace/Unity/Game Dev Tools/Client/Temp/MoiraiEditorCommand.json"
```

| 项 | 形状 |
|---|---|
| `action` | `focus` / `refresh` / `recompile` |
| 增删或移动 `.cs` 之后 | 投 `refresh` 等 `.cs.meta` 与编译落地，不叠 `recompile`；要跳过磁盘检测强制重编时才用 `recompile`，播放中不接受 |
| 回执语义 | 只答命令有没有被执行；效果一律回状态文件读 |
| `refresh`/`recompile` 的回执 | 受理即落一份带配对 `.done` 的回执，结论覆写在同一份回执上——动作可能当场把本域拆走，覆写那一次不一定执行得到 |
| 读到「已受理」 | 就此收手：回状态文件核 `domainSeq`、`assemblies[].unix` 与 `consoleErrors`；不等第二份回执，也不重投同一单 |

## 桥不可用时

编辑器不响应、心跳陈旧、或请求路径不是普通文件 ⇒ 停手报 BLOCKED，附状态文件与路径判定的原文读数。
