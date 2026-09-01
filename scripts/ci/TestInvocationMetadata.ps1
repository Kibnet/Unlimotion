function New-TestInvocationMetadata([string]$Project, [string[]]$Arguments) {
    function Read-Git([string[]]$GitArguments) {
        $value = & git @GitArguments 2>$null
        if ($LASTEXITCODE -eq 0) { return ($value -join "`n").Trim() }
        return $null
    }
    $sha = Read-Git @('rev-parse','HEAD')
    $tree = Read-Git @('rev-parse','HEAD^{tree}')
    $dirty = Read-Git @('status','--porcelain')
    $sdk = & dotnet --version
    $runtimes = @(& dotnet --list-runtimes)
    $tunit = $null
    $packagePath = Join-Path $PSScriptRoot '../../src/Directory.Packages.props'
    if (Test-Path -LiteralPath $packagePath) {
        $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($packagePath), $settings)
        try {
            $xml = [Xml.XmlDocument]::new(); $xml.XmlResolver = $null; $xml.Load($reader)
            $node = $xml.SelectSingleNode("//PackageVersion[@Include='TUnit']")
            if ($node) { $tunit = $node.GetAttribute('Version') }
        } finally { $reader.Dispose() }
    }
    return [ordered]@{
        schemaVersion=1; invocationId=[Guid]::NewGuid().ToString('N'); project=$Project; arguments=$Arguments
        startedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        repository=$env:GITHUB_REPOSITORY; workflow=$env:GITHUB_WORKFLOW
        runId=$env:GITHUB_RUN_ID; runAttempt=$(if ($env:GITHUB_RUN_ATTEMPT) {[int]$env:GITHUB_RUN_ATTEMPT} else {1})
        event=$env:GITHUB_EVENT_NAME; ref=$env:GITHUB_REF; headSha=$env:TEST_HEAD_SHA
        checkoutSha=$sha; treeSha=$tree; worktreeDirty=$(if ($sha -and $tree -and $null -ne $dirty) {![string]::IsNullOrEmpty($dirty)} else {$null})
        environment=[ordered]@{os=[Runtime.InteropServices.RuntimeInformation]::OSDescription; runner=$env:RUNNER_OS; image=$env:ImageOS; imageVersion=$env:ImageVersion; sdk=($sdk -join "`n"); runtimes=$runtimes; tunit=$tunit; configuration='Debug'}
    }
}
