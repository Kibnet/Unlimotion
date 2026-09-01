[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string[]]$Projects = @('main','headless'),
    [ValidateSet('success','failure','cancelled')][string]$PipelineOutcome = 'success',
    [string]$HistoryRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$allTests = [Collections.Generic.List[object]]::new()
$projectReports = [Collections.Generic.List[object]]::new()
$errors = [Collections.Generic.List[string]]::new()
$subcases = [Collections.Generic.List[object]]::new()
$invocations = [Collections.Generic.List[object]]::new()
function Get-Fingerprint($Value) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Value -Depth 12 -Compress)))).ToLowerInvariant()
}
function Read-SafeXml([string]$Path) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader); return ,$document }
    finally { $reader.Dispose() }
}
function Escape-Markdown($Value) {
    return [Net.WebUtility]::HtmlEncode([string]$Value).Replace('|','&#124;').Replace("`r",' ').Replace("`n",' ').Replace('`','&#96;').Replace('[','&#91;').Replace(']','&#93;').Replace('*','&#42;')
}
function Test-InvocationArguments($Value) {
    return $Value -is [Array] -and $Value.Count -gt 0 -and @($Value | Where-Object {$_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)}).Count -eq 0
}
foreach ($project in $Projects) {
    if ($project -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Project must be a simple directory name' }
    $inputDirectory = Join-Path $ResultsRoot $project
    $outputDirectory = Join-Path $OutputRoot $project
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    $stages = @{}
    foreach ($stage in @('restore','build','test')) {
        $stagePath = Join-Path $inputDirectory "stage-$stage.json"
        if (Test-Path -LiteralPath $stagePath) {
            try { $stages[$stage] = Get-Content -Raw -LiteralPath $stagePath | ConvertFrom-Json -AsHashtable }
            catch { $errors.Add("${project}: malformed stage metadata '$([IO.Path]::GetFileName($stagePath))': $($_.Exception.Message)") }
        }
    }
    $trxFiles = @(Get-ChildItem -LiteralPath $inputDirectory -Filter '*.trx' -File -Recurse -ErrorAction SilentlyContinue)
    $htmlFiles = @(Get-ChildItem -LiteralPath $inputDirectory -Filter '*report.html' -File -Recurse -ErrorAction SilentlyContinue | Where-Object Length -gt 0)
    $tests = [Collections.Generic.List[object]]::new()
    $traceEntries = [Collections.Generic.List[object]]::new()
    $invocation = $null
    $manifestPath = Join-Path $inputDirectory 'invocation-test.json'
    if (Test-Path -LiteralPath $manifestPath) {
        try {
            $candidate = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable
            if ($candidate.schemaVersion -ne 1 -or $candidate.project -ne $project) { throw 'Unexpected schema version or project' }
            $invocation = $candidate
            if ([string]::IsNullOrWhiteSpace([string]$invocation.invocationId)) { $errors.Add("${project}: missing invocation identity") }
            if (!(Test-InvocationArguments $invocation.arguments)) { $errors.Add("${project}: missing or invalid invocation arguments") }
            $invocations.Add($invocation)
        } catch {
            $invocation = $null
            $errors.Add("${project}: invalid invocation manifest '$([IO.Path]::GetFileName($manifestPath))': $($_.Exception.Message)")
        }
    }
    $countersComplete = $true
    foreach ($trace in @(Get-ChildItem -LiteralPath $inputDirectory -Filter 'diagnostics-*.jsonl' -File -Recurse -ErrorAction SilentlyContinue)) {
        foreach ($line in Get-Content -LiteralPath $trace.FullName) {
            try {
                $entry = $line | ConvertFrom-Json -AsHashtable
                $traceEntries.Add($entry)
                if ($entry.kind -eq 'subcase' -and $entry.outcome -ne 'started') { $subcases.Add(@{project=$project; entry=$entry}) }
            } catch {
                $errors.Add("${project}: incomplete diagnostic trace '$($trace.Name)'")
            }
        }
    }
    foreach ($file in $trxFiles) {
        try {
            $xml = Read-SafeXml $file.FullName
            $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
            $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
            $definitions = @{}
            foreach ($definition in $xml.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns)) { $definitions[$definition.GetAttribute('id')] = $definition.SelectSingleNode('t:TestMethod', $ns) }
            $records = @($xml.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
            $summaryNode = $xml.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns)
            $counters = $xml.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
            $passedRecords = @($records | Where-Object {$_.GetAttribute('outcome') -eq 'Passed'}).Count
            $failedRecords = @($records | Where-Object {$_.GetAttribute('outcome') -eq 'Failed'}).Count
            if (!$counters -or !$summaryNode -or $summaryNode.GetAttribute('outcome') -notin @('Completed','Failed') -or
                [int]$counters.GetAttribute('total') -ne $records.Count -or [int]$counters.GetAttribute('passed') -ne $passedRecords -or
                [int]$counters.GetAttribute('failed') -ne $failedRecords -or [int]$counters.GetAttribute('executed') -ne ($passedRecords+$failedRecords)) {
                $countersComplete = $false
            }
            if ($counters) {
                foreach ($counterName in @('error','timeout','aborted','inProgress','pending','disconnected')) {
                    if ([int]$counters.GetAttribute($counterName) -ne 0) { $countersComplete=$false }
                }
            }
            foreach ($result in $xml.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns)) {
                $definition = $definitions[$result.GetAttribute('testId')]
                $class = if ($definition) { $definition.GetAttribute('className') } else { '' }
                $method = if ($definition) { $definition.GetAttribute('name') } else { '' }
                $display = $result.GetAttribute('testName')
                $identityFields = @($project, $class, $method, $display) | ConvertTo-Json -Compress
                $identity = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identityFields))).ToLowerInvariant()
                $duration = [TimeSpan]::Parse($result.GetAttribute('duration'), [Globalization.CultureInfo]::InvariantCulture).TotalMilliseconds
                if ($duration -lt 0) { throw 'Negative duration' }
                $message = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
                $tests.Add([ordered]@{
                    project=$project; identity=$identity; identityStatus=$(if ($class -and $method -and $result.GetAttribute('executionId') -and ($display -eq $method -or $display.StartsWith($method + '('))) {'complete'} else {'ambiguous'})
                    className=$class; method=$method; displayName=$display; arguments=$null
                    argumentSource='TRX displayName; collisions are not merged'; executionId=$result.GetAttribute('executionId')
                    testId=$result.GetAttribute('testId'); outcome=$result.GetAttribute('outcome'); durationMs=$duration
                    startedUtc=$result.GetAttribute('startTime'); finishedUtc=$result.GetAttribute('endTime')
                    error=$(if ($message) {$message.InnerText} else {$null})
                    phases=$null; phasesMissingReason='See optional diagnostic trace; TRX does not separate lifecycle phases'
                })
            }
        } catch { $countersComplete=$false; $errors.Add("${project}: malformed TRX '$($file.Name)': $($_.Exception.Message)") }
    }
    foreach ($group in @($tests | Group-Object { $_.identity } | Where-Object Count -gt 1)) {
        foreach ($test in $group.Group) { $test.identityStatus = 'ambiguous' }
    }
    foreach ($group in @($tests | Group-Object { $_.executionId } | Where-Object Count -gt 1)) {
        foreach ($test in $group.Group) { $test.identityStatus = 'ambiguous' }
    }
    foreach ($test in $tests) {
        # TUnit context IDs differ from TRX executionId. Join only a unique, complete
        # class/display identity via the lifecycle record; never guess a collision.
        $starts = @($traceEntries | Where-Object {$_.kind -eq 'test-lifecycle' -and $_.outcome -eq 'started' -and $_.details.className -eq $test.className -and $_.testName -eq $test.displayName})
        if ($test.identityStatus -eq 'complete' -and $starts.Count -eq 1 -and $starts[0].testExecutionId) {
            $contextId = $starts[0].testExecutionId
            $test['diagnosticTestExecutionId'] = $contextId
            $test.phases = @($traceEntries | Where-Object {$_.kind -eq 'phase' -and $_.testExecutionId -eq $contextId} | ForEach-Object {[ordered]@{name=$_.name; durationMs=$_.durationMs; outcome=$_.outcome}})
            $test.phasesMissingReason = if ($test.phases.Count) {$null} else {'This test has no explicit phase instrumentation'}
        }
    }
    $status = 'not-executed'
    $passed = @($tests | Where-Object { $_.outcome -eq 'Passed' }).Count
    $failed = @($tests | Where-Object { $_.outcome -eq 'Failed' }).Count
    $testCompleted = $stages.ContainsKey('test') -and $stages.test.finishedUtc -and $invocation -and ![string]::IsNullOrWhiteSpace([string]$invocation.invocationId) -and $stages.test.invocationId -eq $invocation.invocationId
    if ($tests.Count -gt 0) { $status = if ($testCompleted) {if ($failed) {'test-failure'} elseif ($passed) {'passed'} else {'zero-discovery'}} else {'incomplete'} }
    foreach ($stage in @('restore','build','test')) {
        if ($stages.ContainsKey($stage) -and $stages[$stage].exitCode -ne 0) {
            $status = if ($stage -eq 'test') { if ($stages[$stage].exitCode -eq 8) {'zero-discovery'} elseif ($stages[$stage].exitCode -eq 2 -and $failed) {'test-failure'} else {'runner-failure'} } else { "$stage-failure" }
            break
        }
    }
    $expectReports = ($stages.ContainsKey('test') -and $stages.test.exitCode -in @(0,2)) -or ($PipelineOutcome -eq 'success' -and $stages.Count -eq 0)
    if ($expectReports -and ($trxFiles.Count -eq 0 -or $htmlFiles.Count -eq 0 -or $tests.Count -eq 0)) { $errors.Add("${project}: successful execution requires nonempty TRX and HTML") }
    if ($expectReports -and (!$testCompleted -or !$countersComplete)) { $errors.Add("${project}: incomplete invocation or TRX counters") }
    if ($stages.ContainsKey('test') -and $stages.test.exitCode -eq 0 -and $failed) { $errors.Add("${project}: exit zero contradicts failed test records") }
    if ($status -eq 'passed' -and !$countersComplete) { $status = 'incomplete' }
    if ($status -eq 'zero-discovery' -and $expectReports) { $errors.Add("${project}: no tests actually passed or failed") }
    if (!$testCompleted -and $PipelineOutcome -eq 'cancelled' -and $status -in @('not-executed','incomplete')) { $status = 'cancelled' }
    if ($status -eq 'not-executed' -and $PipelineOutcome -eq 'success') { $errors.Add("${project}: mandatory project was not executed") }
    $projectErrors = @($errors | Where-Object { $_.StartsWith("${project}:") })
    $report = [ordered]@{project=$project; status=$status; stages=$stages; invocation=$invocation; observed=$tests.Count; passed=$passed; failed=$failed; skipped=($tests.Count-$passed-$failed); telemetryComplete=($testCompleted -and $countersComplete -and $projectErrors.Count -eq 0 -and $trxFiles.Count -gt 0 -and $htmlFiles.Count -gt 0 -and $status -in @('passed','test-failure')); telemetryErrors=$projectErrors}
    $projectReports.Add($report)
    $allTests.AddRange([object[]]$tests.ToArray())
    ConvertTo-Json -InputObject @($tests.ToArray()) -Depth 10 | Set-Content -LiteralPath (Join-Path $outputDirectory 'tests.json') -Encoding utf8
}
$source = if ($invocations.Count) {$invocations[0]} else {$null}
$environment = $source.environment
$fingerprint = if ($environment) {Get-Fingerprint $environment} else {$null}
$stageStarts = @($projectReports | ForEach-Object {$_.stages.Values.startedUtc} | Where-Object {$_} | Sort-Object)
$runStart = if ($stageStarts.Count) {$stageStarts[0]} else {($allTests | ForEach-Object {$_.startedUtc} | Where-Object {$_} | Sort-Object | Select-Object -First 1)}
$stageArguments = @($projectReports | Sort-Object {$_.project} | ForEach-Object {
    if ($_.invocation -and (Test-InvocationArguments $_.invocation.arguments)) {
        $arguments = $_.invocation.arguments
        $normalized = for($index=0; $index -lt $arguments.Count; $index++) {
            if ($arguments[$index] -eq '--results-directory') {$index++; continue}
            if ($arguments[$index].StartsWith('--results-directory=',[StringComparison]::Ordinal)) {continue}
            $arguments[$index]
        }
        [ordered]@{project=$_.project; arguments=@($normalized)}
    }
})
$argumentsFingerprint = Get-Fingerprint $stageArguments
$consistentSource = $invocations.Count -eq $Projects.Count -and @($invocations | Where-Object {
    !$_.treeSha -or !$_.checkoutSha -or $_.worktreeDirty -ne $false -or !$_.environment.sdk -or !$_.environment.tunit -or
    $_.treeSha -ne $source.treeSha -or $_.runId -ne $source.runId -or $_.runAttempt -ne $source.runAttempt -or (Get-Fingerprint $_.environment) -ne $fingerprint
}).Count -eq 0
$run = [ordered]@{
    schemaVersion=1; repository=$source.repository; workflow=$source.workflow
    runId=$(if ($source.runId) {$source.runId} elseif ($source.invocationId) {'local-' + $source.invocationId} else {'unverified-' + (Get-Fingerprint ([IO.Path]::GetFullPath($ResultsRoot)))})
    runAttempt=$(if ($source.runAttempt) {$source.runAttempt} else {1}); event=$source.event; ref=$source.ref
    headSha=$source.headSha; checkoutSha=$source.checkoutSha; treeSha=$source.treeSha
    startedUtc=$runStart; generatedUtc=[DateTimeOffset]::UtcNow.ToString('o'); pipelineOutcome=$PipelineOutcome; environment=$environment; environmentFingerprint=$fingerprint
    worktreeDirty=$source.worktreeDirty; argumentFingerprint=$argumentsFingerprint
    historyComparable=($consistentSource -and $stageArguments.Count -eq $Projects.Count -and @($projectReports | Where-Object {!$_.telemetryComplete}).Count -eq 0)
    projects=$projectReports.ToArray(); telemetryErrors=$errors.ToArray()
}
foreach ($project in $Projects) { $run | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path (Join-Path $OutputRoot $project) 'run.json') -Encoding utf8 }
$run | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $OutputRoot 'run.json') -Encoding utf8
ConvertTo-Json -InputObject @($allTests.ToArray()) -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputRoot 'tests.json') -Encoding utf8
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Результаты тестов')
$lines.Add('')
$lines.Add('| Проект | Статус | Passed | Failed | Skipped | Telemetry |')
$lines.Add('| --- | --- | ---: | ---: | ---: | --- |')
foreach ($item in $projectReports) { $lines.Add("| $($item.project) | $($item.status) | $($item.passed) | $($item.failed) | $($item.skipped) | $($item.telemetryComplete) |") }
$lines.Add(''); $lines.Add('## Самые долгие тесты этого запуска'); $lines.Add('')
$lines.Add('| Тест | Проект | Секунды | Результат |'); $lines.Add('| --- | --- | ---: | --- |')
foreach ($test in @($allTests | Sort-Object { $_.durationMs } -Descending | Select-Object -First 20)) { $lines.Add("| $(Escape-Markdown "$($test.className).$($test.displayName)") | $($test.project) | $([math]::Round($test.durationMs / 1000,3)) | $(Escape-Markdown $test.outcome) |") }
$lines.Add(''); $lines.Add('## Ошибки и пропуски'); $lines.Add('')
foreach ($test in @($allTests | Where-Object { $_.outcome -ne 'Passed' })) { $lines.Add("- $(Escape-Markdown $test.displayName): $(Escape-Markdown $test.outcome) — $(Escape-Markdown $test.error)") }
foreach ($errorText in $errors) { $lines.Add("- Telemetry: $(Escape-Markdown $errorText)") }
foreach ($subcase in @($subcases | Where-Object {$_.entry.outcome -ne 'passed'})) { $lines.Add("- BDD subcase ($(Escape-Markdown $subcase.entry.testName)): $(Escape-Markdown $subcase.entry.name) — $(Escape-Markdown $subcase.entry.outcome)") }
$lines.Add(''); $lines.Add('Длительности TRX не разделяют setup/body/cleanup. Их сумма не равна wall-clock при параллельном исполнении. Отсутствующие результаты не считаются успешными.')
if ($HistoryRoot) {
    $history = [Collections.Generic.List[object]]::new()
    foreach ($path in @(Get-ChildItem -LiteralPath $HistoryRoot -Filter 'run.json' -Recurse -File)) {
        $metadata = Get-Content -Raw -LiteralPath $path.FullName | ConvertFrom-Json -AsHashtable
        $testsPath = Join-Path $path.DirectoryName 'tests.json'
        if ($metadata.schemaVersion -eq 1 -and (Test-Path -LiteralPath $testsPath)) { $history.Add(@{metadata=$metadata; tests=@(Get-Content -Raw -LiteralPath $testsPath | ConvertFrom-Json -AsHashtable)}) }
    }
    $recent = @($history | Group-Object { "$($_.metadata.repository)/$($_.metadata.workflow)/$($_.metadata.runId)" } | Sort-Object { ($_.Group | ForEach-Object {if ($_.metadata.startedUtc) {$_.metadata.startedUtc} else {$_.metadata.generatedUtc}} | Sort-Object -Descending | Select-Object -First 1) } -Descending | Select-Object -First 10)
    $observations = foreach ($logicalRun in $recent) {
        # Artifact copies are repeated per project; key execution records rather than counting copies.
        $seen = [Collections.Generic.HashSet[string]]::new()
        foreach ($entry in $logicalRun.Group) { foreach ($test in $entry.tests) {
            $key = "$($entry.metadata.runAttempt)/$($test.project)/$($test.executionId)"
            $sourceGroup = if ($entry.metadata.historyComparable) {"$($entry.metadata.treeSha)/$($entry.metadata.environmentFingerprint)/$($entry.metadata.argumentFingerprint)"} else {"unverified/$($entry.metadata.runId)/$($entry.metadata.runAttempt)"}
            if ($test.identityStatus -eq 'complete' -and $seen.Add($key)) { [pscustomobject]@{test=$test; attempt=$entry.metadata.runAttempt; comparable="$sourceGroup/$($test.identity)"} }
        } }
    }
    $lines.Add(''); $lines.Add("## История: $($recent.Count) логических запусков с артефактами"); $lines.Add('')
    $lines.Add('Группы разделены по дереву исходников, окружению и аргументам. Dirty checkout/неизвестные аргументы не объединяются между runs/attempts. Legacy runs без артефактов сюда не восстановлены; размер выборки не равен числу всех GitHub runs. Attempts учитываются отдельно. Смена fail/pass — кандидат на нестабильность, не доказанная причина.')
    $lines.Add(''); $lines.Add('| Тест | Failed / observed | Skipped | Median, с | Max, с | Кандидат |'); $lines.Add('| --- | ---: | ---: | ---: | ---: | --- |')
    foreach ($group in @($observations | Group-Object comparable)) {
        $executed = @($group.Group.test | Where-Object { $_.outcome -in @('Passed','Failed') })
        if ($executed.Count -eq 0) {continue}
        $durations = @($executed.durationMs | Sort-Object)
        $middle = [int][math]::Floor($durations.Count / 2)
        $median = if ($durations.Count % 2) {$durations[$middle]} else {($durations[$middle-1]+$durations[$middle])/2}
        $failed = @($executed | Where-Object { $_.outcome -eq 'Failed' }).Count
        $lines.Add("| $(Escape-Markdown $executed[0].displayName) | $failed / $($executed.Count) | $($group.Count - $executed.Count) | $([math]::Round($median/1000,3)) | $([math]::Round($durations[-1]/1000,3)) | $($failed -gt 0 -and $failed -lt $executed.Count) |")
    }
}
$summary = $lines -join "`n"
$summary | Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.md') -Encoding utf8
foreach ($project in $Projects) { $summary | Set-Content -LiteralPath (Join-Path (Join-Path $OutputRoot $project) 'summary.md') -Encoding utf8 }
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary -Encoding utf8 }
Write-Output "Reports: $([IO.Path]::GetFullPath($OutputRoot)); tests=$($allTests.Count); telemetryErrors=$($errors.Count)"
if ($errors.Count -gt 0) { exit 1 }
