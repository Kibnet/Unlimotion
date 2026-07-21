#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"

OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
OPENSSL_SOURCE_SHA256="eeca035d4dd4e84fc25846d952da6297484afa0650a6f84c682e39df3a4123ca"
LIBSSH2_VERSION="${LIBSSH2_VERSION:-1.11.1}"
LIBSSH2_SOURCE_SHA256="d9ec76cbe34db98eec3539fe2c899d26b0c837cb3eb466a56b0f109cabf658f7"
LIBGIT2_NATIVE_UPSTREAM_VERSION="${LIBGIT2_NATIVE_UPSTREAM_VERSION:-2.0.323}"
LIBGIT2_NATIVE_UPSTREAM_SHA256="d2a16ac8d0b4bb4e5417e0c9fcb36f9e0e52babd6bc9c8bec0810685553feeb1"
LIBGIT2_NATIVE_PACKAGE_VERSION="${LIBGIT2_NATIVE_PACKAGE_VERSION:-2.0.324-android.7}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-23}"
ANDROID_NDK_VERSION="${ANDROID_NDK_VERSION:-27.2.12479018}"

MODE=""
IDENTITY_PATH=""
OUTPUT_DIR=""
CACHE_DIR="$ROOT_DIR/artifacts/android-distribution-cache"
REQUESTED_CACHE_KEY=""
MATCHED_CACHE_KEY=""
CACHE_HIT="false"
RUNNER_OS="${RUNNER_OS:-$(uname -s)}"
RUNNER_ARCH="${RUNNER_ARCH:-$(uname -m)}"
SIGNATURE_PROFILE=""

usage() {
  cat <<'EOF'
Usage:
  build-android-distribution.sh --mode prepare-cache|build --identity <identity.json> --output-dir <dir> [options]

Options:
  --cache-dir <dir>             Cache root (default: artifacts/android-distribution-cache)
  --requested-cache-key <key>   Exact key emitted by prepare-cache
  --matched-cache-key <key>     Exact key returned by the cache restore action
  --cache-hit true|false        Whether an exact cache entry was restored
  --runner-os <value>           Runner OS used in the cache key
  --runner-arch <value>         Runner architecture used in the cache key
  --signature-profile <value>   test or production; must match the identity policy
EOF
}

fail() {
  echo "$SCRIPT_NAME: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

require_file() {
  [ -f "$1" ] || fail "Required file not found: $1"
}

absolute_path() {
  python3 - "$1" <<'PY'
import pathlib
import sys

print(pathlib.Path(sys.argv[1]).expanduser().resolve())
PY
}

emit_output() {
  local name="$1"
  local value="$2"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    printf '%s=%s\n' "$name" "$value" >> "$GITHUB_OUTPUT"
  fi
  printf '%s=%s\n' "$name" "$value"
}

sha256_file() {
  local checksum_output
  local digest
  checksum_output="$(sha256sum -- "$1")"
  checksum_output="${checksum_output#\\}"
  digest="${checksum_output%% *}"
  [[ "$digest" =~ ^[0-9a-f]{64}$ ]] || fail "Unable to parse SHA-256 for $1"
  printf '%s\n' "$digest"
}

assert_source_tree_contract() {
  local actual_source_sha
  local tracked_status
  local submodule_status_file

  require_command git
  actual_source_sha="$(git -C "$ROOT_DIR" rev-parse --verify HEAD)"
  [[ "$actual_source_sha" =~ ^[0-9a-f]{40}$ ]] || fail "Git HEAD must be an exact 40-character lowercase SHA"
  [ "$SOURCE_SHA" = "$actual_source_sha" ] || \
    fail "Identity sourceSha must equal the exact current Git HEAD"

  tracked_status="$(git -C "$ROOT_DIR" status --porcelain=v1 --untracked-files=no --ignore-submodules=untracked)"
  [ -z "$tracked_status" ] || \
    fail "Distribution source has tracked, index, or submodule changes"

  submodule_status_file="$(mktemp "${TMPDIR:-/tmp}/unlimotion-android-submodules.XXXXXX")"
  if ! git -C "$ROOT_DIR" submodule status --recursive >"$submodule_status_file"; then
    rm -f -- "$submodule_status_file"
    fail "Unable to inspect recursive Git submodule state"
  fi
  while IFS= read -r submodule_status; do
    case "$submodule_status" in
      " "*) ;;
      *)
        rm -f -- "$submodule_status_file"
        fail "Distribution source contains an uninitialized, modified, or conflicted submodule: $submodule_status"
        ;;
    esac
  done <"$submodule_status_file"
  rm -f -- "$submodule_status_file"
}

assert_exact_flat_package_directory() {
  local directory="$1"
  shift
  python3 - "$directory" "$@" <<'PY'
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
expected = sorted(sys.argv[2:])
if root.is_symlink() or not root.is_dir():
    raise SystemExit(f"Package directory must be a real non-symlink directory: {root}")
entries = sorted(root.iterdir(), key=lambda candidate: candidate.name)
actual = [candidate.name for candidate in entries]
if actual != expected:
    raise SystemExit(
        f"Package directory must contain exactly {expected}; actual entries are {actual}"
    )
for candidate in entries:
    if candidate.is_symlink() or not candidate.is_file() or candidate.suffix != ".nupkg":
        raise SystemExit(
            f"Package directory entries must be top-level regular .nupkg files: {candidate}"
        )
descendants = sorted(
    candidate.relative_to(root).as_posix() for candidate in root.rglob("*")
)
if descendants != expected:
    raise SystemExit(
        "Package directory must not contain nested files or directories; "
        f"recursive entries are {descendants}"
    )
PY
}

