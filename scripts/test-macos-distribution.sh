#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

identity=""
architecture=""
artifact_directory=""
output_evidence=""
launch_timeout_seconds=30
expected_runner=""

usage() {
  printf 'Usage: %s --identity <identity.json> --architecture <x64|arm64> --artifact-dir <dir> --expected-runner <macos-15-intel|macos-15> [--evidence <json>] [--launch-timeout <seconds>]\n' "$0" >&2
}

while (($#)); do
  case "$1" in
    --identity) identity="${2:?missing value for --identity}"; shift 2 ;;
    --architecture) architecture="${2:?missing value for --architecture}"; shift 2 ;;
    --artifact-dir) artifact_directory="${2:?missing value for --artifact-dir}"; shift 2 ;;
    --expected-runner) expected_runner="${2:?missing value for --expected-runner}"; shift 2 ;;
    --evidence) output_evidence="${2:?missing value for --evidence}"; shift 2 ;;
    --launch-timeout) launch_timeout_seconds="${2:?missing value for --launch-timeout}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage; exit 2 ;;
  esac
done

[[ "$(uname -s)" == "Darwin" ]] || { printf 'This validator must run on macOS.\n' >&2; exit 1; }
[[ -f "$identity" ]] || { printf 'A readable --identity file is required.\n' >&2; exit 2; }
[[ -d "$artifact_directory" ]] || { printf 'A readable --artifact-dir is required.\n' >&2; exit 2; }
[[ "$architecture" == "x64" || "$architecture" == "arm64" ]] || { printf '%s\n' '--architecture must be x64 or arm64.' >&2; exit 2; }
[[ "$expected_runner" == "macos-15-intel" || "$expected_runner" == "macos-15" ]] || { printf '%s\n' '--expected-runner must be macos-15-intel or macos-15.' >&2; exit 2; }
[[ "$launch_timeout_seconds" =~ ^[0-9]+$ && "$launch_timeout_seconds" -ge 5 && "$launch_timeout_seconds" -le 120 ]] || { printf 'Launch timeout must be 5..120 seconds.\n' >&2; exit 2; }

for command_name in jq plutil pkgutil lipo otool shasum ditto osascript sw_vers codesign python3 mktemp; do
  command -v "$command_name" >/dev/null 2>&1 || { printf 'Required command is unavailable: %s\n' "$command_name" >&2; exit 1; }
done

plan_architecture="x64"
expected_machine="x86_64"
runtime="osx-x64"
if [[ "$architecture" == "arm64" ]]; then
  plan_architecture="arm64"
  expected_machine="arm64"
  runtime="osx-arm64"
fi
[[ "$(uname -m)" == "$expected_machine" ]] || { printf 'Native runner architecture %s does not match requested %s.\n' "$(uname -m)" "$architecture" >&2; exit 1; }
if [[ "$architecture" == "x64" ]]; then
  [[ "$expected_runner" == "macos-15-intel" ]] || { printf 'x64 validation requires macos-15-intel.\n' >&2; exit 1; }
else
  [[ "$expected_runner" == "macos-15" ]] || { printf 'arm64 validation requires macos-15.\n' >&2; exit 1; }
fi
product_version="$(sw_vers -productVersion)"
image_os="${ImageOS:-}"
image_version="${ImageVersion:-}"
[[ "${product_version%%.*}" == "15" && "$image_os" == macos15* && -n "$image_version" ]] || {
  printf 'Expected a macOS 15 GitHub runner; observed productVersion=%s ImageOS=%s ImageVersion=%s.\n' "$product_version" "$image_os" "$image_version" >&2
  exit 1
}

