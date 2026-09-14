#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
PROJECT_PATH="$ROOT_DIR/src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj"
OUTPUT_ROOT="$ROOT_DIR/artifacts/distribution-validation/linux-x64"
CONFIGURATION="Release"
DOTNET_COMMAND="${DOTNET_COMMAND:-dotnet}"
VPK_COMMAND="${VPK_COMMAND:-vpk}"
VERSION=""
SOURCE_SHA=""
IDENTITY_PATH=""
RAW_TAG=""
WORKFLOW_SHA=""
TAG_BINDING=""
MANIFEST_SHA256=""
SUPPORT_MATRIX_SHA256=""
SKIP_SOURCE_CHECK=false

usage() {
  cat <<'USAGE'
Usage: Build-LinuxDistribution.sh --identity <identity.json> --version <MAJOR.MINOR.PATCH> --source-sha <40-hex> [options]

Builds one canonical linux-x64 publish payload, then packages the same executable
bytes into a manual Debian package and a Velopack AppImage without republishing.

Options:
  --version <version>       Normalized stable SemVer (required, no leading v).
  --source-sha <sha>        Immutable lowercase source commit SHA (required).
  --identity <path>         Exact release identity JSON (required).
  --output-root <path>      Output root (default: artifacts/distribution-validation/linux-x64).
  --configuration <name>   MSBuild configuration (default: Release).
  --project <path>          Debian desktop project path.
  --dotnet <command>        dotnet command/path (default: DOTNET_COMMAND or dotnet).
  --vpk <command>           Velopack CLI command/path (default: VPK_COMMAND or vpk).
  --skip-source-check       Explicit local-only escape; CI must never use it.
  -h, --help                Show this help.
USAGE
}

fail() {
  echo "Build-LinuxDistribution: $*" >&2
  exit 1
}

require_command() {
  local command_name="$1"
  command -v "$command_name" >/dev/null 2>&1 || fail "required command not found: $command_name"
}

hash_file() {
  sha256sum "$1" | awk '{print tolower($1)}'
}

file_size() {
  stat -c '%s' "$1"
}

