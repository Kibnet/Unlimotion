[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Before", "After")]
    [string]$Phase,

    [string]$RecorderScriptPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scenarioTimeoutSeconds = 90
$windowReadyTimeoutSeconds = 120
$recorderStartupTimeoutSeconds = 30
$recorderDurationSeconds = $scenarioTimeoutSeconds + 15
$recorderExitGraceSeconds = 30
$testExitTimeoutSeconds = 60
$expectedWidth = 1280
$expectedHeight = 800
$expectedFps = 30
$minimumAverageFpsRatio = 0.98
$targetTestFilter = "/*/*/MainWindowFlaUiTests/StatusContract_TerminalPickerAndUnarchive"
$expectedBeforeFailureIds = @(
    "TerminalInProgressWasEnabled",
    "UnarchiveDidNotRestorePrepared"
)

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$testProjectPath = Join-Path $repoRoot "tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj"
$trackedProcessStartTicks = @{}

function Resolve-ExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [string]$EnvironmentVariableName
    )

    $candidate = $null
    if (-not [string]::IsNullOrWhiteSpace($EnvironmentVariableName)) {
        $candidate = [System.Environment]::GetEnvironmentVariable($EnvironmentVariableName)
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $command = Get-Command $CommandName -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $command) {
            $candidate = $command.Source
        }
    }

    if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        $environmentHint = if ([string]::IsNullOrWhiteSpace($EnvironmentVariableName)) {
            ""
        }
        else {
            " or set $EnvironmentVariableName"
        }

        throw "$CommandName was not found. Add it to PATH$environmentHint."
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

function Resolve-RecorderPath {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "RecorderScriptPath does not exist: $RequestedPath"
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    if ([string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        throw "RecorderScriptPath was not provided and CODEX_HOME is not set. Pass -RecorderScriptPath explicitly."
    }

    $defaultPath = Join-Path $env:CODEX_HOME "skills/record-app-screen/scripts/record_app_window.ps1"
    if (-not (Test-Path -LiteralPath $defaultPath -PathType Leaf)) {
        throw "RecorderScriptPath was not provided and the only permitted default does not exist: $defaultPath"
    }

    return (Resolve-Path -LiteralPath $defaultPath).Path
}

function Assert-RecorderScriptContract {
    param([Parameter(Mandatory = $true)][string]$Path)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$parseErrors)

    if ($parseErrors.Count -gt 0) {
        $messages = $parseErrors | ForEach-Object { $_.Message }
        throw "Recorder script has PowerShell parse errors: $($messages -join '; ')"
    }

    if ($null -eq $ast.ParamBlock) {
        throw "Recorder script does not declare a param block: $Path"
    }

    $parameterNames = @(
        $ast.ParamBlock.Parameters |
            ForEach-Object { $_.Name.VariablePath.UserPath }
    )

    foreach ($requiredName in @("Output", "WindowTitle", "DurationSeconds", "Fps")) {
        if ($requiredName -notin $parameterNames) {
            throw "Recorder script is missing the required -$requiredName parameter: $Path"
        }
    }
}

function Resolve-OutputFilePath {
    param([Parameter(Mandatory = $true)][string]$RequestedPath)

    $fullPath = if ([System.IO.Path]::IsPathRooted($RequestedPath)) {
        [System.IO.Path]::GetFullPath($RequestedPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RequestedPath))
    }

    if (-not [string]::Equals(
            [System.IO.Path]::GetExtension($fullPath),
            ".mp4",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputPath must point to an .mp4 file: $fullPath"
    }

    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw "OutputPath must have a parent directory: $fullPath"
    }

    [void][System.IO.Directory]::CreateDirectory($directory)
    return $fullPath
}

function Start-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [hashtable]$EnvironmentOverrides = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    foreach ($entry in $EnvironmentOverrides.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        $process.Dispose()
        throw "Failed to start process: $FilePath"
    }

    return [pscustomobject]@{
        Process = $process
        StandardOutputTask = $process.StandardOutput.ReadToEndAsync()
        StandardErrorTask = $process.StandardError.ReadToEndAsync()
        StandardOutput = $null
        StandardError = $null
    }
}

function Wait-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Handle,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    if (-not $Handle.Process.WaitForExit($TimeoutSeconds * 1000)) {
        return $false
    }

    $Handle.Process.WaitForExit()
    return $true
}

function Complete-CapturedOutput {
    param([Parameter(Mandatory = $true)][object]$Handle)

    if (-not $Handle.Process.HasExited) {
        throw "Cannot read captured output before process $($Handle.Process.Id) exits."
    }

    if ($null -eq $Handle.StandardOutput) {
        $Handle.StandardOutput = $Handle.StandardOutputTask.GetAwaiter().GetResult()
    }

    if ($null -eq $Handle.StandardError) {
        $Handle.StandardError = $Handle.StandardErrorTask.GetAwaiter().GetResult()
    }
}

function Get-OutputTail {
    param(
        [AllowNull()]
        [string]$Text,

        [int]$LineCount = 60
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return "<empty>"
    }

    $lines = $Text -split "`r?`n"
    return (($lines | Select-Object -Last $LineCount) -join [System.Environment]::NewLine)
}

