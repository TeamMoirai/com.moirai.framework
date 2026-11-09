# 禁碰路径与「为什么搬了就坏」

动手前对着本表过一遍目标路径。多数失效不是编译错误，而是编译照过、运行期或资产侧才断。

| 路径 | 失效方式 |
|---|---|
| 宿主 `Packages/manifest.json` | 整文件重写会抹掉用户条目；必须先读再改 |
| 宿主 `ProjectSettings/` | 改错会让工程设置/玩家设置异常 |
| `Templates~/`（框架包内） | 本包只读；改模板会影响所有安装器用户 |
| `package.json` 的 `version` | 日常提交不碰 |
| 新增 asmdef | 会改变安装器的编译归属，属架构变更 |

## 共享工作树

- 提交前先 `git status --short`，标出别人未提交的文件；它们不进本轮结论，也不进本轮暂存。
- 不用 `git add -A` / `git add .`；按显式路径暂存。
- 不在并行会话活跃时整文件重写共享文档。
- 不执行 `git push` / `git clean`（`settings.json` 已 deny，但仍要自律）。

## deny 的射程边界

`.claude/settings.json` 只是工具层的一道意图声明：它挡 `Edit`/`Write` 的常见路径，但不覆盖 `Bash` 的写能力；`Edit(glob)` 与 `Bash(prefix:*)` 的匹配语义需要在真实会话里验证。写保护由 `settings.json`、命令正文的"只读"约束与本清单三处分担。
