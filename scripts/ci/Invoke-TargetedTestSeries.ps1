[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$TreeNodeFilter,
    [Parameter(Mandatory)][ValidateRange(1,100)][int]$Repeat,
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateRange(1,10000)][int]$ExpectedTests = 1,
    [string]$Timeout = '5m',
    [string]$DotnetCommand = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (Test-Path -LiteralPath $OutputRoot) { throw 'OutputRoot already exists; choose a fresh directory to avoid mixing runs' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
for ($iteration=1; $iteration -le $Repeat; $iteration++) {
    $directory = [IO.Path]::GetFullPath((Join-Path $OutputRoot ('{0:D3}' -f $iteration)))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $env:UNLIMOTION_TEST_TRACE_DIRECTORY = $directory
    & $DotnetCommand test --project $Project -c Debug --no-build --no-restore -- --treenode-filter $TreeNodeFilter --minimum-expected-tests $ExpectedTests --maximum-parallel-tests 1 --output Detailed --report-trx --results-directory $directory --timeout $Timeout *> "$directory.log"
    $code = $LASTEXITCODE
    $watch.Stop()
    $count = 0
    $passed = 0; $failed = 0; $skipped = 0; $parseError = $null
    try {
    foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '*.trx' -File -ErrorAction SilentlyContinue)) {
        $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create($file.FullName, $settings)
        try {
            $xml=[Xml.XmlDocument]::new(); $xml.XmlResolver=$null; $xml.Load($reader)
            foreach ($result in $xml.SelectNodes('//*[local-name()="UnitTestResult"]')) {
                $count++
                switch ($result.GetAttribute('outcome')) { Passed {$passed++}; Failed {$failed++}; default {$skipped++} }
            }
        } finally {$reader.Dispose()}
    }
    } catch { $parseError=$_.Exception.Message }
    $result = [ordered]@{iteration=$iteration; project=$Project; filter=$TreeNodeFilter; exitCode=$code; discovered=$count; executed=($passed+$failed); passed=$passed; failed=$failed; skipped=$skipped; parseError=$parseError; expected=$ExpectedTests; wallSeconds=$watch.Elapsed.TotalSeconds; utc=[DateTimeOffset]::UtcNow.ToString('o'); log="$directory.log"}
    $json = $result | ConvertTo-Json -Compress
    Add-Content -LiteralPath (Join-Path $OutputRoot 'series.jsonl') -Value $json -Encoding utf8
    Write-Output $json
    if ($null -eq $code -or $code -ne 0 -or $count -ne $ExpectedTests -or $passed -ne $ExpectedTests -or $parseError) { throw "Series stopped: exit=$code, discovered=$count, passed=$passed; inspect $directory.log" }
}