function Assert-BeforeTestFailureContract {
    param(
        [Parameter(Mandatory = $true)][string]$TestOutput,
        [Parameter(Mandatory = $true)][int]$ExitCode
    )

    if ($ExitCode -ne 2) {
        throw "Before phase must use Microsoft.Testing.Platform test-failure exit code 2; actual $ExitCode."
    }

    $requiredSummaryPatterns = @(
        '(?im)^\s*(?:итог|total)\s*:\s*1\s*$',
        '(?im)^\s*(?:сбой|failed)\s*:\s*1\s*$',
        '(?im)^\s*(?:успешно|passed|succeeded)\s*:\s*0\s*$',
        '(?im)^\s*(?:пропущено|skipped)\s*:\s*0\s*$'
    )
    foreach ($pattern in $requiredSummaryPatterns) {
        if (-not [System.Text.RegularExpressions.Regex]::IsMatch($TestOutput, $pattern)) {
            throw "Before phase output did not report exactly one executed and failed test with zero passes/skips."
        }
    }

    $failureHeaders = [System.Text.RegularExpressions.Regex]::Matches(
        $TestOutput,
        '(?im)^\s*\[Test Failure\]\s+([A-Za-z0-9_.`]+):')
    if ($failureHeaders.Count -ne 1 -or
        -not [string]::Equals(
            $failureHeaders[0].Groups[1].Value,
            'AssertionException',
            [System.StringComparison]::Ordinal)) {
        throw "Before phase must contain exactly one top-level AssertionException failure and no harness failure."
    }

    $unexpectedFailurePattern =
        '(?i)Unhandled exception|Process terminated|Test host.*(?:crash|abort)|' +
        '\[Test Failure\]\s+(?:TimeoutException|InvalidOperationException|ObjectDisposedException):'
    if ([System.Text.RegularExpressions.Regex]::IsMatch($TestOutput, $unexpectedFailurePattern)) {
        throw "Before phase output contains an unexpected harness, timeout, or teardown failure."
    }
}

function Register-TrackedProcess {
    param([Parameter(Mandatory = $true)][int]$ProcessIdentifier)

    if ($trackedProcessStartTicks.ContainsKey($ProcessIdentifier)) {
        return
    }

    try {
        $process = Get-Process -Id $ProcessIdentifier -ErrorAction Stop
        $trackedProcessStartTicks[$ProcessIdentifier] = $process.StartTime.ToUniversalTime().Ticks
    }
    catch {
        # The process may have exited between discovery and registration.
    }
}

function Test-TrackedProcessIsAlive {
    param([Parameter(Mandatory = $true)][int]$ProcessIdentifier)

    if (-not $trackedProcessStartTicks.ContainsKey($ProcessIdentifier)) {
        return $false
    }

    try {
        $process = Get-Process -Id $ProcessIdentifier -ErrorAction Stop
        return $process.StartTime.ToUniversalTime().Ticks -eq $trackedProcessStartTicks[$ProcessIdentifier]
    }
    catch {
        return $false
    }
}

function Get-DescendantProcessIds {
    param([Parameter(Mandatory = $true)][int]$RootProcessIdentifier)

    $result = [System.Collections.Generic.List[int]]::new()
    $queue = [System.Collections.Generic.Queue[int]]::new()
    $visited = [System.Collections.Generic.HashSet[int]]::new()
    $queue.Enqueue($RootProcessIdentifier)
    [void]$visited.Add($RootProcessIdentifier)

    while ($queue.Count -gt 0) {
        $parentIdentifier = $queue.Dequeue()
        $children = @(
            Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId = $parentIdentifier" -ErrorAction Stop
        )

        foreach ($child in $children) {
            $childIdentifier = [int]$child.ProcessId
            if ($visited.Add($childIdentifier)) {
                $result.Add($childIdentifier)
                $queue.Enqueue($childIdentifier)
            }
        }
    }

    return @($result.ToArray())
}

function Register-TrackedProcessTree {
    param([Parameter(Mandatory = $true)][int]$RootProcessIdentifier)

    Register-TrackedProcess -ProcessIdentifier $RootProcessIdentifier
    foreach ($descendantIdentifier in @(Get-DescendantProcessIds -RootProcessIdentifier $RootProcessIdentifier)) {
        Register-TrackedProcess -ProcessIdentifier $descendantIdentifier
    }
}

function Test-IsDescendantProcess {
    param(
        [Parameter(Mandatory = $true)][int]$RootProcessIdentifier,
        [Parameter(Mandatory = $true)][int]$CandidateProcessIdentifier
    )

    return $CandidateProcessIdentifier -in @(
        Get-DescendantProcessIds -RootProcessIdentifier $RootProcessIdentifier
    )
}

