param(
    [ValidateSet('All', 'InventorySupport', 'IdentityTriggers', 'VelopackFeeds', 'Retry', 'Evidence', 'WorkflowSecurity')]
    [string]$Area = 'All',

    [string]$Manifest = (Join-Path $PSScriptRoot '..\distribution\release-assets.json'),

    [string]$ManifestSchema = (Join-Path $PSScriptRoot '..\distribution\release-assets.schema.json'),

    [string]$Fixture = (Join-Path $PSScriptRoot '..\distribution\fixtures\release-1.27.0.json'),

    [string]$SupportMatrix = (Join-Path $PSScriptRoot '..\distribution\support-matrix.json'),

    [string]$SupportMatrixSchema = (Join-Path $PSScriptRoot '..\distribution\support-matrix.schema.json'),

    [string]$Resolver = (Join-Path $PSScriptRoot 'Resolve-ReleaseIdentity.ps1'),

    [string]$EvidenceSchema = (Join-Path $PSScriptRoot '..\distribution\evidence.schema.json'),

    [string]$ArtifactValidator = (Join-Path $PSScriptRoot 'Test-DistributionArtifact.ps1'),

    [string]$Workflow = (Join-Path $PSScriptRoot '..\.github\workflows\distribution-validation.yml'),

    [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:checks = [System.Collections.Generic.List[string]]::new()
$script:negativeFixtureCount = 0
$script:pythonExecutable = $null

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if ($resolved.Provider.Name -ne 'FileSystem' -or -not [System.IO.File]::Exists($resolved.Path)) {
        throw "$DisplayName must be an existing file: $Path"
    }
    return $resolved.Path
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100 -ErrorAction Stop
    }
    catch {
        throw "$DisplayName is not valid JSON: $($_.Exception.Message)"
    }
}

function Copy-JsonObject {
    param([Parameter(Mandatory = $true)][object]$InputObject)

    return $InputObject | ConvertTo-Json -Depth 100 -Compress | ConvertFrom-Json -Depth 100
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Add-Check {
    param([Parameter(Mandatory = $true)][string]$Name)

    $script:checks.Add($Name)
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $failedAsExpected = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $failedAsExpected = $true
    }

    if (-not $failedAsExpected) {
        throw "Negative fixture '$Name' unexpectedly passed."
    }

    $script:negativeFixtureCount++
    Add-Check -Name "negative:$Name"
}

function Assert-JsonFileSchema {
    param(
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$SchemaPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $isValid = Test-Json -LiteralPath $JsonPath -SchemaFile $SchemaPath -ErrorAction Stop
    if (-not $isValid) {
        throw "$Name does not satisfy schema '$SchemaPath'."
    }
    Add-Check -Name "schema:$Name"
}

function Assert-JsonObjectSchema {
    param(
        [Parameter(Mandatory = $true)][object]$Document,
        [Parameter(Mandatory = $true)][string]$SchemaPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $json = $Document | ConvertTo-Json -Depth 100 -Compress
    try {
        $isValid = Test-Json -Json $json -SchemaFile $SchemaPath -ErrorAction Stop
    }
    catch {
        throw "$Name does not satisfy schema '$SchemaPath': $($_.Exception.Message)"
    }
    if (-not $isValid) {
        throw "$Name does not satisfy schema '$SchemaPath'."
    }
}

function Get-LowerFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Utf8Bytes {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Text.UTF8Encoding]::new($false).GetBytes($Value)
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function New-OrdinalIgnoreCaseMap {
    return [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
}

function Get-YamlTopLevelBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = @($normalized -split "`n")
    $start = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -ceq "${Name}:") {
            $start = $index
            break
        }
    }
    Assert-Condition ($start -ge 0) "Workflow is missing top-level '${Name}:' block."

    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if (-not [string]::IsNullOrWhiteSpace($lines[$index]) -and $lines[$index] -cmatch '^[A-Za-z0-9_.-]+:\s*') {
            $end = $index
            break
        }
    }
    return ($lines[$start..($end - 1)] -join "`n")
}

function Get-WorkflowJobBlocks {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $jobsBlock = Get-YamlTopLevelBlock -Text $WorkflowText -Name 'jobs'
    $jobMatches = [regex]::Matches($jobsBlock, '(?m)^  ([A-Za-z0-9_-]+):\s*$')
    Assert-Condition ($jobMatches.Count -gt 0) 'Workflow jobs block is empty.'

    $result = [ordered]@{}
    for ($index = 0; $index -lt $jobMatches.Count; $index++) {
        $jobId = $jobMatches[$index].Groups[1].Value
        $start = $jobMatches[$index].Index
        $end = if ($index + 1 -lt $jobMatches.Count) { $jobMatches[$index + 1].Index } else { $jobsBlock.Length }
        Assert-Condition (-not $result.Contains($jobId)) "Workflow has duplicate job id '$jobId'."
        $result[$jobId] = $jobsBlock.Substring($start, $end - $start)
    }
    return $result
}

function Test-DistributionProducerResults {
    param(
        [Parameter(Mandatory = $true)][bool]$Relevant,
        [Parameter(Mandatory = $true)][hashtable]$Results
    )

    Assert-Condition ($Results.ContainsKey('changes')) "Producer fixture is missing 'changes'."
    if ([string]$Results.changes -cne 'success') {
        return [ordered]@{ status = 'failure'; applicable = $true; producersOk = $false }
    }

    $mandatory = @($Results.Keys | Where-Object { $_ -cne 'changes' })
    Assert-Condition ($mandatory.Count -gt 0) 'Producer fixture has no mandatory producers.'
    if ($Relevant) {
        $allSucceeded = @($mandatory | Where-Object { [string]$Results[$_] -cne 'success' }).Count -eq 0
        return [ordered]@{
            status = if ($allSucceeded) { 'pendingAggregate' } else { 'failure' }
            applicable = $true
            producersOk = $allSucceeded
        }
    }

    $allSkipped = @($mandatory | Where-Object { [string]$Results[$_] -cne 'skipped' }).Count -eq 0
    return [ordered]@{
        status = if ($allSkipped) { 'notApplicable' } else { 'failure' }
        applicable = -not $allSkipped
        producersOk = $allSkipped
    }
}

function Assert-ProducerFixtureAccepted {
    param([Parameter(Mandatory = $true)][object]$Outcome)

    Assert-Condition ([string]$Outcome.status -cne 'failure') 'Producer fixture must be rejected fail-closed.'
}

function Replace-WorkflowFixtureOnce {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $expression = [regex]::new($Pattern)
    Assert-Condition ($expression.IsMatch($Text)) "Workflow fixture '$Name' could not find its mutation target."
    return $expression.Replace($Text, $Replacement, 1)
}

function Get-WorkflowNamedStepBlock {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowText,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $normalized = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $pattern = "(?m)^      - name:\s*$([regex]::Escape($StepName))\s*$"
    $matches = [regex]::Matches($normalized, $pattern)
    Assert-Condition ($matches.Count -eq 1) "Workflow must contain exactly one step named '$StepName'."

    $start = $matches[0].Index
    $nextStep = $normalized.IndexOf("`n      - name:", $start + $matches[0].Length, [StringComparison]::Ordinal)
    if ($nextStep -lt 0) { $nextStep = $normalized.Length }
    return $normalized.Substring($start, $nextStep - $start)
}

function Get-EmbeddedWorkflowPythonBlock {
    param(
        [Parameter(Mandatory = $true)][string]$StepBlock,
        [Parameter(Mandatory = $true)][string[]]$RequiredSnippets,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $lines = @($StepBlock.Replace("`r`n", "`n").Replace("`r", "`n") -split "`n")
    $matchingBlocks = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cnotmatch "^ *[^`n]*<<'PY'\s*$") { continue }

        $rawCodeLines = [System.Collections.Generic.List[string]]::new()
        $indent = $null
        $terminated = $false
        for ($codeIndex = $index + 1; $codeIndex -lt $lines.Count; $codeIndex++) {
            $line = [string]$lines[$codeIndex]
            if ($line -cmatch '^(?<indent> *)PY\s*$') {
                $indent = [string]$Matches.indent
                $index = $codeIndex
                $terminated = $true
                break
            }
            $rawCodeLines.Add($line)
        }
        Assert-Condition $terminated "Embedded Python block '$DisplayName' has no closing PY marker."

        $codeLines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in $rawCodeLines) {
            Assert-Condition (
                [string]::IsNullOrEmpty($line) -or $line.StartsWith($indent, [StringComparison]::Ordinal)) `
                "Embedded Python block '$DisplayName' escapes its YAML run indentation."
            $codeLines.Add($(if ([string]::IsNullOrEmpty($line)) { '' } else { $line.Substring($indent.Length) }))
        }
        $code = $codeLines -join "`n"
        $containsEverySnippet = $true
        foreach ($snippet in $RequiredSnippets) {
            if (-not $code.Contains($snippet, [StringComparison]::Ordinal)) {
                $containsEverySnippet = $false
                break
            }
        }
        if ($containsEverySnippet) { $matchingBlocks.Add($code) }
    }

    Assert-Condition ($matchingBlocks.Count -eq 1) "Workflow must expose exactly one embedded Python block for '$DisplayName'."
    return $matchingBlocks[0]
}

function Resolve-PythonExecutable {
    if (-not [string]::IsNullOrWhiteSpace([string]$script:pythonExecutable)) {
        return $script:pythonExecutable
    }

    $runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
    $candidateNames = if ($runningOnWindows) { @('python', 'python3') } else { @('python3', 'python') }
    foreach ($candidateName in $candidateNames) {
        foreach ($command in @(Get-Command $candidateName -CommandType Application -All -ErrorAction SilentlyContinue)) {
            $source = [string]$command.Source
            if ($runningOnWindows -and $source -match '(?i)[\\/]WindowsApps[\\/]') { continue }
            $script:pythonExecutable = $source
            return $script:pythonExecutable
        }
    }
    throw 'Python 3 is required to execute the embedded workflow contract fixtures.'
}

function Invoke-EmbeddedWorkflowPython {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [string[]]$ScriptArguments = @(),
        [hashtable]$Environment = @{}
    )

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-workflow-python-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $process = $null
    try {
        $scriptPath = Join-Path $temporaryRoot 'embedded-workflow.py'
        [System.IO.File]::WriteAllText($scriptPath, $Script + "`n", [System.Text.UTF8Encoding]::new($false))

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = Resolve-PythonExecutable
        $startInfo.WorkingDirectory = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add($scriptPath)
        foreach ($argument in $ScriptArguments) { $startInfo.ArgumentList.Add([string]$argument) }
        foreach ($key in $Environment.Keys) { $startInfo.Environment[[string]$key] = [string]$Environment[$key] }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        Assert-Condition $process.Start() 'Failed to start Python for an embedded workflow fixture.'
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return [pscustomobject][ordered]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutput
            StandardError = $standardError
        }
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-EmbeddedPythonSucceeded {
    param(
        [Parameter(Mandatory = $true)][object]$Result,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-Condition (
        [int]$Result.ExitCode -eq 0) `
        "Embedded workflow fixture '$Name' failed: $([string]$Result.StandardError)"
}

function Assert-EmbeddedPythonRejected {
    param(
        [Parameter(Mandatory = $true)][object]$Result,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    if ([int]$Result.ExitCode -eq 0) {
        throw "Negative fixture '$Name' unexpectedly passed."
    }
    $combinedOutput = ([string]$Result.StandardOutput) + "`n" + ([string]$Result.StandardError)
    Assert-Condition (
        $combinedOutput.Contains($ExpectedMessage, [StringComparison]::Ordinal)) `
        "Negative fixture '$Name' failed for an unexpected reason: $combinedOutput"
    $script:negativeFixtureCount++
    Add-Check -Name "negative:$Name"
}

function Write-JsonUtf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Document
    )

    $json = $Document | ConvertTo-Json -Depth 100 -Compress
    [System.IO.File]::WriteAllText($Path, $json + "`n", [System.Text.UTF8Encoding]::new($false))
}

function Read-GitHubOutputMap {
    param([Parameter(Mandatory = $true)][string]$Path)

    $outputs = @{}
    foreach ($line in @(Get-Content -LiteralPath $Path -Encoding utf8)) {
        $separator = $line.IndexOf('=', [StringComparison]::Ordinal)
        Assert-Condition ($separator -gt 0) "Invalid GITHUB_OUTPUT fixture line: '$line'."
        $outputs[$line.Substring(0, $separator)] = $line.Substring($separator + 1)
    }
    return $outputs
}

function New-EmbeddedProducerEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$GitHubOutputPath,
        [switch]$DuplicateArtifactId
    )

    $environment = @{
        RELEVANT = 'true'
        CHANGES_RESULT = 'success'
        CONTRACT_RESULT = 'success'
        WINDOWS_RESULT = 'success'
        LINUX_RESULT = 'success'
        MACOS_X64_RESULT = 'success'
        MACOS_ARM64_RESULT = 'success'
        ANDROID_BUILD_RESULT = 'success'
        ANDROID_API23_RESULT = 'success'
        ANDROID_API36_RESULT = 'success'
        GITHUB_SHA = ('1' * 40)
        WORKFLOW_SHA = ('2' * 40)
        EVENT_NAME = 'pull_request'
        PR_HEAD_SHA = ('1' * 40)
        PR_BASE_SHA = ('3' * 40)
        GITHUB_RUN_ID = '424242'
        GITHUB_RUN_ATTEMPT = '7'
        GITHUB_OUTPUT = $GitHubOutputPath
    }
    $producerPlan = @(
        [pscustomobject]@{ Name = 'contract'; Prefix = 'CONTRACT'; Attempt = 3 },
        [pscustomobject]@{ Name = 'windows-x64'; Prefix = 'WINDOWS'; Attempt = 1 },
        [pscustomobject]@{ Name = 'linux-x64'; Prefix = 'LINUX'; Attempt = 4 },
        [pscustomobject]@{ Name = 'macos-x64'; Prefix = 'MACOS_X64'; Attempt = 2 },
        [pscustomobject]@{ Name = 'macos-arm64'; Prefix = 'MACOS_ARM64'; Attempt = 5 },
        [pscustomobject]@{ Name = 'android-multi'; Prefix = 'ANDROID'; Attempt = 2 },
        [pscustomobject]@{ Name = 'android-api23'; Prefix = 'ANDROID_API23'; Attempt = 6 },
        [pscustomobject]@{ Name = 'android-api36'; Prefix = 'ANDROID_API36'; Attempt = 1 }
    )

    $artifactId = 1001
    foreach ($producer in $producerPlan) {
        $baseName = "distribution-$($producer.Name)-fixture-attempt-$($producer.Attempt)"
        $environment["$($producer.Prefix)_NAME"] = $baseName
        $environment["$($producer.Prefix)_ID"] = [string]$artifactId
        $environment["$($producer.Prefix)_DIGEST"] = ('{0:x64}' -f $artifactId)
        $artifactId++
        $environment["$($producer.Prefix)_RECEIPT_NAME"] = "$baseName-receipt"
        $environment["$($producer.Prefix)_RECEIPT_ID"] = [string]$artifactId
        $environment["$($producer.Prefix)_RECEIPT_DIGEST"] = ('{0:x64}' -f $artifactId)
        $artifactId++
    }
    if ($DuplicateArtifactId) {
        $environment.ANDROID_API36_RECEIPT_ID = $environment.CONTRACT_ID
    }
    return $environment
}

function Invoke-EmbeddedProducerInspectionFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$DuplicateArtifactId
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $producerPath = Join-Path $Root 'producer-results.json'
    $githubOutputPath = Join-Path $Root 'github-output.txt'
    $environment = New-EmbeddedProducerEnvironment -GitHubOutputPath $githubOutputPath -DuplicateArtifactId:$DuplicateArtifactId
    $result = Invoke-EmbeddedWorkflowPython -Script $Script -ScriptArguments @($producerPath) -Environment $environment
    Assert-EmbeddedPythonSucceeded -Result $result -Name 'producer-inspection'
    return [pscustomobject][ordered]@{
        Result = $result
        Document = Read-JsonFile -Path $producerPath -DisplayName 'Embedded producer inspection output'
        Outputs = Read-GitHubOutputMap -Path $githubOutputPath
        Environment = $environment
        ProducerPath = $producerPath
    }
}

function Get-EmbeddedProducerTransports {
    param([Parameter(Mandatory = $true)][object]$ProducerDocument)

    $transports = [System.Collections.Generic.List[object]]::new()
    foreach ($producerProperty in $ProducerDocument.artifacts.psobject.Properties) {
        foreach ($role in @('main', 'receipt')) {
            $transports.Add($producerProperty.Value.$role)
        }
    }
    return @($transports)
}

function New-EmbeddedAggregateDirectoryFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$ProducerDocument
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    foreach ($transport in @(Get-EmbeddedProducerTransports -ProducerDocument $ProducerDocument)) {
        [System.IO.Directory]::CreateDirectory((Join-Path $Root ([string]$transport.name))) | Out-Null
    }
}

function New-EmbeddedReceiptValidationFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$ProducerDocument,
        [Parameter(Mandatory = $true)][object]$IdentityDocument
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $identityPath = Join-Path $Root 'identity.json'
    $producerPath = Join-Path $Root 'producer-results.json'
    Write-JsonUtf8NoBom -Path $identityPath -Document $IdentityDocument
    Write-JsonUtf8NoBom -Path $producerPath -Document $ProducerDocument

    $identityBinding = [ordered]@{}
    foreach ($key in @('rawTag', 'normalizedVersion', 'sourceSha', 'workflowSha', 'tagBinding', 'manifestSha256', 'supportMatrixSha256', 'signatureProfile')) {
        $identityBinding[$key] = $IdentityDocument.$key
    }
    $receiptPaths = @{}
    $payloadRoots = @{}
    foreach ($producerName in @('contract', 'android_api23', 'android_api36')) {
        $producerTransport = $ProducerDocument.artifacts.$producerName
        $payloadRoot = Join-Path $Root ([string]$producerTransport.main.name)
        $receiptRoot = Join-Path $Root ([string]$producerTransport.receipt.name)
        [System.IO.Directory]::CreateDirectory($payloadRoot) | Out-Null
        [System.IO.Directory]::CreateDirectory($receiptRoot) | Out-Null

        if ($producerName -ceq 'contract') {
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'identity.json') -Document $IdentityDocument
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'contract-evidence.json') -Document ([ordered]@{ status = 'pass' })
        }
        else {
            $api = if ($producerName -ceq 'android_api23') { '23' } else { '36' }
            $logcatName = "android-api$api-logcat.txt"
            $emulatorLogName = "android-api$api-emulator.log"
            $logcatPath = Join-Path $payloadRoot $logcatName
            $emulatorLogPath = Join-Path $payloadRoot $emulatorLogName
            [System.IO.File]::WriteAllText($logcatPath, "api$api-logcat-fixture`n", [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText($emulatorLogPath, "api$api-emulator-fixture`n", [System.Text.UTF8Encoding]::new($false))
            $evidence = [ordered]@{
                runtime = [ordered]@{
                    logcat = [ordered]@{
                        fileName = $logcatName
                        sha256 = Get-LowerFileSha256 -Path $logcatPath
                        bytes = (Get-Item -LiteralPath $logcatPath).Length
                    }
                    emulatorLog = [ordered]@{
                        fileName = $emulatorLogName
                        sha256 = Get-LowerFileSha256 -Path $emulatorLogPath
                        bytes = (Get-Item -LiteralPath $emulatorLogPath).Length
                    }
                }
            }
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'evidence.json') -Document $evidence
            Write-JsonUtf8NoBom -Path (Join-Path $payloadRoot 'download-transport.json') -Document ([ordered]@{ status = 'pass' })
        }

        $payloads = @(
            Get-ChildItem -LiteralPath $payloadRoot -File | Sort-Object Name | ForEach-Object {
                [ordered]@{
                    fileName = $_.Name
                    sha256 = Get-LowerFileSha256 -Path $_.FullName
                }
            }
        )
        $receipt = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-evidence-transport-receipt'
            identity = $identityBinding
            artifact = [ordered]@{
                name = [string]$producerTransport.main.name
                id = [string]$producerTransport.main.id
                digest = ([string]$producerTransport.main.digest).ToLowerInvariant()
                retentionDays = 7
            }
            payloads = $payloads
            productionReady = $false
        }
        $receiptPath = Join-Path $receiptRoot 'evidence-transport-receipt.json'
        Write-JsonUtf8NoBom -Path $receiptPath -Document $receipt
        $receiptPaths[$producerName] = $receiptPath
        $payloadRoots[$producerName] = $payloadRoot
    }

    return [pscustomobject][ordered]@{
        Root = $Root
        IdentityPath = $identityPath
        ProducerPath = $producerPath
        ReceiptPaths = $receiptPaths
        PayloadRoots = $payloadRoots
    }
}

function Invoke-EmbeddedReceiptValidator {
    param(
        [Parameter(Mandatory = $true)][string]$Script,
        [Parameter(Mandatory = $true)][object]$Fixture
    )

    return Invoke-EmbeddedWorkflowPython -Script $Script -ScriptArguments @(
        [string]$Fixture.Root,
        [string]$Fixture.IdentityPath,
        [string]$Fixture.ProducerPath)
}

function Test-EmbeddedWorkflowBehaviorFixtures {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $inspectStep = Get-WorkflowNamedStepBlock -WorkflowText $WorkflowText -StepName 'Inspect every producer result'
    $aggregateStep = Get-WorkflowNamedStepBlock -WorkflowText $WorkflowText -StepName 'Require exact artifact set after bounded transport'
    $inspectScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $inspectStep `
        -RequiredSnippets @('artifact_prefixes = (', 'artifact_ids = []', 'stream.write(f"artifact_ids=') `
        -DisplayName 'producer inspection'
    $exactDirectoryScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $aggregateStep `
        -RequiredSnippets @('Expected 16 unique producer artifact names', 'Downloaded artifact set mismatch') `
        -DisplayName 'exact aggregate directory set'
    $receiptScript = Get-EmbeddedWorkflowPythonBlock -StepBlock $aggregateStep `
        -RequiredSnippets @('receipt_plan = {', 'Receipt payload hash mismatch:', 'does not bind the exact sidecar bytes') `
        -DisplayName 'receipt and Android runtime sidecar validation'
    Add-Check -Name 'workflow-behavior:embedded-python-extracted-by-step'

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-workflow-behavior-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $positiveInspection = Invoke-EmbeddedProducerInspectionFixture `
            -Script $inspectScript -Root (Join-Path $temporaryRoot 'inspect-positive')
        Assert-Condition ($positiveInspection.Document.status -ceq 'pendingAggregate') 'Embedded inspection must keep the mixed-attempt producer set pendingAggregate.'
        Assert-Condition ($positiveInspection.Outputs.producers_ok -ceq 'true') 'Embedded inspection must accept 16 valid unique producer artifact ids.'
        $positiveTransports = @(Get-EmbeddedProducerTransports -ProducerDocument $positiveInspection.Document)
        $positiveIds = @($positiveTransports | ForEach-Object { [string]$_.id })
        $positiveNames = @($positiveTransports | ForEach-Object { [string]$_.name })
        Assert-Condition ($positiveIds.Count -eq 16 -and @($positiveIds | Sort-Object -Unique).Count -eq 16) 'Positive producer fixture must expose 16 unique main/receipt artifact ids.'
        Assert-Condition ($positiveNames.Count -eq 16 -and @($positiveNames | Sort-Object -Unique).Count -eq 16) 'Positive producer fixture must expose 16 unique main/receipt artifact names.'
        $reportedIds = @([string]$positiveInspection.Outputs.artifact_ids -split ',')
        Assert-Condition ($reportedIds.Count -eq 16 -and @($reportedIds | Sort-Object -Unique).Count -eq 16) 'Embedded inspection must emit exactly 16 unique artifact ids.'
        Assert-Condition (
            ((@($reportedIds | Sort-Object) -join ',') -ceq (@($positiveIds | Sort-Object) -join ','))) `
            'Embedded inspection artifact_ids output must bind the exact main/receipt id set.'
        $attempts = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($name in $positiveNames) {
            Assert-Condition ($name -cmatch '-attempt-([0-9]+)(?:-receipt)?$') "Mixed-attempt fixture artifact name '$name' is not attempt-scoped."
            $attempts.Add($Matches[1]) | Out-Null
        }
        Assert-Condition ($attempts.Count -gt 1) 'Positive producer fixture must exercise mixed producer run attempts.'
        Add-Check -Name 'workflow-behavior:embedded-inspect-mixed-attempts-16-unique-ids'

        $duplicateInspection = Invoke-EmbeddedProducerInspectionFixture `
            -Script $inspectScript -Root (Join-Path $temporaryRoot 'inspect-duplicate') -DuplicateArtifactId
        $duplicateIds = @(
            Get-EmbeddedProducerTransports -ProducerDocument $duplicateInspection.Document |
                ForEach-Object { [string]$_.id })
        Assert-Condition (@($duplicateIds | Sort-Object -Unique).Count -eq 15) 'Duplicate-id fixture must expose exactly 15 unique ids across 16 transports.'
        Assert-Condition ($duplicateInspection.Document.status -ceq 'failure') 'Embedded inspection must fail closed when 16 transports do not have 16 unique ids.'
        Assert-Condition ($duplicateInspection.Outputs.producers_ok -ceq 'false') 'Duplicate producer artifact id must clear producers_ok.'
        Assert-Condition ([string]::IsNullOrEmpty([string]$duplicateInspection.Outputs.artifact_ids)) 'Duplicate producer artifact id must suppress the aggregate artifact id list.'
        Assert-Condition (
            ([string]$duplicateInspection.Document.reason).Contains('duplicate id=', [StringComparison]::Ordinal)) `
            'Duplicate producer artifact id must be reported as the fail-closed reason.'
        $script:negativeFixtureCount++
        Add-Check -Name 'negative:workflow-embedded-inspect-not-16-unique-ids'

        $producerPath = Join-Path $temporaryRoot 'aggregate-producer-results.json'
        Write-JsonUtf8NoBom -Path $producerPath -Document $positiveInspection.Document
        $downloadRoot = Join-Path $temporaryRoot 'aggregate-download'
        New-EmbeddedAggregateDirectoryFixture -Root $downloadRoot -ProducerDocument $positiveInspection.Document
        $exactResult = Invoke-EmbeddedWorkflowPython -Script $exactDirectoryScript -ScriptArguments @($downloadRoot, $producerPath)
        Assert-EmbeddedPythonSucceeded -Result $exactResult -Name 'exact aggregate directory set'
        Add-Check -Name 'workflow-behavior:embedded-aggregate-exact-16-directories'
        [System.IO.Directory]::CreateDirectory((Join-Path $downloadRoot 'distribution-android-api23-fixture-failure-attempt-1')) | Out-Null
        $staleResult = Invoke-EmbeddedWorkflowPython -Script $exactDirectoryScript -ScriptArguments @($downloadRoot, $producerPath)
        Assert-EmbeddedPythonRejected -Result $staleResult `
            -Name 'workflow-embedded-aggregate-stale-failure-directory' `
            -ExpectedMessage 'Downloaded artifact set mismatch'

        $identityDocument = [pscustomobject][ordered]@{
            rawTag = 'v1.2.3'
            normalizedVersion = '1.2.3'
            sourceSha = ('1' * 40)
            workflowSha = ('2' * 40)
            tagBinding = 'notApplicable'
            manifestSha256 = ('4' * 64)
            supportMatrixSha256 = ('5' * 64)
            signatureProfile = 'validation'
        }
        $receiptPositive = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-positive') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $receiptPositiveResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $receiptPositive
        Assert-EmbeddedPythonSucceeded -Result $receiptPositiveResult -Name 'receipt and Android runtime sidecar validation'
        Add-Check -Name 'workflow-behavior:embedded-receipt-runtime-sidecar-positive'

        $receiptMismatch = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'receipt-payload-mismatch') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $contractReceipt = Read-JsonFile -Path $receiptMismatch.ReceiptPaths.contract -DisplayName 'Contract receipt mismatch fixture'
        @($contractReceipt.payloads | Where-Object fileName -ceq 'identity.json')[0].sha256 = ('0' * 64)
        Write-JsonUtf8NoBom -Path $receiptMismatch.ReceiptPaths.contract -Document $contractReceipt
        $receiptMismatchResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $receiptMismatch
        Assert-EmbeddedPythonRejected -Result $receiptMismatchResult `
            -Name 'workflow-embedded-receipt-payload-hash-mismatch' `
            -ExpectedMessage 'Receipt payload hash mismatch: contract/identity.json'

        $runtimeMismatch = New-EmbeddedReceiptValidationFixture `
            -Root (Join-Path $temporaryRoot 'runtime-sidecar-mismatch') `
            -ProducerDocument $positiveInspection.Document -IdentityDocument $identityDocument
        $api23LogcatPath = Join-Path $runtimeMismatch.PayloadRoots.android_api23 'android-api23-logcat.txt'
        [System.IO.File]::AppendAllText($api23LogcatPath, "mutated-sidecar`n", [System.Text.UTF8Encoding]::new($false))
        $api23Receipt = Read-JsonFile -Path $runtimeMismatch.ReceiptPaths.android_api23 -DisplayName 'Android API 23 runtime mismatch receipt'
        @($api23Receipt.payloads | Where-Object fileName -ceq 'android-api23-logcat.txt')[0].sha256 = Get-LowerFileSha256 -Path $api23LogcatPath
        Write-JsonUtf8NoBom -Path $runtimeMismatch.ReceiptPaths.android_api23 -Document $api23Receipt
        $runtimeMismatchResult = Invoke-EmbeddedReceiptValidator -Script $receiptScript -Fixture $runtimeMismatch
        Assert-EmbeddedPythonRejected -Result $runtimeMismatchResult `
            -Name 'workflow-embedded-android-runtime-sidecar-mismatch' `
            -ExpectedMessage 'android_api23 runtime.logcat does not bind the exact sidecar bytes'
    }
    finally {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-WorkflowSecurityContract {
    param([Parameter(Mandatory = $true)][string]$WorkflowText)

    $normalized = $WorkflowText.Replace("`r`n", "`n").Replace("`r", "`n")
    $onBlock = Get-YamlTopLevelBlock -Text $normalized -Name 'on'
    $eventNames = @([regex]::Matches($onBlock, '(?m)^  ([A-Za-z0-9_-]+):\s*$') | ForEach-Object { $_.Groups[1].Value })
    Assert-Condition (
        (($eventNames | Sort-Object) -join ',') -ceq 'pull_request,workflow_dispatch') `
        'Standalone distribution workflow must expose only pull_request and workflow_dispatch triggers.'
    Assert-Condition ($onBlock -cnotmatch '(?m)^    paths(?:-ignore)?:') 'pull_request must not use a path filter because the stable verdict is required for every PR.'
    Assert-Condition ($onBlock -cmatch '(?m)^      raw_tag:\s*$') 'workflow_dispatch must expose only the synthetic raw_tag input.'
    Assert-Condition ($onBlock -cnotmatch '(?m)^      (?:source|source_sha|ref|release|publish):\s*$') 'workflow_dispatch must not accept a source or publication input.'

    Assert-Condition ($normalized -cmatch '(?m)^permissions:\s*\n  contents:\s*read\s*$') 'Workflow-level permissions must be contents: read.'
    Assert-Condition ($normalized -cnotmatch '(?im)^\s*(?:permissions:\s*)?write-all\s*$') 'Workflow must not grant write-all.'
    Assert-Condition ($normalized -cnotmatch '(?im)^\s*[A-Za-z][A-Za-z0-9_-]*:\s*write\s*$') 'Standalone validation must not grant any write permission.'
    Assert-Condition ($normalized -cnotmatch '(?i)\$\{\{\s*secrets\s*[.\[]') 'Standalone validation must not reference repository or environment secrets.'
    Assert-Condition ($normalized -cnotmatch '(?i)\bGITHUB_TOKEN\b') 'Standalone validation must not expose GITHUB_TOKEN.'

    $mutationPatterns = @(
        '(?im)\bgh\s+release\s+(?:create|upload|edit|delete)\b',
        '(?im)\bgh\s+api\b[^\n]*(?:--method|-X)\s*(?:POST|PUT|PATCH|DELETE)\b',
        '(?im)\bgit\s+(?:push|tag\s+(?!-l\b|--list\b))',
        '(?im)\bcurl\b[^\n]*(?:-X|--request)\s*(?:POST|PUT|PATCH|DELETE)\b[^\n]*(?:api|uploads)\.github\.com',
        '(?im)\b(?:softprops/action-gh-release|ncipollo/release-action|actions/create-release|marvinpinto/action-automatic-releases)@'
    )
    foreach ($pattern in $mutationPatterns) {
        Assert-Condition ($normalized -cnotmatch $pattern) "Standalone validation contains a forbidden release/repository mutation matching '$pattern'."
    }

    $usesMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*([^\s#]+)')
    Assert-Condition ($usesMatches.Count -gt 0) 'Workflow has no action references to validate.'
    foreach ($usesMatch in $usesMatches) {
        $reference = $usesMatch.Groups[1].Value
        if ($reference.StartsWith('./', [StringComparison]::Ordinal)) { continue }
        Assert-Condition (
            $reference -cmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$') `
            "External action '$reference' must use a full lowercase commit SHA."
    }

    $jobBlocks = Get-WorkflowJobBlocks -WorkflowText $normalized
    foreach ($jobId in $jobBlocks.Keys) {
        Assert-Condition (
            [string]$jobBlocks[$jobId] -cmatch '(?m)^    permissions:\s*\n      contents:\s*read\s*$') `
            "Job '$jobId' must explicitly use contents: read."
    }
    Assert-Condition ($jobBlocks.Contains('distribution-verdict')) "Workflow is missing stable job 'distribution-verdict'."
    $finalBlock = [string]$jobBlocks['distribution-verdict']
    Assert-Condition ($finalBlock -cmatch '(?m)^    name:\s*distribution-verdict\s*$') 'Stable final job name must remain distribution-verdict.'
    Assert-Condition ($finalBlock -cmatch '(?m)^    if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$') 'Stable final job must run under always().'
    foreach ($jobId in @($jobBlocks.Keys | Where-Object { $_ -cne 'distribution-verdict' })) {
        Assert-Condition (
            $finalBlock -cmatch "(?m)^      - $([regex]::Escape($jobId))\s*$") `
            "Stable final job must declare '$jobId' in needs."
    }

    Assert-Condition ($finalBlock -cmatch 'if results\["changes"\] != "success":') 'Final evaluator must fail when the scope producer does not succeed.'
    Assert-Condition ($finalBlock -cmatch 'elif relevant == "false":') 'Final evaluator must handle irrelevant changes explicitly.'
    Assert-Condition ($finalBlock -cmatch 'if value != "skipped"') 'Irrelevant changes require every mandatory producer to be skipped.'
    Assert-Condition ($finalBlock -cmatch 'elif relevant == "true":') 'Final evaluator must handle relevant changes explicitly.'
    Assert-Condition ($finalBlock -cmatch 'all\(value == "success" for value in mandatory\.values\(\)\)') 'Relevant changes require every mandatory producer to succeed.'

    Assert-Condition ($finalBlock -cmatch '(?m)^.*source_short=.*GITHUB_SHA.*$') 'Final inspection must derive source_short directly from GITHUB_SHA.'
    Assert-Condition (
        $finalBlock -cmatch '(?ms)^      - name:\s*Upload final verdict evidence\s*$.*?^          name:\s*distribution-verdict-\$\{\{\s*steps\.inspect\.outputs\.source_short\s*\}\}-attempt-\$\{\{\s*github\.run_attempt\s*\}\}\s*$') `
        'Final verdict artifact name must use the direct inspect source_short output and workflow run attempt.'
    Assert-Condition ($finalBlock -cmatch '\$\{\{\s*job\.workflow_sha\s*\}\}') 'Final evidence must read the immutable workflow SHA from job.workflow_sha.'
    Assert-Condition ($finalBlock -cmatch '["'']workflowSha["'']') 'Final machine evidence must record workflowSha.'

    Assert-Condition ($normalized -cmatch 'global\\\.json\$') 'Distribution scope must include root global.json.'
    Assert-Condition ($normalized -cmatch '\\\.gitattributes\$') 'Distribution scope must include root .gitattributes.'
    Assert-Condition ($normalized -cmatch 'Directory\\\.\(Build\|Packages\)\\\.\(props\|targets\)\$') 'Distribution scope must include root Directory.Build/Packages props/targets.'
    Assert-Condition ($normalized -cmatch '\[Nn\]u\[Gg\]et\\\.config\$') 'Distribution scope must include root NuGet.config case variants.'

    $uploadMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*actions/upload-artifact@[0-9a-f]{40}\s*(?:#.*)?$')
    Assert-Condition ($uploadMatches.Count -gt 0) 'Workflow has no upload-artifact invocation.'
    foreach ($uploadMatch in $uploadMatches) {
        $stepStart = $normalized.LastIndexOf("`n      - name:", $uploadMatch.Index, [StringComparison]::Ordinal)
        Assert-Condition ($stepStart -ge 0) 'upload-artifact invocation must belong to a named step.'
        $nextStep = $normalized.IndexOf("`n      - name:", $uploadMatch.Index + $uploadMatch.Length, [StringComparison]::Ordinal)
        if ($nextStep -lt 0) { $nextStep = $normalized.Length }
        $stepBlock = $normalized.Substring($stepStart, $nextStep - $stepStart)
        Assert-Condition ($stepBlock -cmatch '(?m)^          if-no-files-found:\s*error\s*$') 'Every artifact upload must fail when files are missing.'
        Assert-Condition ($stepBlock -cmatch '(?m)^          overwrite:\s*false\s*$') 'Every artifact upload must forbid overwrite.'
        Assert-Condition ($stepBlock -cmatch '(?m)^          retention-days:\s*7\s*$') 'Every artifact upload must retain evidence for seven days.'
        Assert-Condition ($stepBlock -cnotmatch '(?m)^        continue-on-error:\s*true\s*$') 'Artifact upload must be one fail-closed atomic invocation.'
        Assert-Condition ([regex]::Matches($stepBlock, '(?m)^        uses:\s*actions/upload-artifact@').Count -eq 1) 'Each artifact upload step must invoke upload-artifact exactly once.'
        Assert-Condition (
            $stepBlock -cmatch '(?m)^          name:\s*.+\$\{\{\s*github\.run_attempt\s*\}\}.*$') `
            'Every artifact upload name must include github.run_attempt.'
    }

    $downloadMatches = [regex]::Matches($normalized, '(?m)^\s*uses:\s*actions/download-artifact@[0-9a-f]{40}\s*(?:#.*)?$')
    Assert-Condition ($downloadMatches.Count -eq 6) 'Workflow must contain exactly four Android API and two aggregate artifact downloads.'
    $androidDownloadCount = 0
    $aggregateDownloadCount = 0
    foreach ($downloadMatch in $downloadMatches) {
        $stepStart = $normalized.LastIndexOf("`n      - name:", $downloadMatch.Index, [StringComparison]::Ordinal)
        Assert-Condition ($stepStart -ge 0) 'download-artifact invocation must belong to a named step.'
        $nextStep = $normalized.IndexOf("`n      - name:", $downloadMatch.Index + $downloadMatch.Length, [StringComparison]::Ordinal)
        if ($nextStep -lt 0) { $nextStep = $normalized.Length }
        $stepBlock = $normalized.Substring($stepStart, $nextStep - $stepStart)
        Assert-Condition ($stepBlock -cnotmatch '(?m)^          pattern:') 'Distribution evidence must never be downloaded through a broad artifact pattern.'

        if ($stepBlock -cmatch '(?m)^          artifact-ids:\s*\$\{\{\s*needs\.android_build\.outputs\.artifact_id\s*\}\}\s*$') {
            $androidDownloadCount++
            Assert-Condition ($stepBlock -cmatch '(?m)^          merge-multiple:\s*true\s*$') 'Each Android API download must flatten the exact producer artifact.'
            continue
        }
        if ($stepBlock -cmatch '(?m)^          artifact-ids:\s*\$\{\{\s*steps\.inspect\.outputs\.artifact_ids\s*\}\}\s*$') {
            $aggregateDownloadCount++
            Assert-Condition ($stepBlock -cmatch '(?m)^          merge-multiple:\s*false\s*$') 'Each aggregate download must preserve the exact producer artifact directories.'
            continue
        }
        throw 'Every distribution download must use an approved exact artifact-ids output.'
    }
    Assert-Condition ($androidDownloadCount -eq 4) 'Workflow must download the exact Android build artifact twice for each API cell.'
    Assert-Condition ($aggregateDownloadCount -eq 2) 'Workflow must download the exact inspected producer artifact set on both bounded attempts.'
}

