$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot 'TestExecutionPlan.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-execution-contract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
function Require([bool]$Condition,[string]$Message) {if(!$Condition){throw $Message}}
function Reject([scriptblock]$Action,[string]$Message) {try {& $Action} catch {return}; throw $Message}
$cases=@(1..4 | ForEach-Object {[pscustomobject]@{uid="case-$_";className="New.Class$_";displayName="test$_"}})
$plan=New-TestExecutionPlan $cases 2 40
Require (@($plan.batches.tests).Count -eq 4 -and $plan.exclusive.Count -eq 0) 'New classes lost or null exclusive created'
Require (@($plan.batches.lane | Sort-Object -Unique).Count -eq 2) 'Both lanes must be used'
Reject {New-TestExecutionPlan @($cases[0],$cases[0])} 'Duplicate UID accepted'
Reject {New-TestExecutionPlan @()} 'Zero discovery accepted'
Assert-TestPartition $cases @($plan.batches.tests)
Reject {Assert-TestPartition $cases @($cases[0],$cases[1],$cases[2],$cases[2])} 'Duplicate partition replaced a missing case'
Reject {Assert-TestPartition $cases @($cases[0],$cases[1],$cases[2])} 'Incomplete partition accepted'
$oversize=[pscustomobject]@{uid=('x'*100);className='New.Huge';displayName='huge'}
Require ((New-TestExecutionPlan @($oversize) 2 40).batches[0].classFilter -eq 'New.Huge') 'Oversized class was truncated'
$rendered=[pscustomobject]@{uid='rendered';className=$cases[0].className;displayName='rendered';freshProcess=$true}
$mixed=New-TestExecutionPlan (@($cases)+@($rendered))
Assert-TestPartition (@($cases)+@($rendered)) @($mixed.batches.tests)
Require (@($mixed.batches | Where-Object {$_.tests.uid -contains 'rendered' -and $_.tests.Count -eq 1}).Count -eq 1) 'Rendered case shares a process with another backend'
$project=Join-Path $root 'host'; New-Item -ItemType Directory -Path $project | Out-Null
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' | Set-Content (Join-Path $project 'Stub.csproj')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/TestExecutionStub.cs') -Destination (Join-Path $project 'Program.cs')
& dotnet build (Join-Path $project 'Stub.csproj') -o (Join-Path $project 'output') > (Join-Path $root 'build.log') 2>&1
if($LASTEXITCODE){throw "Stub build failed: $root/build.log"}
$hostOutput=Join-Path $project 'output'; $stub=Join-Path $hostOutput 'Stub.exe'
$entry=Join-Path $PSScriptRoot 'Invoke-TestExecution.ps1'
$reporter=Join-Path $PSScriptRoot 'Write-TestReport.ps1'
$previousImportanceRoot=$env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY
try {
    foreach($fault in @('none','failure','failure-zero-exit','missing','duplicate','skip','discovery','timeout','trx-missing','trx-wrong','trx-duplicate')) {
        $result=Join-Path $root "пути с пробелами/$fault"
        $env:TEST_EXECUTION_STUB_FAULT=$fault
        $env:TEST_EXECUTION_STUB_LOG=Join-Path $root "timeline-$fault"
        $env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY=Join-Path $root "importance-$fault"
        $timeout=if($fault -eq 'timeout'){1}else{30}
        & pwsh -NoProfile -File $entry -ResultsRoot $result -DotnetCommand $stub -MainOutput $hostOutput -HeadlessOutput $hostOutput -InvocationTimeoutSeconds $timeout > (Join-Path $root "$fault.log") 2>&1
        $code=$LASTEXITCODE
        Require (($code -eq 0) -eq ($fault -eq 'none')) "Wrong coordinator exit: $fault / $code"
        $timeline=@(Get-ChildItem -LiteralPath $env:TEST_EXECUTION_STUB_LOG -Filter '*.json' | ForEach-Object {Get-Content -Raw $_.FullName | ConvertFrom-Json})
        Require (@($timeline | Where-Object project -eq 'headless').Count -eq 1) "Headless skipped after $fault"
        if($fault -ne 'discovery') {Require (@($timeline | Where-Object {$_.uids -contains 'benchmark'}).Count -eq 1) "Exclusive case skipped after $fault"}
        if($fault -eq 'none') {
            $normal=@($timeline | Where-Object {$_.project -eq 'main' -and $_.uids -notcontains 'benchmark'})
            $overlap=$false
            for($left=0;$left -lt $normal.Count;$left++) {for($right=$left+1;$right -lt $normal.Count;$right++) {
                if([DateTimeOffset]$normal[$left].startedUtc -lt [DateTimeOffset]$normal[$right].finishedUtc -and
                    [DateTimeOffset]$normal[$right].startedUtc -lt [DateTimeOffset]$normal[$left].finishedUtc) {$overlap=$true}
            }}
            Require ($normal.Count -eq 3 -and $overlap) 'Workers did not overlap'
            Require (@($normal.workingDirectory | Sort-Object -Unique).Count -eq 2) 'Shared writable output root'
            $rendered=@($normal | Where-Object {$_.uids -contains 'gamma'})
            Require ($rendered.Count -eq 1 -and @($rendered[0].uids).Count -eq 1 -and $rendered[0].renderedChild) 'Rendered case was not isolated or would fork twice'
            $benchmark=$timeline | Where-Object {$_.uids -contains 'benchmark'}
            foreach($other in @($timeline | Where-Object {$_.uids -notcontains 'benchmark'})) {Require ([DateTimeOffset]$benchmark.startedUtc -ge [DateTimeOffset]$other.finishedUtc) 'Benchmark overlapped another suite'}
            $before=(Get-FileHash (Join-Path $result 'main/invocation-test.json')).Hash
            & pwsh -NoProfile -File $entry -ResultsRoot $result -DotnetCommand $stub -MainOutput $hostOutput -HeadlessOutput $hostOutput *> $null
            Require ($LASTEXITCODE -ne 0 -and (Get-FileHash (Join-Path $result 'main/invocation-test.json')).Hash -eq $before) 'Rerun replaced prior evidence'
            & pwsh -NoProfile -File $reporter -ResultsRoot $result -OutputRoot (Join-Path $root 'report-success') *> $null
            Require ($LASTEXITCODE -eq 0) 'Reporter rejected complete execution'
            $reported=@(Get-Content (Join-Path $root 'report-success/main/tests.json') -Raw|ConvertFrom-Json)
            Require ($reported.Count -eq 4 -and @($reported|Where-Object identityStatus -ne 'complete').Count -eq 0) 'Rendered child inflated report counts or made parent history ambiguous'
            Require (@($timeline|Where-Object {$_.importanceArtifactsRoot -ne [IO.Path]::GetFullPath($env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY)}).Count -eq 0) 'Configured external importance root was overridden'
        }
    }
    # The unconfigured local default must also keep rendered children out of report input.
    $env:TEST_EXECUTION_STUB_FAULT='none'
    $env:TEST_EXECUTION_STUB_LOG=Join-Path $root 'timeline-default-importance'
    Remove-Item Env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY -ErrorAction SilentlyContinue
    $result=Join-Path $root 'default-importance'
    & pwsh -NoProfile -File $entry -ResultsRoot $result -DotnetCommand $stub -MainOutput $hostOutput -HeadlessOutput $hostOutput > (Join-Path $root 'default-importance.log') 2>&1
    Require ($LASTEXITCODE -eq 0) 'Default external importance root failed'
    $execution=Get-Content (Join-Path $result 'execution.json') -Raw|ConvertFrom-Json
    Require ($execution.importanceArtifactsRoot -eq "$result.importance") 'Default importance root is not a sibling of ResultsRoot'
    Require (@(Get-ChildItem $execution.importanceArtifactsRoot -Filter rendered-child.trx -Recurse).Count -eq 1) 'Default rendered evidence was lost during worker cleanup'
    & pwsh -NoProfile -File $reporter -ResultsRoot $result -OutputRoot (Join-Path $root 'report-default-importance') *> $null
    Require ($LASTEXITCODE -eq 0) 'Reporter rejected default external importance artifacts'
    Require (@(Get-Content (Join-Path $root 'report-default-importance/main/tests.json') -Raw|ConvertFrom-Json).Count -eq 4) 'Default rendered child inflated report counts'
    # An explicitly nested root must fail before writing any results.
    $result=Join-Path $root 'nested-importance'
    $env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY=Join-Path $result 'main/importance'
    & pwsh -NoProfile -File $entry -ResultsRoot $result -DotnetCommand $stub -MainOutput $hostOutput -HeadlessOutput $hostOutput > (Join-Path $root 'nested-importance.log') 2>&1
    Require ($LASTEXITCODE -ne 0 -and !(Test-Path $result)) 'Nested importance root was accepted or changed report input'
    # A native launch failure in one project must not suppress the healthy project.
    $env:TEST_EXECUTION_STUB_FAULT='none'
    $env:TEST_EXECUTION_STUB_LOG=Join-Path $root 'timeline-launch-failure'
    $env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY=Join-Path $root 'importance-launch-failure'
    $result=Join-Path $root 'launch-failure'
    & pwsh -NoProfile -File $entry -ResultsRoot $result -DotnetCommand $stub -MainOutput (Join-Path $root 'missing-output') -HeadlessOutput $hostOutput > (Join-Path $root 'launch-failure.log') 2>&1
    Require ($LASTEXITCODE -ne 0) 'Native launch failure returned success'
    $timeline=@(Get-ChildItem $env:TEST_EXECUTION_STUB_LOG -Filter '*.json' | ForEach-Object {Get-Content $_.FullName -Raw|ConvertFrom-Json})
    Require (@($timeline|Where-Object project -eq 'headless').Count -eq 1) 'Healthy Headless project skipped after native launch failure'
    $mainStage=Get-Content (Join-Path $result 'main/stage-test.json') -Raw|ConvertFrom-Json
    Require ($mainStage.exitCode -ne 0 -and !$mainStage.inventoryComplete) 'Failed launch lost its incomplete stage metadata'
    # Cancellation must stop owned processes without touching an unrelated process.
    $env:TEST_EXECUTION_STUB_FAULT='cancellation'
    $env:TEST_EXECUTION_STUB_LOG=Join-Path $root 'timeline-cancellation'
    $cancel=Join-Path $root 'cancel.signal'
    $sentinelInfo=[Diagnostics.ProcessStartInfo]::new($stub); $sentinelInfo.ArgumentList.Add('--sentinel')
    $sentinelInfo.UseShellExecute=$false; $sentinelInfo.CreateNoWindow=$true
    $sentinel=[Diagnostics.Process]::Start($sentinelInfo)
    $cancelInfo=[Diagnostics.ProcessStartInfo]::new('pwsh')
    $cancelInfo.UseShellExecute=$false; $cancelInfo.CreateNoWindow=$true
    $cancelInfo.RedirectStandardOutput=$true; $cancelInfo.RedirectStandardError=$true
    foreach($argument in @('-NoProfile','-File',$entry,'-ResultsRoot',(Join-Path $root 'cancellation'),'-DotnetCommand',$stub,'-MainOutput',$hostOutput,'-HeadlessOutput',$hostOutput,'-CancellationFile',$cancel)) {$cancelInfo.ArgumentList.Add($argument)}
    $cancelProcess=[Diagnostics.Process]::Start($cancelInfo)
    $cancelOut=$cancelProcess.StandardOutput.ReadToEndAsync(); $cancelErr=$cancelProcess.StandardError.ReadToEndAsync()
    try {
        $deadline=[DateTimeOffset]::UtcNow.AddSeconds(20)
        do {
            $startedFiles=@(Get-ChildItem -LiteralPath $env:TEST_EXECUTION_STUB_LOG -Filter 'started-*.txt' -ErrorAction SilentlyContinue)
            if ($startedFiles.Count -ge 2) {break}
            if ($cancelProcess.HasExited -or [DateTimeOffset]::UtcNow -ge $deadline) {throw 'Cancellation fixture did not start workers'}
            Start-Sleep -Milliseconds 50
        } while($true)
        Set-Content -LiteralPath $cancel -Value 'cancel'
        Require ($cancelProcess.WaitForExit(10000) -and $cancelProcess.ExitCode -ne 0) 'Cancellation did not fail promptly'
        Require (!$sentinel.HasExited) 'Cancellation killed an unrelated process'
        foreach($file in @(Get-ChildItem -LiteralPath $env:TEST_EXECUTION_STUB_LOG -Filter 'started-*.txt')) {
            $ownedPid=[int]($file.BaseName -replace '^started-','')
            Require ($null -eq (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) 'Cancellation leaked an owned process'
        }
        [IO.File]::WriteAllText((Join-Path $root 'cancellation.log'),$cancelOut.GetAwaiter().GetResult()+$cancelErr.GetAwaiter().GetResult())
    } finally {
        if (!$cancelProcess.HasExited) {$cancelProcess.Kill($true)}
        if (!$sentinel.HasExited) {$sentinel.Kill($true)}
        $cancelProcess.Dispose(); $sentinel.Dispose()
    }
    # The reporter must reject an aggregate that hides a failed child, even with valid root metadata.
    $result=Join-Path $root 'пути с пробелами/failure'
    $stagePath=Join-Path $result 'main/stage-test.json'; $stage=Get-Content -Raw $stagePath | ConvertFrom-Json
    $stage.exitCode=0; $stage | ConvertTo-Json -Depth 15 | Set-Content $stagePath
    & pwsh -NoProfile -File $reporter -ResultsRoot $result -OutputRoot (Join-Path $root 'report-hidden-failure') *> $null
    Require ($LASTEXITCODE -ne 0) 'Reporter hid a failed invocation'
    Write-Output "PASS: planner, eleven failure/success scenarios, external/default/nested artifact roots, native launch failure, owned cancellation, inventory/TRX reconciliation and reporting; evidence: $root"
} finally {
    Remove-Item Env:TEST_EXECUTION_STUB_FAULT,Env:TEST_EXECUTION_STUB_LOG -ErrorAction SilentlyContinue
    $env:UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY=$previousImportanceRoot
}
