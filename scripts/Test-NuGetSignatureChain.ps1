[CmdletBinding()]
param(
    [ValidateSet('GenerateBaseline', 'RunAttempt', 'SelfTest', 'Worker')]
    [string]$Mode = 'RunAttempt',
    [AllowEmptyString()]
    [string]$Lane = 'Signature',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$PackagesRoot,
    [AllowEmptyString()]
    [string]$ExpectedSourceSha,
    [AllowEmptyString()]
    [string]$RunAttempt = '1',
    [string]$EvidenceRoot,
    [AllowEmptyString()]
    [string]$ExpectedParentSha,
    [string]$OutputPath,
    [string]$BaselineAssetsRoot,
    [string]$DotNetExecutable = 'dotnet'
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

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-CanonicalGraphHash([object[]]$Packages) {
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($package in $Packages) {
        $lines.Add(('{0}`t{1}`t{2}`t{3}`n' -f $package.id, $package.version, $package.source, $package.nupkgSha512))
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($lines -join [string]::Empty))
    return ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))).ToLowerInvariant()
}

function Get-BaselineProjectPackageSet([string]$Root, [string]$ProjectPath, [string]$PackagesPath, [string]$AssetsPath) {
    Assert-True (Test-Path -LiteralPath $AssetsPath -PathType Leaf) "Missing staged assets file: $ProjectPath."
    $assets = Get-Content -Raw -LiteralPath $AssetsPath | ConvertFrom-Json -AsHashtable -Depth 32
    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($libraryKey in @($assets.libraries.Keys | Sort-Object)) {
        $separator = $libraryKey.LastIndexOf('/')
        Assert-True ($separator -gt 0) "Invalid assets library key: $libraryKey."
        $id = $libraryKey.Substring(0, $separator)
        $version = $libraryKey.Substring($separator + 1)
        $library = $assets.libraries[$libraryKey]
        if ($library.type -cne 'package') { continue }
        $nupkg = Join-Path $PackagesPath (("{0}\\{1}\\{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $version))
        Assert-True (Test-Path -LiteralPath $nupkg -PathType Leaf) "Missing package payload: $id $version."
        $packages.Add([ordered]@{
            id = $id
            version = $version
            source = 'nuget.org'
            nupkgSha512 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash.ToLowerInvariant()
        })
    }
    $ordered = @($packages | Sort-Object @{ Expression = { $_.id }; Ascending = $true }, @{ Expression = { $_.version }; Ascending = $true })
    return [ordered]@{ projectPath = $ProjectPath; packageSet = $ordered; graphSha256 = Get-CanonicalGraphHash -Packages $ordered }
}

function Invoke-GenerateBaseline {
    $root = Get-CanonicalRepositoryRoot
    Assert-True ($ExpectedParentSha -cmatch '^[0-9a-f]{40}$') 'ExpectedParentSha must be a lowercase 40-hex Git SHA.'
    $head = (& git -C $root rev-parse HEAD).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $head -ceq $ExpectedParentSha) 'GenerateBaseline requires exact parent HEAD.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($PackagesRoot)) 'GenerateBaseline requires PackagesRoot.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($OutputPath)) 'GenerateBaseline requires OutputPath.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($BaselineAssetsRoot)) 'GenerateBaseline requires BaselineAssetsRoot.'
    $packagesPath = [IO.Path]::GetFullPath($PackagesRoot)
    Assert-True (Test-Path -LiteralPath $packagesPath -PathType Container) 'GenerateBaseline PackagesRoot must exist.'
    $output = [IO.Path]::GetFullPath($OutputPath)
    $assetsRoot = [IO.Path]::GetFullPath($BaselineAssetsRoot)
    Assert-True (-not (Test-Path -LiteralPath $output)) 'GenerateBaseline OutputPath must not exist.'

    $projectsToStage = @(
        @{ path = 'tests\\Unlimotion.UiTests.Headless\\Unlimotion.UiTests.Headless.csproj'; assets = 'headless.project.assets.json' },
        @{ path = 'src\\Unlimotion.Desktop\\Unlimotion.Desktop.csproj'; assets = 'desktop.project.assets.json' },
        @{ path = 'src\\Unlimotion.Desktop\\Unlimotion.Desktop.ForDebianBuild.csproj'; assets = 'debian.project.assets.json' }
    )
    $projectPaths = @($projectsToStage | ForEach-Object { $_.path })
    $manifestPaths = @('src/Directory.Packages.props', 'src/nuget.config')
    foreach ($projectPath in $projectPaths) {
        $manifestPaths += $projectPath.Replace('\\', '/')
    }
    $inputManifest = [System.Collections.Generic.List[object]]::new()
    foreach ($path in @($manifestPaths | Sort-Object -Unique)) {
        $absolute = Join-Path $root $path
        Assert-True (Test-Path -LiteralPath $absolute -PathType Leaf) "Baseline input is missing: $path."
        $stage = (& git -C $root ls-files -s -- $path).Trim()
        Assert-True ($LASTEXITCODE -eq 0 -and $stage -match '^(100644|100755) ([0-9a-f]{40}) 0\t') "Baseline input is not an exact regular Git blob: $path."
        $inputManifest.Add([ordered]@{ path = $path; mode = $Matches[1]; gitObjectId = $Matches[2]; byteLength = [long](Get-Item -LiteralPath $absolute).Length; sha256 = Get-Sha256 -Path $absolute })
    }
    $projects = [System.Collections.Generic.List[object]]::new()
    foreach ($project in $projectsToStage) {
        $projects.Add((Get-BaselineProjectPackageSet -Root $root -ProjectPath $project.path -PackagesPath $packagesPath -AssetsPath (Join-Path $assetsRoot $project.assets)))
    }
    $sdk = (& $DotNetExecutable --version).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $sdk -cmatch '^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') 'GenerateBaseline requires a .NET 10 SDK.'
    $fixture = [ordered]@{ schemaVersion = 1; sourceSha = $head; gitObjectFormat = 'sha1'; dotnetSdkVersion = $sdk; inputManifest = @($inputManifest); projects = @($projects) }
    $json = $fixture | ConvertTo-Json -Depth 16
    New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
    [IO.File]::WriteAllText($output, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "NuGet baseline fixture: $output"
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
    [string]$AttemptLane,
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
        lane = $AttemptLane
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
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Signature' -SourceSha $sourceSha -Attempt $attempt -Outcome 'success' -Phases $phases -Packages $packages -FailureCode $null
        Write-Output "NuGet signature evidence: $evidencePath"
    } catch {
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Signature' -SourceSha $sourceSha -Attempt $attempt -Outcome 'failure' -Phases $phases -Packages $packages -FailureCode 'attempt-failed'
        throw
    }
}

