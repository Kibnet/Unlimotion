param(
    [Parameter(Mandatory = $true)]
    [string]$RawTag,

    [Parameter(Mandatory = $true)]
    [string]$SourceSha,

    [Parameter(Mandatory = $true)]
    [string]$WorkflowSha,

    [Parameter(Mandatory = $true)]
    [ValidateSet('notApplicable', 'required')]
    [string]$TagBinding,

    [Parameter(Mandatory = $true)]
    [long]$AndroidVersionCode,

    [Parameter(Mandatory = $true)]
    [ValidateSet('ci-test', 'production-monotonic')]
    [string]$AndroidVersionCodePolicy,

    [string]$Manifest = (Join-Path $PSScriptRoot '..\distribution\release-assets.json'),

    [string]$SupportMatrix,

    [string]$OutputPath,

    [string]$GitHubOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if ($resolved.Provider.Name -ne 'FileSystem' -or -not [System.IO.File]::Exists($resolved.Path)) {
        throw "$DisplayName must be an existing file: $Path"
    }

    return $resolved.Path
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 -ErrorAction Stop
    }
    catch {
        throw "$DisplayName is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-LowerFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Compare-StableVersion {
    param(
        [Parameter(Mandatory = $true)][string[]]$Left,
        [Parameter(Mandatory = $true)][string[]]$Right
    )

    for ($index = 0; $index -lt 3; $index++) {
        $leftValue = [System.Numerics.BigInteger]::Parse($Left[$index], [System.Globalization.CultureInfo]::InvariantCulture)
        $rightValue = [System.Numerics.BigInteger]::Parse($Right[$index], [System.Globalization.CultureInfo]::InvariantCulture)
        if ($leftValue -lt $rightValue) {
            return -1
        }
        if ($leftValue -gt $rightValue) {
            return 1
        }
    }

    return 0
}

function Resolve-StableVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$MinimumVersion
    )

    $stablePattern = '^(?:v)?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $Tag,
        $stablePattern,
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "RawTag '$Tag' is invalid. Expected stable MAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH without leading zeroes."
    }

    $minimumMatch = [System.Text.RegularExpressions.Regex]::Match(
        $MinimumVersion,
        '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $minimumMatch.Success) {
        throw "Manifest minimumVersion '$MinimumVersion' is invalid."
    }

    $components = @($match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value)
    $minimumComponents = @(
        $minimumMatch.Groups[1].Value,
        $minimumMatch.Groups[2].Value,
        $minimumMatch.Groups[3].Value)
    if ((Compare-StableVersion -Left $components -Right $minimumComponents) -lt 0) {
        throw "RawTag '$Tag' is lower than manifest minimumVersion '$MinimumVersion'."
    }

    return $components -join '.'
}

function Assert-LowerSha {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -cnotmatch '^[0-9a-f]{40}$') {
        throw "$Name must be exactly 40 lowercase hexadecimal characters."
    }
}

function Get-RequiredFilename {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Plan,
        [Parameter(Mandatory = $true)][string]$AssetId
    )

    if (-not $Plan.Contains($AssetId)) {
        throw "Manifest is missing required asset id '$AssetId'."
    }

    return [string]$Plan[$AssetId]
}

Assert-LowerSha -Value $SourceSha -Name 'SourceSha'
Assert-LowerSha -Value $WorkflowSha -Name 'WorkflowSha'

$manifestPath = Resolve-ExistingFile -Path $Manifest -DisplayName 'Manifest'
$manifestDocument = Read-JsonFile -Path $manifestPath -DisplayName 'Manifest'
if ($manifestDocument.schemaVersion -ne 1 -or $manifestDocument.product -ne 'Unlimotion') {
    throw 'Manifest must be Unlimotion schemaVersion 1.'
}
if (-not $manifestDocument.tagPolicy.stableOnly) {
    throw 'Manifest tagPolicy.stableOnly must remain true.'
}

$normalizedVersion = Resolve-StableVersion `
    -Tag $RawTag `
    -MinimumVersion ([string]$manifestDocument.tagPolicy.minimumVersion)

if ($AndroidVersionCode -lt 1 -or $AndroidVersionCode -gt 2100000000) {
    throw 'AndroidVersionCode must be in the inclusive range 1..2100000000.'
}

$byAssetId = [ordered]@{}
$caseInsensitiveNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($asset in @($manifestDocument.assets)) {
    $assetId = [string]$asset.id
    if ($byAssetId.Contains($assetId)) {
        throw "Manifest contains duplicate asset id '$assetId'."
    }

    $template = [string]$asset.filenameTemplate
    if ($template -match '\{rawTag\}') {
        throw "Asset '$assetId' uses forbidden rawTag filename placeholder."
    }

    $filename = $template.Replace('{normalizedVersion}', $normalizedVersion)
    if ($filename -match '[{}]' -or [System.IO.Path]::GetFileName($filename) -ne $filename) {
        throw "Asset '$assetId' produced invalid filename '$filename'."
    }
    if (-not $caseInsensitiveNames.Add($filename)) {
        throw "Manifest generates a case-insensitive filename collision for '$filename'."
    }

    $byAssetId[$assetId] = $filename
}

$lastPublishedAndroidVersionCode = $null
$supportMatrixPath = $null
$supportMatrixSha256 = $null
if (-not [string]::IsNullOrWhiteSpace($SupportMatrix)) {
    $supportMatrixPath = Resolve-ExistingFile -Path $SupportMatrix -DisplayName 'SupportMatrix'
    $supportMatrixDocument = Read-JsonFile -Path $supportMatrixPath -DisplayName 'SupportMatrix'
    if ($supportMatrixDocument.schemaVersion -ne 1 -or $supportMatrixDocument.product -ne 'Unlimotion') {
        throw 'SupportMatrix must be Unlimotion schemaVersion 1.'
    }
    $lastPublishedAndroidVersionCode = [long]$supportMatrixDocument.lastPublishedAndroidVersionCode
    if ($lastPublishedAndroidVersionCode -lt 1 -or $lastPublishedAndroidVersionCode -gt 2100000000) {
        throw 'SupportMatrix lastPublishedAndroidVersionCode is outside 1..2100000000.'
    }
    $supportMatrixSha256 = Get-LowerFileSha256 -Path $supportMatrixPath
}

