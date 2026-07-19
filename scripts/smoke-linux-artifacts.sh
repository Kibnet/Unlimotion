#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
MODE=""
VERSION=""
IDENTITY_PATH=""
MANIFEST_PATH="$ROOT_DIR/distribution/release-assets.json"
DEB_PATH=""
APPIMAGE_PATH=""
DEBIAN_IMAGE=""
HARNESS_IMAGE="debian:12-slim"
REPORT_PATH=""
CONTAINER_CLI="${CONTAINER_CLI:-docker}"
BASELINE_DEB=""
BASELINE_URL="https://github.com/Kibnet/Unlimotion/releases/download/1.27.0/Unlimotion-1.27.0.deb"
BASELINE_SHA256="a192642417ac375ce1230b5cd89f4a11d99de4dd2a638e259b545cfcc3995a13"

TEMP_DIR=""
MOUNT_DIR=""
TARGET_NAME=""
HARNESS_NAME=""
X11_VOLUME=""
APP_EXEC_CLIENT_PID=""
TARGET_APP_PID=""
REPORT_WRITTEN=false
CURRENT_STEP="argument-validation"
DETAIL=""
OS_NAME=""
OS_VERSION=""
ARCHITECTURE=""
IMAGE_IDENTITY=""
HARNESS_IDENTITY=""
HARNESS_TOOLS=""
PRE_CLOSURE_SHA=""
POST_CLOSURE_SHA=""
ELF_CLOSURE_STATUS="notRun"
WINDOW_VERIFIED=false
LAUNCH_MODE="notRun"
APT_ATTEMPT=0
DEB_SHA256=""
APPIMAGE_SHA256=""
DEB_EXECUTABLE_SHA256=""
APPIMAGE_EXECUTABLE_SHA256=""
BASELINE_ACTUAL_SHA256=""
RAW_TAG=""
SOURCE_SHA=""
WORKFLOW_SHA=""
TAG_BINDING=""
MANIFEST_SHA256=""
SUPPORT_MATRIX_SHA256=""
RUNTIME_PACKAGES_USED=""
WINDOW_TITLE=""
APPLICATION_LOG_FILE=""
APPLICATION_LOG_SHA256=""
ELF_CLOSURE_LOG_FILE=""
ELF_CLOSURE_LOG_SHA256=""
RETRY_RULE="aptNetwork"
RETRY_CLASSIFICATION="infrastructure-only"
RETRY_CLEANUP="new-container"
RETRY_MAX_ATTEMPTS=3
RETRY_EXHAUSTED=false

usage() {
  cat <<'USAGE'
Usage: smoke-linux-artifacts.sh --mode <mode> --version <version> [options]

Modes:
  metadata                  Validate .deb/AppImage structure, policy and byte parity.
  clean                     Install and launch .deb in one Debian target cell.
  upgrade                   Upgrade exact migration-only 1.27.0 .deb to candidate.
  appimage                  Extract-and-run AppImage in one Debian target cell.
  missing-runtime-negative  Remove libx11-6 from candidate metadata and prove fail-closed launch.

Required options by mode:
  metadata:                  --deb, --appimage
  clean:                     --deb, --image
  upgrade:                   --deb, --image
  appimage:                  --deb, --appimage, --image
  missing-runtime-negative:  --deb, --image

Common options:
  --identity <path>          Release identity JSON (required).
  --manifest <path>          Release asset manifest (default: distribution/release-assets.json).
  --report <path>            JSON evidence output (required).
  --harness-image <image>    External Xvfb/xdotool sidecar base (default: debian:12-slim).
  --container-cli <command>  Container CLI (default: CONTAINER_CLI or docker).
  --baseline-deb <path>      Local exact 1.27.0 baseline for upgrade mode.
  --baseline-url <url>       Baseline URL used when --baseline-deb is omitted.
  -h, --help                 Show this help.
USAGE
}

