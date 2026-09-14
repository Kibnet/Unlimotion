#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

identity=""
architecture=""
output_directory=""
vpk_path="vpk"
github_output_path=""

usage() {
  printf 'Usage: %s --identity <identity.json> --architecture <x64|arm64> [--output-dir <dir>] [--vpk <path>] [--github-output <path>]\n' "$0" >&2
}

while (($#)); do
  case "$1" in
    --identity) identity="${2:?missing value for --identity}"; shift 2 ;;
    --architecture) architecture="${2:?missing value for --architecture}"; shift 2 ;;
    --output-dir) output_directory="${2:?missing value for --output-dir}"; shift 2 ;;
    --vpk) vpk_path="${2:?missing value for --vpk}"; shift 2 ;;
    --github-output) github_output_path="${2:?missing value for --github-output}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; usage; exit 2 ;;
  esac
done

[[ "$(uname -s)" == "Darwin" ]] || { printf 'This builder must run on macOS.\n' >&2; exit 1; }
[[ -n "$identity" && -f "$identity" ]] || { printf 'A readable --identity file is required.\n' >&2; exit 2; }
[[ "$architecture" == "x64" || "$architecture" == "arm64" ]] || { printf '%s\n' '--architecture must be x64 or arm64.' >&2; exit 2; }

for command_name in dotnet jq plutil productbuild shasum git ditto python3 codesign; do
  command -v "$command_name" >/dev/null 2>&1 || { printf 'Required command is unavailable: %s\n' "$command_name" >&2; exit 1; }
done
command -v "$vpk_path" >/dev/null 2>&1 || [[ -x "$vpk_path" ]] || { printf 'Velopack CLI is unavailable: %s\n' "$vpk_path" >&2; exit 1; }

runtime="osx-$architecture"
channel="osx"
plan_architecture="x64"
expected_machine="x86_64"
if [[ "$architecture" == "arm64" ]]; then
  channel="osx-arm64"
  plan_architecture="arm64"
  expected_machine="arm64"
fi
[[ "$(uname -m)" == "$expected_machine" ]] || { printf 'Native runner architecture %s does not match requested %s.\n' "$(uname -m)" "$architecture" >&2; exit 1; }

version="$(jq -er '.normalizedVersion' "$identity")"
source_sha="$(jq -er '.sourceSha' "$identity")"
workflow_sha="$(jq -er '.workflowSha' "$identity")"
manifest_path="$REPO_ROOT/distribution/release-assets.json"
manifest_sha="$(shasum -a 256 "$manifest_path" | awk '{print $1}')"
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "$version" != "0.0.0" ]] || { printf 'Invalid normalizedVersion: %s\n' "$version" >&2; exit 1; }
[[ "$source_sha" =~ ^[0-9a-f]{40}$ && "$workflow_sha" =~ ^[0-9a-f]{40}$ ]] || { printf 'Invalid sourceSha or workflowSha.\n' >&2; exit 1; }
[[ "$(jq -er '.manifestSha256' "$identity")" == "$manifest_sha" ]] || { printf 'Release identity was not derived from the checked-out distribution manifest.\n' >&2; exit 1; }

head_sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
[[ "$head_sha" == "$source_sha" ]] || { printf 'Checked-out HEAD %s does not match identity sourceSha %s.\n' "$head_sha" "$source_sha" >&2; exit 1; }
[[ -z "$(git -C "$REPO_ROOT" status --porcelain=v1 --untracked-files=all)" ]] || { printf 'Distribution builds require a completely clean source tree matching %s.\n' "$source_sha" >&2; exit 1; }

if [[ -z "$output_directory" ]]; then
  output_directory="$REPO_ROOT/artifacts/distribution-validation/macos-$architecture"
