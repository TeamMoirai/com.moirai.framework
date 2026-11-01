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
echo "[INFO] Building Luban ..."
dotnet build "$LUBAN_DIR/src/Luban/Luban.csproj" -c Release -o Luban
