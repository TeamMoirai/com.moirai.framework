@echo off
REM Windows launcher only: locate bash, then hand the arguments to gen.sh in this folder.
REM All export logic lives in gen.sh (the single bash driver) - never duplicate it here.
REM
REM   gen.bat                        client (same as: gen.bat client)
REM   gen.bat server                 server tables (no l10n, so no variants)
REM   gen.bat all                    client + server
REM   gen.bat client --format=json   this run only: json route (default: bin)
REM   gen.bat client --load=eager    this run only: builtin templates (default: lazy)
REM   gen.bat --help                 usage, printed by gen.sh itself
REM
REM Keep this file ASCII-only: cmd.exe decodes a BOM-less .bat with the console codepage,
REM so non-ASCII comment bytes get parsed as commands and the script runs its own comments.
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
