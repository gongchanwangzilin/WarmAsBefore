@echo off
REM ============================================
REM 温暖如初 - Windows EXE 打包脚本
REM 需要: .NET 10 SDK（net10.0-windows10.0.19041.0）
REM 说明: .NET 10 已移除 win10-x64 RID，统一用 win-x64 +
REM       RuntimeIdentifierOverride（WindowsAppSDK #3337 的官方 workaround）
REM ============================================

echo ========================================
echo  温暖如初 - Windows 发布打包
echo ========================================

set PROJECT_DIR=%~dp0
set OUTPUT_DIR=%~dp0publish

echo [1/3] 还原依赖...
dotnet restore "%PROJECT_DIR%\WarmAsBefore.csproj"

echo [2/3] 编译发布（Windows EXE）...
dotnet publish "%PROJECT_DIR%\WarmAsBefore.csproj" ^
  -f net10.0-windows10.0.19041.0 ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -o "%OUTPUT_DIR%" ^
  -p:RuntimeIdentifierOverride=win-x64 ^
  -p:UseMonoRuntime=false ^
  -p:DebugType=none

echo [3/3] 复制资源...
if not exist "%OUTPUT_DIR%\Resources" mkdir "%OUTPUT_DIR%\Resources"
xcopy /E /I /Y "%PROJECT_DIR%\Resources\Raw" "%OUTPUT_DIR%\Resources\Raw" >nul 2>&1
xcopy /E /I /Y "%PROJECT_DIR%\Resources\Fonts" "%OUTPUT_DIR%\Resources\Fonts" >nul 2>&1

echo.
echo ========================================
echo  打包完成!
echo  输出目录: %OUTPUT_DIR%
echo  运行: %OUTPUT_DIR%\WarmAsBefore.exe
echo ========================================
pause
