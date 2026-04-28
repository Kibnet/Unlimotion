param(
    [switch]$SkipTests,
    [switch]$NoBuild,
    [string]$OutputRoot = "artifacts/readme-media/latest",
    [string]$Languages = "en,ru"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Command
    )

    & $Command[0] $Command[1..($Command.Length - 1)]
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $($Command -join ' ')"
    }
}

if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    $resolvedOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/readme-media"))
if (-not $resolvedOutputRoot.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must stay inside $artifactsRoot"
}

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    Remove-Item -LiteralPath $resolvedOutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedOutputRoot | Out-Null

if (-not $NoBuild) {
    Invoke-Step @("dotnet", "build", "tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj", "-c", "Debug", "/p:UseSharedCompilation=false")
    Invoke-Step @("dotnet", "build", "tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj", "-c", "Debug", "/p:UseSharedCompilation=false")
    Invoke-Step @("dotnet", "build", "tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj", "-c", "Debug", "/p:UseSharedCompilation=false")
}

if (-not $SkipTests) {
    Invoke-Step @("dotnet", "test", "tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj", "-c", "Debug", "--no-build", "--", "--maximum-parallel-tests", "1", "--output", "Detailed")
    Invoke-Step @("dotnet", "test", "tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj", "-c", "Debug", "--no-build", "--", "--maximum-parallel-tests", "1", "--output", "Detailed")
}

$runArgs = @(
    "run",
    "--project",
    "tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj",
    "-c",
    "Debug",
    "--no-build"
)

$runArgs += "--"
$runArgs += "--copy-to-media"
$runArgs += "--no-build-before-launch"
$runArgs += "--languages"
$runArgs += $Languages

if ($OutputRoot) {
    $runArgs += "--output-root"
    $runArgs += $resolvedOutputRoot
}

Invoke-Step ([string[]]("dotnet") + $runArgs)