function Test-InventoryContract {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument
    )

    Assert-Condition ($ManifestDocument.schemaVersion -eq 1) 'Manifest schemaVersion must equal 1.'
    Assert-Condition ($ManifestDocument.product -ceq 'Unlimotion') 'Manifest product must equal Unlimotion.'
    Assert-Condition ($FixtureDocument.schemaVersion -eq 1) 'Fixture schemaVersion must equal 1.'
    Assert-Condition ($FixtureDocument.product -ceq 'Unlimotion') 'Fixture product must equal Unlimotion.'
    Assert-Condition ($FixtureDocument.release.rawTag -ceq '1.27.0') 'Fixture rawTag must remain 1.27.0.'
    Assert-Condition ($FixtureDocument.release.normalizedVersion -ceq '1.27.0') 'Fixture normalizedVersion must remain 1.27.0.'
    Assert-Condition ($FixtureDocument.release.sourceSha -ceq '5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd') 'Fixture sourceSha drifted.'
    Assert-Condition ($FixtureDocument.release.assetCount -eq 22) 'Fixture assetCount must equal 22.'
    Assert-Condition (@($ManifestDocument.assets).Count -eq 22) 'Manifest must classify exactly 22 release assets.'
    Assert-Condition (@($FixtureDocument.assets).Count -eq 22) 'Fixture must contain exactly 22 release assets.'

    $requiredSupportLevels = @('present', 'metadataVerified', 'launchVerified', 'productionReady')
    Assert-Condition (
        (($ManifestDocument.supportLevels | ConvertTo-Json -Compress) -ceq ($requiredSupportLevels | ConvertTo-Json -Compress))) `
        'Manifest supportLevels must remain the ordered four-level contract.'
    Assert-Condition (
        $ManifestDocument.androidProductionCertificateSha256 -cmatch '^[0-9a-f]{64}$') `
        'Manifest Android production certificate fingerprint must be lowercase SHA-256.'
    $expectedLinuxPrerequisites = [ordered]@{
        debian12 = @('ca-certificates', 'libc6', 'libgcc-s1', 'libgssapi-krb5-2', 'libstdc++6', 'tzdata', 'zlib1g', 'libx11-6', 'libice6', 'libsm6', 'libfontconfig1', 'libicu72', 'libssl3')
        debian13 = @('ca-certificates', 'libc6', 'libgcc-s1', 'libgssapi-krb5-2', 'libstdc++6', 'tzdata', 'zlib1g', 'libx11-6', 'libice6', 'libsm6', 'libfontconfig1', 'libicu76', 'libssl3t64')
    }
    foreach ($debian in $expectedLinuxPrerequisites.Keys) {
        Assert-Condition (
            ((@($ManifestDocument.linuxRuntimePrerequisites.appImageExtractAndRun.$debian) | ConvertTo-Json -Compress) -ceq
                (@($expectedLinuxPrerequisites[$debian]) | ConvertTo-Json -Compress))) `
            "Manifest AppImage runtime prerequisite contract drifted for '$debian'."
    }
    Assert-Condition ((@($ManifestDocument.linuxRuntimePrerequisites.directFuseAdditional.debian12) -join ' ') -ceq 'libfuse2') 'Debian 12 direct-FUSE prerequisite drifted.'
    Assert-Condition ((@($ManifestDocument.linuxRuntimePrerequisites.directFuseAdditional.debian13) -join ' ') -ceq 'libfuse2t64') 'Debian 13 direct-FUSE prerequisite drifted.'

    $manifestById = New-OrdinalIgnoreCaseMap
    $generatedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $normalizedVersion = [string]$FixtureDocument.release.normalizedVersion
    foreach ($asset in @($ManifestDocument.assets)) {
        $assetId = [string]$asset.id
        Assert-Condition (-not $manifestById.ContainsKey($assetId)) "Duplicate manifest asset id '$assetId'."
        $manifestById.Add($assetId, $asset)

        $template = [string]$asset.filenameTemplate
        Assert-Condition ($template -notmatch '\{rawTag\}') "Asset '$assetId' uses forbidden rawTag placeholder."
        $generatedName = $template.Replace('{normalizedVersion}', $normalizedVersion)
        Assert-Condition ($generatedName -notmatch '[{}]') "Asset '$assetId' has an unknown filename placeholder."
        Assert-Condition ([System.IO.Path]::GetFileName($generatedName) -ceq $generatedName) "Asset '$assetId' generated a path instead of a filename."
        Assert-Condition ($generatedNames.Add($generatedName)) "Case-insensitive generated filename collision: '$generatedName'."

        if ([bool]$asset.legacy) {
            Assert-Condition ($asset.role -ceq 'legacyDuplicate') "Legacy asset '$assetId' must use role legacyDuplicate."
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$asset.legacyOwner)) "Legacy asset '$assetId' needs an owner."
            Assert-Condition ($asset.migrationStage -ge 4) "Legacy asset '$assetId' needs migrationStage >= 4."
        }
        else {
            Assert-Condition ($asset.role -cne 'legacyDuplicate') "Non-legacy asset '$assetId' cannot use role legacyDuplicate."
        }
    }

    $fixtureById = New-OrdinalIgnoreCaseMap
    $fixtureNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($fixtureAsset in @($FixtureDocument.assets)) {
        $assetId = [string]$fixtureAsset.assetId
        Assert-Condition (-not $fixtureById.ContainsKey($assetId)) "Duplicate fixture asset id '$assetId'."
        Assert-Condition ($manifestById.ContainsKey($assetId)) "Unexpected fixture asset id '$assetId'."
        Assert-Condition ($fixtureNames.Add([string]$fixtureAsset.name)) "Duplicate fixture asset name '$($fixtureAsset.name)'."
        Assert-Condition ($fixtureAsset.size -is [long] -or $fixtureAsset.size -is [int]) "Fixture asset '$assetId' size must be an integer."
        Assert-Condition ([long]$fixtureAsset.size -gt 0) "Fixture asset '$assetId' must not be zero-byte."
        Assert-Condition ([string]$fixtureAsset.sha256 -cmatch '^[0-9a-f]{64}$') "Fixture asset '$assetId' has invalid SHA-256."
        $createdAtIsValid = $fixtureAsset.createdAt -is [DateTime] -or $fixtureAsset.createdAt -is [DateTimeOffset]
        $updatedAtIsValid = $fixtureAsset.updatedAt -is [DateTime] -or $fixtureAsset.updatedAt -is [DateTimeOffset]
        Assert-Condition $createdAtIsValid "Fixture asset '$assetId' createdAt is invalid."
        Assert-Condition $updatedAtIsValid "Fixture asset '$assetId' updatedAt is invalid."

        $manifestAsset = $manifestById[$assetId]
        $expectedName = ([string]$manifestAsset.filenameTemplate).Replace('{normalizedVersion}', $normalizedVersion)
        Assert-Condition ([string]$fixtureAsset.name -ceq $expectedName) "Fixture asset '$assetId' name '$($fixtureAsset.name)' does not match '$expectedName'."
        Assert-Condition ([string]$fixtureAsset.role -ceq [string]$manifestAsset.role) "Fixture asset '$assetId' role does not match manifest."
        $expectedDownloadUrl = "https://github.com/Kibnet/Unlimotion/releases/download/1.27.0/$expectedName"
        Assert-Condition ([string]$fixtureAsset.downloadUrl -ceq $expectedDownloadUrl) "Fixture asset '$assetId' download URL drifted."
        if ($manifestAsset.role -ceq 'updaterPackage') {
            Assert-Condition ($fixtureAsset.PSObject.Properties.Name -contains 'sha1') "Updater package '$assetId' must record SHA-1 for legacy feed relation validation."
            Assert-Condition ([string]$fixtureAsset.sha1 -cmatch '^[0-9a-f]{40}$') "Updater package '$assetId' has invalid SHA-1."
        }
        $fixtureById.Add($assetId, $fixtureAsset)
    }

    foreach ($assetId in $manifestById.Keys) {
        Assert-Condition ($fixtureById.ContainsKey($assetId)) "Fixture is missing manifest asset '$assetId'."
    }

    $relationIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relation in @($ManifestDocument.relations)) {
        Assert-Condition ($relationIds.Add([string]$relation.id)) "Duplicate relation id '$($relation.id)'."
        Assert-Condition ($manifestById.ContainsKey([string]$relation.feedAssetId)) "Relation '$($relation.id)' references unknown feed asset."
        Assert-Condition ($manifestById.ContainsKey([string]$relation.packageAssetId)) "Relation '$($relation.id)' references unknown package asset."
        Assert-Condition ($manifestById[[string]$relation.feedAssetId].role -ceq 'updaterFeed') "Relation '$($relation.id)' feed role is not updaterFeed."
        Assert-Condition ($manifestById[[string]$relation.packageAssetId].role -ceq 'updaterPackage') "Relation '$($relation.id)' package role is not updaterPackage."
    }

    Assert-Condition (@($FixtureDocument.feeds).Count -eq @($ManifestDocument.relations).Count) 'Fixture feed payload count must match manifest relation count.'
}

function Test-SupportContract {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument,
        [Parameter(Mandatory = $true)][object]$SupportDocument
    )

    Assert-Condition ($SupportDocument.schemaVersion -eq 1) 'Support snapshot schemaVersion must equal 1.'
    Assert-Condition ($SupportDocument.product -ceq 'Unlimotion') 'Support snapshot product must equal Unlimotion.'
    Assert-Condition (-not [bool]$SupportDocument.candidateEvidenceAccepted) 'Support snapshot must reject candidate evidence promotion.'
    Assert-Condition ([bool]$SupportDocument.productionReadyDerived) 'productionReady must remain derived.'
    Assert-Condition ($SupportDocument.release.rawTag -ceq $FixtureDocument.release.rawTag) 'Support release tag differs from fixture.'
    Assert-Condition ($SupportDocument.release.normalizedVersion -ceq $FixtureDocument.release.normalizedVersion) 'Support normalized version differs from fixture.'
    Assert-Condition ($SupportDocument.release.sourceSha -ceq $FixtureDocument.release.sourceSha) 'Support source SHA differs from fixture.'
    Assert-Condition ($SupportDocument.release.publishedAt -ceq $FixtureDocument.release.publishedAt) 'Support publishedAt differs from fixture.'
    Assert-Condition ($SupportDocument.release.releaseUrl -ceq $FixtureDocument.release.releaseUrl) 'Support release URL differs from fixture.'
    Assert-Condition ($SupportDocument.lastPublishedAndroidVersionCode -eq 353) 'Current support snapshot must retain Android versionCode 353.'
    Assert-Condition (@($SupportDocument.claims).Count -eq 7) 'Current README-facing support snapshot must contain seven claims.'

    $manifestById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ManifestDocument.assets)) {
        $manifestById.Add([string]$asset.id, $asset)
    }
    $fixtureById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($FixtureDocument.assets)) {
        $fixtureById.Add([string]$asset.assetId, $asset)
    }

    $canonicalPublicAssets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @($ManifestDocument.assets)) {
        if (-not [bool]$asset.legacy -and $asset.role -in @('userInstaller', 'userPortable')) {
            $null = $canonicalPublicAssets.Add([string]$asset.id)
        }
    }

    $claimIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $claimedAssets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($claim in @($SupportDocument.claims)) {
        Assert-Condition ($claimIds.Add([string]$claim.id)) "Duplicate support claim id '$($claim.id)'."
        Assert-Condition ($claim.releaseTag -ceq $FixtureDocument.release.rawTag) "Support claim '$($claim.id)' tag differs from release snapshot."
        Assert-Condition ($claim.sourceSha -ceq $FixtureDocument.release.sourceSha) "Support claim '$($claim.id)' source SHA differs from release snapshot."
        Assert-Condition ($ManifestDocument.supportLevels -ccontains [string]$claim.evidenceLevel) "Support claim '$($claim.id)' has unknown evidence level."
        Assert-Condition ($claim.evidenceLevel -cne 'productionReady') "Release 1.27.0 cannot be marked productionReady by Stage-3 candidate evidence."
        if ($claim.evidenceLevel -ceq 'present') {
            Assert-Condition (@($claim.verifiedCells).Count -eq 0) "Present-only claim '$($claim.id)' cannot carry a verified OS cell."
        }
        else {
            Assert-Condition (@($claim.verifiedCells).Count -gt 0) "Verified claim '$($claim.id)' must name at least one exact OS cell."
        }
        Assert-Condition (@($claim.caveats).Count -gt 0) "Support claim '$($claim.id)' must retain a caveat."
        Assert-Condition (@($claim.durableEvidenceUrls) -ccontains [string]$FixtureDocument.release.releaseUrl) "Support claim '$($claim.id)' must link the exact release."

        foreach ($assetReference in @($claim.assets)) {
            $assetId = [string]$assetReference.assetId
            Assert-Condition ($canonicalPublicAssets.Contains($assetId)) "Support claim '$($claim.id)' references non-canonical public asset '$assetId'."
            Assert-Condition ($claimedAssets.Add($assetId)) "Support asset '$assetId' is claimed more than once."
            Assert-Condition ($fixtureById.ContainsKey($assetId)) "Support asset '$assetId' is missing from fixture."
            $fixtureAsset = $fixtureById[$assetId]
            Assert-Condition ([string]$assetReference.name -ceq [string]$fixtureAsset.name) "Support asset '$assetId' name differs from fixture."
            Assert-Condition ([string]$assetReference.sha256 -ceq [string]$fixtureAsset.sha256) "Support asset '$assetId' digest differs from exact fixture bytes."
            Assert-Condition (@($claim.durableEvidenceUrls) -ccontains [string]$fixtureAsset.downloadUrl) "Support claim '$($claim.id)' lacks exact asset URL for '$assetId'."
        }
    }

    foreach ($assetId in $canonicalPublicAssets) {
        Assert-Condition ($claimedAssets.Contains($assetId)) "Canonical public asset '$assetId' is absent from support claims."
    }
    Assert-Condition ($claimedAssets.Count -eq $canonicalPublicAssets.Count) 'Support claims do not cover canonical public assets exactly once.'
}

function Test-ObservedInventory {
    param(
        [Parameter(Mandatory = $true)][object]$ExpectedFixture,
        [Parameter(Mandatory = $true)][object]$ObservedFixture
    )

    Assert-Condition ($ObservedFixture.release.rawTag -ceq $ExpectedFixture.release.rawTag) 'Observed release tag differs from frozen fixture.'
    Assert-Condition ($ObservedFixture.release.sourceSha -ceq $ExpectedFixture.release.sourceSha) 'Observed release source SHA differs from frozen fixture.'
    Assert-Condition (@($ObservedFixture.assets).Count -eq @($ExpectedFixture.assets).Count) 'Observed asset count differs from frozen fixture.'

    $expectedByName = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ExpectedFixture.assets)) {
        Assert-Condition (-not $expectedByName.ContainsKey([string]$asset.name)) "Frozen fixture has duplicate observed name '$($asset.name)'."
        $expectedByName.Add([string]$asset.name, $asset)
    }

    $observedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in @($ObservedFixture.assets)) {
        $name = [string]$asset.name
        Assert-Condition ($observedNames.Add($name)) "Observed inventory contains duplicate name '$name'."
        Assert-Condition ($expectedByName.ContainsKey($name)) "Observed inventory contains unexpected name '$name'."
        $expected = $expectedByName[$name]
        Assert-Condition ($name -ceq [string]$expected.name) "Observed asset name casing differs for '$name'."
        Assert-Condition ([long]$asset.size -eq [long]$expected.size) "Observed asset '$name' size differs from frozen fixture."
        Assert-Condition ([string]$asset.sha256 -ceq [string]$expected.sha256) "Observed asset '$name' SHA-256 differs from frozen fixture."
    }

    foreach ($name in $expectedByName.Keys) {
        Assert-Condition ($observedNames.Contains($name)) "Observed inventory is missing '$name'."
    }
}

function Get-ExpectedChannel {
    param([Parameter(Mandatory = $true)][object]$FeedAsset)

    if ($FeedAsset.platform -ceq 'windows') {
        return 'windows'
    }
    if ($FeedAsset.platform -ceq 'linux') {
        return 'linux'
    }
    if ($FeedAsset.platform -ceq 'macos' -and $FeedAsset.architecture -ceq 'x64') {
        return 'macos-x64'
    }
    if ($FeedAsset.platform -ceq 'macos' -and $FeedAsset.architecture -ceq 'arm64') {
        return 'macos-arm64'
    }
    throw "Cannot derive feed channel for '$($FeedAsset.id)'."
}

function Get-FeedRecord {
    param(
        [Parameter(Mandatory = $true)][object]$Feed,
        [Parameter(Mandatory = $true)][object]$PackageAsset
    )

    if ($Feed.format -ceq 'velopack-json-v1') {
        $document = [string]$Feed.content | ConvertFrom-Json -Depth 20 -ErrorAction Stop
        $records = @($document.Assets | Where-Object { $_.FileName -ceq $PackageAsset.name })
        Assert-Condition ($records.Count -eq 1) "Feed '$($Feed.assetId)' must contain package '$($PackageAsset.name)' exactly once."
        $record = $records[0]
        return [pscustomobject]@{
            packageId = [string]$record.PackageId
            version = [string]$record.Version
            type = [string]$record.Type
            filename = [string]$record.FileName
            sha1 = ([string]$record.SHA1).ToLowerInvariant()
            sha256 = ([string]$record.SHA256).ToLowerInvariant()
            size = [long]$record.Size
        }
    }

    if ($Feed.format -ceq 'squirrel-releases-v1') {
        $content = ([string]$Feed.content).TrimStart([char[]]@([char]0xfeff)).Trim()
        $records = @()
        foreach ($line in @($content -split '\r?\n')) {
            $match = [regex]::Match($line, '^([0-9A-Fa-f]{40})\s+(\S+)\s+([0-9]+)$')
            Assert-Condition ($match.Success) "Legacy feed '$($Feed.assetId)' has invalid line '$line'."
            if ($match.Groups[2].Value -ceq $PackageAsset.name) {
                $records += [pscustomobject]@{
                    packageId = 'Unlimotion'
                    version = $null
                    type = 'Full'
                    filename = $match.Groups[2].Value
                    sha1 = $match.Groups[1].Value.ToLowerInvariant()
                    sha256 = $null
                    size = [long]$match.Groups[3].Value
                }
            }
        }
        Assert-Condition ($records.Count -eq 1) "Legacy feed '$($Feed.assetId)' must contain package '$($PackageAsset.name)' exactly once."
        return $records[0]
    }

    throw "Unknown feed format '$($Feed.format)'."
}

function Test-FeedRelations {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$FixtureDocument
    )

    $manifestById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($ManifestDocument.assets)) {
        $manifestById.Add([string]$asset.id, $asset)
    }
    $fixtureById = New-OrdinalIgnoreCaseMap
    foreach ($asset in @($FixtureDocument.assets)) {
        $fixtureById.Add([string]$asset.assetId, $asset)
    }
    $feedById = New-OrdinalIgnoreCaseMap
    foreach ($feed in @($FixtureDocument.feeds)) {
        Assert-Condition (-not $feedById.ContainsKey([string]$feed.assetId)) "Duplicate feed payload '$($feed.assetId)'."
        $feedById.Add([string]$feed.assetId, $feed)

        Assert-Condition ($fixtureById.ContainsKey([string]$feed.assetId)) "Feed payload '$($feed.assetId)' has no fixture asset."
        $feedAsset = $fixtureById[[string]$feed.assetId]
        $bytes = Get-Utf8Bytes -Value ([string]$feed.content)
        Assert-Condition ($bytes.Length -eq [long]$feedAsset.size) "Feed '$($feed.assetId)' byte length differs from fixture."
        Assert-Condition ((Get-BytesSha256 -Bytes $bytes) -ceq [string]$feedAsset.sha256) "Feed '$($feed.assetId)' content digest differs from fixture."
    }

    foreach ($relation in @($ManifestDocument.relations)) {
        $feedId = [string]$relation.feedAssetId
        $packageId = [string]$relation.packageAssetId
        Assert-Condition ($feedById.ContainsKey($feedId)) "Relation '$($relation.id)' has no frozen feed payload."
        Assert-Condition ($fixtureById.ContainsKey($packageId)) "Relation '$($relation.id)' has no frozen package asset."
        $feedManifestAsset = $manifestById[$feedId]
        $packageManifestAsset = $manifestById[$packageId]
        $packageFixtureAsset = $fixtureById[$packageId]
        $feed = $feedById[$feedId]

        Assert-Condition ($feed.format -ceq $relation.format) "Relation '$($relation.id)' format differs from feed payload."
        Assert-Condition ($relation.channel -ceq (Get-ExpectedChannel -FeedAsset $feedManifestAsset)) "Relation '$($relation.id)' channel does not match platform/architecture."
        Assert-Condition ($feedManifestAsset.platform -ceq $packageManifestAsset.platform) "Relation '$($relation.id)' crosses platforms."
        Assert-Condition ($feedManifestAsset.architecture -ceq $packageManifestAsset.architecture) "Relation '$($relation.id)' crosses architectures."

        $record = Get-FeedRecord -Feed $feed -PackageAsset $packageFixtureAsset
        Assert-Condition ($record.packageId -ceq $relation.packageId) "Relation '$($relation.id)' package id mismatch."
        Assert-Condition ($record.type -ceq 'Full') "Relation '$($relation.id)' must point to a Full package."
        Assert-Condition ($record.filename -ceq $packageFixtureAsset.name) "Relation '$($relation.id)' filename mismatch."
        Assert-Condition ($record.size -eq [long]$packageFixtureAsset.size) "Relation '$($relation.id)' size mismatch."
        Assert-Condition ($record.sha1 -ceq [string]$packageFixtureAsset.sha1) "Relation '$($relation.id)' SHA-1 mismatch."
        if ($relation.format -ceq 'velopack-json-v1') {
            Assert-Condition ($record.version -ceq $FixtureDocument.release.normalizedVersion) "Relation '$($relation.id)' version mismatch."
            Assert-Condition ($record.sha256 -ceq [string]$packageFixtureAsset.sha256) "Relation '$($relation.id)' SHA-256 mismatch."
        }
        if ($relation.hashAlgorithm -ceq 'sha256') {
            Assert-Condition ($record.sha256 -ceq [string]$packageFixtureAsset.sha256) "Relation '$($relation.id)' configured SHA-256 mismatch."
        }
        elseif ($relation.hashAlgorithm -ceq 'sha1') {
            Assert-Condition ($record.sha1 -ceq [string]$packageFixtureAsset.sha1) "Relation '$($relation.id)' configured SHA-1 mismatch."
        }
        else {
            throw "Relation '$($relation.id)' has unsupported hash algorithm."
        }
    }
}

function Set-FeedContentAndRefreshDigest {
    param(
        [Parameter(Mandatory = $true)][object]$FixtureDocument,
        [Parameter(Mandatory = $true)][string]$FeedAssetId,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $feed = @($FixtureDocument.feeds | Where-Object { $_.assetId -ceq $FeedAssetId })
    Assert-Condition ($feed.Count -eq 1) "Cannot mutate unknown feed '$FeedAssetId'."
    $fixtureAsset = @($FixtureDocument.assets | Where-Object { $_.assetId -ceq $FeedAssetId })
    Assert-Condition ($fixtureAsset.Count -eq 1) "Cannot refresh unknown feed asset '$FeedAssetId'."
    $feed[0].content = $Content
    $bytes = Get-Utf8Bytes -Value $Content
    $fixtureAsset[0].size = [long]$bytes.Length
    $fixtureAsset[0].sha256 = Get-BytesSha256 -Bytes $bytes
}

function Invoke-ResolverDocument {
    param(
        [Parameter(Mandatory = $true)][string]$RawTag,
        [string]$SourceSha = '0123456789abcdef0123456789abcdef01234567',
        [string]$WorkflowSha = '89abcdef0123456789abcdef0123456789abcdef',
        [ValidateSet('notApplicable', 'required')][string]$TagBinding = 'notApplicable',
        [long]$AndroidVersionCode = 1,
        [ValidateSet('ci-test', 'production-monotonic')][string]$AndroidVersionCodePolicy = 'ci-test',
        [switch]$IncludeSupportMatrix
    )

    $arguments = @(
        '-NoProfile',
        '-File', $script:resolverPath,
        '-RawTag', $RawTag,
        '-SourceSha', $SourceSha,
        '-WorkflowSha', $WorkflowSha,
        '-TagBinding', $TagBinding,
        '-AndroidVersionCode', [string]$AndroidVersionCode,
        '-AndroidVersionCodePolicy', $AndroidVersionCodePolicy,
        '-Manifest', $script:manifestPath)
    if ($IncludeSupportMatrix) {
        $arguments += @('-SupportMatrix', $script:supportMatrixPath)
    }

    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Resolver failed: $($output -join [Environment]::NewLine)"
    }
    try {
        return ($output -join [Environment]::NewLine) | ConvertFrom-Json -Depth 100 -ErrorAction Stop
    }
    catch {
        throw "Resolver returned invalid JSON: $($_.Exception.Message)"
    }
}

function Test-IdentityContract {
    $numeric = Invoke-ResolverDocument -RawTag '1.2.3' -IncludeSupportMatrix
    $prefixed = Invoke-ResolverDocument -RawTag 'v1.2.3' -IncludeSupportMatrix
    Assert-Condition ($numeric.rawTag -ceq '1.2.3') 'Resolver must preserve numeric raw tag.'
    Assert-Condition ($prefixed.rawTag -ceq 'v1.2.3') 'Resolver must preserve v-prefixed raw tag.'
    Assert-Condition ($numeric.normalizedVersion -ceq '1.2.3') 'Numeric tag normalized version mismatch.'
    Assert-Condition ($prefixed.normalizedVersion -ceq '1.2.3') 'Prefixed tag normalized version mismatch.'
    Assert-Condition (
        (($numeric.filenamePlan | ConvertTo-Json -Depth 30 -Compress) -ceq ($prefixed.filenamePlan | ConvertTo-Json -Depth 30 -Compress))) `
        'Dual raw tag forms must generate identical filename plans.'
    Assert-Condition (@($numeric.filenamePlan.byAssetId.PSObject.Properties).Count -eq 22) 'Resolver filename plan must cover 22 assets.'
    foreach ($property in @($numeric.filenamePlan.byAssetId.PSObject.Properties)) {
        Assert-Condition ([string]$property.Value -cnotmatch 'v1\.2\.3') "Raw v tag leaked into filename '$($property.Value)'."
    }
    Assert-Condition ($numeric.sourceSha -ceq '0123456789abcdef0123456789abcdef01234567') 'Resolver source SHA mismatch.'
    Assert-Condition ($numeric.workflowSha -ceq '89abcdef0123456789abcdef0123456789abcdef') 'Resolver workflow SHA mismatch.'
    Assert-Condition ($numeric.androidVersionCode -eq 1) 'Resolver ci-test Android versionCode mismatch.'
    Assert-Condition ($numeric.lastPublishedAndroidVersionCode -eq 353) 'Resolver must report last published Android versionCode.'
    Assert-Condition ($numeric.signatureProfile -ceq 'test') 'ci-test resolver output must use test signature profile.'
    Assert-Condition (-not [bool]$numeric.productionVersionCodeMonotonic) 'ci-test version must not claim production monotonicity.'

    foreach ($invalidTag in @('V1.2.3', '01.2.3', '1.02.3', '1.2.03', '1.2', '1.2.3-rc.1', '1.2.3+build', '0.0.0')) {
        $capturedTag = $invalidTag
        Assert-Throws -Name "invalid-tag-$capturedTag" -Action {
            Invoke-ResolverDocument -RawTag $capturedTag -IncludeSupportMatrix
        }
    }
    Assert-Throws -Name 'uppercase-source-sha' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -SourceSha ('A' * 40) -IncludeSupportMatrix
    }
    Assert-Throws -Name 'android-version-code-zero' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -AndroidVersionCode 0 -IncludeSupportMatrix
    }
    Assert-Throws -Name 'android-version-code-overflow' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -AndroidVersionCode 2100000001 -IncludeSupportMatrix
    }
    Assert-Throws -Name 'ci-test-tag-binding-required' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -IncludeSupportMatrix
    }
    Assert-Throws -Name 'production-tag-binding-not-applicable' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding notApplicable -AndroidVersionCode 354 -AndroidVersionCodePolicy production-monotonic -IncludeSupportMatrix
    }
    Assert-Throws -Name 'production-support-matrix-missing' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -AndroidVersionCode 354 -AndroidVersionCodePolicy production-monotonic
    }
    Assert-Throws -Name 'production-version-code-not-monotonic' -Action {
        Invoke-ResolverDocument -RawTag '1.2.3' -TagBinding required -AndroidVersionCode 353 -AndroidVersionCodePolicy production-monotonic -IncludeSupportMatrix
    }

    $production = Invoke-ResolverDocument `
        -RawTag 'v1.2.3' `
        -TagBinding required `
        -AndroidVersionCode 354 `
        -AndroidVersionCodePolicy production-monotonic `
        -IncludeSupportMatrix
    Assert-Condition ($production.signatureProfile -ceq 'production') 'Monotonic production identity must use production signature profile.'
    Assert-Condition ([bool]$production.productionVersionCodeMonotonic) 'Monotonic production identity must report monotonicity.'
    Assert-Condition ($production.androidVersionCodeSource -ceq 'stage4-production-allocator') 'Production version code source must be Stage-4 allocator.'
}

function Test-RetryContract {
    param([Parameter(Mandatory = $true)][object]$ManifestDocument)

    $expected = [ordered]@{
        deterministic = [ordered]@{ maxAttempts = 1; cleanup = 'none'; retryableClassification = 'never' }
        aptNetwork = [ordered]@{ maxAttempts = 3; cleanup = 'new-container'; retryableClassification = 'infrastructure-only' }
        emulatorBoot = [ordered]@{ maxAttempts = 2; cleanup = 'kill-emulator-delete-avd-data-use-new-port'; retryableClassification = 'infrastructure-only' }
        artifactTransport = [ordered]@{ maxAttempts = 2; cleanup = 'clean-extraction-directory'; retryableClassification = 'infrastructure-only' }
    }
    foreach ($property in $expected.GetEnumerator()) {
        $actualRule = $ManifestDocument.retryPolicy.($property.Key)
        Assert-Condition ($actualRule.maxAttempts -eq $property.Value.maxAttempts) "Retry maxAttempts drift for '$($property.Key)'."
        Assert-Condition ($actualRule.cleanup -ceq $property.Value.cleanup) "Retry cleanup drift for '$($property.Key)'."
        Assert-Condition ($actualRule.retryableClassification -ceq $property.Value.retryableClassification) "Retry classification drift for '$($property.Key)'."
    }

    $classificationFixtures = @(
        @{ name = 'metadata-failure'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'package-install-after-network'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'apt-mirror-network'; rule = 'aptNetwork'; attempts = 3 },
        @{ name = 'emulator-boot-timeout'; rule = 'emulatorBoot'; attempts = 2 },
        @{ name = 'emulator-install-failure'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'artifact-download-service-outage'; rule = 'artifactTransport'; attempts = 2 },
        @{ name = 'artifact-upload-action'; rule = 'deterministic'; attempts = 1 },
        @{ name = 'artifact-hash-mismatch'; rule = 'deterministic'; attempts = 1 }
    )
    foreach ($fixture in $classificationFixtures) {
        $rule = $ManifestDocument.retryPolicy.($fixture.rule)
        Assert-Condition ($rule.maxAttempts -eq $fixture.attempts) "Retry fixture '$($fixture.name)' has wrong budget."
    }
}

function Get-IdentityProjectionForEvidence {
    param([Parameter(Mandatory = $true)][object]$Identity)
    return [ordered]@{
        rawTag = [string]$Identity.rawTag
        normalizedVersion = [string]$Identity.normalizedVersion
        sourceSha = [string]$Identity.sourceSha
        workflowSha = [string]$Identity.workflowSha
        tagBinding = [string]$Identity.tagBinding
        manifestSha256 = [string]$Identity.manifestSha256
        supportMatrixSha256 = [string]$Identity.supportMatrixSha256
        signatureProfile = [string]$Identity.signatureProfile
    }
}

function New-NotApplicableUnixEvidence {
    return [ordered]@{
        applicability = 'notApplicable'
        assetId = $null
        archiveFileName = $null
        archiveSha256 = $null
        archiveEntry = $null
        originalMode = $null
        tarStoredMode = $null
        restoredMode = $null
        originalSha256 = $null
        restoredSha256 = $null
    }
}

function New-NativeCellFixture {
    param([Parameter(Mandatory = $true)][string]$Id)
    $common = [ordered]@{
        id = $Id
        status = 'pass'
        platform = ''
        architecture = ''
        osName = ''
        osVersion = ''
        mode = ''
        metadata = 'pass'
        install = 'pass'
        launch = 'pass'
        signature = 'notApplicable'
        negativeControl = 'notApplicable'
        directFuse = 'notApplicable'
        evidenceFile = "$Id.json"
        evidenceSha256 = 'b' * 64
        assetIds = @('fixture-asset')
    }
    if ($Id -ceq 'windows-server-2022-x64') {
        $common.platform = 'windows'; $common.architecture = 'x64'; $common.osName = 'Windows Server'; $common.osVersion = '2022'
        $common.mode = 'setup-and-portable-install-launch'; $common.signature = 'stateRecorded'; $common.assetIds = @('windows-setup-x64', 'windows-portable-x64')
    }
    elseif ($Id -match '^debian-(12|13)-x64-(clean|upgrade|appimage|missing-runtime-negative)$') {
        $common.platform = 'linux'; $common.architecture = 'x64'; $common.osName = 'debian'; $common.osVersion = $Matches[1]; $common.mode = $Matches[2]
        if ($common.mode -ceq 'appimage') {
            $common.assetIds = [string[]]@('linux-deb-x64', 'linux-appimage-x64')
            $common.install = 'extractPassed'; $common.directFuse = 'notVerified'
        }
        elseif ($common.mode -ceq 'missing-runtime-negative') {
            $common.assetIds = [string[]]@('linux-deb-x64')
            $common.launch = 'expectedFailureObserved'; $common.negativeControl = 'pass'
        }
        else { $common.assetIds = [string[]]@('linux-deb-x64') }
    }
    elseif ($Id -match '^macos-15-(x64|arm64)$') {
        $common.platform = 'macos'; $common.architecture = $Matches[1]; $common.osName = 'macOS'; $common.osVersion = '15'
        $common.mode = 'package-and-portable-native-launch'; $common.signature = 'stateRecorded'; $common.assetIds = @("macos-$($Matches[1])-setup", "macos-$($Matches[1])-portable")
    }
    elseif ($Id -match '^android-(arm64|x64)-apk-metadata$') {
        $common.platform = 'android'; $common.architecture = $Matches[1]; $common.osName = 'android'; $common.osVersion = 'notApplicable'
        $common.mode = 'apk-metadata'; $common.install = 'notApplicable'; $common.launch = 'notApplicable'; $common.signature = 'stateRecorded'
        $common.assetIds = @("android-$($Matches[1])-apk")
        $common.evidenceFile = 'android-artifact.json'
    }
    elseif ($Id -match '^android-api-(23|36)-x64-emulator$') {
        $common.platform = 'android'; $common.architecture = 'x64'; $common.osName = 'android'; $common.osVersion = "API $($Matches[1])"
        $common.mode = 'emulator'; $common.signature = 'coveredByArtifactCell'; $common.assetIds = @('android-x64-apk')
        $common.evidenceFile = "android-api$($Matches[1])-emulator.json"
    }
    else { throw "Unknown native cell fixture id '$Id'." }
    return $common
}

function New-PlatformEvidenceFixtures {
    param(
        [Parameter(Mandatory = $true)][object]$ManifestDocument,
        [Parameter(Mandatory = $true)][object]$IdentityDocument
    )
    $identity = Get-IdentityProjectionForEvidence -Identity $IdentityDocument
    $cellGroups = [ordered]@{
        'windows/x64' = @('windows-server-2022-x64')
        'linux/x64' = @(
            'debian-12-x64-clean', 'debian-12-x64-upgrade', 'debian-12-x64-appimage', 'debian-12-x64-missing-runtime-negative',
            'debian-13-x64-clean', 'debian-13-x64-upgrade', 'debian-13-x64-appimage', 'debian-13-x64-missing-runtime-negative')
        'macos/x64' = @('macos-15-x64')
        'macos/arm64' = @('macos-15-arm64')
        'android/multi' = @('android-arm64-apk-metadata', 'android-x64-apk-metadata', 'android-api-23-x64-emulator', 'android-api-36-x64-emulator')
    }
    $fixtures = [System.Collections.Generic.List[object]]::new()
    $artifactId = 100
    foreach ($entry in $cellGroups.GetEnumerator()) {
        $platform, $architecture = $entry.Key -split '/'
        $assets = @($ManifestDocument.assets | Where-Object {
            $_.platform -ceq $platform -and ($architecture -ceq 'multi' -or $_.architecture -ceq $architecture)
        } | ForEach-Object {
            [ordered]@{
                assetId = [string]$_.id
                fileName = [string]$IdentityDocument.filenamePlan.byAssetId.($_.id)
                size = 1
                sha256 = 'a' * 64
            }
        })
        $relations = @($ManifestDocument.relations | Where-Object {
            $feed = @($ManifestDocument.assets | Where-Object id -CEQ $_.feedAssetId)[0]
            $feed.platform -ceq $platform -and ($architecture -ceq 'multi' -or $feed.architecture -ceq $architecture)
        } | ForEach-Object {
            [ordered]@{
                relationId = [string]$_.id
                feedAssetId = [string]$_.feedAssetId
                packageAssetId = [string]$_.packageAssetId
                channel = [string]$_.channel
                format = [string]$_.format
                packageSha1 = 'c' * 40
                packageSha256 = 'a' * 64
                packageSize = 1
                status = 'pass'
            }
        })
        $architectures = if ($architecture -ceq 'multi') { @('arm64', 'x64') } else { @($architecture) }
        $preEvidence = @($architectures | ForEach-Object {
            [ordered]@{
                evidenceId = "$platform-$_"
                fileName = "artifact-evidence-$platform-$_.json"
                sha256 = 'd' * 64
                platform = $platform
                architecture = $_
            }
        })
        $unixMode = if ($platform -ceq 'linux') {
            [ordered]@{
                applicability = 'required'
                assetId = 'linux-appimage-x64'
                archiveFileName = 'distribution-linux-x64.tar'
                archiveSha256 = 'e' * 64
                archiveEntry = 'assets/Unlimotion.AppImage'
                originalMode = '0755'
                tarStoredMode = '0755'
                restoredMode = '0755'
                originalSha256 = 'a' * 64
                restoredSha256 = 'a' * 64
            }
        } else { New-NotApplicableUnixEvidence }
        $transport = [ordered]@{
            schemaVersion = 1
            kind = 'distribution-transport-receipt'
            status = 'pass'
            platform = $platform
            architecture = $architecture
            identity = $identity
            artifactName = "distribution-$platform-$architecture-fixture"
            artifactId = [string]$artifactId
            artifactDigest = 'f' * 64
            retentionDays = 7
            ifNoFilesFound = 'error'
            overwrite = $false
            preEvidence = $preEvidence
            unixMode = $unixMode
            productionReady = $false
        }
        $nativeCells = @($entry.Value | ForEach-Object { New-NativeCellFixture -Id $_ })
        $nativeEvidence = if ($entry.Key -ceq 'android/multi') {
            @(
                [ordered]@{ fileName = 'android-artifact.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'artifact' },
                [ordered]@{ fileName = 'native-cache-evidence.json'; sha256 = '2' * 64; kind = 'distribution-android-native-evidence'; mode = 'provenance' },
                [ordered]@{ fileName = 'native-inputs.json'; sha256 = '5' * 64; kind = 'distribution-android-native-inputs'; mode = 'native-inputs' },
                [ordered]@{ fileName = 'native-provenance.json'; sha256 = '6' * 64; kind = 'distribution-android-native-provenance'; mode = 'native-provenance' },
                [ordered]@{ fileName = 'android-api23-emulator.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'emulator' },
                [ordered]@{ fileName = 'android-api36-emulator.json'; sha256 = 'b' * 64; kind = 'distribution-android-native-evidence'; mode = 'emulator' },
                [ordered]@{ fileName = 'android-api23-download-transport.json'; sha256 = '3' * 64; kind = 'distribution-download-transport'; mode = 'distribution-download-transport' },
                [ordered]@{ fileName = 'android-api36-download-transport.json'; sha256 = '4' * 64; kind = 'distribution-download-transport'; mode = 'distribution-download-transport' }
            )
        }
        else {
            @($nativeCells | ForEach-Object {
                [ordered]@{
                    fileName = [string]$_.evidenceFile
                    sha256 = [string]$_.evidenceSha256
                    kind = 'fixture-native-evidence'
                    mode = [string]$_.mode
                }
            })
        }
        $fixtures.Add([ordered]@{
            schemaVersion = 1
            kind = 'distribution-platform-evidence'
            status = 'pass'
            platform = $platform
            architecture = $architecture
            identity = $identity
            artifactEvidence = $preEvidence
            nativeEvidence = @($nativeEvidence)
            transportReceiptFile = "transport-receipt-$platform-$architecture.json"
            transportReceiptSha256 = '1' * 64
            transport = $transport
            artifacts = $assets
            relations = $relations
            nativeCells = @($nativeCells)
            releasePromotion = 'notApplicable'
            productionSignatureEligibility = 'notApplicable'
            productionReady = $false
        })
        $artifactId++
    }
    return @($fixtures)
}

function Invoke-AggregateFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$IdentityDocument,
        [Parameter(Mandatory = $true)][object[]]$Platforms,
        [string[]]$RequestedAssetIds
    )
    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $identityPath = Join-Path $Root 'identity.json'
    [System.IO.File]::WriteAllText($identityPath, ($IdentityDocument | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
    $platformPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($platform in $Platforms) {
        $path = Join-Path $Root "platform-$($platform.platform)-$($platform.architecture).json"
        [System.IO.File]::WriteAllText($path, ($platform | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
        $platformPaths.Add($path)
    }
    $arguments = @(
        '-NoProfile', '-File', $script:artifactValidatorPath,
        '-Mode', 'Aggregate',
        '-Manifest', $script:manifestPath,
        '-SupportMatrix', $script:supportMatrixPath,
        '-EvidenceSchema', $script:evidenceSchemaPath,
        '-Identity', $identityPath,
        '-EvidencePath', ($platformPaths -join ','),
        '-OutputChecksums', (Join-Path $Root 'SHA256SUMS.txt'),
        '-Evidence', (Join-Path $Root 'distribution-evidence.json'))
    if ($null -ne $RequestedAssetIds) { $arguments += @('-AssetId', ($RequestedAssetIds -join ',')) }
    $output = & pwsh @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Aggregate fixture failed: $($output -join [Environment]::NewLine)" }
    return Read-JsonFile -Path (Join-Path $Root 'distribution-evidence.json') -DisplayName 'Aggregate fixture output'
}

function Test-AndroidNativeEvidenceConverters {
    param([Parameter(Mandatory = $true)][object]$IdentityDocument)

    try {
        . $script:artifactValidatorPath `
            -Mode Validate `
            -Manifest $script:manifestPath `
            -SupportMatrix $script:supportMatrixPath `
            -EvidenceSchema $script:evidenceSchemaPath
    }
    catch {
        if ($_.Exception.Message -notlike 'Validate mode requires*') { throw }
    }

    function New-AndroidRawEvidenceBase {
        param([Parameter(Mandatory = $true)][string]$NativeMode)
        return [pscustomobject][ordered]@{
            schemaVersion = 1
            kind = 'distribution-android-native-evidence'
            outcome = 'passed'
            productionReady = $false
            mode = $NativeMode
            rawTag = [string]$IdentityDocument.rawTag
            normalizedVersion = [string]$IdentityDocument.normalizedVersion
            sourceSha = [string]$IdentityDocument.sourceSha
            workflowSha = [string]$IdentityDocument.workflowSha
            tagBinding = [string]$IdentityDocument.tagBinding
            manifestSha256 = [string]$IdentityDocument.manifestSha256
            supportMatrixSha256 = [string]$IdentityDocument.supportMatrixSha256
            signatureProfile = [string]$IdentityDocument.signatureProfile
            androidVersionCode = [int]$IdentityDocument.androidVersionCode
            androidVersionCodePolicy = [string]$IdentityDocument.androidVersionCodePolicy
        }
    }

    $identityProjection = Copy-JsonObject (Get-IdentityProjectionForEvidence -Identity $IdentityDocument)
    $apkName = [string]$IdentityDocument.filenamePlan.byAssetId.'android-x64-apk'
    $apkSha256 = '4' * 64
    $artifactsByName = @{
        $apkName = [pscustomobject]@{ assetId = 'android-x64-apk'; sha256 = $apkSha256 }
    }
    $expectedTransportName = 'distribution-android-multi-fixture'
    $expectedTransportId = '123'
    $expectedTransportDigest = 'f' * 64

    $provenance = New-AndroidRawEvidenceBase -NativeMode provenance
    $provenance | Add-Member -NotePropertyName nativeInputDigest -NotePropertyValue ('5' * 64)
    $provenance | Add-Member -NotePropertyName nativeInputsSha256 -NotePropertyValue ('5' * 64)
    $provenance | Add-Member -NotePropertyName nativeProvenanceSha256 -NotePropertyValue ('6' * 64)
    $provenance | Add-Member -NotePropertyName outputClosureSha256 -NotePropertyValue ('7' * 64)
    $provenance | Add-Member -NotePropertyName requestedCacheKey -NotePropertyValue ('android-native-v2-linux-x64-' + ('5' * 64))
    $provenance | Add-Member -NotePropertyName matchedCacheKey -NotePropertyValue ([string]$provenance.requestedCacheKey)
    $provenance | Add-Member -NotePropertyName androidApiLevel -NotePropertyValue 23
    $provenance | Add-Member -NotePropertyName outputCount -NotePropertyValue 4
    $provenance | Add-Member -NotePropertyName cacheHit -NotePropertyValue $true
    $provenance | Add-Member -NotePropertyName cacheSave -NotePropertyValue $false

    $inputFileSha256 = [ordered]@{}
    foreach ($relative in @(
        'scripts/android-native-common.sh',
        'scripts/build-openssl-android.sh',
        'scripts/build-libssh2-android.sh',
        'scripts/build-libgit2-android.sh',
        'scripts/pack-libgit2sharp-nativebinaries-android.sh',
        'scripts/build-android-distribution.sh',
        'src/Unlimotion.Android/Unlimotion.Android.csproj',
        'src/Directory.Packages.props',
        'src/nuget.config'
    )) {
        $inputFileSha256[$relative] = 'd' * 64
    }
    $nativeInputs = [pscustomobject][ordered]@{
        schemaVersion = 1
        androidApiLevel = 23
        ndkRevision = '27.2.12479018'
        host = [pscustomobject][ordered]@{
            os = 'Linux'; arch = 'X64'; toolchainTriples = @('aarch64-linux-android', 'x86_64-linux-android')
        }
        abis = @('arm64-v8a', 'x86_64')
        sources = [pscustomobject][ordered]@{
            openssl = [pscustomobject][ordered]@{ version = '3.0.14'; url = 'https://example.invalid/openssl.tar.gz'; sha256 = '1' * 64 }
            libssh2 = [pscustomobject][ordered]@{ version = '1.11.1'; url = 'https://example.invalid/libssh2.tar.gz'; sha256 = '2' * 64 }
            libgit2Commit = '3' * 40
            upstreamNativePackage = [pscustomobject][ordered]@{ version = '2.0.324'; url = 'https://example.invalid/native.nupkg'; sha256 = '3' * 64 }
        }
        nativePackageVersion = '2.0.324-android.7'
        inputFileSha256 = [pscustomobject]$inputFileSha256
    }
    $rawProvenance = [pscustomobject][ordered]@{
        schemaVersion = 1
        nativeInputDigest = '5' * 64
        requestedCacheKey = 'android-native-v2-linux-x64-' + ('5' * 64)
        matchedCacheKey = 'android-native-v2-linux-x64-' + ('5' * 64)
        inputs = $nativeInputs
        outputs = @(
            [pscustomobject][ordered]@{ path = 'nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg'; size = 128; sha256 = 'e' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/Z.so'; size = 2; sha256 = 'a' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/a.so'; size = 3; sha256 = 'b' * 64 },
            [pscustomobject][ordered]@{ path = 'android-native/_fixture.so'; size = 4; sha256 = 'c' * 64 }
        )
    }
    $fixtureOutputClosure = Assert-AndroidNativeProvenanceDocument -Report $rawProvenance
    Assert-Condition ($fixtureOutputClosure -ceq '6a007c20e330e21e70e73a0cd1f26126307a8efe33fa3480d18ca0cd82507910') `
        'Android raw provenance output closure must use Python-compatible ordinal path/key canonicalization.'
    $provenance.outputClosureSha256 = $fixtureOutputClosure
    $provenanceCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $provenance; sha256 = '8' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($provenanceCells.Count -eq 0) 'Android provenance evidence must validate without producing a native cell.'
    Add-Check -Name 'evidence:android-provenance-raw-converter'

    $stringOutputCount = Copy-JsonObject $provenance
    $stringOutputCount.outputCount = '4'
    Assert-Throws -Name 'android-provenance-string-output-count' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringOutputCount; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $numericCacheHit = Copy-JsonObject $provenance
    $numericCacheHit.cacheHit = 1
    Assert-Throws -Name 'android-provenance-numeric-cache-boolean' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericCacheHit; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $nativeInputsEvidence = [pscustomobject]@{ report = $nativeInputs; sha256 = '5' * 64 }
    $nativeProvenanceEvidence = [pscustomobject]@{ report = $rawProvenance; sha256 = '6' * 64 }
    $nativeInputCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence $nativeInputsEvidence `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-inputs.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    $nativeProvenanceCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence $nativeProvenanceEvidence `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path native-provenance.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($nativeInputCells.Count -eq 0 -and $nativeProvenanceCells.Count -eq 0) `
        'Android raw native input/provenance documents must validate without producing native cells.'

    $stringNativeInputsSchema = Copy-JsonObject $nativeInputs
    $stringNativeInputsSchema.schemaVersion = '1'
    Assert-Throws -Name 'android-native-inputs-string-schema-version' -Action {
        Assert-AndroidNativeInputsDocument -Report $stringNativeInputsSchema
    }

    $scalarAbis = Copy-JsonObject $nativeInputs
    $scalarAbis.abis = 'arm64-v8a|x86_64'
    Assert-Throws -Name 'android-native-inputs-scalar-abis' -Action {
        Assert-AndroidNativeInputsDocument -Report $scalarAbis
    }

    $scalarToolchainTriples = Copy-JsonObject $nativeInputs
    $scalarToolchainTriples.host.toolchainTriples = 'aarch64-linux-android|x86_64-linux-android'
    Assert-Throws -Name 'android-native-inputs-scalar-toolchain-triples' -Action {
        Assert-AndroidNativeInputsDocument -Report $scalarToolchainTriples
    }

    $scalarProvenanceOutputs = Copy-JsonObject $rawProvenance
    $scalarProvenanceOutputs.outputs = 'android-native/a.so'
    Assert-Throws -Name 'android-native-provenance-scalar-outputs' -Action {
        Assert-AndroidNativeProvenanceDocument -Report $scalarProvenanceOutputs
    }
    $nativeInputsReference = New-NativeEvidenceReference -InputEvidence ([pscustomobject]@{
        report = $nativeInputs; path = 'native-inputs.json'; sha256 = '5' * 64
    })
    $nativeProvenanceReference = New-NativeEvidenceReference -InputEvidence ([pscustomobject]@{
        report = $rawProvenance; path = 'native-provenance.json'; sha256 = '6' * 64
    })
    Assert-Condition ($nativeInputsReference.kind -ceq 'distribution-android-native-inputs' -and
        $nativeInputsReference.mode -ceq 'native-inputs' -and
        $nativeProvenanceReference.kind -ceq 'distribution-android-native-provenance' -and
        $nativeProvenanceReference.mode -ceq 'native-provenance') `
        'Android raw native documents must produce strict inferred platform-evidence references.'

    $artifactCacheEvidence = [pscustomobject]@{
        report = [pscustomobject]@{
            nativeCache = [pscustomobject][ordered]@{
                nativeInputDigest = '5' * 64
                requestedKey = 'android-native-v2-linux-x64-' + ('5' * 64)
                matchedKey = 'android-native-v2-linux-x64-' + ('5' * 64)
                hit = $true
                saveRequired = $false
            }
        }
    }
    $summaryEvidence = [pscustomobject]@{ report = $provenance }
    Assert-AndroidNativeProvenanceClosure `
        -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $summaryEvidence `
        -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    Add-Check -Name 'evidence:android-native-provenance-byte-closure'

    $badOutputCountSummary = Copy-JsonObject $summaryEvidence
    $badOutputCountSummary.report.outputCount = 5
    Assert-Throws -Name 'android-provenance-output-count-closure-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $badOutputCountSummary `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badArtifactCache = Copy-JsonObject $artifactCacheEvidence
    $badArtifactCache.report.nativeCache.nativeInputDigest = '0' * 64
    Assert-Throws -Name 'android-artifact-native-cache-digest-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $badArtifactCache -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $mutatedNativeInputs = Copy-JsonObject $nativeInputsEvidence
    $mutatedNativeInputs.report.sources.openssl.sha256 = '0' * 64
    $mutatedNativeInputs.sha256 = '0' * 64
    Assert-Throws -Name 'android-downloaded-native-inputs-mutation' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $artifactCacheEvidence -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $mutatedNativeInputs -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badCacheSemantics = Copy-JsonObject $artifactCacheEvidence
    $badCacheSemantics.report.nativeCache.hit = $false
    $badCacheSemantics.report.nativeCache.saveRequired = $true
    Assert-Throws -Name 'android-artifact-native-cache-outcome-mismatch' -Action {
        Assert-AndroidNativeProvenanceClosure `
            -ArtifactEvidence $badCacheSemantics -SummaryEvidence $summaryEvidence `
            -NativeInputsEvidence $nativeInputsEvidence -NativeProvenanceEvidence $nativeProvenanceEvidence
    }

    $badCacheOutcome = Copy-JsonObject $provenance
    $badCacheOutcome.cacheHit = $false
    $badCacheOutcome.cacheSave = $false
    Assert-Throws -Name 'android-provenance-invalid-cache-outcome' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badCacheOutcome; sha256 = '8' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path native-cache-evidence.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $downloadTransport = [pscustomobject][ordered]@{
        schemaVersion = 1
        kind = 'distribution-download-transport'
        status = 'pass'
        scope = 'android-api23'
        identity = $identityProjection
        sourceArtifact = [pscustomobject][ordered]@{
            name = $expectedTransportName
            id = $expectedTransportId
            digest = $expectedTransportDigest
        }
        retry = [pscustomobject][ordered]@{
            rule = 'bounded-clean-retry'
            classification = 'transient-transport'
            maxAttempts = 2
            firstOutcome = 'failure'
            secondOutcome = 'success'
            selectedAttempt = 2
            cleanupBeforeAttempt2 = 'completed'
            exhausted = $false
        }
        productionReady = $false
    }
    $transportCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $downloadTransport; sha256 = '9' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)

    $firstAttemptTransport = Copy-JsonObject $downloadTransport
    $firstAttemptTransport.retry.classification = 'none'
    $firstAttemptTransport.retry.firstOutcome = 'success'
    $firstAttemptTransport.retry.secondOutcome = 'skipped'
    $firstAttemptTransport.retry.selectedAttempt = 1
    $firstAttemptTransport.retry.cleanupBeforeAttempt2 = 'notRequired'
    $firstAttemptTransportCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $firstAttemptTransport; sha256 = '9' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($transportCells.Count -eq 0 -and $firstAttemptTransportCells.Count -eq 0) `
        'Both valid Android download retry outcomes must validate without producing a native cell.'
    Add-Check -Name 'evidence:android-download-transport-raw-converter'

    $numericTransportProductionReady = Copy-JsonObject $downloadTransport
    $numericTransportProductionReady.productionReady = 0
    Assert-Throws -Name 'android-transport-numeric-production-ready' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericTransportProductionReady; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $stringTransportAttempt = Copy-JsonObject $downloadTransport
    $stringTransportAttempt.retry.selectedAttempt = '2'
    Assert-Throws -Name 'android-transport-string-selected-attempt' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringTransportAttempt; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badTransportCleanup = Copy-JsonObject $downloadTransport
    $badTransportCleanup.retry.cleanupBeforeAttempt2 = 'notRequired'
    Assert-Throws -Name 'android-transport-retry-cleanup-missing' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badTransportCleanup; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badTransportSource = Copy-JsonObject $downloadTransport
    $badTransportSource.sourceArtifact.digest = 'e' * 64
    Assert-Throws -Name 'android-transport-source-artifact-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badTransportSource; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    Assert-Throws -Name 'android-transport-scope-filename-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $downloadTransport; sha256 = '9' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api36-download-transport.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $emulator = New-AndroidRawEvidenceBase -NativeMode emulator
    $emulator | Add-Member -NotePropertyName supportLevel -NotePropertyValue launchVerified
    $emulator | Add-Member -NotePropertyName recordedAtUtc -NotePropertyValue '2026-07-19T12:00:00Z'
    $emulator | Add-Member -NotePropertyName asset -NotePropertyValue ([pscustomobject]@{
        name = $apkName; architecture = 'x86_64'; sha256Before = $apkSha256; sha256After = $apkSha256
    })
    $emulator | Add-Member -NotePropertyName runtime -NotePropertyValue ([pscustomobject][ordered]@{
        apiLevel = 23
        bootAttempts = 2
        maxBootAttempts = 2
        deviceFingerprint = 'google/sdk/test:userdebug/test-keys'
        deviceSdk = 23
        systemImagePackage = 'system-images;android-23;google_apis;x86_64'
        systemImageRevision = '10'
        serial = 'emulator-5554'
        applicationId = 'com.Kibnet.Unlimotion'
        activity = 'MainActivity'
        processId = '123'
        fatalLogcatEntries = 0
        logcat = [pscustomobject][ordered]@{
            fileName = 'android-api23-logcat.txt'; sha256 = 'b' * 64; bytes = 128
        }
        emulatorLog = [pscustomobject][ordered]@{
            fileName = 'android-api23-emulator.log'; sha256 = 'c' * 64; bytes = 256
        }
    })
    $emulator | Add-Member -NotePropertyName tools -NotePropertyValue ([pscustomobject]@{
        emulatorVersion = 'Android emulator version 36.4.9.0'
        adbVersion = 'Android Debug Bridge version 1.0.41'
        aaptVersion = 'Android Asset Packaging Tool, v0.2'
    })
    $emulator | Add-Member -NotePropertyName runner -NotePropertyValue ([pscustomobject]@{
        imageOs = 'ubuntu24'; imageVersion = '20260719.1.0'; uname = 'Linux runner'
    })
    $emulator | Add-Member -NotePropertyName bootRetry -NotePropertyValue ([pscustomobject][ordered]@{
        rule = 'bounded-clean-retry'
        classification = 'transient-emulator-boot'
        attempts = 2
        maxAttempts = 2
        cleanupBeforeAttempt2 = 'kill-delete-avd-remove-files-and-wipe-data'
        outcomes = @('failure', 'success')
        exhausted = $false
    })
    $emulatorCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $emulator; sha256 = 'a' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)

    $firstBootEmulator = Copy-JsonObject $emulator
    $firstBootEmulator.recordedAtUtc = '2026-07-19T12:00:00Z'
    $firstBootEmulator.runtime.bootAttempts = 1
    $firstBootEmulator.bootRetry.classification = 'none'
    $firstBootEmulator.bootRetry.attempts = 1
    $firstBootEmulator.bootRetry.cleanupBeforeAttempt2 = 'notRequired'
    $firstBootEmulator.bootRetry.outcomes = @('success')
    $firstBootEmulatorCells = @(Convert-AndroidNativeEvidence `
        -InputEvidence ([pscustomobject]@{ report = $firstBootEmulator; sha256 = 'a' * 64 }) `
        -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
        -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
        -ExpectedArtifactTransportName $expectedTransportName `
        -ExpectedArtifactTransportId $expectedTransportId `
        -ExpectedArtifactTransportDigest $expectedTransportDigest)
    Assert-Condition ($emulatorCells.Count -eq 1 -and $firstBootEmulatorCells.Count -eq 1 -and
        [string]$emulatorCells[0].id -ceq 'android-api-23-x64-emulator' -and
        [string]$firstBootEmulatorCells[0].id -ceq 'android-api-23-x64-emulator') `
        'Both valid Android emulator boot outcomes must produce the exact API-23 native cell.'
    Add-Check -Name 'evidence:android-emulator-raw-converter'

    $stringEmulatorApi = Copy-JsonObject $emulator
    $stringEmulatorApi.recordedAtUtc = '2026-07-19T12:00:00Z'
    $stringEmulatorApi.runtime.apiLevel = '23'
    Assert-Throws -Name 'android-emulator-string-api-level' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $stringEmulatorApi; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $numericRetryExhausted = Copy-JsonObject $emulator
    $numericRetryExhausted.recordedAtUtc = '2026-07-19T12:00:00Z'
    $numericRetryExhausted.bootRetry.exhausted = 0
    Assert-Throws -Name 'android-emulator-numeric-retry-exhausted' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $numericRetryExhausted; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $scalarRetryOutcomes = Copy-JsonObject $emulator
    $scalarRetryOutcomes.recordedAtUtc = '2026-07-19T12:00:00Z'
    $scalarRetryOutcomes.bootRetry.outcomes = 'failure|success'
    Assert-Throws -Name 'android-emulator-scalar-retry-outcomes' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $scalarRetryOutcomes; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $badDeviceSdk = Copy-JsonObject $emulator
    $badDeviceSdk.recordedAtUtc = '2026-07-19T12:00:00Z'
    $badDeviceSdk.runtime.deviceSdk = 24
    Assert-Throws -Name 'android-emulator-device-sdk-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $badDeviceSdk; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api23-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    Assert-Throws -Name 'android-emulator-api-filename-mismatch' -Action {
        Convert-AndroidNativeEvidence `
            -InputEvidence ([pscustomobject]@{ report = $emulator; sha256 = 'a' * 64 }) `
            -IdentityProjection $identityProjection -IdentityObject $IdentityDocument `
            -ArtifactsByName $artifactsByName -Path android-api36-emulator.json `
            -ExpectedArtifactTransportName $expectedTransportName `
            -ExpectedArtifactTransportId $expectedTransportId `
            -ExpectedArtifactTransportDigest $expectedTransportDigest
    }

    $mergeRoot = Join-Path ([IO.Path]::GetTempPath()) ('unlimotion-android-merge-contract-' + [Guid]::NewGuid().ToString('N'))
    try {
        $producerRoot = Join-Path $mergeRoot 'producer'
        $assetsRoot = Join-Path $producerRoot 'assets'
        $artifactEvidenceRoot = Join-Path $producerRoot 'evidence'
        $nativeRoot = Join-Path $mergeRoot 'native'
        [IO.Directory]::CreateDirectory($assetsRoot) | Out-Null
        [IO.Directory]::CreateDirectory($artifactEvidenceRoot) | Out-Null
        [IO.Directory]::CreateDirectory($nativeRoot) | Out-Null
        $writeJson = {
            param([string]$Path, [object]$Value)
            [IO.File]::WriteAllText(
                $Path,
                (($Value | ConvertTo-Json -Depth 100 -Compress) + "`n"),
                [Text.UTF8Encoding]::new($false)
            )
        }

        $identityPath = Join-Path $mergeRoot 'identity.json'
        & $writeJson $identityPath $IdentityDocument
        $arm64Name = [string]$IdentityDocument.filenamePlan.byAssetId.'android-arm64-apk'
        $x64Name = [string]$IdentityDocument.filenamePlan.byAssetId.'android-x64-apk'
        $arm64Path = Join-Path $assetsRoot $arm64Name
        $x64Path = Join-Path $assetsRoot $x64Name
        [IO.File]::WriteAllText($arm64Path, 'arm64-apk-fixture', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($x64Path, 'x64-apk-fixture', [Text.UTF8Encoding]::new($false))
        $arm64Sha = Get-LowerFileSha256 -Path $arm64Path
        $x64Sha = Get-LowerFileSha256 -Path $x64Path

        $artifactEvidencePaths = [Collections.Generic.List[string]]::new()
        foreach ($artifactFixture in @(
            [pscustomobject]@{ Architecture = 'arm64'; AssetId = 'android-arm64-apk'; FileName = $arm64Name; Path = $arm64Path; Sha256 = $arm64Sha },
            [pscustomobject]@{ Architecture = 'x64'; AssetId = 'android-x64-apk'; FileName = $x64Name; Path = $x64Path; Sha256 = $x64Sha }
        )) {
            $artifactEvidence = [ordered]@{
                schemaVersion = 1
                kind = 'distribution-artifact-evidence'
                status = 'pass'
                platform = 'android'
                architecture = $artifactFixture.Architecture
                rawTag = [string]$IdentityDocument.rawTag
                normalizedVersion = [string]$IdentityDocument.normalizedVersion
                sourceSha = [string]$IdentityDocument.sourceSha
                workflowSha = [string]$IdentityDocument.workflowSha
                tagBinding = [string]$IdentityDocument.tagBinding
                manifestSha256 = [string]$IdentityDocument.manifestSha256
                supportMatrixSha256 = [string]$IdentityDocument.supportMatrixSha256
                identitySignatureProfile = [string]$IdentityDocument.signatureProfile
                signatureProfile = 'test'
                productionReady = $false
                artifacts = @([ordered]@{
                    assetId = $artifactFixture.AssetId
                    fileName = $artifactFixture.FileName
                    size = (Get-Item -LiteralPath $artifactFixture.Path).Length
                    sha256 = $artifactFixture.Sha256
                })
                relations = @()
            }
            $artifactEvidencePath = Join-Path $artifactEvidenceRoot "artifact-evidence-$($artifactFixture.Architecture).json"
            & $writeJson $artifactEvidencePath $artifactEvidence
            $artifactEvidencePaths.Add($artifactEvidencePath)
        }

        $integrationNativeInputs = Copy-JsonObject $nativeInputs
        $nativeInputsPath = Join-Path $nativeRoot 'native-inputs.json'
        & $writeJson $nativeInputsPath $integrationNativeInputs
        $nativeInputsSha = Get-LowerFileSha256 -Path $nativeInputsPath
        $integrationCacheKey = "android-native-v2-linux-x64-$nativeInputsSha"
        $integrationRawProvenance = Copy-JsonObject $rawProvenance
        $integrationRawProvenance.nativeInputDigest = $nativeInputsSha
        $integrationRawProvenance.requestedCacheKey = $integrationCacheKey
        $integrationRawProvenance.matchedCacheKey = $integrationCacheKey
        $integrationRawProvenance.inputs = $integrationNativeInputs
        $nativeProvenancePath = Join-Path $nativeRoot 'native-provenance.json'
        & $writeJson $nativeProvenancePath $integrationRawProvenance
        $nativeProvenanceSha = Get-LowerFileSha256 -Path $nativeProvenancePath

        $integrationSummary = Copy-JsonObject $provenance
        $integrationSummary.nativeInputDigest = $nativeInputsSha
        $integrationSummary.nativeInputsSha256 = $nativeInputsSha
        $integrationSummary.nativeProvenanceSha256 = $nativeProvenanceSha
        $integrationSummary.requestedCacheKey = $integrationCacheKey
        $integrationSummary.matchedCacheKey = $integrationCacheKey
        $summaryPath = Join-Path $nativeRoot 'native-cache-evidence.json'
        & $writeJson $summaryPath $integrationSummary

        $integrationArtifact = New-AndroidRawEvidenceBase -NativeMode artifact
        $integrationArtifact | Add-Member -NotePropertyName supportLevel -NotePropertyValue metadataVerified
        $integrationArtifact | Add-Member -NotePropertyName runner -NotePropertyValue ([pscustomobject]@{ os = 'Linux'; architecture = 'X64' })
        $integrationArtifact | Add-Member -NotePropertyName assets -NotePropertyValue @(
            [pscustomobject][ordered]@{
                assetId = 'android-arm64-apk'; name = $arm64Name; rid = 'android-arm64'; architecture = 'arm64-v8a'
                size = (Get-Item -LiteralPath $arm64Path).Length; sha256Before = $arm64Sha; sha256After = $arm64Sha
                applicationId = 'com.Kibnet.Unlimotion'; versionName = [string]$IdentityDocument.normalizedVersion
                versionCode = [int]$IdentityDocument.androidVersionCode; minSdk = 23; targetSdk = 36
                signatureProfile = [string]$IdentityDocument.signatureProfile; signatureFingerprintSha256 = '9' * 64
                signerCount = 1; zipAligned = $true; nativeSymbolsVerified = $true
            },
            [pscustomobject][ordered]@{
                assetId = 'android-x64-apk'; name = $x64Name; rid = 'android-x64'; architecture = 'x86_64'
                size = (Get-Item -LiteralPath $x64Path).Length; sha256Before = $x64Sha; sha256After = $x64Sha
                applicationId = 'com.Kibnet.Unlimotion'; versionName = [string]$IdentityDocument.normalizedVersion
                versionCode = [int]$IdentityDocument.androidVersionCode; minSdk = 23; targetSdk = 36
                signatureProfile = [string]$IdentityDocument.signatureProfile; signatureFingerprintSha256 = '8' * 64
                signerCount = 1; zipAligned = $true; nativeSymbolsVerified = $true
            }
        )
        $integrationArtifact | Add-Member -NotePropertyName arm64LaunchVerified -NotePropertyValue $false
        $integrationArtifact | Add-Member -NotePropertyName arm64LaunchReason -NotePropertyValue 'No native arm64 device in Stage-3 fixture'
        $integrationArtifact | Add-Member -NotePropertyName nativeCache -NotePropertyValue ([pscustomobject][ordered]@{
            nativeInputDigest = $nativeInputsSha
            requestedKey = $integrationCacheKey
            matchedKey = $integrationCacheKey
            hit = $true
            saveRequired = $false
        })
        $artifactReportPath = Join-Path $nativeRoot 'android-artifact.json'
        & $writeJson $artifactReportPath $integrationArtifact

        $integrationEmulator23 = Copy-JsonObject $firstBootEmulator
        $integrationEmulator23.recordedAtUtc = '2026-07-19T12:00:00Z'
        $integrationEmulator23.asset.name = $x64Name
        $integrationEmulator23.asset.sha256Before = $x64Sha
        $integrationEmulator23.asset.sha256After = $x64Sha
        $emulator23Path = Join-Path $nativeRoot 'android-api23-emulator.json'
        & $writeJson $emulator23Path $integrationEmulator23

        $integrationEmulator36 = Copy-JsonObject $firstBootEmulator
        $integrationEmulator36.recordedAtUtc = '2026-07-19T12:00:00Z'
        $integrationEmulator36.asset.name = $x64Name
        $integrationEmulator36.asset.sha256Before = $x64Sha
        $integrationEmulator36.asset.sha256After = $x64Sha
        $integrationEmulator36.runtime.apiLevel = 36
        $integrationEmulator36.runtime.deviceSdk = 36
        $integrationEmulator36.runtime.systemImagePackage = 'system-images;android-36;google_apis;x86_64'
        $integrationEmulator36.runtime.logcat.fileName = 'android-api36-logcat.txt'
        $integrationEmulator36.runtime.emulatorLog.fileName = 'android-api36-emulator.log'
        $emulator36Path = Join-Path $nativeRoot 'android-api36-emulator.json'
        & $writeJson $emulator36Path $integrationEmulator36

        $transport23Path = Join-Path $nativeRoot 'android-api23-download-transport.json'
        & $writeJson $transport23Path $firstAttemptTransport
        $integrationTransport36 = Copy-JsonObject $downloadTransport
        $integrationTransport36.scope = 'android-api36'
        $transport36Path = Join-Path $nativeRoot 'android-api36-download-transport.json'
        & $writeJson $transport36Path $integrationTransport36

        $preEvidence = @(
            foreach ($artifactEvidencePath in $artifactEvidencePaths) {
                $artifactEvidence = Get-Content -Raw -LiteralPath $artifactEvidencePath | ConvertFrom-Json -Depth 100
                [ordered]@{
                    evidenceId = "android-$($artifactEvidence.architecture)"
                    fileName = [IO.Path]::GetFileName($artifactEvidencePath)
                    sha256 = Get-LowerFileSha256 -Path $artifactEvidencePath
                    platform = 'android'
                    architecture = [string]$artifactEvidence.architecture
                }
            }
        )
        $transportReceipt = [ordered]@{
            schemaVersion = 1; kind = 'distribution-transport-receipt'; status = 'pass'
            platform = 'android'; architecture = 'multi'; identity = $identityProjection
            artifactName = $expectedTransportName; artifactId = $expectedTransportId; artifactDigest = $expectedTransportDigest
            retentionDays = 7; ifNoFilesFound = 'error'; overwrite = $false
            preEvidence = $preEvidence; unixMode = New-NotApplicableUnixEvidence
            productionReady = $false
        }
        $transportReceiptPath = Join-Path $mergeRoot 'transport-receipt.json'
        & $writeJson $transportReceiptPath $transportReceipt
        $platformEvidencePath = Join-Path $mergeRoot 'android-multi.json'
        $nativePaths = @(
            $artifactReportPath, $summaryPath, $nativeInputsPath, $nativeProvenancePath,
            $emulator23Path, $transport23Path, $emulator36Path, $transport36Path
        )
        $mergeArguments = @(
            '-NoProfile', '-File', $script:artifactValidatorPath,
            '-Mode', 'MergePlatform',
            '-Manifest', $script:manifestPath,
            '-SupportMatrix', $script:supportMatrixPath,
            '-EvidenceSchema', $script:evidenceSchemaPath,
            '-Identity', $identityPath,
            '-EvidencePath', ($artifactEvidencePaths -join ','),
            '-NativeEvidencePath', ($nativePaths -join ','),
            '-TransportReceiptPath', $transportReceiptPath,
            '-ArtifactTransportName', $expectedTransportName,
            '-ArtifactTransportId', $expectedTransportId,
            '-ArtifactTransportDigest', $expectedTransportDigest,
            '-Evidence', $platformEvidencePath,
            '-Platform', 'android',
            '-Architecture', 'multi'
        )
        $mergeOutput = & pwsh @mergeArguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Android MergePlatform behavioral fixture failed: $($mergeOutput -join [Environment]::NewLine)"
        }
        $platformEvidence = Get-Content -Raw -LiteralPath $platformEvidencePath | ConvertFrom-Json -Depth 100
        Assert-Condition (@($platformEvidence.nativeEvidence).Count -eq 8 -and @($platformEvidence.nativeCells).Count -eq 4) `
            'Android MergePlatform must emit eight exact native references and four mandatory native cells.'
        Add-Check -Name 'evidence:android-merge-platform-eight-sidecar-closure'
    }
    finally {
        if (Test-Path -LiteralPath $mergeRoot) { Remove-Item -LiteralPath $mergeRoot -Recurse -Force }
    }
}

$script:manifestPath = Resolve-ExistingFile -Path $Manifest -DisplayName 'Manifest'
$script:manifestSchemaPath = Resolve-ExistingFile -Path $ManifestSchema -DisplayName 'ManifestSchema'
$script:fixturePath = Resolve-ExistingFile -Path $Fixture -DisplayName 'Fixture'
$script:supportMatrixPath = Resolve-ExistingFile -Path $SupportMatrix -DisplayName 'SupportMatrix'
$script:supportMatrixSchemaPath = Resolve-ExistingFile -Path $SupportMatrixSchema -DisplayName 'SupportMatrixSchema'
$script:resolverPath = Resolve-ExistingFile -Path $Resolver -DisplayName 'Resolver'
$script:evidenceSchemaPath = Resolve-ExistingFile -Path $EvidenceSchema -DisplayName 'EvidenceSchema'
$script:artifactValidatorPath = Resolve-ExistingFile -Path $ArtifactValidator -DisplayName 'ArtifactValidator'
$script:workflowPath = Resolve-ExistingFile -Path $Workflow -DisplayName 'Workflow'

$manifestDocument = Read-JsonFile -Path $script:manifestPath -DisplayName 'Manifest'
$fixtureDocument = Read-JsonFile -Path $script:fixturePath -DisplayName 'Fixture'
$supportDocument = Read-JsonFile -Path $script:supportMatrixPath -DisplayName 'SupportMatrix'

Assert-JsonFileSchema -JsonPath $script:manifestPath -SchemaPath $script:manifestSchemaPath -Name 'release-assets.json'
Assert-JsonFileSchema -JsonPath $script:supportMatrixPath -SchemaPath $script:supportMatrixSchemaPath -Name 'support-matrix.json'

$areasRun = [System.Collections.Generic.List[string]]::new()
if ($Area -in @('All', 'InventorySupport')) {
    Test-InventoryContract -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument
    Test-SupportContract -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument -SupportDocument $supportDocument
    Test-ObservedInventory -ExpectedFixture $fixtureDocument -ObservedFixture (Copy-JsonObject $fixtureDocument)
    Add-Check -Name 'inventory:22-exact-assets'
    Add-Check -Name 'inventory:frozen-observation-exact-match'
    Add-Check -Name 'support:seven-exact-digest-claims'
    $areasRun.Add('InventorySupport')

    $missing = Copy-JsonObject $fixtureDocument
    $missing.assets = @($missing.assets | Select-Object -Skip 1)
    Assert-Throws -Name 'missing-asset' -Action { Test-InventoryContract $manifestDocument $missing }

    $duplicate = Copy-JsonObject $fixtureDocument
    $duplicate.assets = @($duplicate.assets) + @(Copy-JsonObject $duplicate.assets[0])
    Assert-Throws -Name 'duplicate-asset' -Action { Test-InventoryContract $manifestDocument $duplicate }

    $unexpected = Copy-JsonObject $fixtureDocument
    $unexpected.assets[0].assetId = 'unexpected-release-asset'
    Assert-Throws -Name 'unexpected-asset' -Action { Test-InventoryContract $manifestDocument $unexpected }

    $zeroByte = Copy-JsonObject $fixtureDocument
    $zeroByte.assets[0].size = 0
    Assert-Throws -Name 'zero-byte-asset' -Action { Test-InventoryContract $manifestDocument $zeroByte }

    $staleVersion = Copy-JsonObject $fixtureDocument
    @($staleVersion.assets | Where-Object assetId -ceq 'linux-deb-x64')[0].name = 'Unlimotion-1.26.0.deb'
    Assert-Throws -Name 'stale-version-name' -Action { Test-InventoryContract $manifestDocument $staleVersion }

    $hashMismatch = Copy-JsonObject $fixtureDocument
    $hashMismatch.assets[0].sha256 = '0' * 64
    Assert-Throws -Name 'fixture-hash-mismatch' -Action {
        Test-ObservedInventory $fixtureDocument $hashMismatch
    }

    $sameNameDifferentDigest = Copy-JsonObject $supportDocument
    $sameNameDifferentDigest.claims[0].assets[0].sha256 = '0' * 64
    Assert-Throws -Name 'same-name-different-digest' -Action {
        Test-SupportContract $manifestDocument $fixtureDocument $sameNameDifferentDigest
    }

    $illegalPromotion = Copy-JsonObject $supportDocument
    $illegalPromotion.claims[0].evidenceLevel = 'productionReady'
    Assert-Throws -Name 'candidate-support-promotion' -Action {
        Test-SupportContract $manifestDocument $fixtureDocument $illegalPromotion
    }

    $caseCollision = Copy-JsonObject $manifestDocument
    @($caseCollision.assets | Where-Object id -ceq 'windows-portable-x64-legacy')[0].filenameTemplate = 'unlimotion-win-portable.zip'
    Assert-Throws -Name 'case-insensitive-filename-collision' -Action {
        Test-InventoryContract $caseCollision $fixtureDocument
    }

    $unknownRole = Copy-JsonObject $manifestDocument
    $unknownRole.assets[0].role = 'installer'
    Assert-Throws -Name 'unknown-role-enum' -Action {
        Assert-JsonObjectSchema -Document $unknownRole -SchemaPath $script:manifestSchemaPath -Name 'unknown-role-manifest'
    }

    $unknownManifestProperty = Copy-JsonObject $manifestDocument
    $unknownManifestProperty | Add-Member -NotePropertyName unexpectedProperty -NotePropertyValue true
    Assert-Throws -Name 'manifest-additional-property' -Action {
        Assert-JsonObjectSchema -Document $unknownManifestProperty -SchemaPath $script:manifestSchemaPath -Name 'manifest-with-extra-property'
    }

    $unknownSupportProperty = Copy-JsonObject $supportDocument
    $unknownSupportProperty.claims[0] | Add-Member -NotePropertyName unexpectedProperty -NotePropertyValue true
    Assert-Throws -Name 'support-additional-property' -Action {
        Assert-JsonObjectSchema -Document $unknownSupportProperty -SchemaPath $script:supportMatrixSchemaPath -Name 'support-with-extra-property'
    }

    $hiddenAppImagePrerequisite = Copy-JsonObject $manifestDocument
    $hiddenAppImagePrerequisite.linuxRuntimePrerequisites.appImageExtractAndRun.debian12 = @(
        $hiddenAppImagePrerequisite.linuxRuntimePrerequisites.appImageExtractAndRun.debian12 | Where-Object { $_ -cne 'libssl3' })
    Assert-Throws -Name 'appimage-prerequisite-drift' -Action {
        Test-InventoryContract $hiddenAppImagePrerequisite $fixtureDocument
    }
}

if ($Area -in @('All', 'VelopackFeeds')) {
    Test-FeedRelations -ManifestDocument $manifestDocument -FixtureDocument $fixtureDocument
    Add-Check -Name 'feeds:five-exact-relations'
    $areasRun.Add('VelopackFeeds')

    $staleFeed = Copy-JsonObject $fixtureDocument
    $feed = @($staleFeed.feeds | Where-Object assetId -ceq 'linux-feed-json')[0]
    $content = ([string]$feed.content).Replace('"Version":"1.27.0"', '"Version":"1.26.0"')
    Set-FeedContentAndRefreshDigest -FixtureDocument $staleFeed -FeedAssetId 'linux-feed-json' -Content $content
    Assert-Throws -Name 'feed-stale-version' -Action { Test-FeedRelations $manifestDocument $staleFeed }

    $wrongHash = Copy-JsonObject $fixtureDocument
    $feed = @($wrongHash.feeds | Where-Object assetId -ceq 'macos-x64-feed-json')[0]
    $content = ([string]$feed.content).Replace('25401EA2F62DC893BA3FC5393AA216CAF193783EE78B9D3838EB13559E69D77E', ('0' * 64))
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongHash -FeedAssetId 'macos-x64-feed-json' -Content $content
    Assert-Throws -Name 'feed-sha256-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongHash }

    $wrongSize = Copy-JsonObject $fixtureDocument
    $feed = @($wrongSize.feeds | Where-Object assetId -ceq 'macos-arm64-feed-json')[0]
    $content = ([string]$feed.content).Replace('"Size":64103986', '"Size":64103985')
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongSize -FeedAssetId 'macos-arm64-feed-json' -Content $content
    Assert-Throws -Name 'feed-size-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongSize }

    $wrongPackageName = Copy-JsonObject $fixtureDocument
    $feed = @($wrongPackageName.feeds | Where-Object assetId -ceq 'windows-feed-json')[0]
    $content = ([string]$feed.content).Replace('Unlimotion-1.27.0-full.nupkg', 'Unlimotion-1.26.0-full.nupkg')
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongPackageName -FeedAssetId 'windows-feed-json' -Content $content
    Assert-Throws -Name 'feed-filename-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongPackageName }

    $wrongLegacyHash = Copy-JsonObject $fixtureDocument
    $feed = @($wrongLegacyHash.feeds | Where-Object assetId -ceq 'windows-feed-legacy')[0]
    $content = ([string]$feed.content).Replace('3D218564C242EBC2237738B6C9D8445DF16B5402', ('0' * 40))
    Set-FeedContentAndRefreshDigest -FixtureDocument $wrongLegacyHash -FeedAssetId 'windows-feed-legacy' -Content $content
    Assert-Throws -Name 'legacy-feed-sha1-mismatch' -Action { Test-FeedRelations $manifestDocument $wrongLegacyHash }

    $wrongChannel = Copy-JsonObject $manifestDocument
    @($wrongChannel.relations | Where-Object id -ceq 'windows-json-feed-to-package')[0].channel = 'linux'
    Assert-Throws -Name 'feed-wrong-channel' -Action { Test-FeedRelations $wrongChannel $fixtureDocument }
}

if ($Area -in @('All', 'IdentityTriggers')) {
    Test-IdentityContract
    Add-Check -Name 'identity:dual-tag-trigger-version-code'
    $areasRun.Add('IdentityTriggers')
}

if ($Area -in @('All', 'Retry')) {
    Test-RetryContract -ManifestDocument $manifestDocument
    Add-Check -Name 'retry:exact-budgets-and-classification'
    $areasRun.Add('Retry')

    $badRetry = Copy-JsonObject $manifestDocument
    $badRetry.retryPolicy.aptNetwork.maxAttempts = 4
    Assert-Throws -Name 'retry-apt-budget' -Action { Test-RetryContract $badRetry }

    $badCleanup = Copy-JsonObject $manifestDocument
    $badCleanup.retryPolicy.emulatorBoot.cleanup = 'reuse-avd'
    Assert-Throws -Name 'retry-emulator-cleanup' -Action { Test-RetryContract $badCleanup }

    $retryDeterministic = Copy-JsonObject $manifestDocument
    $retryDeterministic.retryPolicy.deterministic.maxAttempts = 2
    Assert-Throws -Name 'retry-deterministic-failure' -Action { Test-RetryContract $retryDeterministic }
}

if ($Area -in @('All', 'WorkflowSecurity')) {
    $workflowText = Get-Content -LiteralPath $script:workflowPath -Raw -Encoding utf8
    Test-WorkflowSecurityContract -WorkflowText $workflowText
    Test-EmbeddedWorkflowBehaviorFixtures -WorkflowText $workflowText
    Add-Check -Name 'workflow-security:pr-manual-read-only-pinned-no-mutation'
    Add-Check -Name 'workflow-security:stable-final-all-needs-fail-closed'
    Add-Check -Name 'workflow-security:root-build-scope-direct-identity'
    Add-Check -Name 'workflow-security:atomic-upload-settings'

    $producerNames = @('contract', 'windows_x64', 'linux_x64', 'macos_x64', 'macos_arm64', 'android_build', 'android_api23', 'android_api36')
    $relevantSuccess = @{ changes = 'success' }
    foreach ($producerName in $producerNames) { $relevantSuccess[$producerName] = 'success' }
    $successOutcome = Test-DistributionProducerResults -Relevant $true -Results $relevantSuccess
    Assert-Condition ($successOutcome.status -ceq 'pendingAggregate' -and [bool]$successOutcome.producersOk) 'Relevant all-success producer fixture must proceed to aggregate.'

    $irrelevantSkipped = @{ changes = 'success' }
    foreach ($producerName in $producerNames) { $irrelevantSkipped[$producerName] = 'skipped' }
    $notApplicableOutcome = Test-DistributionProducerResults -Relevant $false -Results $irrelevantSkipped
    Assert-Condition ($notApplicableOutcome.status -ceq 'notApplicable' -and -not [bool]$notApplicableOutcome.applicable) 'Irrelevant all-skipped producer fixture must be notApplicable.'
    Add-Check -Name 'workflow-producers:relevant-success-and-irrelevant-not-applicable'

    $changesFailed = $relevantSuccess.Clone()
    $changesFailed.changes = 'failure'
    Assert-Throws -Name 'producer-changes-failure' -Action {
        Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $true -Results $changesFailed)
    }
    foreach ($producerResult in @('failure', 'skipped', 'cancelled')) {
        $relevantFailure = $relevantSuccess.Clone()
        $relevantFailure.windows_x64 = $producerResult
        Assert-Throws -Name "producer-relevant-$producerResult" -Action {
            Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $true -Results $relevantFailure)
        }
    }
    $irrelevantUnexpectedSuccess = $irrelevantSkipped.Clone()
    $irrelevantUnexpectedSuccess.contract = 'success'
    Assert-Throws -Name 'producer-irrelevant-unexpected-success' -Action {
        Assert-ProducerFixtureAccepted (Test-DistributionProducerResults -Relevant $false -Results $irrelevantUnexpectedSuccess)
    }

    $triggerMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^on:\s*$' -Replacement ("on:`n  push:") -Name 'disallowed-trigger'
    Assert-Throws -Name 'workflow-disallowed-push-trigger' -Action { Test-WorkflowSecurityContract $triggerMutation }

    $writeMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^  contents:\s*read\s*$' -Replacement '  contents: write' -Name 'write-permission'
    Assert-Throws -Name 'workflow-write-permission' -Action { Test-WorkflowSecurityContract $writeMutation }

    $secretMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^env:\s*$' -Replacement ("env:`n" + '  FORBIDDEN_TOKEN: ${{ secrets.RELEASE_TOKEN }}') -Name 'secret-reference'
    Assert-Throws -Name 'workflow-secret-reference' -Action { Test-WorkflowSecurityContract $secretMutation }

    $floatingActionMutation = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'actions/checkout@[0-9a-f]{40}' -Replacement 'actions/checkout@v4' -Name 'floating-action'
    Assert-Throws -Name 'workflow-floating-action' -Action { Test-WorkflowSecurityContract $floatingActionMutation }

    $mutationCommand = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          set -euo pipefail\s*$' -Replacement ("          set -euo pipefail`n          gh release create v0.0.0-forbidden") -Name 'release-mutation'
    Assert-Throws -Name 'workflow-release-mutation-command' -Action { Test-WorkflowSecurityContract $mutationCommand }

    $withoutAlways = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^    if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$' -Replacement '    if: ${{ success() }}' -Name 'stable-final-always'
    Assert-Throws -Name 'workflow-final-without-always' -Action { Test-WorkflowSecurityContract $withoutAlways }

    $withoutNeed = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^      - android_api36\s*$' -Replacement '      # android_api36 removed by negative fixture' -Name 'stable-final-needs'
    Assert-Throws -Name 'workflow-final-missing-producer-need' -Action { Test-WorkflowSecurityContract $withoutNeed }

    $wrongRelevantRule = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'all\(value == "success" for value in mandatory\.values\(\)\)' -Replacement 'all(value == "skipped" for value in mandatory.values())' -Name 'relevant-all-success'
    Assert-Throws -Name 'workflow-relevant-not-all-success' -Action { Test-WorkflowSecurityContract $wrongRelevantRule }

    $wrongIrrelevantRule = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'if value != "skipped"' -Replacement 'if value != "success"' -Name 'irrelevant-all-skipped'
    Assert-Throws -Name 'workflow-irrelevant-not-all-skipped' -Action { Test-WorkflowSecurityContract $wrongIrrelevantRule }

    Assert-Condition ($workflowText.Contains('steps.inspect.outputs.source_short', [StringComparison]::Ordinal)) 'Workflow fixture is missing direct source_short references.'
    $wrongSourceShort = $workflowText.Replace('steps.inspect.outputs.source_short', 'needs.changes.outputs.source_short')
    Assert-Throws -Name 'workflow-final-source-short-depends-on-changes' -Action { Test-WorkflowSecurityContract $wrongSourceShort }

    $withoutBuildScope = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern 'Directory\\\.\(Build\|Packages\)\\\.\(props\|targets\)\$' -Replacement 'Directory\.Packages\.props$' -Name 'root-build-scope'
    Assert-Throws -Name 'workflow-root-build-scope-missing' -Action { Test-WorkflowSecurityContract $withoutBuildScope }

    $withoutAttributesScope = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '\\\.gitattributes\$' -Replacement '\.editorconfig$' -Name 'gitattributes-scope'
    Assert-Throws -Name 'workflow-gitattributes-scope-missing' -Action { Test-WorkflowSecurityContract $withoutAttributesScope }

    $withoutWorkflowSha = $workflowText.Replace('${{ job.workflow_sha }}', '${{ github.sha }}')
    Assert-Throws -Name 'workflow-job-workflow-sha-missing' -Action { Test-WorkflowSecurityContract $withoutWorkflowSha }

    $withoutOverwriteGuard = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          overwrite:\s*false\s*$' -Replacement '          overwrite: true' -Name 'upload-overwrite'
    Assert-Throws -Name 'workflow-upload-overwrite-enabled' -Action { Test-WorkflowSecurityContract $withoutOverwriteGuard }

    $withoutUploadAttempt = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          name:\s*distribution-contract-\$\{\{\s*needs\.changes\.outputs\.source_short\s*\}\}-attempt-\$\{\{\s*github\.run_attempt\s*\}\}\s*$' -Replacement '          name: distribution-contract-${{ needs.changes.outputs.source_short }}' -Name 'upload-attempt-scope'
    Assert-Throws -Name 'workflow-upload-without-run-attempt' -Action { Test-WorkflowSecurityContract $withoutUploadAttempt }

    $broadAndroidDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          artifact-ids:\s*\$\{\{\s*needs\.android_build\.outputs\.artifact_id\s*\}\}\s*$' -Replacement '          pattern: distribution-android-*' -Name 'android-broad-download'
    Assert-Throws -Name 'workflow-android-broad-download' -Action { Test-WorkflowSecurityContract $broadAndroidDownload }

    $nestedAndroidDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          merge-multiple:\s*true\s*$' -Replacement '          merge-multiple: false' -Name 'android-download-nested'
    Assert-Throws -Name 'workflow-android-download-not-flat' -Action { Test-WorkflowSecurityContract $nestedAndroidDownload }

    $flatAggregateDownload = Replace-WorkflowFixtureOnce -Text $workflowText -Pattern '(?m)^          merge-multiple:\s*false\s*$' -Replacement '          merge-multiple: true' -Name 'aggregate-download-flat'
    Assert-Throws -Name 'workflow-aggregate-download-not-isolated' -Action { Test-WorkflowSecurityContract $flatAggregateDownload }

    $areasRun.Add('WorkflowSecurity')
}

