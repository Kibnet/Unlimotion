[CmdletBinding()]
param(
    [ValidateSet('RunAttempt', 'SelfTest')]
    [string]$Mode = 'RunAttempt',
    [ValidateSet('Signature', 'Regression', 'Full')]
    [string]$Lane = 'Signature',
    [string]$RepositoryRoot = $PSScriptRoot + '\..',
    [string]$PackagesRoot
)

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-ExpectedPackages {
    @(
        @{ Id = 'ReactiveUI.Avalonia'; Version = '12.0.2' },
        @{ Id = 'ReactiveUI'; Version = '23.2.28' },
        @{ Id = 'Splat'; Version = '19.4.1' },
        @{ Id = 'Splat.Builder'; Version = '19.4.1' },
        @{ Id = 'Splat.Core'; Version = '19.4.1' },
        @{ Id = 'Splat.Logging'; Version = '19.4.1' }
    )
}

function Invoke-SignatureAttempt {
    Assert-True ($env:DOTNET_NUGET_SIGNATURE_VERIFICATION -ceq 'true') 'DOTNET_NUGET_SIGNATURE_VERIFICATION must be exactly true.'
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'src\Directory.Packages.props')) 'RepositoryRoot does not contain Directory.Packages.props.'

    if ([string]::IsNullOrWhiteSpace($PackagesRoot)) {
        $PackagesRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-nuget-' + [Guid]::NewGuid().ToString('N'))
    }
    New-Item -ItemType Directory -Force -Path $PackagesRoot | Out-Null
    $env:NUGET_PACKAGES = [IO.Path]::GetFullPath($PackagesRoot)

    $projects = @(
        'tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj',
        'src\Unlimotion.Desktop\Unlimotion.Desktop.csproj',
        'src\Unlimotion.Desktop\Unlimotion.Desktop.ForDebianBuild.csproj'
    )
    foreach ($project in $projects) {
        & dotnet restore (Join-Path $root $project) --force --no-http-cache --configfile (Join-Path $root 'src\nuget.config')
        Assert-True ($LASTEXITCODE -eq 0) "Restore failed: $project"
    }

    foreach ($package in Get-ExpectedPackages) {
        $nupkg = Join-Path $env:NUGET_PACKAGES ("{0}\{1}\{0}.{1}.nupkg" -f $package.Id.ToLowerInvariant(), $package.Version)
        Assert-True (Test-Path -LiteralPath $nupkg) "Expected package is absent: $($package.Id) $($package.Version)"
        & dotnet nuget verify $nupkg --all
        Assert-True ($LASTEXITCODE -eq 0) "Signature verification failed: $($package.Id) $($package.Version)"
    }
}

if ($Mode -eq 'SelfTest') {
    Assert-True ((Get-ExpectedPackages).Count -eq 6) 'Expected signed subset changed.'
    return
}

Invoke-SignatureAttempt
