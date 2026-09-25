#!/usr/bin/env bash
# 转表唯一入口（也是唯一一份逻辑）。Windows 用 gen.bat，它只负责找到 bash 再调本文件。
#
#   ./gen.sh                       客户端，路线与加载类型取 config.ini 的缺省值
#   ./gen.sh server                服务端表（不含多语言，故不涉及变体）
#   ./gen.sh all                   客户端 + 服务端
#   ./gen.sh client --format=json  当次改用 json 路线（缺省 bin）
#   ./gen.sh client --load=eager   当次退回内置模板：构造期加载全部表（缺省 lazy）
#
# 缺省值写在 config.ini：DATA_FORMAT=bin|json 决定 -c/-d 这一对，LAZY_LOAD 决定用不用懒加载模板。
# 所有路径与语言清单也都来自同目录的 config.ini；本文件不内嵌任何项目路径。

set -o pipefail

cd "$(dirname "$0")" || exit 1
CONFIG_FILE="config.ini"

TARGET=""
FORMAT_OVERRIDE=""
LOAD_OVERRIDE=""

fail() { echo "[ERROR] $*" >&2; exit 1; }
step() { echo "===== $* ====="; }

usage() {
    # 只打印 shebang 之后连续那段注释头，遇到第一行非注释就停
    awk 'NR>1 && /^#/{print; next} NR>1{exit}' "$0"
}

# 参数：<目标> [--format=bin|json] [--load=lazy|eager]
# 两个开关都只在缺省时才读 config.ini，显式传入即覆盖配置。
while [ "$#" -gt 0 ]; do
    case "$1" in
        client|server|all)
            [ -n "$TARGET" ] && fail "目标只能给一个，已确定是 $TARGET，又多给一个 $1"
            TARGET="$1" ;;
        --format=*) FORMAT_OVERRIDE="${1#*=}" ;;
        --load=*)   LOAD_OVERRIDE="${1#*=}" ;;
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
declare -A CFG
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
    CFG["$key"]="$value"
done < "$CONFIG_FILE"

need() { [ -n "${CFG[$1]:-}" ] || fail "$CONFIG_FILE 缺少键 $1（节 [$2]）"; }

need LUBAN_DLL tools
need CONF luban
need L10N_CONF luban
need DATA_OUTPUT_PATH_CLIENT client
need CODE_OUTPUT_PATH_CLIENT client
need CODE_OUTPUT_PATH_L10N client
need L10N_LANGUAGES l10n

LUBAN="${CFG[LUBAN_DLL]}"
[ -f "$LUBAN" ] || fail "Luban 不存在：$LUBAN（先跑 build-luban，或把编译好的文件放进 Luban/）"

# 可选键：缺失时按空处理，对应参数整条不传
optional_args=()
[ -n "${CFG[PATH_VALIDATOR_ROOT]:-}" ] && optional_args+=(-x "pathValidator.rootDir=${CFG[PATH_VALIDATOR_ROOT]}")

# ---------- 生成路线（数据格式）与加载类型 ----------
# 缺省值写在 config.ini：DATA_FORMAT 决定 code/data target 这一对，LAZY_LOAD 决定用不用懒加载模板。
# 命令行 --format / --load 只是当次覆盖，不改配置——排查问题时代价最低的做法。
FORMAT="${FORMAT_OVERRIDE:-${CFG[DATA_FORMAT]:-bin}}"
case "$FORMAT" in
    bin)  CODE_TARGET=cs-bin       ; DATA_TARGET=bin  ;;
    json) CODE_TARGET=cs-simple-json; DATA_TARGET=json ;;
    *) fail "DATA_FORMAT/--format 只认 bin 或 json，收到：$FORMAT" ;;
esac

LOAD="${LOAD_OVERRIDE:-${CFG[LAZY_LOAD]:-true}}"
case "$LOAD" in
    lazy|true|TRUE|1)   LOAD=lazy ;;
    eager|false|FALSE|0) LOAD=eager ;;
    *) fail "LAZY_LOAD/--load 只认 lazy 或 eager，收到：$LOAD" ;;
esac

# 懒加载模板按 code target 分目录（Luban 找的是 <dir>/<codeTarget>/tables.sbn）：
# 少一份就只是那一趟静默退回内置模板，症状是"换了 json 路线就不懒加载了"，所以逐份确认。
# 三趟（常规 / 多语言代码 / 各语言数据）必须用同一个模板目录，故只在这里算一次。
LAZY_TEMPLATE_DIR="${CFG[CUSTOM_TEMPLATE_ROOT]:-Templates/}Client_LazyLoad"
template_args=()
if [ "$LOAD" = eager ]; then
    echo "[gen.sh] 路线 $FORMAT，加载类型 eager：三趟统一用内置模板（构造期加载全部表）"
elif [ -f "$LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn" ]; then
    template_args=(--customTemplateDir "$LAZY_TEMPLATE_DIR")
    echo "[gen.sh] 路线 $FORMAT，加载类型 lazy（模板 $LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn）"
