[CmdletBinding()]
param(
    [ValidateSet('Record', 'Validate', 'TransportReceipt', 'MergePlatform', 'Aggregate')]
    [string]$Mode = 'Validate',

    [string]$Manifest,
    [string]$SupportMatrix,
    [string]$EvidenceSchema,
    [string]$Identity,
    [string]$ArtifactDirectory,
    [string]$Evidence,
    [string[]]$AssetId,
    [string[]]$EvidencePath,
    [string[]]$NativeEvidencePath,
    [string]$TransportReceiptPath,
    [string]$OutputChecksums,
    [string]$Platform,
    [string]$Architecture,
    [ValidateSet('unsigned', 'test', 'production', 'notApplicable')]
    [string]$SignatureProfile = 'notApplicable',
    [string]$ArtifactTransportId,
    [string]$ArtifactTransportDigest,
    [string]$ArtifactTransportName,
    [int]$ArtifactRetentionDays = 7,
    [string]$TransportArchivePath,
    [string]$UnixExecutablePath,
    [string]$UnixAssetId,
    [string]$ExpectedUnixMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$AssetId = @($AssetId | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$EvidencePath = @($EvidencePath | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$NativeEvidencePath = @($NativeEvidencePath | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

function Resolve-RequiredPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label,
        [switch]$Container
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        throw "$Label does not exist: $Path"
    }

    $item = Get-Item -LiteralPath $resolved.Path
    if ($Container -and -not $item.PSIsContainer) {
        throw "$Label must be a directory: $Path"
    }
    if (-not $Container -and $item.PSIsContainer) {
        throw "$Label must be a file: $Path"
    }

    return $resolved.Path
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label
    )

    $resolved = Resolve-RequiredPath -Path $Path -Label $Label
    try {
        return Get-Content -LiteralPath $resolved -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "$Label is not valid JSON: $resolved. $($_.Exception.Message)"
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)] [object]$Value,
        [Parameter(Mandatory)] [string]$Path
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}

function Get-LowerFileSha256 {
    param([Parameter(Mandatory)] [string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [switch]$Required
    )

    $property = $Object.PSObject.Properties | Where-Object Name -CEQ $Name
    if ($null -eq $property) {
        if ($Required) { throw "Required property '$Name' is missing." }
        return $null
    }
    return $property.Value
}

function Assert-SafeFileName {
    param([Parameter(Mandatory)] [string]$Value, [Parameter(Mandatory)] [string]$Label)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -cne [IO.Path]::GetFileName($Value) -or $Value -match '[\x00-\x1f]') {
        throw "$Label must be a non-empty plain file name."
    }
}

function Assert-ExactPropertyNames {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string[]]$Expected,
        [Parameter(Mandatory)] [string]$Label
    )
    $actualNames = @($Object.PSObject.Properties.Name | Sort-Object)
    $expectedNames = @($Expected | Sort-Object)
    if (($actualNames -join '|') -cne ($expectedNames -join '|')) {
        throw "$Label property set mismatch. Expected: $($expectedNames -join ', '); actual: $($actualNames -join ', ')."
    }
}

function Test-IntegerValue {
    param([object]$Value)
    return $Value -is [sbyte] -or $Value -is [byte] -or $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or $Value -is [int64] -or $Value -is [uint64]
}

function Get-StrictStringProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Label
    )
    $value = Get-PropertyValue -Object $Object -Name $Name -Required
    if ($value -isnot [string]) {
        throw "$Label property '$Name' must be a JSON string."
    }
    return $value
}

function Get-StrictIntegerProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Label
    )
    $value = Get-PropertyValue -Object $Object -Name $Name -Required
    if (-not (Test-IntegerValue $value)) {
        throw "$Label property '$Name' must be a JSON integer."
    }
    return [long]$value
}

function Get-StrictBooleanProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Label
    )
    $value = Get-PropertyValue -Object $Object -Name $Name -Required
    if ($value -isnot [bool]) {
        throw "$Label property '$Name' must be a JSON boolean."
    }
    return [bool]$value
}

function Get-StrictArrayProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Label
    )
    $property = $Object.PSObject.Properties | Where-Object Name -CEQ $Name
    if ($null -eq $property) {
        throw "Required property '$Name' is missing."
    }
    $value = $property.Value
    if ($value -isnot [Array]) {
        throw "$Label property '$Name' must be a JSON array."
    }
    return ,$value
}

function Assert-JsonSchema {
    param([Parameter(Mandatory)] [object]$Value, [Parameter(Mandatory)] [string]$Label)
    $json = $Value | ConvertTo-Json -Depth 100 -Compress
    try {
        $valid = Test-Json -Json $json -SchemaFile $EvidenceSchema -ErrorAction Stop
    }
    catch {
        throw "$Label does not satisfy the evidence schema: $($_.Exception.Message)"
    }
    if (-not $valid) { throw "$Label does not satisfy the evidence schema."
    }
}

function Get-IdentityProjection {
    param([Parameter(Mandatory)] [object]$IdentityObject)
    return [ordered]@{
        rawTag = [string](Get-PropertyValue -Object $IdentityObject -Name 'rawTag' -Required)
        normalizedVersion = [string](Get-PropertyValue -Object $IdentityObject -Name 'normalizedVersion' -Required)
        sourceSha = [string](Get-PropertyValue -Object $IdentityObject -Name 'sourceSha' -Required)
        workflowSha = [string](Get-PropertyValue -Object $IdentityObject -Name 'workflowSha' -Required)
        tagBinding = [string](Get-PropertyValue -Object $IdentityObject -Name 'tagBinding' -Required)
        manifestSha256 = [string](Get-PropertyValue -Object $IdentityObject -Name 'manifestSha256' -Required)
        supportMatrixSha256 = [string](Get-PropertyValue -Object $IdentityObject -Name 'supportMatrixSha256' -Required)
        signatureProfile = [string](Get-PropertyValue -Object $IdentityObject -Name 'signatureProfile' -Required)
    }
}

function Assert-IdentityContract {
    param([Parameter(Mandatory)] [object]$IdentityObject)
    $projection = Get-IdentityProjection -IdentityObject $IdentityObject
    if ($projection.rawTag -cnotmatch '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $projection.rawTag.TrimStart('v') -cne $projection.normalizedVersion -or
        $projection.normalizedVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $projection.normalizedVersion -ceq '0.0.0' -or
        $projection.sourceSha -cnotmatch '^[0-9a-f]{40}$' -or
        $projection.workflowSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Release identity contains an invalid version, sourceSha or workflowSha.'
    }
    if ($projection.tagBinding -notin @('notApplicable', 'required')) { throw 'Release identity tagBinding is invalid.' }
    Assert-HexSha256 -Value $projection.manifestSha256 -Label 'Identity manifest SHA-256'
    Assert-HexSha256 -Value $projection.supportMatrixSha256 -Label 'Identity support-matrix SHA-256'
    if ($projection.manifestSha256 -cne (Get-LowerFileSha256 -Path $Manifest)) {
        throw 'Release identity manifestSha256 does not match the exact manifest bytes.'
    }
    if ($projection.supportMatrixSha256 -cne (Get-LowerFileSha256 -Path $SupportMatrix)) {
        throw 'Release identity supportMatrixSha256 does not match the exact support-matrix bytes.'
    }
    if ($projection.signatureProfile -notin @('test', 'production')) {
        throw "Release identity signatureProfile '$($projection.signatureProfile)' is invalid."
    }
    return $projection
}

function Assert-IdentityProjectionEquals {
    param(
        [Parameter(Mandatory)] [object]$Expected,
        [Parameter(Mandatory)] [object]$Actual,
        [Parameter(Mandatory)] [string]$Label
    )
    foreach ($field in @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256', 'signatureProfile')) {
        if ((Get-StrictStringProperty -Object $Actual -Name $field -Label $Label) -cne [string]$Expected.$field) {
            throw "$Label identity field '$field' does not match."
        }
    }
}

function Convert-SymbolicModeToOctal {
    param([Parameter(Mandatory)] [string]$Mode)
    if ($Mode -cnotmatch '^[-dlcbps]([r-][w-][xStT-]){3}$') {
        throw "Unsupported tar mode '$Mode'."
    }
    $digits = for ($offset = 1; $offset -le 7; $offset += 3) {
        $digit = 0
        if ($Mode[$offset] -ceq 'r') { $digit += 4 }
        if ($Mode[$offset + 1] -ceq 'w') { $digit += 2 }
        if ($Mode[$offset + 2] -in @('x', 's', 't')) { $digit += 1 }
        [string]$digit
    }
    return '0' + ($digits -join '')
}

function Get-UnixMode {
    param([Parameter(Mandatory)] [string]$Path)
    if ($IsWindows) { throw 'Unix mode validation requires a Unix runner.' }
    $flags = [IO.File]::GetUnixFileMode($Path)
    $owner = 0
    $group = 0
    $other = 0
    if (($flags -band [IO.UnixFileMode]::UserRead) -ne 0) { $owner += 4 }
    if (($flags -band [IO.UnixFileMode]::UserWrite) -ne 0) { $owner += 2 }
    if (($flags -band [IO.UnixFileMode]::UserExecute) -ne 0) { $owner += 1 }
    if (($flags -band [IO.UnixFileMode]::GroupRead) -ne 0) { $group += 4 }
    if (($flags -band [IO.UnixFileMode]::GroupWrite) -ne 0) { $group += 2 }
    if (($flags -band [IO.UnixFileMode]::GroupExecute) -ne 0) { $group += 1 }
    if (($flags -band [IO.UnixFileMode]::OtherRead) -ne 0) { $other += 4 }
    if (($flags -band [IO.UnixFileMode]::OtherWrite) -ne 0) { $other += 2 }
    if (($flags -band [IO.UnixFileMode]::OtherExecute) -ne 0) { $other += 1 }
    return "0$owner$group$other"
}

function Test-TarUnixMode {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$OriginalPath,
        [Parameter(Mandatory)] [string]$AssetId,
        [Parameter(Mandatory)] [string]$ExpectedMode
    )
    if ($ExpectedMode -cne '0755') { throw 'Stage-3 Linux transport requires exact Unix mode 0755.' }
    $archive = Resolve-RequiredPath -Path $ArchivePath -Label 'Linux transport archive'
    $original = Resolve-RequiredPath -Path $OriginalPath -Label 'Original Unix executable'
    Assert-SafeFileName -Value ([IO.Path]::GetFileName($archive)) -Label 'Transport archive name'
    $originalMode = Get-UnixMode -Path $original
    if ($originalMode -cne $ExpectedMode) { throw "Original Unix mode is '$originalMode', expected '$ExpectedMode'." }

    $entries = @(& tar -tf $archive 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Cannot list transport archive '$archive': $($entries -join ' ')" }
    $baseName = [IO.Path]::GetFileName($original)
    $entryMatches = @($entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and [IO.Path]::GetFileName(([string]$_).TrimEnd('/')) -ceq $baseName })
    if ($entryMatches.Count -ne 1) { throw "Transport archive must contain exactly one '$baseName' entry; found $($entryMatches.Count)." }
    $entry = [string]$entryMatches[0]
    if ($entry.StartsWith('/', [StringComparison]::Ordinal) -or $entry -match '(^|/)\.\.(/|$)') {
        throw "Transport archive entry is unsafe: $entry"
    }
    $listing = @(& tar -tvf $archive $entry 2>&1)
    if ($LASTEXITCODE -ne 0 -or $listing.Count -ne 1) { throw "Cannot inspect transport archive entry '$entry'." }
    $modeToken = ([string]$listing[0] -split '\s+', 2)[0]
    if ($modeToken[0] -cne '-') { throw "Transport archive entry '$entry' is not a regular file." }
    $storedMode = Convert-SymbolicModeToOctal -Mode $modeToken
    if ($storedMode -cne $ExpectedMode) { throw "Tar stored Unix mode is '$storedMode', expected '$ExpectedMode'." }

    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-transport-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        $extractOutput = @(& tar -xf $archive -C $temporaryRoot $entry 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Cannot extract transport archive entry '$entry': $($extractOutput -join ' ')" }
        $restored = Resolve-RequiredPath -Path (Join-Path $temporaryRoot $entry) -Label 'Restored Unix executable'
        $restoredMode = Get-UnixMode -Path $restored
        if ($restoredMode -cne $ExpectedMode) { throw "Restored Unix mode is '$restoredMode', expected '$ExpectedMode'." }
        $originalSha = Get-LowerFileSha256 -Path $original
        $restoredSha = Get-LowerFileSha256 -Path $restored
        if ($originalSha -cne $restoredSha) { throw 'Restored Unix executable bytes do not match the original.' }
        return [ordered]@{
            applicability = 'required'
            assetId = $AssetId
            archiveFileName = [IO.Path]::GetFileName($archive)
            archiveSha256 = Get-LowerFileSha256 -Path $archive
            archiveEntry = $entry
            originalMode = $originalMode
            tarStoredMode = $storedMode
            restoredMode = $restoredMode
            originalSha256 = $originalSha
            restoredSha256 = $restoredSha
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
}

function Get-NotApplicableUnixMode {
    return [ordered]@{
        applicability = 'notApplicable'
        assetId = $null
        archiveFileName = $null
        archiveSha256 = $null
        archiveEntry = $null
        originalMode = $null
        tarStoredMode = $null
        restoredMode = $null
        originalSha256 = $null
        restoredSha256 = $null
    }
}

function Get-ManifestAsset {
    param(
        [Parameter(Mandatory)] [object]$ManifestObject,
        [Parameter(Mandatory)] [string]$Id
    )

    $matches = @($ManifestObject.assets | Where-Object { $_.id -ceq $Id })
    if ($matches.Count -ne 1) {
        throw "Manifest must contain exactly one asset with id '$Id'; found $($matches.Count)."
    }
    return $matches[0]
}

function Get-PlannedFileName {
    param(
        [Parameter(Mandatory)] [object]$IdentityObject,
        [Parameter(Mandatory)] [string]$Id
    )

    if ($null -eq $IdentityObject.filenamePlan) {
        throw 'Identity does not contain filenamePlan.'
    }

    $plan = $IdentityObject.filenamePlan
    if ($null -ne $plan.byAssetId) {
        $plan = $plan.byAssetId
    }

    if ($plan -is [System.Array]) {
        $matches = @($plan | Where-Object { $_.assetId -ceq $Id -or $_.id -ceq $Id })
        if ($matches.Count -ne 1) {
            throw "Identity filenamePlan must contain exactly one entry for '$Id'."
        }
        $value = if ($null -ne $matches[0].fileName) { $matches[0].fileName } else { $matches[0].filename }
    }
    else {
        $property = $plan.PSObject.Properties | Where-Object Name -CEQ $Id
        if ($null -eq $property) {
            throw "Identity filenamePlan does not contain '$Id'."
        }
        $value = $property.Value
        if ($value -isnot [string]) {
            if ($null -ne $value.fileName) { $value = $value.fileName }
            elseif ($null -ne $value.filename) { $value = $value.filename }
        }
    }

    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Identity filenamePlan entry '$Id' does not contain a file name."
    }
    if ([IO.Path]::GetFileName($value) -cne $value) {
        throw "Identity filenamePlan entry '$Id' must be a plain file name: $value"
    }
    return $value
}

function Assert-HexSha256 {
    param([Parameter(Mandatory)] [string]$Value, [Parameter(Mandatory)] [string]$Label)
    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Label must be a 64-character lowercase SHA-256 value."
    }
}

function Assert-EvidenceIdentity {
    param([Parameter(Mandatory)] [object]$EvidenceObject)

    if ($EvidenceObject.status -cne 'pass') {
        throw "Evidence status must be 'pass'; found '$($EvidenceObject.status)'."
    }
    if ($EvidenceObject.sourceSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Evidence sourceSha is missing or invalid.'
    }
    if ($EvidenceObject.workflowSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Evidence workflowSha is missing or invalid.'
    }
    if ($EvidenceObject.normalizedVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $EvidenceObject.normalizedVersion -ceq '0.0.0') {
        throw 'Evidence normalizedVersion is missing or invalid.'
    }
}

