#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"
EXPECTED_APPLICATION_ID="com.Kibnet.Unlimotion"
EXPECTED_MIN_SDK="23"
EXPECTED_TARGET_SDK="36"
EXPECTED_PRODUCTION_FINGERPRINT="1cca6de2bb329c14f89cd0441998e00df601e440d2a9b30c29bdd2cf0a321011"

MODE=""
IDENTITY_PATH=""
INPUT_DIR=""
EVIDENCE_PATH=""
SIGNATURE_PROFILE=""
NATIVE_INPUTS_PATH=""
CACHE_PATH=""
REQUESTED_CACHE_KEY=""
MATCHED_CACHE_KEY=""
CACHE_HIT=""
CACHE_SAVE=""
API_LEVEL=""
EMULATOR_FAILURE_EVIDENCE_ENABLED="false"
EMULATOR_EVIDENCE_FINALIZED="false"

usage() {
  cat <<'EOF'
Usage:
  test-android-distribution.sh --mode artifact --identity <identity.json> --input-dir <apk-dir> --evidence <json> [--signature-profile test|production]
  test-android-distribution.sh --mode provenance --identity <identity.json> --native-inputs <json> --cache-path <dir> --requested-cache-key <key> --matched-cache-key <key> --cache-hit true|false --cache-save true|false --evidence <json>
  test-android-distribution.sh --mode emulator --identity <identity.json> --input-dir <apk-dir> --api-level 23|36 --evidence <json>
EOF
}

fail() {
  if [ "$EMULATOR_FAILURE_EVIDENCE_ENABLED" = "true" ] &&
     [ "$EMULATOR_EVIDENCE_FINALIZED" != "true" ] &&
     [ "$(type -t write_emulator_failure_evidence || true)" = "function" ]; then
    if ! write_emulator_failure_evidence "$*"; then
      echo "$SCRIPT_NAME: unable to write structured emulator failure evidence" >&2
    fi
  fi
  echo "$SCRIPT_NAME: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

resolve_tool_version() {
  local label="$1"
  shift
  local output
  local status
  local first_line

  if output="$("$@" 2>&1)"; then
    :
  else
    status=$?
    first_line="$(printf '%s\n' "$output" | sed -n '1p' | tr -d '\r')"
    [ -n "$first_line" ] || first_line="no diagnostic output"
    fail "$label version probe failed (exit $status): $first_line"
  fi

  first_line="$(printf '%s\n' "$output" | sed -n '1p' | tr -d '\r')"
  [ -n "$first_line" ] || fail "Unable to resolve $label version"
  printf '%s\n' "$first_line"
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

sha256_file() {
  local checksum_output
  local digest
  checksum_output="$(sha256sum -- "$1")"
  checksum_output="${checksum_output#\\}"
  digest="${checksum_output%% *}"
  [[ "$digest" =~ ^[0-9a-f]{64}$ ]] || fail "Unable to parse SHA-256 for $1"
  printf '%s\n' "$digest"
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
    --input-dir)
      [ "$#" -ge 2 ] || fail "--input-dir requires a value"
      INPUT_DIR="$2"
      shift 2
      ;;
    --evidence)
      [ "$#" -ge 2 ] || fail "--evidence requires a value"
      EVIDENCE_PATH="$2"
      shift 2
      ;;
    --signature-profile)
      [ "$#" -ge 2 ] || fail "--signature-profile requires a value"
      SIGNATURE_PROFILE="$2"
      shift 2
      ;;
    --native-inputs)
      [ "$#" -ge 2 ] || fail "--native-inputs requires a value"
      NATIVE_INPUTS_PATH="$2"
      shift 2
      ;;
    --cache-path)
      [ "$#" -ge 2 ] || fail "--cache-path requires a value"
      CACHE_PATH="$2"
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
    --cache-save)
      [ "$#" -ge 2 ] || fail "--cache-save requires a value"
      CACHE_SAVE="$2"
      shift 2
      ;;
    --api-level)
      [ "$#" -ge 2 ] || fail "--api-level requires a value"
      API_LEVEL="$2"
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
  artifact|provenance|emulator) ;;
  *)
    usage >&2
    fail "--mode must be artifact, provenance, or emulator"
    ;;
esac

[ -n "$EVIDENCE_PATH" ] || fail "--evidence is required"
require_command python3
require_command sha256sum
EVIDENCE_PATH="$(absolute_path "$EVIDENCE_PATH")"
mkdir -p "$(dirname "$EVIDENCE_PATH")"

if [ "$MODE" = "provenance" ]; then
  [ -n "$IDENTITY_PATH" ] || fail "--identity is required in provenance mode"
  [ -n "$NATIVE_INPUTS_PATH" ] || fail "--native-inputs is required in provenance mode"
  [ -n "$CACHE_PATH" ] || fail "--cache-path is required in provenance mode"
  [ -n "$REQUESTED_CACHE_KEY" ] || fail "--requested-cache-key is required in provenance mode"
  [ -n "$MATCHED_CACHE_KEY" ] || fail "--matched-cache-key is required in provenance mode"
  case "$CACHE_HIT/$CACHE_SAVE" in
    true/false|false/true) ;;
    *) fail "provenance mode requires either --cache-hit true --cache-save false or --cache-hit false --cache-save true" ;;
  esac
  IDENTITY_PATH="$(absolute_path "$IDENTITY_PATH")"
  NATIVE_INPUTS_PATH="$(absolute_path "$NATIVE_INPUTS_PATH")"
  CACHE_PATH="$(absolute_path "$CACHE_PATH")"
  require_file "$IDENTITY_PATH"
  require_file "$NATIVE_INPUTS_PATH"
  require_file "$CACHE_PATH/native-provenance.json"
  require_file "$CACHE_PATH/native-inputs.json"

  python3 - \
    "$IDENTITY_PATH" \
    "$NATIVE_INPUTS_PATH" \
    "$CACHE_PATH" \
    "$REQUESTED_CACHE_KEY" \
    "$MATCHED_CACHE_KEY" \
    "$CACHE_HIT" \
    "$CACHE_SAVE" \
    "$EVIDENCE_PATH" <<'PY'
import hashlib
import json
import pathlib
import re
import sys

identity_path = pathlib.Path(sys.argv[1])
inputs_path = pathlib.Path(sys.argv[2])
cache_path = pathlib.Path(sys.argv[3])
requested_key = sys.argv[4]
matched_key = sys.argv[5]
cache_hit = sys.argv[6] == "true"
cache_save = sys.argv[7] == "true"
evidence_path = pathlib.Path(sys.argv[8])

def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))

inputs_bytes = inputs_path.read_bytes()
inputs = json.loads(inputs_bytes)
digest = hashlib.sha256(inputs_bytes).hexdigest()
identity = json.loads(identity_path.read_text(encoding="utf-8"))
version = str(identity.get("normalizedVersion", ""))
if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
    raise SystemExit("Invalid normalizedVersion")
