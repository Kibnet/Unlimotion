#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
OPENSSL_BASE_URL="${OPENSSL_BASE_URL:-https://www.openssl.org/source}"
BUILD_ROOT="${OPENSSL_BUILD_ROOT:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64}"
DOWNLOAD_DIR="$BUILD_ROOT/downloads"
SOURCE_PARENT_DIR="$BUILD_ROOT/src"
SOURCE_DIR="$SOURCE_PARENT_DIR/openssl-$OPENSSL_VERSION"
INSTALL_DIR="${OPENSSL_OUTPUT_DIR:-$BUILD_ROOT/prefix}"
ARCHIVE_PATH="$DOWNLOAD_DIR/openssl-$OPENSSL_VERSION.tar.gz"

cpu_count() {
  if command -v nproc >/dev/null 2>&1; then
    nproc
    return
  fi

  sysctl -n hw.ncpu
}

host_tag() {
  case "$(uname -s)" in
    Linux)
      echo "linux-x86_64"
      ;;
    Darwin)
      if [ "$(uname -m)" = "arm64" ]; then
        echo "darwin-arm64"
      else
        echo "darwin-x86_64"
      fi
      ;;
    *)
      echo "Unsupported host OS: $(uname -s)" >&2
      exit 1
      ;;
  esac
}

if [ -z "$ANDROID_SDK_ROOT" ]; then
  echo "ANDROID_SDK_ROOT or ANDROID_HOME must be set"
  exit 1
fi

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

TOOLCHAIN_DIR="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/$(host_tag)"
if [ ! -d "$TOOLCHAIN_DIR" ]; then
  echo "Android LLVM toolchain not found: $TOOLCHAIN_DIR"
  exit 1
fi

if [ -f "$INSTALL_DIR/lib/libssl.so.3" ] && [ -f "$INSTALL_DIR/lib/libcrypto.so.3" ] && [ "${FORCE_REBUILD:-0}" != "1" ]; then
  echo "Reusing existing Android OpenSSL build in $INSTALL_DIR"
  exit 0
fi

mkdir -p "$DOWNLOAD_DIR" "$SOURCE_PARENT_DIR"

if [ ! -f "$ARCHIVE_PATH" ]; then
  curl -fL "$OPENSSL_BASE_URL/openssl-$OPENSSL_VERSION.tar.gz" -o "$ARCHIVE_PATH"
fi

rm -rf "$SOURCE_DIR" "$INSTALL_DIR"
tar -xzf "$ARCHIVE_PATH" -C "$SOURCE_PARENT_DIR"

export PATH="$TOOLCHAIN_DIR/bin:$PATH"
export CC="$TOOLCHAIN_DIR/bin/aarch64-linux-android${ANDROID_API_LEVEL}-clang"
export CXX="$TOOLCHAIN_DIR/bin/aarch64-linux-android${ANDROID_API_LEVEL}-clang++"
export AR="$TOOLCHAIN_DIR/bin/llvm-ar"
export AS="$CC"
export LD="$TOOLCHAIN_DIR/bin/ld"
export RANLIB="$TOOLCHAIN_DIR/bin/llvm-ranlib"
export STRIP="$TOOLCHAIN_DIR/bin/llvm-strip"

pushd "$SOURCE_DIR" >/dev/null
./Configure \
  android-arm64 \
  "-D__ANDROID_API__=$ANDROID_API_LEVEL" \
  shared \
  no-tests \
  no-unit-test \
  no-docs \
  --prefix="$INSTALL_DIR" \
  --openssldir="$INSTALL_DIR/ssl"

make -j"$(cpu_count)"
make install_sw
popd >/dev/null

"$STRIP" --strip-unneeded "$INSTALL_DIR/lib/libssl.so.3" "$INSTALL_DIR/lib/libcrypto.so.3"
echo "Wrote $INSTALL_DIR/lib/libssl.so.3"
echo "Wrote $INSTALL_DIR/lib/libcrypto.so.3"
