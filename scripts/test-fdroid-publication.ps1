param(
    [switch]$SkipRecipe
)

$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSScriptRoot
$expectedSourceCommit = '1289a92f3df58ff6dab0b1cd82e547b4bd44c128'
$expectedNodifyCommit = 'a8c9a96c80bc5e666aa34c9d3ce5947376e37722'
$expectedLibgit2Commit = '155578578b78efc6bae7383a708d470eb206e36a'
$expectedOpenSslSha256 = '617e29af8e421f46649484a4937e48c685e47f46488167c982f88bc4ec1d522f'
$expectedLibssh2Sha256 = 'd9ec76cbe34db98eec3539fe2c899d26b0c837cb3eb466a56b0f109cabf658f7'
$expectedDotnetSha512 = 'f78dbac30c9af2230d67ff5c224de3a5dbf63f8a78d1c206594dedb80e6909d2cc8a9d865d5105c72c2fd2aa266fc0c6c77dedac60408cbccf272b116bd11b07'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

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

function Get-PngDimensions {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    Assert-True ($bytes.Length -ge 24) "PNG is too short: $Path"
    for ($index = 0; $index -lt $signature.Length; $index++) {
        Assert-True ($bytes[$index] -eq $signature[$index]) "Invalid PNG signature: $Path"
    }

    $width = [uint32](
        ($bytes[16] * 16777216) +
        ($bytes[17] * 65536) +
        ($bytes[18] * 256) +
        $bytes[19])
    $height = [uint32](
        ($bytes[20] * 16777216) +
        ($bytes[21] * 65536) +
        ($bytes[22] * 256) +
        $bytes[23])
    return [pscustomobject]@{ Width = $width; Height = $height }
}

$requiredPaths = @(
    'global.json',
    '.gitmodules',
    '.native/nodify-avalonia-src',
    'scripts/pack-nodify-fdroid.sh',
    'scripts/pack-libgit2sharp-nativebinaries-fdroid.sh',
    'scripts/build-fdroid-android.sh',
    'scripts/patches/libgit2-android.patch',
    'fdroid/README.md'
)

if (-not $SkipRecipe) {
    $requiredPaths += 'fdroid/com.Kibnet.Unlimotion.yml'
}

foreach ($relativePath in $requiredPaths) {
    $fullPath = Join-Path $rootDir $relativePath
    Assert-True (Test-Path -LiteralPath $fullPath) "Missing F-Droid publication artifact: $relativePath"
}

$globalJson = Get-Content -Raw (Join-Path $rootDir 'global.json') | ConvertFrom-Json
Assert-True ($globalJson.sdk.version -eq '10.0.100') 'global.json must pin .NET SDK 10.0.100.'
Assert-True ($globalJson.sdk.rollForward -eq 'latestPatch') 'global.json must restrict SDK roll-forward to latestPatch.'
Assert-True ($globalJson.sdk.allowPrerelease -eq $false) 'global.json must reject prerelease SDKs.'

$gitmodules = Get-Content -Raw (Join-Path $rootDir '.gitmodules')
Assert-Match $gitmodules '\[submodule "\.native/nodify-avalonia-src"\]' '.gitmodules must define the Nodify source submodule.'
Assert-Match $gitmodules 'url = https://github\.com/Kibnet/nodify-avalonia\.git' 'Nodify submodule must use the public Kibnet fork.'
Assert-Match $gitmodules 'url = https://github\.com/libgit2/libgit2\.git' 'libgit2 submodule must use the official upstream repository.'

$libgit2Source = Join-Path $rootDir '.native/libgit2-src'
$libgit2Commit = (& git -C $libgit2Source rev-parse HEAD).Trim()
Assert-True ($LASTEXITCODE -eq 0) 'Unable to resolve the libgit2 source commit.'
Assert-True ($libgit2Commit -eq $expectedLibgit2Commit) "libgit2 source must be pinned to security-fixed v1.6.5 commit $expectedLibgit2Commit, got $libgit2Commit."

$nodifySource = Join-Path $rootDir '.native/nodify-avalonia-src'
$nodifyCommit = (& git -C $nodifySource rev-parse HEAD).Trim()
Assert-True ($LASTEXITCODE -eq 0) 'Unable to resolve the Nodify source commit.'
Assert-True ($nodifyCommit -eq $expectedNodifyCommit) "Nodify source must be pinned to $expectedNodifyCommit, got $nodifyCommit."

