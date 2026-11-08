#!/usr/bin/env bash
# 温暖如初 - Android APK 打包脚本
# 需要: .NET 10 SDK + Android SDK

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
OUTPUT_DIR="$SCRIPT_DIR/publish"

echo "[1/3] Restore..."
dotnet restore "$PROJECT_DIR/WarmAsBefore.csproj"

echo "[2/3] Build Android APK..."
dotnet publish "$PROJECT_DIR/WarmAsBefore.csproj" \
  -f net10.0-android \
  -c Release \
  -o "$OUTPUT_DIR" \
  -p:AndroidPackageFormat=aab \
  -p:ApplicationId=com.warmasbefore.app

echo "[3/3] Done!"
ls -lh "$OUTPUT_DIR"/*.aab "$OUTPUT_DIR"/*.apk 2>/dev/null || true