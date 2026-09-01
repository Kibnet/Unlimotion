$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-orchestration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$stageScript = Join-Path $PSScriptRoot 'Invoke-TestStage.ps1'
$stub = Join-Path $root 'dotnet-stub.ps1'
@'
$stage=$args[0]
$project=if(($args -join ' ').Contains('UiTests.Headless')){'headless'}else{'main'}
Add-Content -LiteralPath $env:TEST_STUB_LOG -Value "$stage/$project"
if ($env:TEST_STUB_BREAK_METADATA -eq '1') {
    $directory=(@($args | Where-Object {$_ -like '--results-directory=*'})[0] -replace '^--results-directory=','')
    New-Item -ItemType Directory -Path (Join-Path $directory 'stage-test.json') | Out-Null
    exit 8
}
if ("$stage/$project" -eq $env:TEST_STUB_FAIL) { exit ([int]$env:TEST_STUB_EXIT) }
exit 0
'@ | Set-Content -LiteralPath $stub
try {
    foreach ($failure in @('none','test/main','build/headless','restore/main','test/headless')) {
        $env:TEST_STUB_FAIL = $failure
        $env:TEST_STUB_EXIT = if ($failure -eq 'test/headless') {'8'} else {'1'}
        $env:TEST_STUB_LOG = Join-Path $root ($failure.Replace('/','-')+'.log')
        $outcome=@{}
        foreach ($stage in @('restore','build','test')) { foreach ($project in @('main','headless')) {
            $prerequisite = switch ($stage) {build {"restore/$project"}; test {"build/$project"}; default {''}}
            if ($prerequisite -and $outcome[$prerequisite] -ne 0) {continue}
            & pwsh -NoProfile -File $stageScript -Stage $stage -Project $project -ResultsRoot (Join-Path $root $failure.Replace('/','-')) -DotnetCommand $stub *> $null
            $outcome["$stage/$project"]=$LASTEXITCODE
        } }
        $calls=@(Get-Content -LiteralPath $env:TEST_STUB_LOG)
        if ($failure -eq 'test/main' -and $calls[-1] -ne 'test/headless') {throw 'Main failure skipped Headless'}
        if ($failure -eq 'build/headless' -and ($calls -notcontains 'test/main' -or $calls -contains 'test/headless')) {throw 'Build prerequisites violated'}
        if ($failure -eq 'restore/main' -and ($calls -contains 'build/main' -or $calls -notcontains 'test/headless')) {throw 'Restore prerequisites violated'}
        if ($failure -ne 'none' -and $outcome[$failure] -eq 0) {throw 'Native failure swallowed'}
        if ($failure -eq 'test/headless' -and $outcome[$failure] -ne 8) {throw 'Zero-discovery code lost'}
    }
    $existing = Join-Path $root 'none/main'
    $before = @('invocation-test.json','stage-test.json') | ForEach-Object { (Get-FileHash (Join-Path $existing $_)).Hash }
    & pwsh -NoProfile -File $stageScript -Stage test -Project main -ResultsRoot (Join-Path $root 'none') -DotnetCommand $stub *> $null
    if ($LASTEXITCODE -eq 0) {throw 'Repeated invocation accepted'}
    $after = @('invocation-test.json','stage-test.json') | ForEach-Object { (Get-FileHash (Join-Path $existing $_)).Hash }
    if (($before -join ',') -ne ($after -join ',')) {throw 'Rejected invocation altered prior evidence'}
    $env:TEST_STUB_BREAK_METADATA='1'
    & pwsh -NoProfile -File $stageScript -Stage test -Project main -ResultsRoot (Join-Path $root 'metadata-failure') -DotnetCommand $stub *> $null
    if ($LASTEXITCODE -ne 8) {throw 'Secondary metadata failure replaced native exit code'}
    Remove-Item Env:TEST_STUB_BREAK_METADATA
    $workflow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../../.github/workflows/tests.yml')
    if ($workflow -match 'continue-on-error|pull_request_target' -or $workflow -notmatch 'name: All tests' -or $workflow -notmatch 'contents: read') {throw 'Workflow gate/security contract changed'}
    foreach ($project in @('main','headless')) {
        $expected="!cancelled() && steps.build-$project.outcome == 'success'"
        if (!$workflow.Contains($expected)) {throw "Test step own prerequisite missing: $project"}
    }
    if ($workflow.IndexOf('name: Build Headless Tests') -gt $workflow.IndexOf('name: Run Unlimotion Tests')) {throw 'Headless build occurs too late'}
    $invocation=Get-Content -Raw (Join-Path $existing 'invocation-test.json') | ConvertFrom-Json
    if(@($invocation.arguments | Where-Object {$_ -like '--results-directory=*'}).Count -ne 1 -or $invocation.arguments -contains '--results-directory'){throw 'Existing result path must use the attached option form'}
    Write-Output "PASS: 5 native failure-path cases, immutable rerun rejection and workflow contracts; evidence: $root"
} finally {
    Remove-Item Env:TEST_STUB_LOG,Env:TEST_STUB_FAIL,Env:TEST_STUB_EXIT,Env:TEST_STUB_BREAK_METADATA -ErrorAction SilentlyContinue
}