verify_restored_local_package() {
  local assets_path="$1"
  local package_id="$2"
  local package_version="$3"
  local nupkg_path="$4"

  python3 - \
    "$assets_path" \
    "$NUGET_PACKAGES" \
    "$NUGET_LOCAL_DIR" \
    "$package_id" \
    "$package_version" \
    "$nupkg_path" <<'PY'
import base64
import hashlib
import json
import pathlib
import sys

assets_path, packages_root_text, local_feed_text, package_id, version, nupkg_text = sys.argv[1:]
assets_file = pathlib.Path(assets_path)
packages_root = pathlib.Path(packages_root_text).resolve()
local_feed = pathlib.Path(local_feed_text).resolve()
nupkg = pathlib.Path(nupkg_text)
if nupkg.is_symlink() or not nupkg.is_file() or nupkg.parent.resolve() != local_feed:
    raise SystemExit(f"Verified local package is not an exact top-level feed file: {nupkg}")
nupkg_bytes = nupkg.read_bytes()
expected_raw_sha512 = base64.b64encode(hashlib.sha512(nupkg_bytes).digest()).decode("ascii")


def normalize_content_hash(value, label):
    if not isinstance(value, str) or not value:
        raise SystemExit(f"{label} must be a non-empty SHA-512 content hash")
    normalized = value.removeprefix("sha512-")
    if not normalized or normalized != normalized.strip():
        raise SystemExit(f"{label} must be canonical SHA-512 Base64")
    try:
        decoded = base64.b64decode(normalized, validate=True)
    except ValueError as error:
        raise SystemExit(f"{label} must be canonical SHA-512 Base64") from error
    if len(decoded) != 64 or base64.b64encode(decoded).decode("ascii") != normalized:
        raise SystemExit(f"{label} must be canonical SHA-512 Base64")
    return normalized

assets = json.loads(assets_file.read_text(encoding="utf-8"))
library_key = f"{package_id}/{version}"
libraries = assets.get("libraries") or {}
package_library_keys = sorted(
    key
    for key, value in libraries.items()
    if isinstance(key, str)
    and key.lower().startswith(package_id.lower() + "/")
    and isinstance(value, dict)
    and value.get("type") == "package"
)
if package_library_keys != [library_key]:
    raise SystemExit(
        f"Restore assets must contain only exact package identity {library_key}; "
        f"actual identities are {package_library_keys}"
    )
library = libraries.get(library_key)
if not isinstance(library, dict) or library.get("type") != "package":
    raise SystemExit(f"Restore assets do not contain exact package identity {library_key}")
assets_content_hash = normalize_content_hash(
    library.get("sha512"), "Restore assets sha512"
)
expected_library_path = f"{package_id.lower()}/{version.lower()}"
if str(library.get("path", "")).lower() != expected_library_path:
    raise SystemExit(f"Restore assets path does not match exact package identity {library_key}")

package_folders = assets.get("packageFolders") or {}
resolved_package_folders = {pathlib.Path(path).resolve() for path in package_folders}
if resolved_package_folders != {packages_root}:
    raise SystemExit(
        f"Restore assets must use only the isolated global package root: {packages_root}"
    )

installed = packages_root / package_id.lower() / version.lower()
metadata_path = installed / ".nupkg.metadata"
sha_path = installed / f"{package_id.lower()}.{version.lower()}.nupkg.sha512"
installed_nupkg = installed / f"{package_id.lower()}.{version.lower()}.nupkg"
if metadata_path.is_symlink() or not metadata_path.is_file():
    raise SystemExit(f"Installed package metadata is missing for {library_key}")
if sha_path.is_symlink() or not sha_path.is_file():
    raise SystemExit(f"Installed package SHA-512 sidecar is missing for {library_key}")
if installed_nupkg.is_symlink() or not installed_nupkg.is_file():
    raise SystemExit(f"Installed package nupkg is missing for {library_key}")
if installed_nupkg.read_bytes() != nupkg_bytes:
    raise SystemExit(
        f"Installed package nupkg bytes do not match the exact local-feed package: {library_key}"
    )
metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
metadata_content_hash = normalize_content_hash(
    metadata.get("contentHash"), "Installed package metadata contentHash"
)
if assets_content_hash != metadata_content_hash:
    raise SystemExit(
        f"Restore assets logical content hash does not match installed package metadata: {library_key}"
    )
source = metadata.get("source")
if not isinstance(source, str) or pathlib.Path(source).resolve() != local_feed:
    raise SystemExit(f"Installed package source is not the exact isolated local feed: {library_key}")
if sha_path.read_text(encoding="utf-8-sig").strip() != expected_raw_sha512:
    raise SystemExit(f"Installed package SHA-512 sidecar does not match local nupkg {library_key}")
PY
}

fetch_verified_source() {
  local url="$1"
  local expected_sha256="$2"
  local destination="$3"
  local actual_sha256
  mkdir -p -- "$(dirname -- "$destination")"
  if [ -f "$destination" ]; then
    actual_sha256="$(sha256_file "$destination")"
    if [ "$actual_sha256" = "$expected_sha256" ]; then
      return 0
    fi
    rm -f -- "$destination"
  fi
  local temporary
  temporary="$(mktemp "${destination}.download.XXXXXX")"
  if ! curl --fail --location --proto '=https' --tlsv1.2 "$url" --output "$temporary"; then
    rm -f -- "$temporary"
    fail "Failed to download pinned source: $url"
  fi
  actual_sha256="$(sha256_file "$temporary")"
  if [ "$actual_sha256" != "$expected_sha256" ]; then
    rm -f -- "$temporary"
    fail "Pinned source hash mismatch for $url"
  fi
  mv -- "$temporary" "$destination"
}