raw_tag="$(jq -er '.rawTag' "$identity")"
version="$(jq -er '.normalizedVersion' "$identity")"
source_sha="$(jq -er '.sourceSha' "$identity")"
workflow_sha="$(jq -er '.workflowSha' "$identity")"
tag_binding="$(jq -er '.tagBinding' "$identity")"
manifest_sha256="$(jq -er '.manifestSha256' "$identity")"
support_matrix_sha256="$(jq -er '.supportMatrixSha256' "$identity")"
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "$version" != "0.0.0" ]] || { printf 'Invalid normalizedVersion: %s\n' "$version" >&2; exit 1; }
[[ "$raw_tag" =~ ^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "${raw_tag#v}" == "$version" ]] || { printf 'Invalid rawTag/version binding.\n' >&2; exit 1; }
[[ "$source_sha" =~ ^[0-9a-f]{40}$ && "$workflow_sha" =~ ^[0-9a-f]{40}$ ]] || { printf 'Invalid sourceSha or workflowSha.\n' >&2; exit 1; }
[[ "$tag_binding" == 'notApplicable' || "$tag_binding" == 'required' ]] || { printf 'Invalid tagBinding.\n' >&2; exit 1; }
[[ "$manifest_sha256" =~ ^[0-9a-f]{64}$ && "$support_matrix_sha256" =~ ^[0-9a-f]{64}$ ]] || { printf 'Invalid contract SHA-256 identity.\n' >&2; exit 1; }

artifact_directory="$(cd -- "$artifact_directory" && pwd -P)"
if [[ -z "$output_evidence" ]]; then
  output_evidence="$(dirname -- "$artifact_directory")/evidence/macos-native.json"
fi
mkdir -p -- "$(dirname -- "$output_evidence")"

temp_base="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
[[ -d "$temp_base" && ! -L "$temp_base" ]] || { printf 'Unsafe temporary directory: %s\n' "$temp_base" >&2; exit 1; }
work_root="$(mktemp -d "$temp_base/unlimotion-distribution.XXXXXX")"
installed_app=""
installed_package_identifier=""
cleanup() {
  local exit_code=$?
  trap - EXIT
  if [[ -n "$installed_app" && -e "$installed_app" ]]; then
    sudo rm -rf -- "$installed_app" || true
  fi
  if [[ -n "$installed_package_identifier" ]]; then
    sudo pkgutil --forget "$installed_package_identifier" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$work_root"
  exit "$exit_code"
}
trap cleanup EXIT

write_failure() {
  local exit_code="$1"
  local command_text="$2"
  jq -n \
    --arg kind 'macos-native-validation-evidence' \
    --arg architecture "$architecture" \
    --arg rawTag "$raw_tag" \
    --arg version "$version" \
    --arg sourceSha "$source_sha" \
    --arg workflowSha "$workflow_sha" \
    --arg tagBinding "$tag_binding" \
    --arg manifestSha256 "$manifest_sha256" \
    --arg supportMatrixSha256 "$support_matrix_sha256" \
    --arg error "Command failed: $command_text" \
    --argjson exitCode "$exit_code" \
    '{schemaVersion:1,kind:$kind,status:"fail",platform:"macos",architecture:$architecture,rawTag:$rawTag,normalizedVersion:$version,sourceSha:$sourceSha,workflowSha:$workflowSha,tagBinding:$tagBinding,manifestSha256:$manifestSha256,supportMatrixSha256:$supportMatrixSha256,error:$error,exitCode:$exitCode,productionReady:false}' \
    > "$output_evidence"
}
trap 'exit_code=$?; write_failure "$exit_code" "$BASH_COMMAND"; exit "$exit_code"' ERR

plan_prefix=".filenamePlan.macos.${plan_architecture}"
feed_name="$(jq -er "${plan_prefix}.updaterFeedJson" "$identity")"
package_name="$(jq -er "${plan_prefix}.updaterPackage" "$identity")"
setup_name="$(jq -er "${plan_prefix}.setup" "$identity")"
portable_name="$(jq -er "${plan_prefix}.portable" "$identity")"
legacy_pkg_name="$(jq -er "${plan_prefix}.legacyPkg" "$identity")"

for name in "$feed_name" "$package_name" "$setup_name" "$portable_name" "$legacy_pkg_name"; do
  [[ "$name" == "$(basename -- "$name")" ]] || { printf 'Artifact name contains a path: %s\n' "$name" >&2; false; }
  [[ -s "$artifact_directory/$name" ]] || { printf 'Expected artifact is missing or empty: %s\n' "$name" >&2; false; }
done

portable_root="$work_root/portable"
mkdir -p -- "$portable_root"
ditto -x -k "$artifact_directory/$portable_name" "$portable_root"

expand_package() {
  local package_path="$1"
  local destination="$2"
  rm -rf -- "$destination"
  pkgutil --expand-full "$package_path" "$destination"
}

