$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSScriptRoot
$commonScript = Get-Content -Raw (Join-Path $PSScriptRoot 'android-native-common.sh')
$buildLibgit2Script = Get-Content -Raw (Join-Path $PSScriptRoot 'build-libgit2-android.sh')
$buildLibssh2Script = Get-Content -Raw (Join-Path $PSScriptRoot 'build-libssh2-android.sh')
$buildOpenSslScript = Get-Content -Raw (Join-Path $PSScriptRoot 'build-openssl-android.sh')
$packScript = Get-Content -Raw (Join-Path $PSScriptRoot 'pack-libgit2sharp-nativebinaries-android.sh')
$nugetConfig = Get-Content -Raw (Join-Path $rootDir 'src\nuget.config')
$androidProject = Get-Content -Raw (Join-Path $rootDir 'src\Unlimotion.Android\Unlimotion.Android.csproj')
$gitattributes = Get-Content -Raw (Join-Path $rootDir '.gitattributes')
$workflow = Get-Content -Raw (Join-Path $rootDir '.github\workflows\android-packaging.yml')
$submoduleGitFile = Join-Path $rootDir '.native\libgit2-src\.git'
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
Assert-Match $packScript '2\.0\.324-android\.6' 'pack-libgit2sharp-nativebinaries-android.sh must default to the fixed Android native package version.'
Assert-Match $packScript 'ANDROID_ABIS="\$\{ANDROID_ABIS:-arm64-v8a x86_64\}"' 'pack-libgit2sharp-nativebinaries-android.sh must package arm64 and x86_64 Android runtimes by default.'
Assert-Match $packScript 'libssh2\.so\*' 'pack-libgit2sharp-nativebinaries-android.sh must include libssh2 runtime libraries.'
Assert-Match $packScript 'command -v zip' 'pack-libgit2sharp-nativebinaries-android.sh must probe for zip before packing.'
Assert-Match $packScript 'command -v python3' 'pack-libgit2sharp-nativebinaries-android.sh must probe for a native Python archiver fallback.'
Assert-Match $packScript 'zipfile' 'pack-libgit2sharp-nativebinaries-android.sh must support Python-based package creation when zip is unavailable.'
Assert-Match $packScript 'powershell\.exe' 'pack-libgit2sharp-nativebinaries-android.sh must fall back to PowerShell packing on Windows hosts.'
Assert-Match $packScript 'Compress-Archive' 'pack-libgit2sharp-nativebinaries-android.sh must support PowerShell archive creation when zip is unavailable.'