normalize_key_part() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9._-]+/-/g; s/^-+|-+$//g'
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --mode)
      [ "$#" -ge 2 ] || fail "--mode requires a value"
      MODE="$2"
      shift 2
      ;;
    --identity)
      [ "$#" -ge 2 ] || fail "--identity requires a value"
      IDENTITY_PATH="$2"
      shift 2
      ;;
    --output-dir)
      [ "$#" -ge 2 ] || fail "--output-dir requires a value"
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --cache-dir)
      [ "$#" -ge 2 ] || fail "--cache-dir requires a value"
      CACHE_DIR="$2"
      shift 2
      ;;
    --requested-cache-key)
      [ "$#" -ge 2 ] || fail "--requested-cache-key requires a value"
      REQUESTED_CACHE_KEY="$2"
      shift 2
      ;;
    --matched-cache-key)
      [ "$#" -ge 2 ] || fail "--matched-cache-key requires a value"
      MATCHED_CACHE_KEY="$2"
      shift 2
      ;;
    --cache-hit)
      [ "$#" -ge 2 ] || fail "--cache-hit requires a value"
      CACHE_HIT="$2"
      shift 2
      ;;
    --runner-os)
      [ "$#" -ge 2 ] || fail "--runner-os requires a value"
      RUNNER_OS="$2"
      shift 2
      ;;
    --runner-arch)
      [ "$#" -ge 2 ] || fail "--runner-arch requires a value"
      RUNNER_ARCH="$2"
      shift 2
      ;;
    --signature-profile)
      [ "$#" -ge 2 ] || fail "--signature-profile requires a value"
      SIGNATURE_PROFILE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

case "$MODE" in
  prepare-cache|build) ;;
  *)
    usage >&2
    fail "--mode must be prepare-cache or build"
    ;;
esac

[ -n "$IDENTITY_PATH" ] || fail "--identity is required"
[ -n "$OUTPUT_DIR" ] || fail "--output-dir is required"
require_command python3
require_command sha256sum
require_file "$IDENTITY_PATH"

IDENTITY_PATH="$(absolute_path "$IDENTITY_PATH")"
OUTPUT_DIR="$(absolute_path "$OUTPUT_DIR")"
CACHE_DIR="$(absolute_path "$CACHE_DIR")"
mkdir -p "$OUTPUT_DIR" "$CACHE_DIR"

identity_values="$(python3 - "$IDENTITY_PATH" "$SIGNATURE_PROFILE" <<'PY'
import json
import re
import sys

identity_path, requested_profile = sys.argv[1:]
with open(identity_path, encoding="utf-8") as stream:
    identity = json.load(stream)

required = (
    "rawTag",
    "normalizedVersion",
    "sourceSha",
    "workflowSha",
    "tagBinding",
    "androidVersionCode",
    "androidVersionCodePolicy",
    "filenamePlan",
)
missing = [name for name in required if name not in identity]
if missing:
    raise SystemExit(f"Identity is missing required fields: {', '.join(missing)}")

version = str(identity["normalizedVersion"])
if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
    raise SystemExit(f"Invalid normalizedVersion: {version}")
if version == "0.0.0":
    raise SystemExit("normalizedVersion must be at least 0.0.1")
raw_tag = str(identity["rawTag"])
if raw_tag not in (version, "v" + version):
    raise SystemExit("rawTag must be the normalized version with an optional lowercase v prefix")

for field in ("sourceSha", "workflowSha"):
    value = str(identity[field])
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise SystemExit(f"{field} must be a 40-character lowercase SHA")

try:
    version_code = int(identity["androidVersionCode"])
except (TypeError, ValueError):
    raise SystemExit("androidVersionCode must be an integer")
if not 1 <= version_code <= 2_100_000_000:
    raise SystemExit("androidVersionCode must be in 1..2100000000")

policy = str(identity["androidVersionCodePolicy"])
last_published = identity.get("lastPublishedAndroidVersionCode")
if policy == "ci-test":
    derived_profile = "test"
    if identity["tagBinding"] != "notApplicable":
        raise SystemExit("ci-test identity requires tagBinding notApplicable")
elif policy == "production-monotonic":
    derived_profile = "production"
    if identity["tagBinding"] != "required":
        raise SystemExit("production-monotonic identity requires tagBinding required")
    if not isinstance(last_published, int):
        raise SystemExit("production-monotonic requires integer lastPublishedAndroidVersionCode")
    if version_code <= last_published:
        raise SystemExit(
            "production-monotonic androidVersionCode must be greater than lastPublishedAndroidVersionCode"
        )
else:
    raise SystemExit(f"Unsupported androidVersionCodePolicy: {policy}")

profile = requested_profile or derived_profile
if profile != derived_profile:
    raise SystemExit(
        f"signature profile {profile!r} is incompatible with androidVersionCodePolicy {policy!r}"
    )

android_plan = identity.get("filenamePlan", {}).get("android", {})
arm64_name = android_plan.get("arm64Apk")
x64_name = android_plan.get("x64Apk")
for field, value in (("arm64Apk", arm64_name), ("x64Apk", x64_name)):
    if not isinstance(value, str) or not value.endswith(".apk") or "/" in value or "\\" in value:
        raise SystemExit(f"filenamePlan.android.{field} must be a safe APK basename")
    if version not in value:
        raise SystemExit(f"filenamePlan.android.{field} must contain normalizedVersion")
if arm64_name.casefold() == x64_name.casefold():
    raise SystemExit("Android APK filenames must be unique case-insensitively")
if raw_tag.startswith("v") and any(raw_tag in value for value in (arm64_name, x64_name)):
    raise SystemExit("Android APK filenames must not contain the raw v-prefixed tag")

last_value = "" if last_published is None else str(last_published)
print(
    "\t".join(
        (
            version,
            str(identity["sourceSha"]),
            str(identity["workflowSha"]),
            str(version_code),
            policy,
            profile,
            last_value,
            arm64_name,
            x64_name,
        )
    )
)
PY
)"
IFS=$'\t' read -r NORMALIZED_VERSION SOURCE_SHA WORKFLOW_SHA ANDROID_VERSION_CODE ANDROID_VERSION_CODE_POLICY SIGNATURE_PROFILE LAST_PUBLISHED_ANDROID_VERSION_CODE ARM64_APK_NAME X64_APK_NAME <<< "$identity_values"

