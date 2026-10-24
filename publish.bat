@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ==========================================
echo  TrayClockApp .NET 发布脚本
echo ==========================================

rem ---- 自动读取 csproj 的 Version 节点作为发布版本（无需手动传参） ----
set "APP_VERSION="
for /f "tokens=3 delims=<>" %%v in ('findstr /i /c:"<Version>" TrayClockApp.csproj') do (
  set "APP_VERSION=%%v"
)
if not defined APP_VERSION (
  echo [警告] 未在 TrayClockApp.csproj 中找到 Version 节点，将使用默认 1.0.0
  set "APP_VERSION=1.0.0"
)
echo 发布版本: %APP_VERSION%

rem ---- 从 csproj 的 TargetFramework 节点推导所需 .NET 主版本（net10.0-windows 推导出 10） ----
set "DOTNET_MAJOR=10"
set "APP_TFM="
for /f "tokens=3 delims=<>" %%t in ('findstr /i /c:"<TargetFramework>" TrayClockApp.csproj') do set "APP_TFM=%%t"
rem 只接受形如 net10 的取值；格式变化时回退默认主版本，避免给出错误版本号
echo %APP_TFM%| findstr /r /c:"^net[0-9]" >nul || set "APP_TFM="
if not defined APP_TFM goto tfm_ready
set "TFM_REST=%APP_TFM:~3%"
for /f "tokens=1 delims=." %%m in ("%TFM_REST%") do set "DOTNET_MAJOR=%%m"
:tfm_ready
if defined APP_TFM (echo 目标框架: %APP_TFM%) else (echo 目标框架: 未识别，按 %DOTNET_MAJOR%.x 处理)
echo 所需 SDK: %DOTNET_MAJOR%.x
echo.

rem ---- 若应用正在运行则先停止（避免发布时文件被占用） ----
tasklist /fi "imagename eq TrayClockApp.exe" 2>nul | find /i "TrayClockApp.exe" >nul
if not errorlevel 1 (
  echo 检测到 TrayClockApp 正在运行，正在停止...
  taskkill /f /im TrayClockApp.exe >nul 2>&1
  if errorlevel 1 (
    echo [错误] 停止 TrayClockApp 失败，请手动关闭后重试。
    pause
    exit /b 1
  )
  echo 已停止。
)

rem ---- 定位带所需 SDK 的 dotnet ----
rem 依次探测用户级、机器级与 PATH，逐个校验 --list-sdks 是否含所需主版本，
rem 避免命中只有运行时或版本过低的安装后在构建阶段才报 NETSDK1045。
set "DOTNET_EXE="
call :try_dotnet_path "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
call :try_dotnet_path "%LOCALAPPDATA%\dotnet\dotnet.exe"
call :try_dotnet_path "%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE (
  where dotnet >nul 2>&1
  if not errorlevel 1 (
    dotnet --list-sdks 2>nul | findstr /r /c:"^%DOTNET_MAJOR%\." >nul
    if not errorlevel 1 set "DOTNET_EXE=dotnet"
  )
)
if not defined DOTNET_EXE goto no_sdk
echo 使用 dotnet: %DOTNET_EXE%
echo.

echo [1/3] Restore...
"%DOTNET_EXE%" restore TrayClockApp.csproj || (echo 还原失败 & pause & exit /b 1)

echo [2/3] Build (Release)...
"%DOTNET_EXE%" build TrayClockApp.csproj -c Release || (echo 构建失败 & pause & exit /b 1)

echo [3/3] Publish single-file self-contained compressed (v%APP_VERSION%)...
"%DOTNET_EXE%" publish TrayClockApp.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:Version=%APP_VERSION% -p:DebugType=None -p:DebugSymbols=false ^
  -o publish || (echo 发布失败 & pause & exit /b 1)

echo.
echo 完成！TrayClockApp v%APP_VERSION% 输出目录: %~dp0publish
pause
exit /b 0

rem ---- 子程序：候选路径存在且带所需主版本 SDK 时记入 DOTNET_EXE ----
:try_dotnet_path
if defined DOTNET_EXE exit /b 0
if "%~1"=="" exit /b 0
if not exist "%~1" exit /b 0
"%~1" --list-sdks 2>nul | findstr /r /c:"^%DOTNET_MAJOR%\." >nul || exit /b 0
set "DOTNET_EXE=%~1"
exit /b 0

:no_sdk
echo.
echo [错误] 未找到带 .NET %DOTNET_MAJOR%.x SDK 的 dotnet。
echo   已按以下顺序探测并排除：
echo     %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
echo     %LOCALAPPDATA%\dotnet\dotnet.exe
echo     %ProgramFiles%\dotnet\dotnet.exe
echo     PATH 中的 dotnet
echo.
echo   请安装 .NET SDK %DOTNET_MAJOR% 后重试：
echo     winget install Microsoft.DotNet.SDK.%DOTNET_MAJOR%
echo   或访问 https://dotnet.microsoft.com/download/dotnet/%DOTNET_MAJOR%.0
pause
exit /b 1
