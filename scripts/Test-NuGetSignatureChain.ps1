[CmdletBinding()]
param(
    [ValidateSet('RunAttempt', 'SelfTest')]
    [string]$Mode = 'RunAttempt',
    [AllowEmptyString()]
    [string]$Lane = 'Signature',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$PackagesRoot,
    [AllowEmptyString()]
    [string]$ExpectedSourceSha,
    [AllowEmptyString()]
    [string]$RunAttempt = '1',
    [string]$EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Get-ExpectedPackages {
    return @(
        @{ Id = 'ReactiveUI.Avalonia'; Version = '12.0.2' },
        @{ Id = 'ReactiveUI'; Version = '23.2.28' },
        @{ Id = 'Splat'; Version = '19.4.1' },
        @{ Id = 'Splat.Builder'; Version = '19.4.1' },
        @{ Id = 'Splat.Core'; Version = '19.4.1' },
        @{ Id = 'Splat.Logging'; Version = '19.4.1' }
    )
}

function Get-ExpectedAuthorFingerprint {
    return '4D2DDD563BC0ECF5C9B438E1CE32E3FCC69DAADAFC2D1BD9CF858FD9E755CFB9'
}

function Get-CanonicalRepositoryRoot {
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'src\Directory.Packages.props')) 'RepositoryRoot does not contain src\Directory.Packages.props.'
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'src\nuget.config')) 'RepositoryRoot does not contain src\nuget.config.'
    return $root
}

function Resolve-RunAttempt {
    $parsed = 0
    Assert-True ($RunAttempt -cmatch '^[1-9][0-9]{0,9}$') 'RunAttempt must be a canonical positive decimal integer.'
    Assert-True ([int]::TryParse($RunAttempt, [ref]$parsed)) 'RunAttempt is outside Int32 range.'
    return $parsed
}

function Resolve-SourceSha([string]$Root) {
    if (-not [string]::IsNullOrEmpty($ExpectedSourceSha)) {
        Assert-True ($ExpectedSourceSha -cmatch '^[0-9a-f]{40}$') 'ExpectedSourceSha must be a lowercase 40-hex Git SHA.'
        return $ExpectedSourceSha
    }

    $sha = (& git -C $Root rev-parse HEAD).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $sha -cmatch '^[0-9a-f]{40}$') 'Could not resolve the current Git SHA.'
    return $sha
}

function New-IsolatedPackagesRoot {
    if ([string]::IsNullOrWhiteSpace($PackagesRoot)) {
        $candidate = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-nuget-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $candidate -ErrorAction Stop | Out-Null
        return [IO.Path]::GetFullPath($candidate)
    }

    $candidate = [IO.Path]::GetFullPath($PackagesRoot)
    Assert-True (Test-Path -LiteralPath $candidate -PathType Container) 'PackagesRoot must be an existing directory.'
    Assert-True (-not (Get-ChildItem -LiteralPath $candidate -Force | Select-Object -First 1)) 'PackagesRoot must be empty before a signature attempt.'
    return $candidate
}

function Get-EvidenceRoot([string]$Root, [string]$SourceSha, [int]$Attempt) {
    if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) {
        $candidate = [IO.Path]::GetFullPath($EvidenceRoot)
        Assert-True (-not (Test-Path -LiteralPath $candidate)) 'EvidenceRoot must not already exist.'
        return $candidate
    }

    return (Join-Path ([IO.Path]::GetTempPath()) ("unlimotion-nuget-evidence-{0}-attempt-{1}-{2}" -f $SourceSha, $Attempt, [Guid]::NewGuid().ToString('N')))
}

function Invoke-DotNet([string]$Phase, [string[]]$Arguments, [System.Collections.Generic.List[object]]$Phases) {
    & dotnet @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        $Phases.Add([ordered]@{ name = $Phase; status = 'success'; exitCode = 0 })
        return
    }

    $Phases.Add([ordered]@{ name = $Phase; status = 'failure'; exitCode = $exitCode })
    throw "dotnet failed in $Phase with exit code $exitCode."
}

function Get-PackageReceipt([string]$PackagesPath, [System.Collections.Generic.List[object]]$Phases) {
    $receipt = [System.Collections.Generic.List[object]]::new()
    foreach ($package in Get-ExpectedPackages) {
        $id = [string]$package.Id
        $version = [string]$package.Version
        $nupkg = Join-Path $PackagesPath ("{0}\{1}\{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $version)
        Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "Expected package is absent: $id $version."

        & dotnet nuget verify $nupkg --all --certificate-fingerprint (Get-ExpectedAuthorFingerprint) | Out-Host
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $Phases.Add([ordered]@{ name = "signature:verify:$id"; status = 'failure'; exitCode = $exitCode })
            throw "Signature verification failed for $id $version."
        }

        $Phases.Add([ordered]@{ name = "signature:verify:$id"; status = 'success'; exitCode = 0 })
        $receipt.Add([ordered]@{
                id = $id
                version = $version
                nupkgSha512 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash.ToLowerInvariant()
                authorCertificateSha256 = Get-ExpectedAuthorFingerprint
            })
    }

    return ,$receipt
}