$nodifyPacker = Get-Content -Raw (Join-Path $rootDir 'scripts/pack-nodify-fdroid.sh')
Assert-Match $nodifyPacker ([regex]::Escape($expectedNodifyCommit)) 'Nodify packer must verify the approved source commit.'
Assert-Match $nodifyPacker 'dotnet pack' 'Nodify F-Droid package must be built from source with dotnet pack.'
Assert-Match $nodifyPacker '6\.6\.0-unlimotion\.a12\.1\.fdroid\.1' 'Nodify F-Droid package must use the source-build-only package version.'
Assert-NotMatch $nodifyPacker 'curl|wget|\.nupkg.*https?://' 'Nodify packer must not download a prebuilt package.'

$nativePacker = Get-Content -Raw (Join-Path $rootDir 'scripts/pack-libgit2sharp-nativebinaries-fdroid.sh')
Assert-Match $nativePacker 'IncludeBuildOutput>false</IncludeBuildOutput>' 'Native F-Droid package must be generated without managed build output.'
Assert-Match $nativePacker 'runtimes/android-arm64/native' 'Native F-Droid package must contain only the arm64 Android runtime path.'
Assert-Match $nativePacker '2\.0\.324-android\.7\.fdroid\.2' 'Native F-Droid package must use the security-updated source-build package version.'
Assert-Match $nativePacker ([regex]::Escape($expectedLibgit2Commit)) 'Native F-Droid package must verify the security-fixed libgit2 source commit.'
Assert-NotMatch $nativePacker 'api\.nuget\.org|UPSTREAM_PACKAGE|curl|wget' 'Native F-Droid packer must not download a prebuilt package.'

$libgit2Build = Get-Content -Raw (Join-Path $rootDir 'scripts/build-libgit2-android.sh')
$libgit2Patch = Get-Content -Raw (Join-Path $rootDir 'scripts/patches/libgit2-android.patch')
Assert-Match $libgit2Build 'git -C "\$SRC_DIR" apply --unidiff-zero --check' 'libgit2 Android build must validate the tracked compatibility patch explicitly.'
Assert-Match $libgit2Build 'apply --unidiff-zero --reverse' 'libgit2 Android build must remove its compatibility patch on exit.'
Assert-Match $libgit2Patch 'C_STANDARD 99' 'libgit2 Android patch must retain the C99 compatibility adjustment.'
Assert-Match $libgit2Patch 'defined\(__ANDROID__\)' 'libgit2 Android patch must retain the Android random-source adjustment.'

$buildScript = Get-Content -Raw (Join-Path $rootDir 'scripts/build-fdroid-android.sh')
Assert-Match $buildScript 'AVALONIA_TELEMETRY_OPTOUT=1' 'F-Droid build must disable Avalonia build telemetry.'
Assert-Match $buildScript 'VERSION_NAME' 'F-Droid build must require an explicit versionName.'
Assert-Match $buildScript 'VERSION_CODE' 'F-Droid build must require an explicit versionCode.'
Assert-Match $buildScript 'FdroidBuild=true' 'F-Droid build must enable the updater-free build variant.'
Assert-Match $buildScript 'RuntimeIdentifier=android-arm64' 'F-Droid build must target Android arm64.'
Assert-Match $buildScript 'android wasm-tools' 'F-Droid build must validate both Android and transitive WebAssembly workloads.'
Assert-Match $buildScript 'OPENSSL_VERSION="3\.0\.21"' 'F-Droid build must use security-fixed OpenSSL 3.0.21.'
Assert-Match $buildScript ([regex]::Escape($expectedOpenSslSha256)) 'F-Droid build must verify the OpenSSL 3.0.21 archive.'
Assert-Match $buildScript ([regex]::Escape($expectedLibssh2Sha256)) 'F-Droid build must verify the libssh2 1.11.1 archive.'
Assert-NotMatch $buildScript 'AndroidSigning|AndroidKeyStore|SigningKey' 'F-Droid build must not use an upstream signing key.'

$androidProject = Get-Content -Raw (Join-Path $rootDir 'src/Unlimotion.Android/Unlimotion.Android.csproj')
Assert-Match $androidProject '<ItemGroup Condition="''\$\(FdroidBuild\)'' != ''true''">[\s\S]*runtimes\\android-x64\\native' 'x64 native libraries must remain standard-build-only.'
Assert-Match $androidProject 'VersionOverride="2\.0\.324-android\.7\.fdroid\.2"[\s\S]*Condition="''\$\(FdroidBuild\)'' == ''true''"' 'F-Droid Android build must resolve the security-updated source-built native package version.'

$appProject = Get-Content -Raw (Join-Path $rootDir 'src/Unlimotion/Unlimotion.csproj')
Assert-Match $appProject 'VersionOverride="6\.6\.0-unlimotion\.a12\.1\.fdroid\.1"[\s\S]*Condition="''\$\(FdroidBuild\)'' == ''true''"' 'F-Droid app build must resolve the source-built Nodify package version.'

