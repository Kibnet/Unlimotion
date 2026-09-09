#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
cd -- "$SCRIPT_DIR"

exec dotnet run \
  --project "$SCRIPT_DIR/src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj" \
  -- "$@"
