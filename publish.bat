@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ==========================================
echo  TrayClockApp .NET 发布脚本
echo ==========================================

rem ---- 自动读取 TrayClockApp.csproj 中的 <Version> 作为发布版本（无需手动传参） ----
set "APP_VERSION="
for /f "tokens=3 delims=<>" %%v in ('findstr /i /c:"<Version>" TrayClockApp.csproj') do (
  set "APP_VERSION=%%v"
)
if not defined APP_VERSION (
  echo [警告] 未在 TrayClockApp.csproj 中找到 Version 节点，将使用默认 1.0.0
  set "APP_VERSION=1.0.0"
)
echo 发布版本: %APP_VERSION%
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

rem ---- 定位可用的 dotnet（优先用户级 SDK） ----
set "DOTNET_EXE="
if exist "%LOCALAPPDATA%\dotnet\dotnet.exe" set "DOTNET_EXE=%LOCALAPPDATA%\dotnet\dotnet.exe"
if not defined DOTNET_EXE (
  where dotnet >nul 2>&1 && set "DOTNET_EXE=dotnet"
)
if not defined DOTNET_EXE goto no_dotnet

rem ---- 确认该 dotnet 附带 SDK（而非仅有运行时） ----
"%DOTNET_EXE%" --list-sdks >nul 2>&1
if errorlevel 1 goto no_sdk

echo [1/3] Restore...
"%DOTNET_EXE%" restore TrayClockApp.csproj || (echo 还原失败 & pause & exit /b 1)

echo [2/3] Build (Release)...
"%DOTNET_EXE%" build TrayClockApp.csproj -c Release || (echo 构建失败 & pause & exit /b 1)

echo [3/3] Publish single-file self-contained (v%APP_VERSION%)...
"%DOTNET_EXE%" publish TrayClockApp.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:Version=%APP_VERSION% -p:DebugType=None -p:DebugSymbols=false ^
  -o publish || (echo 发布失败 & pause & exit /b 1)

echo.
echo 完成！TrayClockApp v%APP_VERSION% 输出目录: %~dp0publish
pause
exit /b 0

:no_dotnet
echo.
echo [错误] 未找到 dotnet 命令，请先安装 .NET SDK：
echo   1) 打开 https://dotnet.microsoft.com/download/dotnet/10.0
echo   2) 下载并安装 .NET SDK 10（x64）
echo   或运行: winget install Microsoft.DotNet.SDK.10
pause
exit /b 1

:no_sdk
echo.
echo [错误] 找到的 dotnet 未附带 .NET SDK（只有运行时）。
echo   请安装 .NET SDK 10 后重试：
echo   winget install Microsoft.DotNet.SDK.10
echo   或访问 https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1
