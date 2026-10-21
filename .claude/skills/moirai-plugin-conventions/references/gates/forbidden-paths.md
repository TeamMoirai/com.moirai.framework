# 禁碰路径与「为什么搬了就坏」

动手前对着本表过一遍目标路径。多数失效不是编译错误，而是编译照过、运行期或资产侧才断。

| 路径 | 失效方式 |
|---|---|
| 根 `package.json` | 描述目录分支；改 repository/version 会影响包管理器识别 |
| 插件 manifest | `id` 与目录名不一致时包管理器找不到；地板版本错会让安装被拒 |
| 插件 asmdef | 引用/宏错会让插件静默不编译 |
| `*/Tests/` 外的测试代码 | 会混入插件运行期程序集 |
| 外部 SDK 源码 | 不 vendored；保持外部包边界 |

## 共享工作树

- 提交前先 `git status --short`，标出别人未提交的文件；它们不进本轮结论，也不进本轮暂存。
- 不用 `git add -A` / `git add .`；按显式路径暂存。
- 不在并行会话活跃时整文件重写共享文档。
- 不执行 `git push` / `git clean`（`settings.json` 已 deny，但仍要自律）。

## deny 的射程边界

`.claude/settings.json` 只是工具层的一道意图声明：它挡 `Edit`/`Write` 的常见路径，但不覆盖 `Bash` 的写能力；`Edit(glob)` 与 `Bash(prefix:*)` 的匹配语义需要在真实会话里验证。写保护由 `settings.json`、命令正文的"只读"约束与本清单三处分担。
