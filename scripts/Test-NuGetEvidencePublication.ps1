[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory)]
    [string]$ExpectedLane,
    [Parameter(Mandatory)]
    [string]$ExpectedSourceSha,
    [Parameter(Mandatory)]
    [string]$ExpectedRunAttempt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ExactKeys([hashtable]$Object, [string[]]$Expected, [string]$Name) {
    $actual = @($Object.Keys | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)
    Assert-True ($actual.Count -eq $expectedSorted.Count) "$Name has an unexpected property count."
    for ($index = 0; $index -lt $expectedSorted.Count; $index++) {
        Assert-True ($actual[$index] -ceq $expectedSorted[$index]) "$Name has an unexpected property."
    }
}

function Test-PublicationReceipt([string]$Root) {
    $receiptPath = Join-Path $Root 'attempt-receipt.json'
    Assert-True (Test-Path -LiteralPath $receiptPath -PathType Leaf) 'Published evidence receipt is missing.'
    foreach ($directory in @(Get-ChildItem -LiteralPath $Root -Recurse -Directory -Force)) {
        Assert-True (-not $directory.LinkType) 'Published evidence directory cannot be a link.'
    }
    $receiptFile = Get-Item -LiteralPath $receiptPath -Force
    Assert-True (-not $receiptFile.LinkType -and $receiptFile.Length -gt 0 -and $receiptFile.Length -le 1MB) 'Published evidence receipt is invalid.'
    $receipt = [IO.File]::ReadAllText($receiptPath, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 32
    Assert-True ($receipt.receiptKind -is [string] -and $receipt.receiptKind -cin @('primary', 'safe-fallback')) 'Published evidence receipt kind is invalid.'
    $expectedKeys = if ($receipt.receiptKind -ceq 'primary') { @('schemaVersion', 'receiptKind', 'sourceSha', 'runAttempt', 'lane', 'outcome', 'failurePhase', 'failureCode', 'phases', 'evidenceManifest') } else { @('schemaVersion', 'receiptKind', 'sourceSha', 'runAttempt', 'lane', 'outcome', 'failureCode', 'evidenceManifest') }
    Assert-ExactKeys -Object $receipt -Expected $expectedKeys -Name 'published evidence receipt'
    Assert-True ($receipt.schemaVersion -is [long] -and $receipt.schemaVersion -eq 1) 'Published evidence schemaVersion is invalid.'
    Assert-True ($receipt.sourceSha -is [string] -and $receipt.sourceSha -ceq $ExpectedSourceSha) 'Published evidence sourceSha is invalid.'
    Assert-True ($receipt.runAttempt -is [long] -and $receipt.runAttempt -eq [long]$ExpectedRunAttempt) 'Published evidence runAttempt is invalid.'
    Assert-True ($receipt.lane -is [string] -and $receipt.lane -ceq $ExpectedLane) 'Published evidence lane is invalid.'
    $allFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force)
    foreach ($file in $allFiles) { Assert-True (-not $file.LinkType) 'Published evidence file cannot be a link.' }
    if ($receipt.receiptKind -ceq 'safe-fallback') {
        Assert-True ($receipt.outcome -ceq 'failure' -and $receipt.failureCode -ceq 'publication-integrity-failed' -and (@($receipt.evidenceManifest)).Count -eq 0 -and $allFiles.Count -eq 1) 'Published fallback receipt is invalid.'
        return
    }
    Assert-True ($receipt.outcome -is [string] -and $receipt.outcome -cin @('success', 'failure')) 'Published primary outcome is invalid.'
    Assert-True (($receipt.outcome -ceq 'success' -and $receipt.failurePhase -eq $null -and $receipt.failureCode -eq $null) -or ($receipt.outcome -ceq 'failure' -and $receipt.failurePhase -is [string] -and $receipt.failureCode -is [string])) 'Published primary failure tuple is invalid.'
    $manifest = @($receipt.evidenceManifest)
    Assert-True ($manifest.Count -eq $allFiles.Count - 1) 'Published primary manifest cardinality is invalid.'
    $manifestPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $manifest) {
        Assert-True ($entry -is [hashtable]) 'Published manifest entry is invalid.'
        Assert-ExactKeys -Object $entry -Expected @('path', 'sha256', 'byteLength') -Name 'published manifest entry'
        Assert-True ($entry.path -is [string] -and $entry.path -cmatch '^[A-Za-z0-9][A-Za-z0-9./_-]*$' -and $entry.path -cne 'attempt-receipt.json' -and $manifestPaths.Add($entry.path)) 'Published manifest path is invalid.'
        Assert-True ($entry.sha256 -is [string] -and $entry.sha256 -cmatch '^[0-9a-f]{64}$' -and $entry.byteLength -is [long] -and $entry.byteLength -ge 0) 'Published manifest hash tuple is invalid.'
        $path = [IO.Path]::GetFullPath((Join-Path $Root ($entry.path -replace '/', [IO.Path]::DirectorySeparatorChar)))
        Assert-True ([IO.Path]::GetRelativePath($Root, $path).Replace('\', '/') -ceq $entry.path -and (Test-Path -LiteralPath $path -PathType Leaf)) 'Published manifest path escaped its root.'
        $file = Get-Item -LiteralPath $path -Force
        Assert-True ($file.Length -eq $entry.byteLength -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $entry.sha256) 'Published manifest file hash is invalid.'
    }
    $evidencePrefix = $ExpectedLane.ToLowerInvariant()
    $evidencePath = Join-Path $Root "$evidencePrefix\evidence.json"
    Assert-True (Test-Path -LiteralPath $evidencePath -PathType Leaf) 'Published lane evidence is missing.'
    $evidence = [IO.File]::ReadAllText($evidencePath, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 32
    $expectedKind = if ($receipt.outcome -ceq 'success') { "$evidencePrefix-success" } else { "$evidencePrefix-failure" }
    Assert-True ($evidence.evidenceKind -ceq $expectedKind) 'Published lane evidence kind is inconsistent with receipt outcome.'
}

Assert-True ($ExpectedLane -ceq 'Signature' -or $ExpectedLane -ceq 'Regression') 'ExpectedLane must be exactly Signature or Regression.'
Assert-True ($ExpectedSourceSha -cmatch '^[0-9a-f]{40}$') 'ExpectedSourceSha must be a lowercase 40-hex Git SHA.'
Assert-True ($ExpectedRunAttempt -cmatch '^[1-9][0-9]{0,9}$') 'ExpectedRunAttempt must be a canonical positive decimal integer.'

$root = [IO.Path]::GetFullPath($EvidenceRoot)
Assert-True (Test-Path -LiteralPath $root -PathType Container) 'EvidenceRoot does not exist.'
Assert-True (-not (Get-Item -LiteralPath $root -Force).LinkType) 'EvidenceRoot cannot be a link.'

if (Test-Path -LiteralPath (Join-Path $root 'attempt-receipt.json') -PathType Leaf) {
    Test-PublicationReceipt -Root $root
    Write-Output 'NuGet evidence receipt: valid'
    return
}

$files = @(Get-ChildItem -LiteralPath $root -Force -File)
Assert-True ($files.Count -eq 1 -and $files[0].Name -ceq 'attempt.json') 'EvidenceRoot must contain exactly attempt.json.'
Assert-True (-not $files[0].LinkType) 'attempt.json cannot be a link.'
Assert-True ($files[0].Length -gt 0 -and $files[0].Length -le 1MB) 'attempt.json has an invalid length.'

$receipt = [IO.File]::ReadAllText($files[0].FullName, [Text.UTF8Encoding]::new($false)) | ConvertFrom-Json -AsHashtable -Depth 16
Assert-ExactKeys -Object $receipt -Expected @('schemaVersion', 'lane', 'sourceSha', 'runAttempt', 'outcome', 'phases', 'packages', 'failureCode') -Name 'receipt'

Assert-True ($receipt.schemaVersion -is [long] -and $receipt.schemaVersion -eq 1) 'Receipt schemaVersion is invalid.'
Assert-True ($receipt.lane -is [string] -and $receipt.lane -ceq $ExpectedLane) 'Receipt lane is invalid.'
Assert-True ($receipt.sourceSha -is [string] -and $receipt.sourceSha -ceq $ExpectedSourceSha) 'Receipt sourceSha is invalid.'
Assert-True ($receipt.runAttempt -is [long] -and $receipt.runAttempt -eq [long]$ExpectedRunAttempt) 'Receipt runAttempt is invalid.'
Assert-True ($receipt.outcome -is [string] -and ($receipt.outcome -ceq 'success' -or $receipt.outcome -ceq 'failure')) 'Receipt outcome is invalid.'
Assert-True ($receipt.phases -is [System.Collections.IEnumerable]) 'Receipt phases is invalid.'
Assert-True ($receipt.packages -is [System.Collections.IEnumerable]) 'Receipt packages is invalid.'

$phases = @($receipt.phases)
Assert-True ($phases.Count -ge 1) 'Receipt phases cannot be empty.'
foreach ($phase in $phases) {
    Assert-True ($phase -is [hashtable]) 'Receipt phase must be an object.'
    Assert-ExactKeys -Object $phase -Expected @('name', 'status', 'exitCode') -Name 'receipt phase'
    $phasePattern = if ($ExpectedLane -ceq 'Signature') { '^signature:(restore|assets|verify):' } else { '^regression:(restore|build|test):' }
    Assert-True ($phase.name -is [string] -and $phase.name -cmatch $phasePattern) 'Receipt phase name is invalid.'
    Assert-True ($phase.status -is [string] -and ($phase.status -ceq 'success' -or $phase.status -ceq 'failure')) 'Receipt phase status is invalid.'
    Assert-True ($phase.exitCode -is [long]) 'Receipt phase exitCode is invalid.'
}

$expectedPackages = @(
    @{ id = 'ReactiveUI.Avalonia'; version = '12.0.2' },
    @{ id = 'ReactiveUI'; version = '23.2.28' },
    @{ id = 'Splat'; version = '19.4.1' },
    @{ id = 'Splat.Builder'; version = '19.4.1' },
    @{ id = 'Splat.Core'; version = '19.4.1' },
    @{ id = 'Splat.Logging'; version = '19.4.1' }
)

$packages = @($receipt.packages)
if ($receipt.outcome -ceq 'success') {
    Assert-True ($receipt.failureCode -eq $null) 'Successful receipt cannot have failureCode.'
    Assert-True ($phases.Where({ $_.status -cne 'success' }).Count -eq 0) 'Successful receipt contains a failed phase.'
    $expectedPackageCount = if ($ExpectedLane -ceq 'Signature') { $expectedPackages.Count } else { 0 }
    Assert-True ($packages.Count -eq $expectedPackageCount) 'Successful receipt has an unexpected package count.'
    if ($ExpectedLane -ceq 'Signature') {
        for ($index = 0; $index -lt $expectedPackages.Count; $index++) {
            $package = $packages[$index]
            Assert-True ($package -is [hashtable]) 'Receipt package must be an object.'
            Assert-ExactKeys -Object $package -Expected @('id', 'version', 'nupkgSha512', 'authorCertificateSha256') -Name 'receipt package'
            Assert-True ($package.id -is [string] -and $package.id -ceq $expectedPackages[$index].id) 'Receipt package id is invalid.'
            Assert-True ($package.version -is [string] -and $package.version -ceq $expectedPackages[$index].version) 'Receipt package version is invalid.'
            Assert-True ($package.nupkgSha512 -is [string] -and $package.nupkgSha512 -cmatch '^[0-9a-f]{128}$') 'Receipt package SHA-512 is invalid.'
            Assert-True ($package.authorCertificateSha256 -is [string] -and $package.authorCertificateSha256 -ceq '4D2DDD563BC0ECF5C9B438E1CE32E3FCC69DAADAFC2D1BD9CF858FD9E755CFB9') 'Receipt package author certificate is invalid.'
        }
    }
} else {
    Assert-True ($receipt.failureCode -is [string] -and $receipt.failureCode -ceq 'attempt-failed') 'Failed receipt failureCode is invalid.'
    Assert-True ($phases.Where({ $_.status -ceq 'failure' }).Count -ge 1) 'Failed receipt does not contain a failed phase.'
    $maximumPackageCount = if ($ExpectedLane -ceq 'Signature') { $expectedPackages.Count } else { 0 }
    Assert-True ($packages.Count -le $maximumPackageCount) 'Failed receipt has too many packages.'
}

Write-Output 'NuGet evidence receipt: valid'