setup_root="$work_root/setup-expanded"
legacy_root="$work_root/legacy-expanded"
expand_package "$artifact_directory/$setup_name" "$setup_root"
expand_package "$artifact_directory/$legacy_pkg_name" "$legacy_root"

find_single_app() {
  local root="$1"
  local label="$2"
  local -a apps=()
  while IFS= read -r -d '' app; do apps+=("$app"); done < <(find "$root" -type d -name 'Unlimotion.app' -prune -print0)
  [[ "${#apps[@]}" -eq 1 ]] || { printf '%s must contain exactly one Unlimotion.app; found %s.\n' "$label" "${#apps[@]}" >&2; return 1; }
  printf '%s\n' "${apps[0]}"
}

portable_app="$(find_single_app "$portable_root" 'Portable archive')"
setup_app="$(find_single_app "$setup_root" 'Canonical setup package')"
legacy_app="$(find_single_app "$legacy_root" 'Legacy package')"

inspect_app() {
  local app="$1"
  local label="$2"
  local plist="$app/Contents/Info.plist"
  [[ -f "$plist" ]] || { printf '%s has no Info.plist.\n' "$label" >&2; return 1; }
  plutil -lint "$plist" >/dev/null
  local identifier bundle_version short_version executable build_label embedded_source
  identifier="$(plutil -extract CFBundleIdentifier raw "$plist")"
  bundle_version="$(plutil -extract CFBundleVersion raw "$plist")"
  short_version="$(plutil -extract CFBundleShortVersionString raw "$plist")"
  executable="$(plutil -extract CFBundleExecutable raw "$plist")"
  build_label="$(plutil -extract UnlimotionBuildLabel raw "$plist")"
  embedded_source="$(plutil -extract UnlimotionSourceSha raw "$plist")"
  [[ "$identifier" == 'com.Unlimotion' ]] || { printf '%s bundle id is %s.\n' "$label" "$identifier" >&2; return 1; }
  [[ "$bundle_version" == "$version" && "$short_version" == "$version" ]] || { printf '%s bundle version does not match %s.\n' "$label" "$version" >&2; return 1; }
  [[ "$executable" == 'Unlimotion.Desktop.ForMacBuild' ]] || { printf '%s executable metadata is %s.\n' "$label" "$executable" >&2; return 1; }
  [[ "$build_label" == "$version" && "$embedded_source" == "$source_sha" ]] || { printf '%s build identity metadata does not match.\n' "$label" >&2; return 1; }
  local binary="$app/Contents/MacOS/$executable"
  [[ -x "$binary" ]] || { printf '%s executable is missing or not executable.\n' "$label" >&2; return 1; }
  local architectures min_os
  architectures="$(lipo -archs "$binary")"
  [[ "$architectures" == "$expected_machine" ]] || { printf '%s Mach-O architecture is %s, expected %s.\n' "$label" "$architectures" "$expected_machine" >&2; return 1; }
  min_os="$(otool -l "$binary" | awk '$1 == "cmd" && ($2 == "LC_BUILD_VERSION" || $2 == "LC_VERSION_MIN_MACOSX") {in_cmd=1; next} in_cmd && ($1 == "minos" || $1 == "version") {print $2; exit}')"
  [[ "$min_os" == '12.0' || "$min_os" == '12.0.0' ]] || { printf '%s minimum macOS is %s, expected 12.0.\n' "$label" "$min_os" >&2; return 1; }

  local codesign_output codesign_state codesign_details codesign_exit
  if codesign_output="$(codesign --verify --deep --strict --verbose=2 "$app" 2>&1)"; then
    codesign_exit=0
  else
    codesign_exit=$?
  fi
  if [[ "$codesign_exit" -eq 0 ]]; then
    codesign_details="$(codesign --display --verbose=4 "$app" 2>&1)"
    if grep -Eqi 'Signature=adhoc|flags=.*\(adhoc\)' <<<"$codesign_details"; then
      codesign_state='adhoc'
    else
      codesign_state='valid'
    fi
  elif grep -Eqi 'not signed at all|code object is not signed' <<<"$codesign_output"; then
    codesign_state='unsigned'
    codesign_details="$codesign_output"
  else
    printf '%s has an invalid code signature: %s\n' "$label" "$codesign_output" >&2
    return 1
  fi
  local hash
  hash="$(shasum -a 256 "$binary" | awk '{print $1}')"
  jq -cn \
    --arg label "$label" \
    --arg bundleId "$identifier" \
    --arg version "$bundle_version" \
    --arg executable "$executable" \
    --arg architecture "$architectures" \
    --arg minOs "$min_os" \
    --arg binarySha256 "$hash" \
    --arg codesignState "$codesign_state" \
    --arg codesignOutput "$codesign_details" \
    '{label:$label,bundleId:$bundleId,version:$version,executable:$executable,architecture:$architecture,minimumOs:$minOs,binarySha256:$binarySha256,codesignState:$codesignState,codesignOutput:$codesignOutput}'
}

