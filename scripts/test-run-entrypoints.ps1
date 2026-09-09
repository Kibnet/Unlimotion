[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$windowsScript = Join-Path $repoRoot 'run.windows.cmd'
$linuxScript = Join-Path $repoRoot 'run.linux.sh'
$macScript = Join-Path $repoRoot 'run.macos.sh'
$expectedProjects = @{
    Windows = Join-Path $repoRoot 'src\Unlimotion.Desktop\Unlimotion.Desktop.csproj'
    Linux = Join-Path $repoRoot 'src\Unlimotion.Desktop\Unlimotion.Desktop.ForDebianBuild.csproj'
    MacOS = Join-Path $repoRoot 'src\Unlimotion.Desktop\Unlimotion.Desktop.ForMacBuild.csproj'
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [object]$Actual,
        [Parameter(Mandatory)] [object]$Expected,
        [Parameter(Mandatory)] [string]$Message
    )

    if ($Actual -cne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Sequence {
    param(
        [Parameter(Mandatory)] [string[]]$Actual,
        [Parameter(Mandatory)] [string[]]$Expected,
        [Parameter(Mandatory)] [string]$Message
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw "$Message Expected $($Expected.Count) arguments, got $($Actual.Count): $($Actual -join ' | ')"
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($Actual[$index] -cne $Expected[$index]) {
            throw "$Message Argument $index expected '$($Expected[$index])', got '$($Actual[$index])'."
        }
    }
}

function Resolve-GitBash {
    $candidates = @(
        'C:\Program Files\Git\bin\bash.exe',
        'C:\Program Files\Git\usr\bin\bash.exe',
        (Get-Command bash.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $versionText = (& $candidate --version 2>$null | Select-Object -First 1)
        if ($? -and $versionText -match 'GNU bash') {
            $cygpath = & $candidate -lc 'command -v cygpath'
            if ($? -and $cygpath) {
                return $candidate
            }
        }
    }

    throw 'Git Bash with cygpath is required to validate run.linux.sh and run.macos.sh.'
}

function Convert-ToGitBashPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Bash
    )

    $converted = & $Bash -lc 'cygpath -u -- "$1"' '_' $Path
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($converted)) {
        throw "Failed to convert '$Path' to a Git Bash path."
    }
    return $converted.Trim()
}

function Convert-FromGitBashPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Bash
    )

    $converted = & $Bash -lc 'cygpath -w -- "$1"' '_' $Path
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($converted)) {
        throw "Failed to convert Git Bash path '$Path' to a Windows path."
    }
    return [IO.Path]::GetFullPath($converted.Trim())
}

function Read-ArgumentLog {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Fake dotnet did not write its argument log: $Path"
    }
    return @(Get-Content -LiteralPath $Path -Encoding utf8)
}

if (-not $IsWindows) {
    throw 'The root-entrypoint regression must run on Windows so run.windows.cmd and both Bash entrypoints are exercised together.'
}

