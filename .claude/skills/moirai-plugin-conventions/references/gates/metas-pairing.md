# `.meta` 成对口径

- Unity 为包内每个资产生成 `.meta`；GUID 是资产身份。
- 新增 `.cs` 时**不要手写 `.meta`**：让编辑器导入时生成，再把源文件与 `.meta` 同笔提交。
- 删除源文件时同笔删除对应 `.meta`。
- 不要复制或复用其他文件的 `.meta`；GUID 必须唯一。
- `.claude/**` 以点开头，Unity 不导入，不需要 `.meta`。`CLAUDE.md` 需要 `CLAUDE.md.meta`。

## 自查片段

```bash
for root in "addressables" "fmod" "litmotion" "memorypack" "messagepack" "newtonsoft-json" "photon-fusion" "primetween" "protobuf" "serilog" "unity-cloud-save" "unity-logging" "wwise" "zlogger" "zstring"; do
  echo "== $root =="
  comm -3 \
    <(find "$root" \( -name '*.cs' -o -name '*.asmdef' -o -name '*.asmref' \) | sed 's/$/.meta/' | sort) \
    <(find "$root" \( -name '*.cs.meta' -o -name '*.asmdef.meta' -o -name '*.asmref.meta' \) | sort)
done
```

左列 = 有源文件但缺 `.meta`；右列 = 有 `.meta` 但没有源文件。两侧都为空才算成对。`Samples~/` 不参与导入，结果单列判断。

## 与桥的配合

新增 `.cs` 后先投 `refresh` 让 Unity 生成 meta 并导入，再投 `recompile`。若 `.cs` 已存在但编译清单里没有该类型，先检查 meta / 资产库归属，不要先怀疑代码。
