# 提交与发版细则（框架包）

本块是本包该主题的唯一权威落点：`CLAUDE.md` 与命令正文只留摘要与指针，同一段话在两边各写一遍即视为缺陷。

### 4. 提交时的文档与 CHANGELOG

- `CHANGELOG.md` **只有 `[Unreleased]` 一段**：已发布的内容不留在文件里。发版由 `build-release` 工作流收尾——先从该段切出 GitHub Release notes（`>` 引言 + 摘要 + `<details>` 详细日志），再打**一笔** commit：subject 为 `chore(automate): Bump version to v{版本}`，body 为 `>` 之后到 `<details>` 之前的摘要（`[Unreleased]` 里第一个 `###` 之前的段落）；同 commit 写入 `package.json` 的 `version` 并把 `CHANGELOG.md` 整份重置为空白模板，随后用这笔 commit 打 tag 建 Release 并 Publish。版本号与 `CHANGELOG` 重置同 commit，不在手上改。两条硬约定：**发版进行中不要往 `[Unreleased]` 写新条目**（合并 commit 整文件换模板，窗口期内新写的会一并消失且不进任何 Release）；**重跑时若该 Release commit 已落地**，notes 从其父提交的 CHANGELOG 重新切出，不要手工回填上一版条目。
- `CHANGELOG.md` 按**后覆盖**维护：一条只写当前仍然成立的净结果。加了又删的开关、改到一半的命名、逐轮刷新的测试格数与成员计数、当时判为"不采纳"的观察一律不立条目；同一件事被后续提交推翻时，改掉或删掉原条目，不要再追加一条把它推翻。
- 诊断过程与来龙去脉的要点写进 commit message（正文几行内收口，禁长篇叙事铺陈），不进 CHANGELOG；CHANGELOG 面向 release note 读者——只写净结果与迁移口径，一条一行不折行。破坏性变更前置 ⚠ 并给出迁移口径。
- `Documentation~/zh` 与 `Documentation~/en` 是成对副本，接口改动必须双语同步；文档里的类名、成员名与菜单路径要对着代码核真名——`E` 前缀、单复数这类差别会让照文档写出的代码直接编译不过。

### 5. 提交 PR（标准流程：推送 + 一键预填链接）

用户说「提交PR」时按此流程——不依赖 gh CLI / GitHub API，推送通道与凭据按所在环境的记忆记录取用，不在本文件固化：

1. **推送分支到 origin**：`git push origin <分支>:refs/heads/<分支>`（本包 origin 为 `TeamMoirai/com.moirai.framework`）。
2. **生成预填 PR 链接交用户点击即建**（compare 页的 `title`/`body` 查询参数会预填表单）：
   - 模板：`https://github.com/TeamMoirai/com.moirai.framework/compare/master...<分支>?expand=1&title=<URL编码标题>&body=<URL编码正文>`
   - 标题一行说清主题；正文按批列点 + 验证结果，遵循提交精简纪律。
3. **PR 合并后**：`git fetch origin && git checkout master && git merge --ff-only origin/master` 同步本地主干；已推送分支不 rebase/amend。

