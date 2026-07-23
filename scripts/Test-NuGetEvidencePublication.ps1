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

function Test-PublicationPhases([object[]]$Phases, [hashtable]$Receipt) {
    Assert-True ($Phases.Count -ge 1) 'Published primary phases cannot be empty.'
    $expectedPrefix = $Receipt.lane.ToLowerInvariant() + ':'
    $failed = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($phase in $Phases) {
        Assert-True ($phase -is [hashtable]) 'Published primary phase is invalid.'
        Assert-ExactKeys -Object $phase -Expected @('name', 'status', 'exitCode', 'failureCode') -Name 'published primary phase'
        Assert-True ($phase.name -is [string] -and $phase.name.StartsWith($expectedPrefix, [StringComparison]::Ordinal)) 'Published primary phase name is invalid.'
        Assert-True ($phase.status -is [string] -and $phase.status -cin @('success', 'failure')) 'Published primary phase status is invalid.'
        Assert-True ($phase.exitCode -is [long]) 'Published primary phase exit code is invalid.'
        if ($phase.status -ceq 'success') {
            Assert-True ($phase.exitCode -eq 0 -and $phase.failureCode -eq $null) 'Published successful phase tuple is invalid.'
        } else {
            Assert-True ($phase.exitCode -ne 0 -and $phase.failureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($phase.failureCode)) 'Published failed phase tuple is invalid.'
            $failed.Add($phase)
        }
    }
    if ($Receipt.outcome -ceq 'success') {
        Assert-True ($failed.Count -eq 0) 'Published successful receipt contains a failed phase.'
    } else {
        Assert-True ($failed.Count -ge 1 -and $Receipt.failurePhase -ceq $failed[0].name -and $Receipt.failureCode -ceq $failed[0].failureCode) 'Published failure receipt does not bind its first failed phase.'
    }
}

