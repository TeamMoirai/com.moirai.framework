#!/bin/bash
set -e

cd "$(dirname "$0")" || exit 1

LUBAN_REPO="https://github.com/focus-creative-games/luban.git"
LUBAN_DIR="../../luban"
# 钉发布版 tag：v5.1.0 之后的 trunk 提交 029f714 拒收 `目录/Sheet@File.xlsx`（官方示例写法）；要跟 trunk 就把本行改成 origin/main。
LUBAN_REF="v5.1.0"

# Step 1: Check if luban source directory exists
if [ ! -d "$LUBAN_DIR/.git" ]; then
    echo "[INFO] Luban source not found, cloning from $LUBAN_REPO ..."
    git clone "$LUBAN_REPO" "$LUBAN_DIR"
    echo "[INFO] Aligning to $LUBAN_REF ..."
    git -C "$LUBAN_DIR" checkout --detach "$LUBAN_REF"
else
    # Step 2: 对齐到目标 ref。工作树不干净或对不上就拒绝编译，而不是 warn 之后照常产出——
    # 静默换版本正是 Windows 与 macOS 产物不一致的来源。
    echo "[INFO] Aligning luban to $LUBAN_REF ..."
    cd "$LUBAN_DIR"
    if [ -n "$(git status --porcelain)" ]; then
        echo "[ERROR] luban 工作树有未提交的改动，拒绝编译。先清理：git -C \"$LUBAN_DIR\" status" >&2
        exit 1
    fi
    # 必须是 --quiet --verify：裸 rev-parse 在 ref 不存在时会把字面量原样打到 stdout，
    # 于是"取不到"被误判成"取到了"，随后切到一个不存在的版本。
    if git fetch origin --prune --tags >/dev/null 2>&1; then
        FETCH_OK=1
    else
        FETCH_OK=0
    fi
    TARGET=$(git rev-parse --quiet --verify "$LUBAN_REF^{commit}" || true)
    if [ -z "$TARGET" ]; then
        echo "[ERROR] 取不到 ${LUBAN_REF}，拒绝编译。" >&2
        exit 1
    fi
    if [ "$FETCH_OK" = 0 ]; then
        echo "[WARN] fetch 失败（离线或 SSH 不可用），按本地已有的 $LUBAN_REF 对齐：$(git rev-parse --short "$TARGET")"
    fi
    if [ "$(git rev-parse HEAD)" = "$TARGET" ]; then
        echo "[INFO] 已在 ${LUBAN_REF}。"
    else
        echo "[INFO] 从 $(git describe --tags --always) 切到 $LUBAN_REF ($(git rev-parse --short "$TARGET")) ..."
        git checkout --detach "$TARGET"
    fi
    echo "[INFO] 编译所用版本：$(git describe --tags --always) ($(git rev-parse --short HEAD))"
    cd - > /dev/null
fi

# Step 3: Clean previous build output
[ -d Luban ] && rm -rf Luban

# Step 4: Build
# 通过 msbuild 属性把多语言插件（Extensions/Moirai.Luban.Localization）作为 ProjectReference
# 注入 Luban 主程序构建：插件 dll 随构建产出并登记进 Luban.deps.json，运行期的 Assembly.Load
# 才能解析到（.NET Core 只按 deps.json 探测，事后拷 dll 不会生效）。Luban 源码零修改。
echo "[INFO] Building Luban (with Moirai localization plugin) ..."
dotnet build "$LUBAN_DIR/src/Luban/Luban.csproj" -c Release -o Luban \
    "-p:CustomAfterMicrosoftCommonTargets=$(pwd)/Extensions/Moirai.Luban.Localization/LubanHost.targets" \
    "-p:MoiraiLocalizationProject=$(pwd)/Extensions/Moirai.Luban.Localization/Moirai.Luban.Localization.csproj" \
    "-p:LubanCoreProject=$(pwd)/$LUBAN_DIR/src/Luban.Core/Luban.Core.csproj"

echo "[INFO] Verifying plugin was emitted ..."
[ -f "Luban/Moirai.Luban.Localization.dll" ] || {
    echo "[ERROR] 插件 dll 未随 Luban 构建产出：Luban/Moirai.Luban.Localization.dll 不存在。" >&2
    exit 1
}
grep -q "Moirai.Luban.Localization" Luban/Luban.deps.json || {
    echo "[ERROR] Luban.deps.json 未登记插件程序集，运行期将无法加载。" >&2
    exit 1
}
echo "[INFO] Plugin Moirai.Luban.Localization emitted and registered in Luban.deps.json."
