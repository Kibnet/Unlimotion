[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Before", "After")]
    [string]$Phase,

    [Parameter(Mandatory = $true)]
    [string]$RecorderScriptPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateRange(15, 120)]
    [int]$DurationSeconds = 45,

    [ValidateRange(1, 60)]
    [int]$Fps = 30,

    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Path))
}

function Start-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][hashtable]$Environment
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $script:RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Could not start '$FilePath'."
    }

    return [pscustomobject]@{
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
    }
}

function Complete-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)]$Handle,
        [Parameter(Mandatory = $true)][string]$LogPrefix
    )

    $stdout = $Handle.StandardOutput.GetAwaiter().GetResult()
    $stderr = $Handle.StandardError.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText("$LogPrefix.stdout.log", $stdout)
    [System.IO.File]::WriteAllText("$LogPrefix.stderr.log", $stderr)

    return [pscustomobject]@{
        ExitCode = $Handle.Process.ExitCode
        StandardOutput = $stdout
        StandardError = $stderr
    }
}

function Wait-ForFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        $OwningProcess,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $deadline = [System.DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([System.DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return
        }

        if ($null -ne $OwningProcess -and $OwningProcess.HasExited) {
            throw "$Description was not produced before process $($OwningProcess.Id) exited with code $($OwningProcess.ExitCode)."
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Timed out waiting for $Description at '$Path'."
}

function Wait-ForProcessExit {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "$Description process $($Process.Id) did not exit within $TimeoutSeconds seconds."
    }
}

function Get-ErrorMessage {
    param($ErrorValue)

    if ($null -eq $ErrorValue) {
        return $null
    }

    if ($ErrorValue -is [System.Management.Automation.ErrorRecord]) {
        return $ErrorValue.Exception.Message
    }

    if ($ErrorValue -is [System.Exception]) {
        return $ErrorValue.Message
    }

    return [string]$ErrorValue
}

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedRecorderScriptPath = Resolve-RepositoryPath -Path $RecorderScriptPath
if (-not (Test-Path -LiteralPath $resolvedRecorderScriptPath -PathType Leaf)) {
    throw "Recorder script was not found: $resolvedRecorderScriptPath"
}

$recorderLauncherPath = Join-Path $PSScriptRoot "record-app-window-per-monitor-dpi.ps1"
if (-not (Test-Path -LiteralPath $recorderLauncherPath -PathType Leaf)) {
    throw "Per-monitor DPI recorder launcher was not found: $recorderLauncherPath"
}

$resolvedOutputPath = Resolve-RepositoryPath -Path $OutputPath
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "Output path must include a directory: $resolvedOutputPath"
}

[void][System.IO.Directory]::CreateDirectory($outputDirectory)
if (Test-Path -LiteralPath $resolvedOutputPath) {
    if (-not $Overwrite) {
        throw "Output already exists. Pass -Overwrite to replace this generated artifact: $resolvedOutputPath"
    }

    Remove-Item -LiteralPath $resolvedOutputPath -Force
}

$runId = "{0}-{1}" -f [System.DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ"), ([System.Guid]::NewGuid().ToString("N"))
$runDirectory = Join-Path $RepoRoot "artifacts\validation\task-spaces-evidence\$runId"
$handshakeDirectory = Join-Path $runDirectory "handshake"
$screenshotDirectory = Join-Path $runDirectory "screenshots"
$testResultDirectory = Join-Path $runDirectory "test-results"
[void][System.IO.Directory]::CreateDirectory($handshakeDirectory)
[void][System.IO.Directory]::CreateDirectory($screenshotDirectory)
[void][System.IO.Directory]::CreateDirectory($testResultDirectory)

$windowTitle = "Unlimotion Task Spaces Evidence $Phase $runId"
$windowReadyPath = Join-Path $handshakeDirectory "window-ready.json"
$scenarioGoPath = Join-Path $handshakeDirectory "scenario-go.signal"
$scenarioCompletePath = Join-Path $handshakeDirectory "scenario-complete.json"
$recordingFinishedPath = Join-Path $handshakeDirectory "recording-finished.signal"
$manifestPath = Join-Path $runDirectory "manifest.json"
$testLogPrefix = Join-Path $runDirectory "test"
$recorderLogPrefix = Join-Path $runDirectory "recorder"
$testProjectPath = Join-Path $RepoRoot "tests\Unlimotion.UiTests.FlaUI\Unlimotion.UiTests.FlaUI.csproj"
$targetTestFilter = "/*/*/TaskSpacesFlaUiTests/Task_spaces_switch_A_B_A_and_emit_visual_evidence"

$buildArguments = @(
    "build",
    $testProjectPath,
    "-c", "Release",
    "--no-restore",
    "-p:UseSharedCompilation=false"
)
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "FlaUI evidence project build failed with exit code $LASTEXITCODE."
}

