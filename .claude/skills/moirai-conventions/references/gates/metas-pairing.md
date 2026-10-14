# `.meta` 成对：范围口径

Unity 给每个参与导入的资产写一份同名 `.meta`（里面是 GUID 与导入器设置）。资产与它的 `.meta` 必须在同一个提交里进去：
只提交资产，别人拉下来会由 Unity 现生成新 GUID，同目录两份产物从此各说各话；只提交 `.meta` 则是悬空引用。

## 需成对的范围

只有参与 Unity 导入的目录需要成对提交：`Runtime/`、`Editor/`、`Tests/` 这三棵树。
三处的 `.cs` 与 `.cs.meta` 一对一，是可以用一条 shell 断言的不变量。

## 不参与导入、不建 `.meta` 的范围

| 范围 | 形态 |
|---|---|
| 点目录 `.claude/`、`.github/` | 不参与导入 ⇒ 目录内**不建** `.meta`。已入库的配置层文件一条 `.meta` 都没有，本层新建的每个文件也保持这个形状 |
| 尾带 `~` 的目录 `Samples~`、`Templates~`、`Documentation~` | 被排除在导入外。这些目录里历史上存过 `.meta`，但**新增文件不必补**——把它们的缺席当成缺项去手搓一份，只会造出 Unity 不会再读的假资产 |
| `docs/` | 被包根 `.gitignore` 的 `[Dd]ocs/` 排除，底稿不入库，`.meta` 更不入库 |
| `SourceGenerators/Source~/` | `~` 目录不参与导入；无差的地毯式核对会在这里报一堆「缺 .meta」，那是量具射程问题不是缺陷 |

## 会话底稿不被 gitignore 兜住

包根 `.gitignore` 没有 `.superpowers` 这一条（它只写了 `[Dd]ocs/` 与 `[Dd]ocs.meta`）；把 `.superpowers/sdd/**` 挡在提交外的
是 `.superpowers/sdd/.gitignore` 里的一条 `*`，它护不住直接落在 `.superpowers/` 下的散落文件——那种路径连 `git check-ignore` 都不命中。
于是风险方向是「未被忽略的会话底稿被 `git add -A` 一并收进提交」，而不是「它已被忽略、随便扫」。
它作为会话底稿不入库靠的是提交纪律：`git add` 只逐个点具体路径，禁 `git add -A`、禁 `git add .`。

`.cs.meta` 的 GUID **由 Unity 生成**：新增 `.cs` 只写源文件，经命令桥投 `refresh` 等它落地 `.meta`
（投递与回执形状见 `.claude/skills/moirai-conventions/references/gates/bridge-protocol.md`），再连 `.meta` 一起提交。
手写 `.meta` 的两条风险：GUID 撞车会静默断掉所有引用它的人；内容形状与同目录既有 meta 不一致（本仓有只带 `fileFormatVersion` + `guid` 两行的 meta，属正常形态，不是缺陷）。

## 核对片段

配对不变量（按范围取，别拿全仓 `.cs` 去比）：

```bash
git ls-files Runtime Editor Tests | awk '/\.cs$/' | while read -r f; do
  [ -f "${f}.meta" ] || echo "缺 .meta: $f"
done
```

提交后的形状核对（点目录里出现 `.meta` = 手写错了，`git rm --cached` 补正并删文件）：

```bash
git show --name-only --oneline HEAD | grep '\.claude/.*\.meta$' && echo "点目录混进了 .meta" || echo "点目录无 .meta"
```

`.cs` 与 `.cs.meta` 计数对账（差值应为 0）：

```bash
echo "cs=$(git ls-files Runtime Editor Tests | grep -c '\.cs$') meta=$(git ls-files Runtime Editor Tests | grep -c '\.cs\.meta$')"
```