raw_tag = str(identity.get("rawTag", ""))
if raw_tag not in (version, "v" + version):
    raise SystemExit("rawTag does not normalize to normalizedVersion")
for field in ("sourceSha", "workflowSha"):
    if not re.fullmatch(r"[0-9a-f]{40}", str(identity.get(field, ""))):
        raise SystemExit(f"Invalid {field}")
for field in ("manifestSha256", "supportMatrixSha256"):
    if not re.fullmatch(r"[0-9a-f]{64}", str(identity.get(field, ""))):
        raise SystemExit(f"Invalid {field}")
version_code = identity.get("androidVersionCode")
if not isinstance(version_code, int) or not 1 <= version_code <= 2_100_000_000:
    raise SystemExit("Invalid androidVersionCode")
version_policy = identity.get("androidVersionCodePolicy")
if version_policy == "ci-test":
    expected_signature_profile = "test"
    if identity.get("tagBinding") != "notApplicable":
        raise SystemExit("ci-test identity requires tagBinding notApplicable")
elif version_policy == "production-monotonic":
    expected_signature_profile = "production"
    if identity.get("tagBinding") != "required":
        raise SystemExit("production-monotonic identity requires tagBinding required")
    last_version_code = identity.get("lastPublishedAndroidVersionCode")
    if not isinstance(last_version_code, int) or version_code <= last_version_code:
        raise SystemExit("Non-monotonic production androidVersionCode")
else:
    raise SystemExit("Invalid androidVersionCodePolicy")
if identity.get("signatureProfile") != expected_signature_profile:
    raise SystemExit("signatureProfile does not match androidVersionCodePolicy")
host = inputs.get("host") or {}
def key_part(value):
    normalized = []
    previous_dash = False
    for character in str(value).lower():
        if character.isascii() and (character.isalnum() or character in "._-"):
            normalized.append(character)
            previous_dash = character == "-"
        elif not previous_dash:
            normalized.append("-")
            previous_dash = True
    return "".join(normalized).strip("-")

runner_os = key_part(host.get("os", ""))
runner_arch = key_part(host.get("arch", ""))
if not runner_os or not runner_arch:
    raise SystemExit("Native inputs contain an invalid runner OS/architecture")
expected_key = f"android-native-v2-{runner_os}-{runner_arch}-{digest}"
if requested_key != expected_key:
    raise SystemExit("Requested cache key is not the exact native-input key")
if matched_key != requested_key:
    raise SystemExit("Matched cache key must equal the exact requested key")

cached_inputs_path = cache_path / "native-inputs.json"
if cached_inputs_path.is_symlink() or not cached_inputs_path.is_file():
    raise SystemExit("Cached native-inputs metadata must be a regular non-symlink file")
cached_inputs_bytes = cached_inputs_path.read_bytes()
if hashlib.sha256(cached_inputs_bytes).hexdigest() != digest:
    raise SystemExit("Cached native-input bytes do not match nativeInputDigest")
cached_inputs = json.loads(cached_inputs_bytes)
if canonical(cached_inputs) != canonical(inputs):
    raise SystemExit("Cached native inputs do not match requested inputs")

provenance_path = cache_path / "native-provenance.json"
if provenance_path.is_symlink() or not provenance_path.is_file():
    raise SystemExit("Cached native provenance metadata must be a regular non-symlink file")
provenance_bytes = provenance_path.read_bytes()
provenance = json.loads(provenance_bytes)
if provenance.get("schemaVersion") != 1:
    raise SystemExit("Unsupported native provenance schemaVersion")
if provenance.get("nativeInputDigest") != digest:
    raise SystemExit("nativeInputDigest mismatch")
if provenance.get("requestedCacheKey") != requested_key:
    raise SystemExit("Provenance requestedCacheKey mismatch")
if provenance.get("matchedCacheKey") != matched_key:
    raise SystemExit("Provenance matchedCacheKey mismatch")
if canonical(provenance.get("inputs")) != canonical(inputs):
    raise SystemExit("Provenance inputs do not match requested inputs")

api_level = inputs.get("androidApiLevel")
if api_level != 23:
    raise SystemExit(f"Android native cache must be built for API 23, got {api_level!r}")
if inputs.get("abis") != ["arm64-v8a", "x86_64"]:
    raise SystemExit("Android native cache ABI set mismatch")

bundle = cache_path / "bundle"
if bundle.is_symlink() or not bundle.is_dir():
    raise SystemExit("Native cache bundle must be a real non-symlink directory")
symlinks = sorted(
    candidate.relative_to(bundle).as_posix()
    for candidate in bundle.rglob("*")
    if candidate.is_symlink()
)
if symlinks:
    raise SystemExit(f"Native cache bundle must not contain symbolic links: {symlinks}")
non_regular = sorted(
    candidate.relative_to(bundle).as_posix()
    for candidate in bundle.rglob("*")
    if not candidate.is_dir() and not candidate.is_file()
)
if non_regular:
    raise SystemExit(f"Native cache bundle must contain only regular files and directories: {non_regular}")

sources = inputs.get("sources") or {}
openssl_version = str((sources.get("openssl") or {}).get("version", ""))
libssh2_version = str((sources.get("libssh2") or {}).get("version", ""))
upstream_native_package = sources.get("upstreamNativePackage") or {}
upstream_native_package_version = str(upstream_native_package.get("version", ""))
upstream_native_package_sha256 = str(upstream_native_package.get("sha256", ""))
native_package_version = str(inputs.get("nativePackageVersion", ""))
if (
    not openssl_version
    or not libssh2_version
    or not upstream_native_package_version
    or not re.fullmatch(r"[0-9a-f]{64}", upstream_native_package_sha256)
    or not native_package_version
):
    raise SystemExit("Native inputs are missing exact output version identities")
upstream_native_package_path = (
    f"nuget-local/LibGit2Sharp.NativeBinaries.{upstream_native_package_version}.nupkg"
)
custom_native_package_path = (
    f"nuget-local/LibGit2Sharp.NativeBinaries.{native_package_version}.nupkg"
)
if upstream_native_package_path == custom_native_package_path:
    raise SystemExit("Upstream and custom native package identities must be distinct")
required_paths = {
    upstream_native_package_path,
    custom_native_package_path,
    "android-native/libgit2-android-arm64/libgit2-3f4182d.so",
    "android-native/libgit2-android-x64/libgit2-3f4182d.so",
    f"android-native/openssl-{openssl_version}-android-arm64-prefix/lib/libssl.so.3",
    f"android-native/openssl-{openssl_version}-android-arm64-prefix/lib/libcrypto.so.3",
    f"android-native/openssl-{openssl_version}-android-x64-prefix/lib/libssl.so.3",
    f"android-native/openssl-{openssl_version}-android-x64-prefix/lib/libcrypto.so.3",
    f"android-native/libssh2-{libssh2_version}-android-arm64-prefix/lib/libssh2.so",
    f"android-native/libssh2-{libssh2_version}-android-x64-prefix/lib/libssh2.so",
}

