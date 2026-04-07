#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="${SRC_DIR:-$ROOT_DIR/.native/libgit2-src}"
BUILD_DIR="${BUILD_DIR:-$ROOT_DIR/.native/libgit2-build-android-arm64}"
LIB_NAME="libgit2-3f4182d.so"
LIB_OUTPUT_PATH="${LIB_OUTPUT_PATH:-$ROOT_DIR/$LIB_NAME}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
LIBGIT2_HTTPS_BACKEND="${LIBGIT2_HTTPS_BACKEND:-OpenSSL}"

ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-/data/data/com.termux/files/home/android-sdk}}"
OPENSSL_ROOT_DIR="${OPENSSL_ROOT_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64/prefix}"
OPENSSL_INCLUDE_DIR="${OPENSSL_INCLUDE_DIR:-$OPENSSL_ROOT_DIR/include}"
OPENSSL_SSL_LIBRARY="${OPENSSL_SSL_LIBRARY:-$OPENSSL_ROOT_DIR/lib/libssl.so.3}"
OPENSSL_CRYPTO_LIBRARY="${OPENSSL_CRYPTO_LIBRARY:-$OPENSSL_ROOT_DIR/lib/libcrypto.so.3}"

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

if [ ! -d "$SRC_DIR" ] || ! git -C "$SRC_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Missing submodule at $SRC_DIR. Run: git submodule update --init --recursive"
  exit 1
fi

CMAKE_TOOLCHAIN="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake"
if [ ! -f "$CMAKE_TOOLCHAIN" ]; then
  echo "Android toolchain not found: $CMAKE_TOOLCHAIN"
  exit 1
fi

CMAKE_ARGS=(
  -S "$SRC_DIR"
  -B "$BUILD_DIR"
  -G Ninja
  -DCMAKE_TOOLCHAIN_FILE="$CMAKE_TOOLCHAIN"
  -DANDROID_ABI=arm64-v8a
  -DANDROID_PLATFORM="android-$ANDROID_API_LEVEL"
  -DBUILD_SHARED_LIBS=ON
  -DBUILD_TESTS=OFF
  -DBUILD_CLI=OFF
  -DBUILD_EXAMPLES=OFF
  -DBUILD_FUZZERS=OFF
  -DUSE_SSH=OFF
  -DUSE_HTTPS="$LIBGIT2_HTTPS_BACKEND"
  -DUSE_BUNDLED_ZLIB=ON
)

case "$LIBGIT2_HTTPS_BACKEND" in
  OpenSSL|OpenSSL-Dynamic)
    if [ ! -f "$OPENSSL_INCLUDE_DIR/openssl/ssl.h" ]; then
      echo "Android OpenSSL headers not found under $OPENSSL_INCLUDE_DIR. Run: bash ./scripts/build-openssl-android.sh"
      exit 1
    fi

    CMAKE_ARGS+=(
      -DOPENSSL_ROOT_DIR="$OPENSSL_ROOT_DIR"
      -DOPENSSL_INCLUDE_DIR="$OPENSSL_INCLUDE_DIR"
      -DCMAKE_PREFIX_PATH="$OPENSSL_ROOT_DIR"
    )

    if [ "$LIBGIT2_HTTPS_BACKEND" = "OpenSSL" ]; then
      if [ ! -f "$OPENSSL_SSL_LIBRARY" ] || [ ! -f "$OPENSSL_CRYPTO_LIBRARY" ]; then
        echo "Android OpenSSL shared libraries not found under $OPENSSL_ROOT_DIR/lib. Run: bash ./scripts/build-openssl-android.sh"
        exit 1
      fi

      CMAKE_ARGS+=(
        -DOPENSSL_SSL_LIBRARY="$OPENSSL_SSL_LIBRARY"
        -DOPENSSL_CRYPTO_LIBRARY="$OPENSSL_CRYPTO_LIBRARY"
      )
    fi
    ;;
esac

cmake "${CMAKE_ARGS[@]}"

cmake --build "$BUILD_DIR" --target libgit2package

LIB_PATH="$(find "$BUILD_DIR" -type f -name "libgit2.so" | head -n 1 || true)"
if [ -z "$LIB_PATH" ]; then
  LIB_PATH="$(find "$BUILD_DIR" -type f -name "libgit2.so.*" | head -n 1 || true)"
fi

if [ -z "$LIB_PATH" ]; then
  echo "libgit2 shared library not found in $BUILD_DIR"
  exit 1
fi

install -D -m 0644 "$LIB_PATH" "$LIB_OUTPUT_PATH"
echo "Wrote $LIB_OUTPUT_PATH"