else
    echo "[WARN] 懒加载模板缺失：$LAZY_TEMPLATE_DIR/$CODE_TARGET/tables.sbn"
    echo "[WARN] 三趟统一退回内置模板（构造期加载全部表）"
fi

# 语言清单：唯一真源，空格分隔
read -r -a LANGUAGES <<< "${CFG[L10N_LANGUAGES]}"
[ "${#LANGUAGES[@]}" -gt 0 ] || fail "L10N_LANGUAGES 为空，拒绝生成一个没有语言变体的 schema"
for lang in "${LANGUAGES[@]}"; do
    seen=0
    for other in "${LANGUAGES[@]}"; do [ "$other" = "$lang" ] && seen=$((seen + 1)); done
    [ "$seen" -le 1 ] || fail "L10N_LANGUAGES 有重复语言码：$lang"
done
BEAN_FIELD="${CFG[L10N_BEAN_FIELD]:-text}"
VARIANTS="$(IFS=,; echo "${LANGUAGES[*]}")"

# ---------- 阶段 ----------
copy_runtime_scripts() {
    local pair src_key dst_key src dst
    for pair in "CONFIG_SCRIPT_SOURCE CONFIG_SCRIPT_TARGET" \
                "CONFIGINIT_SCRIPT_SOURCE CONFIGINIT_SCRIPT_TARGET" \
                "EXTERNALTYPEUTIL_SCRIPT_SOURCE EXTERNALTYPEUTIL_SCRIPT_TARGET"; do
        src_key="${pair%% *}"; dst_key="${pair#* }"
        src="${CFG[$src_key]:-}"; dst="${CFG[$dst_key]:-}"
        # 三件都可选：项目不覆盖某个处理器时留空即可，但留了源头就必须存在
        [ -z "$src" ] && [ -z "$dst" ] && continue
        [ -n "$src" ] && [ -n "$dst" ] || fail "$CONFIG_FILE：$src_key 与 $dst_key 要同时给"
        [ -f "$src" ] || fail "$src 不存在"
        mkdir -p "$(dirname "$dst")"
        cp -f "$src" "$dst" || fail "拷贝失败：$src -> $dst"
    done
}

