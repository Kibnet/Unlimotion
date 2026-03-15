#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$ROOT_DIR/.native/libgit2-src"
BUILD_DIR="$ROOT_DIR/.native/libgit2-build-android-arm64"
LIB_NAME="libgit2-3f4182d.so"

ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-/data/data/com.termux/files/home/android-sdk}"

if [ -z "${ANDROID_NDK_ROOT:-}" ]; then
  if [ -d "$ANDROID_SDK_ROOT/ndk" ]; then
    ANDROID_NDK_ROOT="$(ls -d "$ANDROID_SDK_ROOT/ndk/"* 2>/dev/null | sort -V | tail -1)"
  elif [ -d "$ANDROID_SDK_ROOT/ndk-bundle" ]; then
    ANDROID_NDK_ROOT="$ANDROID_SDK_ROOT/ndk-bundle"
  else
    echo "ANDROID_NDK_ROOT not set and no NDK found under $ANDROID_SDK_ROOT"
    exit 1
  fi
fi

if [ ! -d "$SRC_DIR/.git" ]; then
  echo "Missing submodule at $SRC_DIR. Run: git submodule update --init --recursive"
  exit 1
fi

CMAKE_TOOLCHAIN="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake"
if [ ! -f "$CMAKE_TOOLCHAIN" ]; then
  echo "Android toolchain not found: $CMAKE_TOOLCHAIN"
  exit 1
fi

cmake -S "$SRC_DIR" -B "$BUILD_DIR" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$CMAKE_TOOLCHAIN" \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-24 \
  -DBUILD_SHARED_LIBS=ON \
  -DBUILD_CLAR=OFF \
  -DUSE_SSH=OFF \
  -DUSE_HTTPS=ON \
  -DUSE_BUNDLED_ZLIB=ON

cmake --build "$BUILD_DIR" --target git2

LIB_PATH="$(find "$BUILD_DIR" -type f -name "libgit2.so" | head -n 1 || true)"
if [ -z "$LIB_PATH" ]; then
  LIB_PATH="$(find "$BUILD_DIR" -type f -name "libgit2.so.*" | head -n 1 || true)"
fi

if [ -z "$LIB_PATH" ]; then
  echo "libgit2 shared library not found in $BUILD_DIR"
  exit 1
fi

install -D -m 0644 "$LIB_PATH" "$ROOT_DIR/$LIB_NAME"
echo "Wrote $ROOT_DIR/$LIB_NAME"