inspect_package_contract() {
  local package_root="$1"
  local label="$2"
  python3 - "$package_root" "$label" "$version" <<'PY'
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
label = sys.argv[2]
version = sys.argv[3]
package_infos = list(root.rglob("PackageInfo"))
if len(package_infos) != 1:
    raise SystemExit(f"{label} must contain exactly one PackageInfo; found {len(package_infos)}")
info_path = package_infos[0]
info = ET.parse(info_path).getroot()
if info.tag != "pkg-info":
    raise SystemExit(f"{label} PackageInfo root is {info.tag!r}")
identifier = info.attrib.get("identifier", "")
package_version = info.attrib.get("version", "")
install_location = info.attrib.get("install-location", "")
if not identifier or "unlimotion" not in identifier.lower():
    raise SystemExit(f"{label} has unexpected package identifier {identifier!r}")
if package_version != version:
    raise SystemExit(f"{label} package version {package_version!r} does not match {version!r}")
if install_location != "/Applications":
    raise SystemExit(f"{label} install-location is {install_location!r}, expected '/Applications'")
payloads = list(info.iter("payload"))
if len(payloads) != 1:
    raise SystemExit(f"{label} must contain exactly one payload metadata record")
scripts = info.attrib.get("scripts", "")
if scripts:
    scripts_path = info_path.parent / scripts
    if not scripts_path.is_dir():
        raise SystemExit(f"{label} declares missing scripts directory {scripts!r}")
distributions = list(root.rglob("Distribution"))
for distribution in distributions:
    parsed = ET.parse(distribution).getroot()
    refs = [node.attrib.get("id", "") for node in parsed.iter("pkg-ref")]
    if refs and identifier not in refs:
        raise SystemExit(f"{label} Distribution does not reference package identifier {identifier!r}")
print(json.dumps({
    "label": label,
    "identifier": identifier,
    "version": package_version,
    "installLocation": install_location,
    "scripts": scripts or None,
    "distributionCount": len(distributions),
    "packageInfo": str(info_path.relative_to(root)),
}, separators=(",", ":")))
PY
}

launch_app() {
  local app="$1"
  local label="$2"
  local run_directory="$work_root/run-$(printf '%s' "$label" | tr '[:upper:] ' '[:lower:]-')"
  mkdir -p -- "$run_directory"
  local binary="$app/Contents/MacOS/Unlimotion.Desktop.ForMacBuild"
  local config="$run_directory/settings.json"
  local task_storage="$run_directory/Tasks"
  local stdout="$run_directory/stdout.log"
  local stderr="$run_directory/stderr.log"
  local automation_stderr="$run_directory/osascript.stderr.log"
  mkdir -p -- "$task_storage"
  jq -cn --arg path "$task_storage" '{TaskStorage:{Path:$path,IsServerMode:"False"}}' >"$config"
  "$binary" "--config=$config" >"$stdout" 2>"$stderr" &
  local pid=$!
  local deadline=$((SECONDS + launch_timeout_seconds))
  local title=""
  local success=false
  while ((SECONDS < deadline)); do
    if ! kill -0 "$pid" 2>/dev/null; then
      wait "$pid" || true
      printf '%s exited before a window appeared. stderr: %s\n' "$label" "$(tr '\n' ' ' < "$stderr")" >&2
      return 1
    fi
    if title="$(osascript -e "tell application \"System Events\" to tell (first process whose unix id is $pid) to if (count windows) > 0 then return name of window 1" 2>"$automation_stderr")"; then
      osascript_exit=0
    else
      osascript_exit=$?
    fi
    if [[ "$osascript_exit" -eq 0 && "$title" == "Unlimotion $version" ]]; then success=true; break; fi
    sleep 0.5
  done
  kill -TERM "$pid" 2>/dev/null || true
  for _ in {1..20}; do kill -0 "$pid" 2>/dev/null || break; sleep 0.25; done
  kill -KILL "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  [[ "$success" == true ]] || {
    printf '%s did not show exact window title Unlimotion %s; observed %s; automation error: %s.\n' "$label" "$version" "$title" "$(tr '\n' ' ' < "$automation_stderr")" >&2
    return 1
  }
  jq -cn \
    --arg label "$label" \
    --arg windowTitle "$title" \
    --arg configPath "$config" \
    --arg taskStoragePath "$task_storage" \
    --arg stdout "$stdout" \
    --arg stderr "$stderr" \
    --arg automationStderr "$automation_stderr" \
    '{label:$label,windowTitle:$windowTitle,configPath:$configPath,taskStoragePath:$taskStoragePath,launchConfiguration:"seeded-isolated-task-storage",unconfiguredFirstRunVerified:false,stdout:$stdout,stderr:$stderr,automationStderr:$automationStderr}'
}