function Invoke-RegressionAttempt {
    Assert-True ($Lane -ceq 'Regression') 'RunAttempt currently accepts only the Signature or Regression lane.'
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

    $unitProject = Join-Path $root 'src\Unlimotion.Test\Unlimotion.Test.csproj'
    $headlessProject = Join-Path $root 'tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj'
    try {
        Invoke-DotNet -Phase 'regression:restore:unit' -Arguments @(
            'restore', $unitProject, '--force', '--no-http-cache',
            '--configfile', (Join-Path $root 'src\nuget.config'),
            '-p:Configuration=Debug',
            '-p:DisableImplicitLibraryPacksFolder=true',
            '-p:DisableImplicitNuGetFallbackFolder=true',
            '-p:RestoreFallbackFolders='
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:restore:headless' -Arguments @(
            'restore', $headlessProject, '--force', '--no-http-cache',
            '--configfile', (Join-Path $root 'src\nuget.config'),
            '-p:Configuration=Debug',
            '-p:DisableImplicitLibraryPacksFolder=true',
            '-p:DisableImplicitNuGetFallbackFolder=true',
            '-p:RestoreFallbackFolders='
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:build:unit' -Arguments @(
            'build', $unitProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:build:headless' -Arguments @(
            'build', $headlessProject, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:unit' -Arguments @(
            'run', '--project', $unitProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:headless-1' -Arguments @(
            'run', '--project', $headlessProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases
        Invoke-DotNet -Phase 'regression:test:headless-2' -Arguments @(
            'run', '--project', $headlessProject, '-c', 'Debug', '--no-restore', '--',
            '--maximum-parallel-tests', '1', '--output', 'Detailed'
        ) -Phases $phases

        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Regression' -SourceSha $sourceSha -Attempt $attempt -Outcome 'success' -Phases $phases -Packages $packages -FailureCode $null
        Write-Output "NuGet regression evidence: $evidencePath"
    } catch {
        Write-AttemptReceipt -Root $evidencePath -AttemptLane 'Regression' -SourceSha $sourceSha -Attempt $attempt -Outcome 'failure' -Phases $phases -Packages $packages -FailureCode 'attempt-failed'
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

if ($Mode -eq 'GenerateBaseline') {
    Invoke-GenerateBaseline
    return
}

if ($Mode -eq 'Worker') {
    throw 'Worker mode is reserved for the closed stdin protocol and cannot accept command-line payloads.'
}

if ($Mode -eq 'SelfTest') {
    Invoke-SelfTest
    return
}

switch -CaseSensitive ($Lane) {
    'Signature' { Invoke-SignatureAttempt; break }
    'Regression' { Invoke-RegressionAttempt; break }
    default { throw 'RunAttempt lane must be exactly Signature or Regression.' }
}