declared_outputs = provenance.get("outputs")
if not isinstance(declared_outputs, list) or not declared_outputs:
    raise SystemExit("Native provenance outputs must be a non-empty array")

declared_by_path = {}
for entry in declared_outputs:
    relative = entry.get("path")
    if not isinstance(relative, str) or relative.startswith(("/", "../")) or "/../" in relative:
        raise SystemExit(f"Invalid provenance output path: {relative!r}")
    if relative in declared_by_path:
        raise SystemExit(f"Duplicate provenance output path: {relative}")
    declared_by_path[relative] = entry

actual_paths = {
    candidate.relative_to(bundle).as_posix()
    for candidate in bundle.rglob("*")
    if candidate.is_file()
}
actual_nuget_paths = {
    relative for relative in actual_paths if relative.startswith("nuget-local/")
}
expected_nuget_paths = {upstream_native_package_path, custom_native_package_path}
if actual_nuget_paths != expected_nuget_paths:
    raise SystemExit(
        "Native cache must contain exactly the pinned upstream and generated local packages; "
        f"actual package paths are {sorted(actual_nuget_paths)}"
    )
if actual_paths != set(declared_by_path):
    missing = sorted(set(declared_by_path) - actual_paths)
    unexpected = sorted(actual_paths - set(declared_by_path))
    raise SystemExit(f"Native cache output set mismatch; missing={missing}, unexpected={unexpected}")

missing_required = sorted(required_paths - actual_paths)
if missing_required:
    raise SystemExit(f"Native cache is missing exact required outputs: {missing_required}")

for relative, entry in declared_by_path.items():
    candidate = bundle / relative
    actual_size = candidate.stat().st_size
    actual_sha = hashlib.sha256(candidate.read_bytes()).hexdigest()
    if entry.get("size") != actual_size:
        raise SystemExit(f"Native cache size mismatch for {relative}")
    if entry.get("sha256") != actual_sha:
        raise SystemExit(f"Native cache SHA-256 mismatch for {relative}")

if declared_by_path[upstream_native_package_path].get("sha256") != upstream_native_package_sha256:
    raise SystemExit("Cached upstream LibGit2Sharp.NativeBinaries package does not match its pinned SHA-256")

closure = [
    {
        "path": relative,
        "size": int(declared_by_path[relative]["size"]),
        "sha256": str(declared_by_path[relative]["sha256"]),
    }
    for relative in sorted(declared_by_path)
]
output_closure_sha256 = hashlib.sha256(canonical(closure).encode("utf-8")).hexdigest()

evidence = {
    "schemaVersion": 1,
    "kind": "distribution-android-native-evidence",
    "outcome": "passed",
    "productionReady": False,
    "mode": "provenance",
    "rawTag": raw_tag,
    "normalizedVersion": version,
    "sourceSha": identity["sourceSha"],
    "workflowSha": identity["workflowSha"],
    "tagBinding": identity["tagBinding"],
    "manifestSha256": identity["manifestSha256"],
    "supportMatrixSha256": identity["supportMatrixSha256"],
    "signatureProfile": expected_signature_profile,
    "androidVersionCode": version_code,
    "androidVersionCodePolicy": version_policy,
    "nativeInputDigest": digest,
    "nativeInputsSha256": digest,
    "nativeProvenanceSha256": hashlib.sha256(provenance_bytes).hexdigest(),
    "requestedCacheKey": requested_key,
    "matchedCacheKey": matched_key,
    "cacheHit": cache_hit,
    "cacheSave": cache_save,
    "androidApiLevel": api_level,
    "outputCount": len(actual_paths),
    "outputClosureSha256": output_closure_sha256,
}
evidence_path.write_text(canonical(evidence) + "\n", encoding="utf-8")
PY
  exit 0
fi

[ -n "$IDENTITY_PATH" ] || fail "--identity is required in $MODE mode"
[ -n "$INPUT_DIR" ] || fail "--input-dir is required in $MODE mode"
IDENTITY_PATH="$(absolute_path "$IDENTITY_PATH")"
INPUT_DIR="$(absolute_path "$INPUT_DIR")"
require_file "$IDENTITY_PATH"
[ -d "$INPUT_DIR" ] || fail "Input directory not found: $INPUT_DIR"

identity_values="$(python3 - "$IDENTITY_PATH" "$SIGNATURE_PROFILE" <<'PY'
import json
import re
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    identity = json.load(stream)
requested_profile = sys.argv[2]

version = str(identity.get("normalizedVersion", ""))
if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
    raise SystemExit("Invalid normalizedVersion")
raw_tag = str(identity.get("rawTag", ""))
if raw_tag not in (version, "v" + version):
    raise SystemExit("rawTag does not normalize to normalizedVersion")
for field in ("sourceSha", "workflowSha"):
    if not re.fullmatch(r"[0-9a-f]{40}", str(identity.get(field, ""))):
        raise SystemExit(f"Invalid {field}")
for field in ("manifestSha256", "supportMatrixSha256"):
    if not re.fullmatch(r"[0-9a-f]{64}", str(identity.get(field, ""))):
        raise SystemExit(f"Invalid {field}")
code = identity.get("androidVersionCode")
if not isinstance(code, int) or not 1 <= code <= 2_100_000_000:
    raise SystemExit("Invalid androidVersionCode")
policy = identity.get("androidVersionCodePolicy")
if policy == "ci-test":
    profile = "test"
    if identity.get("tagBinding") != "notApplicable":
        raise SystemExit("ci-test identity requires tagBinding notApplicable")
elif policy == "production-monotonic":
    profile = "production"
    if identity.get("tagBinding") != "required":
        raise SystemExit("production-monotonic identity requires tagBinding required")
    last = identity.get("lastPublishedAndroidVersionCode")
    if not isinstance(last, int) or code <= last:
        raise SystemExit("Non-monotonic production androidVersionCode")
else:
    raise SystemExit("Invalid androidVersionCodePolicy")
if requested_profile and requested_profile != profile:
    raise SystemExit("signatureProfile does not match androidVersionCodePolicy")
if identity.get("signatureProfile") != profile:
    raise SystemExit("Identity signatureProfile does not match androidVersionCodePolicy")
android_plan = identity.get("filenamePlan", {}).get("android", {})
arm64_name = android_plan.get("arm64Apk")
x64_name = android_plan.get("x64Apk")
for field, value in (("arm64Apk", arm64_name), ("x64Apk", x64_name)):
    if not isinstance(value, str) or not value.endswith(".apk") or "/" in value or "\\" in value:
        raise SystemExit(f"Invalid filenamePlan.android.{field}")
    if version not in value:
        raise SystemExit(f"filenamePlan.android.{field} must contain normalizedVersion")