Assert-Match $nugetConfig '\.\./artifacts/nuget-local' 'src/nuget.config must reference repo-local NuGet feed.'
Assert-Match $androidProject '<RuntimeIdentifiers>android-arm64;android-x64</RuntimeIdentifiers>' 'Unlimotion.Android.csproj must build both arm64 and x64 Android runtimes.'
Assert-Match $androidProject '<AndroidEnableAssemblyCompression>true</AndroidEnableAssemblyCompression>' 'Unlimotion.Android.csproj must keep Android assembly compression enabled so libxamarin-app.so exports runtime symbols required by libmonodroid.so.'
Assert-NotMatch $androidProject '<AndroidEnableAssemblyCompression>false</AndroidEnableAssemblyCompression>' 'Unlimotion.Android.csproj must not disable Android assembly compression because published APKs fail before startup on device.'
Assert-Match $androidProject '<AndroidEnableMarshalMethods>false</AndroidEnableMarshalMethods>' 'Unlimotion.Android.csproj must keep static Java callable wrapper registration because marshal-method registration leaves MainActivity native callbacks unregistered on device.'
Assert-NotMatch $androidProject '<AndroidEnableMarshalMethods>true</AndroidEnableMarshalMethods>' 'Unlimotion.Android.csproj must not enable marshal-method registration for release APKs until device startup is verified.'
Assert-Match $androidProject 'runtimes\\android-arm64\\native\\libssh2\.so' 'Unlimotion.Android.csproj must explicitly package Android arm64 libssh2.so.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libcrypto\.so\.3' 'Unlimotion.Android.csproj must explicitly package Android x64 libcrypto.so.3.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libssl\.so\.3' 'Unlimotion.Android.csproj must explicitly package Android x64 libssl.so.3.'
Assert-Match $androidProject 'runtimes\\android-x64\\native\\libssh2\.so' 'Unlimotion.Android.csproj must explicitly package Android x64 libssh2.so.'
Assert-Match $gitattributes '(?m)^\*\.sh\s+text\s+eol=lf\s*$' '.gitattributes must pin shell scripts to LF line endings.'
Assert-Match $workflow 'ANDROID_PLATFORM:\s+android-36' 'android-packaging workflow must install Android platform 36 for the current .NET Android workload.'
Assert-Match $workflow 'dotnet workload install android --skip-manifest-update' 'android-packaging workflow must skip workload manifest updates to keep Android CI setup fast and reproducible.'
Assert-Match $workflow 'artifacts/android artifacts/android-native artifacts/nuget-local' 'android-packaging workflow must create repo-local feed directory.'
Assert-Match $workflow 'Resolve Android Native Cache Key' 'android-packaging workflow must resolve native dependency cache inputs before restoring cached artifacts.'
Assert-Match $workflow 'git -C \.native/libgit2-src rev-parse HEAD' 'android-packaging workflow must include the libgit2 submodule commit in the Android native cache key.'
Assert-Match $workflow 'Cache Android Native Dependencies[\s\S]*uses: actions/cache@v4[\s\S]*artifacts/android-native[\s\S]*artifacts/nuget-local' 'android-packaging workflow must cache rebuilt Android native artifacts and the local native NuGet package.'
Assert-Match $workflow 'key: android-native-\$\{\{ runner\.os \}\}[\s\S]*\$\{\{ steps\.android_native_cache_key\.outputs\.libgit2_sha \}\}' 'android-packaging workflow must key the Android native cache by native toolchain versions, scripts, and libgit2 commit.'
Assert-Match $workflow 'Cache NuGet Packages[\s\S]*uses: actions/cache@v4[\s\S]*~/.nuget/packages' 'android-packaging workflow must cache NuGet packages for repeated Android restores.'
Assert-Match $workflow 'for abi in arm64-v8a x86_64' 'android-packaging workflow must build native dependencies for arm64 and x86_64.'
Assert-Match $workflow 'expected_package="artifacts/nuget-local/LibGit2Sharp\.NativeBinaries\.\$\{LIBGIT2_NATIVE_PACKAGE_VERSION\}\.nupkg"' 'android-packaging workflow must reuse a cached local Android native NuGet package when present.'
Assert-Match $workflow 'Using cached Android native package' 'android-packaging workflow must skip rebuilding Android native dependencies on native package cache hits.'
Assert-Match $workflow 'bash ./scripts/build-openssl-android\.sh[\s\S]*bash ./scripts/build-libssh2-android\.sh[\s\S]*bash ./scripts/build-libgit2-android\.sh' 'android-packaging workflow must build Android OpenSSL and libssh2 before libgit2.'
Assert-Match $workflow 'Restore Android Project[\s\S]*for rid in android-arm64 android-x64[\s\S]*dotnet restore src/Unlimotion\.Android/Unlimotion\.Android\.csproj[\s\S]*-p:RuntimeIdentifier="\$rid"[\s\S]*-p:RuntimeIdentifiers="\$rid"' 'android-packaging workflow must restore Android RID assets before packaging without repeating restore inside package builds.'
Assert-Match $workflow 'Resolve Android App Version' 'android-packaging workflow must resolve Android app version before building APKs.'
Assert-Match $workflow 'display_version="\$\{GITHUB_REF_NAME#v\}"' 'android-packaging workflow must derive release ApplicationDisplayVersion from the release tag.'
Assert-Match $workflow 'version_code="\$\{GITHUB_RUN_NUMBER\}"' 'android-packaging workflow must use a monotonic GitHub run number for Android ApplicationVersion.'
Assert-Match $workflow '-p:ApplicationDisplayVersion="\$\{\{ steps\.android_version\.outputs\.display_version \}\}"' 'android-packaging workflow must stamp Android APK versionName from the resolved release version.'
Assert-Match $workflow '-p:ApplicationVersion="\$\{\{ steps\.android_version\.outputs\.version_code \}\}"' 'android-packaging workflow must stamp Android APK versionCode from the resolved version code.'
Assert-Match $workflow 'Prepare Android Release Signing' 'android-packaging workflow must prepare a stable release keystore for Android updates.'
Assert-Match $workflow 'ANDROID_SIGNING_KEYSTORE_BASE64' 'android-packaging workflow must require a base64-encoded release keystore secret.'
Assert-Match $workflow 'base64 --decode > "\$signing_keystore"' 'android-packaging workflow must decode the release signing keystore before building release APKs.'
Assert-Match $workflow '-p:AndroidKeyStore=true' 'android-packaging workflow must enable Android keystore signing for release APKs.'
Assert-Match $workflow '-p:AndroidSigningKeyStore="\$\{ANDROID_SIGNING_KEYSTORE\}"' 'android-packaging workflow must pass the stable release keystore to MSBuild.'
Assert-Match $workflow '-p:AndroidSigningStorePass="\$\{ANDROID_SIGNING_STORE_PASS\}"' 'android-packaging workflow must pass the release keystore password to MSBuild.'
Assert-Match $workflow '-p:AndroidSigningKeyAlias="\$\{ANDROID_SIGNING_KEY_ALIAS\}"' 'android-packaging workflow must pass the release key alias to MSBuild.'
Assert-Match $workflow '-p:AndroidSigningKeyPass="\$\{ANDROID_SIGNING_KEY_PASS\}"' 'android-packaging workflow must pass the release key password to MSBuild.'
Assert-Match $workflow 'for rid in android-arm64 android-x64' 'android-packaging workflow must build arm64 and x64 Android APKs.'
Assert-Match $workflow '--no-restore' 'android-packaging workflow must package each Android RID without repeating NuGet restore.'
Assert-NotMatch $workflow 'rm -rf "src/Unlimotion\.Android/bin/Release/net10\.0-android"' 'android-packaging workflow must not delete shared Android build outputs before each RID package build.'
Assert-Match $workflow '-p:RuntimeIdentifiers="\$rid"' 'android-packaging workflow must restrict each APK build to the current RID so arm64 builds do not package x64 intermediates.'
Assert-Match $workflow 'apk_search_root="src/Unlimotion\.Android/bin/Release/net10\.0-android/\$\{rid\}"' 'android-packaging workflow must find the signed APK under the current RID output directory.'
Assert-Match $workflow 'validate_runtime_symbols' 'android-packaging workflow must validate Android runtime native symbols before publishing APK assets.'
Assert-Match $workflow 'compressed_assembly_count' 'android-packaging workflow must catch APKs whose libxamarin-app.so is missing compressed assembly symbols required by libmonodroid.so.'
Assert-Match $workflow 'libxamarin-app\.so' 'android-packaging workflow must inspect libxamarin-app.so before publishing Android APKs.'
Assert-NotMatch $workflow '/storage/emulated/0/nuget-local' 'android-packaging workflow must not prepare Termux-only feed path.'

foreach ($shellScript in $shellScripts) {
    $rawContent = [System.IO.File]::ReadAllText($shellScript.Path)
    Assert-NoCrLf $rawContent "$($shellScript.Name) must use LF line endings so Git Bash can execute it on Windows."
}

Write-Output 'Android build script regression checks passed.'
