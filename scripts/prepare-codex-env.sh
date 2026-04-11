#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   source scripts/prepare-codex-env.sh
#
# This script normalizes PATH for Codex sessions so `dotnet` and global tools
# are available immediately.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

if [[ -x "/root/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="/root/.dotnet"
  export PATH="${DOTNET_ROOT}:${PATH}"
fi

if [[ -d "${HOME}/.dotnet/tools" ]]; then
  export PATH="${HOME}/.dotnet/tools:${PATH}"
fi

if command -v dotnet >/dev/null 2>&1; then
  echo "[prepare-codex-env] dotnet: $(command -v dotnet)"
  dotnet --version
else
  echo "[prepare-codex-env] dotnet not found in PATH after setup." >&2
  echo "[prepare-codex-env] Expected location: /root/.dotnet/dotnet" >&2
  exit 1
fi

git config --global --add safe.directory "${REPO_ROOT}" || true

# Best-effort tuning for large test runs that create many file watchers.
if command -v sysctl >/dev/null 2>&1; then
  CURRENT_INOTIFY_LIMIT="$(sysctl -n fs.inotify.max_user_instances 2>/dev/null || echo "")"
  TARGET_INOTIFY_LIMIT="1024"
  if [[ -n "${CURRENT_INOTIFY_LIMIT}" ]] && [[ "${CURRENT_INOTIFY_LIMIT}" -lt "${TARGET_INOTIFY_LIMIT}" ]]; then
    if sysctl -w "fs.inotify.max_user_instances=${TARGET_INOTIFY_LIMIT}" >/dev/null 2>&1; then
      echo "[prepare-codex-env] inotify max_user_instances raised to ${TARGET_INOTIFY_LIMIT}"
    else
      echo "[prepare-codex-env] warning: unable to raise fs.inotify.max_user_instances (insufficient privileges?)"
    fi
  fi
fi

echo "[prepare-codex-env] Environment ready for ${REPO_ROOT}"