assert_source_tree_contract

NATIVE_INPUTS_PATH="$OUTPUT_DIR/native-inputs.json"

write_native_inputs() {
  local libgit2_commit
  libgit2_commit="$(git -C "$ROOT_DIR" ls-tree HEAD .native/libgit2-src | awk '{print $3}')"
  [[ "$libgit2_commit" =~ ^[0-9a-f]{40}$ ]] || fail "Unable to resolve the committed libgit2 submodule SHA"

  python3 - \
    "$ROOT_DIR" \
    "$NATIVE_INPUTS_PATH" \
    "$ANDROID_API_LEVEL" \
    "$ANDROID_NDK_VERSION" \
    "$RUNNER_OS" \
    "$RUNNER_ARCH" \
    "$OPENSSL_VERSION" \
    "$OPENSSL_SOURCE_SHA256" \
    "$LIBSSH2_VERSION" \
    "$LIBSSH2_SOURCE_SHA256" \
    "$libgit2_commit" \
    "$LIBGIT2_NATIVE_UPSTREAM_VERSION" \
    "$LIBGIT2_NATIVE_UPSTREAM_SHA256" \
    "$LIBGIT2_NATIVE_PACKAGE_VERSION" <<'PY'
import hashlib
import json
import pathlib
import sys

(
    root_text,
    output_text,
    api_level,
    ndk_revision,
    runner_os,
    runner_arch,
    openssl_version,
    openssl_sha,
    libssh2_version,
    libssh2_sha,
    libgit2_commit,
    upstream_version,
    upstream_sha,
    native_package_version,
) = sys.argv[1:]

root = pathlib.Path(root_text)
output = pathlib.Path(output_text)
tracked_inputs = (
    "scripts/android-native-common.sh",
    "scripts/build-openssl-android.sh",
    "scripts/build-libssh2-android.sh",
    "scripts/build-libgit2-android.sh",
    "scripts/pack-libgit2sharp-nativebinaries-android.sh",
    "scripts/build-android-distribution.sh",
    "src/Unlimotion.Android/Unlimotion.Android.csproj",
    "src/Directory.Packages.props",
    "src/nuget.config",
)
input_hashes = {}
for relative in tracked_inputs:
    candidate = root / relative
    if not candidate.is_file():
        raise SystemExit(f"Native input file is missing: {relative}")
    input_hashes[relative] = hashlib.sha256(candidate.read_bytes()).hexdigest()

payload = {
    "schemaVersion": 1,
    "androidApiLevel": int(api_level),
    "ndkRevision": ndk_revision,
    "host": {
        "os": runner_os,
        "arch": runner_arch,
        "toolchainTriples": ["aarch64-linux-android", "x86_64-linux-android"],
    },
    "abis": ["arm64-v8a", "x86_64"],
    "sources": {
        "openssl": {
            "version": openssl_version,
            "url": f"https://www.openssl.org/source/openssl-{openssl_version}.tar.gz",
            "sha256": openssl_sha,
        },
        "libssh2": {
            "version": libssh2_version,
            "url": f"https://www.libssh2.org/download/libssh2-{libssh2_version}.tar.gz",
            "sha256": libssh2_sha,
        },
        "libgit2Commit": libgit2_commit,
        "upstreamNativePackage": {
            "version": upstream_version,
            "url": (
                "https://api.nuget.org/v3-flatcontainer/libgit2sharp.nativebinaries/"
                f"{upstream_version}/libgit2sharp.nativebinaries.{upstream_version}.nupkg"
            ),
            "sha256": upstream_sha,
        },
    },
    "nativePackageVersion": native_package_version,
    "inputFileSha256": input_hashes,
}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY
}

if [ "$MODE" = "prepare-cache" ]; then
  require_command git
  write_native_inputs
  NATIVE_INPUT_DIGEST="$(sha256_file "$NATIVE_INPUTS_PATH")"
  runner_os_part="$(normalize_key_part "$RUNNER_OS")"
  runner_arch_part="$(normalize_key_part "$RUNNER_ARCH")"
  [ -n "$runner_os_part" ] || fail "Runner OS cannot produce an empty cache-key component"
  [ -n "$runner_arch_part" ] || fail "Runner architecture cannot produce an empty cache-key component"
  REQUESTED_CACHE_KEY="android-native-v2-${runner_os_part}-${runner_arch_part}-${NATIVE_INPUT_DIGEST}"
  CACHE_PATH="$CACHE_DIR/$NATIVE_INPUT_DIGEST"

  rm -rf -- "$CACHE_PATH"
  mkdir -p "$CACHE_PATH"

  emit_output native_inputs "$NATIVE_INPUTS_PATH"
  emit_output native_input_digest "$NATIVE_INPUT_DIGEST"
  emit_output cache_key "$REQUESTED_CACHE_KEY"
  emit_output cache_path "$CACHE_PATH"
  exit 0
fi

case "$CACHE_HIT" in
  true|false) ;;
  *) fail "--cache-hit must be true or false" ;;
esac

require_file "$NATIVE_INPUTS_PATH"
NATIVE_INPUT_DIGEST="$(sha256_file "$NATIVE_INPUTS_PATH")"
runner_os_part="$(normalize_key_part "$RUNNER_OS")"
runner_arch_part="$(normalize_key_part "$RUNNER_ARCH")"
EXPECTED_CACHE_KEY="android-native-v2-${runner_os_part}-${runner_arch_part}-${NATIVE_INPUT_DIGEST}"
[ -n "$REQUESTED_CACHE_KEY" ] || REQUESTED_CACHE_KEY="$EXPECTED_CACHE_KEY"
[ "$REQUESTED_CACHE_KEY" = "$EXPECTED_CACHE_KEY" ] || fail "Requested cache key does not match native-input digest"
CACHE_PATH="$CACHE_DIR/$NATIVE_INPUT_DIGEST"

