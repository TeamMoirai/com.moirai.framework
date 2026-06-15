@echo off
cd /d "%~dp0"

REM 读取配置文件（跳过 # 开头的注释和空行）
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("path_export.conf") do (
    REM 输出环境变量
    echo set %%a=%%b
    REM 设置环境变量
    set "%%a=%%b"
)