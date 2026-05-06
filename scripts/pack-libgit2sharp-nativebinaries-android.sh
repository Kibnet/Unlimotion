#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE_ID="${PACKAGE_ID:-LibGit2Sharp.NativeBinaries}"
UPSTREAM_VERSION="${UPSTREAM_VERSION:-2.0.323}"
PACKAGE_VERSION="${PACKAGE_VERSION:-${LIBGIT2_NATIVE_PACKAGE_VERSION:-2.0.324-android.6}}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
LIBSSH2_VERSION="${LIBSSH2_VERSION:-1.11.1}"
ANDROID_ABIS="${ANDROID_ABIS:-arm64-v8a x86_64}"
NUGET_LOCAL_FEED="${NUGET_LOCAL_FEED:-$ROOT_DIR/artifacts/nuget-local}"
DOWNLOAD_DIR="${PACKAGE_DOWNLOAD_DIR:-$ROOT_DIR/artifacts/android-native/nuget-downloads}"
UPSTREAM_PACKAGE_PATH="$DOWNLOAD_DIR/${PACKAGE_ID}.${UPSTREAM_VERSION}.nupkg"
OUTPUT_PACKAGE_PATH="$NUGET_LOCAL_FEED/${PACKAGE_ID}.${PACKAGE_VERSION}.nupkg"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/libgit2sharp-nativebinaries-android.XXXXXX")"
STAGING_DIR="$TEMP_DIR/package"

cleanup() {
  rm -rf "$TEMP_DIR"
}

create_package_archive() {
  if command -v zip >/dev/null 2>&1; then
    pushd "$STAGING_DIR" >/dev/null
    zip -X -q -r "$OUTPUT_PACKAGE_PATH" .
    popd >/dev/null
    return
  fi

  local python_bin=""
  if command -v python3 >/dev/null 2>&1; then
    python_bin="python3"
  elif command -v python >/dev/null 2>&1; then
    python_bin="python"
  fi

  if [ -n "$python_bin" ]; then
    "$python_bin" - "$STAGING_DIR" "$OUTPUT_PACKAGE_PATH" <<'PY'
import pathlib
import sys
import zipfile

staging_dir = pathlib.Path(sys.argv[1])
output_path = pathlib.Path(sys.argv[2])

with zipfile.ZipFile(output_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for candidate in sorted(staging_dir.rglob("*")):
        if candidate.is_dir():
            continue
        archive.write(candidate, candidate.relative_to(staging_dir).as_posix())
PY
    return
  fi

  if command -v powershell.exe >/dev/null 2>&1; then
    local staging_windows_path="$STAGING_DIR"
    local output_windows_path="$OUTPUT_PACKAGE_PATH"

    if command -v cygpath >/dev/null 2>&1; then
      staging_windows_path="$(cygpath -w "$STAGING_DIR")"
      output_windows_path="$(cygpath -w "$OUTPUT_PACKAGE_PATH")"
    fi

    powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'Stop'; \$stagingPath = '$staging_windows_path'; \$outputPath = '$output_windows_path'; if (Test-Path -LiteralPath \$outputPath) { Remove-Item -LiteralPath \$outputPath -Force }; Compress-Archive -Path (Join-Path \$stagingPath '*') -DestinationPath \$outputPath -CompressionLevel Optimal"
    return
  fi

  echo "Neither zip, python, nor powershell.exe is available to create $OUTPUT_PACKAGE_PATH"
  exit 1
}

trap cleanup EXIT

rid_for_abi() {
  case "$1" in
    arm64-v8a|android-arm64|arm64)
      echo "android-arm64"
      ;;
    x86_64|android-x64|x64)
      echo "android-x64"
      ;;
    *)
      echo "Unsupported Android ABI: $1" >&2
      exit 1
      ;;
  esac
}

stage_runtime() {
  local abi="$1"
  local rid
  rid="$(rid_for_abi "$abi")"
  local native_dir="$STAGING_DIR/runtimes/$rid/native"
  local libgit2_path="${LIBGIT2_PATH:-$ROOT_DIR/artifacts/android-native/libgit2-$rid/libgit2-3f4182d.so}"
  local openssl_lib_dir="${OPENSSL_LIB_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-$rid/prefix/lib}"
  local libssh2_lib_dir="${LIBSSH2_LIB_DIR:-$ROOT_DIR/artifacts/android-native/libssh2-$LIBSSH2_VERSION-$rid/prefix/lib}"

  for required_file in \
    "$libgit2_path" \
    "$openssl_lib_dir/libssl.so.3" \
    "$openssl_lib_dir/libcrypto.so.3"; do
    if [ ! -f "$required_file" ]; then
      echo "Missing required file for $rid: $required_file"
      exit 1
    fi
  done

  if ! compgen -G "$libssh2_lib_dir/libssh2.so*" >/dev/null; then
    echo "Missing libssh2 shared library for $rid under $libssh2_lib_dir"
    exit 1
  fi

  mkdir -p "$native_dir"
  install -m 0644 "$libgit2_path" "$native_dir/libgit2-3f4182d.so"
  install -m 0644 "$openssl_lib_dir/libssl.so.3" "$native_dir/libssl.so.3"
  install -m 0644 "$openssl_lib_dir/libcrypto.so.3" "$native_dir/libcrypto.so.3"

  for libssh2_path in "$libssh2_lib_dir"/libssh2.so*; do
    if [ -f "$libssh2_path" ]; then
      install -m 0644 "$libssh2_path" "$native_dir/$(basename "$libssh2_path")"
    fi
  done
}

mkdir -p "$DOWNLOAD_DIR" "$NUGET_LOCAL_FEED" "$STAGING_DIR"

if [ ! -f "$UPSTREAM_PACKAGE_PATH" ]; then
  curl -fL \
    "https://api.nuget.org/v3-flatcontainer/libgit2sharp.nativebinaries/$UPSTREAM_VERSION/libgit2sharp.nativebinaries.$UPSTREAM_VERSION.nupkg" \
    -o "$UPSTREAM_PACKAGE_PATH"
fi

unzip -q "$UPSTREAM_PACKAGE_PATH" -d "$STAGING_DIR"
rm -f "$STAGING_DIR/.signature.p7s"

for abi in $ANDROID_ABIS; do
  stage_runtime "$abi"
done

sed -i "s#<version>$UPSTREAM_VERSION</version>#<version>$PACKAGE_VERSION</version>#" "$STAGING_DIR/$PACKAGE_ID.nuspec"
rm -f "$OUTPUT_PACKAGE_PATH"

create_package_archive

for abi in $ANDROID_ABIS; do
  rid="$(rid_for_abi "$abi")"
  for required_entry in \
    "runtimes/$rid/native/libgit2-3f4182d.so" \
    "runtimes/$rid/native/libssl.so.3" \
    "runtimes/$rid/native/libcrypto.so.3"; do
    if ! unzip -l "$OUTPUT_PACKAGE_PATH" "$required_entry" >/dev/null 2>&1; then
      echo "Packed package is missing $required_entry"
      exit 1
    fi
  done

  if ! unzip -l "$OUTPUT_PACKAGE_PATH" "runtimes/$rid/native/libssh2.so*" >/dev/null 2>&1; then
    echo "Packed package is missing libssh2 for $rid"
    exit 1
  fi
done

echo "Wrote $OUTPUT_PACKAGE_PATH"