if [ "$CACHE_HIT" = "true" ]; then
  [ -n "$MATCHED_CACHE_KEY" ] || fail "A cache hit requires --matched-cache-key"
  [ "$MATCHED_CACHE_KEY" = "$REQUESTED_CACHE_KEY" ] || fail "Partial/prefix Android cache matches are forbidden"
else
  [ -z "$MATCHED_CACHE_KEY" ] || fail "A cache miss cannot provide --matched-cache-key"
fi

NATIVE_ARTIFACTS_DIR="$ROOT_DIR/artifacts/android-native"
NUGET_LOCAL_DIR="$OUTPUT_DIR/nuget-local"
NUGET_CONFIG_PATH="$OUTPUT_DIR/nuget.config"
LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME="LibGit2Sharp.NativeBinaries.${LIBGIT2_NATIVE_UPSTREAM_VERSION}.nupkg"
LIBGIT2_NATIVE_PACKAGE_NAME="LibGit2Sharp.NativeBinaries.${LIBGIT2_NATIVE_PACKAGE_VERSION}.nupkg"
NODIFY_PACKAGE_RELATIVE_PATH="artifacts/nuget-local/NodifyAvalonia.6.6.0-unlimotion.a12.1.nupkg"
NODIFY_PACKAGE_SOURCE="$ROOT_DIR/$NODIFY_PACKAGE_RELATIVE_PATH"
NODIFY_PACKAGE_NAME="$(basename "$NODIFY_PACKAGE_RELATIVE_PATH")"
NODIFY_HEAD_COPY="$OUTPUT_DIR/.${NODIFY_PACKAGE_NAME}.head"

require_command git
require_file "$NODIFY_PACKAGE_SOURCE"
mapfile -t tracked_nodify_packages < <(git -C "$ROOT_DIR" ls-files -- 'artifacts/nuget-local/NodifyAvalonia.*.nupkg')
[ "${#tracked_nodify_packages[@]}" -eq 1 ] && [ "${tracked_nodify_packages[0]}" = "$NODIFY_PACKAGE_RELATIVE_PATH" ] || \
  fail "Exactly one expected NodifyAvalonia package must be tracked by Git"
git -C "$ROOT_DIR" cat-file -e "HEAD:$NODIFY_PACKAGE_RELATIVE_PATH" || fail "Tracked NodifyAvalonia package is missing from HEAD"
rm -f -- "$NODIFY_HEAD_COPY"
if ! git -C "$ROOT_DIR" show "HEAD:$NODIFY_PACKAGE_RELATIVE_PATH" >"$NODIFY_HEAD_COPY"; then
  rm -f -- "$NODIFY_HEAD_COPY"
  fail "Unable to extract the tracked NodifyAvalonia package from HEAD"
fi
if [ "$(sha256_file "$NODIFY_PACKAGE_SOURCE")" != "$(sha256_file "$NODIFY_HEAD_COPY")" ]; then
  rm -f -- "$NODIFY_HEAD_COPY"
  fail "Working-tree NodifyAvalonia package bytes differ from HEAD"
fi

rm -rf -- "$NATIVE_ARTIFACTS_DIR" "$NUGET_LOCAL_DIR"
mkdir -p "$NATIVE_ARTIFACTS_DIR" "$NUGET_LOCAL_DIR" "$CACHE_PATH"
mv -- "$NODIFY_HEAD_COPY" "$NUGET_LOCAL_DIR/$NODIFY_PACKAGE_NAME"

python3 - "$ROOT_DIR/src/nuget.config" "$NUGET_CONFIG_PATH" "$NUGET_LOCAL_DIR" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

source_path = pathlib.Path(sys.argv[1])
output_path = pathlib.Path(sys.argv[2])
isolated_feed = sys.argv[3]
source_root = ET.parse(source_path).getroot()
package_sources = source_root.find("packageSources")
if package_sources is None:
    raise SystemExit("Repository NuGet config has no packageSources")
actual_sources = {
    node.attrib.get("key"): node.attrib.get("value")
    for node in package_sources.findall("add")
}
expected_sources = {
    "local": "../artifacts/nuget-local",
    "nuget.org": "https://api.nuget.org/v3/index.json",
}
if actual_sources != expected_sources or len(package_sources.findall("add")) != 2:
    raise SystemExit("Repository NuGet sources differ from the reviewed two-source contract")

root = ET.Element("configuration")
sources = ET.SubElement(root, "packageSources")
ET.SubElement(sources, "clear")
ET.SubElement(sources, "add", {"key": "local", "value": isolated_feed})
ET.SubElement(
    sources,
    "add",
    {"key": "nuget.org", "value": expected_sources["nuget.org"], "protocolVersion": "3"},
)
mappings = ET.SubElement(root, "packageSourceMapping")
ET.SubElement(mappings, "clear")
local_mapping = ET.SubElement(mappings, "packageSource", {"key": "local"})
ET.SubElement(local_mapping, "package", {"pattern": "NodifyAvalonia"})
ET.SubElement(local_mapping, "package", {"pattern": "LibGit2Sharp.NativeBinaries"})
public_mapping = ET.SubElement(mappings, "packageSource", {"key": "nuget.org"})
ET.SubElement(public_mapping, "package", {"pattern": "*"})
output_path.parent.mkdir(parents=True, exist_ok=True)
ET.ElementTree(root).write(output_path, encoding="utf-8", xml_declaration=True)
PY