foreach ($locale in @('en-US', 'ru-RU')) {
    $localeRoot = Join-Path $rootDir "fastlane/metadata/android/$locale"
    foreach ($textFile in @('title.txt', 'short_description.txt', 'full_description.txt', 'changelogs/1028000.txt')) {
        $textPath = Join-Path $localeRoot $textFile
        Assert-True (Test-Path -LiteralPath $textPath -PathType Leaf) "Missing $locale metadata file: $textFile"
        Assert-True ((Get-Content -Raw $textPath).Trim().Length -gt 0) "Empty $locale metadata file: $textFile"
    }

    $shortDescription = (Get-Content -Raw (Join-Path $localeRoot 'short_description.txt')).Trim()
    Assert-True ($shortDescription.Length -le 80) "$locale short_description.txt must not exceed 80 characters."

    $iconPath = Join-Path $localeRoot 'images/icon.png'
    Assert-True (Test-Path -LiteralPath $iconPath -PathType Leaf) "Missing $locale F-Droid icon."
    $iconDimensions = Get-PngDimensions $iconPath
    Assert-True ($iconDimensions.Width -gt 0 -and $iconDimensions.Height -gt 0) "Invalid $locale icon dimensions."

    $screenshots = @(Get-ChildItem -LiteralPath (Join-Path $localeRoot 'images/phoneScreenshots') -Filter '*.png' -File)
    Assert-True ($screenshots.Count -ge 2) "$locale metadata must include at least two phone screenshots."
    foreach ($screenshot in $screenshots) {
        $dimensions = Get-PngDimensions $screenshot.FullName
        Assert-True ($dimensions.Width -gt 0 -and $dimensions.Height -gt 0) "Invalid screenshot dimensions: $($screenshot.FullName)"
    }
}

$runbook = Get-Content -Raw (Join-Path $rootDir 'fdroid/README.md')
Assert-Match $runbook '24–48' 'F-Droid runbook must set a realistic post-merge indexing expectation.'
Assert-Match $runbook 'подпис|signing|signature' 'F-Droid runbook must explain the signing-key migration caveat.'
Assert-Match $runbook 'RFP' 'F-Droid runbook must describe the Request For Packaging fallback.'
Assert-Match $runbook 'отдельн.*подтвержден|separate.*approval' 'F-Droid runbook must preserve the external publication approval gate.'

if (-not $SkipRecipe) {
    $recipe = Get-Content -Raw (Join-Path $rootDir 'fdroid/com.Kibnet.Unlimotion.yml')
    Assert-Match $recipe 'RepoType:\s+git' 'F-Droid recipe must use the public Git repository.'
    Assert-Match $recipe 'Repo:\s+https://github\.com/Kibnet/Unlimotion\.git' 'F-Droid recipe must point to the public Unlimotion repository.'
    Assert-Match $recipe 'versionName:\s+1\.28\.0' 'F-Droid recipe must define versionName 1.28.0.'
    Assert-Match $recipe 'versionCode:\s+1028000' 'F-Droid recipe must define versionCode 1028000.'
    Assert-Match $recipe ("commit:\s+" + [regex]::Escape($expectedSourceCommit) + "\s") 'F-Droid recipe must pin the reviewed source commit exactly.'
    Assert-Match $recipe 'submodules:\s+true' 'F-Droid recipe must initialize pinned source submodules.'
    Assert-Match $recipe 'scandelete:[\s\S]*libgit2-3f4182d\.so' 'F-Droid recipe must scanner-delete the tracked native library.'
    Assert-Match $recipe 'rm:[\s\S]*NodifyAvalonia\.6\.6\.0-unlimotion\.a12\.1\.nupkg[\s\S]*\.native/libgit2-src/tests[\s\S]*\.native/libgit2-src/fuzzers[\s\S]*\.native/libgit2-src/package\.json' 'F-Droid recipe must remove the unused tracked package, libgit2 fixtures, and its unlocked Node manifest before scanning.'
    Assert-NotMatch $recipe 'scanignore:' 'F-Droid recipe must not hide scanner findings.'
    Assert-Match $recipe ([regex]::Escape($expectedDotnetSha512)) 'F-Droid recipe must verify the exact .NET 10.0.100 SDK archive.'
    Assert-Match $recipe 'AutoUpdateMode:\s+None' 'Initial F-Droid recipe must keep automatic updates disabled.'
    Assert-Match $recipe 'UpdateCheckMode:\s+None' 'Initial F-Droid recipe must avoid ambiguous historical tags.'
    Assert-Match $runbook ([regex]::Escape($expectedSourceCommit)) 'F-Droid runbook must identify the exact source commit used by the recipe.'
}

Write-Output 'F-Droid publication contracts passed.'