if ($Area -in @('All', 'Evidence')) {
    $identity = Invoke-ResolverDocument -RawTag 'v1.2.3' -IncludeSupportMatrix
    Test-AndroidNativeEvidenceConverters -IdentityDocument $identity
    $platformFixtures = @(New-PlatformEvidenceFixtures -ManifestDocument $manifestDocument -IdentityDocument $identity)
    foreach ($platformFixture in $platformFixtures) {
        Assert-JsonObjectSchema -Document $platformFixture -SchemaPath $script:evidenceSchemaPath -Name "platform-$($platformFixture.platform)-$($platformFixture.architecture)"
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('unlimotion-evidence-contract-' + [Guid]::NewGuid().ToString('N'))
    try {
        $positive = Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'positive-default-full-set') -IdentityDocument $identity -Platforms $platformFixtures
        Assert-Condition ($positive.assetCount -eq 22) 'Aggregate default must cover all 22 manifest assets.'
        Assert-Condition (@($positive.mandatoryCellIds).Count -eq 15) 'Aggregate must cover all 15 mandatory native cells.'
        Assert-Condition ($positive.productionReady -eq $false) 'Stage-3 aggregate must remain non-production-ready.'
        Assert-Condition ($positive.productionSignatureEligibility -ceq 'notApplicable') 'Stage-3 aggregate signature eligibility must be explicit N/A.'
        Add-Check -Name 'evidence:full-schema-aggregate-22-assets-15-cells'

        Assert-Throws -Name 'aggregate-explicit-partial-asset-set' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'partial-assets') -IdentityDocument $identity -Platforms $platformFixtures -RequestedAssetIds @('windows-setup-x64')
        }

        $missingCell = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($missingCell | Where-Object platform -CEQ 'android')[0]
        $androidEnvelope.nativeCells = @($androidEnvelope.nativeCells | Where-Object id -CNE 'android-api-36-x64-emulator')
        Assert-Throws -Name 'aggregate-missing-native-cell' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-cell') -IdentityDocument $identity -Platforms $missingCell
        }

        $missingNativeReference = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($missingNativeReference | Where-Object platform -CEQ 'android')[0]
        $androidEnvelope.nativeEvidence = @($androidEnvelope.nativeEvidence | Where-Object fileName -CNE 'native-cache-evidence.json')
        Assert-Throws -Name 'aggregate-missing-android-native-reference' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-android-native-reference') -IdentityDocument $identity -Platforms $missingNativeReference
        }

        $wrongNativeReferenceMode = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($wrongNativeReferenceMode | Where-Object platform -CEQ 'android')[0]
        @($androidEnvelope.nativeEvidence | Where-Object fileName -CEQ 'native-cache-evidence.json')[0].mode = 'artifact'
        Assert-Throws -Name 'aggregate-wrong-android-native-reference-mode' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-android-native-reference-mode') -IdentityDocument $identity -Platforms $wrongNativeReferenceMode
        }

        $wrongNativeCellReference = Copy-JsonObject $platformFixtures
        $androidEnvelope = @($wrongNativeCellReference | Where-Object platform -CEQ 'android')[0]
        @($androidEnvelope.nativeCells | Where-Object id -CEQ 'android-api-23-x64-emulator')[0].evidenceSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-native-cell-reference-sha-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'native-cell-reference-sha-mismatch') -IdentityDocument $identity -Platforms $wrongNativeCellReference
        }

        $wrongOs = Copy-JsonObject $platformFixtures
        @($wrongOs | Where-Object platform -CEQ 'windows')[0].nativeCells[0].osVersion = '2025'
        Assert-Throws -Name 'aggregate-wrong-native-os' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-os') -IdentityDocument $identity -Platforms $wrongOs
        }

        $wrongArchitecture = Copy-JsonObject $platformFixtures
        @($wrongArchitecture | Where-Object { $_.platform -ceq 'macos' -and $_.architecture -ceq 'arm64' })[0].nativeCells[0].architecture = 'x64'
        Assert-Throws -Name 'aggregate-wrong-native-architecture' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-architecture') -IdentityDocument $identity -Platforms $wrongArchitecture
        }

        $failedCell = Copy-JsonObject $platformFixtures
        @($failedCell | Where-Object platform -CEQ 'linux')[0].nativeCells[0].status = 'fail'
        Assert-Throws -Name 'aggregate-failed-native-cell' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'failed-cell') -IdentityDocument $identity -Platforms $failedCell
        }

        $wrongMode = Copy-JsonObject $platformFixtures
        @($wrongMode | Where-Object platform -CEQ 'linux')[0].transport.unixMode.tarStoredMode = '0644'
        Assert-Throws -Name 'transport-wrong-tar-mode' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-mode') -IdentityDocument $identity -Platforms $wrongMode
        }

        $missingPlatform = @((Copy-JsonObject $platformFixtures) | Where-Object { $_.platform -cne 'windows' })
        Assert-Throws -Name 'aggregate-missing-platform-envelope' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'missing-platform') -IdentityDocument $identity -Platforms $missingPlatform
        }

        $wrongIdentity = Copy-JsonObject $identity
        $wrongIdentity.supportMatrixSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-support-matrix-hash-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-support-hash') -IdentityDocument $wrongIdentity -Platforms $platformFixtures
        }

        $wrongManifestIdentity = Copy-JsonObject $identity
        $wrongManifestIdentity.manifestSha256 = '0' * 64
        Assert-Throws -Name 'aggregate-manifest-hash-mismatch' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'wrong-manifest-hash') -IdentityDocument $wrongManifestIdentity -Platforms $platformFixtures
        }

        $freeFormEligibility = Copy-JsonObject $platformFixtures
        @($freeFormEligibility | Where-Object platform -CEQ 'windows')[0] | Add-Member -NotePropertyName productionSignatureEligible -NotePropertyValue $true
        Assert-Throws -Name 'evidence-free-form-production-signature-eligibility' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'free-form-eligibility') -IdentityDocument $identity -Platforms $freeFormEligibility
        }

        $illegalProductionReady = Copy-JsonObject $platformFixtures
        @($illegalProductionReady | Where-Object platform -CEQ 'android')[0].productionReady = $true
        Assert-Throws -Name 'evidence-stage3-production-ready' -Action {
            Invoke-AggregateFixture -Root (Join-Path $temporaryRoot 'production-ready') -IdentityDocument $identity -Platforms $illegalProductionReady
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
    Add-Check -Name 'evidence:transport-receipt-explicit-na-and-unix-mode'
    Add-Check -Name 'evidence:missing-failed-wrong-os-arch-fail-closed'
    $areasRun.Add('Evidence')
}

$evidence = [ordered]@{
    schemaVersion = 1
    outcome = 'pass'
    areaRequested = $Area
    areasRun = @($areasRun)
    checks = @($script:checks)
    negativeFixtures = $script:negativeFixtureCount
    manifestSha256 = Get-LowerFileSha256 -Path $script:manifestPath
    fixtureSha256 = Get-LowerFileSha256 -Path $script:fixturePath
    supportMatrixSha256 = Get-LowerFileSha256 -Path $script:supportMatrixPath
    evidenceSchemaSha256 = Get-LowerFileSha256 -Path $script:evidenceSchemaPath
}
$evidenceJson = $evidence | ConvertTo-Json -Depth 20

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $fullEvidencePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidencePath)
    $evidenceDirectory = [System.IO.Path]::GetDirectoryName($fullEvidencePath)
    if ([string]::IsNullOrWhiteSpace($evidenceDirectory)) {
        throw "EvidencePath must have a parent directory: $EvidencePath"
    }
    [System.IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        $fullEvidencePath,
        $evidenceJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

Write-Output $evidenceJson