if [ "$CACHE_HIT" = "true" ]; then
  "$BASH" "$ROOT_DIR/scripts/test-android-distribution.sh" \
    --mode provenance \
    --identity "$IDENTITY_PATH" \
    --native-inputs "$NATIVE_INPUTS_PATH" \
    --cache-path "$CACHE_PATH" \
    --requested-cache-key "$REQUESTED_CACHE_KEY" \
    --matched-cache-key "$MATCHED_CACHE_KEY" \
    --cache-hit true \
    --cache-save false \
    --evidence "$OUTPUT_DIR/native-cache-evidence.json"

  assert_exact_flat_package_directory \
    "$CACHE_PATH/bundle/nuget-local" \
    "$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" \
    "$LIBGIT2_NATIVE_PACKAGE_NAME"
  cp -a "$CACHE_PATH/bundle/android-native/." "$NATIVE_ARTIFACTS_DIR/"
  cp -a "$CACHE_PATH/bundle/nuget-local/." "$NUGET_LOCAL_DIR/"
  CACHE_SAVE="false"
else
  require_command git
  require_command dotnet
  require_command curl
  require_command unzip

  expected_libgit2_commit="$(python3 - "$NATIVE_INPUTS_PATH" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    print(json.load(stream)["sources"]["libgit2Commit"])
PY
)"
  actual_libgit2_commit="$(git -C "$ROOT_DIR/.native/libgit2-src" rev-parse HEAD)"
  [ "$actual_libgit2_commit" = "$expected_libgit2_commit" ] || fail "Checked-out libgit2 commit does not match the committed gitlink"

  export ANDROID_API_LEVEL ANDROID_NDK_VERSION
  export OPENSSL_VERSION LIBSSH2_VERSION LIBGIT2_NATIVE_PACKAGE_VERSION
  export LIBGIT2_NATIVE_UPSTREAM_VERSION
  export FORCE_REBUILD=1

  for abi in arm64-v8a x86_64; do
    export ANDROID_ABI="$abi"
    case "$abi" in
      arm64-v8a) rid="android-arm64" ;;
      x86_64) rid="android-x64" ;;
      *) fail "Unexpected ABI: $abi" ;;
    esac
    export BUILD_DIR="$NATIVE_ARTIFACTS_DIR/libgit2-build-$rid"
    openssl_archive="$NATIVE_ARTIFACTS_DIR/openssl-$OPENSSL_VERSION-$rid/downloads/openssl-$OPENSSL_VERSION.tar.gz"
    libssh2_archive="$NATIVE_ARTIFACTS_DIR/libssh2-$LIBSSH2_VERSION-$rid/downloads/libssh2-$LIBSSH2_VERSION.tar.gz"
    fetch_verified_source \
      "https://www.openssl.org/source/openssl-$OPENSSL_VERSION.tar.gz" \
      "$OPENSSL_SOURCE_SHA256" \
      "$openssl_archive"
    fetch_verified_source \
      "https://www.libssh2.org/download/libssh2-$LIBSSH2_VERSION.tar.gz" \
      "$LIBSSH2_SOURCE_SHA256" \
      "$libssh2_archive"
    bash "$ROOT_DIR/scripts/build-openssl-android.sh"
    bash "$ROOT_DIR/scripts/build-libssh2-android.sh"
    bash "$ROOT_DIR/scripts/build-libgit2-android.sh"

    require_file "$openssl_archive"
    require_file "$libssh2_archive"
    [ "$(sha256_file "$openssl_archive")" = "$OPENSSL_SOURCE_SHA256" ] || fail "OpenSSL source archive hash mismatch"
    [ "$(sha256_file "$libssh2_archive")" = "$LIBSSH2_SOURCE_SHA256" ] || fail "libssh2 source archive hash mismatch"
  done

  unset BUILD_DIR
  export ANDROID_ABIS="arm64-v8a x86_64"
  export NUGET_LOCAL_FEED="$NUGET_LOCAL_DIR"
  upstream_package="$NATIVE_ARTIFACTS_DIR/nuget-downloads/LibGit2Sharp.NativeBinaries.${LIBGIT2_NATIVE_UPSTREAM_VERSION}.nupkg"
  fetch_verified_source \
    "https://api.nuget.org/v3-flatcontainer/libgit2sharp.nativebinaries/${LIBGIT2_NATIVE_UPSTREAM_VERSION}/libgit2sharp.nativebinaries.${LIBGIT2_NATIVE_UPSTREAM_VERSION}.nupkg" \
    "$LIBGIT2_NATIVE_UPSTREAM_SHA256" \
    "$upstream_package"
  bash "$ROOT_DIR/scripts/pack-libgit2sharp-nativebinaries-android.sh"

  native_package="$NUGET_LOCAL_DIR/$LIBGIT2_NATIVE_PACKAGE_NAME"
  require_file "$upstream_package"
  require_file "$native_package"
  [ "$(sha256_file "$upstream_package")" = "$LIBGIT2_NATIVE_UPSTREAM_SHA256" ] || fail "Upstream LibGit2Sharp.NativeBinaries package hash mismatch"
  cp "$upstream_package" "$NUGET_LOCAL_DIR/$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME"

  rm -rf -- "$CACHE_PATH"
  mkdir -p "$CACHE_PATH/bundle/android-native" "$CACHE_PATH/bundle/nuget-local"
  for rid in android-arm64 android-x64; do
    cp -aL "$NATIVE_ARTIFACTS_DIR/openssl-$OPENSSL_VERSION-$rid/prefix" "$CACHE_PATH/bundle/android-native/openssl-$OPENSSL_VERSION-$rid-prefix"
    cp -aL "$NATIVE_ARTIFACTS_DIR/libssh2-$LIBSSH2_VERSION-$rid/prefix" "$CACHE_PATH/bundle/android-native/libssh2-$LIBSSH2_VERSION-$rid-prefix"
    mkdir -p "$CACHE_PATH/bundle/android-native/libgit2-$rid"
    cp "$NATIVE_ARTIFACTS_DIR/libgit2-$rid/libgit2-3f4182d.so" "$CACHE_PATH/bundle/android-native/libgit2-$rid/"
  done
  cp "$NUGET_LOCAL_DIR/$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" "$CACHE_PATH/bundle/nuget-local/"
  cp "$native_package" "$CACHE_PATH/bundle/nuget-local/"
  cp "$NATIVE_INPUTS_PATH" "$CACHE_PATH/native-inputs.json"

  python3 - "$NATIVE_INPUTS_PATH" "$CACHE_PATH" "$REQUESTED_CACHE_KEY" <<'PY'
