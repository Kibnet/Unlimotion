#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT_DIR/scripts/android-native-common.sh"

LIBSSH2_VERSION="${LIBSSH2_VERSION:-1.11.1}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
LIBSSH2_BASE_URL="${LIBSSH2_BASE_URL:-https://www.libssh2.org/download}"

android_native_select_abi
android_native_find_ndk

BUILD_ROOT="${LIBSSH2_BUILD_ROOT:-$ROOT_DIR/artifacts/android-native/libssh2-$LIBSSH2_VERSION-$ANDROID_RID}"
DOWNLOAD_DIR="$BUILD_ROOT/downloads"
SOURCE_PARENT_DIR="$BUILD_ROOT/src"
SOURCE_DIR="$SOURCE_PARENT_DIR/libssh2-$LIBSSH2_VERSION"
INSTALL_DIR="${LIBSSH2_OUTPUT_DIR:-$BUILD_ROOT/prefix}"
ARCHIVE_PATH="$DOWNLOAD_DIR/libssh2-$LIBSSH2_VERSION.tar.gz"
OPENSSL_ROOT_DIR="${OPENSSL_ROOT_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-$ANDROID_RID/prefix}"
OPENSSL_INCLUDE_DIR="${OPENSSL_INCLUDE_DIR:-$OPENSSL_ROOT_DIR/include}"
OPENSSL_CRYPTO_LIBRARY="${OPENSSL_CRYPTO_LIBRARY:-$OPENSSL_ROOT_DIR/lib/libcrypto.so.3}"

TOOLCHAIN_DIR="$(android_native_toolchain_dir)"
CMAKE_TOOLCHAIN="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake"
if [ ! -f "$CMAKE_TOOLCHAIN" ]; then
  echo "Android toolchain not found: $CMAKE_TOOLCHAIN"
  exit 1
fi

if [ -f "$INSTALL_DIR/include/libssh2.h" ] &&
  compgen -G "$INSTALL_DIR/lib/libssh2.so*" >/dev/null &&
  [ "${FORCE_REBUILD:-0}" != "1" ]; then
  echo "Reusing existing Android libssh2 $ANDROID_ABI build in $INSTALL_DIR"
  exit 0
fi

if [ ! -f "$OPENSSL_INCLUDE_DIR/openssl/ssl.h" ] || [ ! -f "$OPENSSL_CRYPTO_LIBRARY" ]; then
  echo "Android OpenSSL artifacts not found for $ANDROID_ABI under $OPENSSL_ROOT_DIR. Run: ANDROID_ABI=$ANDROID_ABI bash ./scripts/build-openssl-android.sh"
  exit 1
fi

mkdir -p "$DOWNLOAD_DIR" "$SOURCE_PARENT_DIR"

if [ ! -f "$ARCHIVE_PATH" ]; then
  curl -fL "$LIBSSH2_BASE_URL/libssh2-$LIBSSH2_VERSION.tar.gz" -o "$ARCHIVE_PATH"
fi

rm -rf "$SOURCE_DIR" "$INSTALL_DIR" "$BUILD_ROOT/build"
tar -xzf "$ARCHIVE_PATH" -C "$SOURCE_PARENT_DIR"

cmake \
  -S "$SOURCE_DIR" \
  -B "$BUILD_ROOT/build" \
  -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$CMAKE_TOOLCHAIN" \
  -DANDROID_ABI="$ANDROID_ABI" \
  -DANDROID_PLATFORM="android-$ANDROID_API_LEVEL" \
  -DCMAKE_INSTALL_PREFIX="$INSTALL_DIR" \
  -DCMAKE_PREFIX_PATH="$OPENSSL_ROOT_DIR" \
  -DBUILD_SHARED_LIBS=ON \
  -DBUILD_STATIC_LIBS=OFF \
  -DBUILD_EXAMPLES=OFF \
  -DBUILD_TESTING=OFF \
  -DCRYPTO_BACKEND=OpenSSL \
  -DOPENSSL_ROOT_DIR="$OPENSSL_ROOT_DIR" \
  -DOPENSSL_INCLUDE_DIR="$OPENSSL_INCLUDE_DIR" \
  -DOPENSSL_CRYPTO_LIBRARY="$OPENSSL_CRYPTO_LIBRARY"

cmake --build "$BUILD_ROOT/build" --target install

STRIP="$TOOLCHAIN_DIR/bin/llvm-strip"
for library_path in "$INSTALL_DIR"/lib/libssh2.so*; do
  if [ -f "$library_path" ]; then
    "$STRIP" --strip-unneeded "$library_path" || true
    echo "Wrote $library_path"
  fi
done
