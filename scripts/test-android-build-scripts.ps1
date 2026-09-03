$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSScriptRoot
$bashCommand = (Get-Command bash -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if ([string]::IsNullOrWhiteSpace($bashCommand)) {
    throw 'Unable to resolve the Bash executable used by Android contract fixtures.'
}
$commonScript = Get-Content -Raw (Join-Path $PSScriptRoot 'android-native-common.sh')
$buildLibgit2Script = Get-Content -Raw (Join-Path $PSScriptRoot 'build-libgit2-android.sh')
$buildLibssh2Script = Get-Content -Raw (Join-Path $PSScriptRoot 'build-libssh2-android.sh')
$buildOpenSslScript = Get-Content -Raw (Join-Path $PSScriptRoot 'build-openssl-android.sh')
$packScript = Get-Content -Raw (Join-Path $PSScriptRoot 'pack-libgit2sharp-nativebinaries-android.sh')
$distributionBuildScript = Get-Content -Raw (Join-Path $PSScriptRoot 'build-android-distribution.sh')
$distributionTestScript = Get-Content -Raw (Join-Path $PSScriptRoot 'test-android-distribution.sh')
$distributionArtifactScript = Get-Content -Raw (Join-Path $PSScriptRoot 'Test-DistributionArtifact.ps1')
$nugetConfig = Get-Content -Raw (Join-Path $rootDir 'src\nuget.config')
$androidProject = Get-Content -Raw (Join-Path $rootDir 'src\Unlimotion.Android\Unlimotion.Android.csproj')
$androidManifest = Get-Content -Raw (Join-Path $rootDir 'src\Unlimotion.Android\Properties\AndroidManifest.xml')
$androidUpdaterManifestPath = Join-Path $rootDir 'src\Unlimotion.Android\Properties\AndroidManifest.Updater.xml'
$androidUpdaterManifest = if (Test-Path -LiteralPath $androidUpdaterManifestPath) {
    Get-Content -Raw -LiteralPath $androidUpdaterManifestPath
} else {
    ''
}
$androidMainActivity = Get-Content -Raw (Join-Path $rootDir 'src\Unlimotion.Android\MainActivity.cs')
$gitattributes = Get-Content -Raw (Join-Path $rootDir '.gitattributes')
$workflowPath = Join-Path $rootDir '.github\workflows\android-packaging.yml'
$workflow = (Get-Content -Raw $workflowPath).Replace("`r`n", "`n").Replace("`r", "`n")
$distributionValidationWorkflowPath = Join-Path $rootDir '.github\workflows\distribution-validation.yml'
$distributionValidationWorkflow = (Get-Content -Raw $distributionValidationWorkflowPath).Replace("`r`n", "`n").Replace("`r", "`n")
$shellScripts = @(
    @{
        Name = 'android-native-common.sh'
        Path = Join-Path $PSScriptRoot 'android-native-common.sh'
        Content = $commonScript
    },
    @{
        Name = 'build-libgit2-android.sh'
        Path = Join-Path $PSScriptRoot 'build-libgit2-android.sh'
        Content = $buildLibgit2Script
    },
    @{
        Name = 'build-libssh2-android.sh'
        Path = Join-Path $PSScriptRoot 'build-libssh2-android.sh'
        Content = $buildLibssh2Script
    },
    @{
        Name = 'build-openssl-android.sh'
        Path = Join-Path $PSScriptRoot 'build-openssl-android.sh'
        Content = $buildOpenSslScript
    },
    @{
        Name = 'pack-libgit2sharp-nativebinaries-android.sh'
        Path = Join-Path $PSScriptRoot 'pack-libgit2sharp-nativebinaries-android.sh'
        Content = $packScript
    },
    @{
        Name = 'build-android-distribution.sh'
        Path = Join-Path $PSScriptRoot 'build-android-distribution.sh'
        Content = $distributionBuildScript
    },
    @{
        Name = 'test-android-distribution.sh'
        Path = Join-Path $PSScriptRoot 'test-android-distribution.sh'
        Content = $distributionTestScript
    }
)

function Assert-Match {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Message
    )

    if ($Content -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-NotMatch {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Message
    )

    if ($Content -match $Pattern) {
        throw $Message
    }
}

function Assert-NoCrLf {
    param(
        [string]$Content,
        [string]$Message
    )

    if ($Content.Contains("`r`n")) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [scriptblock]$Action,
        [string]$Message
    )

    $threw = $false
    try {
        & $Action
    }
    catch {
        $threw = $true
    }

    if (-not $threw) {
        throw $Message
    }
}

function Convert-ToBashPath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ([System.IO.Path]::DirectorySeparatorChar -eq '/') {
        return $fullPath
    }

    $quotedPath = Quote-Bash $fullPath
    $conversionCommand = @"
case "`$(uname -s)" in
  CYGWIN*|MINGW*|MSYS*)
    command -v cygpath >/dev/null 2>&1 || exit 64
    cygpath -u -- $quotedPath
    ;;
  Linux)
    command -v wslpath >/dev/null 2>&1 || exit 64
    wslpath -a -u -- $quotedPath
    ;;
  *)
    exit 64
    ;;
esac
"@
    $output = @(& $bashCommand --noprofile --norc -c $conversionCommand 2>&1)
    $exitCode = $LASTEXITCODE
    $convertedPath = ($output -join "`n").Trim()
    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($convertedPath) -or
        $convertedPath -notmatch '^/' -or $convertedPath.Contains("`n")) {
        throw "Cannot convert path for Bash '$bashCommand': $fullPath`n$($output -join [Environment]::NewLine)"
    }

    return $convertedPath
}

function Quote-Bash {
    param([string]$Value)

    $singleQuote = [string][char]39
    $doubleQuote = [string][char]34
    $escapedSingleQuote = $singleQuote + $doubleQuote + $singleQuote + $doubleQuote + $singleQuote
    return $singleQuote + $Value.Replace($singleQuote, $escapedSingleQuote) + $singleQuote
}

function Invoke-BashChecked {
    param(
        [string]$Command,
        [string]$Message
    )

    $output = & $bashCommand -lc $Command 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$Message`n$($output -join [Environment]::NewLine)"
    }

    return $output
}

function Invoke-GitChecked {
    param(
        [string[]]$Arguments,
        [string]$Message
    )

    $output = @(& git @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Message`n$($output -join [Environment]::NewLine)"
    }

    return $output
}

function Assert-BashFails {
    param(
        [string]$Command,
        [string]$Message
    )

    & $bashCommand -lc $Command *> $null
    if ($LASTEXITCODE -eq 0) {
        throw $Message
    }
}

function Assert-BashFailsWith {
    param(
        [string]$Command,
        [string]$ExpectedPattern,
        [string]$Message
    )

    $output = @(& $bashCommand -lc $Command 2>&1)
    if ($LASTEXITCODE -eq 0) {
        throw $Message
    }
    if (($output -join "`n") -notmatch $ExpectedPattern) {
        throw "$Message`nExpected failure pattern: $ExpectedPattern`n$($output -join [Environment]::NewLine)"
    }
}

