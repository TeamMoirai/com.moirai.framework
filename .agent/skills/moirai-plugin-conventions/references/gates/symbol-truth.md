# 符号真名口径

命令、技能与文档里出现的每个类型/成员，都要能指到声明处。指不到就写"未取证"。

## 三档归属

| 档 | 含义 | 怎么核 |
|---|---|---|
| A：本包声明 | 类型/成员在本包内声明 | `grep -rn "class <T>\b" --include='*.cs' 15 个插件目录（`addressables/` … `zstring/`）`，再打开声明行确认命名空间 |
| B：兄弟包声明 | 来自 framework/gameplay/juice 等 | 到对应兄弟包目录找声明；文档里写明所有者 |
| C：第三方/外部集成 | Odin、TMP、Cinemachine、外部 SDK | 先看 asmdef 的 `versionDefines` 与代码 `#if`；未安装/未编译的分支不能当作现行行为 |

## 本包常见符号的归属

| 符号 | 归属 |
|---|---|
| `Moirai.Atropos.*` 框架类型 | 框架包 `com.moirai.framework` |
| 插件类型 | 各自 `Moirai.Plugin.*` asmdef |
| ZLogger/MessagePack/Protobuf 等 | 外部包或插件内适配层 |
| SparkyTools 等第三方 | 外部包 |

## 引用纪律

- 文档里给 `文件:行号` 前先打开文件核对；行号会漂移，交付前重取。
- 只看到 `using` 不等于类型由本包声明；先找声明行。
- 兄弟包成员写明所有者，避免读者以为可以在本包内改。
- 第三方成员必须同时给出宏条件；未安装时不描述其运行期行为。
- 找不到声明时写"未取证/未找到声明"，不编造成员名。