function Stop-TrackedProcesses {
    $liveProcesses = @(
        foreach ($entry in $trackedProcessStartTicks.GetEnumerator()) {
            try {
                $process = Get-Process -Id ([int]$entry.Key) -ErrorAction Stop
                if ($process.StartTime.ToUniversalTime().Ticks -eq [long]$entry.Value) {
                    $process
                }
            }
            catch {
                # Already exited; there is nothing to stop.
            }
        }
    )

    foreach ($process in @($liveProcesses | Sort-Object StartTime -Descending)) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            if (Test-TrackedProcessIsAlive -ProcessIdentifier $process.Id) {
                throw
            }
        }
    }

    $deadline = [System.DateTime]::UtcNow.AddSeconds(10)
    while ([System.DateTime]::UtcNow -lt $deadline) {
        $remaining = @(
            $trackedProcessStartTicks.Keys |
                Where-Object { Test-TrackedProcessIsAlive -ProcessIdentifier ([int]$_) }
        )
        if ($remaining.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    $stillAlive = @(
        $trackedProcessStartTicks.Keys |
            Where-Object { Test-TrackedProcessIsAlive -ProcessIdentifier ([int]$_) }
    )
    if ($stillAlive.Count -gt 0) {
        throw "Tracked processes did not exit after cleanup: $($stillAlive -join ', ')"
    }
}

function Initialize-WindowInspector {
    if ("StatusContractWindowInspector" -as [type]) {
        return
    }

    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class StatusContractWindowInspector
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

    try {
        [void][StatusContractWindowInspector]::SetProcessDPIAware()
    }
    catch {
        # DPI awareness may already be configured by the PowerShell host.
    }
}

function Get-VisibleTopLevelWindows {
    param([Parameter(Mandatory = $true)][int]$ProcessIdentifier)

    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [StatusContractWindowInspector+EnumWindowsProc]{
        param([System.IntPtr]$Handle, [System.IntPtr]$Parameter)

        if (-not [StatusContractWindowInspector]::IsWindowVisible($Handle) -or
            [StatusContractWindowInspector]::IsIconic($Handle)) {
            return $true
        }

        $windowProcessIdentifier = [uint32]0
        [void][StatusContractWindowInspector]::GetWindowThreadProcessId(
            $Handle,
            [ref]$windowProcessIdentifier)
        if ([int]$windowProcessIdentifier -ne $ProcessIdentifier) {
            return $true
        }

        $titleBuilder = [System.Text.StringBuilder]::new(1024)
        [void][StatusContractWindowInspector]::GetWindowText(
            $Handle,
            $titleBuilder,
            $titleBuilder.Capacity)
        $title = $titleBuilder.ToString()

        $rect = [StatusContractWindowInspector+RECT]::new()
        if (-not [StatusContractWindowInspector]::GetWindowRect($Handle, [ref]$rect)) {
            return $true
        }

        $windows.Add([pscustomobject]@{
            Handle = $Handle
            ProcessId = [int]$windowProcessIdentifier
            Title = $title
            Left = $rect.Left
            Top = $rect.Top
            Right = $rect.Right
            Bottom = $rect.Bottom
        })

        return $true
    }

    [void][StatusContractWindowInspector]::EnumWindows($callback, [System.IntPtr]::Zero)
    return @($windows.ToArray())
}

function Read-JsonFileWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][scriptblock]$HealthCheck
    )

    $deadline = [System.DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastParseError = $null

    while ([System.DateTime]::UtcNow -lt $deadline) {
        & $HealthCheck

        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                $json = [System.IO.File]::ReadAllText($Path)
                if ([string]::IsNullOrWhiteSpace($json)) {
                    throw "JSON file is empty."
                }

                return $json | ConvertFrom-Json -Depth 20 -ErrorAction Stop
            }
            catch {
                $lastParseError = $_.Exception.Message
            }
        }

        Start-Sleep -Milliseconds 100
    }

    $parseSuffix = if ($null -eq $lastParseError) {
        ""
    }
    else {
        " Last parse error: $lastParseError"
    }
    throw "Timed out waiting for valid JSON at $Path.$parseSuffix"
}

function Get-RequiredJsonProperty {
    param(
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$DocumentName
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$DocumentName is missing required property '$Name'."
    }

    return $property
}

function Convert-ToRequiredInt32 {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory = $true)][string]$FieldName
    )

    try {
        return [System.Convert]::ToInt32($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "$FieldName must be an Int32. Actual value: '$Value'."
    }
}

function Write-JsonSignal {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Payload
    )

    $json = $Payload | ConvertTo-Json -Depth 10
    $temporaryPath = "$Path.tmp-$([System.Guid]::NewGuid().ToString('N'))"
    [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporaryPath, $Path, $true)
}

