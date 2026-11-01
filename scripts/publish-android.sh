#!/usr/bin/env bash
# 温暖如初 - Android APK 打包
#
# 前提：装了 .NET 10 SDK + Android SDK（ANDROID_HOME / ANDROID_SDK_ROOT 指向 SDK）。
# 这台开发机上没有 Android SDK，所以本脚本只在这台机器上「能跑就算数」地留好，
# 真正出包要么装 SDK，要么走 .github/workflows/build-android.yml。
#
# 用法：
#   bash scripts/publish-android.sh                # APK（默认）
#   bash scripts/publish-android.sh aab            # AAB（上架渠道用）
#
# 产物：publish/android/*.apk（或 *.aab）

set -euo pipefail

FORMAT="${1:-apk}"
case "$FORMAT" in
  apk|aab) ;;
  *) echo "用法：$0 [apk|aab]" >&2; exit 1 ;;
esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJ="$ROOT/WarmAsBefore.csproj"
OUT="$ROOT/publish/android"

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$SDK" ] || [ ! -d "$SDK" ]; then
  echo "没有 Android SDK：设好 ANDROID_HOME（或 ANDROID_SDK_ROOT）再跑；" >&2
  echo "本机目前没装 SDK，可改用 GitHub Actions 的 build-android.yml 出包。" >&2
  exit 1
fi

echo "[1/2] publish -> publish/android ($FORMAT)"
dotnet publish "$PROJ" \
  -c Release \
  -f net10.0-android \
  -p:AndroidPackageFormat="$FORMAT" \
  -o "$OUT"

echo "[2/2] 核对产物"
shopt -s nullglob
files=("$OUT"/*."$FORMAT")
if [ ${#files[@]} -eq 0 ]; then
  echo "没找到 *.$FORMAT：打包命令返回了 0，但产物不在 $OUT" >&2
  exit 2
fi
ls -lh "${files[@]}"
