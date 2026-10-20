function Assert-TestInventory($Tests) {
    if (@($Tests).Count -eq 0) { throw 'Empty test inventory' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $Tests) {
        if ([string]::IsNullOrWhiteSpace($test.uid) -or [string]::IsNullOrWhiteSpace($test.className) -or
            [string]::IsNullOrWhiteSpace($test.displayName) -or !$seen.Add($test.uid)) { throw 'Invalid or duplicate inventory identity' }
    }
}

function Assert-TestPartition($Expected, $Assigned) {
    Assert-TestInventory $Expected
    Assert-TestInventory $Assigned
    if (@($Expected).Count -ne @($Assigned).Count) {throw 'Partition count differs from discovery'}
    $uids=[Collections.Generic.HashSet[string]]::new([string[]]@($Expected.uid),[StringComparer]::Ordinal)
    foreach ($test in $Assigned) {if (!$uids.Remove($test.uid)) {throw 'Unexpected or duplicated partition UID'}}
    if ($uids.Count) {throw 'Partition omitted discovered UID'}
}

function New-TestExecutionPlan($Tests, [int]$Workers = 2, [int]$ArgumentBudget = 22000) {
    Assert-TestInventory $Tests
    if ($Workers -notin @(1,2)) { throw 'Only one or two workers are supported' }
    # Historical class costs are hints, never an allowlist. New classes join automatically.
    $costs = @{
        MainControlTaskCardLayoutUiTests=279; MainWindowViewModelTests=260
        MainControlTreeCommandsUiTests=259; RoadmapGraphUiTests=254
        TaskTabEmojiFilterCopyUiTests=194; TaskSpaceTransactionTests=178
        UnlimotionCliIntegrationTests=152; MainControlTaskStatusIconUiTests=143
        TaskImportanceVisualUiTests=121; TaskTabEmojiFilterIsolationUiTests=96
        MainControlFilterToolbarResponsiveUiTests=93; StormServerStorageCrudRealtimeExecutableSpecTests=76
    }
    $groups = @($Tests | Group-Object className | ForEach-Object {
        $short = ($_.Name -split '\.')[-1]
        [pscustomobject]@{className=$_.Name; tests=@($_.Group | Sort-Object uid); cost=$(if ($costs.ContainsKey($short)) {$costs[$short]} else {[Math]::Max(1,$_.Count)})}
    })
    $lanes = @(for ($i=0; $i -lt $Workers; $i++) { [pscustomobject]@{index=$i; cost=0; groups=[Collections.Generic.List[object]]::new()} })
    $exclusive = @($groups | Where-Object className -eq 'Unlimotion.Test.NightAgentWorkflowReplayTests')
    foreach ($group in @($groups | Where-Object className -ne 'Unlimotion.Test.NightAgentWorkflowReplayTests' | Sort-Object @{Expression='cost';Descending=$true},className)) {
        $lane = $lanes | Sort-Object cost,index | Select-Object -First 1
        $lane.groups.Add($group); $lane.cost += $group.cost
    }
    $batches = [Collections.Generic.List[object]]::new()
    foreach ($lane in $lanes) {
        $pending = [Collections.Generic.List[object]]::new(); $length=0; $number=0
        foreach ($group in $lane.groups) {
            $fresh=@($group.tests | Where-Object freshProcess -eq $true)
            foreach($test in $fresh) {
                $batches.Add([pscustomobject]@{lane=$lane.index;batch=$number++;tests=@($test);classFilter=$null;freshProcess=$true})
            }
            $normal=@($group.tests | Where-Object {!$_.freshProcess})
            $groupLength = ($normal | ForEach-Object { $_.uid.Length + 4 } | Measure-Object -Sum).Sum
            if ($pending.Count -and $length+$groupLength -gt $ArgumentBudget) {
                $batches.Add([pscustomobject]@{lane=$lane.index; batch=$number++; tests=$pending.ToArray(); classFilter=$null})
                $pending.Clear(); $length=0
            }
            if ($groupLength -gt $ArgumentBudget) {
                if ($fresh.Count) {throw 'Mixed-isolation class exceeds UID budget; split the class before running'}
                $batches.Add([pscustomobject]@{lane=$lane.index; batch=$number++; tests=$normal; classFilter=$group.className})
            } else { $pending.AddRange([object[]]$normal); $length += $groupLength }
        }
        if ($pending.Count) { $batches.Add([pscustomobject]@{lane=$lane.index; batch=$number; tests=$pending.ToArray(); classFilter=$null}) }
    }
    return [pscustomobject]@{schemaVersion=1; workers=$Workers; batches=$batches.ToArray(); exclusive=@($exclusive | ForEach-Object {$_.tests})}
}