portable_metadata="$(inspect_app "$portable_app" 'portable')"
setup_metadata="$(inspect_app "$setup_app" 'setup')"
legacy_metadata="$(inspect_app "$legacy_app" 'legacy-package')"
setup_package_metadata="$(inspect_package_contract "$setup_root" 'setup')"
legacy_package_metadata="$(inspect_package_contract "$legacy_root" 'legacy-package')"
[[ "$(jq -r '.binarySha256' <<<"$portable_metadata")" == "$(jq -r '.binarySha256' <<<"$setup_metadata")" ]] || { printf 'Portable and setup main executable hashes differ.\n' >&2; false; }

portable_smoke="$(launch_app "$portable_app" 'portable')"
installed_package_identifier="$(jq -er '.identifier' <<<"$setup_package_metadata")"
install_destination='/Applications/Unlimotion.app'
[[ ! -e "$install_destination" ]] || { printf 'Refusing to replace an existing %s on the runner.\n' "$install_destination" >&2; false; }
if pkgutil --pkg-info "$installed_package_identifier" >/dev/null 2>&1; then
  printf 'Refusing to replace an existing package receipt %s on the runner.\n' "$installed_package_identifier" >&2
  false
fi
installed_app="$install_destination"
setup_install_log="$work_root/setup-installer.log"
sudo installer -pkg "$artifact_directory/$setup_name" -target / >"$setup_install_log" 2>&1
[[ -d "$installed_app" ]] || { printf 'Canonical setup package did not install %s.\n' "$installed_app" >&2; false; }
receipt_output="$(pkgutil --pkg-info "$installed_package_identifier")"
installed_metadata="$(inspect_app "$installed_app" 'installed-setup')"
[[ "$(jq -r '.binarySha256' <<<"$portable_metadata")" == "$(jq -r '.binarySha256' <<<"$installed_metadata")" ]] || { printf 'Installed and portable main executable hashes differ.\n' >&2; false; }
setup_smoke="$(launch_app "$installed_app" 'installed-setup')"
setup_install="$(jq -cn \
  --arg target '/' \
  --arg appPath "$installed_app" \
  --arg receiptIdentifier "$installed_package_identifier" \
  --arg receipt "$receipt_output" \
  --arg installerLog "$(cat "$setup_install_log")" \
  '{target:$target,appPath:$appPath,receiptIdentifier:$receiptIdentifier,receipt:$receipt,installerLog:$installerLog,status:"pass"}')"

if setup_signature_output="$(pkgutil --check-signature "$artifact_directory/$setup_name" 2>&1)"; then
  setup_signature_exit=0
else
  setup_signature_exit=$?
fi
if legacy_signature_output="$(pkgutil --check-signature "$artifact_directory/$legacy_pkg_name" 2>&1)"; then
  legacy_signature_exit=0
else
  legacy_signature_exit=$?
fi