function Assert-ExactFailureIds {
    param(
        [Parameter(Mandatory = $true)][object]$ScenarioDocument,
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [string[]]$ExpectedFailureIds
    )

    $failureIdsProperty = Get-RequiredJsonProperty `
        -InputObject $ScenarioDocument `
        -Name "FailureIds" `
        -DocumentName "scenario-complete.json"
    if ($null -eq $failureIdsProperty.Value -or
        $failureIdsProperty.Value -isnot [System.Array]) {
        throw "scenario-complete.json.FailureIds must be a JSON array of strings."
    }

    $actualFailureIds = @($failureIdsProperty.Value)
    foreach ($failureId in $actualFailureIds) {
        if ($failureId -isnot [string]) {
            throw "scenario-complete.json.FailureIds must contain strings only."
        }
    }

    $normalizedExpectedFailureIds = @(
        $ExpectedFailureIds |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($actualFailureIds.Count -ne $normalizedExpectedFailureIds.Count) {
        throw "Unexpected FailureIds count. Expected $($normalizedExpectedFailureIds.Count), actual $($actualFailureIds.Count): $($actualFailureIds -join ', ')"
    }

    foreach ($expectedFailureId in $normalizedExpectedFailureIds) {
        $matchCount = @(
            $actualFailureIds |
                Where-Object { [string]::Equals($_, $expectedFailureId, [System.StringComparison]::Ordinal) }
        ).Count
        if ($matchCount -ne 1) {
            throw "FailureIds must contain '$expectedFailureId' exactly once. Actual: $($actualFailureIds -join ', ')"
        }
    }

    return $actualFailureIds
}

function Convert-FrameRateToDouble {
    param([Parameter(Mandatory = $true)][string]$FrameRate)

    $parts = $FrameRate.Split('/')
    if ($parts.Count -eq 1) {
        return [double]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture)
    }

    if ($parts.Count -ne 2) {
        throw "Unsupported ffprobe frame rate: $FrameRate"
    }

    $numerator = [double]::Parse($parts[0], [System.Globalization.CultureInfo]::InvariantCulture)
    $denominator = [double]::Parse($parts[1], [System.Globalization.CultureInfo]::InvariantCulture)
    if ($denominator -eq 0) {
        throw "ffprobe returned a zero frame-rate denominator: $FrameRate"
    }

    return $numerator / $denominator
}

function Assert-SafeHandshakeDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $leafName = [System.IO.Path]::GetFileName($fullPath)

    if (-not $fullPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $leafName.StartsWith("unlimotion-status-contract-", [System.StringComparison]::Ordinal)) {
        throw "Refusing to recursively remove an unexpected handshake path: $fullPath"
    }
}

if (-not $IsWindows) {
    throw "Status contract video evidence requires an interactive Windows desktop session."
}

if (-not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
    throw "Targeted FlaUI project was not found: $testProjectPath"
}

$resolvedRecorderScriptPath = Resolve-RecorderPath -RequestedPath $RecorderScriptPath
Assert-RecorderScriptContract -Path $resolvedRecorderScriptPath
$resolvedOutputPath = Resolve-OutputFilePath -RequestedPath $OutputPath
$pwshPath = Resolve-ExecutablePath -CommandName "pwsh"
$dotnetPath = Resolve-ExecutablePath -CommandName "dotnet"
$ffmpegPath = Resolve-ExecutablePath -CommandName "ffmpeg" -EnvironmentVariableName "FFMPEG_PATH"
$ffprobePath = Resolve-ExecutablePath -CommandName "ffprobe" -EnvironmentVariableName "FFPROBE_PATH"
Initialize-WindowInspector

$runId = [System.Guid]::NewGuid().ToString("N")
$windowTitle = "Unlimotion Status Contract $Phase $runId"
$handshakeDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "unlimotion-status-contract-$runId"
$handshakeDirectory = [System.IO.Path]::GetFullPath($handshakeDirectory)
Assert-SafeHandshakeDirectory -Path $handshakeDirectory
[void][System.IO.Directory]::CreateDirectory($handshakeDirectory)

$windowReadyPath = Join-Path $handshakeDirectory "window-ready.json"
$scenarioGoPath = Join-Path $handshakeDirectory "scenario-go.signal"
$scenarioCompletePath = Join-Path $handshakeDirectory "scenario-complete.json"
$recordingFinishedPath = Join-Path $handshakeDirectory "recording-finished.signal"

$testHandle = $null
$recorderHandle = $null
$probeHandle = $null
$testOutput = $null
$recorderOutput = $null
$caughtError = $null
$result = $null
$recordingFinishedWritten = $false
$readyProcessIdentifier = $null
$ffmpegProcessIdentifiers = @()
$failureIds = @()

try {
    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }

    $testEnvironment = @{
        UNLIMOTION_STATUS_CONTRACT_HANDSHAKE_DIR = $handshakeDirectory
        UNLIMOTION_AUTOMATION_WINDOW_TITLE = $windowTitle
        UNLIMOTION_STATUS_CONTRACT_ARTIFACT_DIR = [System.IO.Path]::GetDirectoryName($resolvedOutputPath)
        UNLIMOTION_STATUS_CONTRACT_PHASE = $Phase
    }
    $testArguments = @(
        "test",
        "--project",
        $testProjectPath,
        "-c", "Debug",
        "--no-build",
        "--",
        "--treenode-filter", $targetTestFilter,
        "--maximum-parallel-tests", "1",
        "--output", "Detailed"
    )

    Write-Host "Starting targeted FlaUI status contract test."
    Write-Host "  Phase:      $Phase"
    Write-Host "  Handshake:  $handshakeDirectory"
    Write-Host "  Window:     $windowTitle"
    Write-Host "  Output:     $resolvedOutputPath"

    $testHandle = Start-CapturedProcess `
        -FilePath $dotnetPath `
        -Arguments $testArguments `
        -WorkingDirectory $repoRoot `
        -EnvironmentOverrides $testEnvironment
    Register-TrackedProcess -ProcessIdentifier $testHandle.Process.Id

    $readyDocument = Read-JsonFileWithRetry `
        -Path $windowReadyPath `
        -TimeoutSeconds $windowReadyTimeoutSeconds `
        -HealthCheck {
            if ($testHandle.Process.HasExited) {
                throw "Targeted FlaUI test exited before writing window-ready.json (exit code $($testHandle.Process.ExitCode))."
            }
        }

    $readyProcessIdentifier = Convert-ToRequiredInt32 `
        -Value (Get-RequiredJsonProperty `
            -InputObject $readyDocument `
            -Name "ProcessId" `
            -DocumentName "window-ready.json").Value `
        -FieldName "window-ready.json.ProcessId"
    $readyWindowTitle = [string](Get-RequiredJsonProperty `
        -InputObject $readyDocument `
        -Name "WindowTitle" `
        -DocumentName "window-ready.json").Value
    $readyRect = (Get-RequiredJsonProperty `
        -InputObject $readyDocument `
        -Name "OuterRect" `
        -DocumentName "window-ready.json").Value

    if (-not [string]::Equals($readyWindowTitle, $windowTitle, [System.StringComparison]::Ordinal)) {
        throw "window-ready.json title mismatch. Expected '$windowTitle', actual '$readyWindowTitle'."
    }

    if (-not (Test-IsDescendantProcess `
            -RootProcessIdentifier $testHandle.Process.Id `
            -CandidateProcessIdentifier $readyProcessIdentifier)) {
        throw "Ready process $readyProcessIdentifier is not a descendant of targeted test process $($testHandle.Process.Id)."
    }
    Register-TrackedProcessTree -RootProcessIdentifier $testHandle.Process.Id
    Register-TrackedProcess -ProcessIdentifier $readyProcessIdentifier

    $readyLeft = Convert-ToRequiredInt32 `
        -Value (Get-RequiredJsonProperty -InputObject $readyRect -Name "Left" -DocumentName "window-ready.json.OuterRect").Value `
        -FieldName "window-ready.json.OuterRect.Left"
    $readyTop = Convert-ToRequiredInt32 `
        -Value (Get-RequiredJsonProperty -InputObject $readyRect -Name "Top" -DocumentName "window-ready.json.OuterRect").Value `
        -FieldName "window-ready.json.OuterRect.Top"
    $readyRight = Convert-ToRequiredInt32 `
        -Value (Get-RequiredJsonProperty -InputObject $readyRect -Name "Right" -DocumentName "window-ready.json.OuterRect").Value `
        -FieldName "window-ready.json.OuterRect.Right"
    $readyBottom = Convert-ToRequiredInt32 `
        -Value (Get-RequiredJsonProperty -InputObject $readyRect -Name "Bottom" -DocumentName "window-ready.json.OuterRect").Value `
        -FieldName "window-ready.json.OuterRect.Bottom"

    if (($readyRight - $readyLeft) -ne $expectedWidth -or
        ($readyBottom - $readyTop) -ne $expectedHeight) {
        throw "Ready outer rectangle must be ${expectedWidth}x${expectedHeight}; actual $($readyRight - $readyLeft)x$($readyBottom - $readyTop)."
    }

    $visibleWindows = @(Get-VisibleTopLevelWindows -ProcessIdentifier $readyProcessIdentifier)
    if ($visibleWindows.Count -ne 1) {
        $windowSummary = @(
            $visibleWindows |
                ForEach-Object { "'$($_.Title)' [$($_.Left),$($_.Top),$($_.Right),$($_.Bottom)]" }
        ) -join "; "
        throw "Ready process $readyProcessIdentifier must own exactly one visible, non-minimized top-level window. Found $($visibleWindows.Count): $windowSummary"
    }

    $visibleWindow = $visibleWindows[0]
    if (-not [string]::Equals($visibleWindow.Title, $readyWindowTitle, [System.StringComparison]::Ordinal) -or
        $visibleWindow.Left -ne $readyLeft -or
        $visibleWindow.Top -ne $readyTop -or
        $visibleWindow.Right -ne $readyRight -or
        $visibleWindow.Bottom -ne $readyBottom) {
        throw "Visible window does not exactly match window-ready.json PID/title/outer rect."
    }

    $recorderArguments = @(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-File", $resolvedRecorderScriptPath,
        "-WindowTitle", $readyWindowTitle,
        "-Output", $resolvedOutputPath,
        "-DurationSeconds", $recorderDurationSeconds.ToString(
            [System.Globalization.CultureInfo]::InvariantCulture),
        "-Fps", $expectedFps.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )

    if ($recorderDurationSeconds -le ($scenarioTimeoutSeconds + 10)) {
        throw "Recorder duration must be strictly greater than scenario timeout plus 10 seconds."
    }

    $recorderHandle = Start-CapturedProcess `
        -FilePath $pwshPath `
        -Arguments $recorderArguments `
        -WorkingDirectory $repoRoot
    Register-TrackedProcess -ProcessIdentifier $recorderHandle.Process.Id

    $ffmpegProcessName = [System.IO.Path]::GetFileNameWithoutExtension($ffmpegPath)
    $recorderStartupDeadline = [System.DateTime]::UtcNow.AddSeconds($recorderStartupTimeoutSeconds)
    while ([System.DateTime]::UtcNow -lt $recorderStartupDeadline) {
        if ($recorderHandle.Process.HasExited) {
            throw "Recorder exited before capture became live (exit code $($recorderHandle.Process.ExitCode))."
        }
        if ($testHandle.Process.HasExited) {
            throw "Targeted FlaUI test exited before scenario-go.signal was written (exit code $($testHandle.Process.ExitCode))."
        }

        $recorderDescendants = @(
            Get-DescendantProcessIds -RootProcessIdentifier $recorderHandle.Process.Id
        )
        foreach ($descendantIdentifier in $recorderDescendants) {
            Register-TrackedProcess -ProcessIdentifier $descendantIdentifier
        }

        $liveFfmpegIdentifiers = @(
            foreach ($descendantIdentifier in $recorderDescendants) {
                try {
                    $descendantProcess = Get-Process -Id $descendantIdentifier -ErrorAction Stop
                    if ($descendantProcess.ProcessName -ieq $ffmpegProcessName -and
                        (Test-TrackedProcessIsAlive -ProcessIdentifier $descendantIdentifier)) {
                        $descendantIdentifier
                    }
                }
                catch {
                    # The descendant may exit during inspection.
                }
            }
        )

        if ($liveFfmpegIdentifiers.Count -ge 1 -and
            (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) -and
            (Get-Item -LiteralPath $resolvedOutputPath).Length -gt 0) {
            $ffmpegProcessIdentifiers = @($liveFfmpegIdentifiers | Sort-Object -Unique)
            break
        }

        Start-Sleep -Milliseconds 100
    }

    if ($ffmpegProcessIdentifiers.Count -eq 0) {
        throw "Recorder did not expose a live ffmpeg descendant and a nonempty output file within $recorderStartupTimeoutSeconds seconds."
    }

    Write-JsonSignal -Path $scenarioGoPath -Payload ([ordered]@{
        Phase = $Phase
        RunId = $runId
        RecorderProcessId = $recorderHandle.Process.Id
        FfmpegProcessIds = $ffmpegProcessIdentifiers
        RecorderDurationSeconds = $recorderDurationSeconds
        CreatedAtUtc = [System.DateTime]::UtcNow.ToString("O")
    })

    $scenarioDocument = Read-JsonFileWithRetry `
        -Path $scenarioCompletePath `
        -TimeoutSeconds $scenarioTimeoutSeconds `
        -HealthCheck {
            if ($recorderHandle.Process.HasExited) {
                throw "Recorder exited before scenario-complete.json (exit code $($recorderHandle.Process.ExitCode))."
            }
            $liveTrackedFfmpegIdentifiers = @(
                $ffmpegProcessIdentifiers |
                    Where-Object { Test-TrackedProcessIsAlive -ProcessIdentifier ([int]$_) }
            )
            if ($liveTrackedFfmpegIdentifiers.Count -eq 0) {
                throw "All tracked ffmpeg descendants exited before scenario-complete.json."
            }
            if ($testHandle.Process.HasExited) {
                throw "Targeted FlaUI test exited before scenario-complete.json (exit code $($testHandle.Process.ExitCode))."
            }
        }

    $flowCompletedProperty = Get-RequiredJsonProperty `
        -InputObject $scenarioDocument `
        -Name "FlowCompleted" `
        -DocumentName "scenario-complete.json"
    if ($flowCompletedProperty.Value -isnot [bool] -or -not [bool]$flowCompletedProperty.Value) {
        throw "scenario-complete.json must contain FlowCompleted=true."
    }

    $expectedFailureIds = if ($Phase -eq "Before") {
        $expectedBeforeFailureIds
    }
    else {
        @()
    }
    $failureIds = @(Assert-ExactFailureIds `
        -ScenarioDocument $scenarioDocument `
        -ExpectedFailureIds $expectedFailureIds)

    $recorderDeadline = [System.DateTime]::UtcNow.AddSeconds(
        $recorderDurationSeconds + $recorderExitGraceSeconds)
    while (-not $recorderHandle.Process.HasExited -and
        [System.DateTime]::UtcNow -lt $recorderDeadline) {
        if ($testHandle.Process.HasExited) {
            throw "Targeted FlaUI test exited before recording-finished.signal (exit code $($testHandle.Process.ExitCode))."
        }
        Start-Sleep -Milliseconds 100
    }

    if (-not $recorderHandle.Process.HasExited) {
        throw "Recorder did not exit normally within the expected duration."
    }
    $recorderHandle.Process.WaitForExit()
    Complete-CapturedOutput -Handle $recorderHandle
    $recorderOutput = "$($recorderHandle.StandardOutput)`n$($recorderHandle.StandardError)"
    if ($recorderHandle.Process.ExitCode -ne 0) {
        throw "Recorder exited with code $($recorderHandle.Process.ExitCode)."
    }

    $ffmpegExitDeadline = [System.DateTime]::UtcNow.AddSeconds(5)
    $liveTrackedFfmpegIdentifiers = @(
        $ffmpegProcessIdentifiers |
            Where-Object { Test-TrackedProcessIsAlive -ProcessIdentifier ([int]$_) }
    )
    while ($liveTrackedFfmpegIdentifiers.Count -gt 0 -and
        [System.DateTime]::UtcNow -lt $ffmpegExitDeadline) {
        Start-Sleep -Milliseconds 100
        $liveTrackedFfmpegIdentifiers = @(
            $ffmpegProcessIdentifiers |
                Where-Object { Test-TrackedProcessIsAlive -ProcessIdentifier ([int]$_) }
        )
    }
    if ($liveTrackedFfmpegIdentifiers.Count -gt 0) {
        throw "Tracked ffmpeg descendants remained alive after recorder exit: $($liveTrackedFfmpegIdentifiers -join ', ')"
    }

    if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) -or
        (Get-Item -LiteralPath $resolvedOutputPath).Length -le 0) {
        throw "Recorder completed without a nonempty output file: $resolvedOutputPath"
    }

    $probeArguments = @(
        "-v", "error",
        "-show_entries", "stream=index,codec_type,codec_name,width,height,r_frame_rate,avg_frame_rate,nb_frames",
        "-show_entries", "format=duration",
        "-of", "json",
        $resolvedOutputPath
    )
    $probeHandle = Start-CapturedProcess `
        -FilePath $ffprobePath `
        -Arguments $probeArguments `
        -WorkingDirectory $repoRoot
    Register-TrackedProcess -ProcessIdentifier $probeHandle.Process.Id
    if (-not (Wait-CapturedProcess -Handle $probeHandle -TimeoutSeconds 30)) {
        throw "ffprobe did not finish within 30 seconds."
    }
    Complete-CapturedOutput -Handle $probeHandle
    if ($probeHandle.Process.ExitCode -ne 0) {
        throw "ffprobe failed with code $($probeHandle.Process.ExitCode): $($probeHandle.StandardError)"
    }

    try {
        $probeDocument = $probeHandle.StandardOutput | ConvertFrom-Json -Depth 20 -ErrorAction Stop
    }
    catch {
        throw "ffprobe returned invalid JSON: $($_.Exception.Message)"
    }

    $videoStreams = @($probeDocument.streams | Where-Object { $_.codec_type -eq "video" })
    $audioStreams = @($probeDocument.streams | Where-Object { $_.codec_type -eq "audio" })
    if ($videoStreams.Count -ne 1) {
        throw "ffprobe must report exactly one video stream; found $($videoStreams.Count)."
    }
    if ($audioStreams.Count -ne 0) {
        throw "Status contract evidence must not contain audio streams; found $($audioStreams.Count)."
    }

    $videoStream = $videoStreams[0]
    $videoWidth = Convert-ToRequiredInt32 -Value $videoStream.width -FieldName "ffprobe.stream.width"
    $videoHeight = Convert-ToRequiredInt32 -Value $videoStream.height -FieldName "ffprobe.stream.height"
    if ($videoWidth -ne $expectedWidth -or $videoHeight -ne $expectedHeight) {
        throw "Recorded video geometry must be ${expectedWidth}x${expectedHeight}; actual ${videoWidth}x${videoHeight}."
    }

    $nominalFrameRate = Convert-FrameRateToDouble -FrameRate ([string]$videoStream.r_frame_rate)
    if ([System.Math]::Abs($nominalFrameRate - $expectedFps) -gt 0.01) {
        throw "Recorded video nominal frame rate must be $expectedFps fps; actual $nominalFrameRate."
    }

    $averageFrameRate = Convert-FrameRateToDouble -FrameRate ([string]$videoStream.avg_frame_rate)
    $minimumAverageFrameRate = $expectedFps * $minimumAverageFpsRatio
    if ($averageFrameRate -lt $minimumAverageFrameRate) {
        throw "Recorded video average frame rate must be at least $minimumAverageFrameRate fps ($($minimumAverageFpsRatio * 100)% of nominal); actual $averageFrameRate."
    }

    $durationSeconds = [double]::Parse(
        [string]$probeDocument.format.duration,
        [System.Globalization.CultureInfo]::InvariantCulture)
    if ($durationSeconds -le 0) {
        throw "Recorded video duration must be greater than zero; actual $durationSeconds."
    }

    $sha256 = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash
    Write-JsonSignal -Path $recordingFinishedPath -Payload ([ordered]@{
        Succeeded = $true
        Phase = $Phase
        RunId = $runId
        OutputPath = $resolvedOutputPath
        Sha256 = $sha256
        DurationSeconds = $durationSeconds
        Width = $videoWidth
        Height = $videoHeight
        FramesPerSecond = $nominalFrameRate
        AverageFramesPerSecond = $averageFrameRate
        CapturedFrames = Convert-ToRequiredInt32 -Value $videoStream.nb_frames -FieldName "ffprobe.stream.nb_frames"
        CompletedAtUtc = [System.DateTime]::UtcNow.ToString("O")
    })
    $recordingFinishedWritten = $true

    if (-not (Wait-CapturedProcess -Handle $testHandle -TimeoutSeconds $testExitTimeoutSeconds)) {
        throw "Targeted FlaUI test did not exit within $testExitTimeoutSeconds seconds after recording-finished.signal."
    }
    Complete-CapturedOutput -Handle $testHandle
    $testOutput = "$($testHandle.StandardOutput)`n$($testHandle.StandardError)"

    if ($Phase -eq "Before") {
        Assert-BeforeTestFailureContract `
            -TestOutput $testOutput `
            -ExitCode $testHandle.Process.ExitCode

        foreach ($expectedFailureId in $expectedBeforeFailureIds) {
            if ($testOutput.IndexOf($expectedFailureId, [System.StringComparison]::Ordinal) -lt 0) {
                throw "Before test output does not name expected aggregated assertion '$expectedFailureId'."
            }
        }
    }
    elseif ($testHandle.Process.ExitCode -ne 0) {
        throw "After phase targeted FlaUI test must exit with code 0; actual $($testHandle.Process.ExitCode)."
    }

    $result = [ordered]@{
        Phase = $Phase
        RunId = $runId
        OutputPath = $resolvedOutputPath
        Sha256 = $sha256
        DurationSeconds = $durationSeconds
        Width = $videoWidth
        Height = $videoHeight
        FramesPerSecond = $nominalFrameRate
        AverageFramesPerSecond = $averageFrameRate
        CapturedFrames = Convert-ToRequiredInt32 -Value $videoStream.nb_frames -FieldName "ffprobe.stream.nb_frames"
        TestExitCode = $testHandle.Process.ExitCode
        FailureIds = $failureIds
        WindowProcessId = $readyProcessIdentifier
        WindowTitle = $windowTitle
        RecorderDurationSeconds = $recorderDurationSeconds
        ScenarioTimeoutSeconds = $scenarioTimeoutSeconds
    }
}
catch {
    $caughtError = $_
}
finally {
    if (-not $recordingFinishedWritten -and
        (Test-Path -LiteralPath $handshakeDirectory -PathType Container)) {
        try {
            $errorMessage = if ($null -eq $caughtError) {
                "Status contract evidence orchestration did not complete."
            }
            else {
                $caughtError.Exception.Message
            }
            Write-JsonSignal -Path $recordingFinishedPath -Payload ([ordered]@{
                Succeeded = $false
                Phase = $Phase
                RunId = $runId
                Error = $errorMessage
                CompletedAtUtc = [System.DateTime]::UtcNow.ToString("O")
            })
            $recordingFinishedWritten = $true
        }
        catch {
            if ($null -eq $caughtError) {
                $caughtError = $_
            }
            else {
                Write-Warning "Failed to write recording-finished.signal: $($_.Exception.Message)"
            }
        }
    }

    foreach ($rootHandle in @($testHandle, $recorderHandle, $probeHandle)) {
        if ($null -eq $rootHandle) {
            continue
        }

        try {
            if (Test-TrackedProcessIsAlive -ProcessIdentifier $rootHandle.Process.Id) {
                Register-TrackedProcessTree -RootProcessIdentifier $rootHandle.Process.Id
            }
        }
        catch {
            Write-Verbose "Could not enumerate process tree rooted at $($rootHandle.Process.Id): $($_.Exception.Message)"
        }
    }
    if ($null -ne $readyProcessIdentifier) {
        Register-TrackedProcess -ProcessIdentifier $readyProcessIdentifier
    }
    foreach ($trackedFfmpegIdentifier in $ffmpegProcessIdentifiers) {
        Register-TrackedProcess -ProcessIdentifier ([int]$trackedFfmpegIdentifier)
    }

    try {
        Stop-TrackedProcesses
    }
    catch {
        if ($null -eq $caughtError) {
            $caughtError = $_
        }
        else {
            Write-Warning "Tracked process cleanup failed: $($_.Exception.Message)"
        }
    }

    foreach ($capturedHandle in @($testHandle, $recorderHandle, $probeHandle)) {
        if ($null -eq $capturedHandle) {
            continue
        }

        try {
            if ($capturedHandle.Process.HasExited) {
                Complete-CapturedOutput -Handle $capturedHandle
            }
        }
        catch {
            Write-Verbose "Could not finish captured output for process $($capturedHandle.Process.Id): $($_.Exception.Message)"
        }
    }

    if ($null -eq $testOutput -and $null -ne $testHandle) {
        $testOutput = "$($testHandle.StandardOutput)`n$($testHandle.StandardError)"
    }
    if ($null -eq $recorderOutput -and $null -ne $recorderHandle) {
        $recorderOutput = "$($recorderHandle.StandardOutput)`n$($recorderHandle.StandardError)"
    }

    if ($null -ne $caughtError) {
        if (-not [string]::IsNullOrWhiteSpace($testOutput)) {
            Write-Warning "Targeted test output tail:`n$(Get-OutputTail -Text $testOutput)"
        }
        if (-not [string]::IsNullOrWhiteSpace($recorderOutput)) {
            Write-Warning "Recorder output tail:`n$(Get-OutputTail -Text $recorderOutput)"
        }
    }
    else {
        Write-Verbose "Targeted test output:`n$testOutput"
        Write-Verbose "Recorder output:`n$recorderOutput"
    }

    try {
        Assert-SafeHandshakeDirectory -Path $handshakeDirectory
        if (Test-Path -LiteralPath $handshakeDirectory -PathType Container) {
            Remove-Item -LiteralPath $handshakeDirectory -Recurse -Force
        }
    }
    catch {
        if ($null -eq $caughtError) {
            $caughtError = $_
        }
        else {
            Write-Warning "Handshake directory cleanup failed: $($_.Exception.Message)"
        }
    }

    foreach ($capturedHandle in @($testHandle, $recorderHandle, $probeHandle)) {
        if ($null -ne $capturedHandle) {
            $capturedHandle.Process.Dispose()
        }
    }
}

if ($null -ne $caughtError) {
    throw $caughtError
}

$result | ConvertTo-Json -Depth 10