$testEnvironment = @{
    UNLIMOTION_AUTOMATION_DESKTOP_MONITOR = "right"
    UNLIMOTION_AUTOMATION_WINDOW_TITLE = $windowTitle
    UNLIMOTION_TASK_SPACES_EVIDENCE_HANDSHAKE_DIR = $handshakeDirectory
    UNLIMOTION_TASK_SPACES_EVIDENCE_ARTIFACT_DIR = $screenshotDirectory
}
$testArguments = @(
    "test",
    $testProjectPath,
    "-c", "Release",
    "--no-build",
    "--no-restore",
    "--",
    "--treenode-filter", $targetTestFilter,
    "--maximum-parallel-tests", "1",
    "--output", "Detailed",
    "--results-directory", $testResultDirectory
)

$testHandle = $null
$recorderHandle = $null
$testResult = $null
$recorderResult = $null
$recordingStatus = "NotStarted"
$recordingError = $null
$caughtError = $null

try {
    $testHandle = Start-CapturedProcess `
        -FilePath "dotnet" `
        -Arguments $testArguments `
        -Environment $testEnvironment

    Wait-ForFile `
        -Path $windowReadyPath `
        -TimeoutSeconds 120 `
        -OwningProcess $testHandle.Process `
        -Description "window-ready handshake"

    $readyDocument = Get-Content -Raw -LiteralPath $windowReadyPath | ConvertFrom-Json
    if (-not [string]::Equals(
            [string]$readyDocument.WindowTitle,
            $windowTitle,
            [System.StringComparison]::Ordinal)) {
        throw "Ready window title mismatch. Expected '$windowTitle', actual '$($readyDocument.WindowTitle)'."
    }

    $pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
    $recorderArguments = @(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-File", $recorderLauncherPath,
        "-WindowTitle", $windowTitle,
        "-Output", $resolvedOutputPath,
        "-DurationSeconds", $DurationSeconds.ToString(
            [System.Globalization.CultureInfo]::InvariantCulture),
        "-Fps", $Fps.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )
    $recorderHandle = Start-CapturedProcess `
        -FilePath $pwshPath `
        -Arguments $recorderArguments `
        -Environment @{
            UNLIMOTION_RECORD_APP_SCREEN_SCRIPT = $resolvedRecorderScriptPath
        }

    Start-Sleep -Seconds 2
    [System.IO.File]::WriteAllText(
        $scenarioGoPath,
        [System.DateTime]::UtcNow.ToString("O"))

    Wait-ForFile `
        -Path $scenarioCompletePath `
        -TimeoutSeconds 120 `
        -OwningProcess $testHandle.Process `
        -Description "scenario-complete handshake"

    Wait-ForProcessExit `
        -Process $recorderHandle.Process `
        -TimeoutSeconds ($DurationSeconds + 30) `
        -Description "Recorder"
    $recorderResult = Complete-CapturedProcess `
        -Handle $recorderHandle `
        -LogPrefix $recorderLogPrefix

    if ($recorderResult.ExitCode -eq 0 -and
        (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) -and
        (Get-Item -LiteralPath $resolvedOutputPath).Length -gt 0) {
        $recordingStatus = "Captured"
    }
    else {
        $recordingStatus = "ScreenshotFallback"
        $recordingError = "Recorder exited with code $($recorderResult.ExitCode). See '$recorderLogPrefix.stderr.log'."
        if (Test-Path -LiteralPath $resolvedOutputPath) {
            Remove-Item -LiteralPath $resolvedOutputPath -Force
        }
    }
}
catch {
    $caughtError = $_
}
finally {
    if ($null -ne $testHandle -and
        -not (Test-Path -LiteralPath $recordingFinishedPath)) {
        $recordingFinishedDocument = [ordered]@{
            RecordingStatus = $recordingStatus
            Error = $recordingError
            CompletedAtUtc = [System.DateTime]::UtcNow.ToString("O")
        } | ConvertTo-Json
        [System.IO.File]::WriteAllText($recordingFinishedPath, $recordingFinishedDocument)
    }

    if ($null -ne $testHandle) {
        try {
            Wait-ForProcessExit `
                -Process $testHandle.Process `
                -TimeoutSeconds 45 `
                -Description "Targeted FlaUI test"
            $testResult = Complete-CapturedProcess `
                -Handle $testHandle `
                -LogPrefix $testLogPrefix
        }
        catch {
            if ($null -eq $caughtError) {
                $caughtError = $_
            }

            if (-not $testHandle.Process.HasExited) {
                try {
                    $testHandle.Process.Kill($true)
                    $testHandle.Process.WaitForExit()
                }
                catch {
                    if ($null -eq $caughtError) {
                        $caughtError = $_
                    }
                }
            }

            if ($testHandle.Process.HasExited -and $null -eq $testResult) {
                try {
                    $testResult = Complete-CapturedProcess `
                        -Handle $testHandle `
                        -LogPrefix $testLogPrefix
                }
                catch {
                    if ($null -eq $caughtError) {
                        $caughtError = $_
                    }
                }
            }
        }
    }

    if ($null -ne $recorderHandle -and $null -eq $recorderResult) {
        try {
            if (-not $recorderHandle.Process.HasExited) {
                $recorderHandle.Process.Kill($true)
                $recorderHandle.Process.WaitForExit()
            }

            $recorderResult = Complete-CapturedProcess `
                -Handle $recorderHandle `
                -LogPrefix $recorderLogPrefix
        }
        catch {
            if ($null -eq $caughtError) {
                $caughtError = $_
            }
        }
    }
}