classify_package_signature() {
  local label="$1"
  local exit_code="$2"
  local output="$3"
  if [[ "$exit_code" -eq 0 ]]; then
    printf 'valid\n'
  elif grep -Eqi 'not signed|no signature' <<<"$output"; then
    printf 'unsigned\n'
  else
    printf '%s has an invalid package signature: %s\n' "$label" "$output" >&2
    return 1
  fi
}
setup_signature_state="$(classify_package_signature 'setup package' "$setup_signature_exit" "$setup_signature_output")"
legacy_signature_state="$(classify_package_signature 'legacy package' "$legacy_signature_exit" "$legacy_signature_output")"

artifacts_json='[]'
for name in "$feed_name" "$package_name" "$setup_name" "$portable_name" "$legacy_pkg_name"; do
  hash="$(shasum -a 256 "$artifact_directory/$name" | awk '{print $1}')"
  size="$(stat -f '%z' "$artifact_directory/$name")"
  artifacts_json="$(jq -cn --argjson current "$artifacts_json" --arg name "$name" --arg hash "$hash" --argjson size "$size" '$current + [{fileName:$name,size:$size,sha256:$hash}]')"
done

runner_json="$(jq -cn --arg expectedRunner "$expected_runner" --arg swVers "$(sw_vers | tr '\n' ';')" --arg uname "$(uname -a)" --arg image "$image_os" --arg version "$image_version" '{expectedRunner:$expectedRunner,swVers:$swVers,uname:$uname,imageOs:$image,imageVersion:$version}')"
jq -n \
  --arg kind 'macos-native-validation-evidence' \
  --arg architecture "$architecture" \
  --arg runtime "$runtime" \
  --arg rawTag "$raw_tag" \
  --arg version "$version" \
  --arg sourceSha "$source_sha" \
  --arg workflowSha "$workflow_sha" \
  --arg tagBinding "$tag_binding" \
  --arg manifestSha256 "$manifest_sha256" \
  --arg supportMatrixSha256 "$support_matrix_sha256" \
  --argjson runner "$runner_json" \
  --argjson artifacts "$artifacts_json" \
  --argjson portableMetadata "$portable_metadata" \
  --argjson setupMetadata "$setup_metadata" \
  --argjson installedMetadata "$installed_metadata" \
  --argjson setupPackageMetadata "$setup_package_metadata" \
  --argjson setupInstall "$setup_install" \
  --argjson legacyMetadata "$legacy_metadata" \
  --argjson legacyPackageMetadata "$legacy_package_metadata" \
  --argjson portableSmoke "$portable_smoke" \
  --argjson setupSmoke "$setup_smoke" \
  --arg setupPackageSignature "$setup_signature_output" \
  --arg setupPackageSignatureState "$setup_signature_state" \
  --argjson setupPackageSignatureExit "$setup_signature_exit" \
  --arg legacyPackageSignature "$legacy_signature_output" \
  --arg legacyPackageSignatureState "$legacy_signature_state" \
  --argjson legacyPackageSignatureExit "$legacy_signature_exit" \
  '{schemaVersion:1,kind:$kind,status:"pass",platform:"macos",architecture:$architecture,runtime:$runtime,runner:$runner,rawTag:$rawTag,normalizedVersion:$version,sourceSha:$sourceSha,workflowSha:$workflowSha,tagBinding:$tagBinding,manifestSha256:$manifestSha256,supportMatrixSha256:$supportMatrixSha256,artifacts:$artifacts,portable:{metadata:$portableMetadata,smoke:$portableSmoke},setup:{metadata:$setupMetadata,installedMetadata:$installedMetadata,packageMetadata:$setupPackageMetadata,install:$setupInstall,smoke:$setupSmoke,packageSignature:$setupPackageSignature,packageSignatureState:$setupPackageSignatureState,packageSignatureExit:$setupPackageSignatureExit},legacyPackage:{metadata:$legacyMetadata,packageMetadata:$legacyPackageMetadata,packageSignature:$legacyPackageSignature,packageSignatureState:$legacyPackageSignatureState,packageSignatureExit:$legacyPackageSignatureExit,supportClaim:"excluded"},retry:{classification:"deterministic",attempt:1,maxAttempts:1,cleanup:"unique-temporary-directory"},productionReady:false}' \
  > "$output_evidence"

trap - ERR
printf 'macOS %s native validation passed; evidence: %s\n' "$architecture" "$output_evidence"
