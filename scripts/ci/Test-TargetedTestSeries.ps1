$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-series-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$stub = Join-Path $root 'dotnet-stub.ps1'
@'
$directory=$args[[Array]::IndexOf($args,'--results-directory')+1]
New-Item -ItemType Directory -Force -Path $directory | Out-Null
'<TestRun><Results><UnitTestResult outcome="{0}"/></Results></TestRun>' -f $env:TEST_SERIES_OUTCOME | Set-Content (Join-Path $directory 'result.trx')
exit 0
'@ | Set-Content -LiteralPath $stub
try {
    foreach ($outcome in @('Passed','NotExecuted','Failed')) {
        $env:TEST_SERIES_OUTCOME=$outcome
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-TargetedTestSeries.ps1') -Project synthetic -TreeNodeFilter synthetic -Repeat 2 -ExpectedTests 1 -OutputRoot (Join-Path $root $outcome) -DotnetCommand $stub *> (Join-Path $root "$outcome.log")
        $code=$LASTEXITCODE
        $rows=@(Get-Content (Join-Path $root "$outcome/series.jsonl") | ConvertFrom-Json)
        if ($outcome -eq 'Passed') { if ($code -ne 0 -or $rows.Count -ne 2 -or $rows[1].passed -ne 1) {throw 'Passing series rejected'} }
        elseif ($code -eq 0 -or $rows.Count -ne 1 -or $rows[0].passed -ne 0) {throw 'Skipped/failed series accepted or repeated'}
    }
    "PASS: 3 targeted series contracts; $root"
} finally {Remove-Item Env:TEST_SERIES_OUTCOME -ErrorAction SilentlyContinue}