if ($null -ne $recorderResult -and $recordingStatus -eq "NotStarted") {
    $recordingStatus = "ScreenshotFallback"
    $recordingError = "Recorder exited with code $($recorderResult.ExitCode). See '$recorderLogPrefix.stderr.log'."
    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }
}

$expectedScreenshots = @(
    "space-a.png",
    "space-b.png",
    "space-a-return.png",
    "settings-spaces.png"
)
$screenshotPaths = @()
foreach ($screenshotName in $expectedScreenshots) {
    $screenshotPath = Join-Path $screenshotDirectory $screenshotName
    if (-not (Test-Path -LiteralPath $screenshotPath -PathType Leaf) -or
        (Get-Item -LiteralPath $screenshotPath).Length -le 0) {
        if ($null -eq $caughtError) {
            $caughtError = [System.InvalidOperationException]::new(
                "Expected screenshot was not created: $screenshotPath")
        }
    }
    else {
        $screenshotPaths += $screenshotPath
    }
}

if ($null -ne $testResult -and $testResult.ExitCode -ne 0 -and $null -eq $caughtError) {
    $caughtError = [System.InvalidOperationException]::new(
        "Targeted FlaUI test failed with exit code $($testResult.ExitCode). See '$testLogPrefix.stderr.log'.")
}

$scenarioDocument = if (Test-Path -LiteralPath $scenarioCompletePath) {
    Get-Content -Raw -LiteralPath $scenarioCompletePath | ConvertFrom-Json
}
else {
    $null
}

$manifest = [ordered]@{
    RunId = $runId
    Phase = $Phase
    Monitor = "right"
    WindowTitle = $windowTitle
    TestFilter = $targetTestFilter
    TestExitCode = if ($null -eq $testResult) { $null } else { $testResult.ExitCode }
    ScenarioSucceeded = if ($null -eq $scenarioDocument) { $false } else { [bool]$scenarioDocument.Success }
    RecordingStatus = $recordingStatus
    RecordingError = $recordingError
    VideoPath = if ($recordingStatus -eq "Captured") { $resolvedOutputPath } else { $null }
    Screenshots = $screenshotPaths
    TestResultsDirectory = $testResultDirectory
    CompletedAtUtc = [System.DateTime]::UtcNow.ToString("O")
    Error = Get-ErrorMessage -ErrorValue $caughtError
}
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 5))

if ($null -ne $caughtError) {
    throw $caughtError
}

if ($recordingStatus -eq "ScreenshotFallback") {
    Write-Warning $recordingError
}

Write-Host "Task-space evidence completed."
Write-Host "Run directory: $runDirectory"
Write-Host "Manifest: $manifestPath"
Write-Host "Recording status: $recordingStatus"
if ($recordingStatus -eq "Captured") {
    Write-Host "Video: $resolvedOutputPath"
}
else {
    Write-Host "Screenshots: $screenshotDirectory"
}