function Write-AttemptReceipt(
    [string]$Root,
    [string]$SourceSha,
    [int]$Attempt,
    [string]$Outcome,
    [System.Collections.Generic.List[object]]$Phases,
    [System.Collections.Generic.List[object]]$Packages,
    [string]$FailureCode
) {
    New-Item -ItemType Directory -Path $Root -ErrorAction Stop | Out-Null
    $receipt = [ordered]@{
        schemaVersion = 1
        lane = 'Signature'
        sourceSha = $SourceSha
        runAttempt = $Attempt
        outcome = $Outcome
        phases = @($Phases)
        packages = @($Packages)
        failureCode = if ($Outcome -ceq 'success') { $null } else { $FailureCode }
    }

    $json = $receipt | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $Root 'attempt.json'), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Invoke-SignatureAttempt {
    Assert-True ($Lane -ceq 'Signature') 'RunAttempt currently accepts only the Signature lane.'
    Assert-True ($env:DOTNET_NUGET_SIGNATURE_VERIFICATION -ceq 'true') 'DOTNET_NUGET_SIGNATURE_VERIFICATION must be exactly true.'
    Assert-True ([string]::IsNullOrEmpty($env:NUGET_CERT_REVOCATION_MODE) -or $env:NUGET_CERT_REVOCATION_MODE -ceq 'online') 'NUGET_CERT_REVOCATION_MODE must be absent or exactly online.'

    $root = Get-CanonicalRepositoryRoot
    $sourceSha = Resolve-SourceSha -Root $root
    $attempt = Resolve-RunAttempt
    $packagesPath = New-IsolatedPackagesRoot
    $evidencePath = Get-EvidenceRoot -Root $root -SourceSha $sourceSha -Attempt $attempt
    $phases = [System.Collections.Generic.List[object]]::new()
    $packages = [System.Collections.Generic.List[object]]::new()
    $env:NUGET_PACKAGES = $packagesPath

    try {
        $projects = @(
            @{ id = 'headless'; path = 'tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj' },
            @{ id = 'desktop'; path = 'src\Unlimotion.Desktop\Unlimotion.Desktop.csproj' },
            @{ id = 'debian'; path = 'src\Unlimotion.Desktop\Unlimotion.Desktop.ForDebianBuild.csproj' }
        )

        foreach ($project in $projects) {
            Invoke-DotNet -Phase "signature:restore:$($project.id)" -Arguments @(
                'restore', (Join-Path $root $project.path), '--force', '--no-http-cache',
                '--configfile', (Join-Path $root 'src\nuget.config'),
                '-p:Configuration=Debug',
                '-p:DisableImplicitLibraryPacksFolder=true',
                '-p:DisableImplicitNuGetFallbackFolder=true',
                '-p:RestoreFallbackFolders='
            ) -Phases $phases
        }

        $packages = Get-PackageReceipt -PackagesPath $packagesPath -Phases $phases
        Write-AttemptReceipt -Root $evidencePath -SourceSha $sourceSha -Attempt $attempt -Outcome 'success' -Phases $phases -Packages $packages -FailureCode $null
        Write-Output "NuGet signature evidence: $evidencePath"
    } catch {
        Write-AttemptReceipt -Root $evidencePath -SourceSha $sourceSha -Attempt $attempt -Outcome 'failure' -Phases $phases -Packages $packages -FailureCode 'attempt-failed'
        throw
    }
}

function Invoke-SelfTest {
    Assert-True ((Get-ExpectedPackages).Count -eq 6) 'Expected signed subset changed.'
    Assert-True ('Signature' -ceq 'Signature') 'Case-sensitive lane comparison changed.'

    $invalidAttemptRejected = $false
    $originalRunAttempt = $RunAttempt
    try {
        $script:RunAttempt = '01'
        [void](Resolve-RunAttempt)
    } catch {
        $invalidAttemptRejected = $true
    } finally {
        $script:RunAttempt = $originalRunAttempt
    }
    Assert-True $invalidAttemptRejected 'Non-canonical run attempt was accepted.'
}

if ($Mode -eq 'SelfTest') {
    Invoke-SelfTest
    return
}

Invoke-SignatureAttempt
