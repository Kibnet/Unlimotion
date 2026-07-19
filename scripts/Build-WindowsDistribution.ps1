[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Identity,

    [string]$OutputDirectory,
    [string]$VpkPath = 'vpk',
    [string]$GitHubOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Build-WindowsDistribution.ps1 must run on Windows.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/distribution-validation/windows-x64'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$ArgumentList,
        [Parameter(Mandatory)] [string]$Label
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Get-RequiredString {
    param([Parameter(Mandatory)] [object]$Object, [Parameter(Mandatory)] [string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($property.Value)) {
        throw "Release identity is missing '$Name'."
    }
    return [string]$property.Value
}

$identityPath = (Resolve-Path -LiteralPath $Identity -ErrorAction Stop).Path
$identityObject = Get-Content -LiteralPath $identityPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
$version = Get-RequiredString -Object $identityObject -Name 'normalizedVersion'
$sourceSha = Get-RequiredString -Object $identityObject -Name 'sourceSha'
$workflowSha = Get-RequiredString -Object $identityObject -Name 'workflowSha'
$manifestPath = Join-Path $repoRoot 'distribution/release-assets.json'
$manifestObject = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
$manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $version -ceq '0.0.0') {
    throw "Invalid normalizedVersion '$version'."
}
if ($sourceSha -cnotmatch '^[0-9a-f]{40}$' -or $workflowSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Release identity contains an invalid sourceSha or workflowSha.'
}
if ($null -eq $identityObject.filenamePlan.windows) {
    throw 'Release identity does not contain filenamePlan.windows.'
}
if ([string]$identityObject.manifestSha256 -cne $manifestSha256) {
    throw 'Release identity was not derived from the checked-out distribution manifest.'
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $sourceSha) {
    throw "Checked-out HEAD '$head' does not match identity sourceSha '$sourceSha'."
}
$sourceStatus = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $sourceStatus.Count -ne 0) {
    throw "Distribution builds require a completely clean source tree matching '$sourceSha'."
}

$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
$repoFull = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/distribution-validation')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $outputFull.StartsWith("$allowedRoot$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of '$allowedRoot': $outputFull"
}
$relativeOutput = [IO.Path]::GetRelativePath($repoFull, $outputFull)
$cursor = $repoFull
foreach ($segment in $relativeOutput.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
    $cursor = Join-Path $cursor $segment
    if (Test-Path -LiteralPath $cursor) {
        $attributes = (Get-Item -LiteralPath $cursor -Force).Attributes
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Output path traverses a reparse point: $cursor"
        }
    }
}
if (Test-Path -LiteralPath $outputFull) {
    Remove-Item -LiteralPath $outputFull -Recurse -Force
}

$publishDirectory = Join-Path $outputFull 'work/payload'
$velopackDirectory = Join-Path $outputFull 'work/velopack'
$assetDirectory = Join-Path $outputFull 'assets'
$evidenceDirectory = Join-Path $outputFull 'evidence'
New-Item -ItemType Directory -Force -Path $publishDirectory, $velopackDirectory, $assetDirectory, $evidenceDirectory | Out-Null

$project = Join-Path $repoRoot 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-f', 'net10.0',
    '-r', 'win-x64',
    '-o', $publishDirectory,
    '-p:PublishSingleFile=true',
    '--self-contained', 'true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$version",
    '-p:DistributionBuild=true',
    "-p:DistributionVersion=$version",
    "-p:DistributionSourceSha=$sourceSha",
    "-p:GitHubRefName=$version",
    '--ignore-failed-sources'
)
Invoke-Checked -FilePath 'dotnet' -ArgumentList $publishArgs -Label 'Windows self-contained publish'

$mainExecutable = Join-Path $publishDirectory 'Unlimotion.Desktop.exe'
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
    throw "Published main executable was not created: $mainExecutable"
}
$debugFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object Extension -CEQ '.pdb')
$removedDebugSymbols = @($debugFiles | ForEach-Object { $_.Name })
foreach ($debugFile in $debugFiles) {
    Remove-Item -LiteralPath $debugFile.FullName -Force
}
if (@(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object Extension -CEQ '.pdb').Count -ne 0) {
    throw 'Windows publish still contains PDB files after the packaging-only symbol cleanup.'
}

