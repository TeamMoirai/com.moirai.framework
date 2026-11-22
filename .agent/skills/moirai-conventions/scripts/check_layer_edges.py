#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""运行时层间依赖守卫。

跑什么：`Runtime/Kernel/**` 与 `Runtime/Foundation/**` 的源码不得以限定名引用 `Runtime/Services`
的类型（Kernel→Services / Foundation→Services 方向边界）；`Runtime/Services` 不得摸 `Kernel/Internal`
面（Services→内部实现边界）。

判据口径：**限定名匹配**——出现点形如 `命名空间.类型名`（命名空间取自该类型声明文件）
且前一个字符不是标识符字符。裸名（`using` 命名空间后的短名使用）不判，因为它无法区分
Foundation 与 Services 的同名类型（如 `CommandLineUtility`）与泛型嵌套类（`Enumerator`），
纯名匹配只会产出成批假阳性；限定名书写（`Moirai.Atropos.Audio.Xxx`）在真实代码里就是
跨层引用的标准形态，漏报面由此收窄到「上游用短名引用 Services 类型」——那部分由
review 与未来的 asmdef 拆分兜底。

背景：`Moirai.Atropos.asmdef` 是单一程序集，层界靠目录约定与 review 纪律；本脚本把两条最要紧的
方向边固化为可执行守卫。asmdef 拆分的判据与前置清单见 AGENTS.md 的拆分口径段。

退出码三档，彼此可区分：
  0  判据全部真跑过且无违例
  1  跑过了，有违例（逐条点名并给出处）
  2  量不出来（目录取不到、源码读不动、类型名空间解析不出）——绝不等于合格

家规：看不见源码不等于层界合格；判据一趟都没跑成的状态必须自己说话。

用法：
    python .agent/skills/moirai-conventions/scripts/check_layer_edges.py
    python .agent/skills/moirai-conventions/scripts/check_layer_edges.py --root <包根>
