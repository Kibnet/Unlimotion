#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE_ID="${PACKAGE_ID:-LibGit2Sharp.NativeBinaries}"
UPSTREAM_VERSION="${UPSTREAM_VERSION:-2.0.323}"
PACKAGE_VERSION="${PACKAGE_VERSION:-${LIBGIT2_NATIVE_PACKAGE_VERSION:-2.0.324-android.4}}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
NUGET_LOCAL_FEED="${NUGET_LOCAL_FEED:-/storage/emulated/0/nuget-local}"
LIBGIT2_PATH="${LIBGIT2_PATH:-$ROOT_DIR/libgit2-3f4182d.so}"
OPENSSL_LIB_DIR="${OPENSSL_LIB_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64/prefix/lib}"
DOWNLOAD_DIR="${PACKAGE_DOWNLOAD_DIR:-$ROOT_DIR/artifacts/android-native/nuget-downloads}"
UPSTREAM_PACKAGE_PATH="$DOWNLOAD_DIR/${PACKAGE_ID}.${UPSTREAM_VERSION}.nupkg"
OUTPUT_PACKAGE_PATH="$NUGET_LOCAL_FEED/${PACKAGE_ID}.${PACKAGE_VERSION}.nupkg"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/libgit2sharp-nativebinaries-android.XXXXXX")"
STAGING_DIR="$TEMP_DIR/package"

cleanup() {
  rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

for required_file in \
  "$LIBGIT2_PATH" \
  "$OPENSSL_LIB_DIR/libssl.so.3" \
  "$OPENSSL_LIB_DIR/libcrypto.so.3"; do
  if [ ! -f "$required_file" ]; then
    echo "Missing required file: $required_file"
    exit 1
  fi
done

mkdir -p "$DOWNLOAD_DIR" "$NUGET_LOCAL_FEED" "$STAGING_DIR"

if [ ! -f "$UPSTREAM_PACKAGE_PATH" ]; then
  curl -fL \
    "https://api.nuget.org/v3-flatcontainer/libgit2sharp.nativebinaries/$UPSTREAM_VERSION/libgit2sharp.nativebinaries.$UPSTREAM_VERSION.nupkg" \
    -o "$UPSTREAM_PACKAGE_PATH"
fi

unzip -q "$UPSTREAM_PACKAGE_PATH" -d "$STAGING_DIR"
rm -f "$STAGING_DIR/.signature.p7s"

mkdir -p "$STAGING_DIR/runtimes/android-arm64/native"
install -m 0644 "$LIBGIT2_PATH" "$STAGING_DIR/runtimes/android-arm64/native/libgit2-3f4182d.so"
install -m 0644 "$OPENSSL_LIB_DIR/libssl.so.3" "$STAGING_DIR/runtimes/android-arm64/native/libssl.so.3"
install -m 0644 "$OPENSSL_LIB_DIR/libcrypto.so.3" "$STAGING_DIR/runtimes/android-arm64/native/libcrypto.so.3"

sed -i "s#<version>$UPSTREAM_VERSION</version>#<version>$PACKAGE_VERSION</version>#" "$STAGING_DIR/$PACKAGE_ID.nuspec"
rm -f "$OUTPUT_PACKAGE_PATH"

pushd "$STAGING_DIR" >/dev/null
zip -X -q -r "$OUTPUT_PACKAGE_PATH" .
popd >/dev/null

for required_entry in \
  "runtimes/android-arm64/native/libgit2-3f4182d.so" \
  "runtimes/android-arm64/native/libssl.so.3" \
  "runtimes/android-arm64/native/libcrypto.so.3"; do
  if ! unzip -l "$OUTPUT_PACKAGE_PATH" "$required_entry" >/dev/null 2>&1; then
    echo "Packed package is missing $required_entry"
    exit 1
  fi
done

echo "Wrote $OUTPUT_PACKAGE_PATH"