fail() {
  DETAIL="$*"
  echo "smoke-linux-artifacts: $*" >&2
  return 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

hash_file() {
  sha256sum "$1" | awk '{print tolower($1)}'
}

json_string() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  value=${value//$'\r'/\\r}
  value=${value//$'\t'/\\t}
  printf '"%s"' "$value"
}

write_report() {
  local status="$1"
  local report_dir
  report_dir="$(dirname -- "$REPORT_PATH")"
  mkdir -p -- "$report_dir"

  {
    printf '{\n'
    printf '  "schemaVersion": 1,\n'
    printf '  "kind": "linux-native-evidence",\n'
    printf '  "status": %s,\n' "$(json_string "$status")"
    printf '  "mode": %s,\n' "$(json_string "$MODE")"
    printf '  "rawTag": %s,\n' "$(json_string "$RAW_TAG")"
    printf '  "normalizedVersion": %s,\n' "$(json_string "$VERSION")"
    printf '  "sourceSha": %s,\n' "$(json_string "$SOURCE_SHA")"
    printf '  "workflowSha": %s,\n' "$(json_string "$WORKFLOW_SHA")"
    printf '  "tagBinding": %s,\n' "$(json_string "$TAG_BINDING")"
    printf '  "manifestSha256": %s,\n' "$(json_string "$MANIFEST_SHA256")"
    printf '  "supportMatrixSha256": %s,\n' "$(json_string "$SUPPORT_MATRIX_SHA256")"
    printf '  "runtimePackages": %s,\n' "$(json_string "$RUNTIME_PACKAGES_USED")"
    printf '  "step": %s,\n' "$(json_string "$CURRENT_STEP")"
    printf '  "detail": %s,\n' "$(json_string "$DETAIL")"
    printf '  "osName": %s,\n' "$(json_string "$OS_NAME")"
    printf '  "osVersion": %s,\n' "$(json_string "$OS_VERSION")"
    printf '  "architecture": %s,\n' "$(json_string "$ARCHITECTURE")"
    printf '  "targetImage": %s,\n' "$(json_string "$DEBIAN_IMAGE")"
    printf '  "targetImageIdentity": %s,\n' "$(json_string "$IMAGE_IDENTITY")"
    printf '  "externalHarnessImage": %s,\n' "$(json_string "$HARNESS_IMAGE")"
    printf '  "externalHarnessIdentity": %s,\n' "$(json_string "$HARNESS_IDENTITY")"
    printf '  "externalHarnessTools": %s,\n' "$(json_string "$HARNESS_TOOLS")"
    printf '  "debSha256": %s,\n' "$(json_string "$DEB_SHA256")"
    printf '  "appImageSha256": %s,\n' "$(json_string "$APPIMAGE_SHA256")"
    printf '  "debExecutableSha256": %s,\n' "$(json_string "$DEB_EXECUTABLE_SHA256")"
    printf '  "appImageExecutableSha256": %s,\n' "$(json_string "$APPIMAGE_EXECUTABLE_SHA256")"
    printf '  "baselineSha256": %s,\n' "$(json_string "$BASELINE_ACTUAL_SHA256")"
    printf '  "elfClosureStatus": %s,\n' "$(json_string "$ELF_CLOSURE_STATUS")"
    printf '  "installedPackageClosureBeforeLaunch": %s,\n' "$(json_string "$PRE_CLOSURE_SHA")"
    printf '  "installedPackageClosureAfterLaunch": %s,\n' "$(json_string "$POST_CLOSURE_SHA")"
    printf '  "guiHarnessLocation": "external-sidecar",\n'
    printf '  "launchMode": %s,\n' "$(json_string "$LAUNCH_MODE")"
    printf '  "windowTitle": %s,\n' "$(json_string "$WINDOW_TITLE")"
    printf '  "windowVerified": %s,\n' "$WINDOW_VERIFIED"
    printf '  "directFuse": "notVerified",\n'
    printf '  "applicationLogFile": %s,\n' "$(json_string "$APPLICATION_LOG_FILE")"
    printf '  "applicationLogSha256": %s,\n' "$(json_string "$APPLICATION_LOG_SHA256")"
    printf '  "elfClosureLogFile": %s,\n' "$(json_string "$ELF_CLOSURE_LOG_FILE")"
    printf '  "elfClosureLogSha256": %s,\n' "$(json_string "$ELF_CLOSURE_LOG_SHA256")"
    printf '  "retryRule": %s,\n' "$(json_string "$RETRY_RULE")"
    printf '  "retryClassification": %s,\n' "$(json_string "$RETRY_CLASSIFICATION")"
    printf '  "retryCleanup": %s,\n' "$(json_string "$RETRY_CLEANUP")"
    printf '  "attempt": %s,\n' "$APT_ATTEMPT"
    printf '  "maxAttempts": %s,\n' "$RETRY_MAX_ATTEMPTS"
    printf '  "retryExhausted": %s,\n' "$RETRY_EXHAUSTED"
    printf '  "productionReady": false\n'
    printf '}\n'
  } > "$REPORT_PATH"
  REPORT_WRITTEN=true
}

container_exists() {
  [[ -n "$1" ]] && "$CONTAINER_CLI" container inspect "$1" >/dev/null 2>&1
}

cleanup_container_state() {
  if container_exists "$HARNESS_NAME"; then
    "$CONTAINER_CLI" rm -f "$HARNESS_NAME" >/dev/null 2>&1 || true
  fi
  HARNESS_NAME=""
  if container_exists "$TARGET_NAME"; then
    "$CONTAINER_CLI" rm -f "$TARGET_NAME" >/dev/null 2>&1 || true
  fi
  TARGET_NAME=""
  if [[ -n "$X11_VOLUME" ]]; then
    "$CONTAINER_CLI" volume rm -f "$X11_VOLUME" >/dev/null 2>&1 || true
  fi
  X11_VOLUME=""
}

cleanup() {
  local exit_code=$?
  trap - EXIT ERR
  if [[ -n "$APP_EXEC_CLIENT_PID" ]] && kill -0 "$APP_EXEC_CLIENT_PID" >/dev/null 2>&1; then
    kill "$APP_EXEC_CLIENT_PID" >/dev/null 2>&1 || true
  fi
  cleanup_container_state
  if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" ]]; then
    rm -rf -- "$TEMP_DIR"
  fi
  exit "$exit_code"
}

on_error() {
  local exit_code=$?
  trap - ERR
  if [[ -z "$DETAIL" ]]; then
    DETAIL="command failed during $CURRENT_STEP"
  fi
  if [[ -n "$REPORT_PATH" && "$REPORT_WRITTEN" != true ]]; then
    write_report failure || true
  fi
  return "$exit_code"
}

trap cleanup EXIT
trap on_error ERR

while (($# > 0)); do
  case "$1" in
    --mode)
      (($# >= 2)) || { usage >&2; exit 2; }
      MODE="$2"
      shift 2
      ;;
    --version)
      (($# >= 2)) || { usage >&2; exit 2; }
      VERSION="$2"
      shift 2
      ;;
    --identity)
      (($# >= 2)) || { usage >&2; exit 2; }
      IDENTITY_PATH="$2"
      shift 2
      ;;
    --manifest)
      (($# >= 2)) || { usage >&2; exit 2; }
      MANIFEST_PATH="$2"
      shift 2
      ;;
    --deb)
      (($# >= 2)) || { usage >&2; exit 2; }
      DEB_PATH="$2"
      shift 2
      ;;
    --appimage)
      (($# >= 2)) || { usage >&2; exit 2; }
      APPIMAGE_PATH="$2"
      shift 2
      ;;
    --image)
      (($# >= 2)) || { usage >&2; exit 2; }
      DEBIAN_IMAGE="$2"
      shift 2
      ;;
    --harness-image)
      (($# >= 2)) || { usage >&2; exit 2; }
      HARNESS_IMAGE="$2"
      shift 2
      ;;
    --report)
      (($# >= 2)) || { usage >&2; exit 2; }
      REPORT_PATH="$2"
      shift 2
      ;;
    --container-cli)
      (($# >= 2)) || { usage >&2; exit 2; }
      CONTAINER_CLI="$2"
      shift 2
      ;;
    --baseline-deb)
      (($# >= 2)) || { usage >&2; exit 2; }
      BASELINE_DEB="$2"
      shift 2
      ;;
    --baseline-url)
      (($# >= 2)) || { usage >&2; exit 2; }
      BASELINE_URL="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

[[ "$MODE" =~ ^(metadata|clean|upgrade|appimage|missing-runtime-negative)$ ]] || fail "unsupported --mode: $MODE"
[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "$VERSION" != '0.0.0' ]] \
  || fail "--version must be normalized stable SemVer: $VERSION"
[[ -n "$REPORT_PATH" ]] || fail '--report is required'
[[ -f "$IDENTITY_PATH" ]] || fail '--identity must reference an existing release identity JSON file'
require_command jq
RAW_TAG="$(jq -er '.rawTag' "$IDENTITY_PATH")"
identity_version="$(jq -er '.normalizedVersion' "$IDENTITY_PATH")"
SOURCE_SHA="$(jq -er '.sourceSha' "$IDENTITY_PATH")"
WORKFLOW_SHA="$(jq -er '.workflowSha' "$IDENTITY_PATH")"
TAG_BINDING="$(jq -er '.tagBinding' "$IDENTITY_PATH")"
MANIFEST_SHA256="$(jq -er '.manifestSha256' "$IDENTITY_PATH")"
SUPPORT_MATRIX_SHA256="$(jq -er '.supportMatrixSha256' "$IDENTITY_PATH")"
[[ "$identity_version" == "$VERSION" ]] || fail '--version does not match identity.normalizedVersion'
[[ "$RAW_TAG" =~ ^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "${RAW_TAG#v}" == "$VERSION" ]] \
  || fail 'identity rawTag does not normalize to --version'
[[ "$SOURCE_SHA" =~ ^[0-9a-f]{40}$ && "$WORKFLOW_SHA" =~ ^[0-9a-f]{40}$ ]] \
  || fail 'identity sourceSha or workflowSha is invalid'
[[ "$TAG_BINDING" == 'notApplicable' || "$TAG_BINDING" == 'required' ]] || fail 'identity tagBinding is invalid'
[[ "$MANIFEST_SHA256" =~ ^[0-9a-f]{64}$ && "$SUPPORT_MATRIX_SHA256" =~ ^[0-9a-f]{64}$ ]] \
  || fail 'identity manifest/support-matrix SHA-256 is invalid'
[[ -f "$MANIFEST_PATH" ]] || fail '--manifest must reference an existing release asset manifest'
[[ "$(hash_file "$MANIFEST_PATH")" == "$MANIFEST_SHA256" ]] || fail 'release asset manifest bytes do not match identity.manifestSha256'

case "$MODE" in
  metadata)
    [[ -f "$DEB_PATH" && -f "$APPIMAGE_PATH" ]] || fail 'metadata mode requires existing --deb and --appimage files'
    ;;
  clean|upgrade|missing-runtime-negative)
    [[ -f "$DEB_PATH" ]] || fail "$MODE mode requires an existing --deb file"
    [[ -n "$DEBIAN_IMAGE" ]] || fail "$MODE mode requires --image"
    ;;
  appimage)
    [[ -f "$DEB_PATH" && -f "$APPIMAGE_PATH" ]] || fail 'appimage mode requires existing --deb and --appimage files for parity'
    [[ -n "$DEBIAN_IMAGE" ]] || fail 'appimage mode requires --image'
    ;;
esac

for required in sha256sum awk sed grep find stat file readelf dpkg-deb desktop-file-validate lintian; do
  require_command "$required"
done
if [[ "$MODE" != metadata ]]; then
  require_command "$CONTAINER_CLI"
fi
if [[ "$MODE" == upgrade && -z "$BASELINE_DEB" ]]; then
  require_command curl
fi

TEMP_DIR="$(mktemp -d)"
MOUNT_DIR="$TEMP_DIR/candidates"
mkdir -p -- "$MOUNT_DIR"

validate_deb_metadata() {
  CURRENT_STEP="deb-metadata"
  local extract_dir="$TEMP_DIR/deb-metadata"
  local control_dir="$TEMP_DIR/deb-control"
  local control_file="$control_dir/control"
  local lintian_report="${REPORT_PATH%.json}-lintian.txt"
  local depends
  rm -rf -- "$extract_dir" "$control_dir"
  mkdir -p -- "$extract_dir" "$control_dir"

  DEB_SHA256="$(hash_file "$DEB_PATH")"
  dpkg-deb -x "$DEB_PATH" "$extract_dir"
  dpkg-deb -e "$DEB_PATH" "$control_dir"
  [[ -f "$control_file" ]] || fail 'Debian package control file is missing'

  grep -Fxq 'Package: unlimotion.desktop' "$control_file" || fail 'raw Debian Package field must be lowercase unlimotion.desktop'
  grep -Fxq "Version: $VERSION" "$control_file" || fail 'Debian package version does not match normalized version'
  grep -Fxq 'Architecture: amd64' "$control_file" || fail 'Debian package architecture must be amd64'
  grep -Fxq 'Priority: optional' "$control_file" || fail 'Debian package priority must be optional'
  grep -Fxq 'Section: utils' "$control_file" || fail 'Debian package section must be utils'
  grep -Fxq 'Maintainer: Kibnet Philosoff <kibnet@hotmail.com>' "$control_file" || fail 'Debian maintainer metadata is incorrect'
  grep -Fxq 'Homepage: https://github.com/Kibnet/Unlimotion' "$control_file" || fail 'Debian homepage metadata is incorrect'

  depends="$(dpkg-deb -f "$DEB_PATH" Depends)"
  for dependency in \
    'ca-certificates' 'libc6' 'libgcc-s1' 'libgssapi-krb5-2' \
    'libicu76 | libicu72' 'libssl3t64 | libssl3' 'libstdc++6' 'tzdata' 'zlib1g' \
    'libx11-6' 'libice6' 'libsm6' 'libfontconfig1'; do
    grep -Fq "$dependency" <<< "$depends" || fail "Debian Depends is missing: $dependency"
  done

  if dpkg-deb -c "$DEB_PATH" | grep -qE '[[:space:]]\./usr/local(/|$)'; then
    fail 'Debian package must not own any path under /usr/local'
  fi
  [[ -x "$extract_dir/usr/lib/unlimotion/Unlimotion.Desktop" ]] || fail 'Debian main executable is missing or not mode 0755'
  [[ "$(stat -c '%a' "$extract_dir/usr/bin/Unlimotion")" == '755' ]] || fail 'Debian launcher mode must be 0755'
  [[ "$(stat -c '%a' "$extract_dir/usr/share/applications/unlimotion.desktop")" == '644' ]] || fail 'desktop entry mode must be 0644'
  [[ "$(stat -c '%a' "$extract_dir/usr/share/icons/hicolor/512x512/apps/unlimotion.png")" == '644' ]] || fail 'PNG icon mode must be 0644'
  file "$extract_dir/usr/lib/unlimotion/Unlimotion.Desktop" | grep -Eq 'ELF 64-bit.*x86-64' || fail 'Debian executable must be ELF x86-64'
  file "$extract_dir/usr/share/icons/hicolor/512x512/apps/unlimotion.png" | grep -Eq 'PNG image data, 512 x 512' || fail 'Debian icon must be the canonical 512x512 PNG'
  desktop-file-validate "$extract_dir/usr/share/applications/unlimotion.desktop"

  if ! lintian --fail-on error "$DEB_PATH" > "$lintian_report" 2>&1; then
    cat "$lintian_report" >&2
    fail 'lintian reported a policy error'
  fi

  DEB_EXECUTABLE_SHA256="$(hash_file "$extract_dir/usr/lib/unlimotion/Unlimotion.Desktop")"
}

validate_appimage_metadata() {
  CURRENT_STEP="appimage-metadata"
  local extract_dir="$TEMP_DIR/appimage-metadata"
  local appdir="$extract_dir/squashfs-root"
  local before_sha
  local after_sha
  local sq_version
  local desktop_file
  local -a executables

  rm -rf -- "$extract_dir"
  mkdir -p -- "$extract_dir"
  [[ -x "$APPIMAGE_PATH" ]] || fail 'AppImage must have executable mode'
  before_sha="$(hash_file "$APPIMAGE_PATH")"
  APPIMAGE_SHA256="$before_sha"
  file "$APPIMAGE_PATH" | grep -Eq 'ELF 64-bit.*x86-64' || fail 'AppImage must be ELF x86-64'
  (
    cd -- "$extract_dir"
    "$APPIMAGE_PATH" --appimage-extract >/dev/null
  )
  after_sha="$(hash_file "$APPIMAGE_PATH")"
  [[ "$after_sha" == "$before_sha" ]] || fail 'AppImage bytes changed during extraction'
  [[ -x "$appdir/AppRun" ]] || fail 'AppImage AppRun is missing or not executable'
  if find "$appdir" -path '*/ci/deb/*' -print -quit | grep -q .; then
    fail 'AppImage contains Debian-only ci/deb payload'
  fi

  mapfile -t executables < <(find "$appdir" -type f -name 'Unlimotion.Desktop' -print)
  ((${#executables[@]} == 1)) || fail "AppImage must contain one Unlimotion.Desktop; found ${#executables[@]}"
  APPIMAGE_EXECUTABLE_SHA256="$(hash_file "${executables[0]}")"
  [[ -z "$DEB_EXECUTABLE_SHA256" || "$APPIMAGE_EXECUTABLE_SHA256" == "$DEB_EXECUTABLE_SHA256" ]] \
    || fail 'AppImage and Debian inner executable bytes differ'

  sq_version="$(find "$appdir" -type f -name 'sq.version' -print -quit)"
  [[ -n "$sq_version" ]] || fail 'AppImage Velopack metadata is missing'
  grep -Fq "<version>$VERSION</version>" "$sq_version" || fail 'AppImage version metadata mismatch'
  grep -Fq '<rid>linux-x64</rid>' "$sq_version" || fail 'AppImage RID metadata mismatch'
  grep -Fq '<machineArchitecture>x64</machineArchitecture>' "$sq_version" || fail 'AppImage architecture metadata mismatch'

  desktop_file="$(find "$appdir" -maxdepth 1 -type f -name '*.desktop' -print -quit)"
  [[ -n "$desktop_file" ]] || fail 'AppImage desktop entry is missing'
  desktop-file-validate "$desktop_file"
}

resolve_image_identity() {
  local image="$1"
  if ! "$CONTAINER_CLI" image inspect "$image" >/dev/null 2>&1; then
    "$CONTAINER_CLI" pull "$image" >/dev/null
  fi
  "$CONTAINER_CLI" image inspect --format '{{if .RepoDigests}}{{index .RepoDigests 0}}{{else}}{{.Id}}{{end}}' "$image"
}

reset_target() {
  cleanup_container_state
  local suffix="${RANDOM:-0}-$$-$APT_ATTEMPT"
  TARGET_NAME="unlimotion-target-$suffix"
  X11_VOLUME="unlimotion-x11-$suffix"
  "$CONTAINER_CLI" volume create "$X11_VOLUME" >/dev/null
  "$CONTAINER_CLI" run -d \
    --name "$TARGET_NAME" \
    --hostname unlimotion-target \
    -v "$X11_VOLUME:/tmp/.X11-unix" \
    -v "$MOUNT_DIR:/candidates:ro" \
    "$DEBIAN_IMAGE" \
    sh -c 'while :; do sleep 3600; done' >/dev/null

  OS_NAME="$("$CONTAINER_CLI" exec "$TARGET_NAME" sh -c '. /etc/os-release; printf "%s" "$ID"')"
  OS_VERSION="$("$CONTAINER_CLI" exec "$TARGET_NAME" sh -c '. /etc/os-release; printf "%s" "$VERSION_ID"')"
  ARCHITECTURE="$("$CONTAINER_CLI" exec "$TARGET_NAME" dpkg --print-architecture)"
  [[ "$OS_NAME" == debian ]] || fail "target image is not Debian: $OS_NAME"
  [[ "$OS_VERSION" == 12 || "$OS_VERSION" == 13 ]] || fail "unsupported Debian target version: $OS_VERSION"
  [[ "$ARCHITECTURE" == amd64 ]] || fail "target dpkg architecture must be amd64: $ARCHITECTURE"
  [[ "$("$CONTAINER_CLI" exec "$TARGET_NAME" uname -m)" == x86_64 ]] || fail 'target kernel architecture must be x86_64'
}

apt_failure_is_network() {
  grep -Eiq \
    'Temporary failure resolving|Could not connect|Connection failed|Failed to fetch|Unable to fetch some archives|Connection timed out|Network is unreachable|TLS connection was non-properly terminated' \
    "$1"
}

run_target_apt() {
  local command_text="$1"
  local log_file="$TEMP_DIR/apt-$APT_ATTEMPT.log"
  if "$CONTAINER_CLI" exec -e DEBIAN_FRONTEND=noninteractive "$TARGET_NAME" sh -ec "$command_text" > "$log_file" 2>&1; then
    return 0
  fi
  cat "$log_file" >&2
  if apt_failure_is_network "$log_file"; then
    return 75
  fi
  return 1
}

runtime_packages() {
  case "$OS_VERSION" in
    12|13) ;;
    *) return 1 ;;
  esac
  jq -er --arg key "debian$OS_VERSION" '.linuxRuntimePrerequisites.appImageExtractAndRun[$key] | join(" ")' "$MANIFEST_PATH"
}

run_elf_closure() {
  CURRENT_STEP="elf-loader-closure"
  local closure_file="$TEMP_DIR/elf-closure.txt"
  local result=0
  if ! "$CONTAINER_CLI" exec "$TARGET_NAME" sh -ec '
    : > /tmp/unlimotion-elf-closure.txt
    find /usr/lib/unlimotion -type f | while IFS= read -r candidate; do
      magic="$(od -An -t x1 -N4 "$candidate" 2>/dev/null | tr -d " \n")"
      if [ "$magic" = 7f454c46 ]; then
        printf "%s\n" "--- $candidate ---" >> /tmp/unlimotion-elf-closure.txt
        output="$(ldd "$candidate" 2>&1 || true)"
        printf "%s\n" "$output" >> /tmp/unlimotion-elf-closure.txt
        if printf "%s\n" "$output" | grep -q "not found"; then
          exit 1
        fi
      fi
    done
  '; then
    result=1
  fi
  "$CONTAINER_CLI" cp "$TARGET_NAME:/tmp/unlimotion-elf-closure.txt" "$closure_file" >/dev/null 2>&1 || true
  ELF_CLOSURE_LOG_FILE="$(basename -- "${REPORT_PATH%.json}-elf-closure.txt")"
  cp -- "$closure_file" "$(dirname -- "$REPORT_PATH")/$ELF_CLOSURE_LOG_FILE"
  ELF_CLOSURE_LOG_SHA256="$(hash_file "$(dirname -- "$REPORT_PATH")/$ELF_CLOSURE_LOG_FILE")"
  if [[ "$result" -ne 0 ]]; then
    ELF_CLOSURE_STATUS="fail"
    return "$result"
  fi
  ELF_CLOSURE_STATUS="pass"
}

package_closure_hash() {
  "$CONTAINER_CLI" exec "$TARGET_NAME" sh -c 'dpkg-query -W | LC_ALL=C sort | sha256sum | cut -d " " -f 1'
}

assert_target_package_layout() {
  CURRENT_STEP="installed-package-layout"
  "$CONTAINER_CLI" exec "$TARGET_NAME" sh -ec '
    test "$(dpkg-query -W unlimotion.desktop | awk "{print \$1}")" = unlimotion.desktop
    test -x /usr/lib/unlimotion/Unlimotion.Desktop
    test -x /usr/bin/Unlimotion
    test -f /usr/share/applications/unlimotion.desktop
    test -f /usr/share/icons/hicolor/512x512/apps/unlimotion.png
    ! dpkg-query -L unlimotion.desktop | grep -q "^/usr/local\(/\|$\)"
    test ! -e /usr/local/bin/Unlimotion.Desktop
    test ! -e /usr/share/Unlimotion.Desktop
  '
  "$CONTAINER_CLI" exec "$TARGET_NAME" apt-get check >/dev/null
  local audit
  audit="$("$CONTAINER_CLI" exec "$TARGET_NAME" dpkg --audit)"
  [[ -z "$audit" ]] || fail "dpkg --audit reported: $audit"
}

install_deb_clean_with_retry() {
  local result
  CURRENT_STEP="debian-clean-install"
  for APT_ATTEMPT in 1 2 3; do
    reset_target
    if run_target_apt 'apt-get update && apt-get install -y --no-install-recommends /candidates/candidate.deb'; then
      return 0
    else
      result=$?
    fi
    if [[ "$result" -eq 75 && "$APT_ATTEMPT" -lt 3 ]]; then
      echo "APT infrastructure failure; recreating target container (attempt $((APT_ATTEMPT + 1))/3)." >&2
      continue
    fi
    [[ "$result" -ne 75 ]] || DETAIL='APT infrastructure retry exhausted'
    return "$result"
  done
}

install_appimage_prerequisites_with_retry() {
  local result
  local packages
  CURRENT_STEP="appimage-runtime-install"
  for APT_ATTEMPT in 1 2 3; do
    reset_target
    packages="$(runtime_packages)"
    RUNTIME_PACKAGES_USED="$packages"
    if run_target_apt "apt-get update && apt-get install -y --no-install-recommends $packages"; then
      return 0
    else
      result=$?
    fi
    if [[ "$result" -eq 75 && "$APT_ATTEMPT" -lt 3 ]]; then
      echo "APT infrastructure failure; recreating target container (attempt $((APT_ATTEMPT + 1))/3)." >&2
      continue
    fi
    [[ "$result" -ne 75 ]] || DETAIL='APT infrastructure retry exhausted'
    return "$result"
  done
}

install_upgrade_with_retry() {
  local result
  local packages
  CURRENT_STEP="debian-upgrade"
  for APT_ATTEMPT in 1 2 3; do
    reset_target
    packages="$(runtime_packages)"
    RUNTIME_PACKAGES_USED="$packages"
    if run_target_apt "apt-get update && apt-get install -y --no-install-recommends $packages"; then
      :
    else
      result=$?
      if [[ "$result" -eq 75 && "$APT_ATTEMPT" -lt 3 ]]; then
        continue
      fi
      return "$result"
    fi

    "$CONTAINER_CLI" exec "$TARGET_NAME" dpkg --force-depends -i /candidates/baseline.deb > "$TEMP_DIR/baseline-dpkg-$APT_ATTEMPT.log" 2>&1 \
      || fail 'migration-only baseline could not be installed with dpkg --force-depends'
    "$CONTAINER_CLI" exec "$TARGET_NAME" sh -ec '
      test "$(dpkg-query -W unlimotion.desktop | awk "{print \$1}")" = unlimotion.desktop
      useradd --create-home --uid 10001 --shell /bin/sh unlimotion-test
      install -d -o 10001 -g 10001 /home/unlimotion-test/unlimotion-data
      printf "stage3-upgrade-sentinel\n" > /home/unlimotion-test/unlimotion-data/sentinel.txt
      chown 10001:10001 /home/unlimotion-test/unlimotion-data/sentinel.txt
    '

    if run_target_apt 'apt-get install -y --no-install-recommends /candidates/candidate.deb'; then
      "$CONTAINER_CLI" exec "$TARGET_NAME" sh -ec "
        test \"\$(dpkg-query -W -f='\${Version}' unlimotion.desktop)\" = '$VERSION'
        test \"\$(cat /home/unlimotion-test/unlimotion-data/sentinel.txt)\" = stage3-upgrade-sentinel
      "
      return 0
    else
      result=$?
    fi
    if [[ "$result" -eq 75 && "$APT_ATTEMPT" -lt 3 ]]; then
      echo "APT infrastructure failure; recreating target container (attempt $((APT_ATTEMPT + 1))/3)." >&2
      continue
    fi
    [[ "$result" -ne 75 ]] || DETAIL='APT infrastructure retry exhausted'
    return "$result"
  done
}

start_external_harness() {
  CURRENT_STEP="external-x11-harness"
  local attempt
  local suffix="${RANDOM:-0}-$$"
  local harness_log="$TEMP_DIR/harness.log"
  for attempt in 1 2 3; do
    HARNESS_NAME="unlimotion-harness-$suffix-$attempt"
    "$CONTAINER_CLI" run -d \
      --name "$HARNESS_NAME" \
      -v "$X11_VOLUME:/tmp/.X11-unix" \
      "$HARNESS_IMAGE" \
      sh -ec '
        apt-get update
        DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends xvfb xdotool xauth
        rm -rf /var/lib/apt/lists/*
        Xvfb :99 -screen 0 1280x800x24 -ac -nolisten tcp >/tmp/xvfb.log 2>&1 &
        count=0
        while [ ! -S /tmp/.X11-unix/X99 ] && [ "$count" -lt 30 ]; do
          count=$((count + 1))
          sleep 1
        done
        test -S /tmp/.X11-unix/X99
        while :; do sleep 3600; done
      ' >/dev/null

    local ready=false
    local count
    for count in $(seq 1 90); do
      if "$CONTAINER_CLI" exec "$HARNESS_NAME" test -S /tmp/.X11-unix/X99 >/dev/null 2>&1; then
        ready=true
        break
      fi
      if ! container_exists "$HARNESS_NAME" || [[ "$("$CONTAINER_CLI" inspect -f '{{.State.Running}}' "$HARNESS_NAME" 2>/dev/null || true)" != true ]]; then
        break
      fi
      sleep 1
    done
    if [[ "$ready" == true ]]; then
      HARNESS_TOOLS="$("$CONTAINER_CLI" exec "$HARNESS_NAME" sh -c "dpkg-query -W -f='\${Package}=\${Version}\\n' xvfb xdotool | LC_ALL=C sort" 2>/dev/null | tr '\n' ' ')"
      return 0
    fi

    "$CONTAINER_CLI" logs "$HARNESS_NAME" > "$harness_log" 2>&1 || true
    "$CONTAINER_CLI" rm -f "$HARNESS_NAME" >/dev/null 2>&1 || true
    HARNESS_NAME=""
    if apt_failure_is_network "$harness_log" && [[ "$attempt" -lt 3 ]]; then
      echo "Harness APT infrastructure failure; recreating sidecar (attempt $((attempt + 1))/3)." >&2
      continue
    fi
    cat "$harness_log" >&2
    fail 'external X11 harness did not become ready'
  done
}

ensure_test_user() {
  "$CONTAINER_CLI" exec "$TARGET_NAME" sh -ec '
    if ! id unlimotion-test >/dev/null 2>&1; then
      useradd --create-home --uid 10001 --shell /bin/sh unlimotion-test
    fi
    install -d -o 10001 -g 10001 /home/unlimotion-test/unlimotion-data
  '
}

discover_target_app_pid() {
  "$CONTAINER_CLI" exec "$TARGET_NAME" sh -c '
    for process in /proc/[0-9]*; do
      [ -r "$process/cmdline" ] || continue
      [ "$(stat -c %u "$process" 2>/dev/null || true)" = 10001 ] || continue
      command_line="$(tr "\000" " " < "$process/cmdline" 2>/dev/null || true)"
      case "$command_line" in
        *Unlimotion.Desktop*|*candidate.AppImage*) basename "$process"; exit 0 ;;
      esac
    done
    exit 1
  ' 2>/dev/null
}

stop_tracked_application() {
  if [[ -n "$TARGET_APP_PID" ]]; then
    "$CONTAINER_CLI" exec "$TARGET_NAME" kill -TERM "$TARGET_APP_PID" >/dev/null 2>&1 || true
    local count
    for count in $(seq 1 10); do
      if ! "$CONTAINER_CLI" exec "$TARGET_NAME" kill -0 "$TARGET_APP_PID" >/dev/null 2>&1; then
        break
      fi
      sleep 1
    done
    "$CONTAINER_CLI" exec "$TARGET_NAME" kill -KILL "$TARGET_APP_PID" >/dev/null 2>&1 || true
  fi
  if [[ -n "$APP_EXEC_CLIENT_PID" ]]; then
    wait "$APP_EXEC_CLIENT_PID" >/dev/null 2>&1 || true
    APP_EXEC_CLIENT_PID=""
  fi
}

launch_and_verify_window() {
  local artifact_kind="$1"
  local app_log="$TEMP_DIR/application-$artifact_kind.log"
  local config_path='/home/unlimotion-test/unlimotion-data/config.json'
  local count
  local window_id=""
  local executable
  local -a environment_args

  CURRENT_STEP="$artifact_kind-launch"
  ensure_test_user
  start_external_harness
  PRE_CLOSURE_SHA="$(package_closure_hash)"

  environment_args=(--user 10001:10001 -e HOME=/home/unlimotion-test -e DISPLAY=:99)
  if [[ "$artifact_kind" == appimage ]]; then
    environment_args+=(-e APPIMAGE_EXTRACT_AND_RUN=1)
    executable='/candidates/candidate.AppImage'
    LAUNCH_MODE='appimage-extract-and-run'
  else
    executable='/usr/bin/Unlimotion'
    LAUNCH_MODE='debian-package-external-x11'
  fi

  "$CONTAINER_CLI" exec "${environment_args[@]}" "$TARGET_NAME" \
    "$executable" "--config=$config_path" > "$app_log" 2>&1 &
  APP_EXEC_CLIENT_PID=$!

  for count in $(seq 1 30); do
    TARGET_APP_PID="$(discover_target_app_pid || true)"
    window_id="$("$CONTAINER_CLI" exec -e DISPLAY=:99 "$HARNESS_NAME" xdotool search --name Unlimotion 2>/dev/null | head -n 1 || true)"
    if [[ -n "$TARGET_APP_PID" && -n "$window_id" ]]; then
      WINDOW_TITLE="$("$CONTAINER_CLI" exec -e DISPLAY=:99 "$HARNESS_NAME" xdotool getwindowname "$window_id" 2>/dev/null || true)"
      [[ "$WINDOW_TITLE" == "Unlimotion $VERSION" ]] || {
        window_id=""
        WINDOW_TITLE=""
        sleep 1
        continue
      }
      WINDOW_VERIFIED=true
      break
    fi
    if ! kill -0 "$APP_EXEC_CLIENT_PID" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done

  [[ "$WINDOW_VERIFIED" == true ]] || {
    cat "$app_log" >&2 || true
    fail "Unlimotion window was not observed within 30 seconds for $artifact_kind"
  }
  [[ -n "$TARGET_APP_PID" ]] || fail 'launched application PID was not tracked'
  "$CONTAINER_CLI" exec "$TARGET_NAME" kill -0 "$TARGET_APP_PID"

  stop_tracked_application
  if grep -Eiq 'Unhandled exception|DllNotFoundException|EntryPointNotFoundException|symbol lookup error|Segmentation fault|No usable version of libssl' "$app_log"; then
    cat "$app_log" >&2
    fail 'application log contains a fatal native/runtime error'
  fi
  APPLICATION_LOG_FILE="$(basename -- "${REPORT_PATH%.json}-application.log")"
  cp -- "$app_log" "$(dirname -- "$REPORT_PATH")/$APPLICATION_LOG_FILE"
  APPLICATION_LOG_SHA256="$(hash_file "$(dirname -- "$REPORT_PATH")/$APPLICATION_LOG_FILE")"

  POST_CLOSURE_SHA="$(package_closure_hash)"
  [[ "$POST_CLOSURE_SHA" == "$PRE_CLOSURE_SHA" ]] || fail 'target installed-package closure changed after external-harness launch'
}

prepare_candidate_mount() {
  if [[ -n "$DEB_PATH" ]]; then
    cp -- "$DEB_PATH" "$MOUNT_DIR/candidate.deb"
  fi
  if [[ -n "$APPIMAGE_PATH" ]]; then
    cp -- "$APPIMAGE_PATH" "$MOUNT_DIR/candidate.AppImage"
    chmod 0755 "$MOUNT_DIR/candidate.AppImage"
  fi
}

prepare_baseline() {
  CURRENT_STEP="baseline-acquisition"
  local baseline_target="$MOUNT_DIR/baseline.deb"
  if [[ -n "$BASELINE_DEB" ]]; then
    [[ -f "$BASELINE_DEB" ]] || fail "baseline .deb not found: $BASELINE_DEB"
    cp -- "$BASELINE_DEB" "$baseline_target"
  else
    curl --fail --location --retry 2 --retry-all-errors --output "$baseline_target" "$BASELINE_URL"
  fi
  BASELINE_ACTUAL_SHA256="$(hash_file "$baseline_target")"
  [[ "$BASELINE_ACTUAL_SHA256" == "$BASELINE_SHA256" ]] \
    || fail "baseline SHA-256 mismatch: $BASELINE_ACTUAL_SHA256"
  [[ "$(dpkg-deb -f "$baseline_target" Package)" == unlimotion.desktop ]] || fail 'baseline package identity mismatch'
  [[ "$(dpkg-deb -f "$baseline_target" Version)" == 1.27.0 ]] || fail 'baseline version mismatch'
}

run_missing_runtime_negative() {
  CURRENT_STEP="missing-runtime-fixture"
  local mutation_root="$TEMP_DIR/missing-runtime-root"
  local mutated_deb="$MOUNT_DIR/candidate.deb"
  local app_log="$TEMP_DIR/missing-runtime-application.log"
  local result
  local count
  local window_id
  local closure_detected=false

  rm -f -- "$mutated_deb"
  dpkg-deb -R "$DEB_PATH" "$mutation_root"
  sed -i 's/, libx11-6//' "$mutation_root/DEBIAN/control"
  ! grep -Eq '(^|[, ])libx11-6([, ]|$)' "$mutation_root/DEBIAN/control" || fail 'negative fixture did not remove libx11-6'
  dpkg-deb --root-owner-group --build "$mutation_root" "$mutated_deb" >/dev/null

  for APT_ATTEMPT in 1 2 3; do
    reset_target
    if run_target_apt 'apt-get update && apt-get install -y --no-install-recommends /candidates/candidate.deb'; then
      break
    else
      result=$?
    fi
    if [[ "$result" -eq 75 && "$APT_ATTEMPT" -lt 3 ]]; then
      continue
    fi
    return "$result"
  done

  if "$CONTAINER_CLI" exec "$TARGET_NAME" dpkg-query -W libx11-6 >/dev/null 2>&1; then
    fail 'negative fixture is invalid because libx11-6 was installed transitively'
  fi
  assert_target_package_layout
  if ! run_elf_closure; then
    closure_detected=true
    ELF_CLOSURE_STATUS="expectedFailure"
  fi
  ensure_test_user
  start_external_harness
  PRE_CLOSURE_SHA="$(package_closure_hash)"
  LAUNCH_MODE='negative-missing-runtime-external-x11'

  "$CONTAINER_CLI" exec --user 10001:10001 -e HOME=/home/unlimotion-test -e DISPLAY=:99 "$TARGET_NAME" \
    /usr/bin/Unlimotion --config=/home/unlimotion-test/unlimotion-data/config.json > "$app_log" 2>&1 &
  APP_EXEC_CLIENT_PID=$!

  window_id=""
  for count in $(seq 1 15); do
    window_id="$("$CONTAINER_CLI" exec -e DISPLAY=:99 "$HARNESS_NAME" xdotool search --name Unlimotion 2>/dev/null | head -n 1 || true)"
    [[ -z "$window_id" ]] || break
    if ! kill -0 "$APP_EXEC_CLIENT_PID" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done
  [[ -z "$window_id" ]] || fail 'missing-runtime negative unexpectedly opened an Unlimotion window'

  TARGET_APP_PID="$(discover_target_app_pid || true)"
  stop_tracked_application
  if [[ "$closure_detected" != true ]] && ! grep -Eiq 'libX11|DllNotFound|not found|Unable to load|native library|exception' "$app_log"; then
    cat "$app_log" >&2 || true
    fail 'missing-runtime negative did not produce attributable loader/launch evidence'
  fi
  APPLICATION_LOG_FILE="$(basename -- "${REPORT_PATH%.json}-application.log")"
  cp -- "$app_log" "$(dirname -- "$REPORT_PATH")/$APPLICATION_LOG_FILE"
  APPLICATION_LOG_SHA256="$(hash_file "$(dirname -- "$REPORT_PATH")/$APPLICATION_LOG_FILE")"
  POST_CLOSURE_SHA="$(package_closure_hash)"
  [[ "$POST_CLOSURE_SHA" == "$PRE_CLOSURE_SHA" ]] || fail 'negative target package closure changed during launch'
  WINDOW_VERIFIED=false
  DETAIL='missing libx11-6 remained fail-closed despite external X11 harness'
}

CURRENT_STEP="artifact-metadata"
case "$MODE" in
  metadata)
    APT_ATTEMPT=1
    RETRY_RULE='deterministic'
    RETRY_CLASSIFICATION='never'
    RETRY_CLEANUP='none'
    RETRY_MAX_ATTEMPTS=1
    validate_deb_metadata
    validate_appimage_metadata
    DETAIL='Debian and AppImage metadata, policy and inner-byte parity passed'
    write_report pass
    ;;
  clean)
    validate_deb_metadata
    prepare_candidate_mount
    IMAGE_IDENTITY="$(resolve_image_identity "$DEBIAN_IMAGE")"
    HARNESS_IDENTITY="$(resolve_image_identity "$HARNESS_IMAGE")"
    install_deb_clean_with_retry
    assert_target_package_layout
    run_elf_closure
    launch_and_verify_window debian
    [[ "$(hash_file "$DEB_PATH")" == "$DEB_SHA256" ]] || fail 'Debian candidate changed during validation'
    DETAIL='Clean package install, loader closure and external-X11 launch passed'
    write_report pass
    ;;
  upgrade)
    validate_deb_metadata
    prepare_candidate_mount
    prepare_baseline
    IMAGE_IDENTITY="$(resolve_image_identity "$DEBIAN_IMAGE")"
    HARNESS_IDENTITY="$(resolve_image_identity "$HARNESS_IMAGE")"
    install_upgrade_with_retry
    assert_target_package_layout
    run_elf_closure
    launch_and_verify_window debian
    [[ "$(hash_file "$DEB_PATH")" == "$DEB_SHA256" ]] || fail 'Debian candidate changed during upgrade validation'
    DETAIL='Exact migration-only 1.27.0 baseline upgraded with identity, stale-path, sentinel and launch continuity'
    write_report pass
    ;;
  appimage)
    validate_deb_metadata
    validate_appimage_metadata
    prepare_candidate_mount
    IMAGE_IDENTITY="$(resolve_image_identity "$DEBIAN_IMAGE")"
    HARNESS_IDENTITY="$(resolve_image_identity "$HARNESS_IMAGE")"
    install_appimage_prerequisites_with_retry
    PRE_CLOSURE_SHA="$(package_closure_hash)"
    launch_and_verify_window appimage
    [[ "$(hash_file "$APPIMAGE_PATH")" == "$APPIMAGE_SHA256" ]] || fail 'AppImage candidate changed during validation'
    DETAIL='AppImage structural, exact-byte and external-X11 extract-and-run checks passed; direct FUSE not verified'
    write_report pass
    ;;
  missing-runtime-negative)
    validate_deb_metadata
    prepare_candidate_mount
    IMAGE_IDENTITY="$(resolve_image_identity "$DEBIAN_IMAGE")"
    HARNESS_IDENTITY="$(resolve_image_identity "$HARNESS_IMAGE")"
    run_missing_runtime_negative
    write_report pass
    ;;
esac

echo "Linux $MODE evidence: $REPORT_PATH"