fi
artifacts_root="$REPO_ROOT/artifacts"
allowed_root="$artifacts_root/distribution-validation"
[[ ! -L "$artifacts_root" && ! -L "$allowed_root" ]] || { printf 'Output path must not traverse a symlink.\n' >&2; exit 1; }
mkdir -p -- "$allowed_root"
resolved_allowed_root="$(cd -- "$allowed_root" && pwd -P)"
[[ "$resolved_allowed_root" == "$allowed_root" ]] || { printf 'Output root resolved outside the repository: %s\n' "$resolved_allowed_root" >&2; exit 1; }
output_full="$(python3 - "$output_directory" <<'PY'
import os
import sys
print(os.path.abspath(sys.argv[1]))
PY
)"
case "$output_full" in
  "$allowed_root"/*) ;;
  *) printf 'Output directory must be a child of %s: %s\n' "$allowed_root" "$output_full" >&2; exit 1 ;;
esac
cursor="$allowed_root"
relative_output="${output_full#"$allowed_root"/}"
IFS='/' read -r -a output_segments <<< "$relative_output"
for segment in "${output_segments[@]}"; do
  cursor="$cursor/$segment"
  [[ ! -L "$cursor" ]] || { printf 'Output path traverses a symlink: %s\n' "$cursor" >&2; exit 1; }
done
rm -rf -- "$output_full"

publish_directory="$output_full/work/payload"
app_path="$output_full/work/Unlimotion.app"
velopack_directory="$output_full/work/velopack"
asset_directory="$output_full/assets"
evidence_directory="$output_full/evidence"
mkdir -p -- "$publish_directory" "$app_path/Contents/MacOS" "$app_path/Contents/Resources" "$velopack_directory" "$asset_directory" "$evidence_directory"

project="$REPO_ROOT/src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj"
dotnet publish "$project" \
  -c Release \
  -f net10.0 \
  -r "$runtime" \
  -o "$publish_directory" \
  -p:PublishSingleFile=true \
  --self-contained true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  "-p:Version=$version" \
  -p:DistributionBuild=true \
  "-p:DistributionVersion=$version" \
  "-p:DistributionSourceSha=$source_sha" \
  "-p:GitHubRefName=$version" \
  --ignore-failed-sources

main_executable="$publish_directory/Unlimotion.Desktop.ForMacBuild"
[[ -x "$main_executable" ]] || { printf 'Published executable is missing or not executable: %s\n' "$main_executable" >&2; exit 1; }
if find "$publish_directory" -type f -name '*.pdb' -print -quit | grep -q .; then
  printf 'macOS publish unexpectedly contains PDB files.\n' >&2
  exit 1
fi

cp -- "$REPO_ROOT/src/Unlimotion.Desktop/ci/osx/Info.plist" "$app_path/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$app_path/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app_path/Contents/Info.plist"
plutil -replace CFBundleExecutable -string 'Unlimotion.Desktop.ForMacBuild' "$app_path/Contents/Info.plist"
plutil -insert UnlimotionBuildLabel -string "$version" "$app_path/Contents/Info.plist"
plutil -insert UnlimotionSourceSha -string "$source_sha" "$app_path/Contents/Info.plist"
cp -- "$REPO_ROOT/src/Unlimotion.Desktop/Assets/Unlimotion.icns" "$app_path/Contents/Resources/Unlimotion.icns"
ditto "$publish_directory" "$app_path/Contents/MacOS"
chmod 0755 "$app_path/Contents/MacOS/Unlimotion.Desktop.ForMacBuild"

"$vpk_path" pack \
  --packId Unlimotion \
  --packVersion "$version" \
  --packDir "$app_path" \
  --outputDir "$velopack_directory" \
  --channel "$channel" \
  --runtime "$runtime" \
  --mainExe Unlimotion.Desktop.ForMacBuild \
  --packTitle Unlimotion \
  --packAuthors Kibnet \
  --signAppIdentity - \
  --yes \
  --skip-updates

plan_prefix=".filenamePlan.macos.${plan_architecture}"
canonical_name() {
  local field="$1"
  local asset_id="$2"
  local name by_id template expected
  name="$(jq -er "${plan_prefix}.${field}" "$identity")"
  by_id="$(jq -er --arg id "$asset_id" '.filenamePlan.byAssetId[$id]' "$identity")"
  [[ "$name" == "$by_id" ]] || { printf 'Convenience filename %s does not match byAssetId[%s].\n' "$field" "$asset_id" >&2; return 1; }
  [[ "$name" == "$(basename -- "$name")" && "$name" != '.' && "$name" != '..' && ! "$name" =~ [[:cntrl:]] ]] || { printf 'Unsafe planned filename for %s: %s\n' "$asset_id" "$name" >&2; return 1; }
  template="$(jq -er --arg id "$asset_id" '.assets[] | select(.id == $id) | .filenameTemplate' "$manifest_path")"
  expected="${template//\{normalizedVersion\}/$version}"
  [[ "$name" == "$expected" ]] || { printf 'Planned filename %s does not match manifest %s.\n' "$name" "$expected" >&2; return 1; }
  printf '%s\n' "$name"
}

legacy_asset_id="macos-x64-pkg-legacy"
if [[ "$architecture" == 'arm64' ]]; then legacy_asset_id="macos-arm64-pkg-legacy"; fi
legacy_pkg_name="$(canonical_name legacyPkg "$legacy_asset_id")"

codesign --force --deep --sign - "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"

productbuild --component "$app_path" /Applications "$asset_directory/$legacy_pkg_name"

if [[ "$architecture" == 'x64' ]]; then
  fields=(updaterFeedJson updaterPackage setup portable)
  asset_ids=(macos-x64-feed-json macos-x64-updater-package macos-x64-setup macos-x64-portable)
else
  fields=(updaterFeedJson updaterPackage setup portable)
  asset_ids=(macos-arm64-feed-json macos-arm64-updater-package macos-arm64-setup macos-arm64-portable)
fi
seen_names=("$legacy_pkg_name")
for index in "${!fields[@]}"; do
  field="${fields[$index]}"
  name="$(canonical_name "$field" "${asset_ids[$index]}")"
  folded="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
  for existing_name in "${seen_names[@]}"; do
    existing_folded="$(printf '%s' "$existing_name" | tr '[:upper:]' '[:lower:]')"
    [[ "$folded" != "$existing_folded" ]] || { printf 'Case-insensitive artifact filename collision: %s\n' "$name" >&2; exit 1; }
  done
  seen_names+=("$name")
  source_path="$velopack_directory/$name"
  [[ -s "$source_path" ]] || { printf 'Velopack did not produce expected %s asset: %s\n' "$field" "$name" >&2; exit 1; }
  cp -- "$source_path" "$asset_directory/$name"
done
[[ -s "$asset_directory/$legacy_pkg_name" ]] || { printf 'productbuild did not produce the legacy package.\n' >&2; exit 1; }

artifacts_json='[]'
for index in "${!fields[@]}"; do
  field="${fields[$index]}"
  name="$(canonical_name "$field" "${asset_ids[$index]}")"
  size="$(stat -f '%z' "$asset_directory/$name")"
  hash="$(shasum -a 256 "$asset_directory/$name" | awk '{print $1}')"
  artifacts_json="$(jq -cn --argjson current "$artifacts_json" --arg name "$name" --arg hash "$hash" --argjson size "$size" '$current + [{fileName:$name,size:$size,sha256:$hash}]')"
done
size="$(stat -f '%z' "$asset_directory/$legacy_pkg_name")"
hash="$(shasum -a 256 "$asset_directory/$legacy_pkg_name" | awk '{print $1}')"
artifacts_json="$(jq -cn --argjson current "$artifacts_json" --arg name "$legacy_pkg_name" --arg hash "$hash" --argjson size "$size" '$current + [{fileName:$name,size:$size,sha256:$hash}]')"

main_hash="$(shasum -a 256 "$main_executable" | awk '{print $1}')"
builder_evidence="$evidence_directory/builder-evidence.json"
jq -n \
  --arg kind 'macos-distribution-builder-evidence' \
  --arg architecture "$architecture" \
  --arg runtime "$runtime" \
  --arg version "$version" \
  --arg sourceSha "$source_sha" \
  --arg workflowSha "$workflow_sha" \
  --arg manifestSha256 "$manifest_sha" \
  --arg mainExecutableSha256 "$main_hash" \
  --argjson artifacts "$artifacts_json" \
  '{schemaVersion:1,kind:$kind,status:"pass",platform:"macos",architecture:$architecture,runtime:$runtime,normalizedVersion:$version,sourceSha:$sourceSha,workflowSha:$workflowSha,manifestSha256:$manifestSha256,sourceCheck:"passed",mainExecutableSha256:$mainExecutableSha256,artifacts:$artifacts,productionReady:false}' \
  > "$builder_evidence"

if [[ -n "$github_output_path" ]]; then
  printf 'asset-directory=%s\n' "$asset_directory" >> "$github_output_path"
  printf 'builder-evidence=%s\n' "$builder_evidence" >> "$github_output_path"
fi

printf 'Built 5 macOS %s candidate artifacts in %s.\n' "$architecture" "$asset_directory"
