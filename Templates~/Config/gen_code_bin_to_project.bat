@echo off
setlocal enabledelayedexpansion
set "RC=0"
REM 第 1 个参数：lazyload=true 走惰性模板，其余值走默认模板
cd /d %~dp0

call path_export.bat

echo F |xcopy /s /e /i /y "%CONFIG_SCRIPT_SOURCE%" "%CONFIG_SCRIPT_TARGET%"
echo F |xcopy /s /e /i /y "%CONFIGINIT_SCRIPT_SOURCE%" "%CONFIGINIT_SCRIPT_TARGET%"
echo F |xcopy /s /e /i /y "%EXTERNALTYPEUTIL_SCRIPT_SOURCE%" "%EXTERNALTYPEUTIL_SCRIPT_TARGET%"

REM /i 表示不区分大小写
if "%~1"=="" (set "TEMPLATE_SUFFIX=LazyLoad") else if /i "%~1"=="true" (set "TEMPLATE_SUFFIX=LazyLoad") else (set "TEMPLATE_SUFFIX=Default")

REM 模板目录缺失时三趟一起退回内置模板：只让主趟退回会让多语言类与其余表用不同的模板
set "TEMPLATE_ARGS="
if exist "%CUSTOM_TEMPLATE_ROOT%CustomTemplate_Client_%TEMPLATE_SUFFIX%\" set "TEMPLATE_ARGS=--customTemplateDir %CUSTOM_TEMPLATE_ROOT%CustomTemplate_Client_%TEMPLATE_SUFFIX%"
if not defined TEMPLATE_ARGS echo [WARN] CustomTemplate_Client_%TEMPLATE_SUFFIX% not found, built-in templates used for ALL passes.

REM for 体内不做注释，也不直接拼 %%L：先落到 LANG 再延迟展开

REM ---------- 0/3 由 L10N_LANGUAGES 生成变体声明与运行期语言常量 ----------
REM 语言清单整体加引号传给 -Languages：脚本内部按空格/逗号拆分
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\gen_l10n_schema.ps1 -Languages "%L10N_LANGUAGES%" -BeanField %L10N_BEAN_FIELD% -SchemaOut %L10N_SCHEMA_XML% -LanguageCodeOut "%L10N_LANG_LIST_CODE%"
if errorlevel 1 goto :failed

REM ---------- 1/3 常规表：语言无关，一趟出代码与数据 ----------
dotnet %LUBAN_DLL% ^
    -t client ^
    -c cs-bin ^
    -d bin ^
    --conf %CONF% ^
    %TEMPLATE_ARGS% ^
    -x code.lineEnding=crlf ^
    -x pathValidator.rootDir=%PATH_VALIDATOR_ROOT% ^
    -x outputCodeDir=%CODE_OUTPUT_PATH_CLIENT% ^
    -x outputDataDir=%DATA_OUTPUT_PATH_CLIENT%
if errorlevel 1 goto :failed

REM ---------- 2/3 多语言代码：bean 只剩一个字段，代码与语言无关，一趟出各语言共用的类 ----------
REM 输出目录必须与 1/3 分开：代码 saver 会把不属于本次范围的已存在文件当多余项删掉
dotnet %LUBAN_DLL% ^
    -t client ^
    -c cs-bin ^
    --conf %L10N_CONF% ^
    %TEMPLATE_ARGS% ^
    -x code.lineEnding=crlf ^
    -x pathValidator.rootDir=%PATH_VALIDATOR_ROOT% ^
    -x outputCodeDir=%CODE_OUTPUT_PATH_L10N%
if errorlevel 1 goto :failed

REM ---------- 3/3 按语言串行导数据：一次进程只解析一版变体，一个语言一个子目录 ----------
REM 必须排在 1/3 之后：bin saver 默认清理 outputDataDir 连同子目录一起删
for %%L in (%L10N_LANGUAGES%) do (
    set "LANG=%%L"
    dotnet %LUBAN_DLL% -t client -d bin --conf %L10N_CONF% %TEMPLATE_ARGS% --variant default=!LANG! -x pathValidator.rootDir=%PATH_VALIDATOR_ROOT% -x outputDataDir=%DATA_OUTPUT_PATH_CLIENT%!LANG!
    if errorlevel 1 goto :failed
    echo [l10n] !LANG! -^> %DATA_OUTPUT_PATH_CLIENT%!LANG!
)

echo [OK] config generation done.
goto :end

:failed
set "RC=1"
echo [ERROR] config generation failed.

:end
pause
exit /b !RC!