function Test-ArtifactRelations {
    param(
        [Parameter(Mandatory)] [object]$ManifestObject,
        [Parameter(Mandatory)] [object[]]$ArtifactRecords,
        [Parameter(Mandatory)] [string]$ArtifactRoot,
        [Parameter(Mandatory)] [string]$NormalizedVersion
    )

    $recordsById = @{}
    foreach ($artifact in $ArtifactRecords) {
        $recordsById[[string]$artifact.assetId] = $artifact
    }

    $relationEvidence = [Collections.Generic.List[object]]::new()
    foreach ($relation in @($ManifestObject.relations)) {
        $feedId = [string]$relation.feedAssetId
        $packageId = [string]$relation.packageAssetId
        if (-not $recordsById.ContainsKey($feedId) -or -not $recordsById.ContainsKey($packageId)) {
            continue
        }

        $feedRecord = $recordsById[$feedId]
        $packageRecord = $recordsById[$packageId]
        $feedPath = Join-Path $ArtifactRoot ([string]$feedRecord.fileName)
        $packagePath = Join-Path $ArtifactRoot ([string]$packageRecord.fileName)
        $packageItem = Get-Item -LiteralPath $packagePath
        $sha1 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA1).Hash.ToLowerInvariant()
        $sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

        if ($relation.format -ceq 'velopack-json-v1') {
            try { $feed = Get-Content -LiteralPath $feedPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 20 }
            catch { throw "Velopack feed '$($feedRecord.fileName)' is invalid JSON: $($_.Exception.Message)" }
            if (@($feed.Assets).Count -ne 1) { throw "Fresh feed '$($feedRecord.fileName)' must contain exactly one package entry." }
            $matches = @($feed.Assets | Where-Object FileName -CEQ ([string]$packageRecord.fileName))
            if ($matches.Count -ne 1) { throw "Feed '$($feedRecord.fileName)' must reference '$($packageRecord.fileName)' exactly once." }
            $entry = $matches[0]
            if ([string]$entry.PackageId -cne [string]$relation.packageId -or [string]$entry.Type -cne 'Full') {
                throw "Feed '$($feedRecord.fileName)' has the wrong package id or package type."
            }
            if ([string]$entry.Version -cne $NormalizedVersion -or [long]$entry.Size -ne $packageItem.Length) {
                throw "Feed '$($feedRecord.fileName)' version or package size does not match the exact package bytes."
            }
            if ([string]$entry.SHA1 -cne $sha1 -and ([string]$entry.SHA1).ToLowerInvariant() -cne $sha1) {
                throw "Feed '$($feedRecord.fileName)' SHA-1 does not match the exact package bytes."
            }
            if (([string]$entry.SHA256).ToLowerInvariant() -cne $sha256) {
                throw "Feed '$($feedRecord.fileName)' SHA-256 does not match the exact package bytes."
            }
        }
        elseif ($relation.format -ceq 'squirrel-releases-v1') {
            $lines = @(Get-Content -LiteralPath $feedPath -Encoding utf8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($lines.Count -ne 1) { throw "Fresh legacy feed '$($feedRecord.fileName)' must contain exactly one package entry." }
            $matches = @($lines | ForEach-Object {
                $match = [regex]::Match($_.TrimStart([char]0xfeff), '^([0-9A-Fa-f]{40})\s+(\S+)\s+([0-9]+)$')
                if (-not $match.Success) { throw "Legacy feed '$($feedRecord.fileName)' contains an invalid record." }
                if ($match.Groups[2].Value -ceq [string]$packageRecord.fileName) { $match }
            })
            if ($matches.Count -ne 1) { throw "Legacy feed '$($feedRecord.fileName)' must reference '$($packageRecord.fileName)' exactly once." }
            if ($matches[0].Groups[1].Value.ToLowerInvariant() -cne $sha1 -or
                [long]$matches[0].Groups[3].Value -ne $packageItem.Length) {
                throw "Legacy feed '$($feedRecord.fileName)' hash or size does not match the exact package bytes."
            }
        }
        else {
            throw "Unsupported feed relation format '$($relation.format)'."
        }

        $relationEvidence.Add([ordered]@{
            relationId = [string]$relation.id
            feedAssetId = $feedId
            packageAssetId = $packageId
            channel = [string]$relation.channel
            format = [string]$relation.format
            packageSha1 = $sha1
            packageSha256 = $sha256
            packageSize = $packageItem.Length
            status = 'pass'
        })
    }
    return @($relationEvidence)
}

function Assert-ArtifactEvidence {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$IdentityProjection,
        [switch]$ValidateBytes
    )

    $resolved = Resolve-RequiredPath -Path $Path -Label 'Artifact evidence'
    $report = Read-JsonFile -Path $resolved -Label 'Artifact evidence'
    Assert-JsonSchema -Value $report -Label "Artifact evidence '$resolved'"
    Assert-EvidenceIdentity -EvidenceObject $report
    foreach ($field in @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')) {
        if ([string](Get-PropertyValue -Object $report -Name $field -Required) -cne [string]$IdentityProjection.$field) {
            throw "Artifact evidence '$resolved' identity field '$field' does not match."
        }
    }
    if ([string](Get-PropertyValue -Object $report -Name 'identitySignatureProfile' -Required) -cne $IdentityProjection.signatureProfile) {
        throw "Artifact evidence '$resolved' identity signature profile does not match."
    }
    if ((Get-PropertyValue -Object $report -Name 'productionReady' -Required) -ne $false) {
        throw "Artifact evidence '$resolved' cannot be productionReady."
    }

    $artifactRoot = $null
    if ($ValidateBytes) {
        $producerRoot = Split-Path -Parent (Split-Path -Parent $resolved)
        $artifactRoot = Join-Path $producerRoot 'assets'
        $artifactRoot = Resolve-RequiredPath -Path $artifactRoot -Label "Assets for '$resolved'" -Container
    }

    $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($artifact in @($report.artifacts)) {
        $id = [string](Get-PropertyValue -Object $artifact -Name 'assetId' -Required)
        $fileName = [string](Get-PropertyValue -Object $artifact -Name 'fileName' -Required)
        if (-not $seenIds.Add($id)) { throw "Artifact evidence '$resolved' duplicates asset '$id'." }
        if (-not $seenNames.Add($fileName)) { throw "Artifact evidence '$resolved' duplicates file '$fileName'." }
        Assert-SafeFileName -Value $fileName -Label "Artifact '$id' file name"
        $manifestAsset = Get-ManifestAsset -ManifestObject $manifestObject -Id $id
        if ($manifestAsset.platform -cne [string]$report.platform -or $manifestAsset.architecture -cne [string]$report.architecture) {
            throw "Artifact '$id' does not belong to $($report.platform)/$($report.architecture)."
        }
        $plannedName = Get-PlannedFileName -IdentityObject $identityObject -Id $id
        if ($fileName -cne $plannedName) { throw "Artifact '$id' is named '$fileName', expected '$plannedName'." }
        Assert-HexSha256 -Value ([string]$artifact.sha256 -as [string]) -Label "Evidence hash for '$id'"
        if ($ValidateBytes) {
            $file = Resolve-RequiredPath -Path (Join-Path $artifactRoot $fileName) -Label "Artifact '$id'"
            $item = Get-Item -LiteralPath $file
            if ($item.Length -ne [long]$artifact.size -or (Get-LowerFileSha256 -Path $file) -cne [string]$artifact.sha256) {
                throw "Artifact '$fileName' bytes no longer match evidence."
            }
        }
    }

    if ($ValidateBytes) {
        $actualRelations = @(Test-ArtifactRelations -ManifestObject $manifestObject -ArtifactRecords @($report.artifacts) -ArtifactRoot $artifactRoot -NormalizedVersion $IdentityProjection.normalizedVersion)
        $evidenceRelations = @($report.relations)
        if ($actualRelations.Count -ne $evidenceRelations.Count) { throw "Artifact evidence '$resolved' relation coverage is incomplete." }
        foreach ($actual in $actualRelations) {
            $matches = @($evidenceRelations | Where-Object relationId -CEQ ([string]$actual.relationId))
            if ($matches.Count -ne 1) { throw "Relation '$($actual.relationId)' is missing or duplicated in '$resolved'." }
            foreach ($field in @('feedAssetId', 'packageAssetId', 'channel', 'format', 'packageSha1', 'packageSha256', 'packageSize', 'status')) {
                if ([string]$matches[0].$field -cne [string]$actual.$field) { throw "Relation '$($actual.relationId)' field '$field' does not match exact bytes." }
            }
        }
    }

    return [ordered]@{
        report = $report
        path = $resolved
        assetRoot = $artifactRoot
        reference = [ordered]@{
            evidenceId = "$($report.platform)-$($report.architecture)"
            fileName = [IO.Path]::GetFileName($resolved)
            sha256 = Get-LowerFileSha256 -Path $resolved
            platform = [string]$report.platform
            architecture = [string]$report.architecture
        }
    }
}

function Assert-RawIdentity {
    param(
        [Parameter(Mandatory)] [object]$Report,
        [Parameter(Mandatory)] [object]$IdentityProjection,
        [Parameter(Mandatory)] [string]$Label,
        [string[]]$Fields = @('normalizedVersion', 'sourceSha', 'workflowSha')
    )
    foreach ($field in $Fields) {
        if ((Get-StrictStringProperty -Object $Report -Name $field -Label $Label) -cne [string]$IdentityProjection.$field) {
            throw "$Label identity field '$field' does not match."
        }
    }
}

function New-NativeEvidenceReference {
    param(
        [Parameter(Mandatory)] [object]$InputEvidence
    )

    $fileName = [IO.Path]::GetFileName([string]$InputEvidence.path)
    $rawReference = switch ($fileName) {
        'native-inputs.json' { [pscustomobject]@{ kind = 'distribution-android-native-inputs'; mode = 'native-inputs' } }
        'native-provenance.json' { [pscustomobject]@{ kind = 'distribution-android-native-provenance'; mode = 'native-provenance' } }
        default { $null }
    }
    $kind = if ($null -ne $rawReference) {
        [string]$rawReference.kind
    }
    else {
        [string](Get-PropertyValue -Object $InputEvidence.report -Name 'kind' -Required)
    }
    if ($kind -cnotmatch '^[a-z0-9-]+$') {
        throw "Native evidence '$($InputEvidence.path)' has an invalid kind '$kind'."
    }
    $reportedMode = if ($null -ne $rawReference) { [string]$rawReference.mode } else { [string](Get-PropertyValue -Object $InputEvidence.report -Name 'mode') }
    $mode = if ([string]::IsNullOrWhiteSpace($reportedMode)) { $kind } else { $reportedMode }
    if ($mode -cnotmatch '^[a-z0-9-]+$') {
        throw "Native evidence '$($InputEvidence.path)' has an invalid mode '$mode'."
    }
    Assert-SafeFileName -Value $fileName -Label 'Native evidence file name'
    Assert-HexSha256 -Value ([string]$InputEvidence.sha256) -Label "Native evidence '$fileName' SHA-256"
    return [ordered]@{
        fileName = $fileName
        sha256 = [string]$InputEvidence.sha256
        kind = $kind
        mode = $mode
    }
}

function Get-ArtifactById {
    param([Parameter(Mandatory)] [hashtable]$ArtifactsById, [Parameter(Mandatory)] [string]$Id)
    if (-not $ArtifactsById.ContainsKey($Id)) { throw "Platform evidence lacks required artifact '$Id'." }
    return $ArtifactsById[$Id]
}

function Assert-RawArtifactHash {
    param(
        [Parameter(Mandatory)] [hashtable]$ArtifactsByName,
        [Parameter(Mandatory)] [string]$FileName,
        [Parameter(Mandatory)] [string]$Sha256,
        [Parameter(Mandatory)] [string]$Label
    )
    Assert-SafeFileName -Value $FileName -Label "$Label file name"
    if (-not $ArtifactsByName.ContainsKey($FileName)) { throw "$Label references unknown artifact '$FileName'." }
    Assert-HexSha256 -Value $Sha256 -Label "$Label SHA-256"
    if ([string]$ArtifactsByName[$FileName].sha256 -cne $Sha256) { throw "$Label SHA-256 does not match artifact evidence." }
    return [string]$ArtifactsByName[$FileName].assetId
}

function New-NativeCell {
    param(
        [Parameter(Mandatory)] [string]$Id,
        [Parameter(Mandatory)] [string]$Platform,
        [Parameter(Mandatory)] [string]$Architecture,
        [Parameter(Mandatory)] [string]$OsName,
        [Parameter(Mandatory)] [string]$OsVersion,
        [Parameter(Mandatory)] [string]$NativeMode,
        [Parameter(Mandatory)] [string]$Metadata,
        [Parameter(Mandatory)] [string]$Install,
        [Parameter(Mandatory)] [string]$Launch,
        [Parameter(Mandatory)] [string]$Signature,
        [Parameter(Mandatory)] [string]$NegativeControl,
        [Parameter(Mandatory)] [string]$DirectFuse,
        [Parameter(Mandatory)] [string]$EvidenceFile,
        [Parameter(Mandatory)] [string]$EvidenceSha256,
        [Parameter(Mandatory)] [string[]]$CellAssetIds
    )
    return [ordered]@{
        id = $Id
        status = 'pass'
        platform = $Platform
        architecture = $Architecture
        osName = $OsName
        osVersion = $OsVersion
        mode = $NativeMode
        metadata = $Metadata
        install = $Install
        launch = $Launch
        signature = $Signature
        negativeControl = $NegativeControl
        directFuse = $DirectFuse
        evidenceFile = $EvidenceFile
        evidenceSha256 = $EvidenceSha256
        assetIds = @($CellAssetIds)
    }
}

function Assert-NativeCellSemantics {
    param([Parameter(Mandatory)] [object]$Cell)
    if ([string]$Cell.status -cne 'pass') { throw "Native cell '$($Cell.id)' is not passing." }
    $id = [string]$Cell.id
    switch -Regex ($id) {
        '^windows-server-2022-x64$' {
            if ([string]$Cell.platform -cne 'windows' -or [string]$Cell.architecture -cne 'x64' -or
                [string]$Cell.osName -cne 'Windows Server' -or [string]$Cell.osVersion -cne '2022' -or
                [string]$Cell.mode -cne 'setup-and-portable-install-launch-with-seeded-isolated-task-storage' -or
                [string]$Cell.metadata -cne 'pass' -or [string]$Cell.install -cne 'pass' -or [string]$Cell.launch -cne 'pass' -or
                [string]$Cell.signature -cne 'stateRecorded') { throw "Native cell '$id' has the wrong Windows OS/arch/outcome contract." }
        }
        '^debian-(12|13)-x64-(clean|upgrade|appimage|missing-runtime-negative)$' {
            $match = [regex]::Match($id, '^debian-(12|13)-x64-(clean|upgrade|appimage|missing-runtime-negative)$')
            $version = $match.Groups[1].Value
            $mode = $match.Groups[2].Value
            $configuredMode = "$mode-with-seeded-isolated-task-storage"
            if ([string]$Cell.platform -cne 'linux' -or [string]$Cell.architecture -cne 'x64' -or
                [string]$Cell.osName -cne 'debian' -or [string]$Cell.osVersion -cne $version -or [string]$Cell.mode -cne $configuredMode -or
                [string]$Cell.metadata -cne 'pass' -or
                [string]$Cell.signature -cne 'notApplicable') {
                throw "Native cell '$id' has the wrong Debian OS/arch/mode contract."
            }
            if ($mode -ceq 'appimage') {
                if ([string]$Cell.install -cne 'extractPassed' -or [string]$Cell.launch -cne 'pass' -or
                    [string]$Cell.directFuse -cne 'notVerified' -or [string]$Cell.negativeControl -cne 'notApplicable') {
                    throw "Native cell '$id' has the wrong AppImage evidence semantics."
                }
            }
            elseif ($mode -ceq 'missing-runtime-negative') {
                if ([string]$Cell.install -cne 'pass' -or [string]$Cell.launch -cne 'expectedFailureObserved' -or
                    [string]$Cell.negativeControl -cne 'pass' -or [string]$Cell.directFuse -cne 'notApplicable') {
                    throw "Native cell '$id' has the wrong negative-control semantics."
                }
            }
            elseif ([string]$Cell.install -cne 'pass' -or [string]$Cell.launch -cne 'pass' -or
                [string]$Cell.negativeControl -cne 'notApplicable' -or [string]$Cell.directFuse -cne 'notApplicable') {
                throw "Native cell '$id' has the wrong Debian install/launch semantics."
            }
        }
        '^macos-15-(x64|arm64)$' {
            $architecture = [regex]::Match($id, '^macos-15-(x64|arm64)$').Groups[1].Value
            if ([string]$Cell.platform -cne 'macos' -or [string]$Cell.architecture -cne $architecture -or
                [string]$Cell.osName -cne 'macOS' -or [string]$Cell.osVersion -cne '15' -or
                [string]$Cell.mode -cne 'package-and-portable-native-launch-with-seeded-isolated-task-storage' -or
                [string]$Cell.metadata -cne 'pass' -or [string]$Cell.install -cne 'pass' -or [string]$Cell.launch -cne 'pass' -or
                [string]$Cell.signature -cne 'stateRecorded') { throw "Native cell '$id' has the wrong macOS OS/arch/outcome contract." }
        }
        '^android-(arm64|x64)-apk-metadata$' {
            $architecture = [regex]::Match($id, '^android-(arm64|x64)-apk-metadata$').Groups[1].Value
            if ([string]$Cell.platform -cne 'android' -or [string]$Cell.architecture -cne $architecture -or
                [string]$Cell.osName -cne 'android' -or [string]$Cell.osVersion -cne 'notApplicable' -or
                [string]$Cell.mode -cne 'apk-metadata' -or [string]$Cell.metadata -cne 'pass' -or
                [string]$Cell.install -cne 'notApplicable' -or [string]$Cell.launch -cne 'notApplicable' -or
                [string]$Cell.signature -cne 'stateRecorded') { throw "Native cell '$id' has the wrong Android metadata contract." }
        }
        '^android-api-(23|36)-x64-emulator$' {
            $api = [regex]::Match($id, '^android-api-(23|36)-x64-emulator$').Groups[1].Value
            if ([string]$Cell.platform -cne 'android' -or [string]$Cell.architecture -cne 'x64' -or
                [string]$Cell.osName -cne 'android' -or [string]$Cell.osVersion -cne "API $api" -or
                [string]$Cell.mode -cne 'emulator' -or [string]$Cell.metadata -cne 'pass' -or
                [string]$Cell.install -cne 'pass' -or [string]$Cell.launch -cne 'pass' -or
                [string]$Cell.signature -cne 'coveredByArtifactCell') { throw "Native cell '$id' has the wrong Android emulator contract." }
        }
        default { throw "Unknown mandatory native cell '$id'." }
    }
}

