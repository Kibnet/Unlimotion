$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-report-contract-' + [Guid]::NewGuid().ToString('N'))
$reporter = Join-Path $PSScriptRoot 'Write-TestReport.ps1'
New-Item -ItemType Directory -Path $root | Out-Null
function Assert-True($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Write-Fixture([string]$Name, [string]$Body, [switch]$NoHtml) {
    $directory = Join-Path $root "$Name/main"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $Body | Set-Content -LiteralPath (Join-Path $directory 'result.trx')
    if (!$NoHtml) { '<html>synthetic report</html>' | Set-Content -LiteralPath (Join-Path $directory 'test-report.html') }
    [ordered]@{schemaVersion=1; invocationId=$Name; project='main'; arguments=@('test','--results-directory',$directory); treeSha='fixture-tree'; checkoutSha='fixture-sha'; worktreeDirty=$false; environment=[ordered]@{sdk='fixture-sdk'; tunit='fixture-tunit'}; runId=$Name; runAttempt=1} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $directory 'invocation-test.json')
    [ordered]@{stage='test'; exitCode=$(if ($Body.Contains('outcome="Failed"')) {2} else {0}); invocationId=$Name; finishedUtc='2026-08-31T00:00:00Z'} | ConvertTo-Json | Set-Content (Join-Path $directory 'stage-test.json')
}
function Invoke-Report([string]$Name, [int]$Expected, [string]$Outcome='success', [string]$History='') {
    $arguments=@('-NoProfile','-File',$reporter,'-ResultsRoot',(Join-Path $root $Name),'-OutputRoot',(Join-Path $root "$Name-out"),'-Projects','main','-PipelineOutcome',$Outcome)
    if ($History) {$arguments += @('-HistoryRoot',$History)}
    & pwsh @arguments *> (Join-Path $root "$Name.log")
    Assert-True ($LASTEXITCODE -eq $Expected) "$Name exit=$LASTEXITCODE expected=$Expected; see $root"
}
$trx = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions><UnitTest id="a"><TestMethod className="Example.Cases" name="Theory"/></UnitTest><UnitTest id="b"><TestMethod className="Example.Cases" name="Theory"/></UnitTest></TestDefinitions><Results><UnitTestResult testId="a" executionId="one" testName="Theory(1) &amp; | &lt;script&gt;" duration="00:01:02.500" outcome="Passed"/><UnitTestResult testId="b" executionId="two" testName="Theory(2)" duration="00:00:00.250" outcome="Failed"><Output><ErrorInfo><Message>synthetic | failure</Message></ErrorInfo></Output></UnitTestResult></Results><ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="1" failed="1"/></ResultSummary></TestRun>
'@
Write-Fixture 'mixed' $trx
@(
    @{kind='test-lifecycle'; outcome='started'; testExecutionId='context-one'; testName='Theory(1) & | <script>'; details=@{className='Example.Cases'}},
    @{kind='phase'; name='sample/setup'; testExecutionId='context-one'; durationMs=7.5; outcome='completed'}
) | ForEach-Object {$_ | ConvertTo-Json -Compress} | Set-Content (Join-Path $root 'mixed/main/diagnostics-fixture.jsonl')
Invoke-Report 'mixed' 0
$tests = @(Get-Content -Raw (Join-Path $root 'mixed-out/tests.json') | ConvertFrom-Json)
Assert-True ($tests.Count -eq 2 -and $tests[0].durationMs -eq 62500) 'Duration/unit conversion lost'
Assert-True ($tests[0].identity -ne $tests[1].identity) 'Argument variants merged'
Assert-True ($tests[0].phases[0].durationMs -eq 7.5 -and $tests[0].diagnosticTestExecutionId -eq 'context-one') 'Phase join confused TRX/context execution IDs'
$summary = Get-Content -Raw (Join-Path $root 'mixed-out/summary.md')
Assert-True ($summary.Contains('&#124;') -and !$summary.Contains('<script>')) 'Markdown injection not escaped'
Write-Fixture 'collision' ($trx.Replace('Theory(2)','Theory(1) &amp; | &lt;script&gt;'))
Invoke-Report 'collision' 0
$collisions = @(Get-Content -Raw (Join-Path $root 'collision-out/tests.json') | ConvertFrom-Json)
Assert-True (@($collisions | Where-Object identityStatus -eq 'ambiguous').Count -eq 2) 'Colliding identities silently merged'
Write-Fixture 'no-html' $trx -NoHtml
Invoke-Report 'no-html' 1
Write-Fixture 'malformed' '<broken'
Invoke-Report 'malformed' 1
Write-Fixture 'xxe' '<!DOCTYPE x [<!ENTITY e SYSTEM "file:///nonexistent">]><TestRun>&e;</TestRun>'
Invoke-Report 'xxe' 1
$failureDirectory = Join-Path $root 'build-failure/main'
New-Item -ItemType Directory -Force -Path $failureDirectory | Out-Null
'{"stage":"build","exitCode":1,"outcome":"failure"}' | Set-Content (Join-Path $failureDirectory 'stage-build.json')
Invoke-Report 'build-failure' 0 'failure'
$run = Get-Content -Raw (Join-Path $root 'build-failure-out/run.json') | ConvertFrom-Json
Assert-True ($run.projects[0].status -eq 'build-failure' -and $run.projects[0].observed -eq 0) 'Build failure became test pass/failure'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'cancelled/main') | Out-Null
Invoke-Report 'cancelled' 0 'cancelled'
$run = Get-Content -Raw (Join-Path $root 'cancelled-out/run.json') | ConvertFrom-Json
Assert-True ($run.projects[0].status -eq 'cancelled' -and !$run.projects[0].telemetryComplete) 'Cancelled run reported complete'
Write-Fixture 'history' $trx
Invoke-Report 'history' 0 'success' (Join-Path $root 'mixed-out')
$history = Get-Content -Raw (Join-Path $root 'history-out/summary.md')
Assert-True ($history.Contains('История: 1 логических') -and $history.Contains('1 / 1')) 'History counted artifact copies as executions'
Write-Fixture 'partial-cancel' $trx
Remove-Item -LiteralPath (Join-Path $root 'partial-cancel/main/stage-test.json')
Invoke-Report 'partial-cancel' 0 'cancelled'
$run = Get-Content -Raw (Join-Path $root 'partial-cancel-out/run.json') | ConvertFrom-Json
Assert-True ($run.projects[0].status -eq 'cancelled' -and !$run.projects[0].telemetryComplete -and $run.projects[0].observed -eq 2) 'Partial cancelled report became complete'
Write-Fixture 'partial-counters' ($trx.Replace('total="2"','total="3"'))
Invoke-Report 'partial-counters' 1
$allPassed = $trx.Replace('outcome="Failed"','outcome="Passed"').Replace('passed="1" failed="1"','passed="2" failed="0"')
Write-Fixture 'crash' $allPassed
'{"stage":"test","exitCode":1,"invocationId":"crash","finishedUtc":"2026-08-31T00:00:00Z"}' | Set-Content (Join-Path $root 'crash/main/stage-test.json')
Invoke-Report 'crash' 0 'failure'
$run = Get-Content -Raw (Join-Path $root 'crash-out/run.json') | ConvertFrom-Json
Assert-True ($run.projects[0].status -eq 'runner-failure' -and !$run.projects[0].telemetryComplete) 'Runner crash became assertion failure'
Write-Fixture 'crash-with-assertion' $trx
'{"stage":"test","exitCode":1,"invocationId":"crash-with-assertion","finishedUtc":"2026-08-31T00:00:00Z"}' | Set-Content (Join-Path $root 'crash-with-assertion/main/stage-test.json')
Invoke-Report 'crash-with-assertion' 0 'failure'
$run = Get-Content -Raw (Join-Path $root 'crash-with-assertion-out/run.json') | ConvertFrom-Json
Assert-True ($run.projects[0].failed -eq 1 -and $run.projects[0].status -eq 'runner-failure' -and !$run.projects[0].telemetryComplete -and !$run.historyComparable) 'Assertion failure hid a runner crash or made it comparable'
$fingerprints = @()
foreach ($index in 1..4) {
    Invoke-Report 'mixed' 0
    $run = Get-Content -Raw (Join-Path $root 'mixed-out/run.json') | ConvertFrom-Json
    $fingerprints += $run.argumentFingerprint
    Assert-True ($run.treeSha -eq 'fixture-tree' -and $run.environment.sdk -eq 'fixture-sdk' -and $run.historyComparable) 'Analyzer environment replaced immutable execution metadata'
}
Assert-True (@($fingerprints | Select-Object -Unique).Count -eq 1) 'Fingerprint changed across processes'
Write-Fixture 'attached-result-option' $trx
$manifestPath=Join-Path $root 'attached-result-option/main/invocation-test.json'
$manifest=Get-Content -Raw $manifestPath | ConvertFrom-Json -AsHashtable
$manifest.arguments=@('test',"--results-directory=$(Join-Path $root 'attached-result-option/main')")
$manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath
Invoke-Report 'attached-result-option' 0
$attached=Get-Content -Raw (Join-Path $root 'attached-result-option-out/run.json') | ConvertFrom-Json
Assert-True ($attached.argumentFingerprint -eq $fingerprints[0]) 'Attached result path changed comparable arguments'
Write-Fixture 'missing-manifest' $trx
Remove-Item -LiteralPath (Join-Path $root 'missing-manifest/main/invocation-test.json')
Invoke-Report 'missing-manifest' 1
$run = Get-Content -Raw (Join-Path $root 'missing-manifest-out/run.json') | ConvertFrom-Json
Assert-True (!$run.historyComparable -and !$run.treeSha -and !$run.environment) 'Missing provenance invented current source/environment'
foreach ($missingField in @('invocationId','arguments')) {
    $fixtureName="missing-$missingField"
    Write-Fixture $fixtureName $trx
    $manifestPath=Join-Path $root "$fixtureName/main/invocation-test.json"
    $manifest=Get-Content -Raw $manifestPath | ConvertFrom-Json -AsHashtable
    $manifest.Remove($missingField)
    $manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath
    if($missingField -eq 'invocationId') {
        $stagePath=Join-Path $root "$fixtureName/main/stage-test.json"
        $stage=Get-Content -Raw $stagePath | ConvertFrom-Json -AsHashtable
        $stage.Remove('invocationId')
        $stage | ConvertTo-Json -Depth 5 | Set-Content $stagePath
    }
    Invoke-Report $fixtureName 1
    $run=Get-Content -Raw (Join-Path $root "$fixtureName-out/run.json") | ConvertFrom-Json
    Assert-True (!$run.projects[0].telemetryComplete -and !$run.historyComparable) "Missing $missingField became complete/comparable"
}
Write-Fixture 'missing-execution-id' ($trx.Replace('executionId="one"',''))
Invoke-Report 'missing-execution-id' 0
$missingId = @(Get-Content -Raw (Join-Path $root 'missing-execution-id-out/tests.json') | ConvertFrom-Json)
Assert-True ($missingId[0].identityStatus -eq 'ambiguous') 'Missing execution identity entered history dedup'
Write-Fixture 'markdown-link' ($trx.Replace('synthetic | failure','![external](https://example.invalid/image)'))
Invoke-Report 'markdown-link' 0
$escaped = Get-Content -Raw (Join-Path $root 'markdown-link-out/summary.md')
Assert-True (!$escaped.Contains('![external]') -and $escaped.Contains('&#91;external&#93;')) 'Report text injected Markdown image'
Write-Fixture 'partial-trace' $trx
'{"kind":' | Set-Content (Join-Path $root 'partial-trace/main/diagnostics-truncated.jsonl')
Invoke-Report 'partial-trace' 1 'failure'
$partial = Get-Content -Raw (Join-Path $root 'partial-trace-out/run.json') | ConvertFrom-Json
Assert-True (!$partial.projects[0].telemetryComplete) 'Failed pipeline hid incomplete diagnostic trace'
foreach ($metadataCase in @('stage','manifest')) {
    $fixtureName = "malformed-$metadataCase-multi-project"
    Write-Fixture $fixtureName $allPassed
    $mainDirectory = Join-Path $root "$fixtureName/main"
    $headlessDirectory = Join-Path $root "$fixtureName/headless"
    Copy-Item -LiteralPath $mainDirectory -Destination $headlessDirectory -Recurse
    $headlessManifestPath = Join-Path $headlessDirectory 'invocation-test.json'
    $headlessManifest = Get-Content -Raw -LiteralPath $headlessManifestPath | ConvertFrom-Json -AsHashtable
    $headlessManifest.project = 'headless'
    $headlessManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $headlessManifestPath
    $corruptedPath = if ($metadataCase -eq 'stage') {Join-Path $mainDirectory 'stage-test.json'} else {Join-Path $mainDirectory 'invocation-test.json'}
    '{"truncated":' | Set-Content -LiteralPath $corruptedPath
    & pwsh -NoProfile -File $reporter -ResultsRoot (Join-Path $root $fixtureName) -OutputRoot (Join-Path $root "$fixtureName-out") -PipelineOutcome failure *> (Join-Path $root "$fixtureName.log")
    Assert-True ($LASTEXITCODE -eq 1) "$fixtureName did not report damaged metadata"
    $run = Get-Content -Raw -LiteralPath (Join-Path $root "$fixtureName-out/run.json") | ConvertFrom-Json
    $main = $run.projects | Where-Object project -eq 'main'
    $headless = $run.projects | Where-Object project -eq 'headless'
    Assert-True ($main.status -eq 'incomplete' -and !$main.telemetryComplete) "$fixtureName did not mark main incomplete"
    Assert-True ($headless.status -eq 'passed' -and $headless.telemetryComplete) "$fixtureName damaged the healthy project report"
    Assert-True (Test-Path -LiteralPath (Join-Path $root "$fixtureName-out/headless/tests.json")) "$fixtureName lost the healthy project tests"
    Assert-True (Test-Path -LiteralPath (Join-Path $root "$fixtureName-out/summary.md")) "$fixtureName lost the combined summary"
}
Write-Output "PASS: reporter contracts, including phase identity join and multi-process fingerprints; fixtures/logs: $root"
