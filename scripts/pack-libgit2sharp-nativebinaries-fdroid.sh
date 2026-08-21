#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE_VERSION="${LIBGIT2_NATIVE_PACKAGE_VERSION:-2.0.324-android.7.fdroid.2}"
EXPECTED_LIBGIT2_COMMIT="${LIBGIT2_SOURCE_COMMIT:-155578578b78efc6bae7383a708d470eb206e36a}"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.21}"
LIBSSH2_VERSION="${LIBSSH2_VERSION:-1.11.1}"
NUGET_LOCAL_FEED="${NUGET_LOCAL_FEED:-$ROOT_DIR/artifacts/nuget-local}"
LIBGIT2_SOURCE_DIR="${LIBGIT2_SOURCE_DIR:-$ROOT_DIR/.native/libgit2-src}"
LIBGIT2_PATH="${LIBGIT2_PATH:-$ROOT_DIR/artifacts/android-native/libgit2-android-arm64/libgit2-3f4182d.so}"
OPENSSL_LIB_DIR="${OPENSSL_LIB_DIR:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64/prefix/lib}"
LIBSSH2_LIB_DIR="${LIBSSH2_LIB_DIR:-$ROOT_DIR/artifacts/android-native/libssh2-$LIBSSH2_VERSION-android-arm64/prefix/lib}"
OUTPUT_PACKAGE="$NUGET_LOCAL_FEED/LibGit2Sharp.NativeBinaries.$PACKAGE_VERSION.nupkg"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/unlimotion-fdroid-native-package.XXXXXX")"
PROJECT_DIR="$TEMP_DIR/package-project"
PAYLOAD_DIR="$PROJECT_DIR/payload/runtimes/android-arm64/native"

cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

if ! git -C "$LIBGIT2_SOURCE_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Missing libgit2 source submodule at $LIBGIT2_SOURCE_DIR." >&2
  exit 1
fi

actual_libgit2_commit="$(git -C "$LIBGIT2_SOURCE_DIR" rev-parse HEAD)"
if [ "$actual_libgit2_commit" != "$EXPECTED_LIBGIT2_COMMIT" ]; then
  echo "Unexpected libgit2 source commit: $actual_libgit2_commit (expected $EXPECTED_LIBGIT2_COMMIT)" >&2
  exit 1
fi

for required_file in \
  "$LIBGIT2_PATH" \
  "$OPENSSL_LIB_DIR/libssl.so.3" \
  "$OPENSSL_LIB_DIR/libcrypto.so.3"; do
  if [ ! -f "$required_file" ]; then
    echo "Missing required source-built native library: $required_file" >&2
    exit 1
  fi
done

if ! compgen -G "$LIBSSH2_LIB_DIR/libssh2.so*" >/dev/null; then
  echo "Missing source-built libssh2 libraries under $LIBSSH2_LIB_DIR" >&2
  exit 1
fi

mkdir -p "$PAYLOAD_DIR" "$NUGET_LOCAL_FEED"
install -m 0644 "$LIBGIT2_PATH" "$PAYLOAD_DIR/libgit2-3f4182d.so"
install -m 0644 "$OPENSSL_LIB_DIR/libssl.so.3" "$PAYLOAD_DIR/libssl.so.3"
install -m 0644 "$OPENSSL_LIB_DIR/libssl.so.3" "$PAYLOAD_DIR/libssl.so"
install -m 0644 "$OPENSSL_LIB_DIR/libcrypto.so.3" "$PAYLOAD_DIR/libcrypto.so.3"
install -m 0644 "$OPENSSL_LIB_DIR/libcrypto.so.3" "$PAYLOAD_DIR/libcrypto.so"

for libssh2_path in "$LIBSSH2_LIB_DIR"/libssh2.so*; do
  if [ -f "$libssh2_path" ]; then
    install -m 0644 "$libssh2_path" "$PAYLOAD_DIR/$(basename "$libssh2_path")"
  fi
done

cat > "$PROJECT_DIR/LibGit2Sharp.NativeBinaries.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>LibGit2Sharp.NativeBinaries</PackageId>
    <PackageVersion>2.0.324-android.7.fdroid.2</PackageVersion>
    <Authors>LibGit2Sharp contributors; Unlimotion maintainers</Authors>
    <Description>Source-built Android arm64 native libraries for Unlimotion.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/Kibnet/Unlimotion</PackageProjectUrl>
    <RepositoryType>git</RepositoryType>
    <RepositoryUrl>https://github.com/libgit2/libgit2.git</RepositoryUrl>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <None Include="payload/**/*">
      <Pack>true</Pack>
      <PackagePath>%(RecursiveDir)%(Filename)%(Extension)</PackagePath>
    </None>
  </ItemGroup>
</Project>
EOF

rm -f "$OUTPUT_PACKAGE"
dotnet pack "$PROJECT_DIR/LibGit2Sharp.NativeBinaries.csproj" \
  --configuration Release \
  --output "$NUGET_LOCAL_FEED" \
  -p:PackageVersion="$PACKAGE_VERSION" \
  -p:RepositoryCommit="$EXPECTED_LIBGIT2_COMMIT"

if [ ! -f "$OUTPUT_PACKAGE" ]; then
  echo "Native package was not produced: $OUTPUT_PACKAGE" >&2
  exit 1
fi

for required_entry in \
  runtimes/android-arm64/native/libgit2-3f4182d.so \
  runtimes/android-arm64/native/libssl.so \
  runtimes/android-arm64/native/libssl.so.3 \
  runtimes/android-arm64/native/libcrypto.so \
  runtimes/android-arm64/native/libcrypto.so.3; do
  if ! unzip -l "$OUTPUT_PACKAGE" "$required_entry" >/dev/null 2>&1; then
    echo "Packed native package is missing $required_entry" >&2
    exit 1
  fi
done

if ! unzip -l "$OUTPUT_PACKAGE" 'runtimes/android-arm64/native/libssh2.so*' >/dev/null 2>&1; then
  echo "Packed native package is missing libssh2 for android-arm64" >&2
  exit 1
fi

if unzip -l "$OUTPUT_PACKAGE" 'runtimes/android-x64/*' | grep -q 'android-x64'; then
  echo "F-Droid native package must not contain android-x64 binaries" >&2
  exit 1
fi

echo "Wrote $OUTPUT_PACKAGE from source-built Android arm64 libraries"
