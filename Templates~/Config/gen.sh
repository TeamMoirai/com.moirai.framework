#!/usr/bin/env bash
# 转表唯一入口（也是唯一一份逻辑）。Windows 用 gen.bat，它只负责找到 bash 再调本文件。
#
#   ./gen.sh                       客户端，加载类型取 config.ini 的缺省值
#   ./gen.sh server                服务端表（不含多语言，故不涉及变体）
#   ./gen.sh all                   客户端 + 服务端
#   ./gen.sh client --format=json  当次改用 json 路线（缺省 bin）
#   ./gen.sh client --load=eager   当次退回内置模板：构造期加载全部表（缺省 lazy）
#   ./gen.sh client --check        校验模式：真实产物零写入，由插件把暂存产物与已提交
#                                  产物逐字节比对（CI 防漂移；--format/--load 仍生效）
#
# 客户端导出走 Extensions/Moirai.Luban.Localization 的 moirai-l10n 插件管线：单进程完成
# 校验（词条 key 的 ref 校验、资源路径校验，--strict）→ 常规表 → 多语言代码 → 各语言数据，
# 产物经 Temp/moirai_stage 暂存区字节级提交（只写变更、删过期文件及 .meta）。
# 旧的多趟编排路线已移除，各趟 conf 取自 config.ini 的 CONF / L10N_CONF / VALIDATE_CONF。
#
# 路径、语言清单与加载类型的缺省值全在同目录的 config.ini；本文件不内嵌任何项目路径。

set -o pipefail

cd "$(dirname "$0")" || exit 1
CONFIG_FILE="config.ini"

TARGET=""
FORMAT_OVERRIDE=""
LOAD_OVERRIDE=""
CHECK_MODE=""

fail() { echo "[ERROR] $*" >&2; exit 1; }
step() { echo "===== $* ====="; }

usage() {
    # 只打印 shebang 之后连续那段注释头，遇到第一行非注释就停
    awk 'NR>1 && /^#/{print; next} NR>1{exit}' "$0"
}

# 参数：<目标> [--format=bin|json] [--load=lazy|eager] [--check]
# format/load 只在缺省时才读 config.ini，显式传入即覆盖配置。
while [ "$#" -gt 0 ]; do
    case "$1" in
        client|server|all)
            [ -n "$TARGET" ] && fail "目标只能给一个，已确定是 ${TARGET}，又多给一个 $1"
            TARGET="$1" ;;
        --format=*) FORMAT_OVERRIDE="${1#*=}" ;;
        --load=*)   LOAD_OVERRIDE="${1#*=}" ;;
        --check)    CHECK_MODE=1 ;;
        -h|--help|help) usage; exit 0 ;;
        *) fail "未知参数：$1（--help 看用法）" ;;
    esac
    shift
done
TARGET="${TARGET:-client}"

[ -f "$CONFIG_FILE" ] || fail "$CONFIG_FILE 不存在（应与 gen.sh 同目录）"

# ---------- 配置读取 ----------
# 分标题只作分组：键名全局唯一，这里直接摊平成 CFG_<KEY>，读到 [xxx] 就跳过。
# 值不做 xargs 修剪：xargs 会把反斜杠当转义吃掉，路径尾部的 \ 会被削掉。
# macOS 系统 bash 是 3.2，没有关联数组；Windows 的 Git Bash 是 5.x。
# 键摊平成 CFG_<KEY> 全局量，读取走 ${CFG_KEY} 与 cfg_get，两端同一份代码。
while IFS='=' read -r raw_key raw_value || [ -n "${raw_key//[$'\r\n']/}" ]; do
    key="${raw_key%$'\r'}"
    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    [ -z "$key" ] && continue
    case "$key" in
        \#*|\;*|[[]*) continue ;;
    esac
    value="${raw_value%$'\r'}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    case "$key" in
        *[!A-Za-z0-9_]*) echo "[WARN] 跳过非法键名：$key" >&2; continue ;;
    esac
    printf -v "CFG_$key" '%s' "$value"
done < "$CONFIG_FILE"

# 键名来自 config.ini，读不出间接展开就只能靠 cfg_get。
cfg_get() { local _name="CFG_$1"; printf '%s' "${!_name:-}"; }

