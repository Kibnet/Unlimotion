[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('restore','build','test')][string]$Stage,
    [Parameter(Mandatory)][ValidateSet('main','headless')][string]$Project,
    [Parameter(Mandatory)][string]$ResultsRoot,
    [string]$DotnetCommand = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$projectPath = if ($Project -eq 'main') { 'src/Unlimotion.Test/Unlimotion.Test.csproj' } else { 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj' }
$directory = [IO.Path]::GetFullPath((Join-Path $ResultsRoot $Project))
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$manifestPath = Join-Path $directory 'invocation-test.json'
if (Test-Path -LiteralPath $manifestPath) { throw 'Test invocation already exists; use a fresh results directory' }
$arguments = switch ($Stage) {
    restore { @('restore', $projectPath) }
    build { @('build', $projectPath, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false') }
    # The SDK can interpret a bare existing directory as a positional test root,
    # even after --. Keep the result path attached to its MTP option.
    test { @('test', '--project', $projectPath, '-c', 'Debug', '--no-build', '--no-restore', '--', '--maximum-parallel-tests', '1', '--minimum-expected-tests', '1', '--output', 'Detailed', '--report-trx', "--results-directory=$directory") }
}
$started = [DateTimeOffset]::UtcNow
$watch = [Diagnostics.Stopwatch]::StartNew()
$code = 1
$failure = $null
$invocation = $null
try {
    if ($Stage -eq 'test') {
        . (Join-Path $PSScriptRoot 'TestInvocationMetadata.ps1')
        $invocation = New-TestInvocationMetadata $Project $arguments
        $invocation | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        $env:UNLIMOTION_TEST_TRACE_DIRECTORY = $directory
    }
    & $DotnetCommand @arguments
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 1 }
} catch { $failure = $_.Exception.Message }
finally {
    $watch.Stop()
    try { [ordered]@{
        stage=$Stage; project=$Project; command=$DotnetCommand; arguments=$arguments
        invocationId=$invocation.invocationId
        exitCode=$code; outcome=$(if ($code -eq 0) {'success'} else {'failure'})
        startedUtc=$started.ToString('o'); finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        durationMs=$watch.Elapsed.TotalMilliseconds; error=$failure
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directory "stage-$Stage.json") -Encoding utf8
    } catch {
        Write-Error "Could not save stage metadata: $($_.Exception.Message)" -ErrorAction Continue
        if ($code -eq 0) { $code=1 }
        # A secondary reporting failure must not replace the native failure code.
    }
}
exit $code
