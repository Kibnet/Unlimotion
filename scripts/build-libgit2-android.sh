#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT_DIR/scripts/android-native-common.sh"

SRC_DIR="${SRC_DIR:-$ROOT_DIR/.native/libgit2-src}"
LIB_NAME="libgit2-3f4182d.so"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
LIBSSH2_VERSION="${LIBSSH2_VERSION:-1.11.1}"
LIBGIT2_HTTPS_BACKEND="${LIBGIT2_HTTPS_BACKEND:-OpenSSL}"
LIBGIT2_USE_SSH="${LIBGIT2_USE_SSH:-ON}"

android_native_select_abi
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-/data/data/com.termux/files/home/android-sdk}}"
android_native_find_ndk

BUILD_DIR="${BUILD_DIR:-$ROOT_DIR/.native/libgit2-build-$ANDROID_RID}"
LIB_OUTPUT_PATH="${LIB_OUTPUT_PATH:-$ROOT_DIR/artifacts/android-native/libgit2-$ANDROID_RID/$LIB_NAME}"
OPENSSL_ROOT_DIR="${OPENSSL_ROOT_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-$ANDROID_RID/prefix}"
OPENSSL_INCLUDE_DIR="${OPENSSL_INCLUDE_DIR:-$OPENSSL_ROOT_DIR/include}"
OPENSSL_SSL_LIBRARY="${OPENSSL_SSL_LIBRARY:-$OPENSSL_ROOT_DIR/lib/libssl.so.3}"
OPENSSL_CRYPTO_LIBRARY="${OPENSSL_CRYPTO_LIBRARY:-$OPENSSL_ROOT_DIR/lib/libcrypto.so.3}"
LIBSSH2_ROOT_DIR="${LIBSSH2_ROOT_DIR:-$ROOT_DIR/artifacts/android-native/libssh2-$LIBSSH2_VERSION-$ANDROID_RID/prefix}"
LIBSSH2_INCLUDE_DIR="${LIBSSH2_INCLUDE_DIR:-$LIBSSH2_ROOT_DIR/include}"
LIBSSH2_LIBRARY="${LIBSSH2_LIBRARY:-$LIBSSH2_ROOT_DIR/lib/libssh2.so}"

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
  -DANDROID_ABI="$ANDROID_ABI"
  -DANDROID_PLATFORM="android-$ANDROID_API_LEVEL"
  -DBUILD_SHARED_LIBS=ON
  -DBUILD_TESTS=OFF
  -DBUILD_CLI=OFF
  -DBUILD_EXAMPLES=OFF
  -DBUILD_FUZZERS=OFF
  -DUSE_SSH="$LIBGIT2_USE_SSH"
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

if [ "$LIBGIT2_USE_SSH" = "ON" ]; then
  if [ ! -f "$LIBSSH2_INCLUDE_DIR/libssh2.h" ]; then
    echo "Android libssh2 headers not found under $LIBSSH2_INCLUDE_DIR. Run: ANDROID_ABI=$ANDROID_ABI bash ./scripts/build-libssh2-android.sh"
    exit 1
  fi

  if [ ! -f "$LIBSSH2_LIBRARY" ]; then
    LIBSSH2_LIBRARY="$(find "$LIBSSH2_ROOT_DIR/lib" -type f -name "libssh2.so*" | sort | head -n 1 || true)"
  fi

  if [ -z "$LIBSSH2_LIBRARY" ] || [ ! -f "$LIBSSH2_LIBRARY" ]; then
    echo "Android libssh2 shared library not found under $LIBSSH2_ROOT_DIR/lib. Run: ANDROID_ABI=$ANDROID_ABI bash ./scripts/build-libssh2-android.sh"
    exit 1
  fi

  CMAKE_ARGS+=(
    -DLIBSSH2_INCLUDE_DIR="$LIBSSH2_INCLUDE_DIR"
    -DLIBSSH2_LIBRARY="$LIBSSH2_LIBRARY"
    -DCMAKE_PREFIX_PATH="$OPENSSL_ROOT_DIR;$LIBSSH2_ROOT_DIR"
  )
fi

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
