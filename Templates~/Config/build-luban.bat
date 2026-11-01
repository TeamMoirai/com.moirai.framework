@echo off
REM Windows launcher only: locate bash, then hand off to build-luban.sh in this folder.
REM All version pinning and build logic lives in build-luban.sh - the single
REM implementation shared by Windows and macOS. Never duplicate it here.
REM
REM Keep this file ASCII-only: cmd.exe decodes a BOM-less .bat with the console
REM codepage, so non-ASCII comment bytes would be parsed as commands.
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

echo [build-luban.bat] using bash: !BASH_EXE!
"!BASH_EXE!" ./build-luban.sh %*
set "RC=!ERRORLEVEL!"

:end
if "%~1"=="" pause
exit /b %RC%