import hashlib
import json
import pathlib
import sys

inputs_path = pathlib.Path(sys.argv[1])
cache_path = pathlib.Path(sys.argv[2])
requested_key = sys.argv[3]
inputs = json.loads(inputs_path.read_text(encoding="utf-8"))
bundle = cache_path / "bundle"
outputs = []
for candidate in sorted(path for path in bundle.rglob("*") if path.is_file()):
    outputs.append(
        {
            "path": candidate.relative_to(bundle).as_posix(),
            "size": candidate.stat().st_size,
            "sha256": hashlib.sha256(candidate.read_bytes()).hexdigest(),
        }
    )
if not outputs:
    raise SystemExit("Native cache bundle is empty")

payload = {
    "schemaVersion": 1,
    "nativeInputDigest": hashlib.sha256(inputs_path.read_bytes()).hexdigest(),
    "requestedCacheKey": requested_key,
    "matchedCacheKey": requested_key,
    "inputs": inputs,
    "outputs": outputs,
}
(cache_path / "native-provenance.json").write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY

  "$BASH" "$ROOT_DIR/scripts/test-android-distribution.sh" \
    --mode provenance \
    --identity "$IDENTITY_PATH" \
    --native-inputs "$NATIVE_INPUTS_PATH" \
    --cache-path "$CACHE_PATH" \
    --requested-cache-key "$REQUESTED_CACHE_KEY" \
    --matched-cache-key "$REQUESTED_CACHE_KEY" \
    --cache-hit false \
    --cache-save true \
    --evidence "$OUTPUT_DIR/native-cache-evidence.json"
  assert_exact_flat_package_directory \
    "$CACHE_PATH/bundle/nuget-local" \
    "$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" \
    "$LIBGIT2_NATIVE_PACKAGE_NAME"
  CACHE_SAVE="true"
fi

assert_exact_flat_package_directory \
  "$NUGET_LOCAL_DIR" \
  "$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" \
  "$LIBGIT2_NATIVE_PACKAGE_NAME" \
  "$NODIFY_PACKAGE_NAME"

require_command keytool
SIGNING_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/unlimotion-android-signing.XXXXXX")"
cleanup_signing() {
  rm -rf -- "$SIGNING_DIR"
  unset ANDROID_SIGNING_STORE_PASS ANDROID_SIGNING_KEY_PASS
}
trap cleanup_signing EXIT

if [ "$SIGNATURE_PROFILE" = "test" ]; then
  ANDROID_SIGNING_KEYSTORE="$SIGNING_DIR/unlimotion-ci.keystore"
  ANDROID_SIGNING_STORE_PASS="android-ci-test"
  ANDROID_SIGNING_KEY_ALIAS="unlimotion-ci"
  ANDROID_SIGNING_KEY_PASS="android-ci-test"
  keytool -genkeypair \
    -keystore "$ANDROID_SIGNING_KEYSTORE" \
    -storepass "$ANDROID_SIGNING_STORE_PASS" \
    -keypass "$ANDROID_SIGNING_KEY_PASS" \
    -alias "$ANDROID_SIGNING_KEY_ALIAS" \
    -keyalg RSA \
    -keysize 2048 \
    -validity 1 \
    -dname "CN=Unlimotion CI Test,O=Unlimotion,C=US" \
    -noprompt >/dev/null 2>&1
else
  : "${ANDROID_SIGNING_KEYSTORE:?ANDROID_SIGNING_KEYSTORE is required for production signing}"
  : "${ANDROID_SIGNING_STORE_PASS:?ANDROID_SIGNING_STORE_PASS is required for production signing}"
  : "${ANDROID_SIGNING_KEY_ALIAS:?ANDROID_SIGNING_KEY_ALIAS is required for production signing}"
  : "${ANDROID_SIGNING_KEY_PASS:?ANDROID_SIGNING_KEY_PASS is required for production signing}"
  require_file "$ANDROID_SIGNING_KEYSTORE"
fi
export ANDROID_SIGNING_STORE_PASS ANDROID_SIGNING_KEY_PASS

APK_OUTPUT_DIR="$OUTPUT_DIR/apks"
rm -rf -- "$APK_OUTPUT_DIR" "$OUTPUT_DIR/nuget-packages"
mkdir -p "$APK_OUTPUT_DIR" "$OUTPUT_DIR/nuget-packages"
export NUGET_PACKAGES="$OUTPUT_DIR/nuget-packages"
UNLIMOTION_ASSETS_PATH="$ROOT_DIR/src/Unlimotion/obj/project.assets.json"
ANDROID_ASSETS_PATH="$ROOT_DIR/src/Unlimotion.Android/obj/project.assets.json"

rm -rf -- \
  "$ROOT_DIR/src/Unlimotion.Android/bin/Release/net10.0-android" \
  "$ROOT_DIR/src/Unlimotion.Android/obj/Release/net10.0-android"