function Assert-TestExecutionReceipts($Expected, [string]$Directory, [int]$ProcessId, [Nullable[int]]$NativeExitCode = $null) {
    Assert-TestInventory $Expected
    $path = Join-Path $Directory "inventory-lifecycle-$ProcessId.jsonl"
    if (!(Test-Path -LiteralPath $path)) { throw "Missing execution receipts: $path" }
    $rows = @(Get-Content -LiteralPath $path | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($state in @('started','finished')) {
        $actual = @($rows | Where-Object state -eq $state)
        if ($actual.Count -ne @($Expected).Count) { throw "Incomplete $state receipts: $($actual.Count) / $(@($Expected).Count)" }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($row in $actual) { if ($row.processId -ne $ProcessId -or !$seen.Add($row.uid)) { throw 'Duplicate execution receipt or foreign process' } }
        foreach ($test in $Expected) { if (!$seen.Contains($test.uid)) { throw "Missing $state test UID: $($test.uid)" } }
    }
    # Check actual parent TRX outcomes too; hooks alone do not prove that a case ran.
    $trx = @(Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -File)
    if ($trx.Count -ne 1) { throw 'Expected exactly one parent TRX per invocation' }
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null
    $reader = [Xml.XmlReader]::Create($trx[0].FullName,$settings)
    try { $xml=[Xml.XmlDocument]::new(); $xml.XmlResolver=$null; $xml.Load($reader) } finally { $reader.Dispose() }
    $ns=[Xml.XmlNamespaceManager]::new($xml.NameTable); $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $definitions=@{}
    foreach ($definition in $xml.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest',$ns)) { $definitions[$definition.GetAttribute('id')]=$definition.SelectSingleNode('t:TestMethod',$ns).GetAttribute('className') }
    $observed=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $records=@($xml.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult',$ns))
    if ($records.Count -ne @($Expected).Count) { throw 'Parent TRX count differs from inventory' }
    foreach ($record in $records) {
        if ($record.GetAttribute('outcome') -notin @('Passed','Failed')) { throw 'A required case was skipped or did not finish' }
        if ($record.GetAttribute('outcome') -eq 'Failed' -and $NativeExitCode -eq 0) {throw 'Successful process returned a failed TRX result'}
        $output=$record.SelectSingleNode('t:Output/t:StdOut',$ns)
        $prefix="UNLIMOTION_TEST_UID:${ProcessId}:"
        $markers=@(([string]$output.InnerText -split '\r?\n') | Where-Object { $_.StartsWith($prefix,[StringComparison]::Ordinal) })
        if ($markers.Count -ne 1) {throw 'Missing or ambiguous TRX UID receipt'}
        $uid=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($markers[0].Substring($prefix.Length)))
        if ($observed.ContainsKey($uid)) {throw 'Duplicate parent TRX UID'}
        $observed.Add($uid,$definitions[$record.GetAttribute('testId')])
    }
    foreach ($test in $Expected) {
        if (!$observed.ContainsKey($test.uid) -or $observed[$test.uid] -ne $test.className) {throw 'Missing or contradictory parent TRX UID'}
        $observed.Remove($test.uid) | Out-Null
    }
    if ($observed.Count) { throw 'Unexpected parent TRX identity' }
}