if ($AndroidVersionCodePolicy -eq 'ci-test') {
    if ($TagBinding -ne 'notApplicable') {
        throw "ci-test identity requires TagBinding 'notApplicable'."
    }
    $androidVersionCodeSource = 'github.run_number'
    $signatureProfile = 'test'
    $productionVersionCodeMonotonic = $false
}
else {
    if ($TagBinding -ne 'required') {
        throw "production-monotonic identity requires TagBinding 'required'."
    }
    if ($null -eq $lastPublishedAndroidVersionCode) {
        throw 'SupportMatrix is required for production-monotonic AndroidVersionCodePolicy.'
    }
    if ($AndroidVersionCode -le $lastPublishedAndroidVersionCode) {
        throw "Production AndroidVersionCode $AndroidVersionCode must be greater than last published value $lastPublishedAndroidVersionCode."
    }
    $androidVersionCodeSource = 'stage4-production-allocator'
    $signatureProfile = 'production'
    $productionVersionCodeMonotonic = $true
}

$filenamePlan = [ordered]@{
    byAssetId = $byAssetId
    windows = [ordered]@{
        updaterFeedLegacy = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-feed-legacy'
        updaterFeedJson = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-feed-json'
        updaterPackageX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-updater-package-x64'
        setupX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-setup-x64'
        portableX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-portable-x64'
        legacyPortableX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'windows-portable-x64-legacy'
    }
    linux = [ordered]@{
        updaterFeedJson = Get-RequiredFilename -Plan $byAssetId -AssetId 'linux-feed-json'
        updaterPackageX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'linux-updater-package-x64'
        debX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'linux-deb-x64'
        appImageX64 = Get-RequiredFilename -Plan $byAssetId -AssetId 'linux-appimage-x64'
    }
    macos = [ordered]@{
        x64 = [ordered]@{
            updaterFeedJson = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-x64-feed-json'
            updaterPackage = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-x64-updater-package'
            setup = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-x64-setup'
            portable = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-x64-portable'
            legacyPkg = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-x64-pkg-legacy'
        }
        arm64 = [ordered]@{
            updaterFeedJson = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-arm64-feed-json'
            updaterPackage = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-arm64-updater-package'
            setup = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-arm64-setup'
            portable = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-arm64-portable'
            legacyPkg = Get-RequiredFilename -Plan $byAssetId -AssetId 'macos-arm64-pkg-legacy'
        }
    }
    android = [ordered]@{
        arm64Apk = Get-RequiredFilename -Plan $byAssetId -AssetId 'android-arm64-apk'
        x64Apk = Get-RequiredFilename -Plan $byAssetId -AssetId 'android-x64-apk'
    }
}

$identity = [ordered]@{
    schemaVersion = 1
    rawTag = $RawTag
    normalizedVersion = $normalizedVersion
    sourceSha = $SourceSha
    workflowSha = $WorkflowSha
    tagBinding = $TagBinding
    manifestSha256 = Get-LowerFileSha256 -Path $manifestPath
    supportMatrixSha256 = $supportMatrixSha256
    androidVersionCode = $AndroidVersionCode
    androidVersionCodePolicy = $AndroidVersionCodePolicy
    androidVersionCodeSource = $androidVersionCodeSource
    lastPublishedAndroidVersionCode = $lastPublishedAndroidVersionCode
    signatureProfile = $signatureProfile
    productionVersionCodeMonotonic = $productionVersionCodeMonotonic
    filenamePlan = $filenamePlan
}

$json = $identity | ConvertTo-Json -Depth 20 -Compress
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
    if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
        throw "OutputPath must have a parent directory: $OutputPath"
    }
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    [System.IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, $utf8WithoutBom)
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    $fullGitHubOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($GitHubOutputPath)
    $githubOutputDirectory = [System.IO.Path]::GetDirectoryName($fullGitHubOutputPath)
    if ([string]::IsNullOrWhiteSpace($githubOutputDirectory)) {
        throw "GitHubOutputPath must have a parent directory: $GitHubOutputPath"
    }
    [System.IO.Directory]::CreateDirectory($githubOutputDirectory) | Out-Null
    $githubOutputs = @(
        "raw_tag=$RawTag",
        "normalized_version=$normalizedVersion",
        "source_sha=$SourceSha",
        "workflow_sha=$WorkflowSha",
        "tag_binding=$TagBinding",
        "manifest_sha256=$($identity.manifestSha256)",
        "support_matrix_sha256=$supportMatrixSha256",
        "android_version_code=$AndroidVersionCode",
        "android_version_code_policy=$AndroidVersionCodePolicy",
        "android_version_code_source=$androidVersionCodeSource",
        "last_published_android_version_code=$lastPublishedAndroidVersionCode",
        "signature_profile=$signatureProfile",
        "production_version_code_monotonic=$($productionVersionCodeMonotonic.ToString().ToLowerInvariant())",
        "filename_plan_json=$($filenamePlan | ConvertTo-Json -Depth 20 -Compress)"
    ) -join [Environment]::NewLine
    [System.IO.File]::AppendAllText(
        $fullGitHubOutputPath,
        $githubOutputs + [Environment]::NewLine,
        $utf8WithoutBom)
}

Write-Output $json