if arm64_name.casefold() == x64_name.casefold():
    raise SystemExit("Android APK filenames must be unique case-insensitively")
if raw_tag.startswith("v") and any(raw_tag in value for value in (arm64_name, x64_name)):
    raise SystemExit("Android APK filenames must not contain the raw v-prefixed tag")
print("\t".join((version, str(code), profile, str(identity["sourceSha"]), str(identity["workflowSha"]), raw_tag, str(identity.get("tagBinding", "")), arm64_name, x64_name)))
PY
)"
IFS=$'\t' read -r NORMALIZED_VERSION ANDROID_VERSION_CODE SIGNATURE_PROFILE SOURCE_SHA WORKFLOW_SHA RAW_TAG TAG_BINDING ARM64_APK_NAME X64_APK_NAME <<< "$identity_values"

find_build_tool() {
  local name="$1"
  local build_tools_root="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}/build-tools"
  if [ -n "${ANDROID_BUILD_TOOLS:-}" ] && [ -x "$build_tools_root/$ANDROID_BUILD_TOOLS/$name" ]; then
    printf '%s\n' "$build_tools_root/$ANDROID_BUILD_TOOLS/$name"
    return
  fi
  if [ -d "$build_tools_root" ]; then
    local candidate
    candidate="$(find "$build_tools_root" -mindepth 2 -maxdepth 2 -type f -name "$name" -perm -u+x | sort -V | tail -n 1)"
    if [ -n "$candidate" ]; then
      printf '%s\n' "$candidate"
      return
    fi
  fi
  command -v "$name" 2>/dev/null || true
}

if [ "$MODE" = "artifact" ]; then
  require_command unzip
  AAPT="$(find_build_tool aapt)"
  ZIPALIGN="$(find_build_tool zipalign)"
  APKSIGNER="$(find_build_tool apksigner)"
  [ -x "$AAPT" ] || fail "aapt not found in Android build-tools"
  [ -x "$ZIPALIGN" ] || fail "zipalign not found in Android build-tools"
  [ -x "$APKSIGNER" ] || fail "apksigner not found in Android build-tools"

  READELF=""
  if [ -n "${ANDROID_NDK_ROOT:-}" ]; then
    READELF="$(find "$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt" -type f -name llvm-readelf -perm -u+x | head -n 1)"
  fi
  if [ -z "$READELF" ]; then
    READELF="$(command -v llvm-readelf 2>/dev/null || command -v readelf 2>/dev/null || true)"
  fi
  [ -x "$READELF" ] || fail "llvm-readelf/readelf not found"

  mapfile -t actual_apks < <(find "$INPUT_DIR" -maxdepth 1 -type f -name '*.apk' -printf '%f\n' | sort)
  expected_arm64="$ARM64_APK_NAME"
  expected_x64="$X64_APK_NAME"
  mapfile -t expected_apks < <(printf '%s\n' "$expected_arm64" "$expected_x64" | sort)
  if [ "${#actual_apks[@]}" -ne 2 ] || ! diff -u <(printf '%s\n' "${expected_apks[@]}") <(printf '%s\n' "${actual_apks[@]}"); then
    fail "Android candidate directory must contain exactly the two normalized APK filenames"
  fi

  TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/unlimotion-android-artifact.XXXXXX")"
  trap 'rm -rf "$TMP_DIR"' EXIT
  REPORT_TSV="$TMP_DIR/assets.tsv"
  : > "$REPORT_TSV"

  for rid in android-arm64 android-x64; do
    case "$rid" in
      android-arm64) abi="arm64-v8a" ;;
      android-x64) abi="x86_64" ;;
      *) fail "Unexpected RID: $rid" ;;
    esac
    case "$rid" in
      android-arm64) apk_name="$ARM64_APK_NAME" ;;
      android-x64) apk_name="$X64_APK_NAME" ;;
      *) fail "Unexpected RID: $rid" ;;
    esac
    apk_path="$INPUT_DIR/$apk_name"
    require_file "$apk_path"
    before_sha="$(sha256_file "$apk_path")"
    size="$(python3 -c 'import os,sys; print(os.path.getsize(sys.argv[1]))' "$apk_path")"

    badging="$TMP_DIR/$rid.badging.txt"
    "$AAPT" dump badging "$apk_path" > "$badging"
    package_line="$(grep -m1 '^package:' "$badging")"
    application_id="$(sed -n "s/.* name='\([^']*\)'.*/\1/p" <<< "$package_line")"
    version_code="$(sed -n "s/.* versionCode='\([^']*\)'.*/\1/p" <<< "$package_line")"
    version_name="$(sed -n "s/.* versionName='\([^']*\)'.*/\1/p" <<< "$package_line")"
    min_sdk="$(sed -n "s/^sdkVersion:'\([^']*\)'.*/\1/p" "$badging")"
    target_sdk="$(sed -n "s/^targetSdkVersion:'\([^']*\)'.*/\1/p" "$badging")"
    [ "$application_id" = "$EXPECTED_APPLICATION_ID" ] || fail "$apk_name application id mismatch: $application_id"
    [ "$version_code" = "$ANDROID_VERSION_CODE" ] || fail "$apk_name versionCode mismatch: $version_code"
    [ "$version_name" = "$NORMALIZED_VERSION" ] || fail "$apk_name versionName mismatch: $version_name"
    [ "$min_sdk" = "$EXPECTED_MIN_SDK" ] || fail "$apk_name minSdk mismatch: $min_sdk"
    [ "$target_sdk" = "$EXPECTED_TARGET_SDK" ] || fail "$apk_name targetSdk mismatch: $target_sdk"

    mapfile -t packaged_abis < <(unzip -Z1 "$apk_path" | sed -n 's#^lib/\([^/]*\)/.*#\1#p' | sort -u)
    [ "${#packaged_abis[@]}" -eq 1 ] && [ "${packaged_abis[0]}" = "$abi" ] || fail "$apk_name must contain only ABI $abi"
    for native_library in \
      libgit2-3f4182d.so \
      libssl.so \
      libssl.so.3 \
      libcrypto.so \
      libcrypto.so.3 \
      libssh2.so \
      libmonodroid.so \
      libxamarin-app.so; do
      unzip -Z1 "$apk_path" | grep -Fx "lib/$abi/$native_library" >/dev/null || fail "$apk_name is missing lib/$abi/$native_library"
    done

    unzip -p "$apk_path" "lib/$abi/libmonodroid.so" > "$TMP_DIR/$rid-libmonodroid.so"
    unzip -p "$apk_path" "lib/$abi/libxamarin-app.so" > "$TMP_DIR/$rid-libxamarin-app.so"
    "$READELF" --dyn-syms "$TMP_DIR/$rid-libmonodroid.so" > "$TMP_DIR/$rid-libmonodroid.symbols"
    "$READELF" --dyn-syms "$TMP_DIR/$rid-libxamarin-app.so" > "$TMP_DIR/$rid-libxamarin-app.symbols"
    for symbol in compressed_assembly_count compressed_assembly_descriptors uncompressed_assemblies_data_size uncompressed_assemblies_data_buffer; do
      if grep -Eq "[[:space:]]UND[[:space:]]+${symbol}$" "$TMP_DIR/$rid-libmonodroid.symbols" &&
         ! grep -Eq "[[:space:]]GLOBAL[[:space:]]+DEFAULT[[:space:]]+[0-9]+[[:space:]]+${symbol}$" "$TMP_DIR/$rid-libxamarin-app.symbols"; then
        fail "$apk_name is missing required runtime symbol $symbol"
      fi
    done

    "$ZIPALIGN" -c -P 16 4 "$apk_path" >/dev/null
    signature_report="$TMP_DIR/$rid-signature.txt"
    "$APKSIGNER" verify --verbose --print-certs "$apk_path" > "$signature_report"
    mapfile -t fingerprints < <(sed -n 's/^Signer #[0-9][0-9]* certificate SHA-256 digest: //p' "$signature_report" | tr -d ':' | tr '[:upper:]' '[:lower:]')
    [ "${#fingerprints[@]}" -ge 1 ] || fail "$apk_name has no signer fingerprint"
    fingerprint="${fingerprints[0]}"
    signer_count="${#fingerprints[@]}"
    for candidate_fingerprint in "${fingerprints[@]}"; do
      [ "$candidate_fingerprint" = "$fingerprint" ] || fail "$apk_name contains different signer fingerprints"
    done
    if [ "$SIGNATURE_PROFILE" = "production" ]; then
      [ "$fingerprint" = "$EXPECTED_PRODUCTION_FINGERPRINT" ] || fail "$apk_name production certificate fingerprint mismatch"
    else
      [ "$fingerprint" != "$EXPECTED_PRODUCTION_FINGERPRINT" ] || fail "$apk_name test profile unexpectedly uses the production certificate"
    fi

    after_sha="$(sha256_file "$apk_path")"
    [ "$after_sha" = "$before_sha" ] || fail "$apk_name changed during validation"
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
      "$rid" "$abi" "$apk_name" "$size" "$before_sha" "$after_sha" "$fingerprint" "$signer_count" "$min_sdk" "$target_sdk" >> "$REPORT_TSV"
  done

  python3 - \
    "$IDENTITY_PATH" \
    "$REPORT_TSV" \
    "$EVIDENCE_PATH" \
    "$SIGNATURE_PROFILE" \
    "$EXPECTED_APPLICATION_ID" <<'PY'