need() { [ -n "$(cfg_get "$1")" ] || fail "$CONFIG_FILE 缺少键 $1（节 [$2]）"; }

# CONF/L10N_CONF/VALIDATE_CONF 由插件管线逐趟使用，这里先做一次齐全性检查
need LUBAN_DLL tools
need CONF luban
need L10N_CONF luban
need VALIDATE_CONF luban
need DATA_OUTPUT_PATH_CLIENT client
need CODE_OUTPUT_PATH_CLIENT client
need L10N_LANGUAGES l10n

LUBAN="${CFG_LUBAN_DLL}"
[ -f "$LUBAN" ] || fail "Luban 不存在：${LUBAN}（先跑 build-luban，或把编译好的文件放进 Luban/）"

# 可选键：缺失时按空处理，对应参数整条不传
optional_args=()
[ -n "${CFG_PATH_VALIDATOR_ROOT:-}" ] && optional_args+=(-x "pathValidator.rootDir=${CFG_PATH_VALIDATOR_ROOT}")

# ---------- 数据格式与加载类型 ----------
# 缺省取 config.ini，--format / --load 只做当次覆盖：排查问题时不该去改配置。
FORMAT="${FORMAT_OVERRIDE:-${CFG_DATA_FORMAT:-bin}}"
case "$FORMAT" in
    bin)  CODE_TARGET=cs-bin       ; DATA_TARGET=bin  ;;
    json) CODE_TARGET=cs-simple-json; DATA_TARGET=json ;;
    *) fail "DATA_FORMAT/--format 只认 bin 或 json，收到：$FORMAT" ;;
esac

LOAD="${LOAD_OVERRIDE:-${CFG_LAZY_LOAD:-true}}"
case "$LOAD" in
    lazy|true|TRUE|1)   LOAD=lazy ;;
    eager|false|FALSE|0) LOAD=eager ;;
    *) fail "LAZY_LOAD/--load 只认 lazy 或 eager，收到：$LOAD" ;;
esac

# --check 模式下服务端产物落暂存区（客户端暂存由插件自行管理）
CHECK_STAGE=""
if [ "$CHECK_MODE" = 1 ]; then
    CHECK_STAGE="Temp/check_stage"
    rm -rf "$CHECK_STAGE"
fi

# 懒加载模板按 code target 分目录（Luban 找的是 <dir>/<codeTarget>/tables.sbn）：
# 少一份就只是那一趟静默退回内置模板，症状是"换了 json 路线就不懒加载了"，所以逐份确认。
LAZY_TEMPLATE_DIR="${CFG_CUSTOM_TEMPLATE_ROOT:-Templates/}Client_LazyLoad"
template_args=()
if [ "$LOAD" = eager ]; then
    echo "[gen.sh] 路线 ${FORMAT}，加载类型 eager：用内置模板（构造期加载全部表）"
elif [ -f "$LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn" ]; then
    template_args=(--customTemplateDir "$LAZY_TEMPLATE_DIR")
    echo "[gen.sh] 路线 ${FORMAT}，加载类型 lazy（模板 $LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn）"
else
    echo "[WARN] 懒加载模板缺失：$LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn"
    echo "[WARN] 退回内置模板（构造期加载全部表）"
fi

# 语言清单：唯一真源，空格分隔
read -r -a LANGUAGES <<< "${CFG_L10N_LANGUAGES}"
[ "${#LANGUAGES[@]}" -gt 0 ] || fail "L10N_LANGUAGES 为空，拒绝生成一个没有语言变体的 schema"
for lang in "${LANGUAGES[@]}"; do
    seen=0
    for other in "${LANGUAGES[@]}"; do [ "$other" = "$lang" ] && seen=$((seen + 1)); done
    [ "$seen" -le 1 ] || fail "L10N_LANGUAGES 有重复语言码：$lang"
done
BEAN_FIELD="${CFG_L10N_BEAN_FIELD:-text}"
VARIANTS="$(IFS=,; echo "${LANGUAGES[*]}")"

