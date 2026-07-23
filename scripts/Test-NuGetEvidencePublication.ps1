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

Assert-True ($ExpectedLane -ceq 'Signature') 'ExpectedLane must be exactly Signature.'
Assert-True ($ExpectedSourceSha -cmatch '^[0-9a-f]{40}$') 'ExpectedSourceSha must be a lowercase 40-hex Git SHA.'
Assert-True ($ExpectedRunAttempt -cmatch '^[1-9][0-9]{0,9}$') 'ExpectedRunAttempt must be a canonical positive decimal integer.'

$root = [IO.Path]::GetFullPath($EvidenceRoot)
Assert-True (Test-Path -LiteralPath $root -PathType Container) 'EvidenceRoot does not exist.'
Assert-True (-not (Get-Item -LiteralPath $root -Force).LinkType) 'EvidenceRoot cannot be a link.'

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
    Assert-True ($phase.name -is [string] -and $phase.name -cmatch '^signature:(restore|verify):') 'Receipt phase name is invalid.'
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
    Assert-True ($packages.Count -eq $expectedPackages.Count) 'Successful receipt has an unexpected package count.'
    for ($index = 0; $index -lt $expectedPackages.Count; $index++) {
        $package = $packages[$index]
        Assert-True ($package -is [hashtable]) 'Receipt package must be an object.'
        Assert-ExactKeys -Object $package -Expected @('id', 'version', 'nupkgSha512', 'authorCertificateSha256') -Name 'receipt package'
        Assert-True ($package.id -is [string] -and $package.id -ceq $expectedPackages[$index].id) 'Receipt package id is invalid.'
        Assert-True ($package.version -is [string] -and $package.version -ceq $expectedPackages[$index].version) 'Receipt package version is invalid.'
        Assert-True ($package.nupkgSha512 -is [string] -and $package.nupkgSha512 -cmatch '^[0-9a-f]{128}$') 'Receipt package SHA-512 is invalid.'
        Assert-True ($package.authorCertificateSha256 -is [string] -and $package.authorCertificateSha256 -ceq '4D2DDD563BC0ECF5C9B438E1CE32E3FCC69DAADAFC2D1BD9CF858FD9E755CFB9') 'Receipt package author certificate is invalid.'
    }
} else {
    Assert-True ($receipt.failureCode -is [string] -and $receipt.failureCode -ceq 'attempt-failed') 'Failed receipt failureCode is invalid.'
    Assert-True ($phases.Where({ $_.status -ceq 'failure' }).Count -ge 1) 'Failed receipt does not contain a failed phase.'
    Assert-True ($packages.Count -le $expectedPackages.Count) 'Failed receipt has too many packages.'
}

Write-Output 'NuGet evidence receipt: valid'
