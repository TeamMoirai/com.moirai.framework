@echo off
REM 仅限 Windows 启动器：定位 bash，然后把参数交给此文件夹中的 gen.sh。
REM 所有导出逻辑都放在 gen.sh（唯一的 bash 驱动脚本）中——绝不要在这里重复。
REM
REM   ./gen.sh              # 客户端（等价于 ./gen.sh client）
REM   ./gen.sh client       # 常规表 + 多语言代码 + 按语言数据
REM   ./gen.sh server       # 服务端表（不含多语言，故不涉及变体）
REM   ./gen.sh all          # 客户端 + 服务端
REM   ./gen.sh client false # 第2参：lazyload 开关，非 true/空 走默认模板
REM
REM 保持此文件仅使用 ASCII 字符：cmd.exe 会用控制台代码页解码没有 BOM 的 .bat 文件，
REM 因此非 ASCII 注释字节会被当作命令解析，脚本就会执行自己的注释。
setlocal enabledelayedexpansion
set "RC=0"
cd /d "%~dp0"

set "BASH_EXE="
where bash.exe >nul 2>nul
if not errorlevel 1 set "BASH_EXE=bash.exe"

if not defined BASH_EXE (
    for %%D in (
        "%ProgramFiles%\Git\bin\bash.exe"
        "%ProgramFiles(x86)%\Git\bin\bash.exe"
        "%LOCALAPPDATA%\Programs\Git\bin\bash.exe"
        "%ProgramFiles%\Git\usr\bin\bash.exe"
    ) do if not defined BASH_EXE if exist %%D set "BASH_EXE=%%~D"
)

if not defined BASH_EXE (
    for /f "delims=" %%G in ('where git.exe 2^>nul') do (
        if not defined BASH_EXE (
            for %%B in ("%%~dpG..\bin\bash.exe") do if exist %%B set "BASH_EXE=%%~B"
        )
    )
)

if not defined BASH_EXE (
    echo [ERROR] bash.exe not found. Install Git for Windows, or put bash.exe on PATH.
    set "RC=1"
    goto :end
)

echo [gen.bat] using bash: !BASH_EXE!
"!BASH_EXE!" ./gen.sh %*
set "RC=!ERRORLEVEL!"

:end
if "%~1"=="" pause
exit /b %RC%