"""

import argparse
import json
import os
import re
import sys

EXIT_PASS = 0
EXIT_VIOLATION = 1
EXIT_CANNOT_MEASURE = 2

# ---------------------------------------------------------------- 口径常数

# 类型声明：顶层（namespace 内）public/internal 类型；嵌套类型不单独入集（限定名必带外层名，
# 外层名命中即覆盖）。捕获 (类型名)。
TOP_LEVEL_TYPE_DECL_RE = re.compile(
    r"^\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*"
    r"(?:class|interface|struct|enum)\s+(\w+)\b", re.MULTILINE)

# 命名空间声明（文件可多 namespace 块，按行扫描；嵌套 namespace 取拼接形态）
NAMESPACE_DECL_RE = re.compile(r"^\s*namespace\s+([\w.]+)")

# Services → Kernel/Internal 的引用形态：容器内部件只经容器基座（ServiceBase/Context）出借，
# 直接摸 internal 容器件是越层（这些名字唯一性强，整词匹配即可）。
INTERNAL_KERNEL_NAMES = ("ServiceWorld", "ServiceScope", "ServiceEntry", "ContractHandleComparer")

# 组合根现有跨层调用（Kernel→Services，方向性白名单；随拆分判据一并复审）
COMPOSITION_ROOT_ALLOWLIST_FILES = (
    "Runtime/Kernel/GameApp/GameAppSettings.Services.cs",
)

# 伪代码/构建产物豁免目录
EXCLUDED_DIRS = ("obj", "bin", ".git")

IDENT_BOUNDARY = re.compile(r"[A-Za-z0-9_]")


def iter_source_files(root):
    """遍历 Runtime/ 下全部 .cs 源文件，返回 (相对路径, 绝对路径) 列表；目录取不到返回 None。"""
    runtime_dir = os.path.join(root, "Runtime")
    if not os.path.isdir(runtime_dir):
        return None

    files = []
    for dirpath, dirnames, filenames in os.walk(runtime_dir):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDED_DIRS]
        for name in filenames:
            if name.endswith(".cs"):
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, root).replace(os.sep, "/")
                files.append((rel, full))
    return files


def read_text(full):
    """读源码文本；失败返回 None（由调用方判量不出来）。"""
    try:
        with open(full, "r", encoding="utf-8-sig") as f:
            return f.read()
    except OSError:
        return None


def collect_service_type_namespaces(files):
    """从 Services 层源码导出 顶层类型名 → 命名空间 映射；读取失败返回 None。"""
    table = {}
    for rel, full in files:
        if not rel.startswith("Runtime/Services/"):
            continue
        text = read_text(full)
        if text is None:
            return None

        current_ns = ""
        for line in text.splitlines():
            ns = NAMESPACE_DECL_RE.match(line)
            if ns:
                current_ns = ns.group(1)
                continue
            decl = TOP_LEVEL_TYPE_DECL_RE.match(line)
            if decl:
                table[decl.group(1)] = current_ns
    return table


def find_qualified_occurrences(text, name, namespace):
    """text 中 `namespace.name` 限定名的出现位置（namespace 可带 `Moirai.Atropos.` 前缀段）。"""
    occurrences = []
    # 限定名前缀：命名空间的全部尾段形态（Audio.Foo / Atropos.Audio.Foo / Moirai.Atropos.Audio.Foo）
    segments = namespace.split(".")
    for i in range(len(segments)):
        prefix = ".".join(segments[i:]) + "."
        start = 0
        while True:
            index = text.find(prefix + name, start)
            if index < 0:
                break
            left = index
            end = index + len(prefix) + len(name)
            right = text[end] if end < len(text) else ""
            # 右侧不得是标识符字符（防 Foo2）；限定名前一个字符不得是标识符字符
            if (not right or not IDENT_BOUNDARY.match(right)) and \
                    (left == 0 or not IDENT_BOUNDARY.match(text[left - 1])):
                occurrences.append(index)
            start = index + len(prefix) + len(name)
    return occurrences


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def scan_upstream_edges(files, type_ns):
    """Kernel/Foundation 源码中对 Services 类型的限定名引用。"""
    violations = []
    for rel, full in files:
        if not (rel.startswith("Runtime/Kernel/") or rel.startswith("Runtime/Foundation/")):
            continue
        text = read_text(full)
        if text is None:
            return None

        for name, namespace in type_ns.items():
            for index in find_qualified_occurrences(text, name, namespace):
                violations.append(
                    f"{rel}:{line_of(text, index)}: Kernel/Foundation 限定名引用 Services 类型 "
                    f"'{namespace}.{name}'")
    return violations


def scan_internal_edges(files):
    """Services 源码中对 Kernel 内部件的整词引用。"""
    violations = []
    for rel, full in files:
        if not rel.startswith("Runtime/Services/"):
            continue
        text = read_text(full)
        if text is None:
            return None

        for name in INTERNAL_KERNEL_NAMES:
            start = 0
            while True:
                index = text.find(name, start)
                if index < 0:
                    break
                end = index + len(name)
                right = text[end] if end < len(text) else ""
                left = text[index - 1] if index > 0 else ""
                if (not right or not IDENT_BOUNDARY.match(right)) and \
                        (not left or not IDENT_BOUNDARY.match(left)):
                    violations.append(
                        f"{rel}:{line_of(text, index)}: Services 引用 Kernel 内部件 '{name}'")
                start = end
    return violations


def apply_allowlist(violations):
    """组合根白名单：只豁免 Kernel→Services 方向、且文件在白名单内的引用。"""
    return [v for v in violations
            if not (v.split(":")[0] in COMPOSITION_ROOT_ALLOWLIST_FILES
                    and "引用 Services 类型" in v)]


def load_debt(root):
    """读同目录的层界债务登记表；缺失视为无豁免（表损坏则量不出来）。"""
    debt_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "layer-edges-debt.json")
    if not os.path.isfile(debt_path):
        return {}
    try:
        with open(debt_path, "r", encoding="utf-8") as f:
            return json.load(f).get("violations", {})
    except (OSError, json.JSONDecodeError, AttributeError):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=".", help="包根目录（默认当前目录）")
    args = parser.parse_args()

    files = iter_source_files(args.root)
    if files is None:
        print("LAYER-EDGES: 无法定位 Runtime/ 目录——量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE
    if not files:
        print("LAYER-EDGES: Runtime/ 下没有源文件——量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE

    type_ns = collect_service_type_namespaces(files)
    if type_ns is None:
        print("LAYER-EDGES: Services 源码读取失败——量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE
    if not type_ns:
        print("LAYER-EDGES: Services 层未发现任何类型声明——判据未真跑，量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE

    upstream = scan_upstream_edges(files, type_ns)
    internal = scan_internal_edges(files)
    if upstream is None or internal is None:
        print("LAYER-EDGES: 源码读取失败——量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE

    upstream = apply_allowlist(upstream)
    violations = upstream + internal

    # 债务豁免：逐字等值匹配登记串（单向棘轮——行号漂移即失效重红，只准降不准升）
    debt = load_debt(args.root)
    if debt is None:
        print("LAYER-EDGES: 债务登记表（layer-edges-debt.json）读不动——量不出来", file=sys.stderr)
        return EXIT_CANNOT_MEASURE
    remaining = [v for v in violations if v not in debt]
    retired = len(debt) - (len(violations) - len(remaining))
    if len(debt) > 0 and retired == len(debt):
        # 全部债务登记都命中不了任何现行违例——行号漂移或已修复，登记表该清了
        print(f"LAYER-EDGES: 提示——{retired} 条债务登记全部命中不了，登记表该清了", file=sys.stderr)
    violations = remaining

    if violations:
        print(f"LAYER-EDGES: {len(violations)} 条层间越界：")
        for v in violations:
            print(f"  {v}")
        return EXIT_VIOLATION

    print(f"LAYER-EDGES: PASS——{len(files)} 个源文件，Services 类型 {len(type_ns)} 个，"
          f"上游越界 0、内部面越界 0（组合根白名单 {len(COMPOSITION_ROOT_ALLOWLIST_FILES)} 项）")
    return EXIT_PASS


if __name__ == "__main__":
    sys.exit(main())