# ---------- 阶段 ----------
copy_runtime_scripts() {
    local pair src_key dst_key src dst
    for pair in "CONFIG_SCRIPT_SOURCE CONFIG_SCRIPT_TARGET" \
                "CONFIGINIT_SCRIPT_SOURCE CONFIGINIT_SCRIPT_TARGET" \
                "EXTERNALTYPEUTIL_SCRIPT_SOURCE EXTERNALTYPEUTIL_SCRIPT_TARGET"; do
        src_key="${pair%% *}"; dst_key="${pair#* }"
        src="$(cfg_get "$src_key")"; dst="$(cfg_get "$dst_key")"
        # 三件都可选：项目不覆盖某个处理器时留空即可，但留了源头就必须存在
        [ -z "$src" ] && [ -z "$dst" ] && continue
        [ -n "$src" ] && [ -n "$dst" ] || fail "${CONFIG_FILE}：$src_key 与 $dst_key 要同时给"
        [ -f "$src" ] || fail "$src 不存在"
        if [ "$CHECK_MODE" = 1 ]; then
            # 校验模式不落盘：目标文件必须已存在且与模板逐字节一致
            [ -f "$dst" ] || fail "CHECK：运行期处理器目标缺失：$dst（先跑一次正常导出）"
            cmp -s "$src" "$dst" || fail "CHECK：$dst 与模板 $src 不一致（模板更新后未重新导出）"
        else
            mkdir -p "$(dirname "$dst")"
            cp -f "$src" "$dst" || fail "拷贝失败：$src -> $dst"
        fi
    done
}

# 由 L10N_LANGUAGES 派生 Luban 用的变体声明 xml。必须在插件管线之前写好：
# luban_l10n.conf / luban_validate.conf 的 schema 都引用这里生成的 bean 定义。
# xml 里的 comment 会成为生成代码的 /// <summary>，所以用中文与框架口径一致。
generate_l10n_schema() {
    local xml="${CFG_L10N_SCHEMA_XML:-Temp/l10n_schema.xml}"

    mkdir -p "$(dirname "$xml")"
    {
        echo '<module name="L10n">'
        echo '    <bean name="LocalizationBean" comment="按语言分份导出的多语言词条">'
        echo "        <var name=\"$BEAN_FIELD\" type=\"string\" variants=\"$VARIANTS\"/>"
        echo '    </bean>'
        echo '</module>'
    } > "$xml" || fail "写 $xml 失败"
    echo "[l10n] 变体声明 -> $xml (variants=$VARIANTS)"
}

# 客户端导出：单进程插件管线完成 校验 → 常规表 → 多语言代码 → 各语言数据，
# 产物经插件暂存区字节级提交（正常）或与真实产物比对（check）。
# 运行期处理器的拷贝/比对仍由本脚本负责；--conf 传 luban.conf 只为满足 Luban CLI
# 必填项，管线内部各趟的 conf 取自 config.ini。
run_client() {
    step "客户端：拷贝运行期处理器（check 模式改为比对）"
    copy_runtime_scripts

    step "客户端：派生多语言变体声明 xml"
    generate_l10n_schema

    local plugin_args=()
    [ "$CHECK_MODE" = 1 ] && plugin_args+=(-x "moirai.check=true")
    plugin_args+=(-x "moirai.format=${FORMAT}")

    step "客户端：单进程多语言导出（moirai-l10n 管线）"
    dotnet "$LUBAN" -t client -p moirai-l10n --conf "${CFG_CONF}" \
        "${template_args[@]}" \
        -x code.lineEnding=crlf \
        -x data.lineEnding=crlf \
        "${optional_args[@]}" \
        -x "outputSaver=moirai-stage" \
        -x "moirai.config=${CONFIG_FILE}" \
        "${plugin_args[@]}" \
        || fail "插件管线失败"
}

# Luban 的 file header 自带前导空行（\r\n 打在 ////---- 之前），内置模板产物一样有，
# 不是模板能改掉的 —— 写完后统一去掉，让生成码与框架其他文件头部一致。
# （客户端由插件在暂存区内完成同等处理；这里只服务服务端。）
strip_generated_header_blank_line() {
    local dir file
    for dir in "$@"; do
        [ -n "$dir" ] && [ -d "$dir" ] || continue
        while IFS= read -r file; do
            [ -z "$(head -1 "$file" | tr -d '\r\n')" ] || continue
            # in-place 的 sed 参数形态 GNU 与 BSD 不一致，一条 tail 两支通用。
            if tail -n +2 "$file" > "$file.tmp.$$"; then
                mv -f "$file.tmp.$$" "$file"
            else
                rm -f "$file.tmp.$$"
                fail "剥离首行失败：$file"
            fi
        done < <(find "$dir" -type f -name '*.cs' | sort)
    done
}

