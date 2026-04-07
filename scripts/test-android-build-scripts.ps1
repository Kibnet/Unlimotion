$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSScriptRoot
$buildLibgit2Script = Get-Content -Raw (Join-Path $PSScriptRoot 'build-libgit2-android.sh')
$buildOpenSslScript = Get-Content -Raw (Join-Path $PSScriptRoot 'build-openssl-android.sh')
$packScript = Get-Content -Raw (Join-Path $PSScriptRoot 'pack-libgit2sharp-nativebinaries-android.sh')
$nugetConfig = Get-Content -Raw (Join-Path $rootDir 'src\nuget.config')
$gitattributes = Get-Content -Raw (Join-Path $rootDir '.gitattributes')
$workflow = Get-Content -Raw (Join-Path $rootDir '.github\workflows\android-packaging.yml')
$submoduleGitFile = Join-Path $rootDir '.native\libgit2-src\.git'
$shellScripts = @(
    @{
        Name = 'build-libgit2-android.sh'
        Path = Join-Path $PSScriptRoot 'build-libgit2-android.sh'
        Content = $buildLibgit2Script
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

if (-not (Test-Path $submoduleGitFile -PathType Leaf)) {
    throw "Expected git submodule marker file at $submoduleGitFile"
}

Assert-Match $buildLibgit2Script 'git -C "\$SRC_DIR" rev-parse --is-inside-work-tree' 'build-libgit2-android.sh must validate submodule with git rev-parse.'
Assert-NotMatch $buildLibgit2Script '\[ ! -d "\$SRC_DIR/\.git" \]' 'build-libgit2-android.sh must not require .git to be a directory.'

Assert-Match $buildLibgit2Script '#!/usr/bin/env bash' 'build-libgit2-android.sh must use a portable bash shebang.'
Assert-Match $buildLibgit2Script 'LIBGIT2_HTTPS_BACKEND="\$\{LIBGIT2_HTTPS_BACKEND:-OpenSSL\}"' 'build-libgit2-android.sh must default libgit2 HTTPS backend to OpenSSL for Android builds.'
Assert-Match $buildLibgit2Script 'OPENSSL_ROOT_DIR="\$\{OPENSSL_ROOT_DIR:-\$ROOT_DIR/artifacts/android-native/openssl-\$OPENSSL_VERSION-android-arm64/prefix\}"' 'build-libgit2-android.sh must default OpenSSL root to repo-local Android artifacts.'
Assert-Match $buildLibgit2Script '-DBUILD_TESTS=OFF' 'build-libgit2-android.sh must disable libgit2 tests for Android packaging.'
Assert-Match $buildLibgit2Script '-DBUILD_CLI=OFF' 'build-libgit2-android.sh must disable libgit2 CLI for Android packaging.'
Assert-Match $buildLibgit2Script '--target libgit2package' 'build-libgit2-android.sh must build the shared libgit2 package target.'
Assert-Match $buildOpenSslScript 'MINGW\*\|MSYS\*\|CYGWIN\*' 'build-openssl-android.sh must support Windows Git Bash/MSYS host detection.'
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
Assert-Match $buildOpenSslScript 'build_generated libcrypto\.a libssl\.a libcrypto\.ld libssl\.ld crypto/libssl-shlib-packet\.o' 'build-openssl-android.sh must build OpenSSL version scripts and shared packet object before Windows relinking.'
Assert-Match $buildOpenSslScript 'crypto/libssl-shlib-packet\.o' 'build-openssl-android.sh must include the OpenSSL shared packet object in the Windows libssl.so.3 relink.'
Assert-Match $buildOpenSslScript 'for generated_file in libcrypto\.ld libssl\.ld' 'build-openssl-android.sh must verify OpenSSL version scripts exist before Windows relinking.'
Assert-Match $buildOpenSslScript '--whole-archive libcrypto\.a' 'build-openssl-android.sh must relink libcrypto.so.3 from libcrypto.a on Windows hosts.'
Assert-Match $buildOpenSslScript '--whole-archive libssl\.a' 'build-openssl-android.sh must relink libssl.so.3 from libssl.a on Windows hosts.'
Assert-NotMatch $buildOpenSslScript '-Wl,--version-script=libssl\.ld\s+-Wl,--whole-archive libssl\.a\s+-Wl,--no-whole-archive\s+\./libcrypto\.so\.3' 'build-openssl-android.sh must not relink libssl.so.3 without crypto/libssl-shlib-packet.o.'
Assert-Match $buildOpenSslScript 'export ANDROID_NDK_ROOT' 'build-openssl-android.sh must export ANDROID_NDK_ROOT for OpenSSL Configure.'
Assert-Match $buildOpenSslScript 'perl \./Configure' 'build-openssl-android.sh must invoke OpenSSL Configure via the selected perl runtime.'
Assert-NotMatch $buildOpenSslScript 'export CC=' 'build-openssl-android.sh must rely on NDK clang detection from PATH instead of hardwiring CC.'
Assert-NotMatch $buildOpenSslScript 'no-docs' 'build-openssl-android.sh must not pass unsupported no-docs to OpenSSL Configure.'
Assert-Match $buildOpenSslScript 'make -j"\$\(cpu_count\)" build_generated libcrypto\.so libssl\.so' 'build-openssl-android.sh must build only the Android OpenSSL shared libraries on non-Windows hosts.'
Assert-Match $buildOpenSslScript 'install -m 0644 libcrypto\.so "\$INSTALL_DIR/lib/libcrypto\.so\.3"' 'build-openssl-android.sh must rename the non-Windows libcrypto.so output to libcrypto.so.3 when staging Android artifacts.'
Assert-Match $buildOpenSslScript 'install -m 0644 libssl\.so "\$INSTALL_DIR/lib/libssl\.so\.3"' 'build-openssl-android.sh must rename the non-Windows libssl.so output to libssl.so.3 when staging Android artifacts.'
Assert-NotMatch $buildOpenSslScript '(?s)else\s+.*build_sw' 'build-openssl-android.sh must not invoke OpenSSL build_sw on non-Windows hosts.'
Assert-NotMatch $buildOpenSslScript '(?s)else\s+.*make install_sw' 'build-openssl-android.sh must not invoke install_sw on non-Windows hosts.'

Assert-Match $packScript '\$ROOT_DIR/artifacts/nuget-local' 'pack-libgit2sharp-nativebinaries-android.sh must default to repo-local NuGet feed.'
Assert-NotMatch $packScript '/storage/emulated/0/nuget-local' 'pack-libgit2sharp-nativebinaries-android.sh must not hardcode Termux feed path.'
Assert-Match $packScript '2\.0\.324-android\.5' 'pack-libgit2sharp-nativebinaries-android.sh must default to the fixed Android native package version.'
Assert-Match $packScript 'command -v zip' 'pack-libgit2sharp-nativebinaries-android.sh must probe for zip before packing.'
Assert-Match $packScript 'command -v python3' 'pack-libgit2sharp-nativebinaries-android.sh must probe for a native Python archiver fallback.'
Assert-Match $packScript 'zipfile' 'pack-libgit2sharp-nativebinaries-android.sh must support Python-based package creation when zip is unavailable.'
Assert-Match $packScript 'powershell\.exe' 'pack-libgit2sharp-nativebinaries-android.sh must fall back to PowerShell packing on Windows hosts.'
Assert-Match $packScript 'Compress-Archive' 'pack-libgit2sharp-nativebinaries-android.sh must support PowerShell archive creation when zip is unavailable.'

Assert-Match $nugetConfig '\.\./artifacts/nuget-local' 'src/nuget.config must reference repo-local NuGet feed.'
Assert-Match $gitattributes '(?m)^\*\.sh\s+text\s+eol=lf\s*$' '.gitattributes must pin shell scripts to LF line endings.'
Assert-Match $workflow 'ANDROID_PLATFORM:\s+android-36' 'android-packaging workflow must install Android platform 36 for the current .NET Android workload.'
Assert-Match $workflow 'artifacts/android artifacts/android-native artifacts/nuget-local' 'android-packaging workflow must create repo-local feed directory.'
Assert-Match $workflow 'bash ./scripts/build-openssl-android\.sh[\s\S]*bash ./scripts/build-libgit2-android\.sh' 'android-packaging workflow must build Android OpenSSL before libgit2.'
Assert-NotMatch $workflow '/storage/emulated/0/nuget-local' 'android-packaging workflow must not prepare Termux-only feed path.'

foreach ($shellScript in $shellScripts) {
    $rawContent = [System.IO.File]::ReadAllText($shellScript.Path)
    Assert-NoCrLf $rawContent "$($shellScript.Name) must use LF line endings so Git Bash can execute it on Windows."
}

Write-Output 'Android build script regression checks passed.'
