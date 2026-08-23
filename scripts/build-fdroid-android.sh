#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ANDROID_PROJECT="$ROOT_DIR/src/Unlimotion.Android/Unlimotion.Android.csproj"
VERSION_NAME="${VERSION_NAME:-}"
VERSION_CODE="${VERSION_CODE:-}"
OPENSSL_VERSION="3.0.21"
LIBSSH2_VERSION="1.11.1"
OPENSSL_SHA256="617e29af8e421f46649484a4937e48c685e47f46488167c982f88bc4ec1d522f"
LIBSSH2_SHA256="d9ec76cbe34db98eec3539fe2c899d26b0c837cb3eb466a56b0f109cabf658f7"
OPENSSL_ARCHIVE="$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64/downloads/openssl-$OPENSSL_VERSION.tar.gz"
LIBSSH2_ARCHIVE="$ROOT_DIR/artifacts/android-native/libssh2-$LIBSSH2_VERSION-android-arm64/downloads/libssh2-$LIBSSH2_VERSION.tar.gz"
FDROID_ARTIFACTS_DIR="${FDROID_ARTIFACTS_DIR:-$ROOT_DIR/artifacts/fdroid}"
NUGET_LOCAL_FEED="$ROOT_DIR/artifacts/nuget-local"
NUGET_CONFIG="$ROOT_DIR/src/nuget.config"
ANDROID_SDK_DIR="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
OUTPUT_APK="$FDROID_ARTIFACTS_DIR/Unlimotion-$VERSION_NAME-$VERSION_CODE-android-arm64.apk"

export AVALONIA_TELEMETRY_OPTOUT=1
export DOTNET_NUGET_SIGNATURE_VERIFICATION=true
export NUGET_LOCAL_FEED

if ! command -v dotnet >/dev/null 2>&1 && [ -x '/c/Program Files/dotnet/dotnet.exe' ]; then
  export PATH="/c/Program Files/dotnet:$PATH"
fi

fail() {
  echo "$1" >&2
  exit 1
}

sha256_file() {
  local file_path="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file_path" | awk '{print tolower($1)}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file_path" | awk '{print tolower($1)}'
  else
    fail "sha256sum or shasum is required to verify source archives."
  fi
}

ensure_verified_archive() {
  local archive_path="$1"
  local archive_url="$2"
  local expected_sha256="$3"
  local archive_name="$4"

  mkdir -p "$(dirname "$archive_path")"
  if [ ! -f "$archive_path" ]; then
    curl -fL "$archive_url" -o "$archive_path"
  fi

  local actual_sha256
  actual_sha256="$(sha256_file "$archive_path")"
  if [ "$actual_sha256" != "$expected_sha256" ]; then
    fail "$archive_name SHA-256 mismatch: $actual_sha256 (expected $expected_sha256)"
  fi
}

if ! [[ "$VERSION_NAME" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  fail "VERSION_NAME must be an explicit semantic version, for example 1.28.0."
fi

if ! [[ "$VERSION_CODE" =~ ^[1-9][0-9]*$ ]]; then
  fail "VERSION_CODE must be an explicit positive integer, for example 1028000."
fi

if [ -z "$ANDROID_SDK_DIR" ]; then
  fail "ANDROID_SDK_ROOT or ANDROID_HOME must point to the installed Android SDK."
fi

effective_sdk="$(dotnet --version)"
if ! [[ "$effective_sdk" =~ ^10\.0\.1[0-9]{2}$ ]]; then
  fail "F-Droid build requires a stable .NET 10.0.1xx SDK, got $effective_sdk."
fi

workload_list="$(dotnet workload list)"
missing_workloads=()
for workload_id in android wasm-tools; do
  if ! grep -Eq "(^|[[:space:]])$workload_id([[:space:]]|$)" <<<"$workload_list"; then
    missing_workloads+=("$workload_id")
  fi
done

if [ "${#missing_workloads[@]}" -gt 0 ]; then
  if [ "${FDROID_INSTALL_ANDROID_WORKLOAD:-0}" != "1" ]; then
    fail "Missing .NET workloads: ${missing_workloads[*]}. Set FDROID_INSTALL_ANDROID_WORKLOAD=1 to install the pinned manifest workloads."
  fi
  dotnet workload install "${missing_workloads[@]}" --skip-manifest-update
fi

ensure_verified_archive \
  "$OPENSSL_ARCHIVE" \
  "https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/openssl-$OPENSSL_VERSION.tar.gz" \
  "$OPENSSL_SHA256" \
  "OpenSSL $OPENSSL_VERSION"

ensure_verified_archive \
  "$LIBSSH2_ARCHIVE" \
  "https://libssh2.org/download/libssh2-$LIBSSH2_VERSION.tar.gz" \
  "$LIBSSH2_SHA256" \
  "libssh2 $LIBSSH2_VERSION"

export ANDROID_ABI=arm64-v8a
bash "$ROOT_DIR/scripts/build-openssl-android.sh"
bash "$ROOT_DIR/scripts/build-libssh2-android.sh"
bash "$ROOT_DIR/scripts/build-libgit2-android.sh"
bash "$ROOT_DIR/scripts/pack-libgit2sharp-nativebinaries-fdroid.sh"
bash "$ROOT_DIR/scripts/pack-nodify-fdroid.sh"

mkdir -p "$FDROID_ARTIFACTS_DIR" "$NUGET_LOCAL_FEED"

dotnet restore "$ANDROID_PROJECT" \
  --configfile "$NUGET_CONFIG" \
  --force \
  --no-cache \
  -p:AndroidSdkDirectory="$ANDROID_SDK_DIR" \
  -p:FdroidBuild=true \
  -p:RuntimeIdentifier=android-arm64 \
  -p:RuntimeIdentifiers=android-arm64

dotnet build "$ANDROID_PROJECT" \
  --configuration Release \
  --no-restore \
  --target Rebuild \
  -p:AndroidSdkDirectory="$ANDROID_SDK_DIR" \
  -p:FdroidBuild=true \
  -p:RuntimeIdentifier=android-arm64 \
  -p:RuntimeIdentifiers=android-arm64 \
  -p:ApplicationDisplayVersion="$VERSION_NAME" \
  -p:ApplicationVersion="$VERSION_CODE"

UNSIGNED_APK="$ROOT_DIR/src/Unlimotion.Android/bin/Release/net10.0-android/android-arm64/com.Kibnet.Unlimotion.apk"
if [ ! -f "$UNSIGNED_APK" ]; then
  fail "Unsigned F-Droid APK was not produced: $UNSIGNED_APK"
fi

mkdir -p "$FDROID_ARTIFACTS_DIR"
cp "$UNSIGNED_APK" "$OUTPUT_APK"

MERGED_MANIFEST="$ROOT_DIR/src/Unlimotion.Android/obj/Release/net10.0-android/android-arm64/android/AndroidManifest.xml"
if [ ! -f "$MERGED_MANIFEST" ]; then
  fail "Merged Android manifest was not produced: $MERGED_MANIFEST"
fi

if grep -Eq 'REQUEST_INSTALL_PACKAGES|androidx\.core\.content\.FileProvider|apk_file_paths' "$MERGED_MANIFEST"; then
  fail "F-Droid merged manifest still contains updater-only capabilities."
fi

if unzip -l "$OUTPUT_APK" | grep -q 'apk_file_paths'; then
  fail "F-Droid APK still contains the updater file-path resource."
fi

echo "F-Droid APK: $OUTPUT_APK"
echo "Effective .NET SDK: $effective_sdk"