import json
import pathlib
import platform
import sys

identity_path = pathlib.Path(sys.argv[1])
report_path = pathlib.Path(sys.argv[2])
evidence_path = pathlib.Path(sys.argv[3])
signature_profile = sys.argv[4]
application_id = sys.argv[5]
identity = json.loads(identity_path.read_text(encoding="utf-8"))
assets = []
for line in report_path.read_text(encoding="utf-8").splitlines():
    rid, abi, name, size, before_sha, after_sha, fingerprint, signer_count, min_sdk, target_sdk = line.split("\t")
    assets.append(
        {
            "assetId": f"android-{rid.removeprefix('android-')}-apk",
            "name": name,
            "rid": rid,
            "architecture": abi,
            "size": int(size),
            "sha256Before": before_sha,
            "sha256After": after_sha,
            "applicationId": application_id,
            "versionName": identity["normalizedVersion"],
            "versionCode": identity["androidVersionCode"],
            "minSdk": int(min_sdk),
            "targetSdk": int(target_sdk),
            "signatureProfile": signature_profile,
            "signatureFingerprintSha256": fingerprint,
            "signerCount": int(signer_count),
            "zipAligned": True,
            "nativeSymbolsVerified": True,
        }
    )
if signature_profile == "production":
    signer_pairs = {(entry["signatureFingerprintSha256"], entry["signerCount"]) for entry in assets}
    if len(signer_pairs) != 1:
        raise SystemExit("Production APKs must have identical signer fingerprints/counts")

payload = {
    "schemaVersion": 1,
    "kind": "distribution-android-native-evidence",
    "outcome": "passed",
    "supportLevel": "metadataVerified",
    "productionReady": False,
    "mode": "artifact",
    "rawTag": identity["rawTag"],
    "normalizedVersion": identity["normalizedVersion"],
    "sourceSha": identity["sourceSha"],
    "workflowSha": identity["workflowSha"],
    "tagBinding": identity["tagBinding"],
    "manifestSha256": identity["manifestSha256"],
    "supportMatrixSha256": identity["supportMatrixSha256"],
    "androidVersionCode": identity["androidVersionCode"],
    "androidVersionCodePolicy": identity["androidVersionCodePolicy"],
    "signatureProfile": signature_profile,
    "runner": {"os": platform.system(), "architecture": platform.machine()},
    "assets": assets,
    "arm64LaunchVerified": False,
    "arm64LaunchReason": "No native arm64 device was used by artifact validation",
}
evidence_path.write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY
  exit 0
fi

case "$API_LEVEL" in
  23|36) ;;
  *) fail "--api-level must be 23 or 36 in emulator mode" ;;
esac