function Write-Utf8Text {
    param(
        [string]$Path,
        [string]$Content
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Assert-AndroidWorkflowSecurity {
    param([string]$Content)

    Assert-Match $Content '(?m)^\s{2}push:\s*$' 'Android workflow must preserve the push trigger.'
    Assert-Match $Content '(?m)^\s{2}pull_request:\s*$' 'Android workflow must preserve the pull_request trigger.'
    Assert-Match $Content '(?m)^\s{2}workflow_dispatch:\s*$' 'Android workflow must preserve the workflow_dispatch trigger.'
    Assert-Match $Content '(?ms)^on:\s*\n(?:.*?\n)*?\s{2}release:\s*\n\s{4}types:\s*\n\s{6}- published\s*$' 'Android workflow must preserve the release/published trigger.'
    Assert-Match $Content '(?ms)^permissions:\s*\n\s{2}contents:\s*read\s*$' 'Android workflow default permissions must be contents: read.'
    Assert-NotMatch $Content '(?m)^\s{2}GITHUB_TOKEN\s*:' 'Android workflow must not expose GITHUB_TOKEN through global env.'
    Assert-NotMatch $Content 'github\.token' 'Android workflow must not expose the implicit token to shell steps.'

    $writePermissions = [regex]::Matches($Content, '(?m)^\s+contents:\s*write\s*$')
    if ($writePermissions.Count -ne 1) {
        throw "Expected exactly one contents: write assignment, found $($writePermissions.Count)."
    }

    $buildMatch = [regex]::Match($Content, '(?ms)^\s{2}android-build:\s*\n(?<body>.*?)(?=^\s{2}android-release-upload:\s*$)')
    if (-not $buildMatch.Success) {
        throw 'Android workflow must define android-build before android-release-upload.'
    }
    $buildJob = $buildMatch.Groups['body'].Value
    Assert-Match $buildJob '(?m)^\s{4}permissions:\s*\n\s{6}contents:\s*read\s*$' 'android-build must have contents: read.'
    Assert-NotMatch $buildJob '(?m)^\s{6}contents:\s*write\s*$' 'android-build must not have contents: write.'
    Assert-NotMatch $buildJob 'secrets\.GITHUB_TOKEN' 'android-build must not reference GITHUB_TOKEN.'
    Assert-Match $buildJob 'persist-credentials:\s*false' 'Android checkout must not persist repository credentials into build scripts.'
    Assert-Match $buildJob '(?m)^\s{6}artifact_attempt:\s*\$\{\{\s*steps\.apk_manifest\.outputs\.artifact_attempt\s*\}\}\s*$' 'android-build must export the exact producer run attempt with its artifact outputs.'
    Assert-Match $buildJob 'echo "artifact_attempt=\$GITHUB_RUN_ATTEMPT" >> "\$GITHUB_OUTPUT"' 'Android artifact manifest must record the producer run attempt.'

    $secretSteps = [regex]::Matches($buildJob, '(?ms)^\s{4}- name:\s*(?<name>[^\r\n]+)\r?\n(?<body>.*?)(?=^\s{4}- name:|\z)') |
        Where-Object { $_.Groups['body'].Value -match 'secrets\.ANDROID_SIGNING_' }
    if ($secretSteps.Count -eq 0) {
        throw 'Android workflow must have explicit release-only signing steps.'
    }
    foreach ($secretStep in $secretSteps) {
        Assert-Match $secretStep.Groups['body'].Value "if:\s*\$\{\{\s*github\.event_name\s*==\s*'release'\s*&&\s*github\.event\.action\s*==\s*'published'\s*\}\}" "Signing step '$($secretStep.Groups['name'].Value)' must be release/published-only."
    }
    $allSigningSecretReferences = [regex]::Matches($buildJob, 'secrets\.ANDROID_SIGNING_').Count
    $releaseStepSigningSecretReferences = 0
    foreach ($secretStep in $secretSteps) {
        $releaseStepSigningSecretReferences += [regex]::Matches($secretStep.Groups['body'].Value, 'secrets\.ANDROID_SIGNING_').Count
    }
    if ($allSigningSecretReferences -ne $releaseStepSigningSecretReferences) {
        throw 'Production Android signing secrets must exist only inside named release-only steps.'
    }
    Assert-Match $buildJob '(?ms)- name:\s*Build Android APKs For CI\s*\n\s+if:\s*\$\{\{\s*github\.event_name\s*!=\s*''release''\s*\}\}(?:(?!secrets\.ANDROID_SIGNING_).)*?- name:\s*Build Android APKs For Release' 'PR/push/manual Android build path must not contain production signing secret references.'
    Assert-Match $buildJob '(?ms)- name:\s*Cleanup Android Release Signing\s*\n\s+if:\s*\$\{\{\s*always\(\).*?rm -rf -- "\$signing_dir".*?ANDROID_SIGNING_KEYSTORE=' 'Release signing material must be removed and environment cleared under always().'

    $uploadMatch = [regex]::Match($Content, '(?ms)^\s{2}android-release-upload:\s*\n(?<body>.*)\z')
    if (-not $uploadMatch.Success) {
        throw 'Android workflow must define android-release-upload.'
    }
    $uploadJob = $uploadMatch.Groups['body'].Value
    Assert-Match $uploadJob "if:\s*\$\{\{\s*github\.event_name\s*==\s*'release'\s*&&\s*github\.event\.action\s*==\s*'published'\s*&&\s*needs\.android-build\.result\s*==\s*'success'\s*\}\}" 'android-release-upload must require release/published and a successful build.'
    Assert-Match $uploadJob '(?m)^\s{4}permissions:\s*\n\s{6}contents:\s*write\s*$' 'android-release-upload must be the only contents: write job.'
    Assert-NotMatch $uploadJob 'actions/checkout@|dotnet\s+(?:build|publish|run)|bash\s+\.?/?scripts/|secrets\.ANDROID_SIGNING_' 'android-release-upload must not checkout, build, execute repository scripts, or receive signing secrets.'
    Assert-Match $uploadJob 'artifact-ids:\s*\$\{\{\s*needs\.android-build\.outputs\.artifact_id\s*\}\}' 'Release upload must download the exact artifact id.'
    Assert-Match $uploadJob 'PRODUCER_RUN_ATTEMPT:\s*\$\{\{\s*needs\.android-build\.outputs\.artifact_attempt\s*\}\}' 'Release handoff must consume the producer run attempt output.'
    Assert-Match $uploadJob 'expected_name="unlimotion-android-apk-\$\{GITHUB_RUN_ID\}-\$\{PRODUCER_RUN_ATTEMPT\}"' 'Release handoff artifact name must be bound to the producer attempt.'
    Assert-Match $uploadJob 'EXPECTED_ARTIFACT_ATTEMPT:\s*\$\{\{\s*needs\.android-build\.outputs\.artifact_attempt\s*\}\}' 'Downloaded manifest verification must use the producer attempt.'
    Assert-Match $uploadJob 'manifest\.get\("workflowRunAttempt"\) != int\(os\.environ\["EXPECTED_ARTIFACT_ATTEMPT"\]\)' 'Downloaded manifest must be checked against the producer attempt.'
    Assert-NotMatch $uploadJob 'expected_name=.*\$\{GITHUB_RUN_ATTEMPT\}|EXPECTED_ARTIFACT_ATTEMPT:\s*\$\{\{\s*github\.run_attempt\s*\}\}' 'Release handoff must not bind producer artifacts to the consumer rerun attempt.'
    Assert-Match $uploadJob 'EXPECTED_ARTIFACT_DIGEST' 'Release upload must verify the upload-artifact digest.'
    Assert-Match $uploadJob 'workflowRunId.*GITHUB_RUN_ID|GITHUB_RUN_ID.*workflowRunId' 'Release upload must bind artifact metadata to the same workflow run.'
    Assert-Match $uploadJob 'headSha.*GITHUB_SHA|GITHUB_SHA.*headSha' 'Release upload must bind artifact metadata to the same source SHA.'
    Assert-Match $uploadJob 'EXPECTED_ARM64_SHA256' 'Release upload must verify the arm64 candidate hash.'
    Assert-Match $uploadJob 'EXPECTED_X64_SHA256' 'Release upload must verify the x64 candidate hash.'

    $tokenAssignments = [regex]::Matches($Content, '(?m)^\s+GITHUB_TOKEN:\s*\$\{\{\s*secrets\.GITHUB_TOKEN\s*\}\}\s*$')
    if ($tokenAssignments.Count -ne 1) {
        throw "Expected exactly one step-level GITHUB_TOKEN assignment, found $($tokenAssignments.Count)."
    }
    $tokenReferences = [regex]::Matches($Content, 'secrets\.GITHUB_TOKEN').Count
    if ($tokenReferences -ne 1) {
        throw "Expected exactly one secrets.GITHUB_TOKEN reference, found $tokenReferences."
    }
    Assert-Match $uploadJob '(?ms)- name:\s*Upload APKs To Release.*?GITHUB_TOKEN:\s*\$\{\{\s*secrets\.GITHUB_TOKEN\s*\}\}.*?files:\s*artifacts/android/\*\.apk' 'GITHUB_TOKEN must be passed only to the pinned release-upload action.'

    $externalUses = [regex]::Matches($Content, '(?m)^\s*uses:\s*(?<reference>[^\s#]+)')
    if ($externalUses.Count -eq 0) {
        throw 'Android workflow must contain external action references.'
    }
    foreach ($externalUse in $externalUses) {
        $reference = $externalUse.Groups['reference'].Value
        if ($reference.StartsWith('./')) {
            continue
        }
        if ($reference -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$') {
            throw "External action must be pinned to a full commit SHA: $reference"
        }
    }

    Assert-Match $Content 'Unlimotion-\$\{GITHUB_REF_NAME\}-android-arm64\.apk' 'Release arm64 APK filename contract changed.'
    Assert-Match $Content 'Unlimotion-\$\{GITHUB_REF_NAME\}-android-x64\.apk' 'Release x64 APK filename contract changed.'
    Assert-Match $buildJob '-p:AndroidSigningKeyStore="\$\{ANDROID_SIGNING_KEYSTORE\}"' 'Release keystore MSBuild input changed.'
    Assert-Match $buildJob '-p:AndroidSigningStorePass=env:ANDROID_SIGNING_STORE_PASS' 'Release store-password MSBuild input must use the release-only environment.'
    Assert-Match $buildJob '-p:AndroidSigningKeyAlias="\$\{ANDROID_SIGNING_KEY_ALIAS\}"' 'Release key-alias MSBuild input changed.'
    Assert-Match $buildJob '-p:AndroidSigningKeyPass=env:ANDROID_SIGNING_KEY_PASS' 'Release key-password MSBuild input must use the release-only environment.'
}

function Assert-SingleArtifactDownloadsAreFlat {
    param(
        [Parameter(Mandatory)] [string]$Content,
        [Parameter(Mandatory)] [string]$Label
    )

    $singleIdCount = 0
    foreach ($step in [regex]::Split($Content, '(?m)(?=^\s*-\s+name:)')) {
        if ($step -notmatch 'uses:\s*actions/download-artifact@[0-9a-f]{40}') { continue }
        if ($step -match '(?m)^\s*artifact-ids:\s*\$\{\{[^}\r\n]*\.artifact_id\s*\}\}\s*$') {
            $singleIdCount++
            Assert-Match $step '(?m)^\s*merge-multiple:\s*true\s*$' "$Label single-artifact download must flatten the exact artifact into its declared path."
        }
    }
    if ($singleIdCount -eq 0) {
        throw "$Label does not contain any exact single-artifact download consumers."
    }
}

function Assert-EmulatorToolPathContract {
    param([Parameter(Mandatory)] [string]$Content)

    foreach ($jobName in @('android_api23', 'android_api36')) {
        $jobMatch = [regex]::Match(
            $Content,
            "(?ms)^  $([regex]::Escape($jobName)):\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9_]*:\r?$|\z)"
        )
        if (-not $jobMatch.Success) {
            throw "Distribution workflow is missing the $jobName job."
        }

        $job = $jobMatch.Groups['job'].Value
        Assert-Match $job '(?m)^    runs-on: ubuntu-22\.04\s*$' "Distribution workflow $jobName must use the supported Ubuntu 22.04 host that provides the Android Emulator libtiff.so.5 runtime ABI."

        $hostRuntimeStep = [regex]::Match(
            $job,
            '(?ms)^      - name: Install emulator host runtime\s*\r?\n(?<step>.*?)(?=^      - name:|\z)'
        )
        if (-not $hostRuntimeStep.Success) {
            throw "Distribution workflow $jobName job is missing its emulator host-runtime step."
        }
        $hostRuntime = $hostRuntimeStep.Groups['step'].Value
        Assert-Match $hostRuntime 'for attempt in 1 2 3; do' "Distribution workflow $jobName emulator host-runtime install must use the bounded APT retry contract."
        Assert-Match $hostRuntime 'sudo apt-get install -y --no-install-recommends libpulse0' "Distribution workflow $jobName must install the native libpulse.so.0 provider without recommendations."
        Assert-Match $hostRuntime 'dpkg-query -W -f=''\$\{Status\}\\n'' libpulse0' "Distribution workflow $jobName must verify the installed libpulse0 package state."
        Assert-Match $hostRuntime 'test -e /usr/lib/x86_64-linux-gnu/libpulse\.so\.0' "Distribution workflow $jobName must verify the exact emulator libpulse.so.0 runtime path."

        $installStep = [regex]::Match(
            $job,
            '(?ms)^      - name: Install API \d+ emulator image\s*\r?\n(?<step>.*?)(?=^      - name:|\z)'
        )
        if (-not $installStep.Success) {
            throw "Distribution workflow $jobName job is missing its emulator install step."
        }

        $step = $installStep.Groups['step'].Value
        $executableGuards = [regex]::Matches(
            $step,
            '(?m)^\s*test -x "\$\{ANDROID_SDK_ROOT\}/emulator/emulator"\s*$'
        )
        if ($executableGuards.Count -ne 1) {
            throw "Distribution workflow $jobName job must verify its installed emulator executable exactly once; found $($executableGuards.Count)."
        }

        $pathExports = [regex]::Matches(
            $step,
            '(?m)^\s*echo "\$\{ANDROID_SDK_ROOT\}/emulator" >> "\$GITHUB_PATH"\s*$'
        )
        if ($pathExports.Count -ne 1) {
            throw "Distribution workflow $jobName job must export its installed emulator directory through GITHUB_PATH exactly once; found $($pathExports.Count)."
        }

        Assert-Match $step '(?ms)sdkmanager --install\s+.*?"emulator".*?test -x "\$\{ANDROID_SDK_ROOT\}/emulator/emulator".*?echo "\$\{ANDROID_SDK_ROOT\}/emulator" >> "\$GITHUB_PATH"' "Distribution workflow $jobName emulator install, executable guard, and GITHUB_PATH export must stay ordered in one step."
    }
}

function Assert-EmulatorVersionProbeContract {
    param([Parameter(Mandatory)] [string]$Content)

    $helperMatch = [regex]::Match($Content, '(?ms)^resolve_tool_version\(\) \{\r?\n(?<body>.*?)^\}')
    if (-not $helperMatch.Success) {
        throw 'Android distribution validator must define resolve_tool_version.'
    }

    $helper = $helperMatch.Groups['body'].Value
    Assert-Match $helper 'if output="\$\("\$@" 2>&1\)"; then' 'Android tool version probe must capture stderr without triggering errexit.'
    Assert-Match $helper '(?m)^\s*status=\$\?\s*$' 'Android tool version probe must preserve the failed command exit code.'
    Assert-Match $helper 'version probe failed \(exit \$status\): \$first_line' 'Android tool version probe must expose the first diagnostic line and exit code.'

    Assert-Match $Content 'EMULATOR_VERSION="\$\(resolve_tool_version "Android emulator" emulator -version\)"' 'Android emulator version must use the diagnostic-preserving probe.'
    Assert-Match $Content 'ADB_VERSION="\$\(resolve_tool_version "adb" run_adb_command version\)"' 'adb version must use the diagnostic-preserving bounded probe.'
    Assert-Match $Content 'AAPT_VERSION="\$\(resolve_tool_version "aapt" "\$AAPT" version\)"' 'aapt version must use the diagnostic-preserving probe.'
}

function Assert-EmulatorAvdIsolationContract {
    param([Parameter(Mandatory)] [string]$Content)

    Assert-Match $Content '(?m)^EMULATOR_AVD_ROOT="\$\(mktemp -d "\$\{RUNNER_TEMP:-\$\{TMPDIR:-/tmp\}\}/unlimotion-android-avd\.XXXXXX"\)"\s*$' 'Android emulator validation must allocate a unique runner-temporary AVD root.'
    Assert-Match $Content '(?m)^export ANDROID_AVD_HOME="\$EMULATOR_AVD_ROOT"\s*$' 'Android emulator validation must export its exact isolated AVD root to both avdmanager and emulator.'
    Assert-NotMatch $Content '\$\{HOME\}/\.android/avd' 'Android emulator validation must not fall back to a shared HOME AVD root.'
    Assert-Match $Content '(?m)^\[ -d "\$ANDROID_AVD_HOME" \] \|\| fail ' 'Android emulator validation must verify the isolated AVD root before creating an AVD.'
    Assert-Match $Content '(?ms)^cleanup_emulator_avd_root\(\) \{.*?rm -rf -- "\$EMULATOR_AVD_ROOT".*?^\}' 'Android emulator validation must remove its isolated AVD root during exit cleanup.'
    Assert-Match $Content "(?m)^trap 'cleanup_emulator; cleanup_emulator_avd_root' EXIT\s*$" 'Android emulator validation must combine per-AVD and isolated-root cleanup in its EXIT trap.'
    Assert-Match $Content '(?ms)run_avdmanager_command create avd --force --name "\$AVD_NAME" --package "\$SYSTEM_IMAGE" --device pixel.*?\[ -f "\$ANDROID_AVD_HOME/\$\{AVD_NAME\}\.ini" \] \|\| fail .*?\[ -d "\$ANDROID_AVD_HOME/\$\{AVD_NAME\}\.avd" \] \|\| fail ' 'Android emulator validation must prove that avdmanager created the exact AVD descriptor and directory before emulator launch.'
}

$gitLink = (& git -C $rootDir ls-tree HEAD .native/libgit2-src) -join "`n"
if ($LASTEXITCODE -ne 0 -or $gitLink -notmatch '^160000\s+commit\s+[0-9a-f]{40}\s+\.native/libgit2-src$') {
    throw 'Expected .native/libgit2-src to be a committed gitlink; local static checks do not require an initialized submodule.'
}

Assert-Match $buildLibgit2Script 'git -C "\$SRC_DIR" rev-parse --is-inside-work-tree' 'build-libgit2-android.sh must validate submodule with git rev-parse.'
Assert-NotMatch $buildLibgit2Script '\[ ! -d "\$SRC_DIR/\.git" \]' 'build-libgit2-android.sh must not require .git to be a directory.'

Assert-Match $buildLibgit2Script '#!/usr/bin/env bash' 'build-libgit2-android.sh must use a portable bash shebang.'
Assert-Match $buildLibgit2Script 'LIBGIT2_HTTPS_BACKEND="\$\{LIBGIT2_HTTPS_BACKEND:-OpenSSL\}"' 'build-libgit2-android.sh must default libgit2 HTTPS backend to OpenSSL for Android builds.'
Assert-Match $commonScript 'android-arm64' 'android-native-common.sh must map arm64-v8a to the android-arm64 RID.'
Assert-Match $commonScript 'android-x64' 'android-native-common.sh must map x86_64 to the android-x64 RID.'
Assert-Match $commonScript 'cygpath -u' 'android-native-common.sh must normalize Windows SDK and NDK paths for Git Bash.'
Assert-Match $buildLibgit2Script 'OPENSSL_ROOT_DIR="\$\{OPENSSL_ROOT_DIR:-\$ROOT_DIR/artifacts/android-native/openssl-\$OPENSSL_VERSION-\$ANDROID_RID/prefix\}"' 'build-libgit2-android.sh must default OpenSSL root to ABI-specific repo-local Android artifacts.'
Assert-Match $buildLibgit2Script 'LIBSSH2_ROOT_DIR="\$\{LIBSSH2_ROOT_DIR:-\$ROOT_DIR/artifacts/android-native/libssh2-\$LIBSSH2_VERSION-\$ANDROID_RID/prefix\}"' 'build-libgit2-android.sh must default libssh2 root to ABI-specific repo-local Android artifacts.'
Assert-Match $buildLibgit2Script '-DBUILD_TESTS=OFF' 'build-libgit2-android.sh must disable libgit2 tests for Android packaging.'
Assert-Match $buildLibgit2Script '-DBUILD_CLI=OFF' 'build-libgit2-android.sh must disable libgit2 CLI for Android packaging.'
Assert-Match $buildLibgit2Script '-DUSE_SSH="\$LIBGIT2_USE_SSH"' 'build-libgit2-android.sh must enable SSH through libssh2 for Android builds.'
Assert-Match $buildLibgit2Script '-DLIBSSH2_INCLUDE_DIR="\$LIBSSH2_INCLUDE_DIR"' 'build-libgit2-android.sh must pass libssh2 headers to libgit2 CMake.'
Assert-Match $buildLibgit2Script '-DLIBSSH2_LIBRARY="\$LIBSSH2_LIBRARY"' 'build-libgit2-android.sh must pass libssh2 library to libgit2 CMake.'
Assert-Match $buildLibgit2Script 'PKG_CONFIG_LIBDIR="\$EMPTY_PKG_CONFIG_DIR"' 'build-libgit2-android.sh must isolate pkg-config so libgit2 uses the explicit Android libssh2 paths.'
Assert-Match $buildLibgit2Script '--target libgit2package' 'build-libgit2-android.sh must build the shared libgit2 package target.'
Assert-Match $buildLibssh2Script 'CRYPTO_BACKEND=OpenSSL' 'build-libssh2-android.sh must build libssh2 against OpenSSL.'
Assert-Match $buildLibssh2Script '-DBUILD_EXAMPLES=OFF' 'build-libssh2-android.sh must disable libssh2 examples for Android packaging.'
Assert-Match $buildLibssh2Script '-DBUILD_TESTING=OFF' 'build-libssh2-android.sh must disable libssh2 tests for Android packaging.'
Assert-Match $commonScript 'MINGW\*\|MSYS\*\|CYGWIN\*' 'android-native-common.sh must support Windows Git Bash/MSYS host detection.'
Assert-Match $buildOpenSslScript 'Locale::Maketext::Simple' 'build-openssl-android.sh must validate a usable Perl runtime for OpenSSL on Windows.'
Assert-Match $buildOpenSslScript '\$ROOT_DIR/artifacts/tools/strawberry-perl/perl' 'build-openssl-android.sh must source repo-local portable Strawberry Perl modules on Windows.'
Assert-Match $buildOpenSslScript '\$ROOT_DIR/artifacts/tools/perl-lib' 'build-openssl-android.sh must stage portable Perl modules into repo-local perl-lib for Git Bash.'
Assert-Match $buildOpenSslScript 'export PERL5LIB=' 'build-openssl-android.sh must support Git Bash perl via portable Strawberry Perl modules.'
Assert-Match $buildOpenSslScript 'ExtUtils/MakeMaker\.pm' 'build-openssl-android.sh must stage ExtUtils::MakeMaker for Git Bash perl.'
Assert-Match $buildOpenSslScript 'Pod/Usage\.pm' 'build-openssl-android.sh must stage Pod::Usage for Git Bash perl.'
Assert-Match $buildOpenSslScript 'MSYS2_ENV_CONV_EXCL' 'build-openssl-android.sh must prevent MSYS from rewriting PERL5LIB for Windows-host make invocations.'
Assert-Match $buildOpenSslScript 'export MAKESHELL' 'build-openssl-android.sh must force make to run under sh on Windows hosts.'
Assert-Match $buildOpenSslScript 'exe_extension' 'build-openssl-android.sh must patch OpenSSL Configure host executable extension on Windows hosts.'
Assert-Match $buildOpenSslScript 'MSWin32' 'build-openssl-android.sh must special-case Windows-host OpenSSL Configure execution.'
Assert-Match $buildOpenSslScript 'if \(0 \\&\\& eval \{ require IPC::Cmd; 1; \}\)' 'build-openssl-android.sh must disable IPC::Cmd path probing in OpenSSL Configure on Windows hosts.'
Assert-Match $buildOpenSslScript '\$name\.exe' 'build-openssl-android.sh must teach OpenSSL Configure fallback tool lookup to probe .exe host tools.'
Assert-Match $buildOpenSslScript 'Configurations/15-android\.conf' 'build-openssl-android.sh must patch OpenSSL Android config for Windows-host Android builds.'
Assert-Match $buildOpenSslScript '\$ndk =~ s' 'build-openssl-android.sh must normalize Android NDK paths inside OpenSSL Android config on Windows hosts.'
Assert-Match $buildOpenSslScript 'crypto/libssl-shlib-packet\.o' 'build-openssl-android.sh must include the OpenSSL shared packet object in the Windows libssl.so.3 relink.'
Assert-Match $buildOpenSslScript 'ssl/libdefault-lib-s3_cbc\.o' 'build-openssl-android.sh must include OpenSSL CBC digest object in the Windows libssl.so.3 relink.'
Assert-Match $buildOpenSslScript 'ssl/record/libcommon-lib-tls_pad\.o' 'build-openssl-android.sh must include OpenSSL TLS padding object in the Windows libssl.so.3 relink.'
Assert-Match $buildOpenSslScript '--no-undefined' 'build-openssl-android.sh must fail relinking when Android OpenSSL has unresolved symbols.'
Assert-Match $buildOpenSslScript 'for generated_file in libcrypto\.ld libssl\.ld' 'build-openssl-android.sh must verify OpenSSL version scripts exist before Windows relinking.'
Assert-Match $buildOpenSslScript '--whole-archive libcrypto\.a' 'build-openssl-android.sh must relink libcrypto.so.3 from libcrypto.a on Windows hosts.'
Assert-Match $buildOpenSslScript '--whole-archive libssl\.a' 'build-openssl-android.sh must relink libssl.so.3 from libssl.a on Windows hosts.'
Assert-NotMatch $buildOpenSslScript '-Wl,--version-script=libssl\.ld\s+-Wl,--whole-archive libssl\.a\s+-Wl,--no-whole-archive\s+\./libcrypto\.so\.3' 'build-openssl-android.sh must not relink libssl.so.3 without crypto/libssl-shlib-packet.o.'
Assert-Match $commonScript 'export ANDROID_SDK_ROOT ANDROID_NDK_ROOT' 'android-native-common.sh must export ANDROID_NDK_ROOT for native Android builds.'
Assert-Match $buildOpenSslScript 'perl \./Configure' 'build-openssl-android.sh must invoke OpenSSL Configure via the selected perl runtime.'
Assert-Match $buildOpenSslScript 'export CC="\$TOOLCHAIN_DIR/bin/\$\{ANDROID_CLANG_TARGET\}\$\{ANDROID_API_LEVEL\}-clang"' 'build-openssl-android.sh must select the NDK clang compiler explicitly because modern NDKs no longer provide GCC.'
Assert-Match $buildOpenSslScript 'OpenSSL Configure did not produce' 'build-openssl-android.sh must fail when OpenSSL Configure does not create a Makefile.'
Assert-NotMatch $buildOpenSslScript 'no-docs' 'build-openssl-android.sh must not pass unsupported no-docs to OpenSSL Configure.'
Assert-Match $buildOpenSslScript 'OPENSSL_MAKE_JOBS="\$\{OPENSSL_MAKE_JOBS:-\$\(android_native_cpu_count\)\}"' 'build-openssl-android.sh must allow limiting OpenSSL parallelism on Windows hosts.'
Assert-Match $buildOpenSslScript 'make -j"\$OPENSSL_MAKE_JOBS" build_generated libcrypto\.so libssl\.so' 'build-openssl-android.sh must build only the Android OpenSSL shared libraries on non-Windows hosts.'
Assert-Match $buildOpenSslScript 'install -m 0644 libcrypto\.so "\$INSTALL_DIR/lib/libcrypto\.so\.3"' 'build-openssl-android.sh must rename the non-Windows libcrypto.so output to libcrypto.so.3 when staging Android artifacts.'
Assert-Match $buildOpenSslScript 'install -m 0644 libssl\.so "\$INSTALL_DIR/lib/libssl\.so\.3"' 'build-openssl-android.sh must rename the non-Windows libssl.so output to libssl.so.3 when staging Android artifacts.'
Assert-NotMatch $buildOpenSslScript '(?s)else\s+.*build_sw' 'build-openssl-android.sh must not invoke OpenSSL build_sw on non-Windows hosts.'
Assert-NotMatch $buildOpenSslScript '(?s)else\s+.*make install_sw' 'build-openssl-android.sh must not invoke install_sw on non-Windows hosts.'

Assert-Match $packScript '\$ROOT_DIR/artifacts/nuget-local' 'pack-libgit2sharp-nativebinaries-android.sh must default to repo-local NuGet feed.'
Assert-NotMatch $packScript '/storage/emulated/0/nuget-local' 'pack-libgit2sharp-nativebinaries-android.sh must not hardcode Termux feed path.'
Assert-Match $packScript '2\.0\.324-android\.7' 'pack-libgit2sharp-nativebinaries-android.sh must default to the fixed Android native package version.'
Assert-Match $packScript 'ANDROID_ABIS="\$\{ANDROID_ABIS:-arm64-v8a x86_64\}"' 'pack-libgit2sharp-nativebinaries-android.sh must package arm64 and x86_64 Android runtimes by default.'
Assert-Match $packScript 'install -m 0644 "\$openssl_lib_dir/libssl\.so\.3" "\$native_dir/libssl\.so"' 'pack-libgit2sharp-nativebinaries-android.sh must package unversioned libssl.so because libgit2 links against that soname on Android.'
Assert-Match $packScript 'install -m 0644 "\$openssl_lib_dir/libcrypto\.so\.3" "\$native_dir/libcrypto\.so"' 'pack-libgit2sharp-nativebinaries-android.sh must package unversioned libcrypto.so because OpenSSL-linked Android native libraries may require that soname.'
Assert-Match $packScript 'libssh2\.so\*' 'pack-libgit2sharp-nativebinaries-android.sh must include libssh2 runtime libraries.'
Assert-Match $packScript 'command -v zip' 'pack-libgit2sharp-nativebinaries-android.sh must probe for zip before packing.'
Assert-Match $packScript 'command -v python3' 'pack-libgit2sharp-nativebinaries-android.sh must probe for a native Python archiver fallback.'
Assert-Match $packScript 'zipfile' 'pack-libgit2sharp-nativebinaries-android.sh must support Python-based package creation when zip is unavailable.'
Assert-Match $packScript 'powershell\.exe' 'pack-libgit2sharp-nativebinaries-android.sh must fall back to PowerShell packing on Windows hosts.'
Assert-Match $packScript 'Compress-Archive' 'pack-libgit2sharp-nativebinaries-android.sh must support PowerShell archive creation when zip is unavailable.'

Assert-Match $nugetConfig '\.\./artifacts/nuget-local' 'src/nuget.config must reference repo-local NuGet feed.'
Assert-Match $androidProject '<RuntimeIdentifiers>android-arm64;android-x64</RuntimeIdentifiers>' 'Unlimotion.Android.csproj must build both arm64 and x64 Android runtimes.'
Assert-Match $androidProject '<FdroidBuild Condition="''\$\(FdroidBuild\)'' == ''''">false</FdroidBuild>' 'Unlimotion.Android.csproj must keep the F-Droid build switch disabled by default.'
Assert-Match $androidProject '<PropertyGroup Condition="''\$\(FdroidBuild\)'' == ''true''">[\s\S]*<DefineConstants>\$\(DefineConstants\);FDROID_BUILD</DefineConstants>[\s\S]*<RuntimeIdentifiers>android-arm64</RuntimeIdentifiers>' 'Unlimotion.Android.csproj must define the F-Droid compiler symbol and default to arm64.'
Assert-Match $androidProject '<Compile Remove="Services\\AndroidApplicationUpdateService\.cs" />' 'The F-Droid build must exclude AndroidApplicationUpdateService from compilation.'
Assert-Match $androidProject '<AndroidResource Remove="Resources\\xml\\apk_file_paths\.xml" />' 'The F-Droid build must exclude the updater FileProvider paths resource.'
Assert-Match $androidProject '<AndroidManifestOverlay Include="Properties\\AndroidManifest\.Updater\.xml" />' 'The standard Android build must include the updater manifest overlay.'
Assert-NotMatch $androidManifest 'android\.permission\.REQUEST_INSTALL_PACKAGES' 'The store-neutral base Android manifest must not request package installation permission.'
Assert-NotMatch $androidManifest 'androidx\.core\.content\.FileProvider' 'The store-neutral base Android manifest must not declare the updater FileProvider.'
Assert-Match $androidUpdaterManifest 'android\.permission\.REQUEST_INSTALL_PACKAGES' 'The standard updater overlay must request package installation permission.'
Assert-Match $androidUpdaterManifest 'androidx\.core\.content\.FileProvider' 'The standard updater overlay must declare the updater FileProvider.'
Assert-Match $androidMainActivity '#if !FDROID_BUILD[\s\S]*using Unlimotion\.Android\.Services;[\s\S]*#endif' 'MainActivity must not reference the updater namespace in F-Droid builds.'
Assert-Match $androidMainActivity '#if FDROID_BUILD[\s\S]*App\.ConfigureUpdateService\(null\);[\s\S]*#else[\s\S]*App\.ConfigureUpdateService\(new AndroidApplicationUpdateService\(this\)\);[\s\S]*#endif' 'MainActivity must register the updater only for standard Android builds.'
Assert-Match $androidProject '<AndroidEnableAssemblyCompression>true</AndroidEnableAssemblyCompression>' 'Unlimotion.Android.csproj must keep Android assembly compression enabled so libxamarin-app.so exports runtime symbols required by libmonodroid.so.'
Assert-NotMatch $androidProject '<AndroidEnableAssemblyCompression>false</AndroidEnableAssemblyCompression>' 'Unlimotion.Android.csproj must not disable Android assembly compression because published APKs fail before startup on device.'
Assert-Match $androidProject '<AndroidEnableMarshalMethods>false</AndroidEnableMarshalMethods>' 'Unlimotion.Android.csproj must keep static Java callable wrapper registration because marshal-method registration leaves MainActivity native callbacks unregistered on device.'
Assert-NotMatch $androidProject '<AndroidEnableMarshalMethods>true</AndroidEnableMarshalMethods>' 'Unlimotion.Android.csproj must not enable marshal-method registration for release APKs until device startup is verified.'
Assert-Match $androidProject 'runtimes\\android-arm64\\native\\libssh2\.so' 'Unlimotion.Android.csproj must explicitly package Android arm64 libssh2.so.'
Assert-Match $androidProject 'runtimes\\android-arm64\\native\\libcrypto\.so' 'Unlimotion.Android.csproj must explicitly package Android arm64 libcrypto.so.'
Assert-Match $androidProject 'runtimes\\android-arm64\\native\\libssl\.so' 'Unlimotion.Android.csproj must explicitly package Android arm64 libssl.so.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libcrypto\.so\.3' 'Unlimotion.Android.csproj must explicitly package Android x64 libcrypto.so.3.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libcrypto\.so' 'Unlimotion.Android.csproj must explicitly package Android x64 libcrypto.so.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libssl\.so\.3' 'Unlimotion.Android.csproj must explicitly package Android x64 libssl.so.3.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libssl\.so' 'Unlimotion.Android.csproj must explicitly package Android x64 libssl.so.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libssh2\.so' 'Unlimotion.Android.csproj must explicitly package Android x64 libssh2.so.'
Assert-Match $androidProject '<ItemGroup Condition="''\$\(FdroidBuild\)'' != ''true''">[\s\S]*runtimes\\android-x64\\native' 'Unlimotion.Android.csproj must keep x64 native libraries out of the arm64-only F-Droid build.'
Assert-Match $gitattributes '(?m)^\*\.sh\s+text\s+eol=lf\s*$' '.gitattributes must pin shell scripts to LF line endings.'
Assert-Match $workflow 'ANDROID_PLATFORM:\s+android-36' 'android-packaging workflow must install Android platform 36 for the current .NET Android workload.'
Assert-Match $workflow 'ANDROID_API_LEVEL:\s+24' 'android-packaging release publisher must preserve its existing native API level while the standalone Stage-3 builder independently validates API 23.'
Assert-Match $workflow 'dotnet workload install android --skip-manifest-update' 'android-packaging workflow must skip workload manifest updates to keep Android CI setup fast and reproducible.'
Assert-Match $workflow 'artifacts/android artifacts/android-native artifacts/nuget-local' 'android-packaging workflow must create repo-local feed directory.'
Assert-Match $workflow 'Resolve Android Native Cache Key' 'android-packaging workflow must resolve native dependency cache inputs before restoring cached artifacts.'
Assert-Match $workflow 'git -C \.native/libgit2-src rev-parse HEAD' 'android-packaging workflow must include the libgit2 submodule commit in the Android native cache key.'
Assert-Match $workflow 'Cache Android Native Dependencies[\s\S]*uses: actions/cache@[0-9a-f]{40}[\s\S]*artifacts/android-native[\s\S]*artifacts/nuget-local' 'android-packaging workflow must cache rebuilt Android native artifacts and the local native NuGet package with a full-SHA-pinned action.'
Assert-Match $workflow 'key: android-native-\$\{\{ runner\.os \}\}[\s\S]*\$\{\{ steps\.android_native_cache_key\.outputs\.libgit2_sha \}\}' 'android-packaging workflow must key the Android native cache by native toolchain versions, scripts, and libgit2 commit.'
Assert-Match $workflow 'Cache NuGet Packages[\s\S]*uses: actions/cache@[0-9a-f]{40}[\s\S]*~/.nuget/packages' 'android-packaging workflow must cache NuGet packages for repeated Android restores with a full-SHA-pinned action.'
Assert-Match $workflow 'for abi in arm64-v8a x86_64' 'android-packaging workflow must build native dependencies for arm64 and x86_64.'
Assert-Match $workflow 'expected_package="artifacts/nuget-local/LibGit2Sharp\.NativeBinaries\.\$\{LIBGIT2_NATIVE_PACKAGE_VERSION\}\.nupkg"' 'android-packaging workflow must reuse a cached local Android native NuGet package when present.'
Assert-Match $workflow 'Using cached Android native package' 'android-packaging workflow must skip rebuilding Android native dependencies on native package cache hits.'
Assert-Match $workflow 'bash ./scripts/build-openssl-android\.sh[\s\S]*bash ./scripts/build-libssh2-android\.sh[\s\S]*bash ./scripts/build-libgit2-android\.sh' 'android-packaging workflow must build Android OpenSSL and libssh2 before libgit2.'
Assert-Match $workflow 'Resolve Android App Version' 'android-packaging workflow must resolve Android app version before building APKs.'
Assert-Match $workflow 'display_version="\$\{GITHUB_REF_NAME#v\}"' 'android-packaging workflow must derive release ApplicationDisplayVersion from the release tag.'
Assert-Match $workflow 'version_code="\$\{GITHUB_RUN_NUMBER\}"' 'android-packaging workflow must use a monotonic GitHub run number for Android ApplicationVersion.'
Assert-Match $workflow '-p:ApplicationDisplayVersion="\$\{ANDROID_DISPLAY_VERSION\}"' 'android-packaging workflow must stamp Android APK versionName from the resolved release version.'
Assert-Match $workflow '-p:ApplicationVersion="\$\{ANDROID_VERSION_CODE\}"' 'android-packaging workflow must stamp Android APK versionCode from the resolved version code.'
Assert-Match $workflow 'Prepare Android Release Signing' 'android-packaging workflow must prepare a stable release keystore for Android updates.'
Assert-Match $workflow 'ANDROID_SIGNING_KEYSTORE_BASE64' 'android-packaging workflow must require a base64-encoded release keystore secret.'
Assert-Match $workflow 'base64 --decode > "\$signing_keystore"' 'android-packaging workflow must decode the release signing keystore before building release APKs.'
Assert-Match $workflow '-p:AndroidKeyStore=true' 'android-packaging workflow must enable Android keystore signing for release APKs.'
Assert-Match $workflow '-p:AndroidSigningKeyStore="\$\{ANDROID_SIGNING_KEYSTORE\}"' 'android-packaging workflow must pass the stable release keystore to MSBuild.'
Assert-Match $workflow '-p:AndroidSigningStorePass=env:ANDROID_SIGNING_STORE_PASS' 'android-packaging workflow must pass the release keystore password to MSBuild without expanding it into the command line.'
Assert-Match $workflow '-p:AndroidSigningKeyAlias="\$\{ANDROID_SIGNING_KEY_ALIAS\}"' 'android-packaging workflow must pass the release key alias to MSBuild.'
Assert-Match $workflow '-p:AndroidSigningKeyPass=env:ANDROID_SIGNING_KEY_PASS' 'android-packaging workflow must pass the release key password to MSBuild without expanding it into the command line.'
Assert-Match $workflow 'for rid in android-arm64 android-x64' 'android-packaging workflow must build arm64 and x64 Android APKs.'
Assert-NotMatch $workflow 'rm -rf "src/Unlimotion\.Android/bin/Release/net10\.0-android"' 'android-packaging workflow must not delete shared Android build outputs before each RID package build.'
Assert-Match $workflow '-p:RuntimeIdentifiers="\$rid"' 'android-packaging workflow must restrict each APK build to the current RID so arm64 builds do not package x64 intermediates.'
Assert-Match $workflow 'apk_search_root="src/Unlimotion\.Android/bin/Release/net10\.0-android/\$\{rid\}"' 'android-packaging workflow must find the signed APK under the current RID output directory.'
Assert-Match $workflow 'validate_runtime_symbols' 'android-packaging workflow must validate Android runtime native symbols before publishing APK assets.'
Assert-Match $workflow 'libssl\.so[\s\S]*libssl\.so\.3[\s\S]*libcrypto\.so[\s\S]*libcrypto\.so\.3' 'android-packaging workflow must reject APKs missing the unversioned OpenSSL libraries required by libgit2 on Android.'
Assert-Match $workflow 'compressed_assembly_count' 'android-packaging workflow must catch APKs whose libxamarin-app.so is missing compressed assembly symbols required by libmonodroid.so.'
Assert-Match $workflow 'libxamarin-app\.so' 'android-packaging workflow must inspect libxamarin-app.so before publishing Android APKs.'
Assert-NotMatch $workflow '/storage/emulated/0/nuget-local' 'android-packaging workflow must not prepare Termux-only feed path.'

Assert-AndroidWorkflowSecurity $workflow
Assert-SingleArtifactDownloadsAreFlat -Content $workflow -Label 'android-packaging workflow'
Assert-SingleArtifactDownloadsAreFlat -Content $distributionValidationWorkflow -Label 'distribution-validation workflow'
Assert-EmulatorToolPathContract -Content $distributionValidationWorkflow
Assert-EmulatorVersionProbeContract -Content $distributionTestScript
Assert-EmulatorAvdIsolationContract -Content $distributionTestScript

$missingEmulatorPathFixture = [regex]::Replace(
    $distributionValidationWorkflow,
    '(?m)^\s*echo "\$\{ANDROID_SDK_ROOT\}/emulator" >> "\$GITHUB_PATH"\s*\n',
    '',
    1
)
Assert-Throws {
    Assert-EmulatorToolPathContract -Content $missingEmulatorPathFixture
} 'Distribution Android workflow accepted a missing emulator GITHUB_PATH export.'

$incompatibleEmulatorHostFixture = [regex]::Replace(
    $distributionValidationWorkflow,
    '(?ms)(^  android_api23:.*?^    runs-on: )ubuntu-22\.04\s*$',
    '${1}ubuntu-24.04',
    1
)
Assert-Throws {
    Assert-EmulatorToolPathContract -Content $incompatibleEmulatorHostFixture
} 'Distribution Android workflow accepted the Ubuntu 24.04 host without the emulator libtiff.so.5 runtime ABI.'

$missingPulseRuntimeFixture = [regex]::Replace(
    $distributionValidationWorkflow,
    'sudo apt-get install -y --no-install-recommends libpulse0',
    'sudo apt-get install -y --no-install-recommends',
    1
)
Assert-Throws {
    Assert-EmulatorToolPathContract -Content $missingPulseRuntimeFixture
} 'Distribution Android workflow accepted an emulator job without the libpulse.so.0 provider.'

$opaqueEmulatorVersionFixture = $distributionTestScript.Replace(
    'EMULATOR_VERSION="$(resolve_tool_version "Android emulator" emulator -version)"',
    'EMULATOR_VERSION="$(emulator -version 2>&1 | sed -n ''1p'' | tr -d ''\r'')"'
)
Assert-Throws {
    Assert-EmulatorVersionProbeContract -Content $opaqueEmulatorVersionFixture
} 'Android validator accepted an emulator version probe that hides loader diagnostics behind command substitution.'

$unisolatedAvdFixture = $distributionTestScript.Replace(
    'export ANDROID_AVD_HOME="$EMULATOR_AVD_ROOT"',
    'export ANDROID_SDK_HOME="$EMULATOR_AVD_ROOT"'
)
Assert-Throws {
    Assert-EmulatorAvdIsolationContract -Content $unisolatedAvdFixture
} 'Android validator accepted an AVD root that was not exported to emulator.'

$nonFlatSingleArtifactFixture = [regex]::Replace(
    $distributionValidationWorkflow,
    '(?m)^\s*merge-multiple:\s*true\s*$',
    '          merge-multiple: false',
    1
)
Assert-Throws {
    Assert-SingleArtifactDownloadsAreFlat -Content $nonFlatSingleArtifactFixture -Label 'mutated distribution-validation workflow'
} 'Single-artifact download guard accepted a non-flat handoff.'

$readWriteFixture = $workflow -replace '(?m)^permissions:\s*\n\s{2}contents:\s*read\s*$', "permissions:`n  contents: write"
$globalTokenFixture = $workflow -replace '(?m)^env:\s*$', ('env:' + "`n" + '  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}')
$floatingActionFixture = $workflow -replace 'actions/checkout@[0-9a-f]{40}', 'actions/checkout@v4'
$releaseConditionFixture = $workflow.Replace(
    '      if: ${{ github.event_name == ''release'' && github.event.action == ''published'' }}',
    '      if: ${{ always() }}'
)
$cleanupConditionFixture = $workflow.Replace(
    '      if: ${{ always() && github.event_name == ''release'' && github.event.action == ''published'' }}',
    '      if: ${{ github.event_name == ''release'' && github.event.action == ''published'' }}'
)
$artifactIdFixture = $workflow.Replace(
    '        artifact-ids: ${{ needs.android-build.outputs.artifact_id }}',
    '        name: ${{ needs.android-build.outputs.artifact_name }}'
)
$artifactDigestFixture = $workflow.Replace('EXPECTED_ARTIFACT_DIGEST', 'ARTIFACT_DIGEST_NOT_VERIFIED')
$releaseNameFixture = $workflow.Replace('Unlimotion-${GITHUB_REF_NAME}-android-arm64.apk', 'Changed-${GITHUB_REF_NAME}-android-arm64.apk')
$buildJobSecretFixture = $workflow.Replace(
    "  android-build:`n    runs-on: ubuntu-latest",
    ("  android-build:`n    env:`n      LEAK: " + '${{ secrets.ANDROID_SIGNING_KEY_PASS }}' + "`n    runs-on: ubuntu-latest")
)
$uploadSecretFixture = $workflow.Replace(
    "  android-release-upload:`n    needs: android-build",
    ("  android-release-upload:`n    env:`n      LEAK: " + '${{ secrets.ANDROID_SIGNING_KEY_PASS }}' + "`n    needs: android-build")
)
$consumerAttemptFixture = $workflow.
    Replace('${PRODUCER_RUN_ATTEMPT}', '${GITHUB_RUN_ATTEMPT}').
    Replace('${{ needs.android-build.outputs.artifact_attempt }}', '${{ github.run_attempt }}')

$ciStepRegex = [regex]::new('(?ms)(- name:\s*Build Android APKs For CI\s*\n\s+if:[^\r\n]+\r?\n\s+env:\s*\r?\n)')
$ciSecretFixture = $ciStepRegex.Replace(
    $workflow,
    { param($match) $match.Groups[1].Value + '        ANDROID_SIGNING_STORE_PASS: ${{ secrets.ANDROID_SIGNING_STORE_PASS }}' + "`n" },
    1
)

$workflowNegativeFixtures = @(
    @{ Name = 'workflow-level write permission'; Content = $readWriteFixture },
    @{ Name = 'global write token'; Content = $globalTokenFixture },
    @{ Name = 'floating action reference'; Content = $floatingActionFixture },
    @{ Name = 'production signing secret on CI path'; Content = $ciSecretFixture },
    @{ Name = 'production signing secret at build-job scope'; Content = $buildJobSecretFixture },
    @{ Name = 'signing step without release-only condition'; Content = $releaseConditionFixture },
    @{ Name = 'cleanup without always()'; Content = $cleanupConditionFixture },
    @{ Name = 'upload job with a signing secret'; Content = $uploadSecretFixture },
    @{ Name = 'consumer rerun attempt used for producer artifact'; Content = $consumerAttemptFixture },
    @{ Name = 'non-exact artifact download'; Content = $artifactIdFixture },
    @{ Name = 'missing artifact digest verification'; Content = $artifactDigestFixture },
    @{ Name = 'changed release APK filename'; Content = $releaseNameFixture }
)
foreach ($fixture in $workflowNegativeFixtures) {
    if ([string]::Equals($workflow, [string]$fixture.Content, [StringComparison]::Ordinal)) {
        throw "Workflow negative fixture did not mutate source: $($fixture.Name)"
    }
    Assert-Throws {
        Assert-AndroidWorkflowSecurity $fixture.Content
    } "Workflow security validator accepted negative fixture: $($fixture.Name)"
}

function Assert-AndroidLocalFeedIsolationContract {
    param([Parameter(Mandatory)] [string]$Content)

    Assert-Match $Content 'NUGET_LOCAL_DIR="\$OUTPUT_DIR/nuget-local"' 'Distribution Android builder must use an output-scoped isolated local NuGet feed.'
    Assert-NotMatch $Content 'NUGET_LOCAL_DIR="\$ROOT_DIR/artifacts/nuget-local"' 'Distribution Android builder must not restore directly from the ignored repository-local feed.'
    Assert-Match $Content 'LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME="LibGit2Sharp\.NativeBinaries\.\$\{LIBGIT2_NATIVE_UPSTREAM_VERSION\}\.nupkg"' 'Distribution Android builder must name the exact upstream native package.'
    Assert-Match $Content 'LIBGIT2_NATIVE_PACKAGE_NAME="LibGit2Sharp\.NativeBinaries\.\$\{LIBGIT2_NATIVE_PACKAGE_VERSION\}\.nupkg"' 'Distribution Android builder must name the exact generated native package.'
    Assert-Match $Content 'NODIFY_PACKAGE_RELATIVE_PATH="artifacts/nuget-local/NodifyAvalonia\.6\.6\.0-unlimotion\.a12\.1\.nupkg"' 'Distribution Android builder must name the exact reviewed tracked NodifyAvalonia package.'
    Assert-Match $Content 'git -C "\$ROOT_DIR" ls-files -- ''artifacts/nuget-local/NodifyAvalonia\.\*\.nupkg''' 'Distribution Android builder must prove the unique NodifyAvalonia package is tracked.'
    Assert-Match $Content 'git -C "\$ROOT_DIR" cat-file -e "HEAD:\$NODIFY_PACKAGE_RELATIVE_PATH"' 'Distribution Android builder must bind NodifyAvalonia to a blob in HEAD.'
    Assert-Match $Content 'git -C "\$ROOT_DIR" show "HEAD:\$NODIFY_PACKAGE_RELATIVE_PATH" >"\$NODIFY_HEAD_COPY"' 'Distribution Android builder must populate the isolated feed from the HEAD blob.'
    Assert-Match $Content 'sha256_file "\$NODIFY_PACKAGE_SOURCE"\)" != "\$\(sha256_file "\$NODIFY_HEAD_COPY"' 'Distribution Android builder must reject mutated worktree NodifyAvalonia bytes.'
    Assert-Match $Content 'rm -rf -- "\$NATIVE_ARTIFACTS_DIR" "\$NUGET_LOCAL_DIR"' 'Distribution Android builder must recreate the isolated local NuGet feed for every build.'
    Assert-Match $Content 'ET\.SubElement\(sources, "clear"\)' 'Generated Android NuGet config must clear inherited package sources.'
    Assert-Match $Content 'actual_sources != expected_sources or len\(package_sources\.findall\("add"\)\) != 2' 'Generated Android NuGet config must reject source drift.'
    Assert-Match $Content 'mappings = ET\.SubElement\(root, "packageSourceMapping"\)[\s\S]*ET\.SubElement\(mappings, "clear"\)' 'Generated Android NuGet config must clear inherited source mappings.'
    Assert-Match $Content 'local_mapping = ET\.SubElement\(mappings, "packageSource", \{"key": "local"\}\)[\s\S]*"pattern": "NodifyAvalonia"[\s\S]*"pattern": "LibGit2Sharp\.NativeBinaries"' 'Generated Android NuGet config must map both exact private package IDs only to the isolated local feed.'
    Assert-Match $Content 'public_mapping = ET\.SubElement\(mappings, "packageSource", \{"key": "nuget\.org"\}\)[\s\S]*"pattern": "\*"' 'Generated Android NuGet config must map all remaining packages to nuget.org.'
    if ([regex]::Matches($Content, 'ET\.SubElement\(mappings, "packageSource",').Count -ne 2 -or
        [regex]::Matches($Content, '"pattern": "NodifyAvalonia"').Count -ne 1 -or
        [regex]::Matches($Content, '"pattern": "LibGit2Sharp\.NativeBinaries"').Count -ne 1) {
        throw 'Generated Android NuGet config source must have exactly two mapping owners and one local mapping per private package ID.'
    }
    Assert-Match $Content '(?m)^if descendants != expected:$' 'Distribution Android builder must recursively reject nested local-feed content.'
    Assert-Match $Content 'cp "\$upstream_package" "\$NUGET_LOCAL_DIR/\$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME"' 'Distribution Android builder must copy the SHA-verified upstream package into the isolated local feed.'
    $exactCacheClosurePattern = '(?ms)^  assert_exact_flat_package_directory \\\r?\n    "\$CACHE_PATH/bundle/nuget-local" \\\r?\n    "\$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" \\\r?\n    "\$LIBGIT2_NATIVE_PACKAGE_NAME"$'
    if ([regex]::Matches($Content, $exactCacheClosurePattern).Count -ne 2) {
        throw 'Android builder must validate the exact two-package cache closure on both cache hit and cache miss.'
    }
    Assert-Match $Content 'cp "\$NUGET_LOCAL_DIR/\$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" "\$CACHE_PATH/bundle/nuget-local/"' 'Distribution Android builder must preserve the pinned upstream package in the native cache.'
    Assert-Match $Content '(?ms)^assert_exact_flat_package_directory \\\r?\n  "\$NUGET_LOCAL_DIR" \\\r?\n  "\$LIBGIT2_NATIVE_UPSTREAM_PACKAGE_NAME" \\\r?\n  "\$LIBGIT2_NATIVE_PACKAGE_NAME" \\\r?\n  "\$NODIFY_PACKAGE_NAME"$' 'Distribution Android builder must require exactly the three top-level local-feed nupkgs.'
    Assert-Match $Content 'nupkg_bytes = nupkg\.read_bytes\(\)[\s\S]*expected_raw_sha512 = base64\.b64encode\(hashlib\.sha512\(nupkg_bytes\)' 'Distribution Android builder must calculate the raw SHA-512 from the exact local-feed nupkg bytes.'
    Assert-Match $Content 'installed_nupkg\.read_bytes\(\) != nupkg_bytes' 'Distribution Android builder must bind the installed nupkg bytes to the exact local-feed package.'
    Assert-Match $Content 'assets_content_hash = normalize_content_hash[\s\S]*metadata_content_hash = normalize_content_hash[\s\S]*assets_content_hash != metadata_content_hash' 'Distribution Android builder must compare NuGet logical content hashes only with each other.'
    Assert-Match $Content 'sha_path\.read_text\(encoding="utf-8-sig"\)\.strip\(\) != expected_raw_sha512' 'Distribution Android builder must bind the installed SHA-512 sidecar to the raw local-feed package hash.'
    Assert-Match $Content 'pathlib\.Path\(source\)\.resolve\(\) != local_feed' 'Distribution Android builder must prove the exact isolated local-feed source path.'
    Assert-Match $Content 'package_library_keys != \[library_key\]' 'Distribution Android builder must reject extra restored versions of each private package ID.'
    Assert-Match $Content 'UNLIMOTION_ASSETS_PATH="\$ROOT_DIR/src/Unlimotion/obj/project\.assets\.json"' 'Distribution Android builder must inspect the core project restore graph.'
    Assert-Match $Content 'rm -f --[\s\S]*"\$UNLIMOTION_ASSETS_PATH"[\s\S]*src/Unlimotion/obj/project\.nuget\.cache[\s\S]*src/Unlimotion/obj/Unlimotion\.csproj\.nuget\.dgspec\.json[\s\S]*src/Unlimotion/obj/Unlimotion\.csproj\.nuget\.g\.props[\s\S]*src/Unlimotion/obj/Unlimotion\.csproj\.nuget\.g\.targets[\s\S]*"\$ANDROID_ASSETS_PATH"' 'Distribution Android builder must remove stale core and Android restore metadata before proving package sources.'
    Assert-Match $Content '"\$UNLIMOTION_ASSETS_PATH"[\s\S]*"LibGit2Sharp\.NativeBinaries"[\s\S]*"\$LIBGIT2_NATIVE_UPSTREAM_VERSION"[\s\S]*"\$ANDROID_ASSETS_PATH"[\s\S]*"NodifyAvalonia"[\s\S]*"6\.6\.0-unlimotion\.a12\.1"[\s\S]*"\$ANDROID_ASSETS_PATH"[\s\S]*"LibGit2Sharp\.NativeBinaries"[\s\S]*"\$LIBGIT2_NATIVE_PACKAGE_VERSION"' 'Distribution Android builder must prove upstream restore for the core graph and generated-package restore for the Android graph.'
    Assert-Match $Content '-p:RestoreConfigFile="\$NUGET_CONFIG_PATH"' 'Distribution Android build must restore through the generated isolated-feed config.'
    Assert-NotMatch $Content '-p:RestoreConfigFile="\$ROOT_DIR/src/nuget\.config"' 'Distribution Android build must not restore through the repository config that points at the ignored feed.'
}

function Assert-GeneratedAndroidNuGetConfig {
    param([Parameter(Mandatory)] [string]$Content)

    [xml]$document = $Content
    $sourceClear = @($document.SelectNodes('/configuration/packageSources/clear'))
    $sourceNodes = @($document.SelectNodes('/configuration/packageSources/add'))
    if ($sourceClear.Count -ne 1 -or $sourceNodes.Count -ne 2 -or
        @($sourceNodes | Where-Object { $_.key -ceq 'local' }).Count -ne 1 -or
        @($sourceNodes | Where-Object { $_.key -ceq 'nuget.org' -and $_.value -ceq 'https://api.nuget.org/v3/index.json' }).Count -ne 1) {
        throw 'Generated Android NuGet config has unexpected package sources.'
    }

    $mappingClear = @($document.SelectNodes('/configuration/packageSourceMapping/clear'))
    $mappingNodes = @($document.SelectNodes('/configuration/packageSourceMapping/packageSource'))
    $localMappings = @($mappingNodes | Where-Object { $_.key -ceq 'local' })
    $publicMappings = @($mappingNodes | Where-Object { $_.key -ceq 'nuget.org' })
    if ($mappingClear.Count -ne 1 -or $mappingNodes.Count -ne 2 -or
        $localMappings.Count -ne 1 -or $publicMappings.Count -ne 1) {
        throw 'Generated Android NuGet config has unexpected source-mapping owners.'
    }
    $localPatterns = @($localMappings[0].package | ForEach-Object { [string]$_.pattern } | Sort-Object -CaseSensitive)
    $publicPatterns = @($publicMappings[0].package | ForEach-Object { [string]$_.pattern } | Sort-Object -CaseSensitive)
    if (($localPatterns -join '|') -cne 'LibGit2Sharp.NativeBinaries|NodifyAvalonia' -or
        ($publicPatterns -join '|') -cne '*') {
        throw 'Generated Android NuGet config does not map the exact private IDs only to local.'
    }
}

Assert-AndroidLocalFeedIsolationContract $distributionBuildScript

$ignoredFeedFixture = $distributionBuildScript.Replace(
    'NUGET_LOCAL_DIR="$OUTPUT_DIR/nuget-local"',
    'NUGET_LOCAL_DIR="$ROOT_DIR/artifacts/nuget-local"'
)
if ([string]::Equals($ignoredFeedFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android ignored-feed negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $ignoredFeedFixture
} 'Android local-feed validator accepted an ignored feed that can contain extra packages.'

$mutatedNodifyFixture = $distributionBuildScript.Replace(
    '[ "$(sha256_file "$NODIFY_PACKAGE_SOURCE")" != "$(sha256_file "$NODIFY_HEAD_COPY")" ]',
    '[ "trusted-without-comparison" != "trusted-without-comparison" ]'
)
if ([string]::Equals($mutatedNodifyFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android mutated-Nodify negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $mutatedNodifyFixture
} 'Android local-feed validator accepted NodifyAvalonia without a worktree-to-HEAD hash check.'

$publicPrivateIdFixture = $distributionBuildScript.Replace(
    'local_mapping = ET.SubElement(mappings, "packageSource", {"key": "local"})',
    'local_mapping = ET.SubElement(mappings, "packageSource", {"key": "nuget.org"})'
)
if ([string]::Equals($publicPrivateIdFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android public private-ID source-mapping negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $publicPrivateIdFixture
} 'Android local-feed validator accepted private package IDs mapped to nuget.org.'

$missingPrivateIdMappingFixture = $distributionBuildScript.Replace(
    'ET.SubElement(local_mapping, "package", {"pattern": "NodifyAvalonia"})',
    'ET.SubElement(local_mapping, "package", {"pattern": "NotNodifyAvalonia"})'
)
if ([string]::Equals($missingPrivateIdMappingFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android exact private-ID mapping negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $missingPrivateIdMappingFixture
} 'Android local-feed validator accepted a non-exact NodifyAvalonia source mapping.'

$duplicatePublicPrivateIdFixture = $distributionBuildScript.Replace(
    'ET.SubElement(public_mapping, "package", {"pattern": "*"})',
    "ET.SubElement(public_mapping, `"package`", {`"pattern`": `"*`"})`nET.SubElement(public_mapping, `"package`", {`"pattern`": `"NodifyAvalonia`"})"
)
if ([string]::Equals($duplicatePublicPrivateIdFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android duplicate public private-ID mapping negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $duplicatePublicPrivateIdFixture
} 'Android local-feed validator accepted a private package ID mapped to both local and nuget.org.'

$hierarchicalFeedFixture = $distributionBuildScript.Replace(
    'if descendants != expected:',
    'if False and descendants != expected:'
)
if ([string]::Equals($hierarchicalFeedFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android hierarchical-feed negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidLocalFeedIsolationContract $hierarchicalFeedFixture
} 'Android local-feed validator accepted a helper without recursive path closure.'

function Assert-AndroidSourceIdentityContract {
    param([Parameter(Mandatory)] [string]$Content)

    Assert-Match $Content 'actual_source_sha="\$\(git -C "\$ROOT_DIR" rev-parse --verify HEAD\)"' 'Distribution Android builder must resolve exact Git HEAD.'
    Assert-Match $Content '\[ "\$SOURCE_SHA" = "\$actual_source_sha" \]' 'Distribution Android builder must bind identity sourceSha to exact Git HEAD.'
    Assert-Match $Content 'status --porcelain=v1 --untracked-files=no --ignore-submodules=untracked' 'Distribution Android builder must reject tracked, index, and submodule dirt while ignoring untracked artifacts.'
    Assert-Match $Content 'submodule status --recursive' 'Distribution Android builder must inspect recursive submodule commit state.'
    Assert-Match $Content '" "\*\) ;;[\s\S]*uninitialized, modified, or conflicted submodule' 'Distribution Android builder must accept only initialized submodules at their exact committed SHAs.'
    Assert-Match $Content 'IFS=\$''\\t'' read -r NORMALIZED_VERSION[\s\S]*\nassert_source_tree_contract\n\nNATIVE_INPUTS_PATH=' 'Distribution Android builder must enforce source identity before both prepare and build modes.'
}

Assert-AndroidSourceIdentityContract $distributionBuildScript

$headMismatchContractFixture = $distributionBuildScript.Replace(
    '[ "$SOURCE_SHA" = "$actual_source_sha" ]',
    '[ -n "$actual_source_sha" ]'
)
if ([string]::Equals($headMismatchContractFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android HEAD-mismatch contract negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidSourceIdentityContract $headMismatchContractFixture
} 'Android source validator accepted a builder without exact identity-to-HEAD binding.'

$dirtyTreeContractFixture = $distributionBuildScript.Replace(
    'status --porcelain=v1 --untracked-files=no --ignore-submodules=untracked',
    'status --porcelain=v1 --untracked-files=all --ignore-submodules=all'
)
if ([string]::Equals($dirtyTreeContractFixture, $distributionBuildScript, [StringComparison]::Ordinal)) {
    throw 'Android dirty-tree contract negative fixture did not mutate source.'
}
Assert-Throws {
    Assert-AndroidSourceIdentityContract $dirtyTreeContractFixture
} 'Android source validator accepted a builder that does not enforce tracked/index/submodule cleanliness.'

Assert-Match $distributionBuildScript 'ANDROID_API_LEVEL="\$\{ANDROID_API_LEVEL:-23\}"' 'Distribution Android native builds must default to API 23.'
Assert-Match $distributionBuildScript 'android-native-v2-\$\{runner_os_part\}-\$\{runner_arch_part\}-\$\{NATIVE_INPUT_DIGEST\}' 'Distribution Android cache key must bind runner identity to the exact native-input digest.'
Assert-Match $distributionBuildScript 'rm -rf -- "\$CACHE_PATH"[\s\S]*mkdir -p "\$CACHE_PATH"' 'Distribution Android builder must clear the exact cache path before restore.'
Assert-Match $distributionBuildScript '-p:WarningsAsErrors=NU1603' 'Distribution Android builds must reject local-feed fallback as NU1603.'
Assert-Match $distributionBuildScript '"inputFileSha256": input_hashes' 'Distribution Android cache inputs must include all declared input file hashes.'
Assert-Match $distributionBuildScript 'git -C "\$ROOT_DIR" ls-tree HEAD \.native/libgit2-src' 'Distribution Android cache inputs must bind the committed libgit2 gitlink SHA.'
Assert-Match $distributionBuildScript 'OPENSSL_SOURCE_SHA256="[0-9a-f]{64}"' 'OpenSSL source archive hash must be fixed in the distribution builder.'
Assert-Match $distributionBuildScript 'LIBSSH2_SOURCE_SHA256="[0-9a-f]{64}"' 'libssh2 source archive hash must be fixed in the distribution builder.'
Assert-Match $distributionBuildScript 'LIBGIT2_NATIVE_UPSTREAM_SHA256="[0-9a-f]{64}"' 'Upstream native NuGet hash must be fixed in the distribution builder.'
Assert-NotMatch $distributionBuildScript 'SOURCE_SHA256="\$\{' 'Distribution source hashes must not be environment-overridable.'
Assert-Match $distributionBuildScript 'fetch_verified_source[\s\S]*bash "\$ROOT_DIR/scripts/build-openssl-android\.sh"' 'OpenSSL source bytes must be fetched and hash-verified before the build script executes them.'
Assert-Match $distributionBuildScript 'fetch_verified_source[\s\S]*bash "\$ROOT_DIR/scripts/build-libssh2-android\.sh"' 'libssh2 source bytes must be fetched and hash-verified before the build script executes them.'
Assert-Match $distributionBuildScript 'fetch_verified_source[\s\S]*bash "\$ROOT_DIR/scripts/pack-libgit2sharp-nativebinaries-android\.sh"' 'Upstream native package bytes must be fetched and hash-verified before the pack script executes them.'
Assert-Match $distributionBuildScript 'production-monotonic androidVersionCode must be greater than lastPublishedAndroidVersionCode' 'Distribution Android builder must reject non-monotonic production version codes.'
Assert-Match $distributionBuildScript 'filenamePlan.*android' 'Distribution Android builder must consume identity filenamePlan.android.'
Assert-Match $distributionBuildScript 'CACHE_SAVE="true"' 'Distribution Android cache miss path must mark the fully validated bundle for save.'
Assert-Match $distributionBuildScript 'cp -aL "\$NATIVE_ARTIFACTS_DIR/openssl-' 'Distribution Android cache bundle must dereference native build symlinks before hashing.'
Assert-Match $distributionBuildScript '-p:DistributionBuild=true' 'Distribution Android builder must enable the common immutable distribution identity guard.'
Assert-Match $distributionBuildScript '-p:DistributionVersion="\$NORMALIZED_VERSION"' 'Distribution Android builder must pass the normalized distribution version.'
Assert-Match $distributionBuildScript '-p:DistributionSourceSha="\$SOURCE_SHA"' 'Distribution Android builder must pass the exact distribution source SHA.'
Assert-Match $distributionBuildScript 'AndroidSigningStorePass=env:ANDROID_SIGNING_STORE_PASS' 'Distribution Android builder must keep signing passwords out of MSBuild argv values.'
Assert-Match $distributionBuildScript 'cleanup_signing' 'Distribution Android builder must clean ephemeral signing material.'
Assert-Match $distributionBuildScript '--mode provenance[\s\S]*--identity "\$IDENTITY_PATH"[\s\S]*--cache-hit true[\s\S]*--cache-save false' 'Android cache-hit provenance must carry the full identity and exact hit/save outcome.'
Assert-Match $distributionBuildScript '--mode provenance[\s\S]*--identity "\$IDENTITY_PATH"[\s\S]*--cache-hit false[\s\S]*--cache-save true' 'Android cache-miss provenance must carry the full identity and exact hit/save outcome.'
$validatorViaBashMatches = [regex]::Matches(
    $distributionBuildScript,
    '(?m)^\s*"\$BASH"\s+"\$ROOT_DIR/scripts/test-android-distribution\.sh"\s*\\$'
)
if ($validatorViaBashMatches.Count -ne 3) {
    throw "Distribution Android builder must invoke all three validator paths through Bash; found $($validatorViaBashMatches.Count)."
}
Assert-NotMatch $distributionBuildScript '(?m)^\s*"\$ROOT_DIR/scripts/test-android-distribution\.sh"\s*\\$' 'Distribution Android builder must not execute the non-executable validator directly.'

Assert-Match $distributionTestScript 'EXPECTED_MIN_SDK="23"' 'Distribution Android validator must require minSdk 23.'
Assert-Match $distributionTestScript 'EXPECTED_TARGET_SDK="36"' 'Distribution Android validator must require targetSdk 36.'
Assert-Match $distributionTestScript 'EXPECTED_PRODUCTION_FINGERPRINT="1cca6de2bb329c14f89cd0441998e00df601e440d2a9b30c29bdd2cf0a321011"' 'Distribution Android validator must require the public production certificate fingerprint.'
Assert-Match $distributionTestScript 'if api_level != 23' 'Distribution Android provenance validator must reject non-API-23 caches.'
Assert-Match $distributionTestScript 'Matched cache key must equal the exact requested key' 'Distribution Android provenance validator must reject partial/prefix cache matches.'
Assert-Match $distributionTestScript 'actual_paths != set\(declared_by_path\)' 'Distribution Android provenance validator must reject missing or unexpected outputs.'
Assert-Match $distributionTestScript 'actual_nuget_paths != expected_nuget_paths' 'Distribution Android provenance validator must reject extra local-feed package paths.'
Assert-Match $distributionTestScript 'declared_by_path\[upstream_native_package_path\]\.get\("sha256"\) != upstream_native_package_sha256' 'Distribution Android provenance validator must bind the cached upstream package to its pinned input SHA-256.'
Assert-Match $distributionTestScript 'Native cache bundle must not contain symbolic links' 'Distribution Android provenance validator must reject symlink-based cache substitutions.'
Assert-Match $distributionTestScript '"nativeInputsSha256": digest' 'Android provenance evidence must bind the exact native-input bytes.'
Assert-Match $distributionTestScript '"nativeProvenanceSha256": hashlib\.sha256\(provenance_bytes\)\.hexdigest\(\)' 'Android provenance evidence must bind the exact native-provenance bytes.'
Assert-Match $distributionTestScript '"outputClosureSha256": output_closure_sha256' 'Android provenance evidence must bind the validated output closure.'
Assert-Match $distributionTestScript '"productionReady": False' 'Distribution Android test candidates must remain non-promotable.'
Assert-Match $distributionTestScript '"assetId": f"android-\{rid\.removeprefix\(''android-''\)\}-apk"' 'Android metadata evidence must use the exact manifest APK asset identifiers.'
Assert-Match $distributionTestScript '"\$ZIPALIGN" -c -P 16 4' 'Distribution Android validator must verify zip alignment.'
Assert-Match $distributionTestScript '"\$APKSIGNER" verify --verbose --print-certs' 'Distribution Android validator must verify APK signatures and certificates.'
$portableDynamicSymbolReads = [regex]::Matches($distributionTestScript, '"\$READELF" --dyn-syms ')
if ($portableDynamicSymbolReads.Count -ne 2) {
    throw "Distribution Android validator must use the GNU/LLVM-compatible --dyn-syms spelling exactly twice; found $($portableDynamicSymbolReads.Count)."
}
Assert-NotMatch $distributionTestScript '--dyn-symbols' 'Distribution Android validator must not use the LLVM-only --dyn-symbols spelling.'
Assert-Match $distributionTestScript '23\|36\)' 'Distribution Android emulator validator must allow only API 23 and API 36.'
Assert-Match $distributionTestScript 'for port in 5554 5556' 'Distribution Android emulator validator must perform at most two clean boot attempts.'
Assert-Match $distributionTestScript 'EMULATOR_COMMAND_TIMEOUT_SECONDS="\$\{UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS:-30\}"' 'Android emulator validator must configure a bounded timeout for device commands.'
Assert-Match $distributionTestScript 'EMULATOR_INSTALL_TIMEOUT_SECONDS="\$\{UNLIMOTION_ANDROID_EMULATOR_INSTALL_TIMEOUT_SECONDS:-120\}"' 'Android emulator validator must allow APK installation to use its separately bounded timeout.'
Assert-Match $distributionTestScript 'timeout --foreground "\$EMULATOR_COMMAND_TIMEOUT_SECONDS" adb "\$@"' 'Android emulator validator must bound every adb command.'
Assert-Match $distributionTestScript 'local status\s*\r?\n\s*if EMULATOR_COMMAND_TIMEOUT_SECONDS="\$EMULATOR_INSTALL_TIMEOUT_SECONDS" run_adb_command "\$@"; then[\s\S]*?status=\$\?\s*\r?\n\s*handle_emulator_error "\$status" "\$LINENO"' 'Android emulator validator must scope the extended timeout to the install call only and preserve generic-timeout failure evidence.'
Assert-Match $distributionTestScript 'timeout --foreground "\$EMULATOR_COMMAND_TIMEOUT_SECONDS" avdmanager "\$@"' 'Android emulator validator must bound AVD manager cleanup and setup commands.'
Assert-Match $distributionTestScript "adb command result: exit=%s" 'Android emulator validator must retain the exit status of every bounded adb command in the attempt log.'
Assert-Match $distributionTestScript 'readiness poll: serial=%q adb_state=%q sys\.boot_completed=%q init\.svc\.bootanim=%q' 'Android emulator validator must retain observed ADB readiness state in each attempt log.'
Assert-Match $distributionTestScript 'if \[ "\$boot_completed" = "1" \]; then' 'Android emulator readiness must accept the documented sys.boot_completed signal.'
Assert-NotMatch $distributionTestScript 'if \[ "\$boot_completed" = "1" \] && \[ "\$boot_animation" = "stopped" \]' 'Android emulator readiness must not require the optional boot-animation service state.'
Assert-Match $distributionTestScript 'run_adb_install_command -s "\$SERIAL" install -r "\$APK_PATH"' 'Android emulator validator must apply the separate bounded timeout only to APK installation.'
Assert-Match $distributionTestScript 'run_adb_command -s "\$SERIAL" shell am start -n "\$EXPECTED_APPLICATION_ID/\$LAUNCHABLE_ACTIVITY"' 'Android emulator validator must request activity launch without waiting indefinitely for activity completion.'
Assert-Match $distributionTestScript 'sleep 15[\s\S]*?LOGCAT_FILE=.*\r?\n\s*run_adb_command -s "\$SERIAL" logcat -d > "\$LOGCAT_FILE"[\s\S]*?PROCESS_ID=' 'Android emulator validator must capture logcat before it decides whether the launched process is running.'
Assert-NotMatch $distributionTestScript 'shell am start -W ' 'Android emulator validator must not use the blocking am start -W mode.'
Assert-Match $distributionTestScript 'ro\.build\.fingerprint' 'Android emulator evidence must record the exact device build fingerprint.'
Assert-Match $distributionTestScript 'systemImageRevision' 'Android emulator evidence must record the installed system-image revision.'
Assert-Match $distributionTestScript '"classification": "none" if int\(boot_attempts\) == 1 else "transient-emulator-boot"' 'Android emulator evidence must classify first-boot and retried-boot success precisely.'
Assert-Match $distributionTestScript '"cleanupBeforeAttempt2": "notRequired" if int\(boot_attempts\) == 1 else "kill-delete-avd-remove-files-and-wipe-data"' 'Android emulator evidence must record cleanup only when a second boot attempt was required.'
Assert-Match $distributionTestScript '"logcat": file_reference\(logcat_path\)' 'Android emulator evidence must retain a content-addressed logcat payload reference.'
Assert-Match $distributionTestScript '"emulatorLog": file_reference\(emulator_log_path\)' 'Android emulator evidence must retain a content-addressed emulator-log payload reference.'
Assert-NotMatch $distributionTestScript '"(?:logcatPath|emulatorLogPath)"' 'Android emulator evidence must not leak runner-local absolute log paths.'
Assert-Match $distributionTestScript '"outcome": "failed"[\s\S]*"attemptLogs": attempt_logs[\s\S]*"failureClassification": failure_classification[\s\S]*"terminalError": terminal_error' 'Android emulator exhaustion must produce structured diagnostic evidence with per-attempt logs and a terminal classification.'
Assert-Match $distributionTestScript 'rm -f -- "\$\{EMULATOR_ATTEMPT_LOGS\[@\]\}"' 'Successful emulator validation must remove per-attempt logs before the exact four-payload upload.'
Assert-Match $distributionArtifactScript "distribution-download-transport" 'Android platform merge must validate bounded download transport evidence.'
Assert-Match $distributionArtifactScript "sourceArtifact" 'Android download transport evidence must bind the exact producer artifact.'
Assert-Match $distributionArtifactScript 'ExpectedArtifactTransportName \$ArtifactTransportName[\s\S]*ExpectedArtifactTransportId \$ArtifactTransportId[\s\S]*ExpectedArtifactTransportDigest \$ArtifactTransportDigest' 'Android MergePlatform must pass the outer producer transport identity into every raw converter invocation.'
Assert-Match $distributionArtifactScript "'raw-inputs', 'raw-provenance'" 'Android platform merge must require downloaded raw native input and provenance documents.'
Assert-Match $distributionArtifactScript 'Assert-AndroidNativeProvenanceClosure' 'Android platform merge must cross-link artifact cache metadata, summary evidence, and raw provenance bytes.'
Assert-Match $distributionArtifactScript 'nativeEvidence = @\(' 'Platform evidence must retain SHA-256 references to every native sidecar.'
Assert-Match $distributionArtifactScript "'artifact', 'provenance', 'raw-inputs', 'raw-provenance', 'emulator/23', 'emulator/36'" 'Android platform merge must require artifact, cache summary, raw provenance documents, and both exact emulator reports.'
Assert-Match $distributionArtifactScript "'transport/android-api23', 'transport/android-api36'" 'Android platform merge must require both exact bounded-download reports.'

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("unlimotion-android-contract-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $restoredPackageVerifierMatch = [regex]::Match(
        $distributionBuildScript,
        "(?ms)^verify_restored_local_package\(\) \{.*?<<'PY'\r?\n(?<python>.*?)\r?\nPY\r?\n\}"
    )
    if (-not $restoredPackageVerifierMatch.Success) {
        throw 'Unable to extract the restored local-package Python verifier.'
    }
    $restoredPackageVerifierPath = Join-Path $tempRoot 'verify-restored-local-package.py'
    Write-Utf8Text $restoredPackageVerifierPath ($restoredPackageVerifierMatch.Groups['python'].Value + "`n")

    $hashFixtureRoot = Join-Path $tempRoot 'restored-package-hash-fixture'
    $hashFixtureFeed = Join-Path $hashFixtureRoot 'feed'
    $hashFixturePackages = Join-Path $hashFixtureRoot 'packages'
    $hashFixtureAssetsPath = Join-Path $hashFixtureRoot 'project.assets.json'
    $hashFixturePackageId = 'Example.Package'
    $hashFixtureVersion = '1.2.3'
    $hashFixtureLibraryKey = "$hashFixturePackageId/$hashFixtureVersion"
    $hashFixtureInstalled = Join-Path $hashFixturePackages 'example.package/1.2.3'
    $hashFixtureFeedPackage = Join-Path $hashFixtureFeed 'Example.Package.1.2.3.nupkg'
    $hashFixtureInstalledPackage = Join-Path $hashFixtureInstalled 'example.package.1.2.3.nupkg'
    $hashFixtureMetadataPath = Join-Path $hashFixtureInstalled '.nupkg.metadata'
    $hashFixtureSidecarPath = Join-Path $hashFixtureInstalled 'example.package.1.2.3.nupkg.sha512'
    New-Item -ItemType Directory -Force -Path $hashFixtureFeed, $hashFixtureInstalled | Out-Null

    $hashFixturePackageBytes = [System.Text.Encoding]::UTF8.GetBytes('signed nupkg byte fixture')
    [System.IO.File]::WriteAllBytes($hashFixtureFeedPackage, $hashFixturePackageBytes)
    [System.IO.File]::WriteAllBytes($hashFixtureInstalledPackage, $hashFixturePackageBytes)
    $sha512 = [System.Security.Cryptography.SHA512]::Create()
    try {
        $hashFixtureRawSha512 = [Convert]::ToBase64String($sha512.ComputeHash($hashFixturePackageBytes))
        $hashFixtureLogicalSha512 = [Convert]::ToBase64String(
            $sha512.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('logical NuGet content hash fixture'))
        )
        $hashFixtureOtherLogicalSha512 = [Convert]::ToBase64String(
            $sha512.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('different logical content hash fixture'))
        )
    }
    finally {
        $sha512.Dispose()
    }
    if ($hashFixtureRawSha512 -ceq $hashFixtureLogicalSha512) {
        throw 'Restored package fixture did not separate raw and logical SHA-512 domains.'
    }
    Write-Utf8Text $hashFixtureSidecarPath ($hashFixtureRawSha512 + "`n")

    $hashFixtureFeedBash = Convert-ToBashPath $hashFixtureFeed
    $hashFixturePackagesBash = Convert-ToBashPath $hashFixturePackages
    $pythonPathProbe = Quote-Bash 'import pathlib, sys; print(pathlib.Path(sys.argv[1]).resolve())'
    $hashFixtureFeedPythonPath = (@(Invoke-BashChecked `
        ('python3 -c {0} {1}' -f $pythonPathProbe, (Quote-Bash $hashFixtureFeedBash)) `
        'Unable to resolve the fixture feed path in the verifier Python runtime.') -join "`n").Trim()
    $hashFixturePackagesPythonPath = (@(Invoke-BashChecked `
        ('python3 -c {0} {1}' -f $pythonPathProbe, (Quote-Bash $hashFixturePackagesBash)) `
        'Unable to resolve the fixture package path in the verifier Python runtime.') -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($hashFixtureFeedPythonPath) -or
        [string]::IsNullOrWhiteSpace($hashFixturePackagesPythonPath) -or
        $hashFixtureFeedPythonPath.Contains("`n") -or
        $hashFixturePackagesPythonPath.Contains("`n")) {
        throw 'Verifier Python runtime returned an invalid canonical fixture path.'
    }
    $writeHashFixtureAssets = {
        param([string]$ContentHash)

        $libraries = [ordered]@{}
        $libraries[$hashFixtureLibraryKey] = [ordered]@{
            type = 'package'
            sha512 = "sha512-$ContentHash"
            path = 'example.package/1.2.3'
        }
        $packageFolders = [ordered]@{}
        $packageFolders[$hashFixturePackagesPythonPath] = [ordered]@{}
        Write-Utf8Text $hashFixtureAssetsPath (([ordered]@{
            libraries = $libraries
            packageFolders = $packageFolders
        } | ConvertTo-Json -Depth 10 -Compress) + "`n")
    }
    $writeHashFixtureMetadata = {
        param(
            [AllowEmptyString()][string]$ContentHash,
            [string]$Source
        )

        Write-Utf8Text $hashFixtureMetadataPath (([ordered]@{
            contentHash = $ContentHash
            source = $Source
        } | ConvertTo-Json -Compress) + "`n")
    }
    & $writeHashFixtureAssets $hashFixtureLogicalSha512
    & $writeHashFixtureMetadata $hashFixtureLogicalSha512 $hashFixtureFeedPythonPath

    $restoredPackageVerifierCommand = 'python3 {0} {1} {2} {3} {4} {5} {6}' -f @(
        (Quote-Bash (Convert-ToBashPath $restoredPackageVerifierPath)),
        (Quote-Bash (Convert-ToBashPath $hashFixtureAssetsPath)),
        (Quote-Bash $hashFixturePackagesBash),
        (Quote-Bash $hashFixtureFeedBash),
        (Quote-Bash $hashFixturePackageId),
        (Quote-Bash $hashFixtureVersion),
        (Quote-Bash (Convert-ToBashPath $hashFixtureFeedPackage))
    )
    Invoke-BashChecked $restoredPackageVerifierCommand 'Restored local-package verifier rejected distinct valid raw and logical SHA-512 domains.' | Out-Null

    Write-Utf8Text $hashFixtureInstalledPackage 'different installed nupkg bytes'
    Assert-BashFailsWith $restoredPackageVerifierCommand 'Installed package nupkg bytes do not match the exact local-feed package' 'Restored local-package verifier accepted installed bytes that differ from the feed.'
    [System.IO.File]::WriteAllBytes($hashFixtureInstalledPackage, $hashFixturePackageBytes)

    Write-Utf8Text $hashFixtureSidecarPath ($hashFixtureLogicalSha512 + "`n")
    Assert-BashFailsWith $restoredPackageVerifierCommand 'Installed package SHA-512 sidecar does not match local nupkg' 'Restored local-package verifier accepted a sidecar that differs from raw package SHA-512.'
    Write-Utf8Text $hashFixtureSidecarPath ($hashFixtureRawSha512 + "`n")

    & $writeHashFixtureAssets $hashFixtureOtherLogicalSha512
    Assert-BashFailsWith $restoredPackageVerifierCommand 'Restore assets logical content hash does not match installed package metadata' 'Restored local-package verifier accepted different assets and metadata logical content hashes.'
    & $writeHashFixtureAssets $hashFixtureLogicalSha512

    foreach ($invalidContentHash in @('', 'not-base64!', [Convert]::ToBase64String([byte[]](1..32)))) {
        & $writeHashFixtureMetadata $invalidContentHash $hashFixtureFeedPythonPath
        Assert-BashFailsWith $restoredPackageVerifierCommand 'Installed package metadata contentHash' 'Restored local-package verifier accepted an invalid metadata contentHash.'
    }
    & $writeHashFixtureMetadata $hashFixtureLogicalSha512 (Convert-ToBashPath $hashFixtureRoot)
    Assert-BashFailsWith $restoredPackageVerifierCommand 'Installed package source is not the exact isolated local feed' 'Restored local-package verifier accepted a different installed package source.'
    & $writeHashFixtureMetadata $hashFixtureLogicalSha512 $hashFixtureFeedPythonPath

    $sourceFixtureRoot = Join-Path $tempRoot 'source-fixture'
    $fixtureFiles = @(
        'artifacts/nuget-local/NodifyAvalonia.6.6.0-unlimotion.a12.1.nupkg',
        'scripts/android-native-common.sh',
        'scripts/build-android-distribution.sh',
        'scripts/build-libgit2-android.sh',
        'scripts/build-libssh2-android.sh',
        'scripts/build-openssl-android.sh',
        'scripts/pack-libgit2sharp-nativebinaries-android.sh',
        'scripts/test-android-distribution.sh',
        'src/Directory.Packages.props',
        'src/Unlimotion.Android/Unlimotion.Android.csproj',
        'src/nuget.config'
    )
    foreach ($relative in $fixtureFiles) {
        $source = Join-Path $rootDir $relative.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
        $destination = Join-Path $sourceFixtureRoot $relative.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    $submoduleSeedRoot = Join-Path $tempRoot 'submodule-seed'
    New-Item -ItemType Directory -Force -Path $submoduleSeedRoot | Out-Null
    Invoke-GitChecked @('-C', $submoduleSeedRoot, 'init', '--initial-branch=fixture') 'Unable to initialize the Android submodule fixture.' | Out-Null
    Invoke-GitChecked @('-C', $submoduleSeedRoot, 'config', 'user.name', 'Unlimotion Android Fixture') 'Unable to configure the Android submodule fixture author.' | Out-Null
    Invoke-GitChecked @('-C', $submoduleSeedRoot, 'config', 'user.email', 'android-fixture@example.invalid') 'Unable to configure the Android submodule fixture email.' | Out-Null
    Write-Utf8Text (Join-Path $submoduleSeedRoot 'fixture.txt') 'initialized clean submodule fixture'
    Invoke-GitChecked @('-C', $submoduleSeedRoot, 'add', '--all') 'Unable to stage the Android submodule fixture.' | Out-Null
    Invoke-GitChecked @('-C', $submoduleSeedRoot, 'commit', '-m', 'fixture(android): initialize native source') 'Unable to commit the Android submodule fixture.' | Out-Null
    $gitLinkSha = ((Invoke-GitChecked @('-C', $submoduleSeedRoot, 'rev-parse', '--verify', 'HEAD') 'Unable to resolve the Android submodule fixture HEAD.') -join '').Trim()
    if ($gitLinkSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Android submodule fixture HEAD is not an exact lowercase SHA.'
    }

    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'init', '--initial-branch=fixture') 'Unable to initialize the clean Android source fixture.' | Out-Null
    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'config', 'user.name', 'Unlimotion Android Fixture') 'Unable to configure the Android source fixture author.' | Out-Null
    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'config', 'user.email', 'android-fixture@example.invalid') 'Unable to configure the Android source fixture email.' | Out-Null
    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'config', 'core.autocrlf', 'false') 'Unable to disable line-ending conversion in the Android source fixture.' | Out-Null
    Invoke-GitChecked @(
        '-C', $sourceFixtureRoot,
        '-c', 'protocol.file.allow=always',
        'submodule', 'add', '--name', '.native/libgit2-src',
        $submoduleSeedRoot, '.native/libgit2-src'
    ) 'Unable to register the initialized Android submodule fixture.' | Out-Null
    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'add', '--all') 'Unable to stage the clean Android source fixture.' | Out-Null
    Invoke-GitChecked @('-C', $sourceFixtureRoot, 'commit', '-m', 'fixture(android): snapshot source contract') 'Unable to commit the clean Android source fixture.' | Out-Null
    $fixtureHead = ((Invoke-GitChecked @('-C', $sourceFixtureRoot, 'rev-parse', '--verify', 'HEAD') 'Unable to resolve the Android source fixture HEAD.') -join '').Trim()
    if ($fixtureHead -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Android source fixture HEAD is not an exact lowercase SHA.'
    }

    $identity = [ordered]@{
        rawTag = 'v1.28.0'
        normalizedVersion = '1.28.0'
        sourceSha = $fixtureHead
        workflowSha = '1111111111111111111111111111111111111111'
        tagBinding = 'notApplicable'
        manifestSha256 = '2222222222222222222222222222222222222222222222222222222222222222'
        supportMatrixSha256 = '3333333333333333333333333333333333333333333333333333333333333333'
        signatureProfile = 'test'
        androidVersionCode = 1
        androidVersionCodePolicy = 'ci-test'
        lastPublishedAndroidVersionCode = 353
        filenamePlan = [ordered]@{
            android = [ordered]@{
                arm64Apk = 'Unlimotion-1.28.0-android-arm64.apk'
                x64Apk = 'Unlimotion-1.28.0-android-x64.apk'
            }
        }
    }

    $identityPath = Join-Path $tempRoot 'identity.json'
    $outputDir = Join-Path $tempRoot 'output'
    $cacheRoot = Join-Path $tempRoot 'cache'
    New-Item -ItemType Directory -Path $outputDir, $cacheRoot | Out-Null
    Write-Utf8Text $identityPath (($identity | ConvertTo-Json -Depth 10 -Compress) + "`n")

    $rootBash = Convert-ToBashPath $sourceFixtureRoot
    $quotedPathDirectory = Join-Path $tempRoot "bash path's fixture"
    $quotedPathFile = Join-Path $quotedPathDirectory "present file's marker.txt"
    New-Item -ItemType Directory -Path $quotedPathDirectory | Out-Null
    Write-Utf8Text $quotedPathFile 'fixture'
    Invoke-BashChecked (
        'test -f {0}' -f (Quote-Bash (Convert-ToBashPath $quotedPathFile))
    ) 'Bash path conversion did not preserve spaces and single quotes.' | Out-Null
    Assert-BashFails (
        'test -e {0}' -f (Quote-Bash (Convert-ToBashPath (Join-Path $quotedPathDirectory 'missing.txt')))
    ) 'Bash path conversion mapped a missing fixture to an existing path.'

    $gitDir = (& git -C $sourceFixtureRoot rev-parse --absolute-git-dir) -join ''
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitDir)) {
        throw 'Unable to resolve the worktree Git directory for Bash fixtures.'
    }
    $gitDirBash = Convert-ToBashPath $gitDir.Trim()
    $identityBash = Convert-ToBashPath $identityPath
    $outputBash = Convert-ToBashPath $outputDir
    $cacheRootBash = Convert-ToBashPath $cacheRoot
    Write-Utf8Text (Join-Path $sourceFixtureRoot 'artifacts/untracked-build-note.txt') 'untracked Android build artifact'
    $prepareCommand = 'cd {0} && "$BASH" scripts/build-android-distribution.sh --mode prepare-cache --identity {1} --output-dir {2} --cache-dir {3} --runner-os Linux --runner-arch X64' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $identityBash),
        (Quote-Bash $outputBash),
        (Quote-Bash $cacheRootBash)
    )
    $prepareOutput = Invoke-BashChecked $prepareCommand 'Android prepare-cache positive fixture failed.'

    $headMismatchIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $headMismatchIdentity.sourceSha = '0000000000000000000000000000000000000000'
    $headMismatchIdentityPath = Join-Path $tempRoot 'identity-head-mismatch.json'
    Write-Utf8Text $headMismatchIdentityPath (($headMismatchIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    $headMismatchCommand = 'cd {0} && "$BASH" scripts/build-android-distribution.sh --mode prepare-cache --identity {1} --output-dir {2} --cache-dir {3} --runner-os Linux --runner-arch X64' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash (Convert-ToBashPath $headMismatchIdentityPath)),
        (Quote-Bash (Convert-ToBashPath (Join-Path $tempRoot 'head-mismatch-output'))),
        (Quote-Bash (Convert-ToBashPath (Join-Path $tempRoot 'head-mismatch-cache')))
    )
    Assert-BashFails $headMismatchCommand 'Android prepare-cache accepted identity sourceSha that differs from exact Git HEAD.'

    $dirtyFixturePath = Join-Path $sourceFixtureRoot 'src/nuget.config'
    $dirtyFixtureBytes = [System.IO.File]::ReadAllBytes($dirtyFixturePath)
    try {
        [System.IO.File]::AppendAllText($dirtyFixturePath, "`n<!-- tracked dirty fixture -->`n", [System.Text.UTF8Encoding]::new($false))
        $dirtyTreeCommand = 'cd {0} && "$BASH" scripts/build-android-distribution.sh --mode prepare-cache --identity {1} --output-dir {2} --cache-dir {3} --runner-os Linux --runner-arch X64' -f @(
            (Quote-Bash $rootBash),
            (Quote-Bash $identityBash),
            (Quote-Bash (Convert-ToBashPath (Join-Path $tempRoot 'dirty-tree-output'))),
            (Quote-Bash (Convert-ToBashPath (Join-Path $tempRoot 'dirty-tree-cache')))
        )
        Assert-BashFails $dirtyTreeCommand 'Android prepare-cache accepted a tracked dirty source tree.'
    }
    finally {
        [System.IO.File]::WriteAllBytes($dirtyFixturePath, $dirtyFixtureBytes)
    }

    try {
        [System.IO.File]::AppendAllText($dirtyFixturePath, "`n<!-- staged dirty fixture -->`n", [System.Text.UTF8Encoding]::new($false))
        Invoke-GitChecked @('-C', $sourceFixtureRoot, 'add', '--', 'src/nuget.config') 'Unable to stage the Android index-dirty fixture.' | Out-Null
        [System.IO.File]::WriteAllBytes($dirtyFixturePath, $dirtyFixtureBytes)
        Assert-BashFails $dirtyTreeCommand 'Android prepare-cache accepted a dirty Git index.'
    }
    finally {
        Invoke-GitChecked @('-C', $sourceFixtureRoot, 'reset', '--quiet', 'HEAD', '--', 'src/nuget.config') 'Unable to reset the Android index-dirty fixture.' | Out-Null
        [System.IO.File]::WriteAllBytes($dirtyFixturePath, $dirtyFixtureBytes)
    }

    $dirtySubmodulePath = Join-Path $sourceFixtureRoot '.native/libgit2-src/fixture.txt'
    $dirtySubmoduleBytes = [System.IO.File]::ReadAllBytes($dirtySubmodulePath)
    try {
        [System.IO.File]::AppendAllText($dirtySubmodulePath, "`ntracked submodule dirty fixture`n", [System.Text.UTF8Encoding]::new($false))
        Assert-BashFails $dirtyTreeCommand 'Android prepare-cache accepted a tracked dirty submodule.'
    }
    finally {
        [System.IO.File]::WriteAllBytes($dirtySubmodulePath, $dirtySubmoduleBytes)
    }

    $prepareValues = @{}
    foreach ($line in $prepareOutput) {
        $lineText = [string]$line
        if ($lineText -match '^(native_inputs|native_input_digest|cache_key|cache_path)=(.*)$') {
            $prepareValues[$Matches[1]] = $Matches[2]
        }
    }
    foreach ($requiredOutput in 'native_inputs', 'native_input_digest', 'cache_key', 'cache_path') {
        if (-not $prepareValues.ContainsKey($requiredOutput) -or [string]::IsNullOrWhiteSpace($prepareValues[$requiredOutput])) {
            throw "prepare-cache did not emit required output: $requiredOutput"
        }
    }

    $nativeInputsPath = Join-Path $outputDir 'native-inputs.json'
    $nativeInputDigest = (Get-FileHash -Algorithm SHA256 -LiteralPath $nativeInputsPath).Hash.ToLowerInvariant()
    $cacheKey = "android-native-v2-linux-x64-$nativeInputDigest"
    if ($prepareValues.native_input_digest -ne $nativeInputDigest -or $prepareValues.cache_key -ne $cacheKey) {
        throw "prepare-cache outputs are not bound to the exact native-input bytes: digest='$($prepareValues.native_input_digest)' expected='$nativeInputDigest'; cacheKey='$($prepareValues.cache_key)' expected='$cacheKey'."
    }

    $nativeInputs = Get-Content -Raw -LiteralPath $nativeInputsPath | ConvertFrom-Json
    if ($nativeInputs.androidApiLevel -ne 23) {
        throw 'prepare-cache did not record Android API 23.'
    }
    if ((@($nativeInputs.abis) -join ',') -ne 'arm64-v8a,x86_64') {
        throw 'prepare-cache did not record the exact two-ABI set.'
    }
    if ($nativeInputs.sources.openssl.sha256 -ne 'eeca035d4dd4e84fc25846d952da6297484afa0650a6f84c682e39df3a4123ca' -or
        $nativeInputs.sources.libssh2.sha256 -ne 'd9ec76cbe34db98eec3539fe2c899d26b0c837cb3eb466a56b0f109cabf658f7' -or
        $nativeInputs.sources.upstreamNativePackage.version -ne '2.0.323' -or
        $nativeInputs.sources.upstreamNativePackage.sha256 -ne 'd2a16ac8d0b4bb4e5417e0c9fcb36f9e0e52babd6bc9c8bec0810685553feeb1' -or
        $nativeInputs.nativePackageVersion -ne '2.0.324-android.7') {
        throw 'prepare-cache did not record the reviewed source/package identities and hashes.'
    }

    $upstreamPackageRelativePath = 'nuget-local/LibGit2Sharp.NativeBinaries.2.0.323.nupkg'
    $upstreamFixturePath = Join-Path $tempRoot 'LibGit2Sharp.NativeBinaries.2.0.323.fixture.nupkg'
    Write-Utf8Text $upstreamFixturePath 'synthetic upstream native package fixture'
    $nativeInputs.sources.upstreamNativePackage.sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $upstreamFixturePath).Hash.ToLowerInvariant()
    Write-Utf8Text $nativeInputsPath (($nativeInputs | ConvertTo-Json -Depth 20 -Compress) + "`n")
    $nativeInputDigest = (Get-FileHash -Algorithm SHA256 -LiteralPath $nativeInputsPath).Hash.ToLowerInvariant()
    $cacheKey = "android-native-v2-linux-x64-$nativeInputDigest"
    $cachePath = Join-Path $cacheRoot $nativeInputDigest
    $bundlePath = Join-Path $cachePath 'bundle'
    $fixtureOutputs = @(
        $upstreamPackageRelativePath,
        'nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg',
        'android-native/libgit2-android-arm64/libgit2-3f4182d.so',
        'android-native/libgit2-android-x64/libgit2-3f4182d.so',
        'android-native/openssl-3.0.14-android-arm64-prefix/lib/libssl.so.3',
        'android-native/openssl-3.0.14-android-arm64-prefix/lib/libcrypto.so.3',
        'android-native/openssl-3.0.14-android-x64-prefix/lib/libssl.so.3',
        'android-native/openssl-3.0.14-android-x64-prefix/lib/libcrypto.so.3',
        'android-native/libssh2-1.11.1-android-arm64-prefix/lib/libssh2.so',
        'android-native/libssh2-1.11.1-android-x64-prefix/lib/libssh2.so'
    )
    foreach ($relative in $fixtureOutputs) {
        $fixturePath = Join-Path $bundlePath $relative.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fixturePath) | Out-Null
        if ($relative -ceq $upstreamPackageRelativePath) {
            Copy-Item -LiteralPath $upstreamFixturePath -Destination $fixturePath
        }
        else {
            Write-Utf8Text $fixturePath "fixture:$relative"
        }
    }
    Copy-Item -LiteralPath $nativeInputsPath -Destination (Join-Path $cachePath 'native-inputs.json') -Force

    $declaredOutputs = @(
        foreach ($relative in ($fixtureOutputs | Sort-Object)) {
            $fixturePath = Join-Path $bundlePath $relative.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
            [ordered]@{
                path = $relative
                size = (Get-Item -LiteralPath $fixturePath).Length
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $fixturePath).Hash.ToLowerInvariant()
            }
        }
    )
    $provenance = [ordered]@{
        schemaVersion = 1
        nativeInputDigest = $nativeInputDigest
        requestedCacheKey = $cacheKey
        matchedCacheKey = $cacheKey
        inputs = $nativeInputs
        outputs = $declaredOutputs
    }
    $provenancePath = Join-Path $cachePath 'native-provenance.json'
    Write-Utf8Text $provenancePath (($provenance | ConvertTo-Json -Depth 20 -Compress) + "`n")

    $provenanceCommandFor = {
        param(
            [string]$InputsPath,
            [string]$CandidateCachePath,
            [string]$RequestedKey,
            [string]$MatchedKey,
            [string]$EvidencePath
        )

        return 'cd {0} && "$BASH" scripts/test-android-distribution.sh --mode provenance --identity {1} --native-inputs {2} --cache-path {3} --requested-cache-key {4} --matched-cache-key {5} --cache-hit true --cache-save false --evidence {6}' -f @(
            (Quote-Bash $rootBash),
            (Quote-Bash $identityBash),
            (Quote-Bash (Convert-ToBashPath $InputsPath)),
            (Quote-Bash (Convert-ToBashPath $CandidateCachePath)),
            (Quote-Bash $RequestedKey),
            (Quote-Bash $MatchedKey),
            (Quote-Bash (Convert-ToBashPath $EvidencePath))
        )
    }

    $provenanceEvidencePath = Join-Path $outputDir 'provenance-evidence.json'
    $validProvenanceCommand = & $provenanceCommandFor $nativeInputsPath $cachePath $cacheKey $cacheKey $provenanceEvidencePath
    Invoke-BashChecked $validProvenanceCommand 'Exact Android native provenance positive fixture failed.' | Out-Null
    $provenanceEvidence = Get-Content -Raw -LiteralPath $provenanceEvidencePath | ConvertFrom-Json
    if ($provenanceEvidence.outcome -ne 'passed' -or $provenanceEvidence.kind -ne 'distribution-android-native-evidence' -or
        $provenanceEvidence.androidApiLevel -ne 23 -or $provenanceEvidence.outputCount -ne $fixtureOutputs.Count -or
        $provenanceEvidence.nativeInputDigest -ne $nativeInputDigest -or $provenanceEvidence.nativeInputsSha256 -ne $nativeInputDigest -or
        $provenanceEvidence.cacheHit -ne $true -or $provenanceEvidence.cacheSave -ne $false -or
        $provenanceEvidence.rawTag -ne $identity.rawTag -or $provenanceEvidence.signatureProfile -ne 'test' -or
        [string]$provenanceEvidence.nativeProvenanceSha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]$provenanceEvidence.outputClosureSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Exact Android native provenance evidence is incomplete.'
    }

    $hierarchicalOutput = Join-Path $tempRoot 'hierarchical-cache-output'
    $hierarchicalCacheRoot = Join-Path $tempRoot 'hierarchical-cache-root'
    $hierarchicalCachePath = Join-Path $hierarchicalCacheRoot $nativeInputDigest
    New-Item -ItemType Directory -Force -Path $hierarchicalOutput, $hierarchicalCacheRoot | Out-Null
    Copy-Item -LiteralPath $nativeInputsPath -Destination (Join-Path $hierarchicalOutput 'native-inputs.json')
    Copy-Item -LiteralPath $cachePath -Destination $hierarchicalCachePath -Recurse
    $nestedPackagePath = Join-Path $hierarchicalCachePath 'bundle/nuget-local/nested/public-substitute.nupkg'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $nestedPackagePath) | Out-Null
    Write-Utf8Text $nestedPackagePath 'hierarchical cache package fixture'
    $hierarchicalProvenancePath = Join-Path $hierarchicalCachePath 'native-provenance.json'
    $hierarchicalProvenance = Get-Content -Raw -LiteralPath $hierarchicalProvenancePath | ConvertFrom-Json
    $hierarchicalProvenance.outputs = @($hierarchicalProvenance.outputs) + [pscustomobject][ordered]@{
        path = 'nuget-local/nested/public-substitute.nupkg'
        size = (Get-Item -LiteralPath $nestedPackagePath).Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $nestedPackagePath).Hash.ToLowerInvariant()
    }
    Write-Utf8Text $hierarchicalProvenancePath (($hierarchicalProvenance | ConvertTo-Json -Depth 20 -Compress) + "`n")
    $hierarchicalBuildCommand = 'cd {0} && "$BASH" scripts/build-android-distribution.sh --mode build --identity {1} --output-dir {2} --cache-dir {3} --requested-cache-key {4} --matched-cache-key {4} --cache-hit true --runner-os Linux --runner-arch X64' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $hierarchicalOutput)),
        (Quote-Bash (Convert-ToBashPath $hierarchicalCacheRoot)),
        (Quote-Bash $cacheKey)
    )
    Assert-BashFails $hierarchicalBuildCommand 'Android cache-hit build accepted a declared nested local-feed package.'
    $generatedNuGetConfigPath = Join-Path $hierarchicalOutput 'nuget.config'
    if (-not (Test-Path -LiteralPath $generatedNuGetConfigPath -PathType Leaf)) {
        throw 'Android cache-hit fixture did not generate the isolated NuGet config before rejecting the hierarchical feed.'
    }
    $generatedNuGetConfig = Get-Content -Raw -LiteralPath $generatedNuGetConfigPath
    Assert-GeneratedAndroidNuGetConfig $generatedNuGetConfig
    $nugetConfigParseOutput = @(& dotnet nuget list source --configfile $generatedNuGetConfigPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet rejected the generated Android NuGet config.`n$($nugetConfigParseOutput -join [Environment]::NewLine)"
    }

    [xml]$publicSubstitutionConfig = $generatedNuGetConfig
    $publicSubstitutionConfig.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="local"]').SetAttribute('key', 'nuget.org')
    Assert-Throws {
        Assert-GeneratedAndroidNuGetConfig $publicSubstitutionConfig.OuterXml
    } 'Generated Android NuGet config validator accepted private package IDs remapped to nuget.org.'

    [xml]$inexactPrivateIdConfig = $generatedNuGetConfig
    $inexactPrivateIdConfig.SelectSingleNode('/configuration/packageSourceMapping/packageSource[@key="local"]/package[@pattern="NodifyAvalonia"]').SetAttribute('pattern', 'NodifyAvalonia.*')
    Assert-Throws {
        Assert-GeneratedAndroidNuGetConfig $inexactPrivateIdConfig.OuterXml
    } 'Generated Android NuGet config validator accepted an inexact private package ID mapping.'

    $cacheMissEvidencePath = Join-Path $outputDir 'provenance-cache-miss-evidence.json'
    $cacheMissCommand = (& $provenanceCommandFor $nativeInputsPath $cachePath $cacheKey $cacheKey $cacheMissEvidencePath).
        Replace('--cache-hit true --cache-save false', '--cache-hit false --cache-save true')
    Invoke-BashChecked $cacheMissCommand 'Validated Android cache-save provenance fixture failed.' | Out-Null
    $cacheMissEvidence = Get-Content -Raw -LiteralPath $cacheMissEvidencePath | ConvertFrom-Json
    if ($cacheMissEvidence.cacheHit -ne $false -or $cacheMissEvidence.cacheSave -ne $true) {
        throw 'Android provenance evidence did not preserve the validated cache-save outcome.'
    }

    $invalidCacheOutcomeCommand = $validProvenanceCommand.Replace('--cache-hit true --cache-save false', '--cache-hit false --cache-save false')
    Assert-BashFails $invalidCacheOutcomeCommand 'Android provenance validator accepted an impossible cache hit/save outcome.'

    Assert-BashFails (
        & $provenanceCommandFor $nativeInputsPath $cachePath $cacheKey "$cacheKey-prefix" (Join-Path $outputDir 'wrong-key.json')
    ) 'Android provenance validator accepted a requested/matched cache-key mismatch.'

    $wrongPrefixKey = "android-native-v1-linux-x64-$nativeInputDigest"
    Assert-BashFails (
        & $provenanceCommandFor $nativeInputsPath $cachePath $wrongPrefixKey $wrongPrefixKey (Join-Path $outputDir 'wrong-prefix.json')
    ) 'Android provenance validator accepted a non-v2 key with the correct digest suffix.'

    $cachedInputsPath = Join-Path $cachePath 'native-inputs.json'
    $cachedInputsBytes = [System.IO.File]::ReadAllBytes($cachedInputsPath)
    Write-Utf8Text $cachedInputsPath (($nativeInputs | ConvertTo-Json -Depth 20) + "`n")
    Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted reformatted cached native-input bytes.'
    [System.IO.File]::WriteAllBytes($cachedInputsPath, $cachedInputsBytes)

    $nupkgPath = Join-Path $bundlePath 'nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg'
    $nupkgBytes = [System.IO.File]::ReadAllBytes($nupkgPath)
    [System.IO.File]::AppendAllText($nupkgPath, 'mutated', [System.Text.UTF8Encoding]::new($false))
    Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted mutated nupkg bytes.'
    [System.IO.File]::WriteAllBytes($nupkgPath, $nupkgBytes)

    $upstreamNupkgPath = Join-Path $bundlePath $upstreamPackageRelativePath.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
    $upstreamNupkgBytes = [System.IO.File]::ReadAllBytes($upstreamNupkgPath)
    $provenanceBytes = [System.IO.File]::ReadAllBytes($provenancePath)
    try {
        [System.IO.File]::AppendAllText($upstreamNupkgPath, 'pin-mismatch', [System.Text.UTF8Encoding]::new($false))
        $pinMismatchProvenance = $provenance | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $pinMismatchEntry = @($pinMismatchProvenance.outputs | Where-Object { $_.path -ceq $upstreamPackageRelativePath })
        if ($pinMismatchEntry.Count -ne 1) {
            throw 'Unable to locate the synthetic upstream package in Android provenance.'
        }
        $pinMismatchEntry[0].size = (Get-Item -LiteralPath $upstreamNupkgPath).Length
        $pinMismatchEntry[0].sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $upstreamNupkgPath).Hash.ToLowerInvariant()
        Write-Utf8Text $provenancePath (($pinMismatchProvenance | ConvertTo-Json -Depth 20 -Compress) + "`n")
        Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted an upstream nupkg whose declared bytes differ from the pinned input SHA-256.'
    }
    finally {
        [System.IO.File]::WriteAllBytes($upstreamNupkgPath, $upstreamNupkgBytes)
        [System.IO.File]::WriteAllBytes($provenancePath, $provenanceBytes)
    }

    $extraPackageRelativePath = 'nuget-local/LibGit2Sharp.NativeBinaries.2.0.999.nupkg'
    $extraPackagePath = Join-Path $bundlePath $extraPackageRelativePath.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
    try {
        Write-Utf8Text $extraPackagePath 'unexpected local-feed package fixture'
        $extraPackageProvenance = $provenance | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $extraPackageProvenance.outputs = @($extraPackageProvenance.outputs) + [pscustomobject][ordered]@{
            path = $extraPackageRelativePath
            size = (Get-Item -LiteralPath $extraPackagePath).Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $extraPackagePath).Hash.ToLowerInvariant()
        }
        Write-Utf8Text $provenancePath (($extraPackageProvenance | ConvertTo-Json -Depth 20 -Compress) + "`n")
        Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted an extra declared local-feed package.'
    }
    finally {
        Remove-Item -LiteralPath $extraPackagePath -ErrorAction SilentlyContinue
        [System.IO.File]::WriteAllBytes($provenancePath, $provenanceBytes)
    }

    $provenanceBackup = "$provenancePath.missing"
    Move-Item -LiteralPath $provenancePath -Destination $provenanceBackup
    Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted a cache without native-provenance.json.'
    Move-Item -LiteralPath $provenanceBackup -Destination $provenancePath

    $partialOutputPath = Join-Path $bundlePath 'android-native/libgit2-android-x64/libgit2-3f4182d.so'
    $partialOutputBytes = [System.IO.File]::ReadAllBytes($partialOutputPath)
    Remove-Item -LiteralPath $partialOutputPath
    Assert-BashFails $validProvenanceCommand 'Android provenance validator accepted a partial two-ABI cache bundle.'
    [System.IO.File]::WriteAllBytes($partialOutputPath, $partialOutputBytes)

    $api24Inputs = Get-Content -Raw -LiteralPath $nativeInputsPath | ConvertFrom-Json
    $api24Inputs.androidApiLevel = 24
    $api24InputsPath = Join-Path $tempRoot 'native-inputs-api24.json'
    Write-Utf8Text $api24InputsPath (($api24Inputs | ConvertTo-Json -Depth 20 -Compress) + "`n")
    $api24Digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $api24InputsPath).Hash.ToLowerInvariant()
    $api24Key = "android-native-v2-linux-x64-$api24Digest"
    $api24CachePath = Join-Path $tempRoot 'cache-api24'
    Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $api24CachePath 'bundle') -Recurse
    Copy-Item -LiteralPath $api24InputsPath -Destination (Join-Path $api24CachePath 'native-inputs.json')
    $api24Outputs = @(
        foreach ($relative in ($fixtureOutputs | Sort-Object)) {
            $fixturePath = Join-Path (Join-Path $api24CachePath 'bundle') $relative.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar)
            [ordered]@{
                path = $relative
                size = (Get-Item -LiteralPath $fixturePath).Length
                sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $fixturePath).Hash.ToLowerInvariant()
            }
        }
    )
    $api24Provenance = [ordered]@{
        schemaVersion = 1
        nativeInputDigest = $api24Digest
        requestedCacheKey = $api24Key
        matchedCacheKey = $api24Key
        inputs = $api24Inputs
        outputs = $api24Outputs
    }
    Write-Utf8Text (Join-Path $api24CachePath 'native-provenance.json') (($api24Provenance | ConvertTo-Json -Depth 20 -Compress) + "`n")
    $api24Command = & $provenanceCommandFor $api24InputsPath $api24CachePath $api24Key $api24Key (Join-Path $outputDir 'api24.json')
    Assert-BashFails $api24Command 'Android provenance validator accepted API-24 provenance for the API-23 contract.'

    $symlinkTargetPath = Join-Path $tempRoot 'bundle-symlink-target'
    Copy-Item -LiteralPath $bundlePath -Destination $symlinkTargetPath -Recurse
    $symlinkCachePath = Join-Path $tempRoot 'cache-symlink-root'
    New-Item -ItemType Directory -Path $symlinkCachePath | Out-Null
    Copy-Item -LiteralPath $cachedInputsPath -Destination (Join-Path $symlinkCachePath 'native-inputs.json')
    Copy-Item -LiteralPath $provenancePath -Destination (Join-Path $symlinkCachePath 'native-provenance.json')
    $symlinkTargetBash = Quote-Bash (Convert-ToBashPath $symlinkTargetPath)
    $symlinkBundleBash = Quote-Bash (Convert-ToBashPath (Join-Path $symlinkCachePath 'bundle'))
    $createBundleSymlink = 'MSYS=winsymlinks:nativestrict ln -s -- {0} {1} && test -L {1}' -f @(
        $symlinkTargetBash,
        $symlinkBundleBash
    )
    Invoke-BashChecked $createBundleSymlink 'Unable to create the Android bundle-root symlink negative fixture.' | Out-Null
    Assert-BashFails (
        & $provenanceCommandFor $nativeInputsPath $symlinkCachePath $cacheKey $cacheKey (Join-Path $outputDir 'bundle-root-symlink.json')
    ) 'Android provenance validator accepted a symlinked cache bundle root.'

    $fakeBin = Join-Path $tempRoot 'fake-android-bin'
    $fakeSdk = Join-Path $tempRoot 'fake-android-sdk'
    $fakeBuildTools = Join-Path $fakeSdk 'build-tools\fixture'
    $fakeSystemImage = Join-Path $fakeSdk 'system-images\android-23\google_apis\x86_64'
    $emulatorInput = Join-Path $tempRoot 'emulator-input'
    $emulatorFailureRoot = Join-Path $tempRoot 'emulator-failure'
    $emulatorSetupFailureRoot = Join-Path $tempRoot 'emulator-setup-failure'
    $fakeAvdHome = Join-Path $tempRoot 'fake-android-avd-home'
    New-Item -ItemType Directory -Path $fakeBin, $fakeBuildTools, $fakeSystemImage, $emulatorInput, $emulatorFailureRoot, $emulatorSetupFailureRoot, $fakeAvdHome | Out-Null
    Write-Utf8Text (Join-Path $fakeBin 'adb') (@'
#!/bin/bash
if [ "${1:-}" = "version" ]; then
  echo "Android Debug Bridge version 1.0.41"
elif [[ "$*" == *"getprop sys.boot_completed"* ]]; then
  if [ "${FAKE_ADB_HANG_ON_BOOT:-false}" = "true" ]; then
    exec sleep 60
  fi
  echo "${FAKE_ADB_BOOT_COMPLETED:-0}"
elif [[ "$*" == *"getprop init.svc.bootanim"* ]]; then
  echo "${FAKE_ADB_BOOT_ANIMATION:-running}"
elif [[ "$*" == *"getprop ro.build.fingerprint"* ]]; then
  echo "fixture/device/fingerprint"
elif [[ "$*" == *"getprop ro.build.version.sdk"* ]]; then
  echo "${FAKE_ADB_DEVICE_SDK:-23}"
elif [[ "$*" == *"get-state"* ]]; then
  echo "device"
elif [[ "$*" == *"install -r"* ]]; then
  if [ "${FAKE_ADB_INSTALL_DELAY_SECONDS:-0}" != "0" ]; then
    sleep "$FAKE_ADB_INSTALL_DELAY_SECONDS"
  fi
elif [[ "$*" == *"shell am force-stop"* ]]; then
  if [ "${FAKE_ADB_FORCE_STOP_DELAY_SECONDS:-0}" != "0" ]; then
    sleep "$FAKE_ADB_FORCE_STOP_DELAY_SECONDS"
  fi
elif [[ "$*" == *"emu kill"* ]]; then
  if [ "${FAKE_ADB_EMU_KILL_DELAY_SECONDS:-0}" != "0" ]; then
    sleep "$FAKE_ADB_EMU_KILL_DELAY_SECONDS"
  fi
elif [[ "$*" == *"shell pidof"* ]]; then
  if [ "${FAKE_ADB_NO_PROCESS:-false}" != "true" ]; then
    echo "4242"
  fi
elif [[ "$*" == *"logcat -d"* ]]; then
  echo "fixture logcat: application started"
elif [[ "$*" == *"shell am start -W"* ]]; then
  if [ "${FAKE_ADB_HANG_ON_ACTIVITY_WAIT:-false}" = "true" ]; then
    exec sleep 60
  fi
fi
'@.Replace("`r`n", "`n"))
    Write-Utf8Text (Join-Path $fakeBin 'emulator') (@'
#!/bin/bash
if [ "${1:-}" = "-version" ]; then
  echo "Android emulator version fixture"
  exit 0
fi
if [ "${1:-}" = "-avd" ]; then
  avd_name="${2:-}"
  if [ -z "${ANDROID_AVD_HOME:-}" ] || [ -z "$avd_name" ] || \
     [ ! -f "$ANDROID_AVD_HOME/$avd_name.ini" ] || [ ! -d "$ANDROID_AVD_HOME/$avd_name.avd" ]; then
    echo "fixture emulator could not resolve isolated AVD $avd_name" >&2
    exit 23
  fi
fi
echo "fixture emulator boot attempt: $*"
exec sleep 60
'@.Replace("`r`n", "`n"))
    Write-Utf8Text (Join-Path $fakeBin 'sdkmanager') (@'
#!/bin/bash
exit 0
'@.Replace("`r`n", "`n"))
    Write-Utf8Text (Join-Path $fakeBin 'avdmanager') (@'
#!/bin/bash
if [ "${1:-}" = "create" ]; then
  attempt=1
  avd_name=""
  while [ "$#" -gt 0 ]; do
    if [ "$1" = "--name" ] && [ "$#" -ge 2 ]; then
      avd_name="$2"
      shift 2
      continue
    fi
    shift
  done
  if [ -n "${FAKE_AVDMANAGER_STATE_FILE:-}" ]; then
    if [ -f "$FAKE_AVDMANAGER_STATE_FILE" ]; then
      attempt=$(( $(cat "$FAKE_AVDMANAGER_STATE_FILE") + 1 ))
    fi
    printf '%s\n' "$attempt" > "$FAKE_AVDMANAGER_STATE_FILE"
  fi
  echo "fixture avdmanager setup attempt $attempt"
  if [ -n "${FAKE_AVDMANAGER_FAIL_CREATE_ATTEMPT:-}" ] && [ "$attempt" = "$FAKE_AVDMANAGER_FAIL_CREATE_ATTEMPT" ]; then
    echo "fixture avdmanager setup failure on attempt $attempt" >&2
    exit 17
  fi
  if [ -n "${ANDROID_AVD_HOME:-}" ] && [ -n "$avd_name" ]; then
    mkdir -p "$ANDROID_AVD_HOME/$avd_name.avd"
    : > "$ANDROID_AVD_HOME/$avd_name.ini"
    if [ -n "${FAKE_AVDMANAGER_ROOT_RECORD:-}" ]; then
      printf '%s\n' "$ANDROID_AVD_HOME" > "$FAKE_AVDMANAGER_ROOT_RECORD"
    fi
  fi
fi
exit 0
'@.Replace("`r`n", "`n"))
    Write-Utf8Text (Join-Path $fakeBuildTools 'aapt') (@'
#!/bin/bash
if [ "${1:-}" = "version" ]; then
  echo "Android Asset Packaging Tool, v0.2"
elif [ "${1:-}" = "dump" ] && [ "${2:-}" = "badging" ]; then
  echo "launchable-activity: name='MainActivity'"
fi
'@.Replace("`r`n", "`n"))
    Write-Utf8Text (Join-Path $fakeSystemImage 'package.xml') '<repository><localPackage><revision><major>10</major></revision></localPackage></repository>'
    $emulatorApkPath = Join-Path $emulatorInput $identity.filenamePlan.android.x64Apk
    Write-Utf8Text $emulatorApkPath 'fixture-apk'
    $chmodFixtureTools = 'chmod +x -- {0}/* {1}' -f @(
        (Quote-Bash (Convert-ToBashPath $fakeBin)),
        (Quote-Bash (Convert-ToBashPath (Join-Path $fakeBuildTools 'aapt')))
    )
    Invoke-BashChecked $chmodFixtureTools 'Unable to make fake Android fixture tools executable.' | Out-Null

    $emulatorFailureEvidencePath = Join-Path $emulatorFailureRoot 'evidence.json'
    $avdRootRecordPath = Join-Path $emulatorFailureRoot 'avd-root.txt'
    $pythonDirectoryOutput = @(
        & $bashCommand --noprofile --norc -c 'command -v python3 >/dev/null 2>&1 && dirname -- "$(command -v python3)"' 2>&1
    )
    $pythonDirectoryExitCode = $LASTEXITCODE
    $pythonDirectory = ($pythonDirectoryOutput -join "`n").Trim()
    if ($pythonDirectoryExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($pythonDirectory) -or
        $pythonDirectory -notmatch '^/' -or $pythonDirectory.Contains("`n")) {
        throw "Unable to resolve the Python 3 directory from Bash '$bashCommand'.`n$($pythonDirectoryOutput -join [Environment]::NewLine)"
    }
    $fixturePosixPath = (Convert-ToBashPath $fakeBin) + ":${pythonDirectory}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
    $emulatorFailureCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_AVDMANAGER_ROOT_RECORD={4} UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {5} --input-dir {6} --api-level 23 --evidence {7}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash (Convert-ToBashPath $avdRootRecordPath)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorFailureEvidencePath))
    )
    $emulatorFailureOutput = & $bashCommand -lc $emulatorFailureCommand 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Fake Android emulator exhaustion unexpectedly succeeded.'
    }
    if (-not (Test-Path -LiteralPath $emulatorFailureEvidencePath -PathType Leaf)) {
        throw "Android emulator exhaustion did not write structured evidence.`n$($emulatorFailureOutput -join [Environment]::NewLine)"
    }
    if (-not (Test-Path -LiteralPath $avdRootRecordPath -PathType Leaf)) {
        throw 'Fake Android emulator did not receive an isolated AVD root from avdmanager.'
    }
    $recordedAvdRoot = (Get-Content -Raw -LiteralPath $avdRootRecordPath).Trim()
    if ([string]::IsNullOrWhiteSpace($recordedAvdRoot)) {
        throw 'Fake Android emulator recorded an empty isolated AVD root.'
    }
    $avdRootCleanupCommand = 'test ! -e {0}' -f (Quote-Bash $recordedAvdRoot)
    $avdRootCleanupOutput = & $bashCommand -lc $avdRootCleanupCommand 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Android emulator validation leaked its isolated AVD root after failure cleanup.`n$($avdRootCleanupOutput -join [Environment]::NewLine)"
    }
    $emulatorFailure = Get-Content -Raw -LiteralPath $emulatorFailureEvidencePath | ConvertFrom-Json
    foreach ($field in @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256', 'signatureProfile')) {
        if ([string]$emulatorFailure.$field -cne [string]$identity.$field) {
            throw "Android emulator failure evidence identity field '$field' is not exact."
        }
    }
    if ($emulatorFailure.kind -cne 'distribution-android-native-evidence' -or $emulatorFailure.outcome -cne 'failed' -or
        $emulatorFailure.productionReady -ne $false -or $emulatorFailure.runtime.apiLevel -ne 23 -or
        $emulatorFailure.bootRetry.attempts -ne 2 -or $emulatorFailure.bootRetry.maxAttempts -ne 2 -or
        $emulatorFailure.bootRetry.classification -cne 'transient-emulator-boot' -or
        $emulatorFailure.bootRetry.cleanupBeforeAttempt2 -cne 'kill-delete-avd-remove-files-and-wipe-data' -or
        (@($emulatorFailure.bootRetry.outcomes) -join '|') -cne 'failure|failure' -or
        $emulatorFailure.bootRetry.exhausted -ne $true -or
        $emulatorFailure.failureClassification -cne 'transient-emulator-boot' -or
        [string]::IsNullOrWhiteSpace([string]$emulatorFailure.terminalError) -or
        @($emulatorFailure.attemptLogs).Count -ne 2) {
        throw 'Android emulator exhaustion evidence is incomplete or semantically inconsistent.'
    }
    foreach ($attemptLog in @($emulatorFailure.attemptLogs)) {
        if ([string]$attemptLog.fileName -cne "android-api23-emulator-attempt$($attemptLog.attempt).log" -or
            [long]$attemptLog.bytes -le 0 -or [string]$attemptLog.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw 'Android emulator failure evidence contains an invalid per-attempt log reference.'
        }
        $attemptLogPath = Join-Path $emulatorFailureRoot ([string]$attemptLog.fileName)
        if (-not (Test-Path -LiteralPath $attemptLogPath -PathType Leaf) -or
            (Get-Item -LiteralPath $attemptLogPath).Length -ne [long]$attemptLog.bytes -or
            (Get-FileHash -Algorithm SHA256 -LiteralPath $attemptLogPath).Hash.ToLowerInvariant() -cne [string]$attemptLog.sha256) {
            throw 'Android emulator failure log reference does not match exact preserved bytes.'
        }
    }

    $emulatorBoundedRoot = Join-Path $tempRoot 'emulator-bounded'
    New-Item -ItemType Directory -Path $emulatorBoundedRoot | Out-Null
    $emulatorBoundedEvidencePath = Join-Path $emulatorBoundedRoot 'evidence.json'
    $emulatorBoundedCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_HANG_ON_BOOT=true UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorBoundedEvidencePath))
    )
    $emulatorBoundedStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $emulatorBoundedOutput = & $bashCommand -lc $emulatorBoundedCommand 2>&1
    $emulatorBoundedStopwatch.Stop()
    if ($LASTEXITCODE -eq 0) {
        throw 'Hanging fake adb boot probe unexpectedly succeeded.'
    }
    if ($emulatorBoundedStopwatch.Elapsed.TotalSeconds -gt 12) {
        throw "Hanging fake adb boot probe exceeded its bounded timeout: $([math]::Round($emulatorBoundedStopwatch.Elapsed.TotalSeconds, 2)) seconds."
    }
    if (-not (Test-Path -LiteralPath $emulatorBoundedEvidencePath -PathType Leaf)) {
        throw "Hanging fake adb boot probe did not write failure evidence.`n$($emulatorBoundedOutput -join [Environment]::NewLine)"
    }
    $emulatorBoundedEvidence = Get-Content -Raw -LiteralPath $emulatorBoundedEvidencePath | ConvertFrom-Json
    if ($emulatorBoundedEvidence.outcome -cne 'failed' -or
        $emulatorBoundedEvidence.bootRetry.attempts -ne 2 -or
        (@($emulatorBoundedEvidence.bootRetry.outcomes) -join '|') -cne 'failure|failure') {
        throw 'Hanging fake adb boot probe produced incomplete bounded failure evidence.'
    }
    $emulatorBoundedLogPath = Join-Path $emulatorBoundedRoot 'android-api23-emulator-attempt1.log'
    if (-not (Test-Path -LiteralPath $emulatorBoundedLogPath -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath $emulatorBoundedLogPath) -cnotmatch 'adb command result: exit=124 .*getprop sys\.boot_completed') {
        throw 'Hanging fake adb boot probe did not preserve the bounded command identity and exit status.'
    }

    $emulatorReadyRoot = Join-Path $tempRoot 'emulator-ready'
    New-Item -ItemType Directory -Path $emulatorReadyRoot | Out-Null
    $emulatorReadyEvidencePath = Join-Path $emulatorReadyRoot 'evidence.json'
    $emulatorReadyCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_BOOT_COMPLETED=1 FAKE_ADB_BOOT_ANIMATION=running FAKE_ADB_HANG_ON_ACTIVITY_WAIT=true UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorReadyEvidencePath))
    )
    $emulatorReadyOutput = & $bashCommand -lc $emulatorReadyCommand 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Android emulator readiness fixture rejected sys.boot_completed=1 while bootanim remained running.`n$($emulatorReadyOutput -join [Environment]::NewLine)"
    }
    if (-not (Test-Path -LiteralPath $emulatorReadyEvidencePath -PathType Leaf)) {
        throw 'Android emulator readiness fixture did not write success evidence.'
    }
    $emulatorReadyEvidence = Get-Content -Raw -LiteralPath $emulatorReadyEvidencePath | ConvertFrom-Json
    if ($emulatorReadyEvidence.outcome -cne 'passed' -or
        $emulatorReadyEvidence.supportLevel -cne 'launchVerified' -or
        $emulatorReadyEvidence.runtime.apiLevel -ne 23 -or
        $emulatorReadyEvidence.runtime.deviceSdk -ne 23 -or
        [string]$emulatorReadyEvidence.runtime.processId -cne '4242' -or
        $emulatorReadyEvidence.bootRetry.attempts -ne 1 -or
        (@($emulatorReadyEvidence.bootRetry.outcomes) -join '|') -cne 'success') {
        throw 'Android emulator readiness fixture produced incomplete success evidence.'
    }
    $emulatorReadyLogPath = Join-Path $emulatorReadyRoot 'android-api23-emulator.log'
    if (-not (Test-Path -LiteralPath $emulatorReadyLogPath -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath $emulatorReadyLogPath) -cnotmatch 'readiness poll: .*sys\.boot_completed=1.*init\.svc\.bootanim=running') {
        throw 'Android emulator readiness fixture did not retain the observed ADB readiness state.'
    }

    $emulatorInstallDelayRoot = Join-Path $tempRoot 'emulator-install-delay'
    New-Item -ItemType Directory -Path $emulatorInstallDelayRoot | Out-Null
    $emulatorInstallDelayEvidencePath = Join-Path $emulatorInstallDelayRoot 'evidence.json'
    $emulatorInstallDelayCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_BOOT_COMPLETED=1 FAKE_ADB_BOOT_ANIMATION=running FAKE_ADB_INSTALL_DELAY_SECONDS=2 UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_INSTALL_TIMEOUT_SECONDS=3 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorInstallDelayEvidencePath))
    )
    $emulatorInstallDelayOutput = & $bashCommand -lc $emulatorInstallDelayCommand 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Android emulator delayed-install fixture did not use its separate bounded timeout.`n$($emulatorInstallDelayOutput -join [Environment]::NewLine)"
    }
    $emulatorInstallDelayEvidence = Get-Content -Raw -LiteralPath $emulatorInstallDelayEvidencePath | ConvertFrom-Json
    if ($emulatorInstallDelayEvidence.outcome -cne 'passed' -or $emulatorInstallDelayEvidence.supportLevel -cne 'launchVerified') {
        throw 'Android emulator delayed-install fixture did not produce launch evidence.'
    }

    $emulatorNoProcessRoot = Join-Path $tempRoot 'emulator-no-process'
    New-Item -ItemType Directory -Path $emulatorNoProcessRoot | Out-Null
    $emulatorNoProcessEvidencePath = Join-Path $emulatorNoProcessRoot 'evidence.json'
    $emulatorNoProcessCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_BOOT_COMPLETED=1 FAKE_ADB_BOOT_ANIMATION=running FAKE_ADB_NO_PROCESS=true UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorNoProcessEvidencePath))
    )
    $emulatorNoProcessOutput = & $bashCommand -lc $emulatorNoProcessCommand 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Android emulator missing-process fixture unexpectedly succeeded.'
    }
    $emulatorNoProcessLogcatPath = Join-Path $emulatorNoProcessRoot 'android-api23-logcat.txt'
    if (-not (Test-Path -LiteralPath $emulatorNoProcessEvidencePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $emulatorNoProcessLogcatPath -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath $emulatorNoProcessLogcatPath) -cnotmatch 'fixture logcat: application started') {
        throw "Android emulator missing-process fixture did not preserve logcat before failure.`n$($emulatorNoProcessOutput -join [Environment]::NewLine)"
    }

    $emulatorInstallTimeoutRoot = Join-Path $tempRoot 'emulator-install-timeout'
    New-Item -ItemType Directory -Path $emulatorInstallTimeoutRoot | Out-Null
    $emulatorInstallTimeoutEvidencePath = Join-Path $emulatorInstallTimeoutRoot 'evidence.json'
    $emulatorInstallTimeoutCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_BOOT_COMPLETED=1 FAKE_ADB_BOOT_ANIMATION=running FAKE_ADB_INSTALL_DELAY_SECONDS=4 FAKE_ADB_EMU_KILL_DELAY_SECONDS=2 UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_INSTALL_TIMEOUT_SECONDS=3 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorInstallTimeoutEvidencePath))
    )
    $emulatorInstallTimeoutStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $emulatorInstallTimeoutOutput = & $bashCommand -lc $emulatorInstallTimeoutCommand 2>&1
    $emulatorInstallTimeoutStopwatch.Stop()
    if ($LASTEXITCODE -eq 0) {
        throw 'Android emulator over-limit install fixture unexpectedly succeeded.'
    }
    if ($emulatorInstallTimeoutStopwatch.Elapsed.TotalSeconds -lt 2.5 -or $emulatorInstallTimeoutStopwatch.Elapsed.TotalSeconds -gt 12) {
        throw "Android emulator over-limit install fixture did not use the bounded install timeout: $([math]::Round($emulatorInstallTimeoutStopwatch.Elapsed.TotalSeconds, 2)) seconds."
    }
    $emulatorInstallTimeoutLogPath = Join-Path $emulatorInstallTimeoutRoot 'android-api23-emulator-attempt1.log'
    $emulatorInstallTimeoutEvidenceExists = Test-Path -LiteralPath $emulatorInstallTimeoutEvidencePath -PathType Leaf
    $emulatorInstallTimeoutLogExists = Test-Path -LiteralPath $emulatorInstallTimeoutLogPath -PathType Leaf
    $emulatorInstallTimeoutLog = if ($emulatorInstallTimeoutLogExists) {
        Get-Content -Raw -LiteralPath $emulatorInstallTimeoutLogPath
    } else {
        ''
    }
    if (-not $emulatorInstallTimeoutEvidenceExists -or
        -not $emulatorInstallTimeoutLogExists -or
        $emulatorInstallTimeoutLog -cnotmatch 'adb command result: exit=124 .*install -r' -or
        $emulatorInstallTimeoutLog -cnotmatch 'adb command result: exit=124 .*emu kill') {
        throw "Android emulator over-limit install fixture did not preserve bounded failure evidence (evidence=$emulatorInstallTimeoutEvidenceExists, log=$emulatorInstallTimeoutLogExists).`n$emulatorInstallTimeoutLog`n$($emulatorInstallTimeoutOutput -join [Environment]::NewLine)"
    }

    $emulatorPostInstallCommandRoot = Join-Path $tempRoot 'emulator-post-install-command-timeout'
    New-Item -ItemType Directory -Path $emulatorPostInstallCommandRoot | Out-Null
    $emulatorPostInstallCommandEvidencePath = Join-Path $emulatorPostInstallCommandRoot 'evidence.json'
    $emulatorPostInstallCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_ADB_BOOT_COMPLETED=1 FAKE_ADB_BOOT_ANIMATION=running FAKE_ADB_INSTALL_DELAY_SECONDS=2 FAKE_ADB_FORCE_STOP_DELAY_SECONDS=2 UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 UNLIMOTION_ANDROID_EMULATOR_COMMAND_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_INSTALL_TIMEOUT_SECONDS=3 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {4} --input-dir {5} --api-level 23 --evidence {6}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorPostInstallCommandEvidencePath))
    )
    $emulatorPostInstallCommandOutput = & $bashCommand -lc $emulatorPostInstallCommand 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Android emulator post-install generic-command fixture unexpectedly succeeded.'
    }
    $emulatorPostInstallCommandLogPath = Join-Path $emulatorPostInstallCommandRoot 'android-api23-emulator-attempt1.log'
    if (-not (Test-Path -LiteralPath $emulatorPostInstallCommandEvidencePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $emulatorPostInstallCommandLogPath -PathType Leaf)) {
        throw "Android emulator post-install generic-command fixture did not write failure evidence.`n$($emulatorPostInstallCommandOutput -join [Environment]::NewLine)"
    }
    $emulatorPostInstallCommandLog = Get-Content -Raw -LiteralPath $emulatorPostInstallCommandLogPath
    if ($emulatorPostInstallCommandLog -cnotmatch 'adb command result: exit=0 .*install -r' -or
        $emulatorPostInstallCommandLog -cnotmatch 'adb command result: exit=124 .*shell am force-stop') {
        throw 'Android emulator post-install generic-command fixture did not restore the general bounded timeout.'
    }

    $emulatorSetupFailureEvidencePath = Join-Path $emulatorSetupFailureRoot 'evidence.json'
    $avdmanagerStatePath = Join-Path $emulatorSetupFailureRoot 'avdmanager-state.txt'
    $emulatorSetupFailureCommand = 'cd {0} && env PATH={1} ANDROID_AVD_HOME={2} ANDROID_SDK_ROOT={3} ANDROID_BUILD_TOOLS=fixture ImageOS=fixture-os ImageVersion=fixture-version FAKE_AVDMANAGER_STATE_FILE={4} FAKE_AVDMANAGER_FAIL_CREATE_ATTEMPT=2 UNLIMOTION_ANDROID_EMULATOR_BOOT_TIMEOUT_SECONDS=1 UNLIMOTION_ANDROID_EMULATOR_BOOT_POLL_SECONDS=0.1 "$BASH" scripts/test-android-distribution.sh --mode emulator --identity {5} --input-dir {6} --api-level 23 --evidence {7}' -f @(
        (Quote-Bash $rootBash),
        (Quote-Bash $fixturePosixPath),
        (Quote-Bash (Convert-ToBashPath $fakeAvdHome)),
        (Quote-Bash (Convert-ToBashPath $fakeSdk)),
        (Quote-Bash (Convert-ToBashPath $avdmanagerStatePath)),
        (Quote-Bash $identityBash),
        (Quote-Bash (Convert-ToBashPath $emulatorInput)),
        (Quote-Bash (Convert-ToBashPath $emulatorSetupFailureEvidencePath))
    )
    $emulatorSetupFailureOutput = & $bashCommand -lc $emulatorSetupFailureCommand 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Fake Android emulator setup failure unexpectedly succeeded.'
    }
    if (-not (Test-Path -LiteralPath $emulatorSetupFailureEvidencePath -PathType Leaf)) {
        throw "Android emulator setup failure did not write structured evidence.`n$($emulatorSetupFailureOutput -join [Environment]::NewLine)"
    }
    $emulatorSetupFailure = Get-Content -Raw -LiteralPath $emulatorSetupFailureEvidencePath | ConvertFrom-Json
    if ($emulatorSetupFailure.kind -cne 'distribution-android-native-evidence' -or
        $emulatorSetupFailure.outcome -cne 'failed' -or
        $emulatorSetupFailure.bootRetry.attempts -ne 2 -or
        (@($emulatorSetupFailure.bootRetry.outcomes) -join '|') -cne 'failure|failure' -or
        $emulatorSetupFailure.bootRetry.exhausted -ne $true -or
        @($emulatorSetupFailure.attemptLogs).Count -ne 2) {
        throw 'Android emulator setup failure evidence is incomplete or semantically inconsistent.'
    }
    $setupAttemptLog = @($emulatorSetupFailure.attemptLogs | Where-Object attempt -EQ 2)
    if ($setupAttemptLog.Count -ne 1) {
        throw 'Android emulator setup failure evidence lacks the exact second-attempt log reference.'
    }
    $setupAttemptLogPath = Join-Path $emulatorSetupFailureRoot ([string]$setupAttemptLog[0].fileName)
    if (-not (Test-Path -LiteralPath $setupAttemptLogPath -PathType Leaf) -or
        (Get-Item -LiteralPath $setupAttemptLogPath).Length -ne [long]$setupAttemptLog[0].bytes -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $setupAttemptLogPath).Hash.ToLowerInvariant() -cne [string]$setupAttemptLog[0].sha256 -or
        (Get-Content -Raw -LiteralPath $setupAttemptLogPath) -cnotmatch 'fixture avdmanager setup failure on attempt 2') {
        throw 'Android emulator setup failure log does not preserve the exact failing setup diagnostics.'
    }

    $prepareCommandFor = {
        param(
            [string]$CandidateIdentityPath,
            [string]$Label
        )

        $candidateOutput = Join-Path $tempRoot "identity-$Label-output"
        $candidateCache = Join-Path $tempRoot "identity-$Label-cache"
        New-Item -ItemType Directory -Path $candidateOutput, $candidateCache | Out-Null
        return 'cd {0} && "$BASH" scripts/build-android-distribution.sh --mode prepare-cache --identity {1} --output-dir {2} --cache-dir {3} --runner-os Linux --runner-arch X64' -f @(
            (Quote-Bash $rootBash),
            (Quote-Bash (Convert-ToBashPath $CandidateIdentityPath)),
            (Quote-Bash (Convert-ToBashPath $candidateOutput)),
            (Quote-Bash (Convert-ToBashPath $candidateCache))
        )
    }

    $blockedProductionIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $blockedProductionIdentity.androidVersionCodePolicy = 'production-monotonic'
    $blockedProductionIdentity.androidVersionCode = 353
    $blockedProductionIdentity.tagBinding = 'required'
    $blockedProductionIdentity.signatureProfile = 'production'
    $blockedProductionPath = Join-Path $tempRoot 'identity-production-blocked.json'
    Write-Utf8Text $blockedProductionPath (($blockedProductionIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Assert-BashFails (
        & $prepareCommandFor $blockedProductionPath 'production-blocked'
    ) 'Android identity policy accepted a non-monotonic production versionCode.'

    $validProductionIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $validProductionIdentity.androidVersionCodePolicy = 'production-monotonic'
    $validProductionIdentity.androidVersionCode = 354
    $validProductionIdentity.tagBinding = 'required'
    $validProductionIdentity.signatureProfile = 'production'
    $validProductionPath = Join-Path $tempRoot 'identity-production-valid.json'
    Write-Utf8Text $validProductionPath (($validProductionIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Invoke-BashChecked (
        & $prepareCommandFor $validProductionPath 'production-valid'
    ) 'Android identity policy rejected a monotonic production versionCode.' | Out-Null

    $overflowIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $overflowIdentity.androidVersionCode = 2100000001
    $overflowPath = Join-Path $tempRoot 'identity-overflow.json'
    Write-Utf8Text $overflowPath (($overflowIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Assert-BashFails (
        & $prepareCommandFor $overflowPath 'overflow'
    ) 'Android identity policy accepted an overflowing versionCode.'

    $wrongTagBindingIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $wrongTagBindingIdentity.tagBinding = 'required'
    $wrongTagBindingPath = Join-Path $tempRoot 'identity-wrong-tag-binding.json'
    Write-Utf8Text $wrongTagBindingPath (($wrongTagBindingIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Assert-BashFails (
        & $prepareCommandFor $wrongTagBindingPath 'wrong-tag-binding'
    ) 'Android identity policy accepted release tag binding for a ci-test candidate.'

    $wrongRawTagIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $wrongRawTagIdentity.rawTag = 'v9.9.9'
    $wrongRawTagPath = Join-Path $tempRoot 'identity-wrong-raw-tag.json'
    Write-Utf8Text $wrongRawTagPath (($wrongRawTagIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Assert-BashFails (
        & $prepareCommandFor $wrongRawTagPath 'wrong-raw-tag'
    ) 'Android identity policy accepted a raw tag that does not normalize to normalizedVersion.'

    $rawTagFilenameIdentity = $identity | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $rawTagFilenameIdentity.filenamePlan.android.arm64Apk = 'Unlimotion-v1.28.0-android-arm64.apk'
    $rawTagFilenamePath = Join-Path $tempRoot 'identity-raw-tag-filename.json'
    Write-Utf8Text $rawTagFilenamePath (($rawTagFilenameIdentity | ConvertTo-Json -Depth 10 -Compress) + "`n")
    Assert-BashFails (
        & $prepareCommandFor $rawTagFilenamePath 'raw-tag-filename'
    ) 'Android identity policy accepted a raw v-prefixed tag in an APK filename.'
}
finally {
    $tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $tempRootFull = [System.IO.Path]::GetFullPath($tempRoot)
    $tempBasePrefix = $tempBase.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $tempRootFull.StartsWith($tempBasePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($tempRootFull) -notlike 'unlimotion-android-contract-*') {
        throw "Refusing to remove unexpected Android contract fixture path: $tempRootFull"
    }
    Remove-Item -LiteralPath $tempRootFull -Recurse -Force
}

foreach ($shellScript in $shellScripts) {
    $rawContent = [System.IO.File]::ReadAllText($shellScript.Path)
    Assert-NoCrLf $rawContent "$($shellScript.Name) must use LF line endings so Git Bash can execute it on Windows."
}

Write-Output 'Android build script regression checks passed.'
