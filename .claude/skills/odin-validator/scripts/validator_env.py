#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Odin Validator 现场体检：一次读全"装机版本 / define 分层 / API 存在性 / 配置生效值"。

用法:
    python validator_env.py [--project <Unity工程根>]

四段输出全是**读数**，不是结论：
  VERSION  从 EnsureCorrectOdinVersion.cs 的 const validatorVersion 取（比日志/窗口更硬）
  DEFINES  ProjectSettings.asset 各平台的 ODIN_VALIDATOR* 符号（写 #if 之前必须看这里）
  API      关键成员名在各 dll 里的字节命中数（0 = 装机版没有这个名字）
  CONFIG   Odin Validator 配置资产的生效值（automation 开关、global 开关、已序列化的 rule 数、profile 覆盖）

为什么要它：校验行为由三件事共同决定——装的是哪版、编译进没进来、配置资产里现在是几。
只看文档站或只看 C# 字段初值都会得出错的可期望行为（官方文档描述最新版，字段初值不等于生效值）。
"""
import os
import re
import sys

API = [
    "RegisterValidatorAttribute", "RegisterValidationRuleAttribute", "Validator",
    "ValueValidator", "AttributeValidator", "RootObjectValidator", "SceneValidator",
    "GlobalValidator", "ISelfValidator", "SelfValidationResult", "ValidationResult",
    "ValidationResultType", "ValidatorSeverity", "NoValidationInInspectorAttribute",
    "DontValidateAttribute", "WithFix", "FixArgs", "ValidationSession",
    "MainValidationSession", "ValidateEverythingEnumerator", "AutomationConfig",
    "GlobalValidationConfig", "RuleConfig",
    # 反面对照：文档站/旧版里出现过但装机版没有的名字，探针必须把它们判 0 才算活着
    "AbstractValidator", "StringValidatorBase", "ValidationSeverity", "ApplyFix",
]

CONTROL_NAMES = ["RegisterValidatorAttribute", "ValidationResult", "ValidatorSeverity"]
NEGATIVES = ["AbstractValidator", "StringValidatorBase", "ValidationSeverity", "ApplyFix"]


def read(path):
    try:
        return open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def seg_version(proj):
    f = os.path.join(proj, "Assets/Plugins/Sirenix/Odin Validator/EnsureCorrectOdinVersion.cs")
    m = re.search(r'validatorVersion\s*=\s*"([^"]+)"', read(f))
    asm = os.path.join(proj, "Assets/Plugins/Sirenix/Assemblies/Sirenix.OdinValidator.Editor.dll")
    print("VERSION validator=%s  dll=%s  present=%s"
          % (m.group(1) if m else "UNKNOWN(源码不在预期路径)", os.path.basename(asm), os.path.isfile(asm)))
    if not m:
        print("        NOTE: 取不到版本号就先确认 Odin 装在哪（--project 指错最常见）")


def seg_defines(proj):
    ps = read(os.path.join(proj, "ProjectSettings/ProjectSettings.asset"))
    block = re.search(r"scriptingDefineSymbols:(.*?)\n  \w", ps, re.S)
    print("DEFINES")
    if not block:
        print("        no scriptingDefineSymbols block")
        return
    cur = "?"
    for line in block.group(1).splitlines():
        m = re.match(r"\s{4}(.+?):\s*(.*)$", line)
        if m:
            cur, syms = m.group(1).strip(), m.group(2)
            odin = sorted(set(re.findall(r"ODIN_[A-Z0-9_]+", syms)))
            if odin:
                print("        %-22s %s" % (cur, " ".join(odin)))
    print("        -> 写 #if 前先核这里有没有更高的版本档（例如 ODIN_VALIDATOR_4_0 常常并不存在）")


def seg_api(proj):
    d = os.path.join(proj, "Assets/Plugins/Sirenix/Assemblies")
    if not os.path.isdir(d):
        print("API  no Assemblies dir at %s" % d)
        return
    blobs = {}
    for name in sorted(os.listdir(d)):
        if name.endswith(".dll"):
            try:
                blobs[name] = open(os.path.join(d, name), "rb").read()
            except OSError:
                pass
    print("API   (%d dlls, 整名匹配=名字后紧跟 NUL)" % len(blobs))
    for n in API:
        pat = n.encode("utf-8") + b"\x00"
        hits = {k: v.count(pat) for k, v in blobs.items() if v.count(pat)}
        top = ", ".join("%s:%d" % (k.replace("Sirenix.", "").replace(".dll", ""), c)
                        for k, c in sorted(hits.items(), key=lambda x: -x[1])[:3]) or "0"
        print("        %-32s %s" % (n, top))
    pos = sum(1 for n in CONTROL_NAMES if any(b.count(n.encode() + b"\x00") for b in blobs.values()))
    neg = sum(1 for n in NEGATIVES if any(b.count(n.encode() + b"\x00") for b in blobs.values()))
    print("        self-check: controls present %d/%d, known-absent hits %d/%d (期望 3/3 与 0/4)"
          % (pos, len(CONTROL_NAMES), neg, len(NEGATIVES)))
    if pos < len(CONTROL_NAMES) or neg:
        print("        !! 探针本身不可信，别拿上面的 0/命中当结论")


def seg_config(proj):
    base = os.path.join(proj, "Assets/Plugins/Sirenix/Odin Validator/Editor")
    print("CONFIG")
    if not os.path.isdir(base):
        print("        no validator config dir at %s" % base)
        return
    auto = read(os.path.join(base, "Config/AutomationConfig.asset"))
    glob = read(os.path.join(base, "Config/GlobalValidationConfig.asset"))
    rules = read(os.path.join(base, "Config/RuleConfig.asset"))
    prof = read(os.path.join(base, "Profiles/Main Profile.asset"))
    for label, txt, keys in (
        ("automation", auto, ("OnPlayMode", "OnBuild", "OnProjectStartup")),
        ("global", glob, ()),
    ):
        if label == "automation":
            for k in keys:
                m = re.search(r"\n  %s: (\d+)" % k, txt)
                print("        %-22s %s = %s" % (label, k, m.group(1) if m else "MISSING"))
        else:
            on = len(re.findall(r"serializedValue: [1-9]", txt))
            changed = len(re.findall(r"changedFromDefault: [1-9]", txt))
            print("        %-22s toggles-on=%d changedFromDefault=%d" % (label, on, changed))
    n = len(re.findall(r"^\s*- ", rules, re.M)) if "projectRules" in rules else -1
    print("        %-22s projectRules entries=%d" % ("rules", n))
    inc = re.findall(r"Enabled: (\d)\s*\n\s*Type: (\d)", prof)
    print("        %-22s include entries=%d %s" % ("profile", len(inc), inc[:4]))
    print("        NOTE: 生效值在这几份 .asset 里，不在 C# 字段初值；改完要重跑本脚本核对")


def main(argv):
    proj = None
    if len(argv) > 2 and argv[1] == "--project":
        proj = argv[2]
    else:
        proj = os.getcwd()
    if not os.path.isdir(os.path.join(proj, "ProjectSettings")):
        print("not a Unity project root: %s  (pass --project <工程根>)" % proj)
        return 2
    seg_version(proj)
    seg_defines(proj)
    seg_api(proj)
    seg_config(proj)
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace") if hasattr(sys.stdout, "reconfigure") else None
    sys.exit(main(sys.argv))
