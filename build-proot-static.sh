#!/usr/bin/env bash
# 静态编译 proot（NDK，aarch64-linux-android）
# 用法：把 NDK 的 llvm-objcopy 加进 PATH 后跑本脚本
#   export PATH=".../ndk/toolchains/llvm/prebuilt/windows-x86_64/bin:$PATH"
#   bash build-proot-static.sh
set -euo pipefail

NDK="/e/WarmAsBefore/src/WarmAsBefore/android-sdk/android-ndk-r27c"
PROOT_SRC="$(cd "$(dirname "$0")" && pwd)/proot-build/proot-5.1.107.96/src"
OUT="$(cd "$(dirname "$0")" && pwd)/native/aarch64"

CC="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android24-clang"
OBJCOPY="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-objcopy"
OBJDUMP="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-objdump"
STRIP="$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-strip"

# 先把 talloc 静态库编译出来（Termux 只有 .so，我们用它的 .a 路径）
# 从 talloc 源码编译 .a
TALLOC_LIB="/e/WarmAsBefore/src/WarmAsBefore/proot-build/talloc-static/libtalloc.a"

echo "==> 1. 编译 talloc 静态库"
if [ ! -f "$TALLOC_LIB" ]; then
  TALLOC_SRC="/e/WarmAsBefore/src/WarmAsBefore/proot-build/talloc"
  if [ -d "$TALLOC_SRC" ]; then
    make -C "$TALLOC_SRC" clean >/dev/null 2>&1 || true
    # talloc 的 Makefile 支持交叉编译
    make -C "$TALLOC_SRC" CC="$CC" --no-print-directory 2>&1 | tail -5
    cp "$TALLOC_SRC/libtalloc.la" 2>/dev/null || true
    # 找编译出的 .a
    for f in "$TALLOC_SRC"/libtalloc*.$(echo a); do :; done
    if [ -f "$TALLOC_SRC/libtalloc.a" ]; then
      mkdir -p "$(dirname "$TALLOC_LIB")"
      cp "$TALLOC_SRC/libtalloc.a" "$TALLOC_LIB"
      echo "    talloc 静态库: $TALLOC_LIB ($(du -h "$TALLOC_LIB" | cut -f1))"
    else
      echo "    警告: 未找到 talloc .a，继续动态链接"
      TALLOC_LIB=""
    fi
  fi
fi

echo "==> 2. 编译 proot（静态 + 内嵌 loader）"
cd "$PROOT_SRC"

TALLOC_LDFLAGS=""
if [ -n "${TALLOC_LIB:-}" ]; then
  TALLOC_LDFLAGS="-L$(dirname "$TALLOC_LIB") -ltalloc -static"
  echo "    链接 talloc 静态: $TALLOC_LDFLAGS"
fi

make clean 2>/dev/null || true
make CC="$CC" STRIP="$STRIP" OBJCOPY="$OBJCOPY" OBJDUMP="$OBJDUMP" \
     LDFLAGS="$TALLOC_LDFLAGS -static" \
     CFLAGS="-O2 -Wall -Wextra" \
     PROOT_WITH_LIBANDROID_SHMEM= \
     --no-print-directory 2>&1 | tail -15

if [ -f proot ]; then
  cp proot "$OUT/proot-static"
  echo "==> 3. 输出: $OUT/proot-static ($(du -h "$OUT/proot-static" | cut -f1))"
  # 验证无动态依赖
  "$NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-readelf" -d "$OUT/proot-static" 2>/dev/null | grep -i "NEEDED" || echo "    无 NEEDED 动态依赖（静态成功）"
else
  echo "编译失败"
  exit 1
fi
