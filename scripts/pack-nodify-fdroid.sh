#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="${NODIFY_SOURCE_DIR:-$ROOT_DIR/.native/nodify-avalonia-src}"
EXPECTED_COMMIT="${NODIFY_SOURCE_COMMIT:-a8c9a96c80bc5e666aa34c9d3ce5947376e37722}"
PACKAGE_VERSION="${NODIFY_PACKAGE_VERSION:-6.6.0-unlimotion.a12.1.fdroid.1}"
NUGET_LOCAL_FEED="${NUGET_LOCAL_FEED:-$ROOT_DIR/artifacts/nuget-local}"
PROJECT_PATH="$SRC_DIR/Nodify/Nodify.csproj"
OUTPUT_PACKAGE="$NUGET_LOCAL_FEED/NodifyAvalonia.$PACKAGE_VERSION.nupkg"

if [ ! -f "$PROJECT_PATH" ] || ! git -C "$SRC_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Missing Nodify source submodule at $SRC_DIR. Run: git submodule update --init --recursive" >&2
  exit 1
fi

actual_commit="$(git -C "$SRC_DIR" rev-parse HEAD)"
if [ "$actual_commit" != "$EXPECTED_COMMIT" ]; then
  echo "Unexpected Nodify source commit: $actual_commit (expected $EXPECTED_COMMIT)" >&2
  exit 1
fi

mkdir -p "$NUGET_LOCAL_FEED"
rm -f "$OUTPUT_PACKAGE"

AVALONIA_TELEMETRY_OPTOUT=1 dotnet build "$PROJECT_PATH" \
  --configuration Release \
  -p:GeneratePackageOnBuild=false \
  -p:Version="$PACKAGE_VERSION" \
  -p:RepositoryUrl="https://github.com/Kibnet/nodify-avalonia.git" \
  -p:RepositoryCommit="$EXPECTED_COMMIT"

AVALONIA_TELEMETRY_OPTOUT=1 dotnet pack "$PROJECT_PATH" \
  --configuration Release \
  --no-build \
  --output "$NUGET_LOCAL_FEED" \
  -p:GeneratePackageOnBuild=false \
  -p:Version="$PACKAGE_VERSION" \
  -p:PackageVersion="$PACKAGE_VERSION" \
  -p:RepositoryUrl="https://github.com/Kibnet/nodify-avalonia.git" \
  -p:RepositoryCommit="$EXPECTED_COMMIT"

if [ ! -f "$OUTPUT_PACKAGE" ]; then
  echo "Nodify package was not produced: $OUTPUT_PACKAGE" >&2
  exit 1
fi

echo "Wrote $OUTPUT_PACKAGE from Nodify source commit $EXPECTED_COMMIT"