function Assert-SeededTaskStorageEvidence {
    param(
        [Parameter(Mandatory)] [object]$Evidence,
        [Parameter(Mandatory)] [string]$Label
    )

    $launchConfiguration = Get-StrictStringProperty -Object $Evidence -Name 'launchConfiguration' -Label $Label
    $unconfiguredFirstRunVerified = Get-StrictBooleanProperty -Object $Evidence -Name 'unconfiguredFirstRunVerified' -Label $Label
    if ($launchConfiguration -cne 'seeded-isolated-task-storage' -or $unconfiguredFirstRunVerified) {
        throw "$Label must identify seeded isolated task storage and must not claim unconfigured first-run verification."
    }
    $configPath = Get-StrictStringProperty -Object $Evidence -Name 'configPath' -Label $Label
    $taskStoragePath = Get-StrictStringProperty -Object $Evidence -Name 'taskStoragePath' -Label $Label
    Assert-CanonicalTaskStoragePathPair -ConfigPath $configPath -TaskStoragePath $taskStoragePath -Label $Label
}

function Assert-NotApplicableTaskStorageEvidence {
    param(
        [Parameter(Mandatory)] [object]$Evidence,
        [Parameter(Mandatory)] [string]$Label
    )

    $launchConfiguration = Get-StrictStringProperty -Object $Evidence -Name 'launchConfiguration' -Label $Label
    $unconfiguredFirstRunVerified = Get-StrictBooleanProperty -Object $Evidence -Name 'unconfiguredFirstRunVerified' -Label $Label
    $configPath = Get-StrictStringProperty -Object $Evidence -Name 'configPath' -Label $Label
    $taskStoragePath = Get-StrictStringProperty -Object $Evidence -Name 'taskStoragePath' -Label $Label
    if ($launchConfiguration -cne 'notApplicable' -or $unconfiguredFirstRunVerified -or
        $configPath.Length -ne 0 -or $taskStoragePath.Length -ne 0) {
        throw "$Label must report task-storage launch configuration as notApplicable without invented paths or first-run coverage."
    }
}

