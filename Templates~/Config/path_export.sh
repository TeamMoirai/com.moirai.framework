#!/bin/bash

# 切换到脚本所在目录
# 用 BASH_SOURCE 而不是 $0：本文件是被 source 的，$0 是调用方脚本的路径，
# 从别的目录 source 时 cd 会跑偏、path_define.conf 找不到，然后静默什么都不导出
cd "$(dirname "${BASH_SOURCE[0]:-$0}")" || return 1

# 读取配置文件（跳过 # 开头的注释行与空行）
# IFS='=' 只按第一个等号分割，值里的 = 与空格保持原样。
# 不要用 xargs 去空白：它会把路径里的反斜杠当转义符吃掉
# （"..\Client\Assets\Table\" 会被削成 "..ClientAssetsTable"），
# 也会把多空格的语言清单压成单空格以外的形态。
while IFS='=' read -r key value; do
    # 跳过注释和空行（含仅有空白 / 仅有 Windows 换行残留的行）
    [[ -z "${key// /}" || "${key:0:1}" == "#" ]] && continue
    # 去除两端空白（含行尾 \r）
    key="${key%$'\r'}"; key="${key#"${key%%[![:space:]]*}"}"; key="${key%"${key##*[![:space:]]}"}"
    value="${value%$'\r'}"; value="${value#"${value%%[![:space:]]*}"}"; value="${value%"${value##*[![:space:]]}"}"
    # 键名不含空白，多余空白一律去掉
    key="${key//[[:space:]]/}"
    [[ -z "$key" ]] && continue
    # 将反斜杠转为正斜杠（内部空格不动）
    value="${value//\\//}"
    # 输出环境变量
    echo "export ${key}=${value}"
    # 导出环境变量
    export "${key}=${value}"
done < "path_define.conf"
