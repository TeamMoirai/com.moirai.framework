#!/bin/bash

# 切换到脚本所在目录
cd "$(dirname "$0")"

# 读取配置文件（跳过注释和空行）
# IFS='=' 按等号分割。
while IFS='=' read -r key value; do
    # 跳过注释和空行
    [[ -z "$key" || "$key" =~ ^# ]] && continue
    # 去除两端空格
    key=$(echo "$key" | xargs)
    value=$(echo "$value" | xargs)
    # 将反斜杠转为正斜杠
    value="${value//\\//}"
    # 输出环境变量
    echo "export $(key)=$(value)"
    # 导出环境变量    
    export "$key=$value"
done < "path_export.conf"
