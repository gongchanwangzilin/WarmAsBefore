#!/bin/bash
# Windows X64 Build Script for WarmAsBefore
# 需要在 Windows 环境或 GitHub Actions 中运行

set -e

echo "=== WarmAsBefore Windows X64 Build ==="
echo "SDK 版本: $(dotnet --version)"

# 清理旧构建
rm -rf ./publish/win-x64

# 发布 Windows X64 版本
dotnet publish WarmAsBefore.csproj \
    -c Release \
    -f net10.0-windows10.0.19041.0 \
    -r win-x64 \
    --self-contained false \
    -p:RuntimeIdentifierOverride=win-x64 \
    -o ./publish/win-x64

echo "=== 构建完成 ==="
echo "输出目录: ./publish/win-x64"
echo ""
echo "主要文件:"
ls -lh ./publish/win-x64/*.exe 2>/dev/null || echo "可执行文件未找到"
echo ""
echo "发布包大小:"
du -sh ./publish/win-x64