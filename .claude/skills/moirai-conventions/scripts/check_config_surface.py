#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""`.claude/` 配置层量具。

跑什么：`.claude/commands/**` 的装配存在性、frontmatter 齐备与档位写权限、命令正文的框架符号真名、
`CLAUDE.md` 命令表与命令文件的双向对账、交付文档里的指针是否悬空、随包发行面的层隔离方向。

退出码三档，彼此可区分：
  0  各段判据全部真跑过且无违例（段数由本次输出自己数得出，不在文案里写死）
  1  跑过了，有违例（逐条点名并给出处）
  2  量不出来（目录取不到、登记表读不动、声明集自检不过、解析不出内容）——绝不等于合格

家规与 `Tests/Coverage/coverage-gate.ps1` 同源：看不见配置不等于配置合格；判据一趟都没跑成的状态
必须自己说话。

用法：
    python .claude/skills/moirai-conventions/scripts/check_config_surface.py
    python .claude/skills/moirai-conventions/scripts/check_config_surface.py --root <包根> --debt <登记表>

豁免只走同目录的 `config-surface-debt.json`：`frontmatter` 与 `symbols.dead_api` 是债务，单向棘轮——
每项必须仍命中，修好一条删一条，只准降不准升。`symbols.member_form` 与 `symbols.example_placeholder`
是口径外分类登记，不计入债务豁免，但既要仍命中候选集、又要抽取当场能复算出它的分类依据。

