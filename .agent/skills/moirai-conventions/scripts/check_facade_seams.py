#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""门面接缝单向棘轮守卫（v3 治病版 S1.7）。

跑什么：Save/Asset 门面 partial + 消费契约文件不得重新出现家族基座强转/限定引用；
`ISaveService.cs` 不得回退到「见门面」占位 remarks（S2.4 合并落点）。

判据口径（负模式，逐条点名）：
  R1 强转与运行期判型：射程文件内 `((SaveServiceHandler)…)` / `(AssetServiceHandler)` 强转、
     `as SaveServiceHandler` / `as AssetServiceHandler`、`is SaveServiceHandler` / `is AssetServiceHandler`
     ——能力协商（`is ISaveServiceBackend`）取代家族基座谱系判型。
  R2 限定引用：射程文件内 `SaveServiceHandler.` / `AssetServiceHandler.` 限定引用
     （常量宿主/路径基建/静态方法均已迁中立宿主，限定引用即依赖倒置复发）。
  R3 契约占位 remarks：`ISaveService.cs` 内「见 <see cref="SaveService"/> 门面对应 API」空壳文档
     ——S2.1 已补齐主线程语义，占位回退即文档漂移。

棘轮性质：单向——只准降不准升。射程与豁免登记（`facade-seams-debt.json`）随处置桶收窄；
新增违例必须先改代码或登记豁免并写明理由与期限。

退出码三档，彼此可区分：
  0  判据全部真跑过且无违例
  1  跑过了，有违例（逐条点名并给出处）
  2  量不出来（射程文件缺失、源码读不动）——绝不等于合格

家规：看不见源码不等于接缝干净；判据一趟都没跑成的状态必须自己说话。

用法：
    python .agent/skills/moirai-conventions/scripts/check_facade_seams.py
    python .agent/skills/moirai-conventions/scripts/check_facade_seams.py --root <包根>
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

# 射程文件（包根相对路径；显式枚举，新增文件入射程 = 棘轮收紧，须同步更新本表）
SCOPE_FILES = (
    # Save 门面 partial
    "Runtime/Services/Save/SaveService.cs",
    "Runtime/Services/Save/SaveService.Entity.cs",
    "Runtime/Services/Save/SaveService.Events.cs",
    "Runtime/Services/Save/SaveService.Migration.cs",
    "Runtime/Services/Save/SaveService.Screenshot.cs",
    # Save 消费契约
    "Runtime/Services/Save/ISaveService.cs",
    "Runtime/Services/Save/ISaveServiceBackend.cs",
    # Asset 门面 partial（AssetService 主文件 + 现有 partial）
    "Runtime/Services/Asset/AssetService.cs",
    "Runtime/Services/Asset/IAssetServiceBackend.cs",
)

# R1：强转 / as / is 家族基座判型（能力协商取代谱系判型）
CAST_PATTERN_RE = re.compile(
    r"\(\s*(?:SaveServiceHandler|AssetServiceHandler)\s*\)"     # 强转
    r"|\bas\s+(?:SaveServiceHandler|AssetServiceHandler)\b"     # as 判型
    r"|\bis\s+(?:SaveServiceHandler|AssetServiceHandler)\b"     # is 判型
)

# R2：家族基座限定引用（常量/类型/静态方法全部迁出后，限定引用即倒置复发）
QUALIFIED_REF_RE = re.compile(
    r"\b(?:SaveServiceHandler|AssetServiceHandler)\s*\."
)

# R3：契约占位 remarks（空壳文档回退）
PLACEHOLDER_REMARK_RE = re.compile(
    r"见\s*<see\s+cref=\"SaveService\"\s*/>\s*门面对应\s*API"
)

# 文档注释豁免：R1/R2 只扫代码——注释中的 cref/文字提及不判（R3 例外，它专扫注释）
DOC_LINE_RE = re.compile(r"^\s*///")

# 豁免登记（棘轮：只准删条目不准加，除非附日期与理由）
DEBT_FILENAME = "facade-seams-debt.json"


def load_debt(script_dir):
    """读豁免登记；文件缺失视为空豁免。"""
    path = os.path.join(script_dir, DEBT_FILENAME)
    if not os.path.isfile(path):
        return {}
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def read_text(full):
    """读源码文本；失败返回 None。"""
    try:
        with open(full, "r", encoding="utf-8-sig") as f:
            return f.read()
    except OSError:
        return None


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def strip_doc_lines(text):
    """去掉 /// 文档注释行后的文本（R1/R2 扫代码形态用）。"""
    return "\n".join(line for line in text.splitlines() if not DOC_LINE_RE.match(line))


def scan_file(rel, full):
    """单文件扫描，返回违例列表 [(rule, line, snippet)]；读取失败返回 None。"""
    text = read_text(full)
    if text is None:
        return None

    violations = []
    code_text = strip_doc_lines(text)

    # R1：强转与基座判型（代码行）
    for m in CAST_PATTERN_RE.finditer(code_text):
        violations.append(("R1-cast", line_of(code_text, m.start()), m.group(0)))

    # R2：限定引用（代码行）
    for m in QUALIFIED_REF_RE.finditer(code_text):
        violations.append(("R2-qualified-ref", line_of(code_text, m.start()), m.group(0)))

    # R3：契约占位 remarks（仅 ISaveService.cs，扫注释）
    if rel == "Runtime/Services/Save/ISaveService.cs":
        for m in PLACEHOLDER_REMARK_RE.finditer(text):
            violations.append(("R3-placeholder-remark", line_of(text, m.start()), m.group(0)))

    return violations


def main():
    parser = argparse.ArgumentParser(description="门面接缝单向棘轮守卫")
    parser.add_argument("--root", default=".", help="包根目录（默认当前目录）")
    args = parser.parse_args()

    root = os.path.abspath(args.root)
    script_dir = os.path.dirname(os.path.abspath(__file__))

    debt = load_debt(script_dir)
    if debt is None:
        print("[cannot-measure] 豁免登记 %s 解析失败——修 JSON 后重跑" % DEBT_FILENAME)
        return EXIT_CANNOT_MEASURE
    debt_allowed = {(e.get("file"), e.get("rule"), e.get("line")) for e in debt.get("allowances", [])}

    violations = []
    missing = []

    for rel in SCOPE_FILES:
        full = os.path.join(root, rel)
        if not os.path.isfile(full):
            missing.append(rel)
            continue

        found = scan_file(rel, full)
        if found is None:
            missing.append(rel)
            continue

        for rule, line, snippet in found:
            # 豁免按 (file, rule, line) 匹配；行号漂移的豁免条目视为过期（棘轮收紧）
            if (rel, rule, line) in debt_allowed:
                continue
            violations.append((rel, rule, line, snippet))

    if missing:
        print("[cannot-measure] 以下射程文件缺失或读不动（射程表须与现实同步）：")
        for rel in missing:
            print("  - " + rel)
        return EXIT_CANNOT_MEASURE

    if violations:
        print("[violation] 门面接缝棘轮出现新增违例（单向：只准降不准升）：")
        for rel, rule, line, snippet in sorted(violations):
            print("  %s:%d  [%s]  %s" % (rel, line, rule, snippet))
        print()
        print("处置：改代码（能力协商/契约成员/中立宿主引用）或在 %s 登记豁免并写明理由。" % DEBT_FILENAME)
        return EXIT_VIOLATION

    print("[pass] 门面接缝棘轮零违例（射程 %d 文件全扫）" % len(SCOPE_FILES))
    return EXIT_PASS


if __name__ == "__main__":
    sys.exit(main())