while (($# > 0)); do
  case "$1" in
    --version)
      (($# >= 2)) || fail '--version requires a value'
      VERSION="$2"
      shift 2
      ;;
    --source-sha)
      (($# >= 2)) || fail '--source-sha requires a value'
      SOURCE_SHA="$2"
      shift 2
      ;;
    --identity)
      (($# >= 2)) || fail '--identity requires a value'
      IDENTITY_PATH="$2"
      shift 2
      ;;
    --output-root)
      (($# >= 2)) || fail '--output-root requires a value'
      OUTPUT_ROOT="$2"
      shift 2
      ;;
    --configuration)
      (($# >= 2)) || fail '--configuration requires a value'
      CONFIGURATION="$2"
      shift 2
      ;;
    --project)
      (($# >= 2)) || fail '--project requires a value'
      PROJECT_PATH="$2"
      shift 2
      ;;
    --dotnet)
      (($# >= 2)) || fail '--dotnet requires a value'
      DOTNET_COMMAND="$2"
      shift 2
      ;;
    --vpk)
      (($# >= 2)) || fail '--vpk requires a value'
      VPK_COMMAND="$2"
      shift 2
      ;;
    --skip-source-check)
      SKIP_SOURCE_CHECK=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

[[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] \
  || fail "--version must be normalized stable SemVer without a leading v: $VERSION"
[[ "$VERSION" != '0.0.0' ]] || fail '--version must be 0.0.1 or greater'
[[ "$SOURCE_SHA" =~ ^[0-9a-f]{40}$ ]] || fail '--source-sha must be 40 lowercase hexadecimal characters'
[[ -f "$IDENTITY_PATH" ]] || fail '--identity must reference an existing release identity JSON file'
[[ -f "$PROJECT_PATH" ]] || fail "project not found: $PROJECT_PATH"
require_command jq
RAW_TAG="$(jq -er '.rawTag' "$IDENTITY_PATH")"
identity_version="$(jq -er '.normalizedVersion' "$IDENTITY_PATH")"
identity_source_sha="$(jq -er '.sourceSha' "$IDENTITY_PATH")"
WORKFLOW_SHA="$(jq -er '.workflowSha' "$IDENTITY_PATH")"
TAG_BINDING="$(jq -er '.tagBinding' "$IDENTITY_PATH")"
MANIFEST_SHA256="$(jq -er '.manifestSha256' "$IDENTITY_PATH")"
SUPPORT_MATRIX_SHA256="$(jq -er '.supportMatrixSha256' "$IDENTITY_PATH")"
[[ "$identity_version" == "$VERSION" && "$identity_source_sha" == "$SOURCE_SHA" ]] || fail 'version/source inputs do not match the release identity'
[[ "$RAW_TAG" =~ ^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && "${RAW_TAG#v}" == "$VERSION" ]] || fail 'release identity raw tag is invalid'
[[ "$WORKFLOW_SHA" =~ ^[0-9a-f]{40}$ && "$MANIFEST_SHA256" =~ ^[0-9a-f]{64}$ && "$SUPPORT_MATRIX_SHA256" =~ ^[0-9a-f]{64}$ ]] || fail 'release identity contract hashes are invalid'
[[ "$TAG_BINDING" == 'notApplicable' || "$TAG_BINDING" == 'required' ]] || fail 'release identity tag binding is invalid'

SOURCE_CHECK_STATUS="passed"
if [[ "$SKIP_SOURCE_CHECK" == true ]]; then
  if [[ "${CI:-}" == true || "${GITHUB_ACTIONS:-}" == true ]]; then
    fail '--skip-source-check is forbidden in CI'
  fi
  SOURCE_CHECK_STATUS="skipped"
  echo 'WARNING: source cleanliness verification was explicitly skipped; do not use this mode in CI.' >&2
else
  require_command git
  if ! HEAD_SHA="$(git -C "$ROOT_DIR" rev-parse HEAD 2>/dev/null)"; then
    fail 'Git metadata is unavailable; use --skip-source-check only for local diagnostics'
  fi
  [[ "$HEAD_SHA" == "$SOURCE_SHA" ]] \
    || fail "--source-sha does not match checked-out HEAD: expected $SOURCE_SHA, got $HEAD_SHA"
  git -C "$ROOT_DIR" diff --quiet --ignore-submodules -- \
    || fail 'tracked working-tree changes are forbidden for an attributed distribution build'
  git -C "$ROOT_DIR" diff --cached --quiet --ignore-submodules -- \
    || fail 'staged tracked changes are forbidden for an attributed distribution build'

  UNTRACKED_BUILD_INPUTS="$(git -C "$ROOT_DIR" status --porcelain=v1 --untracked-files=all -- \
    src distribution/linux scripts/Build-LinuxDistribution.sh \
    global.json NuGet.config Directory.Build.props Directory.Build.targets Directory.Packages.props \
    | awk 'substr($0,1,2) == "??" { print substr($0,4) }')"
  [[ -z "$UNTRACKED_BUILD_INPUTS" ]] \
    || fail "untracked build inputs are forbidden for an attributed distribution build: $UNTRACKED_BUILD_INPUTS"
fi

require_command "$DOTNET_COMMAND"
require_command "$VPK_COMMAND"
for required in dpkg-deb sha256sum stat sed awk find sort xargs realpath; do
  require_command "$required"
done

ROOT_DIR="$(realpath -m -- "$ROOT_DIR")"
ARTIFACTS_ROOT="$ROOT_DIR/artifacts"
ALLOWED_OUTPUT_ROOT="$ARTIFACTS_ROOT/distribution-validation"
[[ ! -L "$ARTIFACTS_ROOT" && ! -L "$ALLOWED_OUTPUT_ROOT" ]] \
  || fail 'output path must not traverse a symlink'
mkdir -p -- "$ALLOWED_OUTPUT_ROOT"
ALLOWED_OUTPUT_ROOT="$(realpath -m -- "$ALLOWED_OUTPUT_ROOT")"
OUTPUT_ROOT="$(realpath -m -- "$OUTPUT_ROOT")"
case "$OUTPUT_ROOT" in
  "$ALLOWED_OUTPUT_ROOT"/*) ;;
  *) fail "--output-root must be a child of $ALLOWED_OUTPUT_ROOT: $OUTPUT_ROOT" ;;
esac

cursor="$ALLOWED_OUTPUT_ROOT"
relative_output="${OUTPUT_ROOT#"$ALLOWED_OUTPUT_ROOT"/}"
IFS='/' read -r -a output_segments <<< "$relative_output"
for segment in "${output_segments[@]}"; do
  cursor="$cursor/$segment"
  [[ ! -L "$cursor" ]] || fail "output path traverses a symlink: $cursor"
done

PAYLOAD_DIR="$OUTPUT_ROOT/payload"
CANDIDATE_DIR="$OUTPUT_ROOT/candidates"
EVIDENCE_DIR="$OUTPUT_ROOT/evidence"
WORK_DIR="$OUTPUT_ROOT/work"
DEB_STAGE="$WORK_DIR/deb-root"
DEB_EXTRACT="$WORK_DIR/deb-extract"
APPIMAGE_INPUT="$WORK_DIR/appimage-input"
APPIMAGE_OUTPUT="$WORK_DIR/velopack-output"
APPIMAGE_EXTRACT="$WORK_DIR/appimage-extract"
PAYLOAD_MANIFEST="$EVIDENCE_DIR/payload-sha256.txt"
BUILD_EVIDENCE="$EVIDENCE_DIR/linux-build.json"

if [[ -e "$OUTPUT_ROOT" ]]; then
  chmod -R u+w -- "$OUTPUT_ROOT" 2>/dev/null || true
  rm -rf -- "$OUTPUT_ROOT"
fi
mkdir -p -- "$PAYLOAD_DIR" "$CANDIDATE_DIR" "$EVIDENCE_DIR" "$WORK_DIR"

echo "Publishing one canonical linux-x64 payload from $SOURCE_SHA"
"$DOTNET_COMMAND" publish "$PROJECT_PATH" \
  -c "$CONFIGURATION" \
  -f net10.0 \
  -r linux-x64 \
  -o "$PAYLOAD_DIR" \
  --self-contained true \
  --ignore-failed-sources \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  -p:GitHubRefName="$VERSION" \
  -p:SourceRevisionId="$SOURCE_SHA" \
  -p:RepositoryCommit="$SOURCE_SHA" \
  -p:ContinuousIntegrationBuild=true \
  -p:DistributionBuild=true \
  -p:DistributionVersion="$VERSION" \
  -p:DistributionSourceSha="$SOURCE_SHA"

CANONICAL_EXECUTABLE="$PAYLOAD_DIR/Unlimotion.Desktop"
[[ -f "$CANONICAL_EXECUTABLE" ]] || fail "canonical executable was not published: $CANONICAL_EXECUTABLE"
[[ -x "$CANONICAL_EXECUTABLE" ]] || fail 'canonical executable is not executable'

if find "$PAYLOAD_DIR" \( -path '*/ci/deb/*' -o -name 'unlimotion.desktop' -o -name 'create-symlink.sh' \) -print -quit | grep -q .; then
  fail 'candidate payload contains Debian-only integration files'
fi

(
  cd -- "$PAYLOAD_DIR"
  find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum
) > "$PAYLOAD_MANIFEST"

PAYLOAD_MANIFEST_SHA="$(hash_file "$PAYLOAD_MANIFEST")"
CANONICAL_EXECUTABLE_SHA="$(hash_file "$CANONICAL_EXECUTABLE")"
chmod -R a-w -- "$PAYLOAD_DIR"
if find "$PAYLOAD_DIR" -perm /222 -print -quit | grep -q .; then
  fail 'canonical payload remained writable after the publish boundary'
fi

mkdir -p \
  "$DEB_STAGE/DEBIAN" \
  "$DEB_STAGE/usr/lib/unlimotion" \
  "$DEB_STAGE/usr/bin" \
  "$DEB_STAGE/usr/share/applications" \
  "$DEB_STAGE/usr/share/icons/hicolor/512x512/apps"

cp -a -- "$PAYLOAD_DIR/." "$DEB_STAGE/usr/lib/unlimotion/"
find "$DEB_STAGE/usr/lib/unlimotion" -type d -exec chmod 0755 {} +
find "$DEB_STAGE/usr/lib/unlimotion" -type f -exec chmod 0644 {} +
while IFS= read -r -d '' executable_source; do
  executable_relative="${executable_source#"$PAYLOAD_DIR/"}"
  chmod 0755 "$DEB_STAGE/usr/lib/unlimotion/$executable_relative"
done < <(find "$PAYLOAD_DIR" -type f -perm /111 -print0)
install -m 0755 "$ROOT_DIR/distribution/linux/unlimotion-launcher" "$DEB_STAGE/usr/bin/Unlimotion"
install -m 0644 "$ROOT_DIR/distribution/linux/unlimotion.desktop" "$DEB_STAGE/usr/share/applications/unlimotion.desktop"
install -m 0644 "$ROOT_DIR/distribution/linux/unlimotion.png" "$DEB_STAGE/usr/share/icons/hicolor/512x512/apps/unlimotion.png"
chmod 0755 "$DEB_STAGE/usr/lib/unlimotion/Unlimotion.Desktop"

INSTALLED_SIZE="$(du -sk "$DEB_STAGE/usr" | awk '{print $1}')"
sed \
  -e "s/@VERSION@/$VERSION/g" \
  -e "s/@INSTALLED_SIZE@/$INSTALLED_SIZE/g" \
  "$ROOT_DIR/distribution/linux/control.template" > "$DEB_STAGE/DEBIAN/control"
chmod 0644 "$DEB_STAGE/DEBIAN/control"

DEB_PATH="$CANDIDATE_DIR/Unlimotion-$VERSION.deb"
dpkg-deb --root-owner-group --build "$DEB_STAGE" "$DEB_PATH"
[[ -s "$DEB_PATH" ]] || fail "Debian package was not created: $DEB_PATH"

mkdir -p "$DEB_EXTRACT"
dpkg-deb -x "$DEB_PATH" "$DEB_EXTRACT"
DEB_EXECUTABLE="$DEB_EXTRACT/usr/lib/unlimotion/Unlimotion.Desktop"
[[ -x "$DEB_EXECUTABLE" ]] || fail 'Debian package does not contain an executable application payload'
DEB_EXECUTABLE_SHA="$(hash_file "$DEB_EXECUTABLE")"
[[ "$DEB_EXECUTABLE_SHA" == "$CANONICAL_EXECUTABLE_SHA" ]] \
  || fail 'Debian executable bytes differ from the canonical publish payload'

mkdir -p "$APPIMAGE_INPUT" "$APPIMAGE_OUTPUT"
cp -a -- "$PAYLOAD_DIR/." "$APPIMAGE_INPUT/"
chmod -R u+w -- "$APPIMAGE_INPUT"

"$VPK_COMMAND" pack \
  --packId Unlimotion \
  --packVersion "$VERSION" \
  --packDir "$APPIMAGE_INPUT" \
  --outputDir "$APPIMAGE_OUTPUT" \
  --channel linux \
  --runtime linux-x64 \
  --mainExe Unlimotion.Desktop \
  --packTitle Unlimotion \
  --packAuthors Kibnet \
  --icon "$ROOT_DIR/distribution/linux/unlimotion.png"

APPIMAGE_SOURCE="$APPIMAGE_OUTPUT/Unlimotion.AppImage"
NUPKG_SOURCE="$APPIMAGE_OUTPUT/Unlimotion-$VERSION-linux-full.nupkg"
FEED_SOURCE="$APPIMAGE_OUTPUT/releases.linux.json"
[[ -s "$APPIMAGE_SOURCE" ]] || fail "Velopack AppImage missing: $APPIMAGE_SOURCE"
[[ -s "$NUPKG_SOURCE" ]] || fail "Velopack updater package missing: $NUPKG_SOURCE"
[[ -s "$FEED_SOURCE" ]] || fail "Velopack Linux feed missing: $FEED_SOURCE"

APPIMAGE_PATH="$CANDIDATE_DIR/Unlimotion.AppImage"
NUPKG_PATH="$CANDIDATE_DIR/Unlimotion-$VERSION-linux-full.nupkg"
FEED_PATH="$CANDIDATE_DIR/releases.linux.json"
install -m 0755 "$APPIMAGE_SOURCE" "$APPIMAGE_PATH"
install -m 0644 "$NUPKG_SOURCE" "$NUPKG_PATH"
install -m 0644 "$FEED_SOURCE" "$FEED_PATH"

mkdir -p "$APPIMAGE_EXTRACT"
(
  cd -- "$APPIMAGE_EXTRACT"
  "$APPIMAGE_PATH" --appimage-extract >/dev/null
)
APPDIR="$APPIMAGE_EXTRACT/squashfs-root"
[[ -x "$APPDIR/AppRun" ]] || fail 'AppImage does not contain executable AppRun'
mapfile -t APPIMAGE_EXECUTABLES < <(find "$APPDIR" -type f -name 'Unlimotion.Desktop' -print)
((${#APPIMAGE_EXECUTABLES[@]} == 1)) \
  || fail "AppImage must contain exactly one Unlimotion.Desktop; found ${#APPIMAGE_EXECUTABLES[@]}"
APPIMAGE_EXECUTABLE_SHA="$(hash_file "${APPIMAGE_EXECUTABLES[0]}")"
[[ "$APPIMAGE_EXECUTABLE_SHA" == "$CANONICAL_EXECUTABLE_SHA" ]] \
  || fail 'AppImage executable bytes differ from the canonical publish payload'

if find "$APPDIR" -path '*/ci/deb/*' -print -quit | grep -q .; then
  fail 'AppImage contains Debian-only ci/deb payload'
fi

SQ_VERSION="$(find "$APPDIR" -type f -name 'sq.version' -print -quit)"
[[ -n "$SQ_VERSION" ]] || fail 'AppImage does not contain Velopack sq.version metadata'
grep -Fq "<version>$VERSION</version>" "$SQ_VERSION" || fail 'AppImage version metadata does not match normalized version'
grep -Fq '<rid>linux-x64</rid>' "$SQ_VERSION" || fail 'AppImage RID metadata is not linux-x64'
grep -Fq '<machineArchitecture>x64</machineArchitecture>' "$SQ_VERSION" || fail 'AppImage architecture metadata is not x64'

CANONICAL_EXECUTABLE_SHA_AFTER="$(hash_file "$CANONICAL_EXECUTABLE")"
[[ "$CANONICAL_EXECUTABLE_SHA_AFTER" == "$CANONICAL_EXECUTABLE_SHA" ]] \
  || fail 'canonical executable changed during packaging'

DEB_SHA="$(hash_file "$DEB_PATH")"
APPIMAGE_SHA="$(hash_file "$APPIMAGE_PATH")"
NUPKG_SHA="$(hash_file "$NUPKG_PATH")"
FEED_SHA="$(hash_file "$FEED_PATH")"

printf '%s\n' \
  '{' \
  '  "schemaVersion": 1,' \
  '  "kind": "linux-build-parity",' \
  '  "status": "pass",' \
  "  \"rawTag\": \"$RAW_TAG\"," \
  "  \"normalizedVersion\": \"$VERSION\"," \
  "  \"sourceSha\": \"$SOURCE_SHA\"," \
  "  \"workflowSha\": \"$WORKFLOW_SHA\"," \
  "  \"tagBinding\": \"$TAG_BINDING\"," \
  "  \"manifestSha256\": \"$MANIFEST_SHA256\"," \
  "  \"supportMatrixSha256\": \"$SUPPORT_MATRIX_SHA256\"," \
  "  \"sourceCheck\": \"$SOURCE_CHECK_STATUS\"," \
  '  "runtimeIdentifier": "linux-x64",' \
  '  "publishInvocationCount": 1,' \
  "  \"payloadManifestSha256\": \"$PAYLOAD_MANIFEST_SHA\"," \
  "  \"canonicalExecutableSha256\": \"$CANONICAL_EXECUTABLE_SHA\"," \
  "  \"debExecutableSha256\": \"$DEB_EXECUTABLE_SHA\"," \
  "  \"appImageExecutableSha256\": \"$APPIMAGE_EXECUTABLE_SHA\"," \
  '  "artifacts": {' \
  "    \"deb\": { \"fileName\": \"$(basename "$DEB_PATH")\", \"size\": $(file_size "$DEB_PATH"), \"sha256\": \"$DEB_SHA\" }," \
  "    \"appImage\": { \"fileName\": \"$(basename "$APPIMAGE_PATH")\", \"size\": $(file_size "$APPIMAGE_PATH"), \"sha256\": \"$APPIMAGE_SHA\" }," \
  "    \"updaterPackage\": { \"fileName\": \"$(basename "$NUPKG_PATH")\", \"size\": $(file_size "$NUPKG_PATH"), \"sha256\": \"$NUPKG_SHA\" }," \
  "    \"updaterFeed\": { \"fileName\": \"$(basename "$FEED_PATH")\", \"size\": $(file_size "$FEED_PATH"), \"sha256\": \"$FEED_SHA\" }" \
  '  }' \
  '}' > "$BUILD_EVIDENCE"

chmod -R u+w -- "$WORK_DIR" 2>/dev/null || true
rm -rf -- "$WORK_DIR"

echo "Linux candidates: $CANDIDATE_DIR"
echo "Exact-byte evidence: $BUILD_EVIDENCE"
