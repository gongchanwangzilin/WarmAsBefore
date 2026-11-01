# 温暖如初 - Windows 本地打包
#
# 与 .github/workflows/build-win-x64.yml 同一条命令：CI 出的 artifact 与这里打出来的
# 是同一份东西（区别只在 CI 从干净 checkout 开始）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1 -Output publish/win-x64-test
#
# 产物：publish/win-x64/WarmAsBefore.exe（无 MSIX；WindowsPackageType=None 已在 csproj 里）

param(
    [string]$Configuration = "Release",
    [string]$Output = "publish/win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "WarmAsBefore.csproj"
$out = Join-Path $root $Output

Write-Host "[1/2] publish -> $Output"
dotnet publish $proj `
    -c $Configuration `
    -f net10.0-windows10.0.19041.0 `
    -r win-x64 `
    --self-contained false `
    -p:UseMonoRuntime=false `
    -p:RuntimeIdentifierOverride=win-x64 `
    -o $out

if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }

Write-Host "[2/2] 核对产物"
$exe = Join-Path $out "WarmAsBefore.exe"
if (-not (Test-Path $exe)) { throw "没找到 $exe" }

# 互不夹带：Windows 产物里不许出现 Android 的 proot / aarch64 原生库
$leak = Get-ChildItem -Path $out -Recurse -File |
    Where-Object { $_.Name -match "proot" -or $_.FullName -match "android|aarch64" }
if ($leak) {
    Write-Host "!! 疑似 Android 侧文件被打进 Windows 产物：" -ForegroundColor Yellow
    $leak | Select-Object -First 10 | ForEach-Object { Write-Host "   $($_.FullName)" }
    exit 2
}

$total = (Get-ChildItem -Path $out -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("完成：{0}（{1:N1} MB，含 Resources）" -f $exe, ($total / 1MB)) -ForegroundColor Green