run_server() {
    [ -n "${CFG_DATA_OUTPUT_PATH_SERVER:-}" ] || fail "$CONFIG_FILE 缺少键 DATA_OUTPUT_PATH_SERVER（节 [server]）"
    [ -n "${CFG_CODE_OUTPUT_PATH_SERVER:-}" ] || fail "$CONFIG_FILE 缺少键 CODE_OUTPUT_PATH_SERVER（节 [server]）"
    # check 模式下产物落暂存区，真实服务端目录零写入
    local code_out="$CFG_CODE_OUTPUT_PATH_SERVER"
    local data_out="$CFG_DATA_OUTPUT_PATH_SERVER"
    if [ "$CHECK_MODE" = 1 ]; then
        code_out="$CHECK_STAGE/server_code"
        data_out="$CHECK_STAGE/server_data"
    fi
    # 服务端一趟不带 --variant（多语言不在服务端 schema 里），也不带懒加载模板：
    # Client_LazyLoad 只服务客户端，服务端要懒加载得另建一份模板目录并在这里显式传入。
    step "服务端：常规表（${DATA_TARGET}，内置模板 = 构造期加载）"
    dotnet "$LUBAN" -t server -c "$CODE_TARGET" -d "$DATA_TARGET" --conf "${CFG_CONF}" \
        -x code.lineEnding=crlf \
        -x data.lineEnding=crlf \
        "${optional_args[@]}" \
        -x "outputCodeDir=${code_out}" \
        -x "outputDataDir=${data_out}" \
        || fail "服务端趟失败"
    [ -n "$(ls -A "$data_out" 2>/dev/null)" ] \
        || echo "[WARN] 服务端数据输出目录为空或缺失：${data_out}（当前没有分组为 s 的表时属正常）"
    strip_generated_header_blank_line "${code_out}"
}

# ---------- CHECK：服务端暂存区与真实产物逐字节比对 ----------
# diff -rq 的 Only in / differ 覆盖 多出 / 缺失 / 内容差异 三类；.meta 由 Unity 维护，
# 排除。运行期处理器与客户端产物的比对都在插件/拷贝阶段内完成；这里只比对服务端。
check_compare() {
    local rc=0
    compare_tree() {
        local out
        out="$(diff -rq -x '*.meta' "$2" "$3" 2>&1)" || rc=1
        if [ -n "$out" ]; then
            echo "[CHECK] $1 差异："
            printf '%s\n' "$out"
        else
            echo "[CHECK] $1 一致"
        fi
    }
    if [ "$TARGET" = server ] || [ "$TARGET" = all ]; then
        # 没有 s 分组表时服务端产物为空：真实目录不存在且暂存区也无文件时跳过比对（属正常）
        compare_server_tree() {
            local label="$1" real="$2" staged="$3"
            if [ ! -d "$real" ] && [ ! -n "$(ls -A "$staged" 2>/dev/null)" ]; then
                echo "[CHECK] $label 跳过（无 s 分组表，两侧均为空）"
                return
            fi
            compare_tree "$label" "$real" "$staged"
        }
        compare_server_tree "服务端代码" "$CFG_CODE_OUTPUT_PATH_SERVER" "$CHECK_STAGE/server_code"
        compare_server_tree "服务端数据" "$CFG_DATA_OUTPUT_PATH_SERVER" "$CHECK_STAGE/server_data"
    fi
    rm -rf "$CHECK_STAGE"
    [ "$rc" = 0 ] || fail "CHECK 失败：生成产物与仓库不一致（先跑一次 ./gen.sh $TARGET，提交产物后再入 CI）"
}

case "$TARGET" in
    client) run_client ;;
    server) run_server ;;
    all)    run_client; run_server ;;
    *)      fail "未知目标：${TARGET}（可用 client | server | all）" ;;
esac

if [ "$CHECK_MODE" = 1 ]; then
    check_compare
    echo "[OK] CHECK 通过：生成产物与仓库一致（$TARGET）"
else
    echo "[OK] 转表完成：$TARGET"
fi
