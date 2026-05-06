#!/usr/bin/env bash
set -euo pipefail

android_native_cpu_count() {
  if command -v nproc >/dev/null 2>&1; then
    nproc
    return
  fi

  sysctl -n hw.ncpu
}

android_native_host_tag() {
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
    MINGW*|MSYS*|CYGWIN*)
      echo "windows-x86_64"
      ;;
    *)
      echo "Unsupported host OS: $(uname -s)" >&2
      exit 1
      ;;
  esac
}

android_native_normalize_host_path() {
  local path="$1"
  if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]] && command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$path"
    return
  fi

  echo "$path"
}

android_native_select_abi() {
  ANDROID_ABI="${ANDROID_ABI:-arm64-v8a}"

  case "$ANDROID_ABI" in
    arm64-v8a|android-arm64|arm64)
      ANDROID_ABI="arm64-v8a"
      ANDROID_RID="android-arm64"
      ANDROID_OPENSSL_TARGET="android-arm64"
      ANDROID_CLANG_TARGET="aarch64-linux-android"
      ;;
    x86_64|android-x64|x64)
      ANDROID_ABI="x86_64"
      ANDROID_RID="android-x64"
      ANDROID_OPENSSL_TARGET="android-x86_64"
      ANDROID_CLANG_TARGET="x86_64-linux-android"
      ;;
    *)
      echo "Unsupported Android ABI: $ANDROID_ABI. Supported values: arm64-v8a, x86_64." >&2
      exit 1
      ;;
  esac

  export ANDROID_ABI ANDROID_RID ANDROID_OPENSSL_TARGET ANDROID_CLANG_TARGET
}

android_native_find_ndk() {
  ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
  if [ -z "$ANDROID_SDK_ROOT" ]; then
    echo "ANDROID_SDK_ROOT or ANDROID_HOME must be set" >&2
    exit 1
  fi

  if [ -z "${ANDROID_NDK_ROOT:-}" ]; then
    if [ -d "$ANDROID_SDK_ROOT/ndk" ]; then
      ANDROID_NDK_ROOT="$(ls -d "$ANDROID_SDK_ROOT/ndk/"* 2>/dev/null | sort -V | tail -1)"
    elif [ -d "$ANDROID_SDK_ROOT/ndk-bundle" ]; then
      ANDROID_NDK_ROOT="$ANDROID_SDK_ROOT/ndk-bundle"
    else
      echo "ANDROID_NDK_ROOT not set and no NDK found under $ANDROID_SDK_ROOT" >&2
      exit 1
    fi
  fi

  ANDROID_SDK_ROOT="$(android_native_normalize_host_path "$ANDROID_SDK_ROOT")"
  ANDROID_NDK_ROOT="$(android_native_normalize_host_path "$ANDROID_NDK_ROOT")"

  export ANDROID_SDK_ROOT ANDROID_NDK_ROOT
}

android_native_toolchain_dir() {
  local toolchain_dir="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/$(android_native_host_tag)"
  if [ ! -d "$toolchain_dir" ]; then
    echo "Android LLVM toolchain not found: $toolchain_dir" >&2
    exit 1
  fi

  echo "$toolchain_dir"
}

android_native_readelf() {
  local toolchain_dir="$1"
  local readelf_path="$toolchain_dir/bin/llvm-readelf"
  if [ -x "$readelf_path" ]; then
    echo "$readelf_path"
    return
  fi

  if command -v llvm-readelf >/dev/null 2>&1; then
    command -v llvm-readelf
    return
  fi

  echo ""
}
