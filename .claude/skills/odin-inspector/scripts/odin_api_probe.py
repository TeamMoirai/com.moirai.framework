#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Odin API 存在性探针：判断某个类型名/成员名在**装机版本**里到底有没有。

用法:
    python odin_api_probe.py [--dir <Assemblies目录>] <Name> [<Name> ...]

为什么需要它：官方文档站描述的是最新版，且 XML 文档只收"写了注释的成员"，
所以"文档里有"不等于本机有、"XML 里查不到"也不等于 API 不存在。唯一可靠的存在性
证据是程序集元数据的 #Strings 堆（类型名/成员名，UTF-8）；字符串字面量在 #US 堆
（UTF-16LE），两堆要分开数。
"""
import os
import sys

# 默认从当前工作目录（Unity 工程根）解析；跨工程一律显式传 --dir 或 --project。
DEFAULT_SUBPATH = os.path.join("Assets", "Plugins", "Sirenix", "Assemblies")


def find_assemblies(root):
    out = []
    for name in sorted(os.listdir(root)):
        if name.endswith(".dll") or name.endswith(".xml"):
            out.append(os.path.join(root, name))
    return out


def probe(name, files):
    hits = []
    doc_entries = []
    for path in files:
        try:
            data = open(path, "rb").read()
        except OSError:
            continue
        u8 = data.count(name.encode("utf-8"))
        u16 = data.count(name.encode("utf-16-le"))
        if path.endswith(".dll") and (u8 or u16):
            hits.append("%s: names=%d literals=%d" % (os.path.basename(path), u8, u16))
        elif path.endswith(".xml") and u8:
            for line in data.decode("utf-8", "replace").splitlines():
                if name in line and ("<member " in line or "<type " in line or 'name="T:' in line):
                    doc_entries.append(os.path.basename(path) + " " + line.strip()[:120])
    return hits, doc_entries


def main(argv):
    args = argv[1:]
    root = os.path.join(os.getcwd(), DEFAULT_SUBPATH)
    if args and args[0] == "--project":
        root = os.path.join(args[1], DEFAULT_SUBPATH)
        args = args[2:]
    if args and args[0] == "--dir":
        root = args[1]
        args = args[2:]
    if not args:
        print("usage: python odin_api_probe.py [--dir <dir>] <Name> [...]")
        return 2
    if not os.path.isdir(root):
        print("NO-SUCH-DIR %s  (pass --dir <Sirenix Assemblies folder>)" % root)
        return 2

    files = find_assemblies(root)
    print("root=%s  files=%d" % (root, len(files)))
    for name in args:
        hits, docs = probe(name, files)
        verdict = "PRESENT" if hits else "ABSENT"
        print("\n%s -> %s" % (name, verdict))
        for h in hits[:4]:
            print("    dll  %s" % h)
        for d in docs[:2]:
            print("    doc  %s" % d)
        if not hits:
            print("    NOTE: ABSENT only means this byte sequence is not in those dlls.")
            print("          Generic types need a backtick suffix (Foo`1), nested types are")
            print("          Outer/Inner, and a bare member name can match unrelated symbols.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