function Get-CanonicalEvidencePathParts {
    param(
        [Parameter(Mandatory)] [string]$Value,
        [Parameter(Mandatory)] [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '[\x00-\x1f]') {
        throw "$Label must be a non-empty canonical absolute path."
    }

    $style = $null
    $separator = $null
    $tail = $null
    if ($Value -cmatch '^[A-Za-z]:\\') {
        if ($Value.Contains('/')) { throw "$Label must not mix Windows and POSIX separators." }
        $style = 'windows'
        $separator = '\'
        $tail = $Value.Substring(3)
    }
    elseif ($Value.StartsWith('/', [StringComparison]::Ordinal)) {
        if ($Value.Contains('\')) { throw "$Label must not mix POSIX and Windows separators." }
        $style = 'posix'
        $separator = '/'
        $tail = $Value.Substring(1)
    }
    else {
        throw "$Label must be an absolute drive-qualified Windows path or an absolute POSIX path."
    }

    $segments = @($tail -split [regex]::Escape($separator))
    if ($segments.Count -lt 2 -or @($segments | Where-Object {
                [string]::IsNullOrEmpty($_) -or $_ -in @('.', '..') -or $_ -match '[\x00-\x1f]'
            }).Count -gt 0) {
        throw "$Label must be canonical, below a non-root directory, and contain no empty/dot segments."
    }
    if ($style -ceq 'windows' -and @($segments | Where-Object { $_ -match '[<>:"|?*]' }).Count -gt 0) {
        throw "$Label contains a character forbidden in a canonical Windows path."
    }

    $lastSeparator = $Value.LastIndexOf($separator, [StringComparison]::Ordinal)
    return [pscustomobject]@{
        Style = $style
        Parent = $Value.Substring(0, $lastSeparator)
        Leaf = $segments[-1]
    }
}

function Assert-CanonicalTaskStoragePathPair {
    param(
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$TaskStoragePath,
        [Parameter(Mandatory)] [string]$Label
    )

    $config = Get-CanonicalEvidencePathParts -Value $ConfigPath -Label "$Label configPath"
    $storage = Get-CanonicalEvidencePathParts -Value $TaskStoragePath -Label "$Label taskStoragePath"
    if ($config.Leaf -cnotin @('settings.json', 'config.json') -or $storage.Leaf -cne 'Tasks') {
        throw "$Label must use settings.json/config.json and Tasks as exact leaf names."
    }
    $comparison = if ($config.Style -ceq 'windows') {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if ($config.Style -cne $storage.Style -or
        -not [string]::Equals($config.Parent, $storage.Parent, $comparison)) {
        throw "$Label configPath and taskStoragePath must share one canonical isolated parent directory."
    }
}

function Convert-WindowsNativeEvidence {
    param([Parameter(Mandatory)] [object]$InputEvidence, [Parameter(Mandatory)] [object]$IdentityProjection, [Parameter(Mandatory)] [hashtable]$ArtifactsByName, [Parameter(Mandatory)] [string]$Path)
    $report = $InputEvidence.report
    if ([string]$report.kind -cne 'windows-native-validation-evidence' -or [string]$report.status -cne 'pass' -or
        [string]$report.platform -cne 'windows' -or [string]$report.architecture -cne 'x64' -or $report.productionReady -ne $false) {
        throw 'Windows native evidence is not a passing Windows x64 Stage-3 report.'
    }
    Assert-RawIdentity -Report $report -IdentityProjection $IdentityProjection -Label 'Windows native evidence' `
        -Fields @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')
    $runner = Get-PropertyValue -Object $report -Name 'runner' -Required
    $osVersion = [string](Get-PropertyValue -Object $runner -Name 'osVersion' -Required)
    if ([string](Get-PropertyValue -Object $runner -Name 'expectedImage' -Required) -cne 'windows-2022' -or
        [string](Get-PropertyValue -Object $runner -Name 'osCaption' -Required) -cnotmatch 'Windows Server 2022' -or
        [string](Get-PropertyValue -Object $runner -Name 'imageOs' -Required) -cne 'win22' -or
        [string]::IsNullOrWhiteSpace([string](Get-PropertyValue -Object $runner -Name 'imageVersion' -Required)) -or
        $osVersion -cnotmatch '^10\.0\.20348(?:\.|$)') { throw "Windows native evidence is not from the exact windows-2022 runner: '$osVersion'." }
    if ([string](Get-PropertyValue -Object $runner -Name 'processArchitecture' -Required) -notin @('X64', 'Amd64')) { throw 'Windows native evidence runner architecture is not x64.' }
    $portable = Get-PropertyValue -Object $report -Name 'portable' -Required
    $setup = Get-PropertyValue -Object $report -Name 'setup' -Required
    $legacy = Get-PropertyValue -Object $report -Name 'legacyPortable' -Required
    $assetIds = @(
        Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName ([string]$portable.fileName) -Sha256 ([string]$portable.sha256) -Label 'Windows portable report'
        Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName ([string]$setup.fileName) -Sha256 ([string]$setup.sha256) -Label 'Windows setup report'
        Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName ([string]$legacy.fileName) -Sha256 ([string]$legacy.sha256) -Label 'Windows legacy portable report'
    )
    $portableExecutableSha = [string](Get-PropertyValue -Object $portable -Name 'executableSha256' -Required)
    $legacyExecutableSha = [string](Get-PropertyValue -Object $legacy -Name 'executableSha256' -Required)
    $installedExecutableSha = [string](Get-PropertyValue -Object $setup -Name 'installedExecutableSha256' -Required)
    foreach ($executableSha in @($portableExecutableSha, $legacyExecutableSha, $installedExecutableSha)) {
        Assert-HexSha256 -Value $executableSha -Label 'Windows executable SHA-256'
    }
    if ($portableExecutableSha -cne $legacyExecutableSha -or $portableExecutableSha -cne $installedExecutableSha) {
        throw 'Windows canonical portable, legacy portable and installed executable bytes are not identical.'
    }
    $versionPattern = '^' + [regex]::Escape([string]$IdentityProjection.normalizedVersion) + '(?:[.+]|$)'
    foreach ($pe in @(
            (Get-PropertyValue -Object $portable -Name 'pe' -Required),
            (Get-PropertyValue -Object $legacy -Name 'pe' -Required),
            (Get-PropertyValue -Object $setup -Name 'installedPe' -Required))) {
        if ([string](Get-PropertyValue -Object $pe -Name 'machine' -Required) -cne 'Amd64' -or
            [string](Get-PropertyValue -Object $pe -Name 'productVersion' -Required) -cnotmatch $versionPattern -or
            [string](Get-PropertyValue -Object $pe -Name 'fileVersion' -Required) -cnotmatch $versionPattern) {
            throw 'Windows application PE metadata is not exact AMD64/version evidence.'
        }
    }
    $setupPe = Get-PropertyValue -Object $setup -Name 'pe' -Required
    if ([string](Get-PropertyValue -Object $setupPe -Name 'machine' -Required) -notin @('I386', 'Amd64') -or
        [string](Get-PropertyValue -Object $setupPe -Name 'productVersion' -Required) -cnotmatch $versionPattern -or
        [string](Get-PropertyValue -Object $setupPe -Name 'fileVersion' -Required) -cnotmatch $versionPattern) {
        throw 'Windows setup bootstrap PE metadata is not bound to the expected version.'
    }
    $installedLayout = Get-PropertyValue -Object $setup -Name 'installedLayout' -Required
    $installedExecutableRelativePath = [string](Get-PropertyValue -Object $installedLayout -Name 'executableRelativePath' -Required)
    if ([int](Get-PropertyValue -Object $installedLayout -Name 'pdbCount' -Required) -ne 0 -or
        [IO.Path]::GetFileName($installedExecutableRelativePath) -cne 'Unlimotion.Desktop.exe' -or
        [IO.Path]::IsPathRooted($installedExecutableRelativePath) -or
        $installedExecutableRelativePath -match '(^|[\\/])\.\.([\\/]|$)') {
        throw 'Windows installed layout evidence contains PDBs or an invalid application path.'
    }
    foreach ($signatureState in @([string]$portable.authenticode, [string]$setup.authenticode)) {
        if ($signatureState -notin @('NotSigned', 'Valid')) { throw "Windows native evidence contains invalid Authenticode state '$signatureState'." }
    }
    if ([string]$portable.smoke.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)" -or
        [string]$setup.smoke.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)" -or
        $setup.uninstallVerified -ne $true) { throw 'Windows native evidence lacks exact portable/setup launch or uninstall proof.' }
    Assert-SeededTaskStorageEvidence -Evidence $portable.smoke -Label 'Windows portable smoke evidence'
    Assert-SeededTaskStorageEvidence -Evidence $setup.smoke -Label 'Windows setup smoke evidence'
    $retry = Get-PropertyValue -Object $report -Name 'retry' -Required
    if ([string]$retry.classification -cne 'deterministic' -or [int]$retry.attempt -ne 1 -or [int]$retry.maxAttempts -ne 1) {
        throw 'Windows native evidence violates the deterministic retry contract.'
    }
    return New-NativeCell -Id 'windows-server-2022-x64' -Platform windows -Architecture x64 -OsName 'Windows Server' -OsVersion '2022' `
        -NativeMode 'setup-and-portable-install-launch-with-seeded-isolated-task-storage' -Metadata pass -Install pass -Launch pass -Signature stateRecorded `
        -NegativeControl notApplicable -DirectFuse notApplicable -EvidenceFile ([IO.Path]::GetFileName($Path)) `
        -EvidenceSha256 $InputEvidence.sha256 -CellAssetIds $assetIds
}

function Convert-MacNativeEvidence {
    param([Parameter(Mandatory)] [object]$InputEvidence, [Parameter(Mandatory)] [object]$IdentityProjection, [Parameter(Mandatory)] [hashtable]$ArtifactsByName, [Parameter(Mandatory)] [string]$Path)
    $report = $InputEvidence.report
    $architecture = [string](Get-PropertyValue -Object $report -Name 'architecture' -Required)
    if ([string]$report.kind -cne 'macos-native-validation-evidence' -or [string]$report.status -cne 'pass' -or
        [string]$report.platform -cne 'macos' -or $architecture -notin @('x64', 'arm64') -or $report.productionReady -ne $false) {
        throw 'macOS native evidence is not a passing Stage-3 report.'
    }
    Assert-RawIdentity -Report $report -IdentityProjection $IdentityProjection -Label 'macOS native evidence' `
        -Fields @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')
    $runner = Get-PropertyValue -Object $report -Name 'runner' -Required
    $swVers = [string](Get-PropertyValue -Object $runner -Name 'swVers' -Required)
    $uname = [string](Get-PropertyValue -Object $runner -Name 'uname' -Required)
    $expectedRunner = if ($architecture -ceq 'x64') { 'macos-15-intel' } else { 'macos-15' }
    if ([string](Get-PropertyValue -Object $runner -Name 'expectedRunner' -Required) -cne $expectedRunner -or
        [string](Get-PropertyValue -Object $runner -Name 'imageOs' -Required) -cnotmatch '^macos15' -or
        [string]::IsNullOrWhiteSpace([string](Get-PropertyValue -Object $runner -Name 'imageVersion' -Required)) -or
        $swVers -cnotmatch 'ProductVersion:\s*15(?:\.|;|$)') { throw "macOS native evidence is not from the exact $expectedRunner runner: '$swVers'." }
    $machine = if ($architecture -ceq 'x64') { 'x86_64' } else { 'arm64' }
    if ($uname -cnotmatch "\b$machine\b") { throw "macOS native evidence uname does not prove '$machine'." }
    $assetIds = [Collections.Generic.List[string]]::new()
    foreach ($artifact in @($report.artifacts)) {
        $assetIds.Add((Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName ([string]$artifact.fileName) -Sha256 ([string]$artifact.sha256) -Label "macOS $architecture report"))
    }
    $expectedAssetIds = @(
        "macos-$architecture-feed-json",
        "macos-$architecture-updater-package",
        "macos-$architecture-setup",
        "macos-$architecture-portable",
        "macos-$architecture-pkg-legacy"
    )
    $actualAssetIds = @($assetIds | Sort-Object -CaseSensitive)
    $sortedExpectedAssetIds = @($expectedAssetIds | Sort-Object -CaseSensitive)
    if ($assetIds.Count -ne 5 -or ($actualAssetIds -join "`n") -cne ($sortedExpectedAssetIds -join "`n")) {
        throw "macOS $architecture native evidence must cover the exact five platform assets once each."
    }
    foreach ($metadata in @($report.portable.metadata, $report.setup.metadata, $report.setup.installedMetadata, $report.legacyPackage.metadata)) {
        Assert-HexSha256 -Value ([string](Get-PropertyValue -Object $metadata -Name 'binarySha256' -Required)) -Label "macOS $architecture executable SHA-256"
        if ([string]$metadata.bundleId -cne 'com.Unlimotion' -or [string]$metadata.version -cne $IdentityProjection.normalizedVersion -or
            [string]$metadata.executable -cne 'Unlimotion.Desktop.ForMacBuild' -or
            [string]$metadata.architecture -cne $machine -or [string]$metadata.minimumOs -notin @('12.0', '12.0.0') -or
            [string]$metadata.codesignState -notin @('unsigned', 'adhoc', 'valid')) { throw "macOS $architecture metadata/signature state is invalid." }
    }
    if ([string]$report.portable.metadata.binarySha256 -cne [string]$report.setup.metadata.binarySha256 -or
        [string]$report.portable.metadata.binarySha256 -cne [string]$report.setup.installedMetadata.binarySha256) {
        throw "macOS $architecture portable, setup payload and installed executable bytes are not identical."
    }
    foreach ($packageState in @([string]$report.setup.packageSignatureState, [string]$report.legacyPackage.packageSignatureState)) {
        if ($packageState -notin @('unsigned', 'valid')) { throw "macOS $architecture package signature state '$packageState' is invalid." }
    }
    foreach ($packageMetadata in @($report.setup.packageMetadata, $report.legacyPackage.packageMetadata)) {
        if ([string]$packageMetadata.version -cne $IdentityProjection.normalizedVersion -or [string]$packageMetadata.installLocation -cne '/Applications') {
            throw "macOS $architecture package metadata is not bound to the normalized installer contract."
        }
    }
    $install = Get-PropertyValue -Object $report.setup -Name 'install' -Required
    if ([string](Get-PropertyValue -Object $install -Name 'status' -Required) -cne 'pass' -or
        [string](Get-PropertyValue -Object $install -Name 'target' -Required) -cne '/' -or
        [string](Get-PropertyValue -Object $install -Name 'appPath' -Required) -cne '/Applications/Unlimotion.app' -or
        [string](Get-PropertyValue -Object $install -Name 'receiptIdentifier' -Required) -cne [string]$report.setup.packageMetadata.identifier -or
        [string]::IsNullOrWhiteSpace([string](Get-PropertyValue -Object $install -Name 'receipt' -Required)) -or
        [string]::IsNullOrWhiteSpace([string](Get-PropertyValue -Object $install -Name 'installerLog' -Required))) {
        throw "macOS $architecture evidence lacks an exact canonical package install and receipt."
    }
    if ([string]$report.portable.smoke.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)" -or
        [string]$report.setup.smoke.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)") { throw "macOS $architecture launch evidence is incomplete." }
    Assert-SeededTaskStorageEvidence -Evidence $report.portable.smoke -Label "macOS $architecture portable smoke evidence"
    Assert-SeededTaskStorageEvidence -Evidence $report.setup.smoke -Label "macOS $architecture setup smoke evidence"
    $retry = Get-PropertyValue -Object $report -Name 'retry' -Required
    if ([string]$retry.classification -cne 'deterministic' -or [int]$retry.attempt -ne 1 -or [int]$retry.maxAttempts -ne 1) {
        throw "macOS $architecture native evidence violates the deterministic retry contract."
    }
    return New-NativeCell -Id "macos-15-$architecture" -Platform macos -Architecture $architecture -OsName macOS -OsVersion 15 `
        -NativeMode 'package-and-portable-native-launch-with-seeded-isolated-task-storage' -Metadata pass -Install pass -Launch pass -Signature stateRecorded `
        -NegativeControl notApplicable -DirectFuse notApplicable -EvidenceFile ([IO.Path]::GetFileName($Path)) `
        -EvidenceSha256 $InputEvidence.sha256 -CellAssetIds @($assetIds)
}

function Convert-LinuxNativeEvidence {
    param([Parameter(Mandatory)] [object]$InputEvidence, [Parameter(Mandatory)] [object]$IdentityProjection, [Parameter(Mandatory)] [hashtable]$ArtifactsById, [Parameter(Mandatory)] [string]$Path)
    $report = $InputEvidence.report
    if ([string]$report.kind -ceq 'linux-build-parity') {
        if ([string]$report.status -cne 'pass') { throw 'Linux build-parity evidence is not passing.' }
        Assert-RawIdentity -Report $report -IdentityProjection $IdentityProjection -Label 'Linux build-parity evidence' `
            -Fields @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')
        if ([string](Get-PropertyValue -Object $report -Name 'sourceCheck' -Required) -cne 'passed' -or
            [string](Get-PropertyValue -Object $report -Name 'runtimeIdentifier' -Required) -cne 'linux-x64' -or
            [int](Get-PropertyValue -Object $report -Name 'publishInvocationCount' -Required) -ne 1) {
            throw 'Linux build-parity evidence does not prove one attributed clean publish.'
        }
        foreach ($hashField in @('payloadManifestSha256', 'canonicalExecutableSha256', 'debExecutableSha256', 'appImageExecutableSha256')) {
            Assert-HexSha256 -Value ([string](Get-PropertyValue -Object $report -Name $hashField -Required)) -Label "Linux build $hashField"
        }
        if ([string]$report.canonicalExecutableSha256 -cne [string]$report.debExecutableSha256 -or
            [string]$report.canonicalExecutableSha256 -cne [string]$report.appImageExecutableSha256) {
            throw 'Linux build-parity evidence does not prove canonical/deb/AppImage executable equality.'
        }
        $buildAssets = Get-PropertyValue -Object $report -Name 'artifacts' -Required
        $expectedBuildAssets = [ordered]@{
            deb = 'linux-deb-x64'
            appImage = 'linux-appimage-x64'
            updaterPackage = 'linux-updater-package-x64'
            updaterFeed = 'linux-feed-json'
        }
        foreach ($entry in $expectedBuildAssets.GetEnumerator()) {
            $expected = Get-ArtifactById -ArtifactsById $ArtifactsById -Id $entry.Value
            $actual = Get-PropertyValue -Object $buildAssets -Name $entry.Key -Required
            if ([string](Get-PropertyValue -Object $actual -Name 'fileName' -Required) -cne [string]$expected.fileName -or
                [long](Get-PropertyValue -Object $actual -Name 'size' -Required) -ne [long]$expected.size -or
                [string](Get-PropertyValue -Object $actual -Name 'sha256' -Required) -cne [string]$expected.sha256) {
                throw "Linux build-parity artifact '$($entry.Key)' differs from the final exact-byte envelope."
            }
        }
        return $null
    }
    if ([string]$report.kind -cne 'linux-native-evidence' -or [string]$report.status -cne 'pass') { throw 'Linux native evidence is not a passing report.' }
    Assert-RawIdentity -Report $report -IdentityProjection $IdentityProjection -Label 'Linux native evidence' `
        -Fields @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')
    if ($report.productionReady -ne $false) { throw 'Linux native evidence must remain non-productionReady.' }
    $mode = [string](Get-PropertyValue -Object $report -Name 'mode' -Required)
    if ($mode -ceq 'metadata') {
        if ([string]$report.retryRule -cne 'deterministic' -or [string]$report.retryClassification -cne 'never' -or
            [string]$report.retryCleanup -cne 'none' -or [int]$report.attempt -ne 1 -or [int]$report.maxAttempts -ne 1 -or
            $report.retryExhausted -ne $false) { throw 'Linux metadata evidence violates deterministic retry semantics.' }
        Assert-NotApplicableTaskStorageEvidence -Evidence $report -Label 'Linux metadata evidence'
        return $null
    }
    if ($mode -notin @('clean', 'upgrade', 'appimage', 'missing-runtime-negative')) { throw "Unknown Linux native mode '$mode'." }
    if ([string]$report.osName -cne 'debian' -or [string]$report.osVersion -notin @('12', '13') -or [string]$report.architecture -cne 'amd64') {
        throw 'Linux native evidence has the wrong OS version or architecture.'
    }
    if ([int]$report.attempt -lt 1 -or [int]$report.attempt -gt 3 -or [int]$report.maxAttempts -ne 3) {
        throw "Linux $mode native evidence violates the bounded APT retry contract."
    }
    if ([string]$report.retryRule -cne 'aptNetwork' -or [string]$report.retryClassification -cne 'infrastructure-only' -or
        [string]$report.retryCleanup -cne 'new-container' -or $report.retryExhausted -ne $false) {
        throw "Linux $mode native evidence lacks exact retry classification/cleanup state."
    }
    foreach ($imageField in @('targetImageIdentity', 'externalHarnessIdentity')) {
        if ([string](Get-PropertyValue -Object $report -Name $imageField -Required) -cnotmatch '(?:^|@)sha256:[0-9a-f]{64}$') {
            throw "Linux $mode native evidence lacks immutable $imageField."
        }
    }
    $harnessTools = [string](Get-PropertyValue -Object $report -Name 'externalHarnessTools' -Required)
    if ([string](Get-PropertyValue -Object $report -Name 'guiHarnessLocation' -Required) -cne 'external-sidecar' -or
        $harnessTools -notmatch 'xvfb=' -or $harnessTools -notmatch 'xdotool=') {
        throw "Linux $mode native evidence does not prove the external GUI harness contract."
    }
    foreach ($closureField in @('installedPackageClosureBeforeLaunch', 'installedPackageClosureAfterLaunch')) {
        Assert-HexSha256 -Value ([string](Get-PropertyValue -Object $report -Name $closureField -Required)) -Label "Linux $mode $closureField"
    }
    if ([string]$report.installedPackageClosureBeforeLaunch -cne [string]$report.installedPackageClosureAfterLaunch) {
        throw "Linux $mode report changed the target package closure during launch."
    }
    Assert-SeededTaskStorageEvidence -Evidence $report -Label "Linux $mode launch evidence"
    $applicationLogFile = [string](Get-PropertyValue -Object $report -Name 'applicationLogFile' -Required)
    $applicationLogSha = [string](Get-PropertyValue -Object $report -Name 'applicationLogSha256' -Required)
    Assert-SafeFileName -Value $applicationLogFile -Label "Linux $mode application log file"
    Assert-HexSha256 -Value $applicationLogSha -Label "Linux $mode application log SHA-256"
    $applicationLogPath = Join-Path ([IO.Path]::GetDirectoryName($Path)) $applicationLogFile
    if (-not (Test-Path -LiteralPath $applicationLogPath -PathType Leaf) -or (Get-LowerFileSha256 -Path $applicationLogPath) -cne $applicationLogSha) {
        throw "Linux $mode application log sidecar is missing or hash-mismatched."
    }
    if ($mode -cne 'appimage') {
        $closureLogFile = [string](Get-PropertyValue -Object $report -Name 'elfClosureLogFile' -Required)
        $closureLogSha = [string](Get-PropertyValue -Object $report -Name 'elfClosureLogSha256' -Required)
        Assert-SafeFileName -Value $closureLogFile -Label "Linux $mode ELF closure log file"
        Assert-HexSha256 -Value $closureLogSha -Label "Linux $mode ELF closure log SHA-256"
        $closureLogPath = Join-Path ([IO.Path]::GetDirectoryName($Path)) $closureLogFile
        if (-not (Test-Path -LiteralPath $closureLogPath -PathType Leaf) -or (Get-LowerFileSha256 -Path $closureLogPath) -cne $closureLogSha) {
            throw "Linux $mode ELF closure log sidecar is missing or hash-mismatched."
        }
    }
    $deb = Get-ArtifactById -ArtifactsById $ArtifactsById -Id 'linux-deb-x64'
    $appImage = Get-ArtifactById -ArtifactsById $ArtifactsById -Id 'linux-appimage-x64'
    if ([string]$report.debSha256 -cne [string]$deb.sha256) { throw "Linux $mode report Debian hash does not match exact bytes." }
    if ($mode -ceq 'appimage') {
        if ([string]$report.appImageSha256 -cne [string]$appImage.sha256 -or
            [string]$report.appImageExecutableSha256 -cne [string]$report.debExecutableSha256) { throw 'AppImage report hash or inner executable parity is invalid.' }
        if ($report.windowVerified -ne $true -or [string]$report.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)" -or
            [string]$report.launchMode -cne 'appimage-extract-and-run-with-seeded-isolated-task-storage' -or [string]$report.directFuse -cne 'notVerified') {
            throw 'AppImage report lacks extract-and-run proof or overstates direct FUSE.'
        }
        $expectedRuntimePackages = if ([string]$report.osVersion -ceq '12') {
            'ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 libstdc++6 tzdata zlib1g libx11-6 libice6 libsm6 libfontconfig1 libicu72 libssl3'
        } else {
            'ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 libstdc++6 tzdata zlib1g libx11-6 libice6 libsm6 libfontconfig1 libicu76 libssl3t64'
        }
        if ([string]$report.runtimePackages -cne $expectedRuntimePackages) { throw 'AppImage runtime prerequisites differ from the documented manifest contract.' }
    }
    elseif ($mode -ceq 'missing-runtime-negative') {
        if ($report.windowVerified -ne $false -or -not [string]::IsNullOrEmpty([string]$report.windowTitle) -or
            [string]$report.launchMode -cne 'negative-missing-runtime-external-x11-with-seeded-isolated-task-storage' -or
            [string]$report.elfClosureStatus -cne 'expectedFailure') {
            throw 'Missing-runtime negative must prove the expected loader/launch failure without a window.'
        }
    }
    else {
        if ($report.windowVerified -ne $true -or [string]$report.windowTitle -cne "Unlimotion $($IdentityProjection.normalizedVersion)" -or
            [string]$report.launchMode -cne 'debian-package-external-x11-with-seeded-isolated-task-storage') { throw "Linux $mode report lacks installed launch proof." }
        if ([string]$report.elfClosureStatus -cne 'pass') { throw "Linux $mode report lacks a passing ELF closure." }
    }
    if ($mode -ceq 'upgrade') {
        Assert-HexSha256 -Value ([string]$report.baselineSha256) -Label "Debian $($report.osVersion) upgrade baseline SHA-256"
    }
    $suffix = if ($mode -ceq 'missing-runtime-negative') { 'missing-runtime-negative' } else { $mode }
    return New-NativeCell -Id "debian-$($report.osVersion)-x64-$suffix" -Platform linux -Architecture x64 -OsName debian -OsVersion ([string]$report.osVersion) `
        -NativeMode "$mode-with-seeded-isolated-task-storage" -Metadata pass -Install $(if ($mode -ceq 'appimage') { 'extractPassed' } else { 'pass' }) `
        -Launch $(if ($mode -ceq 'missing-runtime-negative') { 'expectedFailureObserved' } else { 'pass' }) -Signature notApplicable `
        -NegativeControl $(if ($mode -ceq 'missing-runtime-negative') { 'pass' } else { 'notApplicable' }) `
        -DirectFuse $(if ($mode -ceq 'appimage') { 'notVerified' } else { 'notApplicable' }) `
        -EvidenceFile ([IO.Path]::GetFileName($Path)) -EvidenceSha256 $InputEvidence.sha256 `
        -CellAssetIds $(if ($mode -ceq 'appimage') { @('linux-deb-x64', 'linux-appimage-x64') } else { @('linux-deb-x64') })
}

function ConvertTo-AndroidCacheKeyPart {
    param([Parameter(Mandatory)] [string]$Value)
    $builder = [Text.StringBuilder]::new()
    $previousDash = $false
    foreach ($character in $Value.ToLowerInvariant().ToCharArray()) {
        $code = [int]$character
        $isAsciiAlphaNumeric = ($code -ge [int][char]'a' -and $code -le [int][char]'z') -or
            ($code -ge [int][char]'0' -and $code -le [int][char]'9')
        if ($isAsciiAlphaNumeric -or $character -in @('.', '_', '-')) {
            [void]$builder.Append($character)
            $previousDash = $character -eq '-'
        }
        elseif (-not $previousDash) {
            [void]$builder.Append('-')
            $previousDash = $true
        }
    }
    $normalized = $builder.ToString().Trim('-')
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "Android native cache-key component '$Value' normalizes to an empty value."
    }
    return $normalized
}

function Assert-AndroidNativeInputsDocument {
    param([Parameter(Mandatory)] [object]$Report)

    Assert-ExactPropertyNames -Object $Report -Expected @(
        'schemaVersion', 'androidApiLevel', 'ndkRevision', 'host', 'abis', 'sources',
        'nativePackageVersion', 'inputFileSha256'
    ) -Label 'Android native inputs'
    $schemaVersion = Get-StrictIntegerProperty -Object $Report -Name 'schemaVersion' -Label 'Android native inputs'
    $androidApiLevel = Get-StrictIntegerProperty -Object $Report -Name 'androidApiLevel' -Label 'Android native inputs'
    $ndkRevision = Get-StrictStringProperty -Object $Report -Name 'ndkRevision' -Label 'Android native inputs'
    $nativePackageVersion = Get-StrictStringProperty -Object $Report -Name 'nativePackageVersion' -Label 'Android native inputs'
    if ($schemaVersion -ne 1 -or $androidApiLevel -ne 23 -or
        $ndkRevision -cnotmatch '^[0-9]+(?:\.[0-9]+)+$' -or
        $nativePackageVersion -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-.][a-zA-Z0-9]+)*$') {
        throw 'Android native inputs contain an invalid schema/API/toolchain/package identity.'
    }
    $abis = Get-StrictArrayProperty -Object $Report -Name 'abis' -Label 'Android native inputs'
    if ($abis.Count -ne 2 -or @($abis | Where-Object { $_ -isnot [string] }).Count -ne 0 -or
        ($abis -join '|') -cne 'arm64-v8a|x86_64') {
        throw 'Android native inputs must contain exactly arm64-v8a and x86_64 in canonical order.'
    }

    $nativeHost = Get-PropertyValue -Object $Report -Name 'host' -Required
    Assert-ExactPropertyNames -Object $nativeHost -Expected @('os', 'arch', 'toolchainTriples') -Label 'Android native inputs host'
    $hostOs = Get-StrictStringProperty -Object $nativeHost -Name 'os' -Label 'Android native inputs host'
    $hostArch = Get-StrictStringProperty -Object $nativeHost -Name 'arch' -Label 'Android native inputs host'
    $toolchainTriples = Get-StrictArrayProperty -Object $nativeHost -Name 'toolchainTriples' -Label 'Android native inputs host'
    if ([string]::IsNullOrWhiteSpace($hostOs) -or [string]::IsNullOrWhiteSpace($hostArch) -or
        $toolchainTriples.Count -ne 2 -or @($toolchainTriples | Where-Object { $_ -isnot [string] }).Count -ne 0 -or
        ($toolchainTriples -join '|') -cne 'aarch64-linux-android|x86_64-linux-android') {
        throw 'Android native inputs host/toolchain identities are incomplete.'
    }

    $sources = Get-PropertyValue -Object $Report -Name 'sources' -Required
    Assert-ExactPropertyNames -Object $sources -Expected @('openssl', 'libssh2', 'libgit2Commit', 'upstreamNativePackage') -Label 'Android native inputs sources'
    if ((Get-StrictStringProperty -Object $sources -Name 'libgit2Commit' -Label 'Android native inputs sources') -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Android native inputs libgit2 commit is invalid.'
    }
    foreach ($sourceName in @('openssl', 'libssh2', 'upstreamNativePackage')) {
        $source = Get-PropertyValue -Object $sources -Name $sourceName -Required
        Assert-ExactPropertyNames -Object $source -Expected @('version', 'url', 'sha256') -Label "Android native input source '$sourceName'"
        $uri = $null
        $sourceVersion = Get-StrictStringProperty -Object $source -Name 'version' -Label "Android native input source '$sourceName'"
        $sourceUrl = Get-StrictStringProperty -Object $source -Name 'url' -Label "Android native input source '$sourceName'"
        $sourceSha256 = Get-StrictStringProperty -Object $source -Name 'sha256' -Label "Android native input source '$sourceName'"
        if ([string]::IsNullOrWhiteSpace($sourceVersion) -or
            -not [Uri]::TryCreate($sourceUrl, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -cne 'https') {
            throw "Android native input source '$sourceName' has an invalid version or HTTPS URL."
        }
        Assert-HexSha256 -Value $sourceSha256 -Label "Android native input source '$sourceName' SHA-256"
    }

    $inputHashes = Get-PropertyValue -Object $Report -Name 'inputFileSha256' -Required
    $expectedInputFiles = @(
        'scripts/android-native-common.sh',
        'scripts/build-openssl-android.sh',
        'scripts/build-libssh2-android.sh',
        'scripts/build-libgit2-android.sh',
        'scripts/pack-libgit2sharp-nativebinaries-android.sh',
        'scripts/build-android-distribution.sh',
        'src/Unlimotion.Android/Unlimotion.Android.csproj',
        'src/Directory.Packages.props',
        'src/nuget.config'
    )
    Assert-ExactPropertyNames -Object $inputHashes -Expected $expectedInputFiles -Label 'Android native input file hashes'
    foreach ($property in $inputHashes.PSObject.Properties) {
        if ($property.Value -isnot [string]) {
            throw "Android native input '$($property.Name)' SHA-256 must be a JSON string."
        }
        Assert-HexSha256 -Value $property.Value -Label "Android native input '$($property.Name)' SHA-256"
    }
}

function Assert-AndroidNativeProvenanceDocument {
    param([Parameter(Mandatory)] [object]$Report)

    Assert-ExactPropertyNames -Object $Report -Expected @(
        'schemaVersion', 'nativeInputDigest', 'requestedCacheKey', 'matchedCacheKey', 'inputs', 'outputs'
    ) -Label 'Android raw native provenance'
    $schemaVersion = Get-StrictIntegerProperty -Object $Report -Name 'schemaVersion' -Label 'Android raw native provenance'
    if ($schemaVersion -ne 1) {
        throw 'Android raw native provenance schemaVersion must be 1.'
    }
    $nativeInputDigest = Get-StrictStringProperty -Object $Report -Name 'nativeInputDigest' -Label 'Android raw native provenance'
    Assert-HexSha256 -Value $nativeInputDigest -Label 'Android raw native provenance input digest'
    $inputs = Get-PropertyValue -Object $Report -Name 'inputs' -Required
    Assert-AndroidNativeInputsDocument -Report $inputs
    $nativeHost = Get-PropertyValue -Object $inputs -Name 'host' -Required
    $expectedKey = 'android-native-v2-{0}-{1}-{2}' -f `
        (ConvertTo-AndroidCacheKeyPart -Value ([string]$nativeHost.os)), `
        (ConvertTo-AndroidCacheKeyPart -Value ([string]$nativeHost.arch)), `
        $nativeInputDigest
    $requestedKey = Get-StrictStringProperty -Object $Report -Name 'requestedCacheKey' -Label 'Android raw native provenance'
    $matchedKey = Get-StrictStringProperty -Object $Report -Name 'matchedCacheKey' -Label 'Android raw native provenance'
    if ($requestedKey -cne $expectedKey -or $matchedKey -cne $expectedKey) {
        throw 'Android raw native provenance is not bound to the exact host/input cache key.'
    }

    $outputs = Get-StrictArrayProperty -Object $Report -Name 'outputs' -Label 'Android raw native provenance'
    if ($outputs.Count -eq 0) { throw 'Android raw native provenance outputs must be non-empty.' }
    $seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $closureByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($output in $outputs) {
        Assert-ExactPropertyNames -Object $output -Expected @('path', 'size', 'sha256') -Label 'Android raw native provenance output'
        $relative = Get-StrictStringProperty -Object $output -Name 'path' -Label 'Android raw native provenance output'
        $segments = @($relative -split '/')
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative -cnotmatch '^[A-Za-z0-9._+-]+(?:/[A-Za-z0-9._+-]+)*$' -or
            $relative.StartsWith('/') -or $relative.Contains('\') -or
            $relative -match '[\x00-\x1f]' -or $segments -contains '.' -or $segments -contains '..' -or
            -not $seenPaths.Add($relative)) {
            throw "Android raw native provenance output path '$relative' is unsafe or duplicated."
        }
        $size = Get-StrictIntegerProperty -Object $output -Name 'size' -Label "Android raw native provenance output '$relative'"
        $sha256 = Get-StrictStringProperty -Object $output -Name 'sha256' -Label "Android raw native provenance output '$relative'"
        if ($size -le 0) { throw "Android raw native provenance output '$relative' is empty." }
        Assert-HexSha256 -Value $sha256 -Label "Android raw native provenance output '$relative' SHA-256"
        $closureByPath.Add($relative, [ordered]@{ path = $relative; sha256 = $sha256; size = $size })
    }
    $orderedPaths = [string[]]@($closureByPath.Keys)
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    $closureArray = @($orderedPaths | ForEach-Object { $closureByPath[$_] })
    $closureJson = ConvertTo-Json -InputObject $closureArray -Depth 10 -Compress
    $closureBytes = [Text.Encoding]::UTF8.GetBytes($closureJson)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($closureBytes)).ToLowerInvariant()
}

function Assert-AndroidNativeProvenanceClosure {
    param(
        [Parameter(Mandatory)] [object]$ArtifactEvidence,
        [Parameter(Mandatory)] [object]$SummaryEvidence,
        [Parameter(Mandatory)] [object]$NativeInputsEvidence,
        [Parameter(Mandatory)] [object]$NativeProvenanceEvidence
    )

    $artifactCache = Get-PropertyValue -Object $ArtifactEvidence.report -Name 'nativeCache' -Required
    Assert-ExactPropertyNames -Object $artifactCache -Expected @('nativeInputDigest', 'requestedKey', 'matchedKey', 'hit', 'saveRequired') -Label 'Android artifact native-cache summary'
    Assert-AndroidNativeInputsDocument -Report $NativeInputsEvidence.report
    $outputClosureSha256 = Assert-AndroidNativeProvenanceDocument -Report $NativeProvenanceEvidence.report

    $nativeInputsSha256 = [string]$NativeInputsEvidence.sha256
    $nativeProvenanceSha256 = [string]$NativeProvenanceEvidence.sha256
    Assert-HexSha256 -Value $nativeInputsSha256 -Label 'Downloaded Android native-inputs SHA-256'
    Assert-HexSha256 -Value $nativeProvenanceSha256 -Label 'Downloaded Android native-provenance SHA-256'
    $summary = $SummaryEvidence.report
    $summaryDigest = Get-StrictStringProperty -Object $summary -Name 'nativeInputDigest' -Label 'Android provenance summary'
    $summaryInputsSha = Get-StrictStringProperty -Object $summary -Name 'nativeInputsSha256' -Label 'Android provenance summary'
    $summaryProvenanceSha = Get-StrictStringProperty -Object $summary -Name 'nativeProvenanceSha256' -Label 'Android provenance summary'
    $summaryClosureSha = Get-StrictStringProperty -Object $summary -Name 'outputClosureSha256' -Label 'Android provenance summary'
    $summaryRequestedKey = Get-StrictStringProperty -Object $summary -Name 'requestedCacheKey' -Label 'Android provenance summary'
    $summaryMatchedKey = Get-StrictStringProperty -Object $summary -Name 'matchedCacheKey' -Label 'Android provenance summary'
    $summaryOutputCount = Get-StrictIntegerProperty -Object $summary -Name 'outputCount' -Label 'Android provenance summary'
    $rawProvenance = $NativeProvenanceEvidence.report
    $rawOutputs = Get-StrictArrayProperty -Object $rawProvenance -Name 'outputs' -Label 'Android raw native provenance'
    if ($summaryDigest -cne $nativeInputsSha256 -or $summaryInputsSha -cne $nativeInputsSha256 -or
        $summaryProvenanceSha -cne $nativeProvenanceSha256 -or $summaryClosureSha -cne $outputClosureSha256 -or
        $summaryOutputCount -ne $rawOutputs.Count -or
        [string]$rawProvenance.nativeInputDigest -cne $nativeInputsSha256 -or
        [string]$rawProvenance.requestedCacheKey -cne $summaryRequestedKey -or
        [string]$rawProvenance.matchedCacheKey -cne $summaryMatchedKey) {
        throw 'Android cache summary does not match the downloaded raw input/provenance bytes, cache key, or output closure.'
    }
    if (($NativeInputsEvidence.report | ConvertTo-Json -Depth 100 -Compress) -cne
        ($rawProvenance.inputs | ConvertTo-Json -Depth 100 -Compress)) {
        throw 'Android raw provenance inputs do not equal the downloaded native-inputs document.'
    }

    $artifactDigest = Get-StrictStringProperty -Object $artifactCache -Name 'nativeInputDigest' -Label 'Android artifact native-cache summary'
    $artifactRequestedKey = Get-StrictStringProperty -Object $artifactCache -Name 'requestedKey' -Label 'Android artifact native-cache summary'
    $artifactMatchedKey = Get-PropertyValue -Object $artifactCache -Name 'matchedKey' -Required
    if ($null -ne $artifactMatchedKey -and $artifactMatchedKey -isnot [string]) {
        throw "Android artifact native-cache summary property 'matchedKey' must be a JSON string or null."
    }
    $artifactHit = Get-StrictBooleanProperty -Object $artifactCache -Name 'hit' -Label 'Android artifact native-cache summary'
    $artifactSaveRequired = Get-StrictBooleanProperty -Object $artifactCache -Name 'saveRequired' -Label 'Android artifact native-cache summary'
    $summaryHit = Get-StrictBooleanProperty -Object $summary -Name 'cacheHit' -Label 'Android provenance summary'
    $summarySave = Get-StrictBooleanProperty -Object $summary -Name 'cacheSave' -Label 'Android provenance summary'
    if ($artifactDigest -cne $nativeInputsSha256 -or $artifactRequestedKey -cne $summaryRequestedKey -or
        $artifactHit -ne $summaryHit -or $artifactSaveRequired -ne $summarySave) {
        throw 'Android artifact native-cache summary does not match the validated provenance outcome.'
    }
    if (($artifactHit -eq $true -and ($artifactSaveRequired -ne $false -or [string]$artifactMatchedKey -cne $summaryRequestedKey)) -or
        ($artifactHit -eq $false -and ($artifactSaveRequired -ne $true -or $null -ne $artifactMatchedKey)) -or
        $summaryMatchedKey -cne $summaryRequestedKey) {
        throw 'Android artifact cache hit/miss/matched-key semantics are inconsistent with the validated provenance.'
    }
}

function Convert-AndroidNativeEvidence {
    param(
        [Parameter(Mandatory)] [object]$InputEvidence,
        [Parameter(Mandatory)] [object]$IdentityProjection,
        [Parameter(Mandatory)] [object]$IdentityObject,
        [Parameter(Mandatory)] [hashtable]$ArtifactsByName,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ExpectedArtifactTransportName,
        [Parameter(Mandatory)] [string]$ExpectedArtifactTransportId,
        [Parameter(Mandatory)] [string]$ExpectedArtifactTransportDigest
    )
    $report = $InputEvidence.report
    $fileName = [IO.Path]::GetFileName($Path)
    if ($fileName -ceq 'native-inputs.json') {
        Assert-AndroidNativeInputsDocument -Report $report
        return @()
    }
    if ($fileName -ceq 'native-provenance.json') {
        [void](Assert-AndroidNativeProvenanceDocument -Report $report)
        return @()
    }
    $kind = Get-StrictStringProperty -Object $report -Name 'kind' -Label 'Android native evidence'
    if ($kind -ceq 'distribution-download-transport') {
        $transportSchemaVersion = Get-StrictIntegerProperty -Object $report -Name 'schemaVersion' -Label 'Android download transport'
        $transportStatus = Get-StrictStringProperty -Object $report -Name 'status' -Label 'Android download transport'
        $transportProductionReady = Get-StrictBooleanProperty -Object $report -Name 'productionReady' -Label 'Android download transport'
        if ($transportSchemaVersion -ne 1 -or $transportStatus -cne 'pass' -or $transportProductionReady -ne $false) {
            throw 'Android download transport evidence is not a passing Stage-3 report.'
        }
        Assert-IdentityProjectionEquals -Expected $IdentityProjection -Actual (Get-PropertyValue -Object $report -Name 'identity' -Required) -Label 'Android download transport'
        $scope = Get-StrictStringProperty -Object $report -Name 'scope' -Label 'Android download transport'
        if ($scope -notin @('android-api23', 'android-api36')) {
            throw "Android download transport evidence has an invalid scope '$scope'."
        }
        if ([IO.Path]::GetFileName($Path) -cne "$scope-download-transport.json") {
            throw "Android download transport evidence filename is not bound to scope '$scope'."
        }
        $sourceArtifact = Get-PropertyValue -Object $report -Name 'sourceArtifact' -Required
        $sourceArtifactName = Get-StrictStringProperty -Object $sourceArtifact -Name 'name' -Label 'Android source artifact transport'
        $sourceArtifactId = Get-StrictStringProperty -Object $sourceArtifact -Name 'id' -Label 'Android source artifact transport'
        $sourceArtifactDigest = Get-StrictStringProperty -Object $sourceArtifact -Name 'digest' -Label 'Android source artifact transport'
        Assert-SafeFileName -Value $sourceArtifactName -Label 'Android source artifact transport name'
        if ($sourceArtifactId -cnotmatch '^[1-9][0-9]*$') {
            throw 'Android source artifact transport id must be a positive decimal value.'
        }
        Assert-HexSha256 -Value $sourceArtifactDigest -Label 'Android source artifact transport digest'
        if ($sourceArtifactName -cne $ExpectedArtifactTransportName -or
            $sourceArtifactId -cne $ExpectedArtifactTransportId -or
            $sourceArtifactDigest -cne $ExpectedArtifactTransportDigest) {
            throw 'Android download transport evidence is not bound to the exact producer artifact name/id/digest.'
        }
        $retry = Get-PropertyValue -Object $report -Name 'retry' -Required
        $firstOutcome = Get-StrictStringProperty -Object $retry -Name 'firstOutcome' -Label 'Android download transport retry'
        $secondOutcome = Get-StrictStringProperty -Object $retry -Name 'secondOutcome' -Label 'Android download transport retry'
        $selectedAttempt = Get-StrictIntegerProperty -Object $retry -Name 'selectedAttempt' -Label 'Android download transport retry'
        $cleanupBeforeAttempt2 = Get-StrictStringProperty -Object $retry -Name 'cleanupBeforeAttempt2' -Label 'Android download transport retry'
        $classification = Get-StrictStringProperty -Object $retry -Name 'classification' -Label 'Android download transport retry'
        $retryRule = Get-StrictStringProperty -Object $retry -Name 'rule' -Label 'Android download transport retry'
        $maxAttempts = Get-StrictIntegerProperty -Object $retry -Name 'maxAttempts' -Label 'Android download transport retry'
        $retryExhausted = Get-StrictBooleanProperty -Object $retry -Name 'exhausted' -Label 'Android download transport retry'
        if ($retryRule -cne 'bounded-clean-retry' -or
            $classification -notin @('none', 'transient-transport') -or
            $maxAttempts -ne 2 -or
            $firstOutcome -notin @('success', 'failure', 'skipped') -or
            $secondOutcome -notin @('success', 'failure', 'skipped') -or
            $selectedAttempt -notin @(1, 2) -or
            $retryExhausted -ne $false) {
            throw 'Android download transport evidence is outside the bounded clean-retry contract.'
        }
        if (($selectedAttempt -eq 1 -and ($classification -cne 'none' -or $firstOutcome -cne 'success' -or $secondOutcome -cne 'skipped' -or $cleanupBeforeAttempt2 -cne 'notRequired')) -or
            ($selectedAttempt -eq 2 -and ($classification -cne 'transient-transport' -or $firstOutcome -cne 'failure' -or $secondOutcome -cne 'success' -or $cleanupBeforeAttempt2 -cne 'completed'))) {
            throw 'Android download transport outcomes do not match the selected successful attempt and cleanup state.'
        }
        return @()
    }
    if ($kind -cne 'distribution-android-native-evidence') {
        throw "Unknown Android native evidence kind '$kind'."
    }
    $mode = Get-StrictStringProperty -Object $report -Name 'mode' -Label 'Android native evidence'
    $reportedProductionReady = Get-StrictBooleanProperty -Object $report -Name 'productionReady' -Label "Android $mode evidence"
    $nativeSchemaVersion = Get-StrictIntegerProperty -Object $report -Name 'schemaVersion' -Label "Android $mode evidence"
    $nativeOutcome = Get-StrictStringProperty -Object $report -Name 'outcome' -Label "Android $mode evidence"
    if ($nativeSchemaVersion -ne 1 -or $nativeOutcome -cne 'passed' -or
        $reportedProductionReady -ne $false) { throw "Android $mode evidence is not a passing Stage-3 report." }
    Assert-RawIdentity -Report $report -IdentityProjection $IdentityProjection -Label "Android $mode evidence" `
        -Fields @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256')
    if ((Get-StrictStringProperty -Object $report -Name 'signatureProfile' -Label "Android $mode evidence") -cne $IdentityProjection.signatureProfile) {
        throw "Android $mode evidence signature profile does not match the release identity."
    }
    $androidVersionCode = Get-StrictIntegerProperty -Object $IdentityObject -Name 'androidVersionCode' -Label 'Release identity'
    $androidVersionCodePolicy = Get-StrictStringProperty -Object $IdentityObject -Name 'androidVersionCodePolicy' -Label 'Release identity'
    if ((Get-StrictIntegerProperty -Object $report -Name 'androidVersionCode' -Label "Android $mode evidence") -ne $androidVersionCode -or
        (Get-StrictStringProperty -Object $report -Name 'androidVersionCodePolicy' -Label "Android $mode evidence") -cne $androidVersionCodePolicy) {
        throw "Android $mode evidence is not bound to the release identity version-code contract."
    }
    if ($mode -ceq 'provenance') {
        if ([IO.Path]::GetFileName($Path) -cne 'native-cache-evidence.json') {
            throw 'Android provenance evidence must use the exact native-cache-evidence.json sidecar name.'
        }
        $nativeInputDigest = Get-StrictStringProperty -Object $report -Name 'nativeInputDigest' -Label 'Android provenance evidence'
        $nativeInputsSha256 = Get-StrictStringProperty -Object $report -Name 'nativeInputsSha256' -Label 'Android provenance evidence'
        $nativeProvenanceSha256 = Get-StrictStringProperty -Object $report -Name 'nativeProvenanceSha256' -Label 'Android provenance evidence'
        $outputClosureSha256 = Get-StrictStringProperty -Object $report -Name 'outputClosureSha256' -Label 'Android provenance evidence'
        Assert-HexSha256 -Value $nativeInputDigest -Label 'Android native input digest'
        Assert-HexSha256 -Value $nativeInputsSha256 -Label 'Android native inputs SHA-256'
        Assert-HexSha256 -Value $nativeProvenanceSha256 -Label 'Android native provenance SHA-256'
        Assert-HexSha256 -Value $outputClosureSha256 -Label 'Android native output closure SHA-256'
        $requestedKey = Get-StrictStringProperty -Object $report -Name 'requestedCacheKey' -Label 'Android provenance evidence'
        $matchedKey = Get-StrictStringProperty -Object $report -Name 'matchedCacheKey' -Label 'Android provenance evidence'
        $provenanceApi = Get-StrictIntegerProperty -Object $report -Name 'androidApiLevel' -Label 'Android provenance evidence'
        $outputCount = Get-StrictIntegerProperty -Object $report -Name 'outputCount' -Label 'Android provenance evidence'
        $escapedDigest = [regex]::Escape($nativeInputDigest)
        if ($nativeInputsSha256 -cne $nativeInputDigest -or
            $requestedKey -cne $matchedKey -or
            $requestedKey -cnotmatch "^android-native-v2-[a-z0-9._-]+-[a-z0-9._-]+-$escapedDigest$" -or
            $provenanceApi -ne 23 -or $outputCount -le 0) {
            throw 'Android provenance evidence is not bound to the exact API-23 cache key and output closure.'
        }
        $cacheHit = Get-StrictBooleanProperty -Object $report -Name 'cacheHit' -Label 'Android provenance evidence'
        $cacheSave = Get-StrictBooleanProperty -Object $report -Name 'cacheSave' -Label 'Android provenance evidence'
        if (($cacheHit -ne $true -or $cacheSave -ne $false) -and ($cacheHit -ne $false -or $cacheSave -ne $true)) {
            throw 'Android provenance evidence must prove either an exact cache hit or a validated cache save.'
        }
        return @()
    }
    if ($mode -ceq 'artifact') {
        if ([IO.Path]::GetFileName($Path) -cne 'android-artifact.json') {
            throw 'Android artifact evidence must use the exact android-artifact.json sidecar name.'
        }
        if ((Get-StrictStringProperty -Object $report -Name 'supportLevel' -Label 'Android artifact evidence') -cne 'metadataVerified' -or
            (Get-StrictStringProperty -Object $report -Name 'signatureProfile' -Label 'Android artifact evidence') -cne $IdentityProjection.signatureProfile) {
            throw 'Android artifact evidence has the wrong support or signature profile.'
        }
        $assets = Get-StrictArrayProperty -Object $report -Name 'assets' -Label 'Android artifact evidence'
        if ($assets.Count -ne 2) { throw 'Android artifact evidence must cover exactly two APKs.' }
        $cells = [Collections.Generic.List[object]]::new()
        $seenArchitectures = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $productionSigner = $null
        foreach ($asset in $assets) {
            $assetArchitecture = Get-StrictStringProperty -Object $asset -Name 'architecture' -Label 'Android artifact asset'
            $architecture = switch ($assetArchitecture) { 'arm64-v8a' { 'arm64' } 'x86_64' { 'x64' } default { throw "Unknown Android ABI '$assetArchitecture'." } }
            if (-not $seenArchitectures.Add($architecture)) { throw "Android artifact evidence duplicates architecture '$architecture'." }
            $assetName = Get-StrictStringProperty -Object $asset -Name 'name' -Label "Android $architecture metadata"
            $sha256Before = Get-StrictStringProperty -Object $asset -Name 'sha256Before' -Label "Android $architecture metadata"
            $sha256After = Get-StrictStringProperty -Object $asset -Name 'sha256After' -Label "Android $architecture metadata"
            $assetId = Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName $assetName -Sha256 $sha256Before -Label "Android $architecture metadata"
            if ($sha256After -cne $sha256Before) { throw "Android $architecture APK changed during native validation." }
            $expectedId = "android-$architecture-apk"
            if ($assetId -cne $expectedId) { throw "Android $architecture metadata mapped to '$assetId', expected '$expectedId'." }
            $expectedRid = "android-$architecture"
            $fingerprint = Get-StrictStringProperty -Object $asset -Name 'signatureFingerprintSha256' -Label "Android $architecture metadata"
            Assert-HexSha256 -Value $fingerprint -Label "Android $architecture signing-certificate fingerprint"
            $signerCount = Get-StrictIntegerProperty -Object $asset -Name 'signerCount' -Label "Android $architecture metadata"
            $assetSize = Get-StrictIntegerProperty -Object $asset -Name 'size' -Label "Android $architecture metadata"
            $assetVersionCode = Get-StrictIntegerProperty -Object $asset -Name 'versionCode' -Label "Android $architecture metadata"
            $minSdk = Get-StrictIntegerProperty -Object $asset -Name 'minSdk' -Label "Android $architecture metadata"
            $targetSdk = Get-StrictIntegerProperty -Object $asset -Name 'targetSdk' -Label "Android $architecture metadata"
            $zipAligned = Get-StrictBooleanProperty -Object $asset -Name 'zipAligned' -Label "Android $architecture metadata"
            $nativeSymbolsVerified = Get-StrictBooleanProperty -Object $asset -Name 'nativeSymbolsVerified' -Label "Android $architecture metadata"
            if ((Get-StrictStringProperty -Object $asset -Name 'assetId' -Label "Android $architecture metadata") -cne $expectedId -or
                (Get-StrictStringProperty -Object $asset -Name 'rid' -Label "Android $architecture metadata") -cne $expectedRid -or
                $assetSize -le 0 -or
                (Get-StrictStringProperty -Object $asset -Name 'applicationId' -Label "Android $architecture metadata") -cne 'com.Kibnet.Unlimotion' -or
                (Get-StrictStringProperty -Object $asset -Name 'versionName' -Label "Android $architecture metadata") -cne $IdentityProjection.normalizedVersion -or
                $assetVersionCode -ne $androidVersionCode -or $minSdk -ne 23 -or $targetSdk -ne 36 -or
                (Get-StrictStringProperty -Object $asset -Name 'signatureProfile' -Label "Android $architecture metadata") -cne $IdentityProjection.signatureProfile -or
                $signerCount -lt 1 -or
                $zipAligned -ne $true -or $nativeSymbolsVerified -ne $true) {
                throw "Android $architecture APK metadata is not bound to the exact application/version/signature contract."
            }
            if ($IdentityProjection.signatureProfile -ceq 'production') {
                $signer = "$fingerprint/$signerCount"
                if ($null -eq $productionSigner) { $productionSigner = $signer }
                elseif ($productionSigner -cne $signer) { throw 'Production Android APKs do not use the same signer identity.' }
            }
            $cells.Add((New-NativeCell -Id "android-$architecture-apk-metadata" -Platform android -Architecture $architecture -OsName android -OsVersion notApplicable `
                -NativeMode 'apk-metadata' -Metadata pass -Install notApplicable -Launch notApplicable -Signature stateRecorded `
                -NegativeControl notApplicable -DirectFuse notApplicable -EvidenceFile ([IO.Path]::GetFileName($Path)) `
                -EvidenceSha256 $InputEvidence.sha256 -CellAssetIds @($expectedId)))
        }
        $arm64LaunchVerified = Get-StrictBooleanProperty -Object $report -Name 'arm64LaunchVerified' -Label 'Android artifact evidence'
        $arm64LaunchReason = Get-StrictStringProperty -Object $report -Name 'arm64LaunchReason' -Label 'Android artifact evidence'
        if ($seenArchitectures.Count -ne 2 -or $arm64LaunchVerified -ne $false -or
            [string]::IsNullOrWhiteSpace($arm64LaunchReason)) {
            throw 'Android artifact evidence must explicitly record arm64 launch as not verified.'
        }
        return @($cells)
    }
    if ($mode -ceq 'emulator') {
        if ((Get-StrictStringProperty -Object $report -Name 'supportLevel' -Label 'Android emulator evidence') -cne 'launchVerified') {
            throw 'Android emulator evidence has the wrong support level.'
        }
        $asset = Get-PropertyValue -Object $report -Name 'asset' -Required
        if ((Get-StrictStringProperty -Object $asset -Name 'architecture' -Label 'Android emulator asset') -cne 'x86_64') {
            throw 'Android emulator evidence is not x64.'
        }
        $assetName = Get-StrictStringProperty -Object $asset -Name 'name' -Label 'Android emulator asset'
        $assetSha256Before = Get-StrictStringProperty -Object $asset -Name 'sha256Before' -Label 'Android emulator asset'
        $assetSha256After = Get-StrictStringProperty -Object $asset -Name 'sha256After' -Label 'Android emulator asset'
        $assetId = Assert-RawArtifactHash -ArtifactsByName $ArtifactsByName -FileName $assetName -Sha256 $assetSha256Before -Label 'Android emulator'
        if ($assetId -cne 'android-x64-apk' -or $assetSha256After -cne $assetSha256Before) {
            throw 'Android emulator did not use unchanged x64 APK bytes.'
        }
        $runtime = Get-PropertyValue -Object $report -Name 'runtime' -Required
        $api = Get-StrictIntegerProperty -Object $runtime -Name 'apiLevel' -Label 'Android emulator runtime'
        if ([IO.Path]::GetFileName($Path) -cne "android-api$api-emulator.json") {
            throw "Android emulator evidence filename is not bound to API $api."
        }
        $fingerprint = Get-StrictStringProperty -Object $runtime -Name 'deviceFingerprint' -Label 'Android emulator runtime'
        $deviceSdk = Get-StrictIntegerProperty -Object $runtime -Name 'deviceSdk' -Label 'Android emulator runtime'
        $systemImagePackage = Get-StrictStringProperty -Object $runtime -Name 'systemImagePackage' -Label 'Android emulator runtime'
        $systemImageRevision = Get-StrictStringProperty -Object $runtime -Name 'systemImageRevision' -Label 'Android emulator runtime'
        $maxBootAttempts = Get-StrictIntegerProperty -Object $runtime -Name 'maxBootAttempts' -Label 'Android emulator runtime'
        $bootAttempts = Get-StrictIntegerProperty -Object $runtime -Name 'bootAttempts' -Label 'Android emulator runtime'
        $fatalLogcatEntries = Get-StrictIntegerProperty -Object $runtime -Name 'fatalLogcatEntries' -Label 'Android emulator runtime'
        $applicationId = Get-StrictStringProperty -Object $runtime -Name 'applicationId' -Label 'Android emulator runtime'
        $activity = Get-StrictStringProperty -Object $runtime -Name 'activity' -Label 'Android emulator runtime'
        $serial = Get-StrictStringProperty -Object $runtime -Name 'serial' -Label 'Android emulator runtime'
        $processId = Get-StrictStringProperty -Object $runtime -Name 'processId' -Label 'Android emulator runtime'
        if ($api -notin @(23, 36) -or $maxBootAttempts -ne 2 -or $bootAttempts -notin @(1, 2) -or
            $fatalLogcatEntries -ne 0 -or $applicationId -cne 'com.Kibnet.Unlimotion' -or
            [string]::IsNullOrWhiteSpace($activity) -or [string]::IsNullOrWhiteSpace($serial) -or
            [string]::IsNullOrWhiteSpace($processId) -or $deviceSdk -ne $api -or
            [string]::IsNullOrWhiteSpace($fingerprint) -or $systemImagePackage -cne "system-images;android-$api;google_apis;x86_64" -or
            $systemImageRevision -cnotmatch '^[0-9]+(?:\.[0-9]+){0,2}$') {
            throw 'Android emulator runtime evidence is incomplete or outside the exact retry contract.'
        }
        foreach ($logContract in @(
            [pscustomobject]@{ Property = 'logcat'; ExpectedFileName = "android-api$api-logcat.txt"; Label = 'logcat' },
            [pscustomobject]@{ Property = 'emulatorLog'; ExpectedFileName = "android-api$api-emulator.log"; Label = 'emulator log' }
        )) {
            $logReference = Get-PropertyValue -Object $runtime -Name $logContract.Property -Required
            Assert-ExactPropertyNames -Object $logReference -Expected @('fileName', 'sha256', 'bytes') -Label "Android $($logContract.Label) reference"
            $logFileName = Get-StrictStringProperty -Object $logReference -Name 'fileName' -Label "Android $($logContract.Label) reference"
            $logSha256 = Get-StrictStringProperty -Object $logReference -Name 'sha256' -Label "Android $($logContract.Label) reference"
            $logBytes = Get-StrictIntegerProperty -Object $logReference -Name 'bytes' -Label "Android $($logContract.Label) reference"
            Assert-SafeFileName -Value $logFileName -Label "Android $($logContract.Label) file name"
            Assert-HexSha256 -Value $logSha256 -Label "Android $($logContract.Label) SHA-256"
            if ($logFileName -cne $logContract.ExpectedFileName -or $logBytes -le 0) {
                throw "Android $($logContract.Label) reference does not identify the exact non-empty API-$api payload."
            }
        }
        $tools = Get-PropertyValue -Object $report -Name 'tools' -Required
        if ((Get-StrictStringProperty -Object $tools -Name 'emulatorVersion' -Label 'Android emulator tools') -cnotmatch '^Android emulator version ' -or
            (Get-StrictStringProperty -Object $tools -Name 'adbVersion' -Label 'Android emulator tools') -cnotmatch '^Android Debug Bridge version ' -or
            (Get-StrictStringProperty -Object $tools -Name 'aaptVersion' -Label 'Android emulator tools') -cnotmatch 'Android Asset Packaging Tool') {
            throw 'Android emulator evidence lacks exact emulator/adb/aapt tool identities.'
        }
        $runner = Get-PropertyValue -Object $report -Name 'runner' -Required
        foreach ($field in @('imageOs', 'imageVersion', 'uname')) {
            if ([string]::IsNullOrWhiteSpace((Get-StrictStringProperty -Object $runner -Name $field -Label 'Android emulator runner'))) {
                throw "Android emulator evidence runner field '$field' is empty."
            }
        }
        $recordedAtUtc = if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $jsonText = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
            $jsonDocument = [Text.Json.JsonDocument]::Parse([string]$jsonText)
            try {
                $jsonDocument.RootElement.GetProperty('recordedAtUtc').GetString()
            }
            finally {
                $jsonDocument.Dispose()
            }
        }
        else {
            Get-StrictStringProperty -Object $report -Name 'recordedAtUtc' -Label 'Android emulator evidence'
        }
        if ($recordedAtUtc -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') {
            throw 'Android emulator evidence recordedAtUtc is not a canonical UTC timestamp.'
        }
        $bootRetry = Get-PropertyValue -Object $report -Name 'bootRetry' -Required
        $retryAttempts = Get-StrictIntegerProperty -Object $bootRetry -Name 'attempts' -Label 'Android emulator boot retry'
        $outcomes = Get-StrictArrayProperty -Object $bootRetry -Name 'outcomes' -Label 'Android emulator boot retry'
        if (@($outcomes | Where-Object { $_ -isnot [string] }).Count -ne 0) {
            throw 'Android emulator boot retry outcomes must be JSON strings.'
        }
        $retryClassification = Get-StrictStringProperty -Object $bootRetry -Name 'classification' -Label 'Android emulator boot retry'
        $cleanupBeforeAttempt2 = Get-StrictStringProperty -Object $bootRetry -Name 'cleanupBeforeAttempt2' -Label 'Android emulator boot retry'
        $retryRule = Get-StrictStringProperty -Object $bootRetry -Name 'rule' -Label 'Android emulator boot retry'
        $retryMaxAttempts = Get-StrictIntegerProperty -Object $bootRetry -Name 'maxAttempts' -Label 'Android emulator boot retry'
        $retryExhausted = Get-StrictBooleanProperty -Object $bootRetry -Name 'exhausted' -Label 'Android emulator boot retry'
        if ($retryRule -cne 'bounded-clean-retry' -or $retryMaxAttempts -ne 2 -or $retryExhausted -ne $false -or
            $retryAttempts -ne $bootAttempts -or $outcomes.Count -ne $retryAttempts) {
            throw 'Android emulator evidence does not prove the bounded clean boot-retry contract.'
        }
        if (($retryAttempts -eq 1 -and ($retryClassification -cne 'none' -or $cleanupBeforeAttempt2 -cne 'notRequired' -or [string]$outcomes[0] -cne 'success')) -or
            ($retryAttempts -eq 2 -and ($retryClassification -cne 'transient-emulator-boot' -or $cleanupBeforeAttempt2 -cne 'kill-delete-avd-remove-files-and-wipe-data' -or
                [string]$outcomes[0] -cne 'failure' -or [string]$outcomes[1] -cne 'success')) -or
            $retryAttempts -notin @(1, 2)) {
            throw 'Android emulator boot outcomes do not match the exact successful attempt and cleanup state.'
        }
        return @((New-NativeCell -Id "android-api-$api-x64-emulator" -Platform android -Architecture x64 -OsName android -OsVersion "API $api" `
            -NativeMode emulator -Metadata pass -Install pass -Launch pass -Signature coveredByArtifactCell `
            -NegativeControl notApplicable -DirectFuse notApplicable -EvidenceFile ([IO.Path]::GetFileName($Path)) `
            -EvidenceSha256 $InputEvidence.sha256 -CellAssetIds @('android-x64-apk')))
    }
    throw "Unknown Android native evidence mode '$mode'."
}

if ([string]::IsNullOrWhiteSpace($Manifest)) {
    $Manifest = Join-Path $repoRoot 'distribution/release-assets.json'
}
$Manifest = Resolve-RequiredPath -Path $Manifest -Label 'Distribution manifest'
if ([string]::IsNullOrWhiteSpace($SupportMatrix)) {
    $SupportMatrix = Join-Path $repoRoot 'distribution/support-matrix.json'
}
$SupportMatrix = Resolve-RequiredPath -Path $SupportMatrix -Label 'Distribution support matrix'
if ([string]::IsNullOrWhiteSpace($EvidenceSchema)) {
    $EvidenceSchema = Join-Path $repoRoot 'distribution/evidence.schema.json'
}
$EvidenceSchema = Resolve-RequiredPath -Path $EvidenceSchema -Label 'Distribution evidence schema'
$manifestObject = Read-JsonFile -Path $Manifest -Label 'Distribution manifest'

switch ($Mode) {
    'Record' {
        if ([string]::IsNullOrWhiteSpace($Identity) -or
            [string]::IsNullOrWhiteSpace($ArtifactDirectory) -or
            [string]::IsNullOrWhiteSpace($Evidence) -or
            [string]::IsNullOrWhiteSpace($Platform) -or
            [string]::IsNullOrWhiteSpace($Architecture) -or
            $null -eq $AssetId -or $AssetId.Count -eq 0) {
            throw 'Record mode requires -Identity, -ArtifactDirectory, -Evidence, -Platform, -Architecture and at least one -AssetId.'
        }

        $identityObject = Read-JsonFile -Path $Identity -Label 'Release identity'
        $identityProjection = Assert-IdentityContract -IdentityObject $identityObject
        $artifactRoot = Resolve-RequiredPath -Path $ArtifactDirectory -Label 'Artifact directory' -Container
        if ($Platform -notin @('windows', 'linux', 'macos', 'android') -or $Architecture -notin @('x64', 'arm64')) {
            throw 'Record mode requires a canonical platform and architecture.'
        }

        $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $artifactRecords = foreach ($id in $AssetId) {
            if (-not $seenIds.Add($id)) {
                throw "Duplicate requested asset id '$id'."
            }
            $asset = Get-ManifestAsset -ManifestObject $manifestObject -Id $id
            if ($asset.platform -cne $Platform -or $asset.architecture -cne $Architecture) {
                throw "Asset '$id' is $($asset.platform)/$($asset.architecture), not $Platform/$Architecture."
            }
            $fileName = Get-PlannedFileName -IdentityObject $identityObject -Id $id
            if (-not $seenNames.Add($fileName)) {
                throw "Duplicate planned file name '$fileName'."
            }
            if ($identityObject.rawTag -cmatch '^v' -and $fileName.Contains([string]$identityObject.rawTag, [StringComparison]::Ordinal)) {
                throw "Artifact '$fileName' contains raw tag '$($identityObject.rawTag)'."
            }
            $filePath = Join-Path $artifactRoot $fileName
            $resolvedFile = Resolve-RequiredPath -Path $filePath -Label "Artifact '$id'"
            $item = Get-Item -LiteralPath $resolvedFile
            if ($item.Length -le 0) {
                throw "Artifact '$fileName' is empty."
            }
            [ordered]@{
                assetId = $id
                fileName = $fileName
                size = $item.Length
                sha256 = (Get-FileHash -LiteralPath $resolvedFile -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }

        $relationRecords = @(Test-ArtifactRelations -ManifestObject $manifestObject -ArtifactRecords @($artifactRecords) -ArtifactRoot $artifactRoot -NormalizedVersion ([string]$identityObject.normalizedVersion))
        if ($ArtifactTransportId -or $ArtifactTransportDigest -or $ArtifactTransportName -or $TransportArchivePath -or $UnixExecutablePath -or $UnixAssetId -or $ExpectedUnixMode) {
            throw 'Record mode is pre-upload evidence; transport inputs belong to TransportReceipt mode.'
        }

        $record = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-artifact-evidence'
            status = 'pass'
            platform = $Platform
            architecture = $Architecture
            rawTag = $identityProjection.rawTag
            normalizedVersion = $identityProjection.normalizedVersion
            sourceSha = $identityProjection.sourceSha
            workflowSha = $identityProjection.workflowSha
            tagBinding = $identityProjection.tagBinding
            manifestSha256 = $identityProjection.manifestSha256
            supportMatrixSha256 = $identityProjection.supportMatrixSha256
            identitySignatureProfile = $identityProjection.signatureProfile
            signatureProfile = $SignatureProfile
            productionReady = $false
            artifacts = @($artifactRecords)
            relations = $relationRecords
        }
        Assert-JsonSchema -Value $record -Label 'Recorded artifact evidence'
        Write-JsonFile -Value $record -Path $Evidence
        Write-Output "Recorded $($artifactRecords.Count) $Platform/$Architecture artifact(s) in $Evidence."
    }

    'Validate' {
        if ([string]::IsNullOrWhiteSpace($Evidence) -or [string]::IsNullOrWhiteSpace($ArtifactDirectory) -or [string]::IsNullOrWhiteSpace($Identity)) {
            throw 'Validate mode requires -Identity, -Evidence and -ArtifactDirectory.'
        }
        $identityObject = Read-JsonFile -Path $Identity -Label 'Release identity'
        $identityProjection = Assert-IdentityContract -IdentityObject $identityObject
        $artifactResult = Assert-ArtifactEvidence -Path $Evidence -IdentityProjection $identityProjection -ValidateBytes
        $evidenceObject = $artifactResult.report
        $providedRoot = Resolve-RequiredPath -Path $ArtifactDirectory -Label 'Artifact directory' -Container
        if ([IO.Path]::GetFullPath($providedRoot) -cne [IO.Path]::GetFullPath($artifactResult.assetRoot)) {
            throw 'ArtifactDirectory does not match the evidence sibling assets directory.'
        }
        if ($Platform -and [string]$evidenceObject.platform -cne $Platform) { throw "Evidence platform '$($evidenceObject.platform)' does not match '$Platform'." }
        if ($Architecture -and [string]$evidenceObject.architecture -cne $Architecture) { throw "Evidence architecture '$($evidenceObject.architecture)' does not match '$Architecture'." }
        if ($ArtifactTransportId -or $ArtifactTransportDigest -or $ArtifactTransportName -or $ExpectedUnixMode) {
            throw 'Validate mode validates pre-upload evidence; transport inputs belong to TransportReceipt or MergePlatform mode.'
        }
        if ($AssetId.Count -gt 0) {
            $expectedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($id in $AssetId) {
                if (-not $expectedIds.Add($id)) { throw "Duplicate expected asset id '$id'." }
            }
            $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($artifact in @($evidenceObject.artifacts)) { [void]$seenIds.Add([string]$artifact.assetId) }
            $missing = @($expectedIds | Where-Object { -not $seenIds.Contains($_) })
            $unexpected = @($seenIds | Where-Object { -not $expectedIds.Contains($_) })
            if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
                throw "Evidence asset coverage mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
            }
        }
        Write-Output "Validated $(@($evidenceObject.artifacts).Count) artifact(s) against $Evidence."
    }

    'TransportReceipt' {
        if ([string]::IsNullOrWhiteSpace($Identity) -or [string]::IsNullOrWhiteSpace($Evidence) -or
            $EvidencePath.Count -eq 0 -or [string]::IsNullOrWhiteSpace($Platform) -or [string]::IsNullOrWhiteSpace($Architecture) -or
            [string]::IsNullOrWhiteSpace($ArtifactTransportName) -or [string]::IsNullOrWhiteSpace($ArtifactTransportId) -or
            [string]::IsNullOrWhiteSpace($ArtifactTransportDigest)) {
            throw 'TransportReceipt mode requires identity, pre-evidence path(s), output evidence, platform/architecture and upload name/id/digest.'
        }
        if ($Platform -notin @('windows', 'linux', 'macos', 'android') -or $Architecture -notin @('x64', 'arm64', 'multi')) {
            throw 'TransportReceipt mode has an invalid platform or architecture.'
        }
        Assert-SafeFileName -Value $ArtifactTransportName -Label 'Uploaded artifact name'
        if ($ArtifactTransportId -cnotmatch '^[1-9][0-9]*$') { throw 'Artifact transport id must be a positive decimal GitHub artifact id.' }
        $ArtifactTransportDigest = $ArtifactTransportDigest.ToLowerInvariant()
        Assert-HexSha256 -Value $ArtifactTransportDigest -Label 'Artifact transport digest'
        if ($ArtifactRetentionDays -ne 7) { throw 'Stage-3 artifact retention must be exactly seven days.' }

        $identityObject = Read-JsonFile -Path $Identity -Label 'Release identity'
        $identityProjection = Assert-IdentityContract -IdentityObject $identityObject
        $preEvidence = [Collections.Generic.List[object]]::new()
        $seenPreEvidence = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($path in $EvidencePath) {
            $result = Assert-ArtifactEvidence -Path $path -IdentityProjection $identityProjection
            $reference = $result.reference
            if (-not $seenPreEvidence.Add([string]$reference.evidenceId)) { throw "Duplicate pre-evidence id '$($reference.evidenceId)'." }
            if ([string]$result.report.platform -cne $Platform) { throw "Pre-evidence '$path' platform does not match '$Platform'." }
            $preEvidence.Add($reference)
        }
        $expectedArchitectures = if ($Architecture -ceq 'multi') { @('x64', 'arm64') } else { @($Architecture) }
        $actualArchitectures = @($preEvidence | ForEach-Object architecture | Sort-Object -Unique)
        if ((($expectedArchitectures | Sort-Object) -join '|') -cne (($actualArchitectures | Sort-Object) -join '|')) {
            throw "Pre-evidence architectures '$($actualArchitectures -join ',')' do not match receipt architecture '$Architecture'."
        }

        if ($Platform -ceq 'linux') {
            if ($Architecture -cne 'x64' -or [string]::IsNullOrWhiteSpace($TransportArchivePath) -or
                [string]::IsNullOrWhiteSpace($UnixExecutablePath) -or [string]::IsNullOrWhiteSpace($UnixAssetId) -or
                [string]::IsNullOrWhiteSpace($ExpectedUnixMode)) { throw 'Linux receipt requires x64 tar/original/asset/mode inputs.' }
            if ($UnixAssetId -cne 'linux-appimage-x64') { throw 'Linux Unix-mode receipt must bind linux-appimage-x64.' }
            $unixMode = Test-TarUnixMode -ArchivePath $TransportArchivePath -OriginalPath $UnixExecutablePath -AssetId $UnixAssetId -ExpectedMode $ExpectedUnixMode
            $planned = Get-PlannedFileName -IdentityObject $identityObject -Id $UnixAssetId
            if ([IO.Path]::GetFileName($UnixExecutablePath) -cne $planned) { throw "Unix executable name does not match '$UnixAssetId' plan." }
        }
        else {
            if ($TransportArchivePath -or $UnixExecutablePath -or $UnixAssetId -or $ExpectedUnixMode) { throw 'Non-Linux receipt must use explicit Unix-mode notApplicable fields.' }
            $unixMode = Get-NotApplicableUnixMode
        }
        $receipt = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-transport-receipt'
            status = 'pass'
            platform = $Platform
            architecture = $Architecture
            identity = $identityProjection
            artifactName = $ArtifactTransportName
            artifactId = $ArtifactTransportId
            artifactDigest = $ArtifactTransportDigest
            retentionDays = $ArtifactRetentionDays
            ifNoFilesFound = 'error'
            overwrite = $false
            preEvidence = @($preEvidence)
            unixMode = $unixMode
            productionReady = $false
        }
        Assert-JsonSchema -Value $receipt -Label 'Transport receipt'
        Write-JsonFile -Value $receipt -Path $Evidence
        Write-Output "Recorded post-upload transport receipt for '$ArtifactTransportName' in $Evidence."
    }

    'MergePlatform' {
        if ([string]::IsNullOrWhiteSpace($Identity) -or [string]::IsNullOrWhiteSpace($Evidence) -or
            $EvidencePath.Count -eq 0 -or $NativeEvidencePath.Count -eq 0 -or
            [string]::IsNullOrWhiteSpace($TransportReceiptPath) -or [string]::IsNullOrWhiteSpace($Platform) -or
            [string]::IsNullOrWhiteSpace($Architecture) -or [string]::IsNullOrWhiteSpace($ArtifactTransportName) -or
            [string]::IsNullOrWhiteSpace($ArtifactTransportId) -or [string]::IsNullOrWhiteSpace($ArtifactTransportDigest)) {
            throw 'MergePlatform mode requires identity, artifact/native evidence, receipt, expected upload name/id/digest, platform/architecture and output evidence.'
        }
        $identityObject = Read-JsonFile -Path $Identity -Label 'Release identity'
        $identityProjection = Assert-IdentityContract -IdentityObject $identityObject
        $expectedKey = "$Platform/$Architecture"
        if ($expectedKey -notin @('windows/x64', 'linux/x64', 'macos/x64', 'macos/arm64', 'android/multi')) {
            throw "Unsupported Stage-3 platform envelope '$expectedKey'."
        }

        $artifactResults = [Collections.Generic.List[object]]::new()
        $artifactRecords = [Collections.Generic.List[object]]::new()
        $relationRecords = [Collections.Generic.List[object]]::new()
        $artifactsById = @{}
        $artifactsByName = @{}
        foreach ($path in $EvidencePath) {
            $result = Assert-ArtifactEvidence -Path $path -IdentityProjection $identityProjection -ValidateBytes
            if ([string]$result.report.platform -cne $Platform) { throw "Artifact evidence '$path' has the wrong platform." }
            $artifactResults.Add($result)
            foreach ($artifact in @($result.report.artifacts)) {
                if ($artifactsById.ContainsKey([string]$artifact.assetId)) { throw "Duplicate platform asset '$($artifact.assetId)'." }
                if ($artifactsByName.ContainsKey([string]$artifact.fileName)) { throw "Duplicate platform file '$($artifact.fileName)'." }
                $artifactsById[[string]$artifact.assetId] = $artifact
                $artifactsByName[[string]$artifact.fileName] = $artifact
                $artifactRecords.Add($artifact)
            }
            foreach ($relation in @($result.report.relations)) { $relationRecords.Add($relation) }
        }
        $expectedArtifactArchitectures = if ($Architecture -ceq 'multi') { @('arm64', 'x64') } else { @($Architecture) }
        $actualArtifactArchitectures = @($artifactResults | ForEach-Object { [string]$_.report.architecture } | Sort-Object -Unique)
        if ((($expectedArtifactArchitectures | Sort-Object) -join '|') -cne (($actualArtifactArchitectures | Sort-Object) -join '|')) {
            throw "Artifact evidence architectures do not match '$expectedKey'."
        }

        $receiptResolved = Resolve-RequiredPath -Path $TransportReceiptPath -Label 'Transport receipt'
        $receipt = Read-JsonFile -Path $receiptResolved -Label 'Transport receipt'
        Assert-JsonSchema -Value $receipt -Label 'Transport receipt'
        if ([string]$receipt.kind -cne 'distribution-transport-receipt' -or [string]$receipt.status -cne 'pass' -or
            [string]$receipt.platform -cne $Platform -or [string]$receipt.architecture -cne $Architecture -or $receipt.productionReady -ne $false) {
            throw 'Transport receipt does not match the platform envelope.'
        }
        Assert-IdentityProjectionEquals -Expected $identityProjection -Actual $receipt.identity -Label 'Transport receipt'
        Assert-SafeFileName -Value $ArtifactTransportName -Label 'Expected uploaded artifact name'
        if ($ArtifactTransportId -cnotmatch '^[1-9][0-9]*$') { throw 'Expected artifact transport id must be a positive decimal value.' }
        $ArtifactTransportDigest = $ArtifactTransportDigest.ToLowerInvariant()
        Assert-HexSha256 -Value $ArtifactTransportDigest -Label 'Expected artifact transport digest'
        if ([string]$receipt.artifactName -cne $ArtifactTransportName -or [string]$receipt.artifactId -cne $ArtifactTransportId -or
            [string]$receipt.artifactDigest -cne $ArtifactTransportDigest) {
            throw 'Transport receipt name/id/digest does not match the final job producer outputs.'
        }
        if ([int]$receipt.retentionDays -ne 7 -or [string]$receipt.ifNoFilesFound -cne 'error' -or $receipt.overwrite -ne $false) {
            throw 'Transport receipt does not prove seven-day, no-overwrite, fail-on-missing upload semantics.'
        }
        $expectedPre = @($artifactResults | ForEach-Object { $_.reference } | Sort-Object evidenceId)
        $actualPre = @($receipt.preEvidence | Sort-Object evidenceId)
        if (($expectedPre | ConvertTo-Json -Depth 10 -Compress) -cne ($actualPre | ConvertTo-Json -Depth 10 -Compress)) {
            throw 'Transport receipt pre-evidence SHA binding does not match exact producer evidence.'
        }
        if ($Platform -ceq 'linux') {
            if ([string]::IsNullOrWhiteSpace($TransportArchivePath)) { throw 'Linux MergePlatform requires the downloaded transport archive.' }
            $appImage = Get-ArtifactById -ArtifactsById $artifactsById -Id 'linux-appimage-x64'
            $appImageRoot = @($artifactResults | Where-Object { $_.report.architecture -ceq 'x64' })[0].assetRoot
            $recheckedMode = Test-TarUnixMode -ArchivePath $TransportArchivePath -OriginalPath (Join-Path $appImageRoot $appImage.fileName) -AssetId linux-appimage-x64 -ExpectedMode 0755
            if (($recheckedMode | ConvertTo-Json -Depth 10 -Compress) -cne ($receipt.unixMode | ConvertTo-Json -Depth 10 -Compress)) {
                throw 'Downloaded Linux archive Unix-mode/byte evidence differs from the post-upload receipt.'
            }
        }
        elseif ([string]$receipt.unixMode.applicability -cne 'notApplicable' -or $TransportArchivePath) {
            throw 'Non-Linux platform transport must have explicit Unix-mode N/A and no archive input.'
        }

        $nativeInputs = [Collections.Generic.List[object]]::new()
        $nativeReferences = [Collections.Generic.List[object]]::new()
        $nativeFileNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($path in $NativeEvidencePath) {
            $resolved = Resolve-RequiredPath -Path $path -Label 'Native evidence'
            $inputEvidence = [ordered]@{
                report = Read-JsonFile -Path $resolved -Label 'Native evidence'
                path = $resolved
                sha256 = Get-LowerFileSha256 -Path $resolved
            }
            $reference = New-NativeEvidenceReference -InputEvidence $inputEvidence
            if (-not $nativeFileNames.Add([string]$reference.fileName)) {
                throw "Native evidence file '$($reference.fileName)' is duplicated in the platform envelope."
            }
            $nativeInputs.Add($inputEvidence)
            $nativeReferences.Add($reference)
        }
        $nativeCells = [Collections.Generic.List[object]]::new()
        switch ($Platform) {
            'windows' {
                if ($nativeInputs.Count -ne 1) { throw 'Windows envelope requires exactly one native report.' }
                $nativeCells.Add((Convert-WindowsNativeEvidence -InputEvidence $nativeInputs[0] -IdentityProjection $identityProjection -ArtifactsByName $artifactsByName -Path $nativeInputs[0].path))
            }
            'macos' {
                if ($nativeInputs.Count -ne 1) { throw 'Each macOS envelope requires exactly one native report.' }
                $nativeCells.Add((Convert-MacNativeEvidence -InputEvidence $nativeInputs[0] -IdentityProjection $identityProjection -ArtifactsByName $artifactsByName -Path $nativeInputs[0].path))
            }
            'linux' {
                foreach ($input in $nativeInputs) {
                    $cell = Convert-LinuxNativeEvidence -InputEvidence $input -IdentityProjection $identityProjection -ArtifactsById $artifactsById -Path $input.path
                    if ($null -ne $cell) { $nativeCells.Add($cell) }
                }
            }
            'android' {
                $androidEvidenceCoverage = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $androidEvidenceByCoverage = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
                foreach ($input in $nativeInputs) {
                    $nativeFileName = [IO.Path]::GetFileName([string]$input.path)
                    $coverageKey = if ($nativeFileName -ceq 'native-inputs.json') {
                        'raw-inputs'
                    }
                    elseif ($nativeFileName -ceq 'native-provenance.json') {
                        'raw-provenance'
                    }
                    else {
                        $kind = [string](Get-PropertyValue -Object $input.report -Name 'kind' -Required)
                        if ($kind -ceq 'distribution-download-transport') {
                            "transport/$([string](Get-PropertyValue -Object $input.report -Name 'scope' -Required))"
                        }
                        else {
                            $nativeMode = [string](Get-PropertyValue -Object $input.report -Name 'mode' -Required)
                            if ($nativeMode -ceq 'emulator') {
                                "emulator/$([int](Get-PropertyValue -Object (Get-PropertyValue -Object $input.report -Name 'runtime' -Required) -Name 'apiLevel' -Required))"
                            }
                            else {
                                $nativeMode
                            }
                        }
                    }
                    if (-not $androidEvidenceCoverage.Add($coverageKey)) {
                        throw "Android native evidence duplicates coverage key '$coverageKey'."
                    }
                    $androidEvidenceByCoverage.Add($coverageKey, $input)
                    foreach ($cell in @(Convert-AndroidNativeEvidence `
                        -InputEvidence $input -IdentityProjection $identityProjection -IdentityObject $identityObject `
                        -ArtifactsByName $artifactsByName -Path $input.path `
                        -ExpectedArtifactTransportName $ArtifactTransportName `
                        -ExpectedArtifactTransportId $ArtifactTransportId `
                        -ExpectedArtifactTransportDigest $ArtifactTransportDigest)) {
                        if ($null -ne $cell) { $nativeCells.Add($cell) }
                    }
                }
                $expectedAndroidEvidenceCoverage = @(
                    'artifact', 'provenance', 'raw-inputs', 'raw-provenance', 'emulator/23', 'emulator/36',
                    'transport/android-api23', 'transport/android-api36'
                )
                if ((@($androidEvidenceCoverage | Sort-Object) -join '|') -cne (($expectedAndroidEvidenceCoverage | Sort-Object) -join '|')) {
                    throw "Android native evidence sidecar coverage mismatch. Expected: $($expectedAndroidEvidenceCoverage -join ', '); actual: $(@($androidEvidenceCoverage | Sort-Object) -join ', ')."
                }
                Assert-AndroidNativeProvenanceClosure `
                    -ArtifactEvidence $androidEvidenceByCoverage['artifact'] `
                    -SummaryEvidence $androidEvidenceByCoverage['provenance'] `
                    -NativeInputsEvidence $androidEvidenceByCoverage['raw-inputs'] `
                    -NativeProvenanceEvidence $androidEvidenceByCoverage['raw-provenance']
            }
        }

        $expectedCellIds = switch ($expectedKey) {
            'windows/x64' { @('windows-server-2022-x64') }
            'linux/x64' { @('debian-12-x64-clean', 'debian-12-x64-upgrade', 'debian-12-x64-appimage', 'debian-12-x64-missing-runtime-negative', 'debian-13-x64-clean', 'debian-13-x64-upgrade', 'debian-13-x64-appimage', 'debian-13-x64-missing-runtime-negative') }
            'macos/x64' { @('macos-15-x64') }
            'macos/arm64' { @('macos-15-arm64') }
            'android/multi' { @('android-arm64-apk-metadata', 'android-x64-apk-metadata', 'android-api-23-x64-emulator', 'android-api-36-x64-emulator') }
        }
        $actualCellIds = @($nativeCells | ForEach-Object id | Sort-Object)
        if ((($expectedCellIds | Sort-Object) -join '|') -cne (($actualCellIds | Sort-Object) -join '|')) {
            throw "$expectedKey native cell coverage mismatch. Expected: $($expectedCellIds -join ', '); actual: $($actualCellIds -join ', ')."
        }
        foreach ($cell in $nativeCells) { Assert-NativeCellSemantics -Cell $cell }
        $envelope = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-platform-evidence'
            status = 'pass'
            platform = $Platform
            architecture = $Architecture
            identity = $identityProjection
            artifactEvidence = @($artifactResults | ForEach-Object reference | Sort-Object evidenceId)
            nativeEvidence = @($nativeReferences | Sort-Object fileName)
            transportReceiptFile = [IO.Path]::GetFileName($receiptResolved)
            transportReceiptSha256 = Get-LowerFileSha256 -Path $receiptResolved
            transport = $receipt
            artifacts = @($artifactRecords | Sort-Object assetId)
            relations = @($relationRecords | Sort-Object relationId)
            nativeCells = @($nativeCells | Sort-Object id)
            releasePromotion = 'notApplicable'
            productionSignatureEligibility = 'notApplicable'
            productionReady = $false
        }
        Assert-JsonSchema -Value $envelope -Label "$expectedKey platform envelope"
        Write-JsonFile -Value $envelope -Path $Evidence
        Write-Output "Merged $expectedKey platform envelope with $($nativeCells.Count) mandatory native cell(s)."
    }

    'Aggregate' {
        if ($EvidencePath.Count -eq 0 -or
            [string]::IsNullOrWhiteSpace($OutputChecksums) -or [string]::IsNullOrWhiteSpace($Evidence) -or
            [string]::IsNullOrWhiteSpace($Identity)) {
            throw 'Aggregate mode requires -Identity, -EvidencePath, -OutputChecksums and -Evidence.'
        }

        $identityObject = Read-JsonFile -Path $Identity -Label 'Release identity'
        $identityProjection = Assert-IdentityContract -IdentityObject $identityObject
        $platforms = [Collections.Generic.List[object]]::new()
        $allArtifacts = [Collections.Generic.List[object]]::new()
        $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $seenNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $seenPlatformKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $seenCellIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $seenRelationIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($path in $EvidencePath) {
            $resolved = Resolve-RequiredPath -Path $path -Label 'Platform evidence'
            $report = Read-JsonFile -Path $resolved -Label 'Platform evidence'
            Assert-JsonSchema -Value $report -Label "Platform evidence '$resolved'"
            if ([string]$report.kind -cne 'distribution-platform-evidence' -or [string]$report.status -cne 'pass' -or $report.productionReady -ne $false) {
                throw "Platform evidence '$resolved' is not a passing Stage-3 envelope."
            }
            Assert-IdentityProjectionEquals -Expected $identityProjection -Actual $report.identity -Label "Platform evidence '$resolved'"
            $platformKey = "$($report.platform)/$($report.architecture)"
            if (-not $seenPlatformKeys.Add($platformKey)) { throw "Duplicate platform envelope '$platformKey'." }
            $nativeEvidenceByFile = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($nativeReference in @($report.nativeEvidence)) {
                $nativeFileName = [string]$nativeReference.fileName
                Assert-SafeFileName -Value $nativeFileName -Label "$platformKey native evidence filename"
                Assert-HexSha256 -Value ([string]$nativeReference.sha256) -Label "$platformKey native evidence SHA-256"
                if (-not $nativeEvidenceByFile.TryAdd($nativeFileName, $nativeReference)) {
                    throw "$platformKey native evidence duplicates file '$nativeFileName'."
                }
            }
            if ($platformKey -ceq 'android/multi') {
                $expectedAndroidNativeEvidence = [ordered]@{
                    'android-artifact.json' = [pscustomobject]@{ kind = 'distribution-android-native-evidence'; mode = 'artifact' }
                    'native-cache-evidence.json' = [pscustomobject]@{ kind = 'distribution-android-native-evidence'; mode = 'provenance' }
                    'native-inputs.json' = [pscustomobject]@{ kind = 'distribution-android-native-inputs'; mode = 'native-inputs' }
                    'native-provenance.json' = [pscustomobject]@{ kind = 'distribution-android-native-provenance'; mode = 'native-provenance' }
                    'android-api23-emulator.json' = [pscustomobject]@{ kind = 'distribution-android-native-evidence'; mode = 'emulator' }
                    'android-api36-emulator.json' = [pscustomobject]@{ kind = 'distribution-android-native-evidence'; mode = 'emulator' }
                    'android-api23-download-transport.json' = [pscustomobject]@{ kind = 'distribution-download-transport'; mode = 'distribution-download-transport' }
                    'android-api36-download-transport.json' = [pscustomobject]@{ kind = 'distribution-download-transport'; mode = 'distribution-download-transport' }
                }
                $actualNativeFiles = @($nativeEvidenceByFile.Keys | Sort-Object)
                $expectedNativeFiles = @($expectedAndroidNativeEvidence.Keys | Sort-Object)
                if (($actualNativeFiles -join '|') -cne ($expectedNativeFiles -join '|')) {
                    throw "Android aggregate native-evidence coverage mismatch. Expected: $($expectedNativeFiles -join ', '); actual: $($actualNativeFiles -join ', ')."
                }
                foreach ($expectedNativeFile in $expectedNativeFiles) {
                    $actualReference = $nativeEvidenceByFile[$expectedNativeFile]
                    $expectedReference = $expectedAndroidNativeEvidence[$expectedNativeFile]
                    if ([string]$actualReference.kind -cne [string]$expectedReference.kind -or
                        [string]$actualReference.mode -cne [string]$expectedReference.mode) {
                        throw "Android native evidence '$expectedNativeFile' has the wrong kind/mode."
                    }
                }
            }
            foreach ($artifact in @($report.artifacts)) {
                [void](Get-ManifestAsset -ManifestObject $manifestObject -Id ([string]$artifact.assetId))
                if (-not $seenIds.Add([string]$artifact.assetId)) { throw "Asset '$($artifact.assetId)' appears in more than one producer report." }
                if (-not $seenNames.Add([string]$artifact.fileName)) { throw "File '$($artifact.fileName)' appears in more than one producer report." }
                $allArtifacts.Add($artifact)
            }
            foreach ($relation in @($report.relations)) {
                if ([string]$relation.status -cne 'pass' -or -not $seenRelationIds.Add([string]$relation.relationId)) {
                    throw "Relation '$($relation.relationId)' is failed or duplicated."
                }
            }
            foreach ($cell in @($report.nativeCells)) {
                if ([string]$cell.status -cne 'pass' -or -not $seenCellIds.Add([string]$cell.id)) {
                    throw "Native cell '$($cell.id)' is failed or duplicated."
                }
                Assert-NativeCellSemantics -Cell $cell
                $cellEvidenceFile = [string]$cell.evidenceFile
                if (-not $nativeEvidenceByFile.ContainsKey($cellEvidenceFile) -or
                    [string]$nativeEvidenceByFile[$cellEvidenceFile].sha256 -cne [string]$cell.evidenceSha256) {
                    throw "Native cell '$($cell.id)' is not cross-linked to its exact native-evidence file/SHA-256 reference."
                }
            }
            $platforms.Add($report)
        }

        $requiredPlatformKeys = @('windows/x64', 'linux/x64', 'macos/x64', 'macos/arm64', 'android/multi')
        $missingPlatforms = @($requiredPlatformKeys | Where-Object { -not $seenPlatformKeys.Contains($_) })
        $unexpectedPlatforms = @($seenPlatformKeys | Where-Object { $_ -cnotin $requiredPlatformKeys })
        if ($missingPlatforms.Count -gt 0 -or $unexpectedPlatforms.Count -gt 0) {
            throw "Aggregate platform coverage mismatch. Missing: $($missingPlatforms -join ', '); unexpected: $($unexpectedPlatforms -join ', ')."
        }

        $manifestIds = @($manifestObject.assets | ForEach-Object { [string]$_.id })
        $expectedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $requestedIds = if ($AssetId.Count -eq 0) { $manifestIds } else { $AssetId }
        foreach ($id in $requestedIds) {
            if (-not $expectedIds.Add($id)) { throw "Duplicate requested aggregate asset id '$id'." }
        }
        $manifestSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($id in $manifestIds) { [void]$manifestSet.Add($id) }
        $requestedMissing = @($manifestSet | Where-Object { -not $expectedIds.Contains($_) })
        $requestedUnexpected = @($expectedIds | Where-Object { -not $manifestSet.Contains($_) })
        if ($requestedMissing.Count -gt 0 -or $requestedUnexpected.Count -gt 0) {
            throw "Aggregate -AssetId must equal the full manifest set. Missing: $($requestedMissing -join ', '); unexpected: $($requestedUnexpected -join ', ')."
        }
        $missing = @($expectedIds | Where-Object { -not $seenIds.Contains($_) })
        $unexpected = @($seenIds | Where-Object { -not $expectedIds.Contains($_) })
        if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
            throw "Aggregate coverage mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
        }

        $requiredRelationIds = @($manifestObject.relations | ForEach-Object { [string]$_.id })
        $missingRelations = @($requiredRelationIds | Where-Object { -not $seenRelationIds.Contains($_) })
        $unexpectedRelations = @($seenRelationIds | Where-Object { $_ -cnotin $requiredRelationIds })
        if ($missingRelations.Count -gt 0 -or $unexpectedRelations.Count -gt 0) {
            throw "Aggregate relation coverage mismatch. Missing: $($missingRelations -join ', '); unexpected: $($unexpectedRelations -join ', ')."
        }

        $requiredCellIds = @(
            'windows-server-2022-x64',
            'debian-12-x64-clean', 'debian-12-x64-upgrade', 'debian-12-x64-appimage', 'debian-12-x64-missing-runtime-negative',
            'debian-13-x64-clean', 'debian-13-x64-upgrade', 'debian-13-x64-appimage', 'debian-13-x64-missing-runtime-negative',
            'macos-15-x64', 'macos-15-arm64',
            'android-arm64-apk-metadata', 'android-x64-apk-metadata', 'android-api-23-x64-emulator', 'android-api-36-x64-emulator'
        )
        $missingCells = @($requiredCellIds | Where-Object { -not $seenCellIds.Contains($_) })
        $unexpectedCells = @($seenCellIds | Where-Object { $_ -cnotin $requiredCellIds })
        if ($missingCells.Count -gt 0 -or $unexpectedCells.Count -gt 0) {
            throw "Aggregate native-cell coverage mismatch. Missing: $($missingCells -join ', '); unexpected: $($unexpectedCells -join ', ')."
        }

        $checksumParent = Split-Path -Parent $OutputChecksums
        if ($checksumParent) { New-Item -ItemType Directory -Force -Path $checksumParent | Out-Null }
        $lines = @($allArtifacts | Sort-Object fileName | ForEach-Object { "$($_.sha256)  $($_.fileName)" })
        $lines | Set-Content -LiteralPath $OutputChecksums -Encoding utf8NoBOM
        $checksumHash = Get-LowerFileSha256 -Path $OutputChecksums
        $aggregate = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-aggregate-evidence'
            status = 'pass'
            identity = $identityProjection
            platforms = @($platforms | Sort-Object platform, architecture)
            artifacts = @($allArtifacts | Sort-Object assetId)
            assetCount = $allArtifacts.Count
            relationIds = @($seenRelationIds | Sort-Object)
            mandatoryCellIds = @($seenCellIds | Sort-Object)
            checksumsFile = [IO.Path]::GetFileName($OutputChecksums)
            checksumsSha256 = $checksumHash
            releasePromotion = 'notApplicable'
            productionSignatureEligibility = 'notApplicable'
            productionReady = $false
        }
        Assert-JsonSchema -Value $aggregate -Label 'Distribution aggregate evidence'
        Write-JsonFile -Value $aggregate -Path $Evidence
        Write-Output "Aggregated $($allArtifacts.Count) artifact(s) into $OutputChecksums."
    }
}
