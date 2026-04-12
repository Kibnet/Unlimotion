#!/usr/bin/env bash
set -euo pipefail

REPO_ARCHIVE_URL="${REPO_ARCHIVE_URL:-https://github.com/Kibnet/Agents.md/archive/refs/heads/main.tar.gz}"
TARGET_DIR="${TARGET_DIR:-./agents}"

if ! command -v curl >/dev/null 2>&1; then
  echo "Error: curl is required but not installed" >&2
  exit 1
fi

if ! command -v tar >/dev/null 2>&1; then
  echo "Error: tar is required but not installed" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
ARCHIVE_PATH="$TMP_DIR/agents.tar.gz"
EXTRACT_DIR="$TMP_DIR/extracted"

cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

echo "Downloading archive from: $REPO_ARCHIVE_URL"
mkdir -p "$EXTRACT_DIR"
curl -fsSL "$REPO_ARCHIVE_URL" -o "$ARCHIVE_PATH"

echo "Extracting archive..."
tar -xzf "$ARCHIVE_PATH" -C "$EXTRACT_DIR"

ROOT_DIR="$(find "$EXTRACT_DIR" -mindepth 1 -maxdepth 1 -type d | head -n 1)"

if [ -z "${ROOT_DIR:-}" ] || [ ! -d "$ROOT_DIR" ]; then
  echo "Error: failed to detect extracted archive root directory" >&2
  exit 1
fi

echo "Refreshing target directory: $TARGET_DIR"
rm -rf "$TARGET_DIR"
mkdir -p "$TARGET_DIR"

if command -v rsync >/dev/null 2>&1; then
  rsync -a --delete "$ROOT_DIR"/ "$TARGET_DIR"/
else
  cp -R "$ROOT_DIR"/. "$TARGET_DIR"/
fi

echo "Agents instructions were extracted to: $TARGET_DIR"
