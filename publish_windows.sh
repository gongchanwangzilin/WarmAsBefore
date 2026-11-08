#!/usr/bin/env bash
# 温暖如初 - Windows EXE 打包脚本 (bash 版)
# 需要: .NET 10 SDK
# 说明: .NET 10 已移除 win10-x64 RID；RuntimeIdentifierOverride 是
#       WindowsAppSDK #3337 的官方 workaround，必须带着。

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
OUTPUT_DIR="$SCRIPT_DIR/publish"

echo "========================================"
echo " 温暖如初 - Windows 发布打包"
echo "========================================"

echo "[1/3] Restore..."
dotnet restore "$PROJECT_DIR/WarmAsBefore.csproj"

echo "[2/3] Publish (Windows EXE)..."
dotnet publish "$PROJECT_DIR/WarmAsBefore.csproj" \
  -f net10.0-windows10.0.19041.0 \
  -c Release \
  -o "$OUTPUT_DIR" \
  -r win-x64 \
  --self-contained false \
  -p:RuntimeIdentifierOverride=win-x64 \
  -p:UseMonoRuntime=false \
  -p:DebugType=none

echo "[3/3] Copy assets..."
mkdir -p "$OUTPUT_DIR/Resources"
cp -r "$PROJECT_DIR/Resources/Raw" "$OUTPUT_DIR/Resources/" 2>/dev/null || true
cp -r "$PROJECT_DIR/Resources/Fonts" "$OUTPUT_DIR/Resources/" 2>/dev/null || true

echo ""
echo "========================================"
echo " 打包完成!"
echo " 输出: $OUTPUT_DIR"
echo " 运行: $OUTPUT_DIR/WarmAsBefore.exe"
echo "========================================"