排除表（`non_framework_names`，B 档）的牙分三级，别把它读成「排除表整体有牙」：
①「同一个名字既坐在 symbols 的符号位又在排除表」有牙，直接红；
②「belongs 必须是外部根命名空间起头、末段等于该名字的限定名」有牙，只挡「Unity 的 UI 组件」这类含糊话；
③「删净原登记、只留一条写了诚实限定名的排除表项」无牙——判 A 还是判 B 的分界是「命令在此断言的是不是
本包的 API」，机器读不出来；要抓它得靠成员级判据（限定真名之后核被断言的成员），那是单列的活。
"""

import argparse
import json
import os
import re
import subprocess
import sys

EXIT_PASS = 0
EXIT_VIOLATION = 1
EXIT_CANNOT_MEASURE = 2

# ---------------------------------------------------------------- 口径常数

# frontmatter 必备键（顺序即缺键串顺序；改顺序会把全部登记项打成不等）
REQUIRED_KEYS = ("name", "description", "when_to_use", "argument-hint", "arguments", "allowed-tools")

# 写工具整词集合：`allowed-tools` 先按空白再按逗号切词后整词比对，不做子串匹配。
# 两种书写都要看得见：`Read, Edit` 一行式，与 YAML 清单式（`- Read` 每项一行）
WRITE_TOOLS = ("Edit", "Write", "MultiEdit", "NotebookEdit")
# 已知书写只有上面两种。其余值形态（YAML flow sequence `[Read, Edit]`、折叠/字面标量 `>`/`|` 一类
# 切不出工具名的写法）单列一段按违例上报：切词切不出不等于「没声明写工具」，把它当非空值放行就是静默绿。
# 切词前剥掉的字符就是这两种形态的括号
FLOW_COLLECTION_CHARS = "[]{}"
KNOWN_TOOL_WRITINGS = ("inline", "list")
TOOL_NAME_START_RE = re.compile(r"[A-Za-z_]")

# 有写权限的档位；扁平形态下档位由文件名推导，重组期没有一支落在这两档里
WRITABLE_TIERS = ("make", "change")
KNOWN_TIERS = ("make", "change", "check", "gate")

# 框架符号书写形态的尾缀表。缩小它能把红变成绿且不改变命令里的死 API，所以它只在判据口径里改
TYPE_SUFFIXES = (
    "Service", "Handler", "Window", "Settings", "Attribute", "Event", "Base", "Args",
    "Manager", "System", "Bridge", "Lease", "Entry", "Slot", "Options", "Request",
)

# 声明集：扫这两棵源树的类型声明（含嵌套类型）
DECLARED_ROOTS = ("Runtime", "Editor")
# 量具自检 = 规模下限 + 三条必然非零对照；只查规模会让一个抓漏一半的抽取给出看着可信的假绿
DECLARED_MIN = 600
DECLARED_CONTROLS = ("ServiceBase", "UIWindow", "EventManager")

# 示例占位形态（系统性书写，不是对本包类型的断言）：CamelCase 首段命中即归一化丢掉
PLACEHOLDER_HEAD_STEMS = ("Some", "Any", "Foo", "Xxx", "My", "Sample", "New", "Old")
# 示例占位形态：任一 CamelCase 段命中即丢掉。这条路只在「这些段在本包声明名的 CamelCase 段里一名不存」
# 时才不可能吃掉真实类型名，所以那句话不写在注释里当口头保证，由 declared_type_names 的自检当场断言
PLACEHOLDER_ANY_STEMS = ("Some", "Any", "Foo", "Xxx")
# 示例代入的词根门槛：短于这个长度的词根（`IEvent` 的 `I`）在散文里以普通词身份出现的概率太高，
# 不能当「这一行交代过被代入的参数名」的证据
EXAMPLE_STEM_MIN_LEN = 4

# 登记表的 symbols 段按三档分节：只有 dead_api 是债务（走棘轮），另两节是口径外分类登记。
# 节名与 class 必须自洽，class 只校验不认它自己写的是什么档位——纯装饰的标签等于没分档
SYMBOL_SECTIONS = ("dead_api", "member_form", "example_placeholder")
SECTION_CLASS = {"dead_api": "A", "member_form": "C", "example_placeholder": "C"}
EXCLUSION_CLASS = "B"
VALID_CLASSES = ("A", "B", "C")

# 排除表的 belongs 只认这些外部根命名空间起头的限定名。每一项都核过本包确实在用这支外部程序集：
# System / UnityEngine / UnityEditor / Sirenix / YooAsset / Newtonsoft 见 `Runtime/`+`Editor/` 源码的
# `using` 顶层命名空间（YooAsset 另由 `Editor/Services/Resource/YooAsset/Internal/YooAsset.Editor.asmref`
# 接进来），NUnit 见 `Tests/EditorMode` asmdef 的 nunit.framework.dll 预编译引用。表外的合法外部根命名空间
# 会当红报出，补项要拿同样的程序集证据，不许为了让某条登记转绿而往这张表里塞根段——
# 那和缩小尾缀表是同一类拆判据的动作
EXTERNAL_ROOTS = ("System", "UnityEngine", "UnityEditor", "Sirenix", "NUnit", "YooAsset", "Newtonsoft")
QUALIFIED_NAME_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*")

# 受检根段：反引号内以它们起头的路径一律按包根解析
POINTER_ROOTS = (".claude/", "Tests/", "Runtime/", "Editor/", "Documentation~/", "Samples~/",
                 "Templates~/", "Specs~/")
# 非字面路径不受检：模板、交替、glob、尖括号占位与运行期产物解析不到具体目标
NON_LITERAL_CHARS = set("*?<>{}|[]$ \t\"'")
# 只活在检出环境的层：随包发行的文档里出现这些根段的路径指针，在别人机器上必悬空。
# 它们故意不在 POINTER_ROOTS 里——用 POINTER_ROOTS 过滤就等于永远看不见要抓的东西。
# 运行期产物（外层工程的 Temp、Library、obj）不在本表：与判据 E 同口径，干净检出上它们本就不存在，
# 受检就是假红，而文档里点名 `Temp/MoiraiEditorState.json` 这类桥文件是正当用法
IGNORED_ROOTS = ("docs/", ".superpowers/", ".codely-cli/")
# 判据 F 的射程 = 随包发行面。未入库的在途产物不判（与判据 E 同理由）
SHIPPED_SURFACES = ("Documentation~/", "README.md", "README_EN.md", "CONTRIBUTING.md",
                    "CHANGELOG.md", ".claude/", "Specs~/")
# 给消费者看的那一层：只讲用法，不许指进内建契约层
PUBLIC_ONLY = ("Documentation~/", "README.md", "README_EN.md")
# skill 根相对写法（`references/…` 一类）不受检也不该再写：两种解析基座并存必脆
# 成员名不在「类型是否存在」这一口径内：本判据只答类型面，方法、字段与生成成员一律不查

CAMEL_SEG_RE = re.compile(r"[A-Z][a-z0-9]*|[A-Z]+|[a-z0-9]+")
WORD_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
FENCE_RE = re.compile(r"^\s*(`{3,})([A-Za-z0-9_+~-]*)\s*$")
INLINE_SPAN_RE = re.compile(r"`([^`\n]+)`")
EXT_TAIL_RE = re.compile(r"\.\w{2,4}$")
# `event <Type> Name;` 的声明位：Name 前面必须是 `event` + 一个类型书写，后面直接收尾
EVENT_DECL_PREFIX_RE = re.compile(r"\bevent\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^<>]*>)?\s+$")
EVENT_DECL_SLOT_RE = re.compile(r"\s*(?:<[^<>]*>)?\s*[;,{=]")
# `_ = Name;` 的丢弃目标位：`_` 与 `=` 之间不许有别的书写，右值必须直接以 `;` 收尾
DISCARD_RVALUE_PREFIX_RE = re.compile(r"\b_\s*=\s*$")
# 词级路径判定用：围栏行里紧挨 `/` 的词、以及带这些扩展名的词是文件名/目录名，不是类型名的断言
KNOWN_EXT_RE = re.compile(r"\.(?:cs|md|json|py|asset|prefab|unity|uxml|uss|txt|dll|asmdef|asmref"
                          r"|meta|psd|png|tss|xml|html|yml|yaml|shader|cginc|tmpl|csv)\b")
COMMAND_ROW_RE = re.compile(r"^/[A-Za-z0-9_-]+$")


class ToolError(Exception):
    """量具自身跑不动，对应退出码 2。"""


class Reading:
    """一类判据的读数与违例收集器：让「跑过」与「没跑」在输出里永远是两回事。"""

    def __init__(self, tag, title):
        self.tag = tag
        self.title = title
        self.notes = []
        self.violations = []
        self.failures = []

    def note(self, text):
        self.notes.append(text)

    def violate(self, text):
        self.violations.append(text)

    def cannot_measure(self, text):
        self.failures.append(text)

    def dump(self):
        print("[%s] %s" % (self.tag, self.title))
        for line in self.notes:
            print("    读数      %s" % line)
        for line in self.violations:
            print("    VIOLATION %s" % line)
        for line in self.failures:
            print("    MEASURE-FAIL %s" % line)


# ---------------------------------------------------------------- 取数底座


def git_lines(root, args):
    """跑 git 取按行结果；git 本身不可用按「量不出来」上报，不许当成「没东西可查」。"""
    try:
        out = subprocess.run(["git", "-C", root] + args, capture_output=True, text=True,
                             encoding="utf-8", errors="replace")
    except OSError as exc:
        raise ToolError("git %s 跑不起来: %s" % (" ".join(args), exc))
    if out.returncode != 0:
        raise ToolError("git %s 退出码 %d: %s" % (" ".join(args), out.returncode, out.stderr.strip()))
    return [line for line in out.stdout.replace("\r\n", "\n").split("\n") if line]


def read_text(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    if b"\x00" in raw[:4096]:
        raise ToolError("%s 看着不是文本（前 4096 字节含 NUL）" % path)
    return raw.decode("utf-8-sig", errors="replace").replace("\r\n", "\n")


def rel_of(path, root):
    return os.path.relpath(path, root).replace(os.sep, "/")


def collect_md_files(dir_path, reading, what):
    """取目录下全部 `.md`。目录取不到或取到 0 份都是量不出来，专属文案与「合格」区分开。"""
    if not os.path.isdir(dir_path):
        reading.cannot_measure("%s 目录取不到: %s —— 看不见装配不等于装配合格" % (what, dir_path))
        return None
    files = []
    for base, _dirs, names in os.walk(dir_path):
        for name in names:
            if name.endswith(".md"):
                files.append(os.path.join(base, name))
    files.sort()
    if not files:
        reading.cannot_measure("%s 目录下取到 0 个 `.md`: %s —— 没有可判对象不等于合格" % (what, dir_path))
    return files


# ---------------------------------------------------------------- frontmatter 底座


def strip_frontmatter(text):
    """去掉 `---` 头，只留正文——A 段的「空壳」判据看的就是这部分。"""
    return split_frontmatter(text)[2]


def split_frontmatter(text):
    """返回 (键值表或 None, 用了清单形态的键集合, 正文)。

    没有 `---` 头时键值表给 None，让缺键串把整份必备键都报出来。
    """
    if not text.startswith("---\n"):
        return None, set(), text
    end = text.find("\n---", 4)
    if end == -1:
        return None, set(), text
    head = text[4:end + 1]
    tail_start = text.find("\n", end + 1)
    body = text[tail_start + 1:] if tail_start != -1 else ""
    keys, list_form = parse_frontmatter_keys(head)
    return keys, list_form, body


def parse_frontmatter_keys(head):
    """取键值，并记下哪些键的值是 YAML 清单形态拼出来的。

    清单形态不能和一行式混成同一个值再按逗号切——`- Read`/`- Edit` 拼成 `Read Edit` 之后
    逗号切不出 `Edit`，只读档位声明写工具就静默通过（豁免面宽于判据面）。这里把边界留成显式集合。
    """
    keys = {}
    list_form = set()
    current = None
    for line in head.split("\n"):
        if not line.strip():
            continue
        top = re.match(r"^([^\s:][^:]*?)\s*:\s*(.*)$", line)
        if top:
            current = top.group(1).strip().strip("\"'")
            keys[current] = top.group(2).strip()
        elif line.lstrip().startswith("- ") and current is not None:
            keys[current] = (keys[current] + " " + line.lstrip()[2:].strip()).strip()
            list_form.add(current)
    return keys, list_form


# ---------------------------------------------------------------- 判据 A：装配存在性


def judge_assembly(root, reading):
    commands_dir = os.path.join(root, ".claude", "commands")
    files = collect_md_files(commands_dir, reading, ".claude/commands")
    if files is None:
        return None
    shells = []
    for path in files:
        if not strip_frontmatter(read_text(path)).strip():
            shells.append(rel_of(path, root))
    reading.note("命令文件取到 %d 个，其中空壳 %d 个" % (len(files), len(shells)))
    for rel in shells:
        reading.violate("命令文件是空壳（去掉 frontmatter 后没有正文）: %s" % rel)
    return files


# ---------------------------------------------------------------- 判据 B：frontmatter 与档位写权限


def derive_tier(rel_path):
    """`commands/<档位>/<name>.md` 取目录名；扁平形态 `commands/<name>.md` 取文件名（重组期成立）。"""
    parts = rel_path.split("/")
    parent = parts[-2] if len(parts) >= 2 else ""
    stem = os.path.basename(rel_path)[:-3]
    return parent if parent in KNOWN_TIERS else stem


def write_tools_in(allowed_value):
    """先按空白再按逗号切词，整词比对写工具集合。

    只按逗号切的话，YAML 清单形态拼出来的 `Read Edit` 匹配不到任何写工具，
    「只读档位不得声明写工具」整段空转；先按空白切再按逗号切，两种书写都在射程内。
    切词前还要剥掉 `[]` 与 `{}`：`[Read, Edit]` 这种 flow sequence 的 chunk 是 `[Read` 与 `Edit]`，
    不剥括号就一个工具名都切不出来，而值本身非空 ⇒ 既不算缺键也不报写权限，measured 空串直接绿。
    """
    found = []
    for chunk in re.split(r"[\s,]+", allowed_value):
        word = chunk.strip().strip("`\"'" + FLOW_COLLECTION_CHARS)
        if word in WRITE_TOOLS:
            found.append(word)
    return found


def allowed_tools_writing(allowed_value, in_list_form):
    """值形态归类：`inline`（一行式逗号）、`list`（YAML 清单）是两种已知书写，其余是 `flow`/`unreadable`。

    `flow` = 值里带 `[]`/`{}`（YAML flow sequence / flow mapping）；`unreadable` = 切出来的 chunk 没有一个
    以字母或下划线起头（`allowed-tools: >` 这类折叠/字面标量在行解析里只剩一个 `>`）。
    这两种形态按违例单列一段，不当成「没声明写工具」放行。
    """
    stripped = allowed_value.strip().strip("`\"'")
    if any(ch in stripped for ch in FLOW_COLLECTION_CHARS):
        return "flow"
    if in_list_form:
        return "list"
    chunks = [chunk for chunk in re.split(r"[\s,]+", stripped) if chunk]
    if not any(TOOL_NAME_START_RE.match(chunk) for chunk in chunks):
        return "unreadable"
    return "inline"


def measure_frontmatter(root, command_files, debt, reading):
    entries = debt["frontmatter"]
    if not isinstance(entries, dict):
        reading.cannot_measure("登记表 frontmatter 段不是对象（键为命令文件名）: %s"
                               % type(entries).__name__)
        return
    for name, value in entries.items():
        if not isinstance(value, dict) or "measured" not in value or "reason" not in value:
            reading.cannot_measure("登记表 frontmatter 项 `%s` 不是 {measured, reason} 形状" % name)
            return
    exempted = 0
    violating = 0
    for path in command_files:
        rel = rel_of(path, root)
        name = os.path.basename(rel)
        keys, list_form, _body = split_frontmatter(read_text(path))
        tier = derive_tier(rel)
        parts = []
        writing = None
        if keys is None:
            parts.append("missing-keys:" + ",".join(REQUIRED_KEYS))
        else:
            # 空值即缺键：`allowed-tools:` 挂个空值不算声明了工具，否则六键全空也绿
            missing = [k for k in REQUIRED_KEYS if k not in keys or not keys[k].strip()]
            if missing:
                parts.append("missing-keys:" + ",".join(missing))
            if "allowed-tools" in keys and keys["allowed-tools"].strip():
                writing = allowed_tools_writing(keys["allowed-tools"], "allowed-tools" in list_form)
                found = write_tools_in(keys["allowed-tools"])
                writable = tier in WRITABLE_TIERS
                if writable and not found:
                    parts.append("write-permission:tier=%s declares none of %s"
                                 % (tier, ",".join(WRITE_TOOLS)))
                elif not writable and found:
                    parts.append("write-permission:tier=%s declares %s"
                                 % (tier, ",".join(sorted(set(found)))))
                # 不属于两种已知书写的形态自己就是一处违例，不等别的违例出现才挂上来：
                # 它的后果恰好是让上面的写工具判定看不见工具名，只挂 riders 就是「唯一特征就是形态」
                # 的文件一条都不报，登记也无处可指
                if writing not in KNOWN_TOOL_WRITINGS:
                    parts.append("allowed-tools-shape=%s" % writing)
        # YAML 清单形态在登记串里单列一段：形态本身就是可指位置，手改漏一项、或清单与一行式互换，
        # 登记串不等值即红。清单是合法书写，所以这一段只随别的违例挂，不把合法写法本身变成债务
        if writing == "list" and parts:
            parts.append("allowed-tools-shape=list")
        # 缺键串与档位违例合并成同一条 measured 串走棘轮等值：
        # 分成两条的话，登记一条部分缺键的项会顺带豁免掉写权限检查
        measured = "; ".join(parts)
        if not measured:
            if name in entries:
                reading.violate("%s 已合格但登记表仍挂着豁免条目（只准降不准升，删掉它）: %s"
                                % (name, entries[name]["reason"]))
            continue
        violating += 1
        if name not in entries:
            reading.violate("%s (%s) 未登记的违例: %s" % (name, rel, measured))
        elif entries[name]["measured"] == measured:
            exempted += 1
        else:
            reading.violate("%s (%s) 现值与登记串不等（登记只豁免登记的那一条串）\n"
                            "              登记: %s\n"
                            "              现值: %s" % (name, rel, entries[name]["measured"], measured))
    for name in sorted(set(entries) - {os.path.basename(p) for p in command_files}):
        reading.violate("登记表 frontmatter 项 `%s` 今天挡不到任何命令文件，请删" % name)
    reading.note("命令文件 %d 个逐个数过；违例 %d 处，其中登记表豁免 %d 处；登记项共 %d 条"
                 % (len(command_files), violating, exempted, len(entries)))


# ---------------------------------------------------------------- 判据 C：符号真名


def declared_type_names(root, reading):
    names = set()
    cs_count = 0
    for base_dir in DECLARED_ROOTS:
        abs_dir = os.path.join(root, base_dir)
        if not os.path.isdir(abs_dir):
            reading.cannot_measure("声明集源树取不到: %s" % abs_dir)
            return None
        for base, _dirs, files in os.walk(abs_dir):
            for name in files:
                if not name.endswith(".cs"):
                    continue
                cs_count += 1
                try:
                    text = read_text(os.path.join(base, name))
                except (OSError, ToolError) as exc:
                    reading.cannot_measure("声明集源文件读不动: %s (%s)" % (name, exc))
                    return None
                for _kind, decl in re.findall(
                        r"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", text):
                    names.add(decl)
    absent = [c for c in DECLARED_CONTROLS if c not in names]
    if len(names) < DECLARED_MIN:
        reading.cannot_measure("声明集只取出 %d 个类型名（下限 %d）—— 抽取本身坏了，此时的绿不可信"
                               % (len(names), DECLARED_MIN))
        return None
    if absent:
        reading.cannot_measure("声明集缺必然非零对照 %s —— 抽取覆盖不足，此时的绿不可信"
                               % ", ".join(absent))
        return None
    # 按任一段丢示例占位这条路，只在那些段在本包声明名的 CamelCase 段里一名不存时才不可能吃掉真实类型名。
    # 这句保证不写在注释里，写成有牙的自检：词根一旦出现在声明段里，抽取就会静默放行真死 API
    declared_segs = set()
    for name in names:
        declared_segs.update(CAMEL_SEG_RE.findall(name))
    collided = [s for s in PLACEHOLDER_ANY_STEMS if s in declared_segs]
    if collided:
        reading.cannot_measure("示例占位的任一段词根 %s 出现在本包声明名的 CamelCase 段里 —— "
                               "按段丢会吃掉真实类型名，此时的绿不可信" % "/".join(collided))
        return None
    reading.note("声明集：扫 %s 下 %d 份 `.cs`，取到 %d 个类型名（下限 %d，对照 %s 全命中；"
                 "示例段词根 %s 全不在声明段里）"
                 % (" 与 ".join(DECLARED_ROOTS), cs_count, len(names), DECLARED_MIN,
                    "/".join(DECLARED_CONTROLS), "/".join(PLACEHOLDER_ANY_STEMS)))
    return names


def iter_lines(text):
    """产出 (line, is_fence, 反引号段区间)。

    围栏内的每一行整体当一个段，行内反引号各当一个段：正向候选同时覆盖两者，走同一条过滤链。
    围栏栈按长度与语言标记配对，让 ```markdown 里嵌套的 ```csharp 不会被错当成外层收口。
    """
    stack = []
    for line in text.split("\n"):
        fence = FENCE_RE.match(line)
        if fence:
            ticks, lang = len(fence.group(1)), fence.group(2)
            if not stack or lang:
                stack.append(ticks)
            elif ticks >= stack[-1]:
                stack.pop()
            else:
                stack.append(ticks)
            continue
        if stack:
            yield line, True, [(0, len(line))] if line.strip() else []
            continue
        spans = [(m.start(1), m.end(1)) for m in INLINE_SPAN_RE.finditer(line)]
        yield line, False, spans


def live_ranges(ranges, line, fence):
    """留下真正受检的段。

    行内段：段内含 `/` 或以 `.xx`–`.xxxx` 收尾时整段不受检（路径、文件扩展、菜单串、`A / B` 式并列清单），
    这条与原文径同源。围栏行整行受检——代码行里到处是 `//` 与 `a/b`，按整段跳会把骨架代码块全放出射程；
    围栏里的路径与文件名改按词级判定（见 scan_line 的 fence 分支）。
    """
    live = []
    for lo, hi in ranges:
        content = line[lo:hi]
        if not content.strip():
            continue
        if not fence and ("/" in content or EXT_TAIL_RE.search(content.strip())):
            continue
        live.append((lo, hi))
    return live


def gap_is_coordination(gap):
    """两个标识符之间只剩并列分隔符（含被剥掉的泛型/模板/实参括号）时算同一枚举串。"""
    stripped = gap
    while True:
        before = stripped
        stripped = re.sub(r"<[^<>]*>", "", stripped)
        stripped = re.sub(r"\{[^{}]*}", "", stripped)
        stripped = re.sub(r"\([^()]*\)", "", stripped)
        if stripped == before:
            break
    return re.fullmatch(r"[\s`/、,,]*", stripped) is not None


def owner_is_declared(line, offset, declared):
    owners = WORD_RE.findall(line[:offset])
    return bool(owners) and owners[-1] in declared


def is_discard_rvalue(line, start, end):
    """`_ = Handler;` 这一种写法：丢弃目标 `_` 加一个裸名右值，取用的是成员，不是类型断言。

    形状收到最小：赋值左边必须正好是 `_`，右值必须直接以 `;` 收尾。放宽成「任何 `= Name`」的话，
    `var probe = EventSystem.Create();` 这类真断言会整个被认成成员形态——那是把判据拆了。
    """
    if not DISCARD_RVALUE_PREFIX_RE.search(line[:start]):
        return False
    return re.match(r"\s*;", line[end:]) is not None


def is_event_decl_slot(line, start, end):
    """`public event EventHandler OnEvent;` 里坐在声明位上的 `OnEvent` 是成员名。

    前置必须是 `event` + 一个类型书写，后置必须直接收尾（`;`、`,`、`=`，或泛型实参后同样位置）——
    中间那个类型名（`EventHandler`）不满足，照旧进候选。
    """
    if not EVENT_DECL_SLOT_RE.match(line[end:]):
        return False
    return EVENT_DECL_PREFIX_RE.search(line[:start]) is not None


def example_stem_written(token, quoted_words, declared):
    """`<Module>Service` 拿示例参数代入出来的名字，同一行会把那个参数名写在反引号里（如 `Quest`，生成 `QuestService`）。

    这条只给 `example_placeholder` 分类提供机械依据，依据收到两处：词根长度 ≥ `EXAMPLE_STEM_MIN_LEN`，
    且词根就在同一行的行内反引号段里。放到「同一行的任意一个词」的话，`EventSystem` 能靠散文里随口一提的
    `Event` 自称示例代入、`IEvent` 能靠前缀 `I` 自称——那是给真死 API 发免检通道。
    围栏代码行整行受检、没有行内反引号可言，所以不产出示例依据：被代入的参数名是在正文里交代给读者的。
    """
    for suffix in TYPE_SUFFIXES:
        if token.endswith(suffix) and len(token) > len(suffix):
            stem = token[:-len(suffix)]
            if len(stem) >= EXAMPLE_STEM_MIN_LEN and stem in quoted_words and stem not in declared:
                return True
    return False


def is_placeholder(token):
    segs = CAMEL_SEG_RE.findall(token)
    if not segs:
        return False
    if segs[0] in PLACEHOLDER_HEAD_STEMS:
        return True
    return any(seg in PLACEHOLDER_ANY_STEMS for seg in segs)


def hits_type_shape(token):
    if any(token.endswith(suffix) for suffix in TYPE_SUFFIXES):
        return True
    return len(token) >= 4 and token[0] in ("E", "I") and token[1].isupper()


def scan_line(line, spans, declared, fence=False):
    """一行扫到底，返回 (候选命中, 形态命中, 归一化, 成员形态命中, 示例代入命中)，都是 [token]。

    成员形态在行内按并列串传播：`UIService.HasWindow<T>` / `GetWindow<T>` / `GetTopWindow()`
    里第一条成立，后两条同判成员形态——只认第一条会把同一条枚举拆成两个档位。
    长度不足或首字母小写的词（泛型参数 `T` 一类）既不参与判定，也不打断并列串。
    `fence` 为真时整行受检，目录段与文件名按词级让位。
    成员形态认五种书写：已声明类型的 `.` 接收者、`(` 调用、`=` 清单左值、`_ = Name;` 丢弃右值裸名、
    `event <Type> Name;` 声明位；并列传播的射程盖过这五种。
    成员命中与示例代入命中都仍是候选，只是单列出来给分档登记当机械依据。
    示例代入的依据只看行内反引号段里写出的词（`quoted_words`），围栏行不给。
    """
    hits = []
    shapes = []
    normalized = []
    members = []
    examples = []
    quoted_words = set()
    if not fence:
        for lo, hi in spans:
            quoted_words.update(WORD_RE.findall(line[lo:hi]))
    prev_end = None
    prev_member = False
    for match in WORD_RE.finditer(line):
        token = match.group(0)
        start, end = match.span()
        if len(token) < 4 or not token[0].isupper():
            continue  # 短词与首字母小写的词（泛型参数 `T` 一类）不参与判定，也不打断并列串
        prev_char = line[start - 1] if start else ""
        next_char = line[end] if end < len(line) else ""
        inside = any(lo <= start and end <= hi for lo, hi in spans)

        member = False
        if prev_char == "." and owner_is_declared(line, start, declared):
            member = True
        elif next_char == "(":
            member = True
        elif next_char == "=":
            member = True
        elif is_discard_rvalue(line, start, end):
            member = True
        elif is_event_decl_slot(line, start, end):
            member = True
        elif prev_member and prev_end is not None and gap_is_coordination(line[prev_end:start]):
            member = True
        prev_end, prev_member = end, member

        # 围栏行里的目录段与文件名按词级让位（整行不当成一条路径）
        if fence and (prev_char == "/" or next_char == "/" or KNOWN_EXT_RE.match(line[end:])):
            continue
        if not inside or not hits_type_shape(token):
            continue
        shapes.append(token)
        if member:
            members.append(token)
            continue
        if (prev_char and prev_char in "{}<>") or (next_char and next_char in "{}"):
            continue
        if is_placeholder(token):
            continue
        if token in declared:
            continue
        if (token + "Attribute") in declared:
            normalized.append(token + "Attribute")
            continue
        if example_stem_written(token, quoted_words, declared):
            examples.append(token)
        hits.append(token)
    return hits, shapes, normalized, members, examples


def extract_candidates(text, declared):
    """正向：候选 = 命中书写形态、又没被归一化与成员口径收掉的词。反向探针与它同量法。

    成员命中集与示例代入集是从候选里另切出来的两列读数，用来核验分档登记的说法有没有机械依据，
    不参与候选的增减。
    """
    candidates = set()
    shapes = set()
    normalized = set()
    members = set()
    examples = set()
    for line, is_fence, spans in iter_lines(text):
        hits, hit_shapes, hit_normalized, hit_members, hit_examples = scan_line(
            line, live_ranges(spans, line, is_fence), declared, is_fence)
        candidates.update(hits)
        shapes.update(hit_shapes)
        normalized.update(hit_normalized)
        members.update(hit_members)
        examples.update(hit_examples)
    return candidates, shapes, normalized, members, examples


def belongs_qualifies(token, belongs):
    """belongs 要给出一个「以外部根命名空间起头、末段等于该名字」的限定名，含糊归属话术不算证据。

    这条打死「Unity 的 UI 组件」这类写法——现实里把死 API 改称非框架同名者，多就是这么一句。
    它只到形状为止：`UnityEngine.EventSystems.EventSystem` 这种诚实限定名照样喂得过去，
    而命令在这里断言的到底是不是本包 API，形状判不出来（要判得核被断言的成员，是单列的活）。
    """
    for name in QUALIFIED_NAME_RE.findall(belongs):
        segments = name.split(".")
        if segments[0] in EXTERNAL_ROOTS and segments[-1] == token:
            return True
    return False


def validate_debt_shapes(debt, reading):
    """登记表形状与档位标签在跑判据之前校验。

    `class` 只写在登记里而不校验，分档就是纯装饰：串档（把活成员记成死 API）与改档不留痕都无从发现。
    """
    symbols = debt["symbols"]
    exclusions = debt["non_framework_names"]
    if not isinstance(symbols, dict) or not isinstance(exclusions, dict):
        reading.cannot_measure("登记表 symbols / non_framework_names 段不是对象")
        return None
    sections = {}
    for section in SYMBOL_SECTIONS:
        entries = symbols.get(section, {})
        if not isinstance(entries, dict):
            reading.cannot_measure("登记表 symbols.%s 段不是对象（键为 `文件名|符号`）: %s"
                                   % (section, type(entries).__name__))
            return None
        for key, value in entries.items():
            if not isinstance(value, dict) or "class" not in value or "reason" not in value:
                reading.cannot_measure("登记表 symbols.%s 项 `%s` 不是 {class, reason} 形状"
                                       "（class 不校验的话标签就是纯装饰）" % (section, key))
                return None
            if value["class"] not in VALID_CLASSES:
                reading.cannot_measure("登记表 symbols.%s 项 `%s` 的 class 写 `%s`，不属于 A/B/C 三档"
                                       % (section, key, value["class"]))
                return None
            if value["class"] != SECTION_CLASS[section]:
                reading.cannot_measure("登记表 symbols.%s 项 `%s` 的 class 写 `%s`，与该节的档位 `%s` 不自洽"
                                       % (section, key, value["class"], SECTION_CLASS[section]))
                return None
        sections[section] = entries
    for token, value in exclusions.items():
        if not isinstance(value, dict) or "class" not in value or "belongs" not in value:
            reading.cannot_measure("排除表项 `%s` 不是 {class, belongs} 形状（归属证据必填）" % token)
            return None
        if not isinstance(value["belongs"], str) or not value["belongs"].strip():
            reading.cannot_measure("排除表项 `%s` 的 belongs 是空值 —— 没有归属证据的排除表项就是白拿的豁免" % token)
            return None
        if not belongs_qualifies(token, value["belongs"]):
            reading.cannot_measure("排除表项 `%s` 的 belongs 给不出可核验的所有者：要的是以外部根命名空间 %s "
                                   "起头、末段等于 `%s` 的限定名，现值只有归属话术（%s）"
                                   % (token, "/".join(EXTERNAL_ROOTS), token, value["belongs"]))
            return None
        if value["class"] != EXCLUSION_CLASS:
            reading.cannot_measure("排除表项 `%s` 的 class 写 `%s`，排除表是 B 档"
                                   % (token, value["class"]))
            return None
    # 同一个键挂在两个分节上时 dead_api 先命中、另一节永远读不到，等于给同一符号偷偷留第二条通道
    seen = {}
    for section in SYMBOL_SECTIONS:
        for key in sections[section]:
            if key in seen:
                reading.cannot_measure("登记表符号项 `%s` 同时挂在 symbols.%s 与 symbols.%s 两节 —— "
                                       "一个键只能有一个档位" % (key, seen[key], section))
                return None
            seen[key] = section
    return sections


def judge_symbols(root, command_files, debt, declared, reading):
    sections = validate_debt_shapes(debt, reading)
    if sections is None:
        return
    dead_api = sections["dead_api"]
    exclusions = debt["non_framework_names"]

    per_file = {}
    per_file_members = {}
    per_file_examples = {}
    shape_pool = set()
    normalized_pool = set()
    for path in command_files:
        name = os.path.basename(rel_of(path, root))
        candidates, shapes, normalized, members, examples = extract_candidates(read_text(path), declared)
        per_file[name] = candidates
        per_file_members[name] = members
        per_file_examples[name] = examples
        shape_pool.update(shapes)
        normalized_pool.update(normalized)

    # 排除表的两条反向对账量的都是**候选集**。形态命中集在模板邻接/示例占位/已声明/成员形态四道筛之前，
    # 拿它当「挡到东西」的证据，等于往排除表塞任何只以成员形态出现的名字都白拿一条豁免、还把红转成绿。
    blocked_tokens = {t for t in exclusions if any(t in v for v in per_file.values())}
    for token in sorted(exclusions):
        if token in declared:
            reading.violate("排除表项 `%s` 在本包确有声明，属陈旧豁免，请删" % token)
        if token not in blocked_tokens:
            reading.violate("排除表项 `%s` 今天挡不到任何候选，请删" % token)

    # 矛盾对账：同名同时坐在登记表符号位与排除表，就是「A 改 B」没把原登记删掉
    debt_tokens = set()
    for section in SYMBOL_SECTIONS:
        for key in sections[section]:
            debt_tokens.add(key.partition("|")[2])
    for token in sorted(set(exclusions) & debt_tokens):
        reading.violate("符号 `%s` 既登记在 symbols 又登记在排除表 non_framework_names —— 两条通道同时挂着"
                        "等于都不认账；本条挡的是「两边挂着」，删净原登记只留排除表项那一半见下面的口径边界" % token)

    attributed = 0
    out_of_scope = 0
    unattributed = 0
    for name in sorted(per_file):
        for token in sorted(per_file[name]):
            if token in exclusions:
                continue
            key = "%s|%s" % (name, token)
            if key in dead_api:
                attributed += 1
                continue
            section = next((s for s in ("member_form", "example_placeholder") if key in sections[s]), None)
            if section:
                out_of_scope += 1
                if section == "member_form" and token not in per_file_members[name]:
                    reading.violate("symbols.member_form 项 `%s` 在该命令文件里取不到任何成员形态的书写"
                                    "（已声明类型的 `.` 接收者、`(` 调用、`=` 清单、`_ = Name;` 赋值右值、"
                                    "`event <Type> Name;` 声明位、并列传播）—— 分类没有机械依据，"
                                    "要么改档 dead_api 要么删（登记理由: %s）"
                                    % (key, sections[section][key]["reason"]))
                if section == "example_placeholder" and token not in per_file_examples[name]:
                    reading.violate("symbols.example_placeholder 项 `%s` 没有在同一行写出被代入的参数名"
                                    "（`如 Quest，生成 QuestService` 那种同行书写）—— 分类没有机械依据，"
                                    "要么改档 dead_api 要么删（登记理由: %s）"
                                    % (key, sections[section][key]["reason"]))
                continue
            unattributed += 1
            reading.violate("命令正文写了本包查无声明的符号 `%s`（来自 %s）；"
                            "按三档归类后才准进登记表或排除表" % (token, name))

    # dead_api 是债务，走单向棘轮：每项必须仍命中候选集
    for key in sorted(dead_api):
        name, sep, token = key.partition("|")
        if not sep:
            reading.violate("登记表符号项键形如 `文件名|符号`，现值 `%s` 不合形状" % key)
            continue
        if name not in per_file:
            reading.violate("登记表符号项 `%s` 指向的命令文件今天不存在，请删或改键" % key)
        elif token not in per_file[name]:
            reading.violate("登记表符号项 `%s` 今天不再命中候选集（修好了就删掉它）\n"
                            "              登记理由: %s" % (key, dead_api[key]["reason"]))
    # 口径外分档登记不进债务棘轮，但也不许烂在那里：不再命中就是分类已经失效
    for section in ("member_form", "example_placeholder"):
        for key in sorted(sections[section]):
            name, sep, token = key.partition("|")
            if not sep:
                reading.violate("登记表 symbols.%s 项键形如 `文件名|符号`，现值 `%s` 不合形状"
                                % (section, key))
                continue
            if name not in per_file or token not in per_file[name]:
                reading.violate("symbols.%s 项 `%s` 今天不再命中候选集（抽取收掉了就删掉它）\n"
                                "              分类理由: %s"
                                % (section, key, sections[section][key]["reason"]))

    reading.note("去重候选符号 %d 个、逐文件命中 %d 条；形态命中 %d 个名字，归一化收掉 %d 个（`X`⇒`XAttribute`），"
                 "排除表挡到 %d 个候选，债务表豁免 %d 条，未归因违例 %d 条"
                 % (len({t for v in per_file.values() for t in v}),
                    sum(len(v) for v in per_file.values()), len(shape_pool), len(normalized_pool),
                    len(blocked_tokens), attributed, unattributed))
    reading.note("口径外候选 %d 条（symbols.member_form 登记 %d 条、symbols.example_placeholder 登记 %d 条）："
                 "这一类不计入债务豁免、也不走「只准降不准升」的棘轮，单列在这里；"
                 "但每项既要仍命中候选集，又要抽取当场能复算出它的成员形态或示例代入书写"
                 % (out_of_scope, len(sections["member_form"]), len(sections["example_placeholder"])))
    reading.note("口径边界自述：成员名不在「类型是否存在」这一口径内——方法、字段与源生成成员一律不查，"
                 "覆盖面只到类型面；成员形态的书写由抽取归一化收掉，收不掉的走 symbols.member_form "
                 "分档登记并逐条注明依据")
    reading.note("排除表的牙分三级，别把它读成「进了排除表就有人替你把关」：①同名既在 symbols 又在排除表即红；"
                 "②belongs 必须以外部根命名空间 %s 起头、且末段等于该名字的限定名，「%s」这类含糊话按量不出来收；"
                 "③删净原登记、只留一条写了诚实限定名的排除表项——这一半机械上抓不到，判 A 还是判 B 的分界是"
                 "「命令在此断言的是不是本包的 API」，机器读不出来；要抓得核被断言的成员（真名之后那个成员在本包"
                 "有没有声明），属成员级判据，不在本口径内"
                 % ("/".join(EXTERNAL_ROOTS), "Unity 的 UI 组件"))


# ---------------------------------------------------------------- 判据 D：文档对账


def parse_claude_command_table(text, reading):
    names = set()
    for line in text.split("\n"):
        if not line.strip().startswith("|"):
            continue
        cells = [c.strip().strip("`") for c in line.strip().strip("|").split("|")]
        if cells and COMMAND_ROW_RE.match(cells[0]):
            names.add(cells[0][1:])
    if not names:
        reading.cannot_measure("CLAUDE.md 里解析出 0 条命令表项 —— 看不见内容不等于内容合格")
        return None
    return names


def judge_doc_ledger(root, command_files, reading):
    claude_path = os.path.join(root, "CLAUDE.md")
    if not os.path.isfile(claude_path):
        reading.cannot_measure("CLAUDE.md 取不到: %s" % claude_path)
        return
    table = parse_claude_command_table(read_text(claude_path), reading)
    if table is None:
        return
    files = {os.path.basename(p)[:-3] for p in command_files}
    reading.note("CLAUDE.md 命令表 %d 条 ↔ `.claude/commands` 文件 %d 个，双向等价要求不多不少"
                 % (len(table), len(files)))
    for name in sorted(table - files):
        reading.violate("CLAUDE.md 命令表列了 `/%s`，`.claude/commands` 下没有对应文件" % name)
    for name in sorted(files - table):
        reading.violate("`%s.md` 存在却没进 CLAUDE.md 命令表" % name)


# ---------------------------------------------------------------- 判据 E：指针不悬空


def pointer_targets(text):
    """反引号内、以已知根段起头的字面路径。绝对路径、`<占位>`、glob、运行期产物都不受检。"""
    found = []
    for line, _is_fence, spans in iter_lines(text):
        for lo, hi in spans:
            span = line[lo:hi]
            for token in re.split(r"[\s,;()\"'，。；]+", span):
                token = token.strip("`").rstrip(".")
                if not token or any(bad in token for bad in NON_LITERAL_CHARS):
                    continue
                if ":" in token or token.startswith("/") or token.startswith("\\"):
                    continue
                if any(token.startswith(root_seg) for root_seg in POINTER_ROOTS):
                    found.append(token)
    return found


def judge_pointers(root, tracked, reading):
    """扫描范围 = tracked 的 `CLAUDE.md` 与 `.claude/skills/**/*.md`。

    未入库的在途产物不在范围内：别人的活没落位不该把本判据打成红，
    而指针目标必须是 tracked，否则 fresh clone 上必悬空。
    """
    tracked_set = set(tracked or [])
    if not tracked_set:
        return  # tracked 清单取不到时这一类判据没跑成，原因已由调用方记在 E 段
    scan_files = [rel for rel in sorted(tracked_set)
                  if rel == "CLAUDE.md" or (rel.startswith(".claude/skills/") and rel.endswith(".md"))]
    if not any(rel.startswith(".claude/skills/") for rel in tracked_set):
        reading.cannot_measure("tracked 清单里没有 `.claude/skills/**/*.md` —— 看不见文档不等于文档没有指针")
    if "CLAUDE.md" not in tracked_set:
        reading.cannot_measure("`CLAUDE.md` 不在 tracked 清单里 —— 命令表对账与指针判据都缺对象")
    checked = 0
    hit_count = 0
    for rel in scan_files:
        path = os.path.join(root, *rel.split("/"))
        try:
            text = read_text(path)
        except (OSError, ToolError) as exc:
            reading.cannot_measure("受检文档读不动: %s (%s)" % (rel, exc))
            continue
        checked += 1
        for token in pointer_targets(text):
            hit_count += 1
            target = token.rstrip("/")
            hit = target in tracked_set or any(p.startswith(target + "/") for p in tracked_set)
            if not hit:
                reading.violate("%s 指向未入库或已不存在的目标: `%s`" % (rel, token))
    reading.note("受检文档 %d 份（tracked 的 `CLAUDE.md` + `.claude/skills/**/*.md`），"
                 "字面路径指针 %d 条逐条按包根核 tracked" % (checked, hit_count))


# ---------------------------------------------------------------- 判据 F：层隔离


def ignored_root_targets(text):
    """反引号内、以「只活在检出环境」根段起头的路径。

    与 `pointer_targets` 同一条切词链，但根段表相反：那条只认受检根段，
    拿它去过滤 docs/ 与 .superpowers/ 就等于永远看不见要抓的东西。
    """
    found = []
    for line, _is_fence, spans in iter_lines(text):
        for lo, hi in spans:
            span = line[lo:hi]
            for token in re.split(r"[\s,;()\"'，。；]+", span):
                token = token.strip("`").rstrip(".")
                if not token or any(bad in token for bad in NON_LITERAL_CHARS):
                    continue
                if any(token.startswith(seg) for seg in IGNORED_ROOTS):
                    found.append(token)
    return found


def judge_layers(root, tracked, debt, reading):
    """三层各自的引用方向：公开层只讲用法；代理层可指契约层；契约层每份文件都要在索引里对上。

    射程 = tracked 的随包发行面，未入库的在途产物不判（与判据 E 同理由）。
    本段对自己没有例外：章程与细则块凡是把被禁根段写进反引号的，照样计违例，
    由登记表逐条注明「这一行写的就是这条禁令本身」。
    """
    tracked_set = set(tracked or [])
    if not tracked_set:
        return  # 取不到 tracked 清单时这一类没跑成，原因已由 E 段记在案
    exempt = debt.get("layer_isolation", {}) or {}
    scan = [rel for rel in sorted(tracked_set)
            if rel.startswith(SHIPPED_SURFACES) and rel.endswith(".md")]
    if not scan:
        reading.cannot_measure("tracked 清单里找不到任何随包发行面文档（`.md`）—— 看不见文档不等于隔离成立")
        return
    texts = {}
    for rel in scan:
        path = os.path.join(root, *rel.split("/"))
        try:
            texts[rel] = read_text(path)
        except (OSError, ToolError) as exc:
            reading.cannot_measure("受检文档读不动: %s (%s)" % (rel, exc))
    hits = 0
    matched = set()
    for rel, text in sorted(texts.items()):
        for token in ignored_root_targets(text):
            hits += 1
            key = "%s|%s" % (rel, token)
            if key in exempt:
                matched.add(key)
                continue
            reading.violate("%s 指向只活在检出环境的层: `%s`（干净检出上必悬空）" % (rel, token))
        if rel.startswith(PUBLIC_ONLY):
            for token in pointer_targets(text):
                if not token.startswith("Specs~/"):
                    continue
                hits += 1
                key = "%s|%s" % (rel, token)
                if key in exempt:
                    matched.add(key)
                    continue
                reading.violate("公开层指进内建契约层: %s -> `%s`" % (rel, token))
    index_rel = "Specs~/README.md"
    if index_rel in texts:
        index = texts[index_rel]
        for rel in sorted(p for p in texts if p.startswith("Specs~/")
                          and p.endswith(".md") and p != index_rel):
            stem = os.path.basename(rel)[:-3]
            count = index.count("`%s`" % stem)
            if count != 1:
                reading.violate("契约层索引与文件不对上: %s 在索引里出现 %d 次（应为 1）" % (rel, count))
    elif any(rel.startswith("Specs~/") for rel in tracked_set):
        reading.violate("契约层有契约文件但没有索引: 缺 `Specs~/README.md`")
    # 反向对账：登记项挡不到任何命中就是陈旧豁免，留着它等于给未来的违例预先放行
    for key in sorted(set(exempt) - matched):
        reading.violate("层隔离登记项今天挡不到任何命中，删掉它: %s" % key)
    reading.note("受检文档 %d 份，越界引用 %d 条" % (len(texts), hits))


# ---------------------------------------------------------------- 登记表


def load_debt(path, reading):
    if not os.path.isfile(path):
        reading.cannot_measure("债务登记表取不到: %s —— 没有登记表时判据只会把全部红都算成未归因违例" % path)
        return None
    try:
        with open(path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (ValueError, OSError) as exc:
        reading.cannot_measure("债务登记表读不动或不是合法 JSON: %s (%s)" % (path, exc))
        return None
    if not isinstance(data, dict):
        reading.cannot_measure("债务登记表顶层不是对象: %s" % path)
        return None
    for section in ("frontmatter", "symbols", "non_framework_names", "layer_isolation"):
        data.setdefault(section, {})
    return data


# ---------------------------------------------------------------- 主流程


def main():
    # 输出走 UTF-8：读数里有中文与 `⇒` 一类的箭头，Windows 控制台默认码会把它们截成乱码
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_root = os.path.abspath(os.path.join(script_dir, os.pardir, os.pardir, os.pardir, os.pardir))
    parser = argparse.ArgumentParser(description="`.claude/` 配置层量具（退出码 0 合格 / 1 有违例 / 2 量不出来）")
    parser.add_argument("--root", default=default_root, help="包根，默认取脚本所在目录往上四级")
    parser.add_argument("--debt", default=os.path.join(script_dir, "config-surface-debt.json"),
                        help="债务登记表路径，默认与脚本同目录")
    args = parser.parse_args()

    root = os.path.abspath(args.root)
    debt_path = os.path.abspath(args.debt)
    readings = [
        Reading("debt", "债务登记表"),
        Reading("A", "装配存在性"),
        Reading("B", "frontmatter 与档位写权限"),
        Reading("C", "符号真名"),
        Reading("D", "文档对账"),
        Reading("E", "指针不悬空"),
        Reading("F", "层隔离"),
    ]
    debt_reading, a, b, c, d, e, f = readings

    print("config-surface gate | root = %s" % root)
    print("                    | debt = %s" % debt_path)

    try:
        if not os.path.isdir(root):
            raise ToolError("包根取不到: %s" % root)
        debt = load_debt(debt_path, debt_reading)
        if debt is None:
            raise ToolError("债务登记表不可用，判据不跑: %s" % debt_path)

        # A 与声明集先跑：git 只是 E 的取数手段，它坏了不该把「装配取不到」这条专属文案一起吞掉
        command_files = judge_assembly(root, a)
        declared = declared_type_names(root, c)
        try:
            tracked = git_lines(root, ["ls-files"])
        except ToolError as exc:
            tracked = None
            e.cannot_measure(str(exc))
        if tracked is not None and not tracked:
            e.cannot_measure("git ls-files 取到 0 条 —— 拿不到 tracked 清单就没法判指针落位")
            tracked = None
        if command_files is not None and declared is not None:
            measure_frontmatter(root, command_files, debt, b)
            judge_symbols(root, command_files, debt, declared, c)
            judge_doc_ledger(root, command_files, d)
        else:
            b.cannot_measure("装配或声明集取不到，B/C/D 这一趟一条都没跑")
            c.cannot_measure("装配或声明集取不到，B/C/D 这一趟一条都没跑")
            d.cannot_measure("装配或声明集取不到，B/C/D 这一趟一条都没跑")
        judge_pointers(root, tracked, e)
        judge_layers(root, tracked, debt, f)
    except ToolError as exc:
        readings[0].cannot_measure(str(exc))

    total_violations = sum(len(r.violations) for r in readings)
    total_failures = sum(len(r.failures) for r in readings)
    # 结论文案里的段数由本次实际跑的判据段数得出，不写死数字：加一段判据时这里跟着变，
    # 文档只要写「段数以量具输出为准」就不会跟着漂
    judged_sections = len([r for r in readings if r.tag != "debt"])
    for reading in readings:
        reading.dump()

    print("")
    if total_failures:
        print("RESULT: CANNOT-MEASURE | 判据没能全部跑通 %d 处（未归因违例 %d 条不计入结论）"
              % (total_failures, total_violations))
        return EXIT_CANNOT_MEASURE
    if total_violations:
        print("RESULT: FAIL | %d 段判据全部跑过，未归因违例 %d 条" % (judged_sections, total_violations))
        return EXIT_VIOLATION
    print("RESULT: PASS | %d 段判据全部跑过且无违例" % judged_sections)
    return EXIT_PASS


if __name__ == "__main__":
    sys.exit(main())