$bash = Resolve-GitBash
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("Unlimotion entrypoint regression {0}" -f [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $tempRoot 'fake bin'
$unrelatedCwd = Join-Path $tempRoot 'unrelated cwd'
$windowsLog = Join-Path $tempRoot 'windows-arguments.txt'
$bashLog = Join-Path $tempRoot 'bash-arguments.txt'
$windowsCwdLog = Join-Path $tempRoot 'windows-cwd.txt'
$bashCwdLog = Join-Path $tempRoot 'bash-cwd.txt'
$fakePowerShell = Join-Path $tempRoot 'fake-dotnet.ps1'
$fakeCmd = Join-Path $fakeBin 'dotnet.cmd'
$fakeBash = Join-Path $fakeBin 'dotnet'
$originalPath = $env:PATH
$originalLog = $env:FAKE_DOTNET_LOG
$originalExit = $env:FAKE_DOTNET_EXIT
$originalHelper = $env:FAKE_DOTNET_HELPER
$originalCwdLog = $env:FAKE_DOTNET_CWD_LOG

try {
    New-Item -ItemType Directory -Force -Path $fakeBin, $unrelatedCwd | Out-Null

    @'
[IO.File]::WriteAllLines($env:FAKE_DOTNET_LOG, [string[]]$args)
[IO.File]::WriteAllText($env:FAKE_DOTNET_CWD_LOG, (Get-Location).Path)
exit [int]$env:FAKE_DOTNET_EXIT
'@ | Set-Content -LiteralPath $fakePowerShell -Encoding utf8NoBOM

    @'
@echo off
pwsh.exe -NoProfile -File "%FAKE_DOTNET_HELPER%" %*
exit /b %FAKE_DOTNET_EXIT%
'@ | Set-Content -LiteralPath $fakeCmd -Encoding ascii

    @'
#!/usr/bin/env bash
set -euo pipefail
: > "$FAKE_DOTNET_LOG"
for argument in "$@"; do
  printf '%s\n' "$argument" >> "$FAKE_DOTNET_LOG"
done
pwd -P > "$FAKE_DOTNET_CWD_LOG"
exit "$FAKE_DOTNET_EXIT"
'@ | Set-Content -LiteralPath $fakeBash -Encoding utf8NoBOM

    $fakeBashPath = Convert-ToGitBashPath -Path $fakeBash -Bash $bash
    & $bash -lc 'chmod 0755 -- "$1"' '_' $fakeBashPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to make the fake Bash dotnet executable.'
    }

    $arguments = @(
        '--config',
        'C:\Config Folder\settings.json',
        '--flag',
        'value with spaces',
        '--literal=*'
    )

    $env:PATH = "$fakeBin$([IO.Path]::PathSeparator)$originalPath"
    $env:FAKE_DOTNET_HELPER = $fakePowerShell
    $env:FAKE_DOTNET_EXIT = '0'
    $env:FAKE_DOTNET_LOG = $windowsLog
    $env:FAKE_DOTNET_CWD_LOG = $windowsCwdLog

    Push-Location $unrelatedCwd
    try {
        & $windowsScript @arguments
        Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'run.windows.cmd success exit code mismatch.'
    }
    finally {
        Pop-Location
    }

    $windowsActual = Read-ArgumentLog -Path $windowsLog
    $windowsExpected = @('run', '--project', [IO.Path]::GetFullPath($expectedProjects.Windows), '--') + $arguments
    Assert-Sequence -Actual $windowsActual -Expected $windowsExpected -Message 'run.windows.cmd argv mismatch.'
    $windowsActualCwd = (Get-Content -LiteralPath $windowsCwdLog -Raw -Encoding utf8).Trim()
    Assert-Equal -Actual ([IO.Path]::GetFullPath($windowsActualCwd)) -Expected ([IO.Path]::GetFullPath($repoRoot)) -Message 'run.windows.cmd process CWD mismatch.'

    $env:FAKE_DOTNET_EXIT = '37'
    Push-Location $unrelatedCwd
    try {
        & $windowsScript @arguments *> $null
        Assert-Equal -Actual $LASTEXITCODE -Expected 37 -Message 'run.windows.cmd did not preserve the dotnet exit code.'
    }
    finally {
        Pop-Location
    }

    $bashFakeBin = Convert-ToGitBashPath -Path $fakeBin -Bash $bash
    $bashLogPath = Convert-ToGitBashPath -Path $bashLog -Bash $bash
    $bashCwdLogPath = Convert-ToGitBashPath -Path $bashCwdLog -Bash $bash
    $bashCwd = Convert-ToGitBashPath -Path $unrelatedCwd -Bash $bash

    foreach ($case in @(
        @{ Name = 'Linux'; Script = $linuxScript },
        @{ Name = 'MacOS'; Script = $macScript }
    )) {
        $scriptPath = Convert-ToGitBashPath -Path $case.Script -Bash $bash
        $env:FAKE_DOTNET_LOG = $bashLogPath
        $env:FAKE_DOTNET_CWD_LOG = $bashCwdLogPath
        $env:FAKE_DOTNET_EXIT = '0'
        Remove-Item -LiteralPath $bashLog -Force -ErrorAction SilentlyContinue

        & $bash -lc 'cd "$1" && PATH="$2:$PATH" bash "$3" "${@:4}"' '_' $bashCwd $bashFakeBin $scriptPath @arguments
        Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message "run.$($case.Name) success exit code mismatch."

        $bashActual = Read-ArgumentLog -Path $bashLog
        Assert-Equal -Actual $bashActual[0] -Expected 'run' -Message "run.$($case.Name) command mismatch."
        Assert-Equal -Actual $bashActual[1] -Expected '--project' -Message "run.$($case.Name) project switch mismatch."
        $actualProject = Convert-FromGitBashPath -Path $bashActual[2] -Bash $bash
        Assert-Equal -Actual $actualProject -Expected ([IO.Path]::GetFullPath($expectedProjects[$case.Name])) -Message "run.$($case.Name) project path mismatch."
        Assert-Sequence -Actual $bashActual[3..($bashActual.Count - 1)] -Expected (@('--') + $arguments) -Message "run.$($case.Name) forwarded argv mismatch."
        $actualBashCwd = (Get-Content -LiteralPath $bashCwdLog -Raw -Encoding utf8).Trim()
        $actualBashCwdWindows = Convert-FromGitBashPath -Path $actualBashCwd -Bash $bash
        Assert-Equal -Actual $actualBashCwdWindows -Expected ([IO.Path]::GetFullPath($repoRoot)) -Message "run.$($case.Name) process CWD mismatch."

        $env:FAKE_DOTNET_EXIT = '37'
        & $bash -lc 'cd "$1" && PATH="$2:$PATH" bash "$3" "${@:4}"' '_' $bashCwd $bashFakeBin $scriptPath @arguments *> $null
        Assert-Equal -Actual $LASTEXITCODE -Expected 37 -Message "run.$($case.Name) did not preserve the dotnet exit code."
    }

    foreach ($shellScript in @($linuxScript, $macScript)) {
        $content = Get-Content -LiteralPath $shellScript -Raw -Encoding utf8
        if (-not $content.StartsWith("#!/usr/bin/env bash`n", [StringComparison]::Ordinal)) {
            throw "$shellScript must start with the portable Bash shebang and LF."
        }
        if ($content.Contains("`r")) {
            throw "$shellScript must use LF line endings."
        }
        if ($content -notmatch '(?m)^set -euo pipefail$') {
            throw "$shellScript must enable strict Bash mode."
        }
    }

    Write-Output 'Root entrypoint regression passed for Windows, Linux and macOS scripts.'
}
finally {
    $env:PATH = $originalPath
    $env:FAKE_DOTNET_LOG = $originalLog
    $env:FAKE_DOTNET_EXIT = $originalExit
    $env:FAKE_DOTNET_HELPER = $originalHelper
    $env:FAKE_DOTNET_CWD_LOG = $originalCwdLog
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