# 由 L10N_LANGUAGES 派生 Luban 用的变体声明 xml。必须在多语言那几趟之前写好。
# xml 里的 comment 会成为生成代码的 /// <summary>，所以用中文与框架口径一致。
generate_l10n_schema() {
    local xml="${CFG[L10N_SCHEMA_XML]:-Temp/l10n_schema.xml}"

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

# 游戏侧自报语言用的 C# 常量。
# ⚠ 必须在常规趟之后调用：常量落在 Luban 的代码输出目录里时，那一趟的代码 saver 会把
# "不属于本次生成范围"的已存在文件当多余项删掉——实测主趟日志出现
# [remove] .../Gen\L10n\L10nLanguages.cs 且退出码仍是 0，先写后跑等于白写。
# 注释是中文，故文件带 UTF-8 BOM：与 Templates 下那几个 .cs 一致，也不看编辑器脸色。
generate_language_constant() {
    local cs="${CFG[L10N_LANG_LIST_CODE]:-}"
    local ns="${CFG[L10N_LANG_CLASS_NAMESPACE]:-Moirai.GameProto.Config}"

    [ -n "$cs" ] || fail "$CONFIG_FILE 缺少键 L10N_LANG_LIST_CODE（节 [l10n]）"
    mkdir -p "$(dirname "$cs")"
    {
        printf '\xef\xbb\xbf'
        echo '//------------------------------------------------------------------------------'
        echo '// <auto-generated>'
        echo '//     This code was generated by a tool.'
        echo '//     Changes to this file may cause incorrect behavior and will be lost if'
        echo '//     the code is regenerated.'
        echo '// </auto-generated>'
        echo '//------------------------------------------------------------------------------'
        echo ''
        echo "namespace $ns"
        echo '{'
        echo '    /// <summary>'
        echo '    /// 本次转表导出了哪几种语言，顺序即框架内的列序（回退链按此下标取列）。'
        echo '    /// 每种语言的数据在配置表根目录下的同名子目录里；用哪一种语言由 LocalizationService 决定。'
        echo '    /// </summary>'
        echo '    public static class L10nLanguages'
        echo '    {'
        echo '        public static readonly string[] Codes ='
        echo '        {'
        local i
        for i in "${!LANGUAGES[@]}"; do
            if [ "$i" -lt "$(( ${#LANGUAGES[@]} - 1 ))" ]; then
                echo "            \"${LANGUAGES[$i]}\","
            else
                echo "            \"${LANGUAGES[$i]}\""
            fi
        done
        echo '        };'
        echo '    }'
        echo '}'
    } > "$cs" || fail "写 $cs 失败"
    echo "[l10n] 语言常量 -> $cs"
}

# Luban 的 file header 自带一个前导空行（\r\n 打在 ////---- 之前），所有产物一律如此——
# 内置模板生成的 ItemConfig.cs 也一样，所以这不是模板能改掉的，只在写完后统一去掉，
# 让生成码与框架内其他文件的头部一致。
strip_generated_header_blank_line() {
    local dir file
    for dir in "$@"; do
        [ -n "$dir" ] && [ -d "$dir" ] || continue
        while IFS= read -r file; do
            [ -z "$(head -1 "$file" | tr -d '\r\n')" ] && sed -i '1d' "$file"
        done < <(find "$dir" -type f -name '*.cs' | sort)
    done
}

run_client() {
    step "客户端：拷贝运行期处理器"
    copy_runtime_scripts

    step "客户端：派生多语言变体声明 xml"
    generate_l10n_schema

    step "客户端 1/3：常规表（语言无关，一趟出代码与数据）"
    dotnet "$LUBAN" -t client -c "$CODE_TARGET" -d "$DATA_TARGET" --conf "${CFG[CONF]}" \
        "${template_args[@]}" \
        -x code.lineEnding=crlf \
        "${optional_args[@]}" \
        -x "outputCodeDir=${CFG[CODE_OUTPUT_PATH_CLIENT]}" \
        -x "outputDataDir=${CFG[DATA_OUTPUT_PATH_CLIENT]}" \
        || fail "常规趟失败"

    # 多语言代码落在 Gen/L10n/（与主趟同树、分目录）。必须在常规趟之后：常规趟的清理是递归的，
    # 会把 Gen/L10n/ 整个删掉；反过来这一趟的清理只及自己目录，不会碰常规表的类。
    # 多语言代码一趟也要带 --variant：bean 的字段声明了 variants，Luban 每次解析 schema 都要求定一版，
    # 不带就刷 "type:'L10n.LocalizationBean' field:'text' not set variant" 警告。
    # 实测"不带 / zh-Hans / en"三种跑法的产物逐字节相同（bean 只剩一个字段，代码本就与语言无关），
    # 所以这里取清单第一项只为满足解析器——它不进入任何文件名或路径，不是"默认语言"。
    step "客户端 2/3：多语言代码（bean 只剩一个变体字段，代码与语言无关）"
    dotnet "$LUBAN" -t client -c "$CODE_TARGET" --conf "${CFG[L10N_CONF]}" \
        "${template_args[@]}" \
        --variant "default=${LANGUAGES[0]}" \
        -x code.lineEnding=crlf \
        "${optional_args[@]}" \
        -x "outputCodeDir=${CFG[CODE_OUTPUT_PATH_L10N]}" \
        || fail "多语言代码趟失败"

    # 一次进程只解析一版变体，所以有几种语言就跑几趟；且必须排在常规趟之后
    # （常规趟的 bin saver 会把输出目录连同语言子目录一起清掉，退出码还是 0）
    local data_root="${CFG[DATA_OUTPUT_PATH_CLIENT]}"
    local lang
    for lang in "${LANGUAGES[@]}"; do
        step "客户端 3/3：按语言导数据 -> ${data_root}${lang}"
        dotnet "$LUBAN" -t client -d "$DATA_TARGET" --conf "${CFG[L10N_CONF]}" \
            "${template_args[@]}" \
            --variant "default=${lang}" \
            "${optional_args[@]}" \
            -x "outputDataDir=${data_root}${lang}" \
            || fail "语言 $lang 的数据趟失败"
    done

    # 所有会清 Gen/ 的趟都跑完了，才写这个落在生成目录里的常量（见函数注释）
    step "客户端：生成运行期语言常量"
    generate_language_constant
    strip_generated_header_blank_line "${CFG[CODE_OUTPUT_PATH_CLIENT]}" "${CFG[CODE_OUTPUT_PATH_L10N]}"
}

run_server() {
    [ -n "${CFG[DATA_OUTPUT_PATH_SERVER]:-}" ] || fail "$CONFIG_FILE 缺少键 DATA_OUTPUT_PATH_SERVER（节 [server]）"
    # 服务端一趟不带 --variant（多语言不在服务端 schema 里），也不带懒加载模板：
    # Client_LazyLoad 只服务客户端，服务端要懒加载得另建一份模板目录并在这里显式传入。
    step "服务端：常规表（$DATA_TARGET，内置模板 = 构造期加载）"
    dotnet "$LUBAN" -t server -c "$CODE_TARGET" -d "$DATA_TARGET" --conf "${CFG[CONF]}" \
        -x code.lineEnding=crlf \
        "${optional_args[@]}" \
        -x "outputCodeDir=${CFG[CODE_OUTPUT_PATH_SERVER]}" \
        -x "outputDataDir=${CFG[DATA_OUTPUT_PATH_SERVER]}" \
        || fail "服务端趟失败"
    strip_generated_header_blank_line "${CFG[CODE_OUTPUT_PATH_SERVER]}"
}

case "$TARGET" in
    client) run_client ;;
    server) run_server ;;
    all)    run_client; run_server ;;
    *)      fail "未知目标：$TARGET（可用 client | server | all）" ;;
esac

echo "[OK] 转表完成：$TARGET"
