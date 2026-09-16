param(
    [Parameter(Mandatory)][string]$Dataset,
    [Parameter(Mandatory)][string]$BaselineExe,
    [Parameter(Mandatory)][string]$CandidateExe,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 20)][int]$Runs = 5,
    [switch]$DisableCandidateTieredCompilation
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$datasetPath = (Resolve-Path -LiteralPath $Dataset).Path
$baselinePath = (Resolve-Path -LiteralPath $BaselineExe).Path
$candidatePath = (Resolve-Path -LiteralPath $CandidateExe).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a new output directory to keep measurement series separate.' }
New-Item -ItemType Directory -Path $outputPath | Out-Null
$envNames = @('UNLIMOTION_LOADING_DATASET', 'UNLIMOTION_LOADING_EXE', 'UNLIMOTION_LOADING_LABEL', 'UNLIMOTION_LOADING_REPORT', 'DOTNET_TieredCompilation')
$previous = @{}
foreach ($name in $envNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $repo
try {
    # Build the harness beforehand. Builds, tracing and other test suites must not run during this series.
    # Copying and SHA256 verification are outside the launch timer and warm the file cache in both variants.
    $env:UNLIMOTION_LOADING_DATASET = $datasetPath
    for ($iteration = 0; $iteration -le $Runs; $iteration++) {
        $variants = if ($iteration % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($variant in $variants) {
            $label = if ($iteration -eq 0) { "$variant-warmup" } else { "$variant-$iteration" }
            $env:UNLIMOTION_LOADING_EXE = if ($variant -eq 'baseline') { $baselinePath } else { $candidatePath }
            $env:DOTNET_TieredCompilation = if ($DisableCandidateTieredCompilation -and $variant -eq 'candidate') { '0' } else { $previous['DOTNET_TieredCompilation'] }
            $env:UNLIMOTION_LOADING_LABEL = $label
            $env:UNLIMOTION_LOADING_REPORT = Join-Path $outputPath $(if ($iteration -eq 0) { 'warmup.jsonl' } else { 'measurements.jsonl' })
            $log = Join-Path $outputPath "$label.log"
            $results = Join-Path $outputPath $label
            Write-Host "Running $label"
            & dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Release --no-build -- --treenode-filter '/*/*/TaskLoadingPerformanceFlaUiTests/*' --maximum-parallel-tests 1 --output Normal --results-directory $results *> $log
            if ($LASTEXITCODE -ne 0) { throw "UI run failed: $log" }
        }
    }
    Write-Host "Measurements: $(Join-Path $outputPath 'measurements.jsonl')"
}
finally {
    Pop-Location
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