EMULATOR_BOOT_TIMEOUT_SECONDS="${UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS:-300}"
EMULATOR_BOOT_POLL_SECONDS="${UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS:-5}"
EMULATOR_COMMAND_TIMEOUT_SECONDS="${UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS:-30}"
EMULATOR_INSTALL_TIMEOUT_SECONDS="${UNLIMOTION_ANDROID_EMULATOR_INSTALL_TIMEOUT_SECONDS:-120}"
[[ "$EMULATOR_BOOT_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "Emulator boot timeout must be a positive integer"
[[ "$EMULATOR_BOOT_POLL_SECONDS" =~ ^(0\.[0-9]+|[1-9][0-9]*(\.[0-9]+)?)$ ]] || fail "Emulator boot poll interval must be positive"
[[ "$EMULATOR_COMMAND_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "Emulator command timeout must be a positive integer"
[[ "$EMULATOR_INSTALL_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "Emulator install timeout must be a positive integer"

require_command adb
require_command emulator
require_command sdkmanager
require_command avdmanager
require_command timeout
run_adb_command() {
  local status
  if [ -n "${CURRENT_EMULATOR_LOG:-}" ] && [ "$EMULATOR_EVIDENCE_FINALIZED" != "true" ]; then
    {
      printf 'adb command:'
      printf ' %q' "$@"
      printf '\n'
    } >>"$CURRENT_EMULATOR_LOG"
  fi
  if timeout --foreground "$EMULATOR_COMMAND_TIMEOUT_SECONDS" adb "$@"; then
    status=0
  else
    status=$?
  fi
  if [ -n "${CURRENT_EMULATOR_LOG:-}" ] && [ "$EMULATOR_EVIDENCE_FINALIZED" != "true" ]; then
    {
      printf 'adb command result: exit=%s' "$status"
      printf ' %q' "$@"
      printf '\n'
    } >>"$CURRENT_EMULATOR_LOG"
  fi
  return "$status"
}

run_adb_install_command() {
  EMULATOR_COMMAND_TIMEOUT_SECONDS="$EMULATOR_INSTALL_TIMEOUT_SECONDS" run_adb_command "$@"
}

run_avdmanager_command() {
  timeout --foreground "$EMULATOR_COMMAND_TIMEOUT_SECONDS" avdmanager "$@"
}

AAPT="$(find_build_tool aapt)"
[ -x "$AAPT" ] || fail "aapt not found in Android build-tools"

APK_NAME="$X64_APK_NAME"
APK_PATH="$INPUT_DIR/$APK_NAME"
require_file "$APK_PATH"
APK_SHA_BEFORE="$(sha256_file "$APK_PATH")"
BADGING_FILE="$(mktemp "${TMPDIR:-/tmp}/unlimotion-android-badging.XXXXXX")"
"$AAPT" dump badging "$APK_PATH" > "$BADGING_FILE"
LAUNCHABLE_ACTIVITY="$(sed -n "s/^launchable-activity: name='\([^']*\)'.*/\1/p" "$BADGING_FILE" | head -n 1)"
rm -f "$BADGING_FILE"
[ -n "$LAUNCHABLE_ACTIVITY" ] || fail "Unable to resolve launchable Android activity"

SYSTEM_IMAGE="system-images;android-${API_LEVEL};google_apis;x86_64"
SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
[ -n "$SDK_ROOT" ] || fail "ANDROID_SDK_ROOT or ANDROID_HOME is required"
if [ ! -d "$SDK_ROOT/system-images/android-${API_LEVEL}/google_apis/x86_64" ]; then
  yes | sdkmanager --licenses >/dev/null
  sdkmanager --install "platform-tools" "platforms;android-${API_LEVEL}" "$SYSTEM_IMAGE"
fi
SYSTEM_IMAGE_DIR="$SDK_ROOT/system-images/android-${API_LEVEL}/google_apis/x86_64"
SYSTEM_IMAGE_PACKAGE_XML="$SYSTEM_IMAGE_DIR/package.xml"
require_file "$SYSTEM_IMAGE_PACKAGE_XML"
SYSTEM_IMAGE_REVISION="$(python3 - "$SYSTEM_IMAGE_PACKAGE_XML" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

root = ET.fromstring(pathlib.Path(sys.argv[1]).read_bytes())
revision = root.find(".//revision")
if revision is None:
    raise SystemExit("System-image package.xml does not contain a revision")
parts = []
for name in ("major", "minor", "micro"):
    value = revision.findtext(name)
    if value is not None:
        if not value.isdigit():
            raise SystemExit(f"Invalid system-image revision {name}: {value!r}")
        parts.append(str(int(value)))
if not parts:
    raise SystemExit("System-image package.xml revision is empty")
print(".".join(parts))
PY
)"
EMULATOR_VERSION="$(resolve_tool_version "Android emulator" emulator -version)"
ADB_VERSION="$(resolve_tool_version "adb" run_adb_command version)"
AAPT_VERSION="$(resolve_tool_version "aapt" "$AAPT" version)"
RUNNER_IMAGE_OS="${ImageOS:-local-$(uname -s)}"
RUNNER_IMAGE_VERSION="${ImageVersion:-notApplicable-local}"
RUNNER_UNAME="$(uname -a)"

EMULATOR_PID=""
AVD_NAME=""
SERIAL=""
BOOT_ATTEMPTS=0
BOOT_SUCCEEDED="false"
BOOT_OUTCOMES=()
EMULATOR_ATTEMPT_LOGS=()
CURRENT_EMULATOR_LOG=""
EMULATOR_LOG="$(dirname "$EVIDENCE_PATH")/android-api${API_LEVEL}-emulator.log"
EMULATOR_AVD_ROOT="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/unlimotion-android-avd.XXXXXX")"
export ANDROID_AVD_HOME="$EMULATOR_AVD_ROOT"
cleanup_emulator() {
  if [ -n "$SERIAL" ]; then
    run_adb_command -s "$SERIAL" emu kill >/dev/null 2>&1 || true
  fi
  if [ -n "$EMULATOR_PID" ]; then
    if kill -0 "$EMULATOR_PID" >/dev/null 2>&1; then
      kill "$EMULATOR_PID" >/dev/null 2>&1 || true
      cleanup_deadline=$((SECONDS + EMULATOR_COMMAND_TIMEOUT_SECONDS))
      while kill -0 "$EMULATOR_PID" >/dev/null 2>&1 && [ "$SECONDS" -lt "$cleanup_deadline" ]; do
        sleep 1
      done
      if kill -0 "$EMULATOR_PID" >/dev/null 2>&1; then
        kill -9 "$EMULATOR_PID" >/dev/null 2>&1 || true
      fi
    fi
    wait "$EMULATOR_PID" >/dev/null 2>&1 || true
  fi
  if [ -n "$AVD_NAME" ]; then
    run_avdmanager_command delete avd -n "$AVD_NAME" >/dev/null 2>&1 || true
  fi
}

cleanup_emulator_avd_root() {
  if [ -n "${EMULATOR_AVD_ROOT:-}" ] && [ -d "$EMULATOR_AVD_ROOT" ]; then
    rm -rf -- "$EMULATOR_AVD_ROOT"
  fi
}

write_emulator_failure_evidence() {
  local terminal_error="$1"
  local boot_outcomes_csv
  cleanup_emulator
  while [ "${#BOOT_OUTCOMES[@]}" -lt "$BOOT_ATTEMPTS" ]; do
    BOOT_OUTCOMES+=("failure")
  done
  boot_outcomes_csv="$(IFS=,; printf '%s' "${BOOT_OUTCOMES[*]}")"
  python3 - \
    "$IDENTITY_PATH" \
    "$EVIDENCE_PATH" \
    "$API_LEVEL" \
    "$BOOT_ATTEMPTS" \
    "$boot_outcomes_csv" \
    "$terminal_error" \
    "$APK_NAME" \
    "$APK_SHA_BEFORE" \
    "${EMULATOR_ATTEMPT_LOGS[@]}" <<'PY'
from datetime import datetime, timezone
import hashlib
import json
import pathlib
import sys

identity_path = pathlib.Path(sys.argv[1])
evidence_path = pathlib.Path(sys.argv[2])
api_level = int(sys.argv[3])
attempts = int(sys.argv[4])
outcomes = [entry for entry in sys.argv[5].split(",") if entry]
terminal_error = sys.argv[6]
apk_name = sys.argv[7]
apk_sha256 = sys.argv[8]
attempt_log_paths = [pathlib.Path(value) for value in sys.argv[9:]]
identity = json.loads(identity_path.read_text(encoding="utf-8"))
attempt_logs = []
for attempt, path in enumerate(attempt_log_paths, start=1):
    if not path.is_file():
        continue
    content = path.read_bytes()
    attempt_logs.append(
        {
            "attempt": attempt,
            "fileName": path.name,
            "sha256": hashlib.sha256(content).hexdigest(),
            "bytes": len(content),
        }
    )
retried = attempts >= 2
exhausted = attempts == 2 and outcomes == ["failure", "failure"]
failure_classification = (
    "deterministic-post-boot" if outcomes and outcomes[-1] == "success"
    else "transient-emulator-boot"
)
payload = {
    "schemaVersion": 1,
    "kind": "distribution-android-native-evidence",
    "outcome": "failed",
    "supportLevel": "launchNotVerified",
    "productionReady": False,
    "mode": "emulator",
    "rawTag": identity["rawTag"],
    "normalizedVersion": identity["normalizedVersion"],
    "sourceSha": identity["sourceSha"],
    "workflowSha": identity["workflowSha"],
    "tagBinding": identity["tagBinding"],
    "manifestSha256": identity["manifestSha256"],
    "supportMatrixSha256": identity["supportMatrixSha256"],
    "androidVersionCode": identity["androidVersionCode"],
    "androidVersionCodePolicy": identity["androidVersionCodePolicy"],
    "signatureProfile": identity["signatureProfile"],
    "recordedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "asset": {
        "name": apk_name,
        "architecture": "x86_64",
        "sha256Before": apk_sha256,
    },
    "runtime": {
        "apiLevel": api_level,
        "maxBootAttempts": 2,
    },
    "bootRetry": {
        "rule": "bounded-clean-retry",
        "classification": "transient-emulator-boot" if retried else "none",
        "attempts": attempts,
        "maxAttempts": 2,
        "cleanupBeforeAttempt2": "kill-delete-avd-remove-files-and-wipe-data" if retried else "notRequired",
        "outcomes": outcomes,
        "exhausted": exhausted,
    },
    "attemptLogs": attempt_logs,
    "failureClassification": failure_classification,
    "terminalError": terminal_error,
}
evidence_path.write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY
  EMULATOR_EVIDENCE_FINALIZED="true"
}

handle_emulator_error() {
  local status="$1"
  local line="$2"
  trap - ERR
  fail "Unexpected emulator validation command failure at line $line (exit $status)"
}

trap 'cleanup_emulator; cleanup_emulator_avd_root' EXIT
[ -d "$ANDROID_AVD_HOME" ] || fail "Unable to create isolated Android AVD root"
EMULATOR_FAILURE_EVIDENCE_ENABLED="true"
trap 'handle_emulator_error "$?" "$LINENO"' ERR
for port in 5554 5556; do
  BOOT_ATTEMPTS=$((BOOT_ATTEMPTS + 1))
  cleanup_emulator
  EMULATOR_PID=""
  SERIAL="emulator-$port"
  AVD_NAME="unlimotion-api${API_LEVEL}-${GITHUB_RUN_ID:-local}-${BOOT_ATTEMPTS}"
  CURRENT_EMULATOR_LOG="$(dirname "$EVIDENCE_PATH")/android-api${API_LEVEL}-emulator-attempt${BOOT_ATTEMPTS}.log"
  EMULATOR_ATTEMPT_LOGS+=("$CURRENT_EMULATOR_LOG")
  : > "$CURRENT_EMULATOR_LOG"
  rm -rf -- "$ANDROID_AVD_HOME/${AVD_NAME}.avd" "$ANDROID_AVD_HOME/${AVD_NAME}.ini" >>"$CURRENT_EMULATOR_LOG" 2>&1
  printf 'no\n' | run_avdmanager_command create avd --force --name "$AVD_NAME" --package "$SYSTEM_IMAGE" --device pixel >>"$CURRENT_EMULATOR_LOG" 2>&1
  [ -f "$ANDROID_AVD_HOME/${AVD_NAME}.ini" ] || fail "Android AVD descriptor was not created for $AVD_NAME"
  [ -d "$ANDROID_AVD_HOME/${AVD_NAME}.avd" ] || fail "Android AVD directory was not created for $AVD_NAME"
  emulator \
    -avd "$AVD_NAME" \
    -port "$port" \
    -no-window \
    -no-audio \
    -no-boot-anim \
    -no-snapshot \
    -wipe-data \
    -gpu swiftshader_indirect >>"$CURRENT_EMULATOR_LOG" 2>&1 &
  EMULATOR_PID=$!

  deadline=$((SECONDS + EMULATOR_BOOT_TIMEOUT_SECONDS))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if ! kill -0 "$EMULATOR_PID" >/dev/null 2>&1; then
      break
    fi
    adb_state="$(run_adb_command -s "$SERIAL" get-state 2>&1 | tr -d '\r' || true)"
    boot_completed="$(run_adb_command -s "$SERIAL" shell getprop sys.boot_completed 2>&1 | tr -d '\r' || true)"
    boot_animation="$(run_adb_command -s "$SERIAL" shell getprop init.svc.bootanim 2>&1 | tr -d '\r' || true)"
    printf 'readiness poll: serial=%q adb_state=%q sys.boot_completed=%q init.svc.bootanim=%q\n' \
      "$SERIAL" "$adb_state" "$boot_completed" "$boot_animation" >>"$CURRENT_EMULATOR_LOG"
    if [ "$boot_completed" = "1" ]; then
      BOOT_SUCCEEDED="true"
      BOOT_OUTCOMES+=("success")
      break
    fi
    sleep "$EMULATOR_BOOT_POLL_SECONDS"
  done
  if [ "$BOOT_SUCCEEDED" = "true" ]; then
    break
  fi
  BOOT_OUTCOMES+=("failure")
done

[ "$BOOT_SUCCEEDED" = "true" ] || fail "Android API $API_LEVEL emulator failed to boot after two clean attempts"
BOOT_OUTCOMES_CSV="$(IFS=,; printf '%s' "${BOOT_OUTCOMES[*]}")"
DEVICE_FINGERPRINT="$(run_adb_command -s "$SERIAL" shell getprop ro.build.fingerprint 2>/dev/null | tr -d '\r')"
DEVICE_SDK="$(run_adb_command -s "$SERIAL" shell getprop ro.build.version.sdk 2>/dev/null | tr -d '\r')"
[ -n "$DEVICE_FINGERPRINT" ] || fail "Android API $API_LEVEL emulator did not report ro.build.fingerprint"
[ "$DEVICE_SDK" = "$API_LEVEL" ] || fail "Android emulator SDK mismatch: expected $API_LEVEL, got $DEVICE_SDK"

run_adb_command -s "$SERIAL" logcat -c
run_adb_install_command -s "$SERIAL" install -r "$APK_PATH" >/dev/null
run_adb_command -s "$SERIAL" shell am force-stop "$EXPECTED_APPLICATION_ID"
run_adb_command -s "$SERIAL" shell am start -n "$EXPECTED_APPLICATION_ID/$LAUNCHABLE_ACTIVITY" >/dev/null
sleep 15

PROCESS_ID="$(run_adb_command -s "$SERIAL" shell pidof "$EXPECTED_APPLICATION_ID" 2>/dev/null | tr -d '\r' | awk '{print $1}' || true)"
if [ -z "$PROCESS_ID" ]; then
  PROCESS_ID="$(run_adb_command -s "$SERIAL" shell ps 2>/dev/null | tr -d '\r' | awk -v package="$EXPECTED_APPLICATION_ID" '$NF == package {print $2; exit}' || true)"
fi
[ -n "$PROCESS_ID" ] || fail "Android application process is not running on API $API_LEVEL"

LOGCAT_FILE="$(dirname "$EVIDENCE_PATH")/android-api${API_LEVEL}-logcat.txt"
run_adb_command -s "$SERIAL" logcat -d > "$LOGCAT_FILE"
if grep -Eiq 'FATAL EXCEPTION|AndroidRuntime.*FATAL|UnsatisfiedLinkError|NoClassDefFoundError|Fatal signal|SIGABRT' "$LOGCAT_FILE"; then
  fail "Fatal Android runtime entry found in API $API_LEVEL logcat"
fi

APK_SHA_AFTER="$(sha256_file "$APK_PATH")"
[ "$APK_SHA_AFTER" = "$APK_SHA_BEFORE" ] || fail "APK changed during API $API_LEVEL emulator validation"

# Freeze the emulator output before hashing the log payloads referenced by evidence.
cleanup_emulator
cp -- "$CURRENT_EMULATOR_LOG" "$EMULATOR_LOG"
[ -s "$LOGCAT_FILE" ] || fail "Android API $API_LEVEL logcat payload is empty"
[ -s "$EMULATOR_LOG" ] || fail "Android API $API_LEVEL emulator log payload is empty"

python3 - \
  "$IDENTITY_PATH" \
  "$EVIDENCE_PATH" \
  "$APK_NAME" \
  "$APK_SHA_BEFORE" \
  "$APK_SHA_AFTER" \
  "$API_LEVEL" \
  "$BOOT_ATTEMPTS" \
  "$SERIAL" \
  "$PROCESS_ID" \
  "$LAUNCHABLE_ACTIVITY" \
  "$LOGCAT_FILE" \
  "$EMULATOR_LOG" \
  "$DEVICE_FINGERPRINT" \
  "$DEVICE_SDK" \
  "$SYSTEM_IMAGE" \
  "$SYSTEM_IMAGE_REVISION" \
  "$EMULATOR_VERSION" \
  "$ADB_VERSION" \
  "$AAPT_VERSION" \
  "$RUNNER_IMAGE_OS" \
  "$RUNNER_IMAGE_VERSION" \
  "$RUNNER_UNAME" \
  "$BOOT_OUTCOMES_CSV" <<'PY'
from datetime import datetime, timezone
import hashlib
import json
import pathlib
import sys

(
    identity_text,
    evidence_text,
    apk_name,
    before_sha,
    after_sha,
    api_level,
    boot_attempts,
    serial,
    process_id,
    activity,
    logcat_path,
    emulator_log_path,
    device_fingerprint,
    device_sdk,
    system_image_package,
    system_image_revision,
    emulator_version,
    adb_version,
    aapt_version,
    runner_image_os,
    runner_image_version,
    runner_uname,
    boot_outcomes_csv,
) = sys.argv[1:]
identity = json.loads(pathlib.Path(identity_text).read_text(encoding="utf-8"))

def file_reference(path_text):
    path = pathlib.Path(path_text)
    content = path.read_bytes()
    return {
        "fileName": path.name,
        "sha256": hashlib.sha256(content).hexdigest(),
        "bytes": len(content),
    }

payload = {
    "schemaVersion": 1,
    "kind": "distribution-android-native-evidence",
    "outcome": "passed",
    "supportLevel": "launchVerified",
    "productionReady": False,
    "mode": "emulator",
    "rawTag": identity["rawTag"],
    "normalizedVersion": identity["normalizedVersion"],
    "sourceSha": identity["sourceSha"],
    "workflowSha": identity["workflowSha"],
    "tagBinding": identity["tagBinding"],
    "manifestSha256": identity["manifestSha256"],
    "supportMatrixSha256": identity["supportMatrixSha256"],
    "androidVersionCode": identity["androidVersionCode"],
    "androidVersionCodePolicy": identity["androidVersionCodePolicy"],
    "signatureProfile": identity["signatureProfile"],
    "recordedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "asset": {
        "name": apk_name,
        "architecture": "x86_64",
        "sha256Before": before_sha,
        "sha256After": after_sha,
    },
    "runtime": {
        "apiLevel": int(api_level),
        "bootAttempts": int(boot_attempts),
        "maxBootAttempts": 2,
        "deviceFingerprint": device_fingerprint,
        "deviceSdk": int(device_sdk),
        "systemImagePackage": system_image_package,
        "systemImageRevision": system_image_revision,
        "serial": serial,
        "applicationId": "com.Kibnet.Unlimotion",
        "activity": activity,
        "processId": process_id,
        "fatalLogcatEntries": 0,
        "logcat": file_reference(logcat_path),
        "emulatorLog": file_reference(emulator_log_path),
    },
    "tools": {
        "emulatorVersion": emulator_version,
        "adbVersion": adb_version,
        "aaptVersion": aapt_version,
    },
    "runner": {
        "imageOs": runner_image_os,
        "imageVersion": runner_image_version,
        "uname": runner_uname,
    },
    "bootRetry": {
        "rule": "bounded-clean-retry",
        "classification": "none" if int(boot_attempts) == 1 else "transient-emulator-boot",
        "attempts": int(boot_attempts),
        "maxAttempts": 2,
        "cleanupBeforeAttempt2": "notRequired" if int(boot_attempts) == 1 else "kill-delete-avd-remove-files-and-wipe-data",
        "outcomes": boot_outcomes_csv.split(","),
        "exhausted": False,
    },
}
pathlib.Path(evidence_text).write_text(
    json.dumps(payload, sort_keys=True, separators=(",", ":")) + "\n",
    encoding="utf-8",
)
PY
rm -f -- "${EMULATOR_ATTEMPT_LOGS[@]}"
EMULATOR_EVIDENCE_FINALIZED="true"
trap - ERR
