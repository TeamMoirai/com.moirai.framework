#!/bin/bash
# 第 1 个参数：lazyload=true 走惰性模板，其余值走默认模板
set -o pipefail

cd "$(dirname "$0")" || exit 1
echo "当前目录: $(pwd)"

source path_export.sh

cp -f "${CONFIG_SCRIPT_SOURCE}" "${CONFIG_SCRIPT_TARGET}" || exit 1
cp -f "${CONFIGINIT_SCRIPT_SOURCE}" "${CONFIGINIT_SCRIPT_TARGET}" || exit 1
cp -f "${EXTERNALTYPEUTIL_SCRIPT_SOURCE}" "${EXTERNALTYPEUTIL_SCRIPT_TARGET}" || exit 1

# 使用 grep -i 不区分大小写
if [ -z "$1" ]; then
    export TEMPLATE_SUFFIX="LazyLoad"
elif echo "$1" | grep -qi "^true$"; then
    export TEMPLATE_SUFFIX="LazyLoad"
else
    export TEMPLATE_SUFFIX="Default"
fi

# 模板目录缺失时三趟一起退回内置模板：只让主趟退回会让多语言类与其余表用不同的模板
TEMPLATE_DIR="${CUSTOM_TEMPLATE_ROOT}CustomTemplate_Client_${TEMPLATE_SUFFIX}"
TEMPLATE_ARGS=()
if [ -d "$TEMPLATE_DIR" ]; then
    TEMPLATE_ARGS=(--customTemplateDir "$TEMPLATE_DIR")
else
    echo "[WARN] $TEMPLATE_DIR not found, built-in templates used for ALL passes."
fi

echo "===== 0/3 由 L10N_LANGUAGES 生成变体声明与运行期语言常量 ====="
# 语言清单整体加引号传给 -Languages：脚本内部按空格/逗号拆分
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/gen_l10n_schema.ps1 \
    -Languages "$L10N_LANGUAGES" \
    -BeanField "$L10N_BEAN_FIELD" \
    -SchemaOut "$L10N_SCHEMA_XML" \
    -LanguageCodeOut "$L10N_LANG_LIST_CODE" || exit 1

echo "===== 1/3 常规表（语言无关） ====="
dotnet "${LUBAN_DLL}" \
    -t client \
    -c cs-bin \
    -d bin \
    --conf "${CONF}" \
    "${TEMPLATE_ARGS[@]}" \
    -x code.lineEnding=crlf \
    -x pathValidator.rootDir="${PATH_VALIDATOR_ROOT}" \
    -x outputCodeDir="${CODE_OUTPUT_PATH_CLIENT}" \
    -x outputDataDir="${DATA_OUTPUT_PATH_CLIENT}" || exit 1

# 代码输出目录必须与主趟分开：代码 saver 会把不属于本次范围的已存在文件当多余项删掉
echo "===== 2/3 多语言代码（各语言共用一份） ====="
dotnet "${LUBAN_DLL}" \
    -t client \
    -c cs-bin \
    --conf "${L10N_CONF}" \
    "${TEMPLATE_ARGS[@]}" \
    -x code.lineEnding=crlf \
    -x pathValidator.rootDir="${PATH_VALIDATOR_ROOT}" \
    -x outputCodeDir="${CODE_OUTPUT_PATH_L10N}" || exit 1

# 必须排在主趟之后：bin saver 默认清理 outputDataDir 连同子目录一起删
# 一次进程只解析一版变体，因此按语言串行，一个语言一个子目录
echo "===== 3/3 按语言导数据: ${L10N_LANGUAGES} ====="
for LANG in $L10N_LANGUAGES; do
    dotnet "${LUBAN_DLL}" \
        -t client \
        -d bin \
        --conf "${L10N_CONF}" \
        "${TEMPLATE_ARGS[@]}" \
        --variant "default=${LANG}" \
        -x pathValidator.rootDir="${PATH_VALIDATOR_ROOT}" \
        -x outputDataDir="${DATA_OUTPUT_PATH_CLIENT}${LANG}" || exit 1
    echo "[l10n] ${LANG} -> ${DATA_OUTPUT_PATH_CLIENT}${LANG}"
done

echo "操作完成"
exit 0
