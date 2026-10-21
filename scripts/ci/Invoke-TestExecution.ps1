<#
.SYNOPSIS
Runs all built Debug tests with two isolated main lanes, then Headless and the exclusive benchmark.
.EXAMPLE
./scripts/ci/Invoke-TestExecution.ps1 -ResultsRoot artifacts/test-results/local-001
Build both projects first. Use a fresh results directory. Ordinary dotnet test remains the serial fallback.
Importance diagnostics retain their configured external root, or default to ResultsRoot.importance.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsRoot,
    [ValidateNotNullOrEmpty()][ValidateSet('main','headless')][string[]]$Projects = @('main','headless'),
    [ValidateSet(1,2)][int]$Workers = 2,
    [string[]]$Classes = @(),
    [ValidateRange(1,7200)][int]$InvocationTimeoutSeconds = 2400,
    [string]$CancellationFile,
    [switch]$KeepWorkingCopies,
    [Parameter(DontShow)][string]$DotnetCommand = 'dotnet',
    [Parameter(DontShow)][string]$MainOutput,
    [Parameter(DontShow)][string]$HeadlessOutput
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot 'TestExecutionPlan.ps1')
. (Join-Path $PSScriptRoot 'TestInvocationMetadata.ps1')
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ResultsRoot=[IO.Path]::GetFullPath($ResultsRoot)
$workingRoot=Join-Path $repository ('artifacts/test-workers/'+[Guid]::NewGuid().ToString('N'))
$importanceArtifactsRoot=$env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY
if ([string]::IsNullOrWhiteSpace($importanceArtifactsRoot)) {
    $importanceArtifactsRoot=[IO.Path]::TrimEndingDirectorySeparator($ResultsRoot)+'.importance'
}
$importanceArtifactsRoot=[IO.Path]::GetFullPath($importanceArtifactsRoot)
$resultsPrefix=$ResultsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
if ($importanceArtifactsRoot.Equals($ResultsRoot,[StringComparison]::OrdinalIgnoreCase) -or
    $importanceArtifactsRoot.StartsWith($resultsPrefix,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Importance artifacts must stay outside ResultsRoot so rendered child TRXs do not inflate reports'
}
$owned=[Collections.Generic.List[object]]::new()
$failures=[Collections.Generic.List[string]]::new()
$metadata=@{}; $inventories=@{}
$started=[DateTimeOffset]::UtcNow; $watch=[Diagnostics.Stopwatch]::StartNew()
$buildOutputs=@{main='src/Unlimotion.Test/bin/Debug/net10.0';headless='tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0'}
foreach ($project in @('main','headless')) {$buildOutputs[$project]=Join-Path $repository $buildOutputs[$project]}
if ($MainOutput) {$buildOutputs.main=[IO.Path]::GetFullPath($MainOutput)}
if ($HeadlessOutput) {$buildOutputs.headless=[IO.Path]::GetFullPath($HeadlessOutput)}
$assemblies=@{main='Unlimotion.Test.dll';headless='Unlimotion.UiTests.Headless.dll'}

function Save-Json($Value,[string]$Path) {
    $Value | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $Path -Encoding utf8
}
function Copy-WorkerOutput([string]$Source,[string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        # Previous runs are not input. Copy all deployed dependencies, snapshots and baselines.
        if ($item.PSIsContainer -and ($item.Name -in @('artifacts','TestResults') -or $item.Name -like 'MainWindowViewModelFixture_*')) {continue}
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}
function Start-TestChild([string]$Project,[string]$Output,[string]$Directory,$Expected,[string]$ClassFilter=$null,[string]$InventoryPath=$null) {
    if ($CancellationFile -and (Test-Path -LiteralPath $CancellationFile)) {throw [OperationCanceledException]::new('Test execution cancelled')}
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $arguments=[Collections.Generic.List[string]]::new()
    $arguments.Add((Join-Path $Output $assemblies[$Project]))
    if ($InventoryPath) { $arguments.Add('--list-tests') }
    else {
        $arguments.AddRange([string[]]@('--maximum-parallel-tests','1','--minimum-expected-tests',([string]@($Expected).Count),'--output','Detailed','--report-trx',"--results-directory=$Directory"))
        if ($ClassFilter) {
            $split=$ClassFilter.LastIndexOf('.')
            $arguments.AddRange([string[]]@('--treenode-filter',"/*/$($ClassFilter.Substring(0,$split))/$($ClassFilter.Substring($split+1))/*"))
        } else {
            $arguments.Add('--filter-uid'); $arguments.AddRange([string[]]@($Expected.uid))
        }
    }
    # Leave room for executable, quoting and environment-independent Windows limits.
    if (($arguments | ForEach-Object {$_.Length+3} | Measure-Object -Sum).Sum -gt 30000) {throw 'Test invocation exceeds safe command-line length'}
    $info=[Diagnostics.ProcessStartInfo]::new($DotnetCommand)
    $info.WorkingDirectory=$Output; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach ($argument in $arguments) {$info.ArgumentList.Add($argument)}
    foreach ($name in @('UNLIMOTION_TEST_INVENTORY_PATH','UNLIMOTION_TEST_UID_DIRECTORY','UNLIMOTION_TEST_TRACE_DIRECTORY',
        'UNLIMOTION_RENDERED_TEST_CHILD','UNLIMOTION_TAB_EMOJI_RENDERED_CHILD','UNLIMOTION_IMPORTANCE_RENDERED_CHILD',
        'UNLIMOTION_DEEP_LINK_HEADLESS_CHILD','UNLIMOTION_GIT_INDICATOR_HEADLESS_CHILD','UNLIMOTION_EMOJI_HEADLESS_CHILD')) {$info.Environment.Remove($name) | Out-Null}
    if ($InventoryPath) {$info.Environment['UNLIMOTION_TEST_INVENTORY_PATH']=$InventoryPath}
    else {
        $info.Environment['UNLIMOTION_TEST_UID_DIRECTORY']=$Directory
        $info.Environment['UNLIMOTION_TEST_TRACE_DIRECTORY']=$Directory
        $info.Environment['UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY']=$importanceArtifactsRoot
        if (@($Expected).Count -eq 1 -and $Expected[0].freshProcess) {$info.Environment['UNLIMOTION_RENDERED_TEST_CHILD']='1'}
    }
    $invocation=$metadata[$Project] | ConvertTo-Json -Depth 15 | ConvertFrom-Json -AsHashtable
    $invocation.invocationId=[Guid]::NewGuid().ToString('N'); $invocation.arguments=$arguments.ToArray()
    Save-Json $invocation (Join-Path $Directory 'invocation-test.json')
    $process=[Diagnostics.Process]::Start($info)
    if (!$process) {throw 'Could not start test process'}
    $child=[pscustomobject]@{project=$Project; directory=$Directory; process=$process; processId=$process.Id
        expected=@($Expected); discovery=[bool]$InventoryPath; invocationId=$invocation.invocationId
        startedUtc=[DateTimeOffset]::UtcNow; watch=[Diagnostics.Stopwatch]::StartNew()
        stdout=$process.StandardOutput.ReadToEndAsync(); stderr=$process.StandardError.ReadToEndAsync()
        exitCode=$null; error=$null; peakWorkingSetBytes=0L; finished=$false; arguments=$arguments.ToArray()}
    $owned.Add($child)
    Write-Output "Started $Project PID=$($child.processId), cases=$(@($Expected).Count), directory=$Directory" | Out-Host
    return $child
}
function Complete-TestChild($Child) {
    if ($Child.finished) {return}
    $Child.process.WaitForExit()
    $Child.watch.Stop(); $Child.exitCode=$Child.process.ExitCode
    [IO.File]::WriteAllText((Join-Path $Child.directory 'stdout.log'),$Child.stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $Child.directory 'stderr.log'),$Child.stderr.GetAwaiter().GetResult())
    if ($Child.exitCode -ne 0) {$failures.Add("$($Child.project): PID=$($Child.processId) exit=$($Child.exitCode)")}
    if (!$Child.discovery) {
        try {Assert-TestExecutionReceipts $Child.expected $Child.directory $Child.processId $Child.exitCode}
        catch {$Child.error=$_.Exception.Message; $failures.Add("$($Child.project): $($Child.error)")}
    }
    Save-Json ([ordered]@{stage='test';project=$Child.project;invocationId=$Child.invocationId
        exitCode=$(if($Child.error){1}else{$Child.exitCode}); processId=$Child.processId
        startedUtc=$Child.startedUtc.ToString('o');finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        durationMs=$Child.watch.Elapsed.TotalMilliseconds; peakWorkingSetBytes=$Child.peakWorkingSetBytes
        error=$Child.error; arguments=$Child.arguments}) (Join-Path $Child.directory 'stage-test.json')
    $Child.finished=$true
    Write-Output "Finished $($Child.project) PID=$($Child.processId), exit=$($Child.exitCode), seconds=$([Math]::Round($Child.watch.Elapsed.TotalSeconds,2)), receiptError=$($Child.error)" | Out-Host
}
function Poll-TestChild($Child) {
    if ($CancellationFile -and (Test-Path -LiteralPath $CancellationFile)) {throw [OperationCanceledException]::new('Test execution cancelled')}
    if ($Child.finished) {return $true}
    if (!$Child.process.HasExited) {
        try {
            $Child.process.Refresh()
            $Child.peakWorkingSetBytes=[Math]::Max($Child.peakWorkingSetBytes,$Child.process.PeakWorkingSet64)
        } catch [InvalidOperationException] {if (!$Child.process.HasExited) {throw}}
        if ($Child.watch.Elapsed.TotalSeconds -gt $InvocationTimeoutSeconds) {
            $Child.process.Kill($true); $Child.error='Invocation timed out'; $failures.Add("$($Child.project): invocation timed out")
        } else {return $false}
    }
    Complete-TestChild $Child
    return $true
}
function Wait-TestChild($Child) {while (!(Poll-TestChild $Child)) {Start-Sleep -Milliseconds 200}}
function Stop-PendingTestChildren([string]$Project) {
    $pending=@($owned | Where-Object {!$_.finished -and (!$Project -or $_.project -eq $Project)})
    # Stop every owned tree before any fallible metadata I/O.
    foreach ($child in $pending) {
        if (!$child.process.HasExited) {$child.process.Kill($true)}
        $child.process.WaitForExit()
    }
    foreach ($child in $pending) {
        $child.error='Interrupted before completion'; $failures.Add("$($child.project): interrupted")
        try {Complete-TestChild $child} catch {$failures.Add("$($child.project): completion metadata failed: $($_.Exception.Message)")}
    }
}

# Reject reuse before creating/altering any project evidence.
if (@($Projects | Sort-Object -Unique).Count -ne $Projects.Count) {throw 'Duplicate project requested'}
foreach ($project in $Projects) {
    if (Test-Path -LiteralPath (Join-Path $ResultsRoot "$project/invocation-test.json")) {throw 'Test invocation already exists; use fresh results'}
}
if (Test-Path -LiteralPath (Join-Path $ResultsRoot 'execution.json')) {throw 'Execution already exists; use fresh results'}
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null
try {
    foreach ($project in $Projects) {
        try {
        $root=Join-Path $ResultsRoot $project; New-Item -ItemType Directory -Force -Path $root | Out-Null
        $entryArguments=@('pwsh','Invoke-TestExecution.ps1','-Workers',([string]$Workers),'-Projects',$project)
        if ($Classes.Count) {$entryArguments+=@('-Classes',($Classes -join ','))}
        $metadata[$project]=New-TestInvocationMetadata $project $entryArguments
        Save-Json $metadata[$project] (Join-Path $root 'invocation-test.json')
        $output=$buildOutputs[$project]
        $inventoryPath=Join-Path $ResultsRoot "inventory-$project.json"
        $discovery=Start-TestChild $project $output (Join-Path $ResultsRoot "discovery-$project") @() -InventoryPath $inventoryPath
        Wait-TestChild $discovery
        if ($discovery.exitCode -ne 0 -or !(Test-Path -LiteralPath $inventoryPath)) {throw "Discovery failed for $project"}
        $inventory=Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
        if ($inventory.schemaVersion -ne 1) {throw 'Unknown inventory schema'}
        Assert-TestInventory $inventory.tests
        $selected=@($inventory.tests)
        if ($Classes.Count) {$selected=@($selected | Where-Object className -in $Classes)}
        Assert-TestInventory $selected
        $inventories[$project]=$selected
        } catch {
            if ($_.Exception -is [OperationCanceledException]) {throw}
            $failures.Add("${project}: discovery failed: $($_.Exception.Message)")
        }
    }
    try {
    if ($inventories.ContainsKey('main')) {
        $plan=New-TestExecutionPlan $inventories.main $Workers
        Assert-TestPartition $inventories.main (@($plan.batches.tests)+@($plan.exclusive))
        Save-Json $plan (Join-Path $ResultsRoot 'plan-main.json')
        $running=@{}; $queues=@{}; $outputs=@{}
        for($lane=0;$lane -lt $Workers;$lane++) {
            $queues[$lane]=[Collections.Generic.Queue[object]]::new()
            foreach($batch in @($plan.batches | Where-Object lane -eq $lane)) {$queues[$lane].Enqueue($batch)}
            if ($queues[$lane].Count) {
                $outputs[$lane]=Join-Path $workingRoot "lane-$lane"
                Copy-WorkerOutput $buildOutputs.main $outputs[$lane]
            }
        }
        do {
            for($lane=0;$lane -lt $Workers;$lane++) {
                if ($running.ContainsKey($lane) -and (Poll-TestChild $running[$lane])) {$running.Remove($lane)}
                if (!$running.ContainsKey($lane) -and $queues[$lane].Count) {
                    $batch=$queues[$lane].Dequeue()
                    try {$running[$lane]=Start-TestChild main $outputs[$lane] (Join-Path $ResultsRoot "main/invocations/lane-$lane-batch-$($batch.batch)") $batch.tests $batch.classFilter}
                    catch {if ($_.Exception -is [OperationCanceledException]) {throw}; $failures.Add("main: launch failed: $($_.Exception.Message)")}
                }
            }
            if ($running.Count) {Start-Sleep -Milliseconds 200}
        } while ($running.Count -or @($queues.Values | Where-Object Count -gt 0).Count)
    }
    } catch {
        if ($_.Exception -is [OperationCanceledException]) {throw}
        $failures.Add("main: worker stage failed: $($_.Exception.Message)")
        Stop-PendingTestChildren main
    }
    try {
    if ($inventories.ContainsKey('headless')) {
        $headless=Start-TestChild headless $buildOutputs.headless (Join-Path $ResultsRoot 'headless/invocations/full') $inventories.headless
        Wait-TestChild $headless
    }
    } catch {
        if ($_.Exception -is [OperationCanceledException]) {throw}
        $failures.Add("headless: launch or monitoring failed: $($_.Exception.Message)")
        Stop-PendingTestChildren headless
    }
    try {
    if ($inventories.ContainsKey('main') -and $plan.exclusive.Count) {
        $exclusiveOutput=Join-Path $workingRoot 'exclusive'
        Copy-WorkerOutput $buildOutputs.main $exclusiveOutput
        $benchmark=Start-TestChild main $exclusiveOutput (Join-Path $ResultsRoot 'main/invocations/exclusive') $plan.exclusive
        Wait-TestChild $benchmark
    }
    } catch {if ($_.Exception -is [OperationCanceledException]) {throw}; $failures.Add("main: exclusive stage failed: $($_.Exception.Message)")}
} catch {$failures.Add($_.Exception.Message)}
finally {
    Stop-PendingTestChildren
    foreach ($child in $owned) {
        $child.process.Dispose()
    }
    # Some existing UI tests still capture relative to AppContext.BaseDirectory.
    # Preserve those images/logs before deleting the isolated deployment copies.
    foreach ($worker in @(Get-ChildItem -LiteralPath $workingRoot -Directory -ErrorAction SilentlyContinue)) {
        foreach ($name in @('artifacts','TestResults')) {
            $artifacts=Join-Path $worker.FullName $name
            if (Test-Path -LiteralPath $artifacts) {
                $saved=Join-Path $ResultsRoot "main/worker-artifacts/$($worker.Name)"
                New-Item -ItemType Directory -Force -Path $saved | Out-Null
                Copy-Item -LiteralPath $artifacts -Destination $saved -Recurse -Force
            }
        }
    }
    foreach ($project in $Projects) {
        if (!$metadata.ContainsKey($project)) {continue}
        $children=@($owned | Where-Object {$_.project -eq $project -and !$_.discovery})
        $projectFailures=@($failures | Where-Object {$_ -like "${project}:*"})
        $complete=$false
        if ($inventories.ContainsKey($project) -and $children.Count -gt 0 -and @($children | Where-Object {$_.error -or !$_.finished}).Count -eq 0) {
            try {Assert-TestPartition $inventories[$project] @($children.expected); $complete=$true}
            catch {$failures.Add("${project}: $($_.Exception.Message)"); $projectFailures+=@($_.Exception.Message)}
        }
        $code=if($projectFailures.Count -or !$complete){1}else{0}
        if ($complete -and @($children | Where-Object {$_.error -or $_.exitCode -notin @(0,2)}).Count -eq 0 -and
            @($children | Where-Object exitCode -eq 2).Count -gt 0) {$code=2}
        $projectStart=$started; $projectFinish=[DateTimeOffset]::UtcNow
        if ($children.Count) {
            $projectStart=($children | Sort-Object startedUtc | Select-Object -First 1).startedUtc
            $projectFinish=$children | ForEach-Object {$_.startedUtc.Add($_.watch.Elapsed)} | Sort-Object | Select-Object -Last 1
        }
        $summary=[ordered]@{stage='test';project=$project;invocationId=$metadata[$project].invocationId;exitCode=$code
            startedUtc=$projectStart.ToString('o');finishedUtc=$projectFinish.ToString('o');durationMs=($projectFinish-$projectStart).TotalMilliseconds
            outcome=$(if($code){'failure'}else{'success'});error=($failures -join '; ');arguments=$metadata[$project].arguments
            executionSchemaVersion=1; inventoryComplete=$complete; expectedCount=$(if($inventories.ContainsKey($project)) {@($inventories[$project]).Count} else {0})
            invocations=@($children | ForEach-Object {[ordered]@{directory=[IO.Path]::GetRelativePath((Join-Path $ResultsRoot $project),$_.directory);invocationId=$_.invocationId;processId=$_.processId;exitCode=$_.exitCode;error=$_.error}})}
        Save-Json $summary (Join-Path $ResultsRoot "$project/stage-test.json")
    }
    # Only the GUID root created by this invocation can be removed.
    $allowed=[IO.Path]::GetFullPath((Join-Path $repository 'artifacts/test-workers'))+[IO.Path]::DirectorySeparatorChar
    if (!$KeepWorkingCopies -and (Test-Path -LiteralPath $workingRoot)) {
        $resolved=[IO.Path]::GetFullPath($workingRoot)
        if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe working-copy cleanup path'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $watch.Stop()
    Save-Json ([ordered]@{schemaVersion=1;workers=$Workers;startedUtc=$started.ToString('o');finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        durationMs=$watch.Elapsed.TotalMilliseconds; errors=$failures.ToArray();workingRoot=$workingRoot;importanceArtifactsRoot=$importanceArtifactsRoot}) (Join-Path $ResultsRoot 'execution.json')
}
if ($failures.Count) {$failures | Write-Error -ErrorAction Continue; exit 1}
Write-Output "PASS: complete execution in $([Math]::Round($watch.Elapsed.TotalSeconds,2)) seconds; $ResultsRoot"
exit 0
