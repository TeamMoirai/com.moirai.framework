---
name: gate:policy
description: 只读跑本包结构判据：.meta 成对、asmref/asmdef 指向、禁碰面与 .claude 指针存在性
when_to_use: 改完程序集/目录/metas 或提交前做本地结构自查时
argument-hint: [无参数]
arguments: []
allowed-tools: Read, Grep, Glob, Bash(python:*), Bash(git status:*), Bash(git diff:*), Bash(git log:*)
---

## 判据

- 退出码 0 才算过；每项分别报 pass / fail / 未取证。
- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无 是事实，不是 fail。
- 只读：不改源码、不改 `.meta`、不修 asmref、不跑 git 写命令。

## 流程

1. `.meta` 成对：对 ['addressables', 'fmod', 'litmotion', 'memorypack', 'messagepack', 'newtonsoft-json', 'photon-fusion', 'primetween', 'protobuf', 'serilog', 'unity-cloud-save', 'unity-logging', 'wwise', 'zlogger', 'zstring'] 逐树比较「源文件 → 期望 meta」与「实际 meta」。
2. asmdef/asmref：逐个插件检查 asmdef 是否存在、名称是否 `Moirai.Plugin.<名>`、测试 asmdef 是否只在 `*/Tests/`；例外是 `primetween/PrimeTween/PrimeTween.Runtime.asmref`（vendored PrimeTween 并入插件程序集），其余无根 asmref。。
3. 禁碰面：`git status --porcelain` 里出现 `ProjectSettings/`、`.github/`（不存在）、插件 manifest 的 `id`/版本 时逐条报。
4. 指针存在性：抽取 `.claude/**/*.md` 与 `CLAUDE.md` 里的包根相对路径，去掉 `:行号` 后缀后逐条检查。
5. 版本归属：比较工作树 `package.json` 的 `version` 与 `git show HEAD:package.json`。

## 可执行片段

```bash
python - <<'PY'
import json, re, subprocess
from pathlib import Path

ROOT = Path.cwd()
for tree in ["addressables", "fmod", "litmotion", "memorypack", "messagepack", "newtonsoft-json", "photon-fusion", "primetween", "protobuf", "serilog", "unity-cloud-save", "unity-logging", "wwise", "zlogger", "zstring"]:
    base = ROOT / tree
    if not base.exists():
        continue
    srcs = {str(p) + ".meta" for p in base.rglob("*") if p.suffix in {".cs", ".asmdef", ".asmref"}}
    metas = {str(p) for p in base.rglob("*") if p.name.endswith((".cs.meta", ".asmdef.meta", ".asmref.meta"))}
    missing, orphan = sorted(srcs - metas), sorted(metas - srcs)
    print(f"[meta] {tree}: missing={len(missing)} orphan={len(orphan)}")
    for p in missing + orphan:
        print("   ", p)

for p in sorted(ROOT.rglob("*.asmref")):
    rel = p.relative_to(ROOT).as_posix()
    m = re.search(r"GUID:([0-9a-fA-F]+)", p.read_text(encoding="utf-8-sig"))
    guid = m.group(1).lower() if m else ""
    print(f"[asmref] {rel} -> {guid}")

out = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True).stdout
for line in out.splitlines():
    if any(k in line for k in ["ProjectSettings/", ".github/"]):
        print("[forbidden]", line)

pat = re.compile(r"`([^`\n]+)`")
roots = "addressables/", "fmod/", "litmotion/", "memorypack/", "messagepack/", "newtonsoft-json/", "photon-fusion/", "primetween/", "protobuf/", "serilog/", "unity-cloud-save/", "unity-logging/", "wwise/", "zlogger/", "zstring/", ".claude/", "README.md", "package.json", "CLAUDE.md"
missing = []
for md in list(ROOT.rglob(".claude/**/*.md")) + [ROOT / "CLAUDE.md"]:
    if not md.exists():
        continue
    for token in pat.findall(md.read_text(encoding="utf-8")):
        token = re.sub(r":\d+(-\d+)?$", "", token.strip())
        if not token.startswith(roots) or token.startswith(".."):
            continue
        if "<" in token or "**" in token or "..." in token:
            continue
        if not (ROOT / token).exists():
            missing.append((md.relative_to(ROOT).as_posix(), token))
print(f"[pointer] missing={len(missing)}")
for md, token in missing:
    print("   ", md, "->", token)

try:
    head = json.loads(subprocess.run(["git", "show", "HEAD:package.json"], capture_output=True, text=True, check=True).stdout)
    cur = json.loads((ROOT / "package.json").read_text(encoding="utf-8-sig"))
    print(f"[version] HEAD={head.get('version')} worktree={cur.get('version')}")
except Exception as exc:
    print("[version] UNVERIFIED", exc)
PY
```

## 输出

```markdown
## 结构判据

| 项 | 结果 | 证据 |
|---|---|---|
| meta 成对 | pass/fail | missing=N, orphan=N |
| asmdef/asmref | pass/fail | 非法目标清单 |
| 禁碰面 | 有/无 | git status 行 |
| 指针存在性 | pass/fail | 缺失清单 |
| 版本归属 | pass/fail/未取证 | HEAD vs 工作树 |

未取证：
```