function Test-RegressionEvidenceReference([hashtable]$Reference, [string]$ExpectedPath, [string]$Root) {
    Assert-ExactKeys -Object $Reference -Expected @('path', 'sha256', 'byteLength') -Name 'published regression report reference'
    Assert-True ($Reference.path -is [string] -and $Reference.path -ceq $ExpectedPath -and $Reference.sha256 -is [string] -and $Reference.sha256 -cmatch '^[0-9a-f]{64}$' -and $Reference.byteLength -is [long] -and $Reference.byteLength -gt 0) 'Published regression report reference is invalid.'
    $path = [IO.Path]::GetFullPath((Join-Path $Root ($Reference.path -replace '/', [IO.Path]::DirectorySeparatorChar)))
    Assert-True ([IO.Path]::GetRelativePath($Root, $path).Replace('\', '/') -ceq $Reference.path -and (Test-Path -LiteralPath $path -PathType Leaf)) 'Published regression report reference escaped its root.'
    $file = Get-Item -LiteralPath $path -Force
    Assert-True (-not $file.LinkType -and $file.Length -eq $Reference.byteLength -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $Reference.sha256) 'Published regression report reference hash is invalid.'
}

function Test-RegressionSanitizedReports([hashtable]$Run, [string]$Root) {
    $trxPath = Join-Path $Root ($Run.trx.path -replace '/', [IO.Path]::DirectorySeparatorChar)
    $htmlPath = Join-Path $Root ($Run.html.path -replace '/', [IO.Path]::DirectorySeparatorChar)
    $expectedTrx = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><ResultSummary outcome=`"Completed`"><Counters total=`"$($Run.discovered)`" executed=`"$($Run.discovered)`" passed=`"$($Run.passed)`" failed=`"0`" notExecuted=`"0`" /></ResultSummary></TestRun>"
    $expectedHtml = "<!doctype html><html><head><meta charset=`"utf-8`"><title>Unlimotion regression $($Run.runId)</title></head><body><h1>Unlimotion regression $($Run.runId)</h1><dl><dt>discovered</dt><dd>$($Run.discovered)</dd><dt>passed</dt><dd>$($Run.passed)</dd><dt>failed</dt><dd>0</dd><dt>skipped</dt><dd>0</dd><dt>durationMs</dt><dd>$($Run.durationMs)</dd></dl></body></html>"
    Assert-True ([IO.File]::ReadAllText($trxPath, [Text.UTF8Encoding]::new($false)) -ceq $expectedTrx -and [IO.File]::ReadAllText($htmlPath, [Text.UTF8Encoding]::new($false)) -ceq $expectedHtml) 'Published regression reports are not the canonical sanitized projections.'
}

function Test-RegressionEvidence([hashtable]$Evidence, [hashtable]$Receipt, [string]$Root) {
    Assert-ExactKeys -Object $Evidence -Expected @('schemaVersion', 'evidenceKind', 'sourceSha', 'runAttempt', 'lane', 'runtime', 'runs') -Name 'published regression evidence'
    Assert-True ($Evidence.schemaVersion -is [long] -and $Evidence.schemaVersion -eq 1 -and $Evidence.sourceSha -ceq $Receipt.sourceSha -and $Evidence.runAttempt -eq $Receipt.runAttempt -and $Evidence.lane -ceq 'Regression') 'Published regression evidence identity is invalid.'
    Assert-ExactKeys -Object $Evidence.runtime -Expected @('os', 'architecture', 'dotnetSdkVersion', 'executionContext', 'signatureVerification', 'revocationMode', 'signatureAuthoritative') -Name 'published regression runtime'
    Assert-True ($Evidence.runtime.executionContext -ceq 'local' -and $Evidence.runtime.signatureVerification -eq $true -and $Evidence.runtime.signatureAuthoritative -eq $false -and $Evidence.runtime.revocationMode -eq $null) 'Published regression runtime authority is invalid.'
    $expectedRuns = @(
        @{ runId = 'unit'; projectPath = 'src/Unlimotion.Test/Unlimotion.Test.csproj'; minimumDiscovered = 830 },
        @{ runId = 'headless-1'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; minimumDiscovered = 36 },
        @{ runId = 'headless-2'; projectPath = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; minimumDiscovered = 36 }
    )
    $runs = @($Evidence.runs)
    Assert-True ($runs.Count -eq $expectedRuns.Count) 'Published regression run count is invalid.'
    $hasNonSuccessRun = $false
    for ($index = 0; $index -lt $expectedRuns.Count; $index++) {
        $run = $runs[$index]
        $expected = $expectedRuns[$index]
        Assert-True ($run -is [hashtable]) 'Published regression run is invalid.'
        Assert-ExactKeys -Object $run -Expected @('runId', 'state', 'projectPath', 'configuration', 'nativeExitCode', 'failureCode', 'discovered', 'passed', 'failed', 'skipped', 'durationMs', 'trx', 'html', 'skipReason') -Name 'published regression run'
        Assert-True ($run.runId -ceq $expected.runId -and $run.projectPath -ceq $expected.projectPath -and $run.configuration -ceq 'Debug' -and $run.state -cin @('success', 'failure', 'not-attempted')) 'Published regression run identity is invalid.'
        foreach ($property in @('nativeExitCode', 'discovered', 'passed', 'failed', 'skipped', 'durationMs')) {
            Assert-True ($run[$property] -eq $null -or $run[$property] -is [long]) "Published regression $property is invalid."
        }
        if ($run.state -ceq 'success') {
            Assert-True ($run.nativeExitCode -eq 0 -and $run.failureCode -eq $null -and $run.skipReason -eq $null -and $run.discovered -ge $expected.minimumDiscovered -and $run.passed -eq $run.discovered -and $run.failed -eq 0 -and $run.skipped -eq 0 -and $run.durationMs -ge 0) 'Published successful regression run is invalid.'
            Test-RegressionEvidenceReference -Reference $run.trx -ExpectedPath "regression/$($expected.runId).trx" -Root $Root
            Test-RegressionEvidenceReference -Reference $run.html -ExpectedPath "regression/$($expected.runId).html" -Root $Root
            Test-RegressionSanitizedReports -Run $run -Root $Root
        } elseif ($run.state -ceq 'failure') {
            $hasNonSuccessRun = $true
            Assert-True ($run.nativeExitCode -is [long] -and $run.failureCode -is [string] -and -not [string]::IsNullOrWhiteSpace($run.failureCode) -and $run.skipReason -eq $null -and $run.trx -eq $null -and $run.html -eq $null) 'Published failed regression run is invalid.'
        } else {
            $hasNonSuccessRun = $true
            Assert-True ($run.nativeExitCode -eq $null -and $run.failureCode -eq $null -and $run.discovered -eq $null -and $run.passed -eq $null -and $run.failed -eq $null -and $run.skipped -eq $null -and $run.durationMs -eq $null -and $run.trx -eq $null -and $run.html -eq $null -and $run.skipReason -ceq 'prerequisite-failed') 'Published skipped regression run is invalid.'
        }
    }
    if ($runs[1].state -ceq 'success' -and $runs[2].state -ceq 'success') {
        Assert-True ($runs[1].discovered -eq $runs[2].discovered -and $runs[1].passed -eq $runs[2].passed -and $runs[1].failed -eq $runs[2].failed -and $runs[1].skipped -eq $runs[2].skipped) 'Published regression headless run counts are inconsistent.'
    }
    $expectedKind = if ($hasNonSuccessRun) { 'regression-failure' } else { 'regression-success' }
    $receiptExpectedKind = if ($Receipt.outcome -ceq 'success') { 'regression-success' } else { 'regression-failure' }
    Assert-True ($Evidence.evidenceKind -ceq $expectedKind -and $Evidence.evidenceKind -ceq $receiptExpectedKind) 'Published regression evidence outcome is invalid.'
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
    Test-PublicationPhases -Phases @($receipt.phases) -Receipt $receipt
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
    if ($ExpectedLane -ceq 'Regression') { Test-RegressionEvidence -Evidence $evidence -Receipt $receipt -Root $Root }
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