function Get-CanonicalAssetName {
    param(
        [Parameter(Mandatory)] [string]$ConvenienceField,
        [Parameter(Mandatory)] [string]$AssetId
    )
    $name = Get-RequiredString -Object $identityObject.filenamePlan.windows -Name $ConvenienceField
    if ([IO.Path]::GetFileName($name) -cne $name -or $name -in @('.', '..') -or $name -match '[\x00-\x1f]') {
        throw "Unsafe planned file name for '$AssetId': $name"
    }
    $byId = $identityObject.filenamePlan.byAssetId.PSObject.Properties[$AssetId]
    if ($null -eq $byId -or [string]$byId.Value -cne $name) {
        throw "Convenience filename '$ConvenienceField' does not match filenamePlan.byAssetId['$AssetId']."
    }
    $manifestMatches = @($manifestObject.assets | Where-Object id -CEQ $AssetId)
    if ($manifestMatches.Count -ne 1) { throw "Manifest asset '$AssetId' is not unique." }
    $expected = ([string]$manifestMatches[0].filenameTemplate).Replace('{normalizedVersion}', $version)
    if ($name -cne $expected) { throw "Planned file '$name' does not match manifest '$expected'." }
    return $name
}

$legacyPortableName = Get-CanonicalAssetName -ConvenienceField 'legacyPortableX64' -AssetId 'windows-portable-x64-legacy'
$legacyPortablePath = Join-Path $assetDirectory $legacyPortableName
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $legacyPortablePath -CompressionLevel Optimal

$icon = Join-Path $repoRoot 'src/Unlimotion.Desktop/Assets/Unlimotion.ico'
$packArgs = @(
    'pack',
    '--packId', 'Unlimotion',
    '--packVersion', $version,
    '--packDir', $publishDirectory,
    '--outputDir', $velopackDirectory,
    '--channel', 'win',
    '--runtime', 'win-x64',
    '--mainExe', 'Unlimotion.Desktop.exe',
    '--packTitle', 'Unlimotion',
    '--packAuthors', 'Kibnet',
    '--icon', $icon,
    '--yes',
    '--skip-updates'
)
Invoke-Checked -FilePath $VpkPath -ArgumentList $packArgs -Label 'Velopack Windows pack'

$velopackFields = [ordered]@{
    updaterFeedLegacy = 'windows-feed-legacy'
    updaterFeedJson = 'windows-feed-json'
    updaterPackageX64 = 'windows-updater-package-x64'
    setupX64 = 'windows-setup-x64'
    portableX64 = 'windows-portable-x64'
}
$produced = [Collections.Generic.List[object]]::new()
foreach ($field in $velopackFields.Keys) {
    $name = Get-CanonicalAssetName -ConvenienceField $field -AssetId $velopackFields[$field]
    $source = Join-Path $velopackDirectory $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Velopack did not produce expected '$field' asset '$name'."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $assetDirectory $name)
}

$expectedNames = @($velopackFields.Keys | ForEach-Object { Get-CanonicalAssetName -ConvenienceField $_ -AssetId $velopackFields[$_] }) + $legacyPortableName
$caseInsensitiveNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in $expectedNames) {
    if (-not $caseInsensitiveNames.Add($name)) { throw "Case-insensitive artifact filename collision: $name" }
}
foreach ($name in $expectedNames) {
    $path = Join-Path $assetDirectory $name
    $item = Get-Item -LiteralPath $path
    if ($item.Length -le 0) { throw "Produced artifact '$name' is empty." }
    $produced.Add([ordered]@{
        fileName = $name
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$builderEvidencePath = Join-Path $evidenceDirectory 'builder-evidence.json'
$builderEvidence = [ordered]@{
    schemaVersion = 1
    kind = 'windows-distribution-builder-evidence'
    status = 'pass'
    platform = 'windows'
    architecture = 'x64'
    normalizedVersion = $version
    sourceSha = $sourceSha
    workflowSha = $workflowSha
    manifestSha256 = $manifestSha256
    sourceCheck = 'passed'
    mainExecutableSha256 = (Get-FileHash -LiteralPath $mainExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    removedDebugSymbols = $removedDebugSymbols
    artifacts = @($produced)
    productionReady = $false
}
$builderEvidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $builderEvidencePath -Encoding utf8NoBOM

if ($GitHubOutputPath) {
    "asset-directory=$assetDirectory" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8NoBOM
    "builder-evidence=$builderEvidencePath" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8NoBOM
}

Write-Output "Built $($produced.Count) Windows x64 candidate artifacts in $assetDirectory."