rm -f -- \
  "$UNLIMOTION_ASSETS_PATH" \
  "$ROOT_DIR/src/Unlimotion/obj/project.nuget.cache" \
  "$ROOT_DIR/src/Unlimotion/obj/Unlimotion.csproj.nuget.dgspec.json" \
  "$ROOT_DIR/src/Unlimotion/obj/Unlimotion.csproj.nuget.g.props" \
  "$ROOT_DIR/src/Unlimotion/obj/Unlimotion.csproj.nuget.g.targets" \
  "$ANDROID_ASSETS_PATH" \
  "$ROOT_DIR/src/Unlimotion.Android/obj/project.nuget.cache" \
  "$ROOT_DIR/src/Unlimotion.Android/obj/Unlimotion.Android.csproj.nuget.dgspec.json" \
  "$ROOT_DIR/src/Unlimotion.Android/obj/Unlimotion.Android.csproj.nuget.g.props" \
  "$ROOT_DIR/src/Unlimotion.Android/obj/Unlimotion.Android.csproj.nuget.g.targets"

for rid in android-arm64 android-x64; do
  dotnet build "$ROOT_DIR/src/Unlimotion.Android/Unlimotion.Android.csproj" \
    -c Release \
    -t:Package \
    -p:WarningsAsErrors=NU1603 \
    -p:RuntimeIdentifier="$rid" \
    -p:RuntimeIdentifiers="$rid" \
    -p:ContinuousIntegrationBuild=true \
    -p:DistributionBuild=true \
    -p:DistributionVersion="$NORMALIZED_VERSION" \
    -p:DistributionSourceSha="$SOURCE_SHA" \
    -p:GitHubRefName="$NORMALIZED_VERSION" \
    -p:SourceRevisionId="$SOURCE_SHA" \
    -p:RepositoryCommit="$SOURCE_SHA" \
    -p:ApplicationDisplayVersion="$NORMALIZED_VERSION" \
    -p:ApplicationVersion="$ANDROID_VERSION_CODE" \
    -p:Version="$NORMALIZED_VERSION" \
    -p:RestoreConfigFile="$NUGET_CONFIG_PATH" \
    -p:AndroidSdkDirectory="${ANDROID_SDK_ROOT:?ANDROID_SDK_ROOT is required}" \
    -p:JavaSdkDirectory="${JAVA_HOME:?JAVA_HOME is required}" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$ANDROID_SIGNING_KEYSTORE" \
    -p:AndroidSigningStorePass=env:ANDROID_SIGNING_STORE_PASS \
    -p:AndroidSigningKeyAlias="$ANDROID_SIGNING_KEY_ALIAS" \
    -p:AndroidSigningKeyPass=env:ANDROID_SIGNING_KEY_PASS

  require_file "$UNLIMOTION_ASSETS_PATH"
  require_file "$ANDROID_ASSETS_PATH"
  verify_restored_local_package \
    "$UNLIMOTION_ASSETS_PATH" \
    "LibGit2Sharp.NativeBinaries" \
    "$LIBGIT2_NATIVE_UPSTREAM_VERSION" \
    "$NUGET_LOCAL_DIR/$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME"
  verify_restored_local_package \
    "$ANDROID_ASSETS_PATH" \
    "NodifyAvalonia" \
    "6.6.0-unlimotion.a12.1" \
    "$NUGET_LOCAL_DIR/$NODIFY_PACKAGE_NAME"
  verify_restored_local_package \
    "$ANDROID_ASSETS_PATH" \
    "LibGit2Sharp.NativeBinaries" \
    "$LIBGIT2_NATIVE_PACKAGE_VERSION" \
    "$NUGET_LOCAL_DIR/$LIBGIT2_NATIVE_PACKAGE_NAME"

  apk_search_root="$ROOT_DIR/src/Unlimotion.Android/bin/Release/net10.0-android/$rid"
  mapfile -t signed_apks < <(find "$apk_search_root" -type f -name '*-Signed.apk' -print)
  [ "${#signed_apks[@]}" -eq 1 ] || fail "Expected exactly one signed APK for $rid, found ${#signed_apks[@]}"
  case "$rid" in
    android-arm64) apk_name="$ARM64_APK_NAME" ;;
    android-x64) apk_name="$X64_APK_NAME" ;;
    *) fail "Unexpected RID: $rid" ;;
  esac
  cp "${signed_apks[0]}" "$APK_OUTPUT_DIR/$apk_name"
done

EVIDENCE_PATH="$OUTPUT_DIR/evidence.json"
"$BASH" "$ROOT_DIR/scripts/test-android-distribution.sh" \
  --mode artifact \
  --identity "$IDENTITY_PATH" \
  --input-dir "$APK_OUTPUT_DIR" \
  --evidence "$EVIDENCE_PATH" \
  --signature-profile "$SIGNATURE_PROFILE"

python3 - "$EVIDENCE_PATH" "$NATIVE_INPUT_DIGEST" "$REQUESTED_CACHE_KEY" "$MATCHED_CACHE_KEY" "$CACHE_HIT" "$CACHE_SAVE" <<'PY'
import json
import pathlib
import sys

evidence_path = pathlib.Path(sys.argv[1])
payload = json.loads(evidence_path.read_text(encoding="utf-8"))
payload["nativeCache"] = {
    "nativeInputDigest": sys.argv[2],
    "requestedKey": sys.argv[3],
    "matchedKey": sys.argv[4] or None,
    "hit": sys.argv[5] == "true",
    "saveRequired": sys.argv[6] == "true",
}
evidence_path.write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY

emit_output artifact_dir "$APK_OUTPUT_DIR"
emit_output evidence "$EVIDENCE_PATH"
emit_output native_inputs "$NATIVE_INPUTS_PATH"
emit_output native_input_digest "$NATIVE_INPUT_DIGEST"
emit_output cache_key "$REQUESTED_CACHE_KEY"
emit_output cache_path "$CACHE_PATH"
emit_output cache_save "$CACHE_SAVE"
